/**
 * Babylon Toolkit content service worker (PWA Content Installer).
 *
 * The worker is the only network gateway for files listed in a content manifest (plan decision D14): every request for a
 * listed file comes through here, is answered from Cache Storage when its hash is stored, and otherwise is downloaded once
 * per hash, verified against the manifest's SHA-256 and stored while the page reads the same bytes. The page-side
 * `TOOLKIT.ContentInstaller` decides what to install and when; this worker never decides what to install (D3), never
 * downloads in its `install` event, and only knows Cache Storage, the protocol messages and the manifests.
 *
 * Storage (D15): `bt-content-files-v1` holds decoded file bytes keyed by hash; `bt-content-state-v1` holds the roots index,
 * root states and manifests (written by the leader page) and the client pins (written here). A page keeps the version it
 * first requested (D16 pins); `collect-garbage` deletes manifests, pins and files that no kept version needs.
 *
 * Plain modern JavaScript with no imports and no build step: hosts serve this file as it is.
 */

/** Cache holding stored files by hash. */
const CONTENT_FILES_CACHE = "bt-content-files-v1";
/** Cache holding manifests, root states, the roots index and client pins. */
const CONTENT_STATE_CACHE = "bt-content-state-v1";
/** Path prefix of every synthetic store key on this origin. */
const STORE_KEY_PREFIX = "/__bt-content__/";
/** Query parameter marking an installer download; its value is the manifest version to resolve against. */
const INSTALL_MARKER_PARAMETER = "bt-content-install";
/** Query parameter carrying the installer's attempt number (0 first); above 0 the worker bypasses the HTTP cache. */
const ATTEMPT_PARAMETER = "bt-content-attempt";
/** Broadcast channel shared by the worker and every page. */
const BROADCAST_CHANNEL_NAME = "bt-content-installer";

/** Store-key folder of file entries (`files/<hash>`). */
const FILES_FOLDER = "files/";
/** Store-key folder of root states (`roots/<encodeURIComponent(rootKey)>`). */
const ROOTS_FOLDER = "roots/";
/** Store-key folder of manifests (`manifests/<encodeURIComponent(rootKey)>/<version>`). */
const MANIFESTS_FOLDER = "manifests/";
/** Store-key folder of client pins (`pins/<encodeURIComponent(clientId)>`). */
const PINS_FOLDER = "pins/";
/** Store key name of the roots index (a JSON array of root keys). */
const ROOTS_INDEX_NAME = "roots-index";
/** Install policy under which pass-through requests are not stored. */
const INSTALL_POLICY_OFF = "off";
/** Matches one byte range: `bytes=start-end`, `bytes=start-` or `bytes=-suffix`. */
const SINGLE_RANGE_PATTERN = /^bytes=(\d*)-(\d*)$/;
/** HTTP status of a full answer from the store. */
const STATUS_OK = 200;
/** HTTP status of a ranged answer from the store. */
const STATUS_PARTIAL_CONTENT = 206;
/** HTTP status of the empty answer to an installer download the worker could not store (see `fallbackResponse`). */
const STATUS_NO_CONTENT = 204;
/** Header of that empty answer, naming it as a download that was not stored. */
const NOT_STORED_HEADER = "X-Bt-Content-Not-Stored";
/** HTTP status of a Range header the stored file cannot satisfy. */
const STATUS_RANGE_NOT_SATISFIABLE = 416;

/** Root states by root key (the absolute manifest URL), read from the roots index; reloaded on `roots-changed`. */
const rootsByKey = new Map();
/** Parsed manifests by `rootKey + "\n" + version`. */
const manifestsByKey = new Map();
/** Pins by client id: root key → the version that client keeps (D16). */
const pinsByClient = new Map();
/** One promise per hash being downloaded and stored; resolves true when the file was stored. */
const storesInFlight = new Map();
/** Hashes known to be in the files cache, so a request can be passed through without waiting for a store read. */
const storedHashes = new Set();
/** The worker → pages broadcast channel. */
const contentChannel = new BroadcastChannel(BROADCAST_CHANNEL_NAME);

/** Resolves once roots, pins and stored hashes have been read from the store; null until the first read starts. */
let stateReady = null;
/** True once `stateReady` has resolved, so fetch events can decide synchronously whether to answer. */
let isStateLoaded = false;
/** Tail of the serial message queue: protocol messages run one after another in arrival order. */
let messageQueue = Promise.resolve();

self.addEventListener("install", handleInstall);
self.addEventListener("activate", handleActivate);
self.addEventListener("message", handleMessage);
self.addEventListener("fetch", handleFetch);

// ---------------------------------------------------------------------------------------------------------------------
// Lifecycle (D17)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Skips waiting on a first visit only, so the first page is controlled at once (FR-3); an updated worker waits until a
 * page posts `skip-waiting`, so it never activates over open pages unasked. Nothing is downloaded here (D3).
 * @param {ExtendableEvent} event The install event.
 */
function handleInstall(event) {
    if (!self.registration.active) event.waitUntil(self.skipWaiting());
}

/**
 * Takes control of every open page of the scope, so the first visit is served by this worker without a reload.
 * @param {ExtendableEvent} event The activate event.
 */
function handleActivate(event) {
    event.waitUntil(self.clients.claim());
}

// ---------------------------------------------------------------------------------------------------------------------
// Messages (page → worker)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Runs one protocol message: `pin`, `roots-changed`, `collect-garbage` or `skip-waiting`. Store-changing messages run
 * serially in arrival order, so a `collect-garbage` sent after `roots-changed` sees the new roots.
 * @param {ExtendableMessageEvent} event The message event; `event.source.id` is the sending client.
 */
function handleMessage(event) {
    const message = event.data;
    if (message == null || typeof message.type !== "string") return;
    if (message.type === "skip-waiting") {
        event.waitUntil(self.skipWaiting());
        return;
    }
    const clientId = event.source != null && typeof event.source.id === "string" ? event.source.id : "";
    const task = messageQueue.then(() => runQueuedMessage(message, clientId));
    messageQueue = task.catch((error) => console.warn("[bt-content-worker] The " + message.type + " message failed: " + describeError(error)));
    event.waitUntil(messageQueue);
}

/**
 * Applies one queued store-changing message.
 * @param {{ type: string, rootKey?: string, version?: string }} message The protocol message.
 * @param {string} clientId The sending client's id ("" when unknown).
 * @returns {Promise<void>} Settles when the message has been applied.
 */
async function runQueuedMessage(message, clientId) {
    if (message.type === "pin") {
        if (clientId === "" || typeof message.rootKey !== "string" || typeof message.version !== "string") return;
        await loadState();
        await writePin(clientId, message.rootKey, message.version);
        return;
    }
    if (message.type === "roots-changed") {
        await loadState();
        await loadRoots();
        return;
    }
    if (message.type === "collect-garbage") await collectGarbage();
}

// ---------------------------------------------------------------------------------------------------------------------
// Fetch
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Routes a fetch: navigations to `handleNavigation`, listed GET requests to the store or the gateway download. A request
 * the worker already knows it will not handle (unlisted, or ranged / unstored-by-policy and not stored) gets no
 * `respondWith`, so the browser handles it untouched. `respondWith` must be called while the event is dispatched, so
 * that decision is made from memory; while the state is still loading every request goes through the asynchronous path,
 * which ends in `fetch(event.request)` for anything it does not serve.
 * @param {FetchEvent} event The fetch event.
 */
function handleFetch(event) {
    const request = event.request;
    if (request.method !== "GET") return;
    if (request.mode === "navigate") {
        handleNavigation(event);
        return;
    }
    if (isKnownPassThrough(request, event.clientId)) return;
    const outcome = respondToContentRequest(event);
    event.respondWith(outcome.then((result) => result.response));
    event.waitUntil(outcome.then((result) => result.storeSettled));
}

/**
 * Answers a request that may be listed: from the store, by joining a download already in flight for its hash, or by a
 * gateway download. Any failure to read the store falls back to `fetch(event.request)`; it never produces an error page.
 * A page request of a root the installer set to network-only (content-manifest-always-current P7: the server's files no
 * longer match the manifest this page uses) is normally never answered (see `isKnownPassThrough`); one that arrives while
 * the state is still loading is answered by `fetchNetworkOnly`, and nothing is stored. Installer downloads still go
 * through the gateway, so an update can download.
 * @param {FetchEvent} event The fetch event.
 * @returns {Promise<{ response: Response | Promise<Response>, storeSettled: Promise<boolean> | null }>} The answer and the
 *   store work the event must outlive.
 */
async function respondToContentRequest(event) {
    let resolved = null;
    try {
        resolved = await resolveRequest(event.request, event.clientId);
    } catch (error) {
        return { response: fetch(event.request), storeSettled: null };
    }
    if (resolved === null) return { response: fetch(event.request), storeSettled: null };
    if (resolved.root.networkOnly != null && !resolved.isInstall) return { response: fetchNetworkOnly(event, resolved), storeSettled: null };
    try {
        const storedResponse = await serveStored(event.request, resolved.entry, resolved.entry.hash);
        if (storedResponse !== null) {
            // The installer waits for a stored message per file; repeat it when its download is answered from the store.
            if (resolved.isInstall) broadcast(createStoredMessage(resolved));
            return { response: storedResponse, storeSettled: null };
        }
    } catch (error) {
        return { response: fallbackResponse(event, resolved), storeSettled: null };
    }
    return respondThroughGateway(event, resolved);
}

/**
 * Answers a listed request whose file is not stored: joins the download in flight for its hash, passes a ranged or
 * not-to-be-stored request through, or starts the gateway download.
 * @param {FetchEvent} event The fetch event.
 * @param {ResolvedRequest} resolved The resolved request.
 * @returns {Promise<{ response: Response | Promise<Response>, storeSettled: Promise<boolean> | null }>} The answer.
 */
async function respondThroughGateway(event, resolved) {
    const storeInFlight = storesInFlight.get(resolved.entry.hash);
    if (storeInFlight !== undefined) return { response: serveAfterStore(event, resolved, storeInFlight), storeSettled: null };
    if (isRangeRequest(event.request) || !shouldStore(resolved)) return { response: fetch(event.request), storeSettled: null };
    return downloadAndStore(resolved, event);
}

/**
 * Waits for another request's download of the same hash, then serves the stored file; when that download failed, the
 * request goes to the network itself.
 * @param {FetchEvent} event The fetch event.
 * @param {ResolvedRequest} resolved The resolved request.
 * @param {Promise<boolean>} storeInFlight The in-flight store of the same hash.
 * @returns {Promise<Response>} The answer.
 */
async function serveAfterStore(event, resolved, storeInFlight) {
    const wasStored = await storeInFlight;
    if (!wasStored) return fallbackResponse(event, resolved);
    try {
        const storedResponse = await serveStored(event.request, resolved.entry, resolved.entry.hash);
        if (storedResponse !== null) return storedResponse;
    } catch (error) {
        // A store read that throws falls through to the network, exactly like a miss.
    }
    return fallbackResponse(event, resolved);
}

/**
 * The answer when the worker cannot serve a listed request itself. A scene request goes to the network unchanged. An
 * installer download is not sent again (its marker parameters must never reach the server; the installer retries with a
 * fresh attempt and learns the outcome from `file-failed`), so it gets an empty 204 marked `X-Bt-Content-Not-Stored`.
 * Not `Response.error()`: Chrome logs every network-error answer as a console error ("Failed to load resource"), which
 * would add one console error per file to the single warning the installer reports (Error policy: one `cors` warning).
 * @param {FetchEvent} event The fetch event.
 * @param {ResolvedRequest} resolved The resolved request.
 * @returns {Response | Promise<Response>} The fallback answer.
 */
function fallbackResponse(event, resolved) {
    if (!resolved.isInstall) return fetch(event.request);
    return new Response(null, { status: STATUS_NO_CONTENT, headers: { [NOT_STORED_HEADER]: "true" } });
}

/**
 * Answers a page request of a network-only root (P7) from the network without storing it. It fetches like the gateway
 * download (`cors`, no credentials) and answers with a copy, so a cross-origin `no-cors` request never gets an opaque
 * response, which a page with `Cross-Origin-Embedder-Policy` would reject. A host without CORS headers gets the page's
 * own request replayed, as `fallbackResponse` does; so does a ranged request, whose Range header only that replay keeps.
 * @param {FetchEvent} event The fetch event.
 * @param {ResolvedRequest} resolved The resolved page request.
 * @returns {Promise<Response>} The network answer.
 */
async function fetchNetworkOnly(event, resolved) {
    if (isRangeRequest(event.request)) return fetch(event.request);
    let networkResponse = null;
    try {
        networkResponse = await fetch(resolved.networkUrl, { mode: "cors", credentials: "omit" });
    } catch (error) {
        return fetch(event.request);
    }
    if (networkResponse.body === null) return networkResponse;
    return buildPageResponse(networkResponse, networkResponse.body);
}

/**
 * Whether a listed file may be stored for this request: installer downloads always are (D14); scene requests unless
 * the root's install policy is `off`.
 * @param {ResolvedRequest} resolved The resolved request.
 * @returns {boolean} True when the downloaded bytes are verified and stored.
 */
function shouldStore(resolved) {
    return resolved.isInstall || resolved.root.installPolicy !== INSTALL_POLICY_OFF;
}

/**
 * Whether the request carries a Range header.
 * @param {Request} request The request.
 * @returns {boolean} True for a ranged request.
 */
function isRangeRequest(request) {
    return request.headers.has("range");
}

/**
 * Decides from memory, while the fetch event is dispatched, that the worker will not answer a request: it is outside
 * every root, it is a page request of a network-only root (content-manifest-always-current P7), it is not listed in the
 * version the client would get, or it is not stored and is either ranged or not to be stored. Returns false whenever
 * memory cannot tell (state not loaded yet, manifest not in memory).
 * A network-only page request gets no `respondWith`, so the browser fetches it itself under the page's own rules: a
 * worker replay of a cross-origin `no-cors` request returns an opaque response that a page with
 * `Cross-Origin-Embedder-Policy` rejects (`net::ERR_FAILED`).
 * @param {Request} request The request.
 * @param {string} clientId The requesting client ("" when none).
 * @returns {boolean} True when the request must get no `respondWith`.
 */
function isKnownPassThrough(request, clientId) {
    if (!isStateLoaded) return false;
    const parsed = parseContentUrl(request.url);
    const root = findRoot(parsed.lookupUrl);
    if (root !== null && root.networkOnly != null && parsed.installVersion === null) return true;
    const resolved = resolveFromMemory(parsed, clientId);
    if (resolved === undefined) return false;
    if (resolved === null) return true;
    const hash = resolved.entry.hash;
    if (storedHashes.has(hash) || storesInFlight.has(hash)) return false;
    return isRangeRequest(request) || !shouldStore(resolved);
}

// ---------------------------------------------------------------------------------------------------------------------
// Request resolution (Algorithms › Worker request resolution)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * @typedef {object} ParsedContentUrl
 * @property {string} lookupUrl The URL without query and hash, matched against root base URLs.
 * @property {string} networkUrl The URL with the two marker parameters removed (other parameters kept): the request the
 *   network sees, identical to the scene's own request.
 * @property {string | null} installVersion The install marker's version, or null for a scene request.
 * @property {number} attempt The installer attempt number (0 when absent).
 */

/**
 * @typedef {object} ResolvedRequest
 * @property {object} root The root state the URL belongs to.
 * @property {string} version The manifest version the request is served from.
 * @property {string} path The file's manifest path (relative to the root's base URL, decoded).
 * @property {{ size: number, contentSize: number, hash: string, type: string, encoding?: string }} entry The manifest entry.
 * @property {boolean} isInstall True for an installer download.
 * @property {number} attempt The installer attempt number (0 for a scene request).
 * @property {string} networkUrl The URL to download from.
 */

/**
 * Splits a request URL into the parts resolution needs.
 * @param {string} requestUrl The request's absolute URL.
 * @returns {ParsedContentUrl} The parsed URL.
 */
function parseContentUrl(requestUrl) {
    const url = new URL(requestUrl);
    const installVersion = url.searchParams.get(INSTALL_MARKER_PARAMETER);
    const attemptText = url.searchParams.get(ATTEMPT_PARAMETER);
    const attemptNumber = attemptText === null ? 0 : Number.parseInt(attemptText, 10);
    url.searchParams.delete(INSTALL_MARKER_PARAMETER);
    url.searchParams.delete(ATTEMPT_PARAMETER);
    url.hash = "";
    const networkUrl = url.href;
    url.search = "";
    return {
        lookupUrl: url.href,
        networkUrl: networkUrl,
        installVersion: installVersion,
        attempt: Number.isFinite(attemptNumber) && attemptNumber > 0 ? attemptNumber : 0
    };
}

/**
 * Resolves a request against the stored roots and manifests, loading what is not in memory and writing the client's pin
 * when it has none for the root (D16).
 * @param {Request} request The request.
 * @param {string} clientId The requesting client ("" when none).
 * @returns {Promise<ResolvedRequest | null>} The resolution, or null when the URL is not a listed file.
 * @throws {Error} When the store cannot be read.
 */
async function resolveRequest(request, clientId) {
    await loadState();
    const parsed = parseContentUrl(request.url);
    const root = findRoot(parsed.lookupUrl);
    if (root === null) return null;
    const version = parsed.installVersion !== null ? parsed.installVersion : await versionForClient(clientId, root);
    if (version === null) return null;
    const manifest = await loadManifest(root.rootKey, version);
    if (manifest === null) return null;
    return buildResolution(parsed, root, version, manifest);
}

/**
 * The synchronous twin of `resolveRequest`: resolves from memory only and writes nothing.
 * @param {ParsedContentUrl} parsed The parsed request URL.
 * @param {string} clientId The requesting client ("" when none).
 * @returns {ResolvedRequest | null | undefined} The resolution, null when not listed, undefined when memory cannot tell.
 */
function resolveFromMemory(parsed, clientId) {
    const root = findRoot(parsed.lookupUrl);
    if (root === null) return null;
    const version = parsed.installVersion !== null ? parsed.installVersion : pinnedOrActiveVersion(clientId, root);
    if (version === null) return null;
    const manifest = manifestsByKey.get(manifestMapKey(root.rootKey, version));
    if (manifest === undefined) return undefined;
    return buildResolution(parsed, root, version, manifest);
}

/**
 * Looks the URL's path up in a manifest.
 * @param {ParsedContentUrl} parsed The parsed request URL.
 * @param {object} root The root state whose base URL prefixes the URL.
 * @param {string} version The manifest version.
 * @param {{ files: Object<string, object> }} manifest The manifest.
 * @returns {ResolvedRequest | null} The resolution, or null when the path is not listed.
 */
function buildResolution(parsed, root, version, manifest) {
    const path = relativePathOf(parsed.lookupUrl, root.baseUrl);
    if (path === null || manifest.files == null || !Object.hasOwn(manifest.files, path)) return null;
    return {
        root: root,
        version: version,
        path: path,
        entry: manifest.files[path],
        isInstall: parsed.installVersion !== null,
        attempt: parsed.attempt,
        networkUrl: parsed.networkUrl
    };
}

/**
 * The root whose base URL is the longest prefix of the URL, so a content root inside the app-shell root wins.
 * @param {string} lookupUrl The URL without query and hash.
 * @returns {object | null} The root state, or null when no root contains the URL.
 */
function findRoot(lookupUrl) {
    let bestRoot = null;
    for (const root of rootsByKey.values()) {
        if (typeof root.baseUrl !== "string" || !lookupUrl.startsWith(root.baseUrl)) continue;
        if (bestRoot === null || root.baseUrl.length > bestRoot.baseUrl.length) bestRoot = root;
    }
    return bestRoot;
}

/**
 * The decoded manifest path of a URL inside a root.
 * @param {string} lookupUrl The URL without query and hash.
 * @param {string} baseUrl The root's base URL (ends with "/").
 * @returns {string | null} The decoded path, or null when the URL is malformed percent-encoding.
 */
function relativePathOf(lookupUrl, baseUrl) {
    try {
        return decodeURIComponent(lookupUrl.slice(baseUrl.length));
    } catch (error) {
        return null;
    }
}

/**
 * The version a client is served for a root: its pin, else the root's active version, which is then pinned (D16).
 * @param {string} clientId The requesting client ("" when none: nothing is pinned).
 * @param {object} root The root state.
 * @returns {Promise<string | null>} The version, or null when the root has no active version.
 */
async function versionForClient(clientId, root) {
    const pins = await readPin(clientId);
    if (typeof pins[root.rootKey] === "string") return pins[root.rootKey];
    const activeVersion = typeof root.activeVersion === "string" ? root.activeVersion : null;
    if (clientId !== "" && activeVersion !== null) await writePin(clientId, root.rootKey, activeVersion);
    return activeVersion;
}

/**
 * The version a client would be served for a root, from memory, without pinning.
 * @param {string} clientId The requesting client ("" when none).
 * @param {object} root The root state.
 * @returns {string | null} The pinned version, else the active version, else null.
 */
function pinnedOrActiveVersion(clientId, root) {
    const pins = pinsByClient.get(clientId);
    if (pins !== undefined && typeof pins[root.rootKey] === "string") return pins[root.rootKey];
    return typeof root.activeVersion === "string" ? root.activeVersion : null;
}

// ---------------------------------------------------------------------------------------------------------------------
// Serving from the store
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Serves a stored file, whole or ranged.
 * @param {Request} request The request (its Range header decides the shape of the answer).
 * @param {{ type: string }} entry The manifest entry (its `type` becomes `Content-Type`).
 * @param {string} hash The file's hash.
 * @returns {Promise<Response | null>} The answer, or null when the hash is not stored.
 * @throws {Error} When the store cannot be read.
 */
async function serveStored(request, entry, hash) {
    const filesCache = await caches.open(CONTENT_FILES_CACHE);
    const storedResponse = await filesCache.match(fileKey(hash));
    if (storedResponse === undefined) {
        storedHashes.delete(hash);
        return null;
    }
    const rangeHeader = request.headers.get("range");
    if (rangeHeader === null) {
        const headers = new Headers({ "Content-Type": entry.type, "Accept-Ranges": "bytes" });
        const storedLength = storedResponse.headers.get("Content-Length");
        if (storedLength !== null) headers.set("Content-Length", storedLength);
        return new Response(storedResponse.body, { status: STATUS_OK, headers: headers });
    }
    return serveStoredRange(new Uint8Array(await storedResponse.arrayBuffer()), rangeHeader, entry.type);
}

/**
 * Answers a Range request from stored bytes: 206 with the slice, or 416 when the header is not one satisfiable range.
 * @param {Uint8Array} bytes The whole stored file.
 * @param {string} rangeHeader The request's Range header.
 * @param {string} contentType The manifest's content type.
 * @returns {Response} The ranged answer.
 */
function serveStoredRange(bytes, rangeHeader, contentType) {
    const total = bytes.byteLength;
    const range = parseRange(rangeHeader, total);
    if (range === null) {
        return new Response(null, { status: STATUS_RANGE_NOT_SATISFIABLE, headers: { "Content-Range": "bytes */" + total } });
    }
    const slice = bytes.slice(range.start, range.end + 1);
    const headers = {
        "Content-Type": contentType,
        "Content-Range": "bytes " + range.start + "-" + range.end + "/" + total,
        "Content-Length": String(slice.byteLength),
        "Accept-Ranges": "bytes"
    };
    return new Response(slice, { status: STATUS_PARTIAL_CONTENT, headers: headers });
}

/**
 * Parses a single byte range (`bytes=start-end`, `bytes=start-`, `bytes=-suffix`) against a file length. Several ranges,
 * other units and ranges outside the file are unsatisfiable.
 * @param {string} header The Range header.
 * @param {number} total The file length in bytes.
 * @returns {{ start: number, end: number } | null} The inclusive byte range clamped to the file, or null when unsatisfiable.
 */
function parseRange(header, total) {
    const match = SINGLE_RANGE_PATTERN.exec(header.trim());
    if (match === null || total === 0) return null;
    const startText = match[1];
    const endText = match[2];
    if (startText === "" && endText === "") return null;
    if (startText === "") {
        const suffixLength = Number(endText);
        if (suffixLength === 0) return null;
        return { start: Math.max(0, total - suffixLength), end: total - 1 };
    }
    const start = Number(startText);
    if (start >= total) return null;
    const end = endText === "" ? total - 1 : Math.min(Number(endText), total - 1);
    if (end < start) return null;
    return { start: start, end: end };
}

// ---------------------------------------------------------------------------------------------------------------------
// Gateway download (D14)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Downloads a listed file once for its hash, hands the page its bytes as they arrive and, in parallel, verifies and
 * stores them. The in-flight entry is registered before the first await, so a concurrent request for the same hash joins
 * this download instead of starting another. Failed, opaque and mismatched responses are never stored.
 * @param {ResolvedRequest} resolved The resolved request.
 * @param {FetchEvent} event The fetch event.
 * @returns {Promise<{ response: Response | Promise<Response>, storeSettled: Promise<boolean> }>} The page's answer and the
 *   store outcome (true when stored).
 */
async function downloadAndStore(resolved, event) {
    const hash = resolved.entry.hash;
    let settleStore = null;
    const storeSettled = new Promise((resolve) => { settleStore = resolve; });
    storesInFlight.set(hash, storeSettled);
    storeSettled.then(() => storesInFlight.delete(hash));

    let networkResponse = null;
    try {
        networkResponse = await fetch(resolved.networkUrl, { mode: "cors", credentials: "omit", cache: resolved.attempt > 0 ? "reload" : "default" });
    } catch (error) {
        const reason = await diagnoseFetchFailure(resolved.networkUrl, error);
        broadcast(createFailedMessage(resolved, reason, null));
        settleStore(false);
        return { response: fallbackResponse(event, resolved), storeSettled: storeSettled };
    }
    // `ok` is false for an opaque response too (status 0), so neither a failed nor an opaque response is stored.
    if (!networkResponse.ok || networkResponse.body === null) {
        broadcast(createFailedMessage(resolved, "status", networkResponse.status));
        settleStore(false);
        return { response: networkResponse, storeSettled: storeSettled };
    }
    const [pageBody, storeBody] = networkResponse.body.tee();
    verifyAndStore(storeBody, resolved).then(settleStore);
    return { response: buildPageResponse(networkResponse, pageBody), storeSettled: storeSettled };
}

/**
 * Tells a CORS failure from a network failure after the CORS fetch threw: a cross-origin `no-cors` probe that succeeds
 * means the host answers but sends no CORS headers. A same-origin failure is always a network failure.
 * @param {string} networkUrl The URL that failed.
 * @param {Error} error The error the CORS fetch threw.
 * @returns {Promise<"cors" | "network">} The failure reason.
 */
async function diagnoseFetchFailure(networkUrl, error) {
    if (error == null || error.name !== "TypeError") return "network";
    if (new URL(networkUrl).origin === self.location.origin) return "network";
    try {
        await fetch(networkUrl, { mode: "no-cors", credentials: "omit" });
        return "cors";
    } catch (probeError) {
        return "network";
    }
}

/**
 * The page's copy of a network response, streaming the page's branch of the tee. The browser has already decoded a
 * gzip-encoded body, so `Content-Encoding` and the wire `Content-Length` are dropped from the copy.
 * @param {Response} networkResponse The network response.
 * @param {ReadableStream<Uint8Array>} pageBody The page's branch of the body.
 * @returns {Response} The answer for the page.
 */
function buildPageResponse(networkResponse, pageBody) {
    const headers = new Headers(networkResponse.headers);
    if (headers.has("content-encoding")) {
        headers.delete("content-encoding");
        headers.delete("content-length");
    }
    return new Response(pageBody, { status: networkResponse.status, statusText: networkResponse.statusText, headers: headers });
}

/**
 * Reads the store's branch of a download, compares its SHA-256 with the manifest and stores it on a match. Broadcasts
 * `file-stored`, or `file-failed` with reason `mismatch` or `network` (a storage error such as `QuotaExceededError` is a
 * `network`-kind failure whose `message` names the error). Never rejects.
 * @param {ReadableStream<Uint8Array>} storeBody The store's branch of the body.
 * @param {ResolvedRequest} resolved The resolved request.
 * @returns {Promise<boolean>} True when the file was stored.
 */
async function verifyAndStore(storeBody, resolved) {
    try {
        const bytes = await new Response(storeBody).arrayBuffer();
        const actualHash = await sha256Hex(bytes);
        if (actualHash !== resolved.entry.hash) {
            broadcast(createFailedMessage(resolved, "mismatch", null));
            return false;
        }
        await putFile(resolved.entry, bytes);
        broadcast(createStoredMessage(resolved));
        return true;
    } catch (error) {
        const failedMessage = createFailedMessage(resolved, "network", null);
        failedMessage.message = describeError(error);
        broadcast(failedMessage);
        return false;
    }
}

/**
 * Writes verified, decoded bytes to the files cache under their hash.
 * @param {{ hash: string, type: string }} entry The manifest entry.
 * @param {ArrayBuffer} bytes The verified bytes.
 * @returns {Promise<void>} Settles when stored.
 * @throws {Error} When the cache rejects the write (for example `QuotaExceededError`).
 */
async function putFile(entry, bytes) {
    const filesCache = await caches.open(CONTENT_FILES_CACHE);
    const headers = { "Content-Type": entry.type, "Content-Length": String(bytes.byteLength), "X-Bt-Content-Hash": entry.hash };
    await filesCache.put(fileKey(entry.hash), new Response(bytes, { status: STATUS_OK, headers: headers }));
    storedHashes.add(entry.hash);
}

/**
 * Lowercase hex SHA-256 of a byte buffer.
 * @param {ArrayBuffer} bytes The bytes.
 * @returns {Promise<string>} 64 lowercase hex characters.
 */
async function sha256Hex(bytes) {
    const digest = await crypto.subtle.digest("SHA-256", bytes);
    return Array.from(new Uint8Array(digest), (byteValue) => byteValue.toString(16).padStart(2, "0")).join("");
}

// ---------------------------------------------------------------------------------------------------------------------
// Navigations and the app shell (D23)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Answers a navigation network-first when an app-shell root exists, falling back offline to the shell's stored
 * `navigationFallback` page. Without a shell root the navigation gets no `respondWith`.
 * @param {FetchEvent} event The navigation's fetch event.
 */
function handleNavigation(event) {
    if (isStateLoaded && findShellRoot() === null) return;
    const outcome = respondToNavigation(event);
    event.respondWith(outcome.then((result) => result.response));
    event.waitUntil(outcome.then((result) => result.storeSettled));
}

/**
 * Network-first answer to a navigation. A successful answer for the shell's own page (the shell's base URL or its
 * `navigationFallback` path) is also stored when that page is not stored yet, so an `as-you-play` tab can start offline
 * next time; a failed network answer falls back to the stored page of the version the new page is pinned to.
 * @param {FetchEvent} event The navigation's fetch event.
 * @returns {Promise<{ response: Response, storeSettled: Promise<boolean> | null }>} The answer and any store work.
 */
async function respondToNavigation(event) {
    const pageClientId = typeof event.resultingClientId === "string" ? event.resultingClientId : "";
    let networkResponse = null;
    try {
        networkResponse = await fetch(event.request);
    } catch (networkError) {
        const fallbackPage = await readNavigationFallback(event.request, pageClientId);
        return { response: fallbackPage !== null ? fallbackPage : Response.error(), storeSettled: null };
    }
    const shellPage = await resolveShellPageForStoring(event.request, networkResponse, pageClientId);
    if (shellPage === null) return { response: networkResponse, storeSettled: null };
    const [pageBody, storeBody] = networkResponse.body.tee();
    return { response: buildPageResponse(networkResponse, pageBody), storeSettled: verifyAndStore(storeBody, shellPage) };
}

/**
 * The stored `navigationFallback` page of the shell root, for the version the navigating page is pinned to (else the
 * active version, which is then pinned).
 * @param {Request} request The navigation request.
 * @param {string} pageClientId The id of the page the navigation creates ("" when unknown).
 * @returns {Promise<Response | null>} The stored page, or null when there is none or the store cannot be read.
 */
async function readNavigationFallback(request, pageClientId) {
    try {
        const shellPage = await resolveShellFallback(pageClientId);
        if (shellPage === null) return null;
        return await serveStored(request, shellPage.entry, shellPage.entry.hash);
    } catch (error) {
        return null;
    }
}

/**
 * The shell root's `navigationFallback` entry for a page's version.
 * @param {string} pageClientId The page's client id ("" when unknown).
 * @returns {Promise<ResolvedRequest | null>} The fallback page as a resolved request (scene kind), or null.
 * @throws {Error} When the store cannot be read.
 */
async function resolveShellFallback(pageClientId) {
    await loadState();
    const shellRoot = findShellRoot();
    if (shellRoot === null) return null;
    const version = await versionForClient(pageClientId, shellRoot);
    if (version === null) return null;
    const manifest = await loadManifest(shellRoot.rootKey, version);
    if (manifest === null || typeof manifest.navigationFallback !== "string") return null;
    const fallbackUrl = shellRoot.baseUrl + manifest.navigationFallback.split("/").map(encodeURIComponent).join("/");
    return buildResolution(parseContentUrl(fallbackUrl), shellRoot, version, manifest);
}

/**
 * The shell page to store from a successful navigation answer: only when the navigation is for the shell's base URL or
 * its fallback path, the answer is a plain OK same-origin response, storing is allowed and the page is not stored yet.
 * @param {Request} request The navigation request.
 * @param {Response} networkResponse The network's answer.
 * @param {string} pageClientId The page's client id ("" when unknown).
 * @returns {Promise<ResolvedRequest | null>} The page to verify and store, or null to store nothing.
 */
async function resolveShellPageForStoring(request, networkResponse, pageClientId) {
    if (!networkResponse.ok || networkResponse.redirected || networkResponse.body === null) return null;
    try {
        const shellPage = await resolveShellFallback(pageClientId);
        if (shellPage === null || !shouldStore(shellPage) || storedHashes.has(shellPage.entry.hash)) return null;
        const lookupUrl = parseContentUrl(request.url).lookupUrl;
        if (!lookupUrl.startsWith(shellPage.root.baseUrl)) return null;
        const navigationPath = relativePathOf(lookupUrl, shellPage.root.baseUrl);
        return navigationPath === "" || navigationPath === shellPage.path ? shellPage : null;
    } catch (error) {
        return null;
    }
}

/**
 * The first app-shell root.
 * @returns {object | null} The shell root state, or null when none is configured.
 */
function findShellRoot() {
    for (const root of rootsByKey.values()) {
        if (root.isAppShell === true) return root;
    }
    return null;
}

// ---------------------------------------------------------------------------------------------------------------------
// State: roots, manifests, pins (D15, D16)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Reads roots, pins and stored hashes from the store once per worker start; later calls return the same promise. A read
 * that fails is retried by the next call.
 * @returns {Promise<void>} Resolves when memory mirrors the store.
 */
function loadState() {
    if (stateReady === null) {
        stateReady = Promise.all([loadRoots(), loadPins(), loadStoredHashes()]).then(
            () => { isStateLoaded = true; },
            (error) => { stateReady = null; throw error; }
        );
    }
    return stateReady;
}

/**
 * Reads the roots index and every root state, preloads the manifests of their active and pending versions, then
 * replaces `rootsByKey` in one step (fetches keep using the previous roots until then).
 * @returns {Promise<void>} Resolves when the roots are in memory.
 * @throws {Error} When the store cannot be read.
 */
async function loadRoots() {
    const stateCache = await caches.open(CONTENT_STATE_CACHE);
    const indexedKeys = await readJson(stateCache, storeKey(ROOTS_INDEX_NAME));
    const rootKeys = Array.isArray(indexedKeys) ? indexedKeys : [];
    const loadedRoots = new Map();
    for (const rootKey of rootKeys) {
        const rootState = await readJson(stateCache, rootStateKey(rootKey));
        if (rootState != null && typeof rootState.baseUrl === "string") loadedRoots.set(rootKey, rootState);
    }
    for (const rootState of loadedRoots.values()) {
        for (const version of [rootState.activeVersion, rootState.pendingVersion]) {
            if (typeof version === "string") await loadManifest(rootState.rootKey, version);
        }
    }
    rootsByKey.clear();
    for (const [rootKey, rootState] of loadedRoots) rootsByKey.set(rootKey, rootState);
}

/**
 * Reads every stored client pin into `pinsByClient`. Pins are written only by this worker, so memory stays exact after.
 * @returns {Promise<void>} Resolves when the pins are in memory.
 * @throws {Error} When the store cannot be read.
 */
async function loadPins() {
    const stateCache = await caches.open(CONTENT_STATE_CACHE);
    for (const keyRequest of await stateCache.keys()) {
        const keyName = storeKeyName(keyRequest.url);
        if (keyName === null || !keyName.startsWith(PINS_FOLDER)) continue;
        const pins = await readJson(stateCache, keyRequest.url);
        if (pins != null && typeof pins === "object") pinsByClient.set(decodeURIComponent(keyName.slice(PINS_FOLDER.length)), pins);
    }
}

/**
 * Reads the hashes present in the files cache into `storedHashes`.
 * @returns {Promise<void>} Resolves when the hashes are in memory.
 * @throws {Error} When the store cannot be read.
 */
async function loadStoredHashes() {
    const filesCache = await caches.open(CONTENT_FILES_CACHE);
    storedHashes.clear();
    for (const keyRequest of await filesCache.keys()) {
        const hash = hashOfFileKey(keyRequest.url);
        if (hash !== null) storedHashes.add(hash);
    }
}

/**
 * A manifest by root and version, from memory or the state cache.
 * @param {string} rootKey The root key.
 * @param {string} version The manifest version.
 * @returns {Promise<object | null>} The parsed manifest, or null when it is not stored.
 * @throws {Error} When the store cannot be read.
 */
async function loadManifest(rootKey, version) {
    const mapKey = manifestMapKey(rootKey, version);
    const known = manifestsByKey.get(mapKey);
    if (known !== undefined) return known;
    const stateCache = await caches.open(CONTENT_STATE_CACHE);
    const manifest = await readJson(stateCache, manifestKey(rootKey, version));
    if (manifest == null || typeof manifest.files !== "object") return null;
    manifestsByKey.set(mapKey, manifest);
    return manifest;
}

/**
 * A client's pins (root key → version).
 * @param {string} clientId The client id ("" gives no pins).
 * @returns {Promise<Object<string, string>>} The pins; an empty object when the client has none.
 * @throws {Error} When the store cannot be read.
 */
async function readPin(clientId) {
    if (clientId === "") return {};
    await loadState();
    const pins = pinsByClient.get(clientId);
    return pins !== undefined ? pins : {};
}

/**
 * Pins a client to a version of a root, in memory and in the state cache, so the pin survives a worker restart.
 * @param {string} clientId The client id.
 * @param {string} rootKey The root key.
 * @param {string} version The version the client keeps.
 * @returns {Promise<void>} Settles when the pin is stored.
 * @throws {Error} When the store cannot be written.
 */
async function writePin(clientId, rootKey, version) {
    const pins = Object.assign({}, pinsByClient.get(clientId));
    pins[rootKey] = version;
    pinsByClient.set(clientId, pins);
    const stateCache = await caches.open(CONTENT_STATE_CACHE);
    await stateCache.put(pinKey(clientId), jsonResponse(pins));
}

// ---------------------------------------------------------------------------------------------------------------------
// Garbage collection (D16)
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Deletes what no kept version needs. A version is kept when it is a root's active or pending version or a live window
 * client's pin. In order: pins of closed clients, manifests of versions not kept, then every file whose hash no kept
 * manifest lists. When a kept manifest cannot be read, no file is deleted (its files are unknown).
 * @returns {Promise<void>} Settles when collection is done.
 * @throws {Error} When the store cannot be read.
 */
async function collectGarbage() {
    await loadState();
    await loadRoots();
    const stateCache = await caches.open(CONTENT_STATE_CACHE);
    const liveClientIds = await listLiveClientIds();
    await deleteClosedClientPins(stateCache, liveClientIds);
    const keptVersions = collectKeptVersions();
    const keptHashes = await deleteUnkeptManifests(stateCache, keptVersions);
    if (keptHashes === null) return;
    await deleteUnkeptFiles(keptHashes);
}

/**
 * The ids of every open window client of this origin, controlled or not.
 * @returns {Promise<Set<string>>} The live client ids.
 */
async function listLiveClientIds() {
    const windowClients = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
    return new Set(windowClients.map((windowClient) => windowClient.id));
}

/**
 * Deletes the pins of clients that are no longer open, in memory and in the state cache.
 * @param {Cache} stateCache The state cache.
 * @param {Set<string>} liveClientIds The open clients.
 * @returns {Promise<void>} Settles when deleted.
 */
async function deleteClosedClientPins(stateCache, liveClientIds) {
    for (const clientId of Array.from(pinsByClient.keys())) {
        if (liveClientIds.has(clientId)) continue;
        pinsByClient.delete(clientId);
        await stateCache.delete(pinKey(clientId));
    }
}

/**
 * The kept versions per root: active, pending and live pins (pins of closed clients are already gone).
 * @returns {Map<string, Set<string>>} Root key → kept versions.
 */
function collectKeptVersions() {
    const keptVersions = new Map();
    const keepVersion = (rootKey, version) => {
        if (typeof version !== "string") return;
        if (!keptVersions.has(rootKey)) keptVersions.set(rootKey, new Set());
        keptVersions.get(rootKey).add(version);
    };
    for (const rootState of rootsByKey.values()) {
        keepVersion(rootState.rootKey, rootState.activeVersion);
        keepVersion(rootState.rootKey, rootState.pendingVersion);
    }
    for (const pins of pinsByClient.values()) {
        for (const [rootKey, version] of Object.entries(pins)) {
            if (rootsByKey.has(rootKey)) keepVersion(rootKey, version);
        }
    }
    return keptVersions;
}

/**
 * Deletes every stored manifest whose version is not kept and gathers the hashes the kept ones list.
 * @param {Cache} stateCache The state cache.
 * @param {Map<string, Set<string>>} keptVersions Root key → kept versions.
 * @returns {Promise<Set<string> | null>} The kept hashes, or null when a kept manifest could not be read.
 */
async function deleteUnkeptManifests(stateCache, keptVersions) {
    const keptHashes = new Set();
    let isEveryKeptManifestRead = true;
    for (const keyRequest of await stateCache.keys()) {
        const manifestId = parseManifestKey(keyRequest.url);
        if (manifestId === null) continue;
        const versions = keptVersions.get(manifestId.rootKey);
        if (versions === undefined || !versions.has(manifestId.version)) {
            manifestsByKey.delete(manifestMapKey(manifestId.rootKey, manifestId.version));
            await stateCache.delete(keyRequest.url);
            continue;
        }
        const manifest = await loadManifest(manifestId.rootKey, manifestId.version);
        if (manifest === null) {
            isEveryKeptManifestRead = false;
            continue;
        }
        for (const entry of Object.values(manifest.files)) keptHashes.add(entry.hash);
    }
    return isEveryKeptManifestRead ? keptHashes : null;
}

/**
 * Deletes every stored file whose hash no kept manifest lists.
 * @param {Set<string>} keptHashes The hashes kept versions need.
 * @returns {Promise<void>} Settles when deleted.
 */
async function deleteUnkeptFiles(keptHashes) {
    const filesCache = await caches.open(CONTENT_FILES_CACHE);
    for (const keyRequest of await filesCache.keys()) {
        const hash = hashOfFileKey(keyRequest.url);
        if (hash === null || keptHashes.has(hash)) continue;
        await filesCache.delete(keyRequest.url);
        storedHashes.delete(hash);
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Store keys and helpers
// ---------------------------------------------------------------------------------------------------------------------

/**
 * The absolute synthetic store key of a name on this worker's origin.
 * @param {string} path The key name below `STORE_KEY_PREFIX`.
 * @returns {string} The key URL.
 */
function storeKey(path) {
    return new URL(STORE_KEY_PREFIX + path, self.location.origin).href;
}

/**
 * The files-cache key of a hash.
 * @param {string} hash The lowercase hex SHA-256.
 * @returns {string} The key URL.
 */
function fileKey(hash) {
    return storeKey(FILES_FOLDER + hash);
}

/**
 * The state-cache key of a root state.
 * @param {string} rootKey The root key (absolute manifest URL).
 * @returns {string} The key URL.
 */
function rootStateKey(rootKey) {
    return storeKey(ROOTS_FOLDER + encodeURIComponent(rootKey));
}

/**
 * The state-cache key of a manifest version.
 * @param {string} rootKey The root key.
 * @param {string} version The manifest version.
 * @returns {string} The key URL.
 */
function manifestKey(rootKey, version) {
    return storeKey(MANIFESTS_FOLDER + encodeURIComponent(rootKey) + "/" + version);
}

/**
 * The state-cache key of a client's pins.
 * @param {string} clientId The client id.
 * @returns {string} The key URL.
 */
function pinKey(clientId) {
    return storeKey(PINS_FOLDER + encodeURIComponent(clientId));
}

/**
 * The in-memory key of a manifest.
 * @param {string} rootKey The root key.
 * @param {string} version The manifest version.
 * @returns {string} `rootKey + "\n" + version`.
 */
function manifestMapKey(rootKey, version) {
    return rootKey + "\n" + version;
}

/**
 * The name part of a store key URL (below `STORE_KEY_PREFIX`).
 * @param {string} keyUrl A cache key URL.
 * @returns {string | null} The name, or null when the URL is not a store key of this origin.
 */
function storeKeyName(keyUrl) {
    const url = new URL(keyUrl);
    if (url.origin !== self.location.origin || !url.pathname.startsWith(STORE_KEY_PREFIX)) return null;
    return url.pathname.slice(STORE_KEY_PREFIX.length);
}

/**
 * The hash of a files-cache key.
 * @param {string} keyUrl A files-cache key URL.
 * @returns {string | null} The hash, or null when the URL is not a file key.
 */
function hashOfFileKey(keyUrl) {
    const keyName = storeKeyName(keyUrl);
    if (keyName === null || !keyName.startsWith(FILES_FOLDER)) return null;
    return keyName.slice(FILES_FOLDER.length);
}

/**
 * The root key and version of a manifest key.
 * @param {string} keyUrl A state-cache key URL.
 * @returns {{ rootKey: string, version: string } | null} The manifest's identity, or null for another kind of key.
 */
function parseManifestKey(keyUrl) {
    const keyName = storeKeyName(keyUrl);
    if (keyName === null || !keyName.startsWith(MANIFESTS_FOLDER)) return null;
    const parts = keyName.slice(MANIFESTS_FOLDER.length).split("/");
    if (parts.length !== 2) return null;
    return { rootKey: decodeURIComponent(parts[0]), version: parts[1] };
}

/**
 * Reads a JSON value stored in a cache.
 * @param {Cache} cache The cache.
 * @param {string} key The key URL.
 * @returns {Promise<*>} The parsed value, or null when the key is missing or not valid JSON.
 * @throws {Error} When the cache cannot be read.
 */
async function readJson(cache, key) {
    const storedResponse = await cache.match(key);
    if (storedResponse === undefined) return null;
    try {
        return JSON.parse(await storedResponse.text());
    } catch (error) {
        return null;
    }
}

/**
 * A JSON response for the state cache.
 * @param {*} value The value to store.
 * @returns {Response} The response.
 */
function jsonResponse(value) {
    return new Response(JSON.stringify(value), { status: STATUS_OK, headers: { "Content-Type": "application/json" } });
}

/**
 * The `file-stored` message of a resolved request.
 * @param {ResolvedRequest} resolved The resolved request.
 * @returns {{ type: string, rootKey: string, version: string, path: string, hash: string }} The message.
 */
function createStoredMessage(resolved) {
    return { type: "file-stored", rootKey: resolved.root.rootKey, version: resolved.version, path: resolved.path, hash: resolved.entry.hash };
}

/**
 * The `file-failed` message of a resolved request.
 * @param {ResolvedRequest} resolved The resolved request.
 * @param {"mismatch" | "cors" | "status" | "network"} reason Why the file was not stored.
 * @param {number | null} status The HTTP status for `status`, else null.
 * @returns {{ type: string, rootKey: string, version: string, path: string, hash: string, reason: string, status: number | null, message?: string }} The message.
 */
function createFailedMessage(resolved, reason, status) {
    return {
        type: "file-failed",
        rootKey: resolved.root.rootKey,
        version: resolved.version,
        path: resolved.path,
        hash: resolved.entry.hash,
        reason: reason,
        status: status
    };
}

/**
 * Posts a message to every page on the broadcast channel.
 * @param {object} message The protocol message.
 */
function broadcast(message) {
    contentChannel.postMessage(message);
}

/**
 * A short description of an error for logs and failure messages.
 * @param {*} error The error.
 * @returns {string} Its name and message.
 */
function describeError(error) {
    if (error == null) return "unknown error";
    const errorName = typeof error.name === "string" ? error.name : "Error";
    return typeof error.message === "string" && error.message !== "" ? errorName + ": " + error.message : errorName;
}

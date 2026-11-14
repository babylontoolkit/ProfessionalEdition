// Babylon Toolkit - Unity CLI deploy commands
//
// `unity command bt_deploy_targets`, `bt_deploy` and `bt_deploy_status`: deploy an export folder to the Amazon S3
// targets in Assets/[Config]/deploy.json from the command line, with the same upload code, order and skip rule as
// Babylon Toolkit's Amazon Web Services (S3) window (AmazonBucket in CanvasTools.dll).
//
// bt_deploy is a dry run unless --confirm true: the dry run reads checksum.json and the bucket listing and writes
// nothing. The deploy work runs on the thread pool so it progresses whether or not the editor ticks between bridge
// calls; bt_deploy returns at once and bt_deploy_status reports progress, then the last report (also written to
// Logs/BabylonToolkitDeploy.txt). Every text these commands return or write passes through Redact, so the access
// key, secret key, bucket name and CloudFront id never appear in output.
//
// Compiled by the BabylonToolkit.Editor.CLI assembly definition next to it (BT_UNITY_PIPELINE), like
// BabylonToolkitCliCommands.cs.
#if UNITY_EDITOR && BT_UNITY_PIPELINE
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Amazon;
using Unity.Pipeline.Commands;   // [CliCommand] / [CliArg] - assembly Unity.Pipeline.Attributes (com.unity.pipeline 0.8+)

/// <summary>
/// The bt_deploy_targets / bt_deploy / bt_deploy_status CLI commands, plus the documented public seams the
/// DeployCommandsCheck script calls (BuildDestinationUrl, FindTarget, Redact, StartDeploy, GetStatus, BeginTestRun, EndTestRun).
/// One deploy runs at a time; its state is guarded by a lock because the work runs on the thread pool.
/// </summary>
public static class BabylonToolkitDeployCommands
{
    /// <summary>The project-relative path of the full report of the last deploy (every plan entry included).</summary>
    public const string REPORT_PATH = "Logs/BabylonToolkitDeploy.txt";

    private const string DEPLOY_CONFIG_DISPLAY_PATH = "Assets/[Config]/deploy.json";
    private const string DEPLOY_WINDOW_NAME = "Babylon Toolkit's Amazon Web Services (S3) window";
    private const string REDACTED_TEXT = "***";
    private const string MODE_DRY_RUN = "dry-run";
    private const string MODE_UPLOAD = "upload";
    private const string PHASE_CHECKING = "checking";
    private const string PHASE_PLANNING = "planning";
    private const string PHASE_UPLOADING = "uploading";
    private const string PHASE_INVALIDATING = "invalidating";
    private const string UPLOADING_STATUS_PREFIX = "Uploading ";
    private const string SKIPPING_STATUS_PREFIX = "Skipping ";
    private const string SAVING_CHECKSUM_STATUS = "Saving remote checksum list";
    private const string NO_REPORT_YET = "no deploy has run in this editor session";
    private const string TIME_FORMAT = "yyyy-MM-dd HH:mm:ss zzz";
    private const int REPORT_PREVIEW_ENTRIES = 20;
    private const int MANIFEST_CHECK_LISTED_NAMES = 5;

    private static readonly object StateLock = new object();
    private static bool deployRunning = false;
    private static string currentPhase = "";
    private static string currentFile = "";
    private static bool countsCurrent = false;
    private static string lastReport = null;
    private static string[] activeSecrets = new string[0];

    /// unity command bt_deploy_targets
    [CliCommand("bt_deploy_targets",
                "List the Amazon S3 deploy targets in Assets/[Config]/deploy.json (secrets shown only as set / not set)",
                MainThreadRequired = true)]
    public static string DeployTargets()
    {
        string accessKey = PlayerPrefs.GetString(AmazonUtilities.AWS_ACCESS_KEY);
        string secretKey = PlayerPrefs.GetString(AmazonUtilities.AWS_SECRET_KEY);
        DeployInfo deployInfo = RunRedacted(ReadDeployInfo, new[] { accessKey, secretKey });
        List<string> secrets = new List<string> { accessKey, secretKey };
        StringBuilder listing = new StringBuilder();
        foreach (AmazonBucketInfo target in deployInfo.aws)
        {
            secrets.Add(target.bucketName);
            secrets.Add(target.cloudFront);
            AppendTargetListing(listing, target, accessKey, secretKey);
        }
        return Redact(listing.ToString().TrimEnd(), secrets);
    }

    /// unity command bt_deploy [--target "<deployType>"] [--confirm true] [--always true]
    [CliCommand("bt_deploy",
                "Deploy the export folder to an Amazon S3 target: a dry run (writes nothing) unless --confirm true; returns at once, poll bt_deploy_status",
                MainThreadRequired = true)]
    public static string Deploy(
        [CliArg("target", "deployType of the deploy.json target; empty = the only target")] string target = null,
        [CliArg("confirm", "Really upload (and invalidate CloudFront when set); otherwise a dry run")] bool confirm = false,
        [CliArg("always", "Upload every file, ignoring the remote checksums")] bool always = false)
    {
        string accessKey = PlayerPrefs.GetString(AmazonUtilities.AWS_ACCESS_KEY);
        string secretKey = PlayerPrefs.GetString(AmazonUtilities.AWS_SECRET_KEY);
        // Until a target is chosen the keys are the only secrets known; after that its bucket and CloudFront id join them.
        AmazonBucketInfo deployTarget = RunRedacted(() => FindTarget(ReadDeployInfo(), target), new[] { accessKey, secretKey });
        string sourceFolder = RunRedacted(() => ResolveSourceFolder(deployTarget), SecretsOf(deployTarget, accessKey, secretKey));
        bool previous = CanvasTools.CanvasToolsExporter.SuppressDialogs;
        CanvasTools.CanvasToolsExporter.SuppressDialogs = true;
        try
        {
            return StartDeploy(deployTarget, sourceFolder, confirm, always, accessKey, secretKey);
        }
        finally
        {
            CanvasTools.CanvasToolsExporter.SuppressDialogs = previous;
        }
    }

    /// unity command bt_deploy_status
    [CliCommand("bt_deploy_status",
                "Report the deploy: running <phase> <current>/<total> <file>, or idle with the last report",
                MainThreadRequired = true)]
    public static string DeployStatus()
    {
        return GetStatus();
    }

    /// <summary>
    /// The deploy status: <c>running &lt;phase&gt; &lt;current&gt;/&lt;total&gt; &lt;file&gt;</c> while a deploy runs, else
    /// <c>idle</c> followed by the last report. Phases: checking (the upload's remote checksum and listing reads), planning
    /// (dry run), uploading, invalidating. Counts come from AmazonBucket's progress statics.
    /// </summary>
    /// <returns>The redacted status text.</returns>
    public static string GetStatus()
    {
        lock (StateLock)
        {
            if (deployRunning)
            {
                return FormatRunningStatus();
            }
            return "idle\n" + (lastReport ?? NO_REPORT_YET);
        }
    }

    /// <summary>
    /// Joins a host address and a remote path into the destination URL: the host without a trailing '/', then '/',
    /// then the remote path without leading or trailing '/', then '/' (an empty remote path gives host + "/").
    /// </summary>
    /// <param name="hostAddress">deploy.json's hostAddress, e.g. https://repo.babylontoolkit.com</param>
    /// <param name="remotePath">deploy.json's remotePath, e.g. mariokart</param>
    /// <returns>The URL the deployed folder is served from.</returns>
    public static string BuildDestinationUrl(string hostAddress, string remotePath)
    {
        string host = (hostAddress ?? "").Trim().TrimEnd('/');
        string remote = (remotePath ?? "").Trim().Trim('/');
        return String.IsNullOrEmpty(remote) ? host + "/" : host + "/" + remote + "/";
    }

    /// <summary>
    /// Finds a deploy target by deployType (ordinal). An empty name picks the only target.
    /// </summary>
    /// <param name="deployInfo">The parsed deploy.json.</param>
    /// <param name="targetName">The deployType to find, or null/empty for the only target.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ArgumentException">No targets; no name with several targets; or no target of that name. The message lists every deployType.</exception>
    public static AmazonBucketInfo FindTarget(DeployInfo deployInfo, string targetName)
    {
        AmazonBucketInfo[] targets = deployInfo?.aws ?? new AmazonBucketInfo[0];
        if (targets.Length == 0)
        {
            throw new ArgumentException($"{DEPLOY_CONFIG_DISPLAY_PATH} has no aws deploy targets - add one in {DEPLOY_WINDOW_NAME}.");
        }
        string targetList = String.Join(", ", targets.Select(target => "\"" + target.deployType + "\""));
        if (String.IsNullOrEmpty(targetName))
        {
            if (targets.Length == 1)
            {
                return targets[0];
            }
            throw new ArgumentException($"Several deploy targets - pass --target with one of: {targetList}");
        }
        AmazonBucketInfo match = targets.FirstOrDefault(target => String.Equals(target.deployType, targetName, StringComparison.Ordinal));
        if (match == null)
        {
            throw new ArgumentException($"Unknown deploy target \"{targetName}\" - pass --target with one of: {targetList}");
        }
        return match;
    }

    /// <summary>
    /// Replaces every non-empty secret in the text with ***. A backstop: callers never put secrets in text on purpose.
    /// </summary>
    /// <param name="text">The text to clean; null stays null.</param>
    /// <param name="secrets">The secret strings (null or empty entries are ignored).</param>
    /// <returns>The text with every secret replaced.</returns>
    public static string Redact(string text, IEnumerable<string> secrets)
    {
        if (String.IsNullOrEmpty(text) || secrets == null)
        {
            return text;
        }
        string redacted = text;
        foreach (string secret in secrets.Where(secret => !String.IsNullOrEmpty(secret)).OrderByDescending(secret => secret.Length))
        {
            redacted = redacted.Replace(secret, REDACTED_TEXT);
        }
        return redacted;
    }

    /// <summary>
    /// The core of bt_deploy, after deploy.json and PlayerPrefs are read (the public seam the check script calls with a
    /// temporary target, folder and keys). Validates, runs the content manifest check, then starts the dry run or the
    /// upload on the thread pool and returns at once. A real upload is refused on a manifest mismatch before any S3 client
    /// exists. Must be called on the main thread. Every exception message is redacted.
    /// </summary>
    /// <param name="target">The deploy target.</param>
    /// <param name="sourceFolder">The local export folder to deploy.</param>
    /// <param name="confirm">True uploads (then invalidates CloudFront when set); false plans a dry run that writes nothing.</param>
    /// <param name="alwaysUpload">True uploads (or plans) every file regardless of the remote checksums.</param>
    /// <param name="accessKey">The AWS access key.</param>
    /// <param name="secretKey">The AWS secret key.</param>
    /// <returns><c>started dry-run|upload "&lt;target&gt;" &lt;source folder&gt; → &lt;destination URL&gt;</c></returns>
    /// <exception cref="InvalidOperationException">A deploy is running, validation failed, or the manifest check refused the upload.</exception>
    public static string StartDeploy(AmazonBucketInfo target, string sourceFolder, bool confirm, bool alwaysUpload, string accessKey, string secretKey)
    {
        string[] secrets = SecretsOf(target, accessKey, secretKey);
        try
        {
            ThrowIfRunning();
            RegionEndpoint region = ValidateTarget(target, sourceFolder, accessKey, secretKey);
            DeployRun deployRun = CreateRun(target, sourceFolder, confirm, alwaysUpload, accessKey, secretKey, region, secrets);
            if (confirm && !deployRun.ManifestMatches)
            {
                throw new InvalidOperationException($"Refusing to upload: the content manifest does not match the export ({deployRun.ManifestSummary}). Run `unity command bt_build_content_manifest` first (or re-export), then deploy again.");
            }
            BeginRun(deployRun.Confirm ? PHASE_CHECKING : PHASE_PLANNING, secrets);
            Task.Run(() => ExecuteDeployAsync(deployRun));
            return Redact($"started {deployRun.Mode} \"{deployRun.TargetName}\" {deployRun.SourceFolder} → {deployRun.DestinationUrl}", secrets);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(Redact(exception.Message, secrets));
        }
    }

    /// <summary>
    /// Test seam: marks a deploy as running in the planning phase without starting any work (no network), so the check
    /// script can prove a second start is refused. Pair every call with <see cref="EndTestRun"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A real deploy is running.</exception>
    public static void BeginTestRun()
    {
        ThrowIfRunning();
        BeginRun(PHASE_PLANNING, new string[0]);
    }

    /// <summary>Test seam: clears the running state set by <see cref="BeginTestRun"/>; the last report is kept.</summary>
    public static void EndTestRun()
    {
        lock (StateLock)
        {
            deployRunning = false;
        }
    }

    /// <summary>Everything a started deploy needs, captured on the main thread so the pool thread touches no Unity state.</summary>
    private sealed class DeployRun
    {
        public string TargetName;
        public string Mode;
        public bool Confirm;
        public bool AlwaysUpload;
        public string SourceFolder;
        public string RemotePath;
        public string BucketName;
        public string CloudFront;
        public string DestinationUrl;
        public string AccessKey;
        public string SecretKey;
        public RegionEndpoint Region;
        public bool ManifestMatches;
        public string ManifestSummary;
        public string ReportFilePath;
        public string[] Secrets;
    }

    /// <summary>Reads Assets/[Config]/deploy.json exactly as the Amazon Web Services (S3) window does.</summary>
    private static DeployInfo ReadDeployInfo()
    {
        string deployFile = Path.Combine(Application.dataPath, CanvasToolsStatics.CANVAS_TOOLS_CONFIG + "/deploy.json");
        if (!File.Exists(deployFile))
        {
            throw new FileNotFoundException($"No deploy configuration at {deployFile} ({DEPLOY_CONFIG_DISPLAY_PATH}) - create a target in {DEPLOY_WINDOW_NAME}.");
        }
        DeployInfo deployInfo = UnityTools.FromJson<DeployInfo>(FileTools.ReadAllText(deployFile));
        if (deployInfo == null || deployInfo.aws == null || deployInfo.aws.Length == 0)
        {
            throw new InvalidOperationException($"{DEPLOY_CONFIG_DISPLAY_PATH} has no aws deploy targets - add one in {DEPLOY_WINDOW_NAME}.");
        }
        return deployInfo;
    }

    /// <summary>The window's source folder rule: the target's sourceFolder when set, else the export folder (plus the scenes folder unless the project deploys the scene viewer).</summary>
    private static string ResolveSourceFolder(AmazonBucketInfo target)
    {
        if (!String.IsNullOrEmpty(target.sourceFolder))
        {
            return target.sourceFolder;
        }
        string exportFolder = UnityTools.GetDefaultExportFolder();
        if (UnityTools.DeploySceneViewer())
        {
            return exportFolder;
        }
        return Path.Combine(exportFolder, CanvasToolsInfo.Instance.DefaultScenePath);
    }

    /// <summary>The window's remote path rule: "/" means the bucket root.</summary>
    private static string NormalizeRemotePath(string remotePath)
    {
        return remotePath == "/" ? "" : (remotePath ?? "");
    }

    /// <summary>Appends one target's listing; secrets appear only as set / not set.</summary>
    private static void AppendTargetListing(StringBuilder listing, AmazonBucketInfo target, string accessKey, string secretKey)
    {
        listing.AppendLine("target \"" + target.deployType + "\"");
        listing.AppendLine("  region      : " + target.regionName);
        listing.AppendLine("  remotePath  : " + NormalizeRemotePath(target.remotePath));
        listing.AppendLine("  bucket      : " + FormatIsSet(target.bucketName));
        listing.AppendLine("  cloudFront  : " + FormatIsSet(target.cloudFront));
        listing.AppendLine("  destination : " + BuildDestinationUrl(target.hostAddress, NormalizeRemotePath(target.remotePath)));
        listing.AppendLine("  source      : " + ResolveSourceFolder(target));
        listing.AppendLine("  accessKey   : " + FormatIsSet(accessKey));
        listing.AppendLine("  secretKey   : " + FormatIsSet(secretKey));
    }

    /// <summary>"set" or "not set" - how every secret appears in a listing.</summary>
    private static string FormatIsSet(string value)
    {
        return String.IsNullOrEmpty(value) ? "not set" : "set";
    }

    /// <summary>The strings Redact removes for a target: both keys, its bucket name and its CloudFront id.</summary>
    private static string[] SecretsOf(AmazonBucketInfo target, string accessKey, string secretKey)
    {
        return new[] { accessKey, secretKey, target?.bucketName, target?.cloudFront };
    }

    /// <summary>
    /// Runs one step and rethrows any failure with its message redacted, keeping ArgumentException (a bad --target)
    /// so the CLI still reports it as a parameter error.
    /// </summary>
    private static T RunRedacted<T>(Func<T> step, string[] secrets)
    {
        try
        {
            return step();
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(Redact(exception.Message, secrets));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(Redact(exception.Message, secrets));
        }
    }

    /// <summary>Throws with the current status line when a deploy is running.</summary>
    private static void ThrowIfRunning()
    {
        lock (StateLock)
        {
            if (deployRunning)
            {
                throw new InvalidOperationException("A deploy is already running: " + FormatRunningStatus());
            }
        }
    }

    /// <summary>Checks the folder, keys, bucket and region, with a how-to-fix message for each.</summary>
    /// <returns>The resolved region.</returns>
    private static RegionEndpoint ValidateTarget(AmazonBucketInfo target, string sourceFolder, string accessKey, string secretKey)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (String.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder} - export the project first (bt_build_project / bt_export_level).");
        }
        if (String.IsNullOrEmpty(accessKey) || String.IsNullOrEmpty(secretKey))
        {
            throw new InvalidOperationException($"AWS keys not set - set the Access Key and Secret Key in {DEPLOY_WINDOW_NAME} (stored in PlayerPrefs {AmazonUtilities.AWS_ACCESS_KEY} / {AmazonUtilities.AWS_SECRET_KEY}).");
        }
        if (String.IsNullOrEmpty(target.bucketName))
        {
            throw new InvalidOperationException($"Target \"{target.deployType}\" has no bucket - set bucketName in {DEPLOY_CONFIG_DISPLAY_PATH}.");
        }
        return AmazonBucket.ResolveRegion(target.regionName);
    }

    /// <summary>Captures the run on the main thread, including the content manifest check of the source folder.</summary>
    private static DeployRun CreateRun(AmazonBucketInfo target, string sourceFolder, bool confirm, bool alwaysUpload, string accessKey, string secretKey, RegionEndpoint region, string[] secrets)
    {
        string remotePath = NormalizeRemotePath(target.remotePath);
        DeployRun deployRun = new DeployRun
        {
            TargetName = target.deployType,
            Mode = confirm ? MODE_UPLOAD : MODE_DRY_RUN,
            Confirm = confirm,
            AlwaysUpload = alwaysUpload,
            SourceFolder = sourceFolder,
            RemotePath = remotePath,
            BucketName = target.bucketName,
            CloudFront = target.cloudFront,
            DestinationUrl = BuildDestinationUrl(target.hostAddress, remotePath),
            AccessKey = accessKey,
            SecretKey = secretKey,
            Region = region,
            ReportFilePath = Path.Combine(Directory.GetCurrentDirectory(), REPORT_PATH),
            Secrets = secrets
        };
        CheckContentManifest(deployRun);
        return deployRun;
    }

    /// <summary>
    /// Runs ContentManifestExport.CheckForProject on the folder that holds the manifest: the source folder when it holds
    /// content-manifest.json, else its scenes folder when that exists, else the source folder itself.
    /// </summary>
    private static void CheckContentManifest(DeployRun deployRun)
    {
        string manifestFolder = ResolveManifestFolder(deployRun.SourceFolder);
        try
        {
            CanvasTools.ContentManifestCheckResult checkResult = CanvasTools.ContentManifestExport.CheckForProject(manifestFolder);
            deployRun.ManifestMatches = checkResult.Matches;
            deployRun.ManifestSummary = FormatManifestCheck(checkResult);
        }
        catch (Exception exception)
        {
            deployRun.ManifestMatches = false;
            deployRun.ManifestSummary = "check failed: " + exception.Message;
        }
    }

    /// <summary>The folder holding content-manifest.json: the source, else its scenes folder when that exists, else the source.</summary>
    private static string ResolveManifestFolder(string sourceFolder)
    {
        if (File.Exists(Path.Combine(sourceFolder, AmazonBucket.CONTENT_MANIFEST_FILE)))
        {
            return sourceFolder;
        }
        string scenesFolder = Path.Combine(sourceFolder, CanvasToolsInfo.Instance.DefaultScenePath);
        return Directory.Exists(scenesFolder) ? scenesFolder : sourceFolder;
    }

    /// <summary>One line: matches, missing, or a mismatch with each non-empty list's count and first names.</summary>
    private static string FormatManifestCheck(CanvasTools.ContentManifestCheckResult checkResult)
    {
        if (checkResult.ManifestMissing)
        {
            return "missing - no readable content-manifest.json at " + checkResult.ManifestPath;
        }
        if (checkResult.Matches)
        {
            return "matches (version " + checkResult.ListedVersion + ")";
        }
        List<string> differences = new List<string>();
        AppendDifference(differences, "changed", checkResult.ChangedFiles);
        AppendDifference(differences, "missing", checkResult.MissingFiles);
        AppendDifference(differences, "unlisted", checkResult.UnlistedReferencedFiles);
        AppendDifference(differences, "changed groups", checkResult.ChangedGroups);
        if (differences.Count == 0)
        {
            differences.Add("version " + checkResult.ListedVersion + " listed, " + checkResult.CurrentVersion + " now");
        }
        return "mismatch - " + String.Join("; ", differences);
    }

    /// <summary>Adds "&lt;label&gt; &lt;count&gt; (&lt;first names&gt;)" for a non-empty list.</summary>
    private static void AppendDifference(List<string> differences, string label, List<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }
        string shownNames = String.Join(", ", names.Take(MANIFEST_CHECK_LISTED_NAMES));
        string moreNames = names.Count > MANIFEST_CHECK_LISTED_NAMES ? ", …" : "";
        differences.Add($"{label} {names.Count} ({shownNames}{moreNames})");
    }

    /// <summary>Marks a deploy as running in the given phase.</summary>
    private static void BeginRun(string phase, string[] secrets)
    {
        lock (StateLock)
        {
            if (deployRunning)
            {
                throw new InvalidOperationException("A deploy is already running: " + FormatRunningStatus());
            }
            deployRunning = true;
            countsCurrent = false;
            currentPhase = phase;
            currentFile = "";
            activeSecrets = secrets;
        }
    }

    /// <summary>
    /// The running status line. Shows 0/0 until this run has reset AmazonBucket's counters, which still hold the previous
    /// run's numbers when a deploy starts. Call under StateLock.
    /// </summary>
    private static string FormatRunningStatus()
    {
        string counts = countsCurrent ? $"{AmazonBucket.CurrentFileNum}/{AmazonBucket.TotalFileCount}" : "0/0";
        string statusLine = $"running {currentPhase} {counts} {currentFile}";
        return Redact(statusLine.TrimEnd(), activeSecrets);
    }

    /// <summary>Publishes the running phase and current file under the state lock.</summary>
    private static void SetProgress(string phase, string file)
    {
        lock (StateLock)
        {
            currentPhase = phase;
            currentFile = file;
        }
    }

    /// <summary>
    /// Marks AmazonBucket's counters as this run's. Called once PlanUploadAsync / UploadFolderAsync has returned its task:
    /// both reset the counters before their first await, so by then the synchronous start has run.
    /// </summary>
    private static void MarkCountsCurrent()
    {
        lock (StateLock)
        {
            countsCurrent = true;
        }
    }

    /// <summary>Maps the upload's progress text to a phase and file name.</summary>
    private static void OnUploadProgress(string status)
    {
        if (status.StartsWith(UPLOADING_STATUS_PREFIX, StringComparison.Ordinal))
        {
            SetProgress(PHASE_UPLOADING, status.Substring(UPLOADING_STATUS_PREFIX.Length));
        }
        else if (status.StartsWith(SKIPPING_STATUS_PREFIX, StringComparison.Ordinal))
        {
            SetProgress(PHASE_UPLOADING, status.Substring(SKIPPING_STATUS_PREFIX.Length));
        }
        else if (status == SAVING_CHECKSUM_STATUS)
        {
            SetProgress(PHASE_UPLOADING, AmazonBucket.CHECKSUM_FILE);
        }
        else
        {
            SetProgress(PHASE_CHECKING, "");
        }
    }

    /// <summary>The pool-thread body: runs the dry run or the upload, and always finishes idle with a report.</summary>
    private static async Task ExecuteDeployAsync(DeployRun deployRun)
    {
        string statusReport;
        string fullReport;
        try
        {
            if (deployRun.Confirm)
            {
                statusReport = await ExecuteUploadAsync(deployRun);
                fullReport = statusReport;
            }
            else
            {
                Task<AmazonUploadPlan> planTask = AmazonBucket.PlanUploadAsync(deployRun.SourceFolder, deployRun.RemotePath, deployRun.BucketName, deployRun.AccessKey, deployRun.SecretKey, deployRun.Region, deployRun.AlwaysUpload);
                MarkCountsCurrent();
                AmazonUploadPlan uploadPlan = await planTask;
                statusReport = FormatPlanReport(deployRun, uploadPlan, REPORT_PREVIEW_ENTRIES);
                fullReport = FormatPlanReport(deployRun, uploadPlan, uploadPlan.Entries.Count);
            }
        }
        catch (Exception exception)
        {
            statusReport = FormatFailureReport(deployRun, exception);
            fullReport = statusReport;
        }
        FinishRun(deployRun, statusReport, fullReport);
    }

    /// <summary>Uploads, then invalidates CloudFront when configured and the upload succeeded.</summary>
    private static async Task<string> ExecuteUploadAsync(DeployRun deployRun)
    {
        Task<bool> uploadTask = AmazonBucket.UploadFolderAsync(deployRun.AlwaysUpload, deployRun.SourceFolder, deployRun.RemotePath, deployRun.BucketName, deployRun.AccessKey, deployRun.SecretKey, deployRun.Region,
            (string status, int fileNumber, int totalFiles, long sizeBytes) => OnUploadProgress(status));
        MarkCountsCurrent();
        bool uploaded = await uploadTask;
        string invalidation = await InvalidateAfterUploadAsync(deployRun, uploaded);
        return FormatUploadReport(deployRun, uploaded, invalidation);
    }

    /// <summary>Invalidates the CloudFront distribution after a successful upload.</summary>
    /// <returns>sent, failed, not configured, or skipped.</returns>
    private static async Task<string> InvalidateAfterUploadAsync(DeployRun deployRun, bool uploaded)
    {
        if (String.IsNullOrEmpty(deployRun.CloudFront))
        {
            return "not configured";
        }
        if (!uploaded)
        {
            return "skipped (the upload failed)";
        }
        SetProgress(PHASE_INVALIDATING, "");
        bool invalidated = await AmazonBucket.InvalidateCloudFrontAsync(deployRun.CloudFront, deployRun.AccessKey, deployRun.SecretKey, deployRun.Region);
        return invalidated ? "sent" : "failed (see the Unity console)";
    }

    /// <summary>The report lines every mode shares: mode, target, destination, source and the manifest check.</summary>
    private static void AppendReportHeader(StringBuilder report, DeployRun deployRun)
    {
        report.AppendLine("mode         : " + deployRun.Mode + (deployRun.AlwaysUpload ? " (always upload)" : ""));
        report.AppendLine("target       : \"" + deployRun.TargetName + "\"");
        report.AppendLine("destination  : " + deployRun.DestinationUrl);
        report.AppendLine("source       : " + deployRun.SourceFolder);
        report.AppendLine("manifest     : " + deployRun.ManifestSummary);
    }

    /// <summary>The dry-run report: totals, every content-manifest.json's place among the would-upload entries, and the first entries.</summary>
    private static string FormatPlanReport(DeployRun deployRun, AmazonUploadPlan uploadPlan, int entryCount)
    {
        StringBuilder report = new StringBuilder();
        AppendReportHeader(report, deployRun);
        report.AppendLine($"would upload : {uploadPlan.UploadCount} files, {UnityTools.FormatFileSize(uploadPlan.UploadBytes)} ({uploadPlan.UploadBytes} bytes)");
        report.AppendLine($"would skip   : {uploadPlan.SkipCount} files");
        foreach (string manifestLine in FormatManifestPositions(uploadPlan))
        {
            report.AppendLine(manifestLine);
        }
        int shownCount = Math.Min(entryCount, uploadPlan.Entries.Count);
        report.AppendLine($"entries      : {shownCount} of {uploadPlan.Entries.Count} in deploy order (then checksum.json)");
        foreach (AmazonUploadPlanEntry entry in uploadPlan.Entries.Take(shownCount))
        {
            string action = entry.WillUpload ? "upload" : "skip  ";
            report.AppendLine($"  {action}  {UnityTools.FormatFileSize(entry.SizeBytes),10}  {entry.Key}");
        }
        AppendFinishTime(report);
        return report.ToString().TrimEnd();
    }

    /// <summary>One line per content-manifest.json: its position among the would-upload entries, or that it is skipped.</summary>
    private static List<string> FormatManifestPositions(AmazonUploadPlan uploadPlan)
    {
        List<string> manifestLines = new List<string>();
        int uploadPosition = 0;
        foreach (AmazonUploadPlanEntry entry in uploadPlan.Entries)
        {
            if (entry.WillUpload)
            {
                uploadPosition++;
            }
            bool isManifest = Path.GetFileName(entry.Key).Equals(AmazonBucket.CONTENT_MANIFEST_FILE, StringComparison.OrdinalIgnoreCase);
            if (!isManifest)
            {
                continue;
            }
            string placement = entry.WillUpload
                ? $"would upload, position {uploadPosition} of {uploadPlan.UploadCount}" + (uploadPosition == uploadPlan.UploadCount ? " (last)" : "")
                : "would skip (unchanged)";
            manifestLines.Add($"{AmazonBucket.CONTENT_MANIFEST_FILE}: {placement} - {entry.Key}");
        }
        if (manifestLines.Count == 0)
        {
            manifestLines.Add(AmazonBucket.CONTENT_MANIFEST_FILE + ": not in the source folder");
        }
        return manifestLines;
    }

    /// <summary>The upload report: result, uploaded / skipped / errors from AmazonBucket's counters and the invalidation result.</summary>
    private static string FormatUploadReport(DeployRun deployRun, bool uploaded, string invalidation)
    {
        StringBuilder report = new StringBuilder();
        AppendReportHeader(report, deployRun);
        report.AppendLine("result       : " + (uploaded ? "succeeded" : "failed (see the Unity console)"));
        report.AppendLine($"uploaded     : {AmazonBucket.UploadedFileNum} of {AmazonBucket.TotalFileCount}");
        report.AppendLine($"skipped      : {AmazonBucket.SkippedFileNum}");
        report.AppendLine($"errors       : {AmazonBucket.TotalErrors}");
        report.AppendLine("invalidation : " + invalidation);
        AppendFinishTime(report);
        return report.ToString().TrimEnd();
    }

    /// <summary>The report of a deploy that threw (for example a failed bucket listing); the message is redacted by FinishRun.</summary>
    private static string FormatFailureReport(DeployRun deployRun, Exception exception)
    {
        StringBuilder report = new StringBuilder();
        AppendReportHeader(report, deployRun);
        report.AppendLine("result       : failed");
        report.AppendLine("errors       : 1");
        report.AppendLine("error        : " + exception.GetType().Name + ": " + exception.Message);
        AppendFinishTime(report);
        return report.ToString().TrimEnd();
    }

    /// <summary>Appends the local finish time.</summary>
    private static void AppendFinishTime(StringBuilder report)
    {
        report.AppendLine("finished     : " + DateTime.Now.ToString(TIME_FORMAT));
    }

    /// <summary>Writes the full report to Logs/BabylonToolkitDeploy.txt and publishes the status report, then marks the deploy idle.</summary>
    private static void FinishRun(DeployRun deployRun, string statusReport, string fullReport)
    {
        string redactedStatusReport = Redact(statusReport, deployRun.Secrets);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(deployRun.ReportFilePath));
            File.WriteAllText(deployRun.ReportFilePath, Redact(fullReport, deployRun.Secrets) + "\n");
        }
        catch (Exception exception)
        {
            redactedStatusReport += "\nreport file  : not written - " + Redact(exception.Message, deployRun.Secrets);
        }
        lock (StateLock)
        {
            lastReport = redactedStatusReport;
            deployRunning = false;
            currentPhase = "";
            currentFile = "";
        }
    }
}
#endif

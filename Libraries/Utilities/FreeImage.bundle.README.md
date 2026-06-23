# FreeImage.bundle — macOS native plugin (universal: x86_64 + arm64)

`FreeImage.bundle` is the native FreeImage library the Babylon Toolkit editor code
loads via P/Invoke (`FreeImageNet.dll` → `FreeImage.IsAvailable()` /
`UnityTools.ReadFreeImage` / `UnityTools.WriteFreeImage`). It is required for
high-bit-depth image work in the editor, notably **16-bit normal-map export** and
the atlas packer's 16-bit TIFF/PSD transcode. When it cannot load, every 16-bit
path silently falls back to 8-bit PNG (banding on smooth normals).

## Why this file exists

The originally shipped bundle was **`x86_64 + i386` only — no `arm64` slice**.
On an Apple-Silicon Mac the Unity 2022.3 editor runs as a native **arm64** process
and therefore **could not load** the bundle, so `FreeImage.IsAvailable()` returned
`false` and all 16-bit output came out 8-bit. (The arm64-only editor also can't be
relaunched under Rosetta, so matching the old x86_64 bundle that way isn't an option.)

This bundle has been rebuilt as a **universal `x86_64 + arm64`** binary so it loads
natively on both Intel and Apple-Silicon editors.

## Verify

```sh
lipo -archs "FreeImage.bundle/Contents/MacOS/FreeImage"
# expect: x86_64 arm64

codesign -dv "FreeImage.bundle/Contents/MacOS/FreeImage" 2>&1 | grep -i signature
# expect: Signature=adhoc
```

At runtime the editor logs `FreeImage=True` and `Wrote 16-bit PNG via FreeImage`
(see the `[NormalMap]` diagnostics in `WriteNormalMapImage` / `TranscodeSourceToPng`).

## How it was built (to regenerate)

- arm64 slice: Homebrew `freeimage` 3.18.0 →
  `/opt/homebrew/Cellar/freeimage/3.18.0/lib/libfreeimage.3.18.0.dylib`
  (deps are system-only: `libc++`, `libSystem`).
- x86_64 slice: kept from the original 2023 bundle binary (the dead i386 slice was dropped).

```sh
BUNDLE=".../Libraries/Utilities/FreeImage.bundle"
BIN="$BUNDLE/Contents/MacOS/FreeImage"
ARM="/opt/homebrew/Cellar/freeimage/3.18.0/lib/libfreeimage.3.18.0.dylib"

cp "$BIN" /tmp/FreeImage.orig.bin                 # backup
lipo "$BIN" -thin x86_64 -output /tmp/fi_x86_64   # keep existing Intel slice
lipo -create /tmp/fi_x86_64 "$ARM" -output /tmp/FreeImage.universal

# IMPORTANT: sign the Mach-O as a STANDALONE file (not inside the .bundle dir),
# otherwise codesign trips on Unity's .meta files ("unsealed contents in bundle root").
cp /tmp/FreeImage.universal /tmp/FreeImage.signed
codesign --force --sign - /tmp/FreeImage.signed
cp /tmp/FreeImage.signed "$BIN"

# Confirm an arm64 process can actually dlopen it:
#   clang -arch arm64 dlopen-test.c && ./test "$BIN"  ->  FreeImage_GetVersion resolves

# Update the fallback zip too (the toolkit auto-extracts it if the bundle dir is missing/empty):
#   zip -f FreeImage.bundle.zip FreeImage.bundle/Contents/MacOS/FreeImage
```

## Installing into another project

Copy these three into the other project's
`Packages/com.babylontoolkit.editor/Libraries/Utilities/` (overwrite the old ones),
then **restart Unity**:

- `FreeImage.bundle/`        (the whole folder — universal binary)
- `FreeImage.bundle.zip`     (kept in sync)
- `FreeImage.bundle.meta`    (preserves the plugin GUID + import settings)

Plugin import settings (`.meta`) must keep **Editor: enabled** with **no Intel-only
CPU restriction** (`Standalone: OSXUniversal / CPU: AnyCPU`).

## Notes

- The signature is **ad-hoc** (`-s -`). Fine for local use and files copied between
  your machines. If the package is distributed by **download** (web/registry tarball),
  macOS may quarantine it on other users' machines — if FreeImage reports unavailable
  there, run: `xattr -dr com.apple.quarantine FreeImage.bundle`.
- If `com.babylontoolkit.editor` is consumed via Package Manager (registry/git/tarball)
  rather than embedded, update the package **at its source** and bump the version —
  edits to the read-only `Library/PackageCache/` copy are wiped on reimport.
- Homebrew flags `freeimage` as deprecated (outstanding CVEs); irrelevant for offline
  conversion of trusted project textures, but worth tracking for a future replacement.

// Babylon Toolkit - Unity CLI command bridge
//
// Registers first-class `unity command bt_*` commands for the Unity CLI (UnityCLI, package
// `com.unity.pipeline`) so agents and scripts can export, serve and inspect a Babylon Toolkit
// project without shipping C# strings through `unity command eval`.
//
// This file ships inside the Babylon Toolkit editor package and is compiled by the
// `BabylonToolkit.Editor.CLI` assembly definition next to it. That asmdef carries a version
// define keyed on `com.unity.pipeline` (BT_UNITY_PIPELINE) and a matching define constraint,
// so the whole assembly is skipped in projects that do not have the Unity Pipeline package
// installed and activates automatically, on the next domain reload, once it is added.
//
// Canonical reference: Babylon Toolkit Agent Reference, "Unity Exporter - Command Line
// Interface", section 11 ("The production pattern - a [CliCommand] bridge").
//
// Project specific commands (test scaffolding, one-off automation) do NOT belong here.
// Put them in the project's own Assets/Editor folder; Assembly-CSharp-Editor already sees
// Unity.Pipeline because that assembly is auto referenced.
#if UNITY_EDITOR && BT_UNITY_PIPELINE
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Pipeline.Commands;   // [CliCommand] / [CliArg] - assembly Unity.Pipeline (com.unity.pipeline)

public static class BabylonToolkitCliCommands
{
    // Every export path must do this first.
    // NOTE: this covers the runtime static only. It does NOT replace the Scene Exporter panel bootstrap
    // (layers, FreeImage, shader list and root namespace still require the panel to have been opened
    // once in a GUI session, with the resulting ProjectSettings/ changes committed).
    private static void PrepareExporter()
    {
        // Dialogs are suppressed for the DURATION of this call and restored on the way out - see
        // SuppressDialogs below for why the scope matters.
        bool previous = CanvasTools.CanvasToolsExporter.SuppressDialogs;
        CanvasTools.CanvasToolsExporter.SuppressDialogs = true;
        try
        {
            CanvasToolsExporter_Init();
            CanvasToolsInfo.DefaultProjectFolder = UnityTools.GetDefaultExportFolder();
        }
        finally
        {
            CanvasTools.CanvasToolsExporter.SuppressDialogs = previous;
        }
    }

    /// <summary>
    /// BuildProject with modal dialogs suppressed for the duration of the call, and RESTORED on the way
    /// out - including when it throws.
    ///
    /// Nothing in this bridge can answer a dialog: EditorUtility.DisplayDialog blocks the main thread,
    /// which is the thread these commands run on, so a modal here is a HANG and not a message
    /// (shadergraph-transpiler F-T.126 - a failed build wedged the Editor until it was killed).
    ///
    /// The scope is the whole point, and getting it wrong was F-T.144: setting the flag once in
    /// PrepareExporter left it set for the REST OF THE EDITOR SESSION, because it is a static and nothing
    /// cleared it. The Editor these commands drive is the same one the user is clicking in, so from the
    /// first bt_* command onward their own Build button stopped asking for confirmation and every other
    /// dialog went silent. A headless caller's convenience must not outlive the headless call.
    /// </summary>
    private static void BuildProjectHeadless(EditorBuildType mode, Transform[] selection, string filename,
        string folder, bool animationMode, int handSystem, int meshSystem, bool metadata)
    {
        bool previous = CanvasTools.CanvasToolsExporter.SuppressDialogs;
        CanvasTools.CanvasToolsExporter.SuppressDialogs = true;
        try
        {
            CanvasTools.CanvasToolsExporter.BuildProject(
                mode, selection, filename, folder, animationMode, handSystem, meshSystem, metadata);
        }
        finally
        {
            CanvasTools.CanvasToolsExporter.SuppressDialogs = previous;
        }
    }

    private static void CanvasToolsExporter_Init()
    {
        // Validates the license and toolkit requirements; returns the ExportMetadata flag.
        CanvasTools.CanvasToolsExporter.Initialize();
    }

    // Package version of com.babylontoolkit.editor (the CanvasTools.dll assembly version is not bumped per release).
    private static string ToolkitPackageVersion()
    {
        var asm = typeof(CanvasTools.CanvasToolsExporter).Assembly;
        var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(asm);
        return pkg != null ? pkg.version : asm.GetName().Version.ToString();
    }

    /// unity command bt_status
    [CliCommand("bt_status", "Report Babylon Toolkit exporter readiness and output paths")]
    public static string Status()
    {
        var info = CanvasToolsInfo.Instance;
        return string.Join("\n", new[] {
            "unity      : " + Application.unityVersion,
            "toolkit    : " + ToolkitPackageVersion(),
            "scene      : " + EditorSceneManager.GetActiveScene().path,
            "pro        : " + ToolkitManager.IsPro(),
            "exportRoot : " + UnityTools.GetDefaultExportFolder(),
            "sceneDir   : " + info.DefaultScenePath,
            "sceneFmt   : " + info.ExportFileFormat,   // 0 = GLTF, 2 = GLB
            "prefabFmt  : " + info.PrefabFileFormat,
            "metadata   : " + info.ExportMetadata,
            "compiling  : " + EditorApplication.isCompiling,
            "baking     : " + Lightmapping.isRunning,
            "devserver  : " + WebServer.IsStarted,
        });
    }

    /// unity command bt_refresh [--force true]
    /// Re-imports changed assets. Use it after replacing a library in the package (for example a
    /// rebuilt CanvasTools.dll) so the Editor reloads it. A changed DLL triggers a domain reload,
    /// which drops the CLI bridge for a while; treat a lost connection right after this call as
    /// "not yet" and poll until `unity command eval 'return true;'` answers again.
    [CliCommand("bt_refresh", "Refresh the AssetDatabase so rebuilt Babylon Toolkit libraries are reloaded",
                MainThreadRequired = true)]
    public static string Refresh(
        [CliArg("force", "Force a synchronous re-import of every asset")] bool force = false)
    {
        if (EditorApplication.isCompiling) throw new Exception("Scripts are still compiling.");
        AssetDatabase.Refresh(force
            ? ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
            : ImportAssetOptions.Default);
        return "refreshed compiling=" + EditorApplication.isCompiling;
    }

    /// unity command bt_export_level --scene Assets/Scenes/Level01.unity --geometryOnly true
    [CliCommand("bt_export_level",
                "Export the active (or named) scene as a Babylon Toolkit game level",
                MainThreadRequired = true)]
    public static string ExportLevel(
        [CliArg("scene",        "Scene asset path to open first (optional)")] string scene = null,
        [CliArg("filename",     "Output name without extension (optional)")] string filename = null,
        [CliArg("folder",       "Absolute output folder (optional)")]        string folder = null,
        [CliArg("geometryOnly", "Skip script/web/PWA emit; export only the scene")] bool geometryOnly = true,
        [CliArg("compileScripts", "Compile Assets/**/*.ts into the project bundle even when geometryOnly")] bool compileScripts = false)
    {
        if (EditorApplication.isCompiling) throw new Exception("Scripts are still compiling.");
        if (Lightmapping.isRunning)        throw new Exception("A lightmap bake is in progress.");

        if (!string.IsNullOrWhiteSpace(scene))
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

        PrepareExporter();
        var info = CanvasToolsInfo.Instance;

        // BuildProject persists settings, so snapshot and restore what we toggle.
        bool compile = info.CompileProjectScript, web = info.BuildWebProject, pwa = info.ProgressiveWebApp;
        // shadergraph-transpiler D20: a geometry-only export still has to be able to compile the project
        // TypeScript, because the transpiler stage writes generated material classes that are worthless
        // until tsc turns them into the bundle. The web page and the PWA stay off either way, so this
        // never rewrites export/index.html.
        if (geometryOnly) { info.CompileProjectScript = compileScripts; info.BuildWebProject = false; info.ProgressiveWebApp = false; }
        try
        {
            // Automate == Project minus every modal dialog. This is not optional for agent work.
            BuildProjectHeadless(
                EditorBuildType.Automate, null, filename, folder, false,
                info.HandedExportSystem, info.MeshExportSystem, info.ExportMetadata);
        }
        finally
        {
            if (geometryOnly) { info.CompileProjectScript = compile; info.BuildWebProject = web; info.ProgressiveWebApp = pwa; }
        }

        // D18: a stage-01 TypeScript failure SKIPS stage 03, so without this the command would happily
        // return a path to a scene file that was never written. Thrown after the finally block, so the
        // settings snapshot is restored either way.
        ThrowIfBuildFailed();

        string root = string.IsNullOrWhiteSpace(folder) ? UnityTools.GetDefaultExportFolder() : folder;
        return Path.Combine(root, info.DefaultScenePath, CanvasTools.CanvasToolsExporter.SceneFilename ?? "");
    }

    /// unity command bt_export_prefab --paths "Props/Crate,Props/Barrel" --filename Crates --folder /abs/out
    [CliCommand("bt_export_prefab",
                "Export selected transforms as a Babylon Toolkit asset container (no scene metadata)",
                MainThreadRequired = true)]
    public static string ExportPrefab(
        [CliArg("paths",    "Comma-separated hierarchy paths, e.g. Props/Crate")] string paths = null,
        [CliArg("filename", "Output name without extension")]                     string filename = null,
        [CliArg("folder",   "Absolute output folder")]                            string folder = null,
        [CliArg("metadata", "Emit extras.metadata component data")]               bool metadata = true)
    {
        if (EditorApplication.isCompiling) throw new Exception("Scripts are still compiling.");

        Transform[] targets;
        if (string.IsNullOrWhiteSpace(paths))
        {
            targets = Selection.GetTransforms(SelectionMode.Editable);
        }
        else
        {
            var found = new List<Transform>();
            foreach (var p in paths.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                var go = GameObject.Find(p);
                if (go == null) throw new Exception("GameObject not found: " + p);
                found.Add(go.transform);
            }
            targets = found.ToArray();
        }
        if (targets == null || targets.Length == 0) throw new Exception("Nothing selected to export.");

        PrepareExporter();
        var info = CanvasToolsInfo.Instance;

        string outName = string.IsNullOrWhiteSpace(filename) ? targets[0].name : filename;
        string outDir  = string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(UnityTools.GetDefaultExportFolder(), info.DefaultScenePath)
            : folder;
        Directory.CreateDirectory(outDir);

        // selection != null  =>  ExportSelectionOnly = true  =>  NO skybox / IBL / fog / navmesh / physics.
        // Uses PrefabFileFormat, and writes straight into outDir (no "scenes" subfolder).
        BuildProjectHeadless(
            EditorBuildType.Scene, targets, outName, outDir, false,
            info.HandedExportSystem, info.MeshExportSystem, metadata);

        return Path.Combine(outDir, CanvasTools.CanvasToolsExporter.SceneFilename ?? outName);
    }

    /// unity command bt_export_animation --path Characters/Hero --filename HeroRun
    [CliCommand("bt_export_animation", "Export one animated transform as a .glb", MainThreadRequired = true)]
    public static string ExportAnimation(
        [CliArg("path",     "Hierarchy path of the animated root")] string path = null,
        [CliArg("filename", "Output name without extension")]       string filename = null,
        [CliArg("folder",   "Absolute output folder")]              string folder = null)
    {
        var go = GameObject.Find(path);
        if (go == null) throw new Exception("GameObject not found: " + path);

        PrepareExporter();
        var info = CanvasToolsInfo.Instance;
        string outDir = string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(UnityTools.GetDefaultExportFolder(), info.DefaultScenePath) : folder;
        Directory.CreateDirectory(outDir);

        // animationMode: true forces .glb and drops metadata.
        BuildProjectHeadless(
            EditorBuildType.Scene, new[] { go.transform },
            string.IsNullOrWhiteSpace(filename) ? go.name : filename,
            outDir, true, info.HandedExportSystem, 0, false);

        return Path.Combine(outDir, CanvasTools.CanvasToolsExporter.SceneFilename ?? "");
    }

    /// unity command bt_devserver_start   - start the Toolkit development web server
    [CliCommand("bt_devserver_start", "Start the Babylon Toolkit development web server",
                MainThreadRequired = true)]
    public static string StartDevServer(
        [CliArg("port", "HTTP port to serve on (default: keep current setting)")] int port = 0)
    {
        // The server refuses to start without an export root - same dependency as exporting.
        if (string.IsNullOrWhiteSpace(CanvasToolsInfo.DefaultProjectFolder))
            CanvasToolsInfo.DefaultProjectFolder = UnityTools.GetDefaultExportFolder();

        var info = CanvasToolsInfo.Instance;
        if (port > 0) { info.DefaultServerPort = port; CanvasToolsInfo.SaveSettings(); }

        // Only the InternalWebServer mode runs the built-in listener.
        info.HostPreviewType = (int)EditorHostingType.InternalWebServer;

        if (WebServer.IsStarted)
            return "already running: http://localhost:" + info.DefaultServerPort + "/ root=" + WebServer.Root;

        // Prefer StartDevelopmentServer() when this Toolkit build exposes it.
        var mi = typeof(CanvasTools.CanvasToolsExporter).GetMethod("StartDevelopmentServer",
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                     null, System.Type.EmptyTypes, null);
        if (mi != null) mi.Invoke(null, null);
        else UnityTools.StartWebServer(CanvasTools.CVPanel.RelativeHostPath);

        if (!WebServer.IsStarted) throw new Exception("Development server failed to start.");
        return "http://localhost:" + info.DefaultServerPort + "/ root=" + WebServer.Root;
    }

    /// unity command bt_devserver_status
    [CliCommand("bt_devserver_status", "Report Babylon Toolkit development web server state",
                MainThreadRequired = true)]
    public static string DevServerStatus()
    {
        var info = CanvasToolsInfo.Instance;
        return string.Join("\n", new[] {
            "started   : " + WebServer.IsStarted,
            "supported : " + WebServer.IsSupported,
            "root      : " + WebServer.Root,
            "port      : " + info.DefaultServerPort,
            "securePort: " + (info.EnableSecureSockets ? info.DefaultSecurePort.ToString() : "disabled"),
            "hosting   : " + ((EditorHostingType)info.HostPreviewType),
        });
    }

    /// unity command bt_build_project   - full web build (scripts + scene + web + PWA), no dialogs
    [CliCommand("bt_build_project", "Full Babylon Toolkit project build, headless", MainThreadRequired = true)]
    public static string BuildProjectFull()
    {
        PrepareExporter();
        var info = CanvasToolsInfo.Instance;
        BuildProjectHeadless(
            EditorBuildType.Automate, null, null, null, false,
            info.HandedExportSystem, info.MeshExportSystem, info.ExportMetadata);
        ThrowIfBuildFailed();
        return UnityTools.GetDefaultExportFolder();
    }

    /// <summary>
    /// D18: turn a silent stage-01 TypeScript failure into a failed command. BuildProject returns void and
    /// `if (buildResult == 0)` gates the scene export, so a tsc error otherwise looks exactly like success
    /// from the outside - the command returns a path and the file behind it is stale or missing.
    /// </summary>
    private static void ThrowIfBuildFailed()
    {
        int result = CanvasTools.CanvasToolsExporter.LastBuildResult;
        if (result != 0)
        {
            throw new Exception("TypeScript compile failed (exit " + result + ") - see Debug/tsc-errors.txt");
        }
    }
}
#endif

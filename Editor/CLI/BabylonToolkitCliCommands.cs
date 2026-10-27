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
using Unity.Pipeline.Commands;   // [CliCommand] / [CliArg] - assembly Unity.Pipeline.Attributes (com.unity.pipeline 0.8+)

public static class BabylonToolkitCliCommands
{
    // Every export path must do this first.
    // NOTE: this covers the runtime static only. It does NOT replace the Scene Exporter panel bootstrap
    // (layers, FreeImage, shader list and root namespace come from CVPanel.OnEnable: open the panel once,
    // or run its headless equivalent - the UnityTools.Validate* calls - through eval / run_script, then
    // commit the resulting ProjectSettings/ changes).
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
            CanvasTools.CanvasToolsExporter.LastBuildResult = -1;
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
            "sceneFmt   : " + info.ExportFileFormat,   // EditorExportFormat: 0 = GLTF, 1 = GLB
            "prefabFmt  : " + info.PrefabFileFormat,   // EditorExportFormat: 0 = GLTF, 1 = GLB
            "metadata   : " + info.ExportMetadata,
            "compiling  : " + EditorApplication.isCompiling,
            "baking     : " + Lightmapping.isRunning,
            "devserver  : " + WebServer.IsStarted,
            "automation : " + (UnityTools.AutomatedEnabled ? "on until " + UnityTools.AutomationExpiresUtc.ToString("o") : "off"),
        });
    }

    /// unity command bt_automation [--grant <signed grant from the App Builder>]
    [CliCommand("bt_automation", "Enable App Builder automation mode with a signed grant, or report it",
                MainThreadRequired = true)]
    public static string Automation([CliArg("grant", "Signed grant from the App Builder (omit to report)")] string grant = null)
    {
        if (!string.IsNullOrWhiteSpace(grant))
        {
            string reason = UnityTools.EnableAutomation(grant);
            if (reason.Length > 0) throw new Exception("Automation grant refused: " + reason);
        }
        return UnityTools.AutomatedEnabled
            ? "automation : on until " + UnityTools.AutomationExpiresUtc.ToString("o")
            : "automation : off";
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
        [CliArg("compileScripts", "Compile Assets/**/*.ts into the project bundle even when geometryOnly (default true: generated Shader Graph classes must be in the bundle)")] bool compileScripts = true,
        [CliArg("save", "Save open scenes with unsaved changes before opening --scene (otherwise refuse)")] bool save = false)
    {
        if (EditorApplication.isCompiling) throw new Exception("Scripts are still compiling.");
        if (Lightmapping.isRunning)        throw new Exception("A lightmap bake is in progress.");

        if (!string.IsNullOrWhiteSpace(scene)) OpenSceneWithoutDiscardingChanges(scene, save, "Save them (save_all) or pass --save true.");

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

    /// unity command bt_bake_timelines --scene Assets/Scenes/Level01.unity --force false
    /// <summary>
    /// Bakes (or loads from the cache) every PlayableDirector Timeline in the open or named scene, with no export, and
    /// returns the bake report (unity-timeline-playback DR-9.7). Fresh bakes reload the scenes from disk, so they never
    /// leave the scene dirty.
    /// </summary>
    [CliCommand("bt_bake_timelines",
                "Bake (or load from cache) every PlayableDirector Timeline in the open (or named) scene and return the bake report",
                MainThreadRequired = true)]
    public static string BakeTimelines(
        [CliArg("scene", "Scene asset path to open first (optional)")] string scene = null,
        [CliArg("force", "Ignore the bake cache")] bool force = false)
    {
        if (EditorApplication.isCompiling) throw new Exception("Scripts are still compiling.");
        if (Lightmapping.isRunning)        throw new Exception("A lightmap bake is in progress.");

        if (!string.IsNullOrWhiteSpace(scene)) OpenSceneWithoutDiscardingChanges(scene, false, "Save them (save_all) first.");
        return CanvasTools.TimelineExportTool.BakeOpenScenes(force);
    }

    /// <summary>
    /// Opens a scene in place of the open ones without silently losing work: when any open scene has unsaved changes it
    /// saves them first (saveDirtyScenes) or refuses with a message ending in refusalAdvice.
    /// </summary>
    /// <param name="scene">Scene asset path to open.</param>
    /// <param name="saveDirtyScenes">Save unsaved scenes instead of refusing.</param>
    /// <param name="refusalAdvice">The sentence that tells the caller how to proceed.</param>
    /// <exception cref="Exception">An open scene has unsaved changes and saveDirtyScenes is false.</exception>
    private static void OpenSceneWithoutDiscardingChanges(string scene, bool saveDirtyScenes, string refusalAdvice)
    {
        var dirtyScenes = new List<string>();
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var openScene = EditorSceneManager.GetSceneAt(i);
            if (openScene.isDirty) dirtyScenes.Add(string.IsNullOrEmpty(openScene.path) ? openScene.name : openScene.path);
        }
        if (dirtyScenes.Count > 0)
        {
            if (!saveDirtyScenes) throw new Exception("Unity has unsaved changes in " + string.Join(", ", dirtyScenes) + ". Opening " + scene + " would discard them. " + refusalAdvice);
            EditorSceneManager.SaveOpenScenes();
        }
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
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
        ThrowIfBuildFailed();

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
        ThrowIfBuildFailed();

        return Path.Combine(outDir, CanvasTools.CanvasToolsExporter.SceneFilename ?? "");
    }

    /// unity command bt_devserver_start   - start the Toolkit development web server
    [CliCommand("bt_devserver_start", "Start the Babylon Toolkit development web server",
                MainThreadRequired = true)]
    public static string StartDevServer(
        [CliArg("port", "HTTP port to serve on (default: keep current setting)")] int port = 0,
        [CliArg("auto", "Pick the first free port from 8888 to 8899 and save it")] bool auto = false)
    {
        // The server refuses to start without an export root - same dependency as exporting.
        if (string.IsNullOrWhiteSpace(CanvasToolsInfo.DefaultProjectFolder))
            CanvasToolsInfo.DefaultProjectFolder = UnityTools.GetDefaultExportFolder();

        var info = CanvasToolsInfo.Instance;
        info.HostPreviewType = (int)EditorHostingType.InternalWebServer;
        string scheme = info.EnableSecureSockets ? "https" : "http";
        // LAN serving (the default) may report the alias; loopback-only is reachable ONLY as localhost.
        string alias = UnityTools.ReadServerAliasName();
        string host = info.ServerListenAllInterfaces && !string.IsNullOrWhiteSpace(alias) ? alias : "localhost";
        if (WebServer.IsStarted)
            return "already running: " + scheme + "://" + host + ":" + (info.EnableSecureSockets ? info.DefaultSecurePort : info.DefaultServerPort) + "/ root=" + WebServer.Root;
        if (auto)
        {
            int free = FirstFreePort(8888, 8899);
            if (free < 0) throw new Exception("No free port between 8888 and 8899 — close another Unity Editor's dev server.");
            port = free;
        }
        if (port > 0) { info.DefaultServerPort = port; CanvasToolsInfo.SaveSettings(); }
        var mi = typeof(CanvasTools.CanvasToolsExporter).GetMethod("StartDevelopmentServer",
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, System.Type.EmptyTypes, null);
        if (mi != null) mi.Invoke(null, null);
        else UnityTools.StartWebServer(CanvasTools.CVPanel.RelativeHostPath);
        if (!WebServer.IsStarted)
            throw new Exception("Development server failed to start on port " + info.DefaultServerPort + ": " + (WebServer.LastError ?? "unknown error") +
                                ". Another Unity Editor may already be serving it — run bt_devserver_start --auto true.");
        return scheme + "://" + host + ":" + (info.EnableSecureSockets ? info.DefaultSecurePort : info.DefaultServerPort) + "/ root=" + WebServer.Root;
    }

    /// <summary>
    /// D29: the first port in [from, to] that nothing on this machine is bound to, or -1.
    /// A loopback bind alone is NOT a free-port test on macOS/Linux: Mono sets SO_REUSEADDR on listeners, so
    /// 127.0.0.1:P binds happily beside another Editor's `*:P` dev server (measured: it returned the very port
    /// the server held). So a port counts as taken when anything ANSWERS on it, and must also bind on all
    /// interfaces (which the default LAN-serving listener needs).
    /// </summary>
    public static int FirstFreePort(int from, int to)
    {
        for (int p = from; p <= to; p++)
        {
            if (PortAnswers(p)) continue;
            try { var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, p); l.Start(); l.Stop(); return p; }
            catch { }
        }
        return -1;
    }

    private static bool PortAnswers(int port)
    {
        try
        {
            using (var client = new System.Net.Sockets.TcpClient())
            {
                var pending = client.BeginConnect(System.Net.IPAddress.Loopback, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(250)) return false;
                client.EndConnect(pending);
                return true;
            }
        }
        catch { return false; }
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
            "project   : " + Application.productName,
            "listen    : " + (info.ServerListenAllInterfaces ? "all" : "loopback"),
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

    /// unity command bt_build_game_assets [--only scripts,containers,levels,<file names>]
    /// Starts Build All Game Assets (Editor/GameAssets/GameAssetBuilder.cs) from the project's
    /// Assets/[Config]/assets.json and returns at once: an export can make Unity compile, so the build runs one
    /// asset per editor update. Follow it with bt_build_game_assets_status.
    [CliCommand("bt_build_game_assets",
                "Start building every game asset in Assets/[Config]/assets.json (scripts, asset containers, game levels)",
                MainThreadRequired = true)]
    public static string BuildGameAssets(
        [CliArg("only", "Comma-separated groups (scripts, containers, levels) or output file names; empty = all")] string only = null)
    {
        return GameAssetBuilder.StartWithoutDialogs(only);
    }

    /// unity command bt_build_game_assets_status
    /// `running <done>/<total> <next asset>` while the build runs, then `idle` followed by its report
    /// (also in Logs/BuildAllGameAssets.txt; an asset that failed is listed as FAIL).
    [CliCommand("bt_build_game_assets_status",
                "Report Build All Game Assets: running with progress, or idle with the last report",
                MainThreadRequired = true)]
    public static string BuildGameAssetsStatus()
    {
        return GameAssetBuilder.GetStatus();
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
            if (CanvasTools.CanvasToolsExporter.LastBuildResult == -1) throw new Exception("The export did not run — the exporter stopped before building (licence, compiling, baking, or missing project folder). Check the Unity console.");
            throw new Exception("TypeScript compile failed (exit " + result + ") - see Debug/tsc-errors.txt");
        }
    }
}
#endif

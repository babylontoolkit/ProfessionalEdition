#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/**
 * Build All Game Assets: builds every asset a game loads into the project's export folder, from one list —
 * the project script bundle, the asset containers (scene roots exported together) and the game levels (whole scenes).
 * The list is the project's Assets/[Config]/assets.json (Tools ▸ Babylon Toolkit ▸ Generate Assets ▸ Edit Game Asset List
 * creates it). Compression (Draco, KTX2, gzip) and file naming come from the project's exporter settings.
 *
 * Nothing but Unity is needed to run it:
 * - the menu Tools ▸ Babylon Toolkit ▸ Generate Assets ▸ Build All Game Assets (and one item per group);
 * - the Unity CLI against an open editor: `unity command bt_build_game_assets [--only <selection>]`, then
 *   `bt_build_game_assets_status` until it reports idle;
 * - a batch-mode editor when Unity is closed: `-batchmode -projectPath <project> -executeMethod
 *   GameAssetBuilder.BuildFromCommandLine [-only <selection>]`, which exits when the build is done (0 = every asset built).
 *
 * An export can make Unity compile scripts (it refreshes the asset database, and generated material classes change),
 * and the exporter refuses to run while a compile is in progress. So the build is a queue that exports one asset per
 * editor update, waits whenever Unity is compiling, and survives the domain reload that follows a compile.
 */
[InitializeOnLoad]
public static class GameAssetBuilder
{
    /** Root menu path of the game asset commands: Tools ▸ Babylon Toolkit ▸ Generate Assets. */
    private const string MENU_ROOT = "Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Generate Assets/";

    /**
     * Menu priority of the Generate Assets submenu (its first item): right after Export Animation (2) with a divider
     * before it, and a divider before Geometry Tools (41) after it — Unity draws a divider only where neighbouring
     * priorities differ by more than 10, so every item of the submenu stays between 13 and 30.
     */
    private const int BUILD_ALL_MENU_PRIORITY = 15;

    /** Menu priority of Edit Game Asset List: more than 10 after Build Selected Assets, so a divider separates it from the build items. */
    private const int EDIT_LIST_MENU_PRIORITY = BUILD_ALL_MENU_PRIORITY + 14;

    /** Title of the progress bar and the result dialog. */
    private const string BUILD_TITLE = "Build All Game Assets";

    /** The project's list of game assets. */
    public const string MANIFEST_PATH = "Assets/[Config]/assets.json";

    /** Where the last build report is written. */
    public const string REPORT_PATH = "Logs/BuildAllGameAssets.txt";

    /** Session key that holds the running build, so it survives domain reloads. */
    private const string STATE_KEY = "GameAssetBuilder.State";

    /** How many times one asset is retried when the exporter refused it because a compile had just started. */
    private const int MAXIMUM_COMPILE_RETRIES = 20;

    /** Group name of the project script bundle. */
    private const string SCRIPTS_GROUP = "scripts";

    /** Group name of the asset containers. */
    private const string CONTAINERS_GROUP = "containers";

    /** Group name of the game levels. */
    private const string LEVELS_GROUP = "levels";

    /** Selection token that means every asset. */
    private const string ALL_SELECTION = "all";

    /** Build-queue name of the script bundle when the list does not name its file. */
    private const string SCRIPTS_QUEUE_NAME = "scripts";

    /** The project's list of game assets, as Assets/[Config]/assets.json holds it. */
    [Serializable]
    private sealed class GameAssetManifest
    {
        /** True to compile the project's TypeScript into the script bundle first. */
        public bool buildScripts = true;

        /** File name of the script bundle without extension, to check it was written; empty skips the check. */
        public string scriptBundle = "";

        /** True to refuse a build whose exporter settings do not force lowercase file names. */
        public bool requireLowercaseNames;

        /** True to refuse a build whose exporter settings do not write .gltf for scenes and containers. */
        public bool requireGltf;

        /** Asset containers, built in this order. */
        public ContainerEntry[] containers = Array.Empty<ContainerEntry>();

        /** Game levels, built in this order after the containers. */
        public LevelEntry[] levels = Array.Empty<LevelEntry>();
    }

    /** One asset container: roots of a scene exported together. */
    [Serializable]
    private sealed class ContainerEntry
    {
        /** Output file name without extension. */
        public string fileName;

        /** Scene asset path that holds the roots. */
        public string scene;

        /** Names of the scene roots exported together. */
        public string[] roots = Array.Empty<string>();

        /** Optional prefab asset path that re-creates a single missing root in the scene (saved); empty for none. */
        public string prefab = "";
    }

    /** One game level: a whole scene. */
    [Serializable]
    private sealed class LevelEntry
    {
        /** Output file name without extension. */
        public string fileName;

        /** Scene asset path. */
        public string scene;
    }

    /** What kind of export an asset needs. */
    private enum AssetKind { Scripts, Container, Level }

    /** One queued asset, resolved from the list. */
    private sealed class GameAsset
    {
        /** How the asset is exported. */
        public AssetKind Kind;

        /** Queue name: the output file name without extension (or "scripts" for an unnamed bundle). */
        public string Name;

        /** File name to check on disk, without extension; empty skips the check. */
        public string OutputFileName;

        /** Scene the asset is exported from. */
        public string ScenePath;

        /** Container only: the scene roots exported together. */
        public string[] RootNames = Array.Empty<string>();

        /** Container only: the prefab that re-creates a missing root, or empty. */
        public string PrefabPath = "";

        /** The selection group the asset belongs to. */
        public string Group => Kind == AssetKind.Scripts ? SCRIPTS_GROUP : Kind == AssetKind.Container ? CONTAINERS_GROUP : LEVELS_GROUP;
    }

    /** The outcome of building one asset, kept in the build state. */
    [Serializable]
    private sealed class AssetResult
    {
        /** Queue name of the asset. */
        public string name;

        /** True when the export ran and its file is on disk, newer than the build start. */
        public bool succeeded;

        /** Seconds the export took. */
        public double seconds;

        /** What was written (size and compressed twin), or why it failed. */
        public string detail;
    }

    /** The running build, stored in the editor session so it continues after a domain reload. */
    [Serializable]
    private sealed class BuildState
    {
        /** Assets still to build, by queue name, in order. */
        public List<string> pendingNames = new List<string>();

        /** Assets built so far. */
        public List<AssetResult> results = new List<AssetResult>();

        /** Number of assets selected. */
        public int totalCount;

        /** Retries of the current asset after compile refusals. */
        public int compileRetries;

        /** When the build started, as DateTime ticks. */
        public long startTicks;

        /** True when started from the menu: shows the result dialog at the end. */
        public bool showResultDialog;

        /** True when started by the batch-mode command line: quits the editor at the end. */
        public bool exitWhenDone;

        /** Scenes open before the build, restored at the end. */
        public List<string> openScenePaths = new List<string>();

        /** Which of those scenes were loaded. */
        public List<bool> openScenesLoaded = new List<bool>();

        /** Which of those scenes was active. */
        public List<bool> openScenesActive = new List<bool>();
    }

    /** Resumes a build that a domain reload interrupted. */
    static GameAssetBuilder()
    {
        if (LoadState() != null) ScheduleTicks();
    }

    /** Builds every game asset from the editor menu. */
    [MenuItem(MENU_ROOT + "Build All Game Assets", false, BUILD_ALL_MENU_PRIORITY)]
    private static void BuildAllFromMenu() => StartFromMenu(ALL_SELECTION);

    /** Builds only the project script bundle from the editor menu. */
    [MenuItem(MENU_ROOT + "Build Selected Assets/Scripts", false, BUILD_ALL_MENU_PRIORITY + 1)]
    private static void BuildScriptsFromMenu() => StartFromMenu(SCRIPTS_GROUP);

    /** Builds only the asset containers from the editor menu. */
    [MenuItem(MENU_ROOT + "Build Selected Assets/Asset Containers", false, BUILD_ALL_MENU_PRIORITY + 3)]
    private static void BuildContainersFromMenu() => StartFromMenu(CONTAINERS_GROUP);

    /** Builds only the game levels from the editor menu. */
    [MenuItem(MENU_ROOT + "Build Selected Assets/Game Levels", false, BUILD_ALL_MENU_PRIORITY + 2)]
    private static void BuildLevelsFromMenu() => StartFromMenu(LEVELS_GROUP);

    /** Selects the game asset list in the Project window, creating an empty one when the project has none. */
    [MenuItem(MENU_ROOT + "Edit Game Asset List", false, EDIT_LIST_MENU_PRIORITY)]
    private static void SelectManifestFromMenu()
    {
        if (!File.Exists(MANIFEST_PATH))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MANIFEST_PATH));
            File.WriteAllText(MANIFEST_PATH, JsonUtility.ToJson(new GameAssetManifest(), true));
            AssetDatabase.ImportAsset(MANIFEST_PATH);
        }
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(MANIFEST_PATH);
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    /**
     * Starts a build in this editor without any dialog and returns at once; the build then runs one asset per editor update.
     * Used by the Unity CLI command `bt_build_game_assets`.
     * @param only Comma-separated groups (scripts, containers, levels) or output file names; empty builds everything.
     * @returns What was queued.
     * @throws Exception The build cannot start (see StartBuild).
     */
    public static string StartWithoutDialogs(string only) => StartBuild(only, false, false, false);

    /**
     * Reports the build: `running <done>/<total> <next asset>`, or `idle` followed by the last report.
     * Used by the Unity CLI command `bt_build_game_assets_status`.
     * @returns The status.
     */
    public static string GetStatus()
    {
        BuildState state = LoadState();
        if (state != null) return $"running {state.results.Count}/{state.totalCount} {state.pendingNames.FirstOrDefault()}";
        return "idle\n" + (File.Exists(REPORT_PATH) ? File.ReadAllText(REPORT_PATH) : "");
    }

    /**
     * Batch-mode entry for `-executeMethod GameAssetBuilder.BuildFromCommandLine [-only <selection>]`.
     * Starts the build and keeps the editor running; the editor exits with 0 when every asset was built, 1 otherwise.
     */
    public static void BuildFromCommandLine()
    {
        try
        {
            Debug.Log("[GameAssetBuilder] " + StartBuild(ReadCommandLineOption("-only"), false, false, true));
        }
        catch (Exception error)
        {
            Debug.LogError("[GameAssetBuilder] " + error.Message);
            EditorApplication.Exit(1);
        }
    }

    /**
     * Starts a build from a menu item once the user confirms it, showing a refusal in a dialog.
     * @param selection The group to build, or "all".
     */
    private static void StartFromMenu(string selection)
    {
        if (!EditorUtility.DisplayDialog(BUILD_TITLE, "Are you sure you want to continue?", "Build", "Cancel")) return;
        try
        {
            StartBuild(selection, true, true, false);
        }
        catch (Exception error)
        {
            Debug.LogError("[GameAssetBuilder] " + error.Message);
            EditorUtility.DisplayDialog(BUILD_TITLE, error.Message, "OK");
        }
    }

    /**
     * Checks the editor can build, queues the selected assets and starts exporting them one per editor update.
     * @param selection Comma-separated groups or file names; null, empty or "all" selects everything.
     * @param askToSaveScenes True from the menu: asks the user to save modified scenes. Otherwise unsaved changes refuse the build.
     * @param showResultDialog True to show the report in a dialog at the end.
     * @param exitWhenDone True to quit the editor at the end (batch mode).
     * @returns What was queued.
     * @throws Exception A build is already running, the editor is busy, the list or settings are wrong, the selection is unknown, or scenes are unsaved.
     */
    private static string StartBuild(string selection, bool askToSaveScenes, bool showResultDialog, bool exitWhenDone)
    {
        if (LoadState() != null) throw new Exception("A game asset build is already running.");
        ThrowIfEditorBusy();
        GameAssetManifest manifest = LoadManifest();
        ThrowIfNamingIsNotRequired(manifest);
        List<GameAsset> assets = SelectAssets(CreateQueue(manifest), selection);
        if (!PrepareOpenScenes(askToSaveScenes)) throw new Exception("Build cancelled: the modified scenes were not saved.");

        var state = new BuildState
        {
            pendingNames = assets.Select(asset => asset.Name).ToList(),
            totalCount = assets.Count,
            startTicks = DateTime.Now.Ticks,
            showResultDialog = showResultDialog,
            exitWhenDone = exitWhenDone,
        };
        RememberOpenScenes(state);
        if (File.Exists(REPORT_PATH)) File.Delete(REPORT_PATH);
        SaveState(state);
        ScheduleTicks();
        return $"started: {assets.Count} assets ({string.Join(", ", state.pendingNames)})";
    }

    /** Subscribes the build step to the editor update loop, once. */
    private static void ScheduleTicks()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    /**
     * One build step per editor update: waits while Unity compiles or imports, otherwise exports the next asset.
     * An asset the exporter refused because a compile had just started is retried on a later update.
     */
    private static void Tick()
    {
        BuildState state = LoadState();
        if (state == null)
        {
            EditorApplication.update -= Tick;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (state.pendingNames.Count == 0)
        {
            Finish(state);
            return;
        }

        GameAsset asset = CreateQueue(LoadManifest()).FirstOrDefault(candidate => candidate.Name == state.pendingNames[0]);
        AssetResult result;
        if (asset == null)
        {
            result = new AssetResult { name = state.pendingNames[0], detail = $"no longer in {MANIFEST_PATH}" };
        }
        else
        {
            ShowProgress(state, asset.Name);
            Debug.Log($"[GameAssetBuilder] {state.results.Count + 1}/{state.totalCount} building {asset.Name} ({asset.Group})");
            result = BuildAsset(asset, new DateTime(state.startTicks));
            bool refusedForCompile = !result.succeeded && CanvasTools.CanvasToolsExporter.LastBuildResult == -1 && EditorApplication.isCompiling;
            if (refusedForCompile && state.compileRetries < MAXIMUM_COMPILE_RETRIES)
            {
                state.compileRetries++;
                Debug.Log($"[GameAssetBuilder] {asset.Name}: Unity started compiling — retrying once it finishes");
                SaveState(state);
                return;
            }
        }
        state.compileRetries = 0;
        state.results.Add(result);
        state.pendingNames.RemoveAt(0);
        SaveState(state);
    }

    /**
     * Ends the build: restores the scenes, writes the report, and shows it, or quits in batch mode.
     * @param state The finished build.
     */
    private static void Finish(BuildState state)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseString(STATE_KEY);
        if (!Application.isBatchMode) EditorUtility.ClearProgressBar();
        RestoreOpenScenes(state);

        string report = FormatReport(state);
        Debug.Log("[GameAssetBuilder] " + report);
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT_PATH));
        File.WriteAllText(REPORT_PATH, report);

        bool allSucceeded = state.results.All(result => result.succeeded);
        if (state.showResultDialog) EditorUtility.DisplayDialog(BUILD_TITLE, report, "OK");
        if (state.exitWhenDone) EditorApplication.Exit(allSucceeded ? 0 : 1);
    }

    /**
     * Builds one asset and checks its file reached the export folder.
     * @param asset The asset.
     * @param buildStart When the build began; the output must be newer.
     * @returns The outcome.
     */
    private static AssetResult BuildAsset(GameAsset asset, DateTime buildStart)
    {
        var result = new AssetResult { name = asset.Name };
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            PrepareExporter();
            if (asset.Kind == AssetKind.Scripts) BuildScripts();
            else if (asset.Kind == AssetKind.Container) ExportContainer(asset);
            else ExportLevel(asset);
            ThrowIfBuildFailed();
            result.detail = DescribeOutput(asset, buildStart);
            result.succeeded = true;
        }
        catch (Exception error)
        {
            result.detail = error.Message;
        }
        result.seconds = stopwatch.Elapsed.TotalSeconds;
        return result;
    }

    /** Compiles the project's TypeScript into the script bundle, with no scene export. */
    private static void BuildScripts()
    {
        RunExporter(() => CanvasTools.CanvasToolsExporter.BuildProject(EditorBuildType.Script, null, null, null));
    }

    /**
     * Exports one asset container: its roots in its scene, selected together, as one container file.
     * @param asset The container asset.
     */
    private static void ExportContainer(GameAsset asset)
    {
        Scene containerScene = OpenSceneIfNeeded(asset.ScenePath);
        string recreatingPrefab = asset.RootNames.Length == 1 ? asset.PrefabPath : "";
        Transform[] targets = asset.RootNames.Select(rootName => FindOrCreateRoot(containerScene, rootName, recreatingPrefab)).ToArray();
        CanvasToolsInfo settings = CanvasToolsInfo.Instance;
        RunExporter(() => CanvasTools.CanvasToolsExporter.BuildProject(
            EditorBuildType.Scene, targets, asset.Name, ScenesFolder(), false, settings.HandedExportSystem, settings.MeshExportSystem, true));
    }

    /**
     * Exports one scene as a game level: scene and script bundle, without the web page or the PWA.
     * @param asset The level asset.
     */
    private static void ExportLevel(GameAsset asset)
    {
        OpenSceneIfNeeded(asset.ScenePath);
        CanvasToolsInfo settings = CanvasToolsInfo.Instance;
        bool compileScripts = settings.CompileProjectScript;
        bool buildWebProject = settings.BuildWebProject;
        bool progressiveWebApp = settings.ProgressiveWebApp;
        // The level still compiles the project TypeScript: generated Shader Graph material classes only work once they are in the bundle.
        settings.CompileProjectScript = true;
        settings.BuildWebProject = false;
        settings.ProgressiveWebApp = false;
        try
        {
            RunExporter(() => CanvasTools.CanvasToolsExporter.BuildProject(
                EditorBuildType.Automate, null, asset.Name, null, false, settings.HandedExportSystem, settings.MeshExportSystem, settings.ExportMetadata));
        }
        finally
        {
            settings.CompileProjectScript = compileScripts;
            settings.BuildWebProject = buildWebProject;
            settings.ProgressiveWebApp = progressiveWebApp;
        }
    }

    /**
     * Runs one exporter call with its modal dialogs suppressed (nothing here can answer one), restoring the flag after.
     * @param build The exporter call.
     */
    private static void RunExporter(Action build)
    {
        bool suppressDialogs = CanvasTools.CanvasToolsExporter.SuppressDialogs;
        CanvasTools.CanvasToolsExporter.SuppressDialogs = true;
        try
        {
            CanvasTools.CanvasToolsExporter.LastBuildResult = -1;
            build();
        }
        finally
        {
            CanvasTools.CanvasToolsExporter.SuppressDialogs = suppressDialogs;
        }
    }

    /**
     * Finds a root in a container scene. A missing root with a re-creating prefab is added from the prefab at the
     * origin and the scene is saved, so the scene keeps every root the build needs.
     * @param containerScene The open container scene.
     * @param rootName The root's name.
     * @param prefabPath The prefab that re-creates it, or empty.
     * @returns The root's transform.
     * @throws Exception The root is missing and cannot be re-created.
     */
    private static Transform FindOrCreateRoot(Scene containerScene, string rootName, string prefabPath)
    {
        GameObject root = containerScene.GetRootGameObjects().FirstOrDefault(candidate => candidate.name == rootName);
        if (root != null) return root.transform;
        if (string.IsNullOrEmpty(prefabPath)) throw new Exception($"{containerScene.path} has no root named {rootName}.");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new Exception($"{rootName}: prefab {prefabPath} is missing.");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, containerScene);
        instance.name = rootName;
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        EditorSceneManager.SaveScene(containerScene);
        Debug.Log($"[GameAssetBuilder] added {rootName} to {containerScene.path} from {prefabPath}");
        return instance.transform;
    }

    /**
     * Opens a scene as the only scene unless it already is.
     * @param scenePath The scene asset path.
     * @returns The open scene.
     */
    private static Scene OpenSceneIfNeeded(string scenePath)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == scenePath && EditorSceneManager.sceneCount == 1) return activeScene;
        return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    }

    /**
     * Describes what an asset wrote: its file size and whether the gzip twin is there. When the export removed the
     * uncompressed file (Compress Scenes on, KeepUncompressedFiles off) the gzip twin is described instead.
     * @param asset The asset.
     * @param buildStart The build start; an older file means the export did not write it.
     * @returns One line for the report.
     * @throws Exception Neither the file nor its gzip twin is there, or the one there was not rewritten by this build.
     */
    private static string DescribeOutput(GameAsset asset, DateTime buildStart)
    {
        if (string.IsNullOrEmpty(asset.OutputFileName)) return "built";
        CanvasToolsInfo settings = CanvasToolsInfo.Instance;
        string fileName = settings.ExportCaseMode == (int)EditorCasingMode.ForceLowerCasing ? asset.OutputFileName.ToLowerInvariant() : asset.OutputFileName;
        string extension = ExtensionOf(asset, settings);
        string outputPath = Path.Combine(ScenesFolder(), fileName + extension);
        string compressedTwin = Path.Combine(ScenesFolder(), fileName + ".gz" + extension);
        bool hasUncompressedFile = File.Exists(outputPath);
        bool hasCompressedTwin = asset.Kind != AssetKind.Scripts && File.Exists(compressedTwin);
        if (!hasUncompressedFile && !hasCompressedTwin) throw new Exception($"the export reported success but {outputPath} is missing");
        if (!hasUncompressedFile) return DescribeFreshFile(compressedTwin, buildStart) + " (uncompressed removed)";

        string description = DescribeFreshFile(outputPath, buildStart);
        if (asset.Kind == AssetKind.Scripts) return description;
        return description + (hasCompressedTwin ? " + gzip twin" : "");
    }

    /**
     * Describes one written file by its name and size, after checking this build wrote it.
     * @param filePath The file on disk.
     * @param buildStart The build start; an older file means the export did not write it.
     * @returns The file name and its formatted size.
     * @throws Exception The file was not rewritten by this build.
     */
    private static string DescribeFreshFile(string filePath, DateTime buildStart)
    {
        var output = new FileInfo(filePath);
        if (output.LastWriteTime < buildStart) throw new Exception($"{output.Name} was not rewritten by this build");
        return $"{output.Name} {FormatSize(output.Length)}";
    }

    /**
     * The file extension an asset's export gets under the current settings.
     * @param asset The asset.
     * @param settings The exporter settings.
     * @returns ".js", ".gltf" or ".glb".
     */
    private static string ExtensionOf(GameAsset asset, CanvasToolsInfo settings)
    {
        if (asset.Kind == AssetKind.Scripts) return ".js";
        int fileFormat = asset.Kind == AssetKind.Container ? settings.PrefabFileFormat : settings.ExportFileFormat;
        return fileFormat == (int)EditorExportFormat.GLB ? ".glb" : ".gltf";
    }

    /**
     * Refuses to start while the editor cannot export: compiling, baking or in Play mode.
     * @throws Exception The editor is busy.
     */
    private static void ThrowIfEditorBusy()
    {
        if (EditorApplication.isCompiling) throw new Exception("Scripts are still compiling. Try again once they finish.");
        if (Lightmapping.isRunning) throw new Exception("A lightmap bake is in progress. Try again once it finishes.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Leave Play mode before building the game assets.");
    }

    /**
     * Refuses to build file names the game would not find, when the list asks for lowercase or .gltf names.
     * @param manifest The game asset list.
     * @throws Exception The exporter settings would write other names.
     */
    private static void ThrowIfNamingIsNotRequired(GameAssetManifest manifest)
    {
        CanvasToolsInfo settings = CanvasToolsInfo.Instance;
        var problems = new List<string>();
        if (manifest.requireLowercaseNames && settings.ExportCaseMode != (int)EditorCasingMode.ForceLowerCasing) problems.Add("Export Case Mode must be Force Lower Casing");
        if (manifest.requireGltf && settings.ExportFileFormat != (int)EditorExportFormat.GLTF) problems.Add("Scene File Format must be GLTF");
        if (manifest.requireGltf && settings.PrefabFileFormat != (int)EditorExportFormat.GLTF) problems.Add("Prefab File Format must be GLTF");
        if (problems.Count > 0) throw new Exception($"The exporter settings do not match the names {MANIFEST_PATH} requires: " + string.Join("; ", problems) + ".");
    }

    /**
     * Makes sure no unsaved scene change is lost when the build opens other scenes.
     * @param askToSaveScenes True from the menu: asks the user to save. Otherwise unsaved changes refuse the build.
     * @returns False when the user cancelled the save prompt.
     * @throws Exception Not asking, and a scene has unsaved changes.
     */
    private static bool PrepareOpenScenes(bool askToSaveScenes)
    {
        if (askToSaveScenes) return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        var modifiedScenes = new List<string>();
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            Scene openScene = EditorSceneManager.GetSceneAt(i);
            if (openScene.isDirty) modifiedScenes.Add(string.IsNullOrEmpty(openScene.path) ? openScene.name : openScene.path);
        }
        if (modifiedScenes.Count > 0) throw new Exception("Unity has unsaved changes in " + string.Join(", ", modifiedScenes) + ". Save them first; the build opens other scenes.");
        return true;
    }

    /**
     * Records the scenes open before the build, so the end of the build can put them back.
     * @param state The build state to fill.
     */
    private static void RememberOpenScenes(BuildState state)
    {
        foreach (SceneSetup setup in EditorSceneManager.GetSceneManagerSetup())
        {
            state.openScenePaths.Add(setup.path);
            state.openScenesLoaded.Add(setup.isLoaded);
            state.openScenesActive.Add(setup.isActive);
        }
    }

    /**
     * Puts back the scenes that were open before the build, when they all live on disk.
     * @param state The finished build.
     */
    private static void RestoreOpenScenes(BuildState state)
    {
        if (state.openScenePaths.Count == 0 || state.openScenePaths.Any(string.IsNullOrEmpty)) return;
        SceneSetup[] setups = state.openScenePaths
            .Select((path, index) => new SceneSetup { path = path, isLoaded = state.openScenesLoaded[index], isActive = state.openScenesActive[index] })
            .ToArray();
        EditorSceneManager.RestoreSceneManagerSetup(setups);
    }

    /**
     * Shows the build's progress bar in an interactive editor.
     * @param state The running build.
     * @param assetName The asset being built.
     */
    private static void ShowProgress(BuildState state, string assetName)
    {
        if (Application.isBatchMode) return;
        float progress = (float)state.results.Count / state.totalCount;
        EditorUtility.DisplayProgressBar(BUILD_TITLE, $"{state.results.Count + 1}/{state.totalCount}  {assetName}", progress);
    }

    /** Initialises the exporter and points it at this project's export folder, the way the toolkit's CLI bridge does. */
    private static void PrepareExporter()
    {
        RunExporter(() => CanvasTools.CanvasToolsExporter.Initialize());
        CanvasToolsInfo.DefaultProjectFolder = UnityTools.GetDefaultExportFolder();
    }

    /**
     * Turns a build the exporter stopped, or a TypeScript compile failure, into an error.
     * @throws Exception The last exporter build did not succeed.
     */
    private static void ThrowIfBuildFailed()
    {
        int buildResult = CanvasTools.CanvasToolsExporter.LastBuildResult;
        if (buildResult == 0) return;
        if (buildResult == -1) throw new Exception("the exporter stopped before building (licence, compiling, baking or missing project folder) — see the Unity console");
        throw new Exception($"TypeScript compile failed (exit {buildResult}) — see Debug/tsc-errors.txt");
    }

    /**
     * Reads and checks the project's game asset list.
     * @returns The list.
     * @throws Exception The file is missing, does not parse, names a scene that does not exist, or repeats a file name.
     */
    private static GameAssetManifest LoadManifest()
    {
        if (!File.Exists(MANIFEST_PATH)) throw new Exception($"{MANIFEST_PATH} is missing — create it with Tools ▸ {CanvasToolsStatics.CANVAS_TOOLS_MENU} ▸ Generate Assets ▸ Edit Game Asset List.");
        GameAssetManifest manifest = JsonUtility.FromJson<GameAssetManifest>(File.ReadAllText(MANIFEST_PATH));
        if (manifest == null) throw new Exception($"{MANIFEST_PATH} is not valid JSON.");
        manifest.containers ??= Array.Empty<ContainerEntry>();
        manifest.levels ??= Array.Empty<LevelEntry>();

        var problems = new List<string>();
        foreach (ContainerEntry container in manifest.containers)
        {
            if (string.IsNullOrEmpty(container.fileName)) problems.Add("a container has no fileName");
            if (container.roots == null || container.roots.Length == 0) problems.Add($"container {container.fileName} lists no roots");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(container.scene ?? "") == null) problems.Add($"container {container.fileName}: scene {container.scene} not found");
        }
        foreach (LevelEntry level in manifest.levels)
        {
            if (string.IsNullOrEmpty(level.fileName)) problems.Add("a level has no fileName");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(level.scene ?? "") == null) problems.Add($"level {level.fileName}: scene {level.scene} not found");
        }
        IEnumerable<string> fileNames = manifest.containers.Select(container => container.fileName).Concat(manifest.levels.Select(level => level.fileName));
        problems.AddRange(fileNames.GroupBy(fileName => fileName).Where(group => group.Count() > 1).Select(group => $"file name {group.Key} is used twice"));
        if (problems.Count > 0) throw new Exception($"{MANIFEST_PATH}: " + string.Join("; ", problems) + ".");
        return manifest;
    }

    /**
     * Turns the list into the build queue: the script bundle first (exports compile against it), then the containers, then the levels.
     * @param manifest The game asset list.
     * @returns The queue.
     */
    private static List<GameAsset> CreateQueue(GameAssetManifest manifest)
    {
        var queue = new List<GameAsset>();
        if (manifest.buildScripts)
        {
            string bundle = manifest.scriptBundle ?? "";
            queue.Add(new GameAsset { Kind = AssetKind.Scripts, Name = bundle.Length > 0 ? bundle : SCRIPTS_QUEUE_NAME, OutputFileName = bundle });
        }
        queue.AddRange(manifest.containers.Select(container => new GameAsset
        {
            Kind = AssetKind.Container,
            Name = container.fileName,
            OutputFileName = container.fileName,
            ScenePath = container.scene,
            RootNames = container.roots,
            PrefabPath = container.prefab ?? "",
        }));
        queue.AddRange(manifest.levels.Select(level => new GameAsset { Kind = AssetKind.Level, Name = level.fileName, OutputFileName = level.fileName, ScenePath = level.scene }));
        return queue;
    }

    /**
     * Resolves a selection to queue entries, in build order.
     * @param queue Every asset in the list.
     * @param selection Comma-separated groups or file names; null, empty or "all" selects everything.
     * @returns The selected assets.
     * @throws Exception A token names no group and no asset, or nothing is selected.
     */
    private static List<GameAsset> SelectAssets(List<GameAsset> queue, string selection)
    {
        string[] tokens = (selection ?? "").Split(',').Select(token => token.Trim().ToLowerInvariant()).Where(token => token.Length > 0).ToArray();
        List<GameAsset> selected;
        if (tokens.Length == 0 || tokens.Contains(ALL_SELECTION))
        {
            selected = queue;
        }
        else
        {
            string[] unknownTokens = tokens.Where(token => !queue.Any(asset => asset.Group == token || asset.Name.ToLowerInvariant() == token)).ToArray();
            if (unknownTokens.Length > 0)
            {
                string knownNames = string.Join(", ", new[] { SCRIPTS_GROUP, CONTAINERS_GROUP, LEVELS_GROUP }.Concat(queue.Select(asset => asset.Name)).Distinct());
                throw new Exception($"Unknown asset or group: {string.Join(", ", unknownTokens)}. Known: {knownNames}.");
            }
            selected = queue.Where(asset => tokens.Contains(asset.Group) || tokens.Contains(asset.Name.ToLowerInvariant())).ToList();
        }
        if (selected.Count == 0) throw new Exception($"Nothing to build — {MANIFEST_PATH} lists no assets in that selection.");
        return selected;
    }

    /**
     * Reads the running build from the editor session.
     * @returns The build state, or null when no build is running.
     */
    private static BuildState LoadState()
    {
        string json = SessionState.GetString(STATE_KEY, "");
        return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<BuildState>(json);
    }

    /**
     * Stores the running build in the editor session.
     * @param state The build state.
     */
    private static void SaveState(BuildState state)
    {
        SessionState.SetString(STATE_KEY, JsonUtility.ToJson(state));
    }

    /**
     * The folder the scenes, containers and script bundle are written to.
     * @returns The absolute path.
     */
    private static string ScenesFolder() => Path.Combine(UnityTools.GetDefaultExportFolder(), CanvasToolsInfo.Instance.DefaultScenePath);

    /**
     * Formats the build report: one line per asset, then the totals and the active compression settings.
     * @param state The finished build.
     * @returns The report.
     */
    private static string FormatReport(BuildState state)
    {
        CanvasToolsInfo settings = CanvasToolsInfo.Instance;
        TimeSpan elapsed = DateTime.Now - new DateTime(state.startTicks);
        int failures = state.results.Count(result => !result.succeeded);
        var report = new StringBuilder();
        report.AppendLine($"{BUILD_TITLE} — {state.results.Count - failures}/{state.results.Count} built in {(int)elapsed.TotalMinutes} min {elapsed.Seconds} s → {ScenesFolder()}");
        foreach (AssetResult result in state.results)
        {
            report.AppendLine($"  {(result.succeeded ? "OK  " : "FAIL")} {result.name,-20} {result.seconds,6:0.0} s  {result.detail}");
        }
        report.AppendLine($"Compression: Draco {(settings.EnableDracoCompression ? "on" : "off")}, images {(EditorImageFormat)settings.TextureImageFormat}, gzip scenes {(settings.CompressSceneFiles ? "on" : "off")}");
        return report.ToString();
    }

    /**
     * Formats a byte count for the report.
     * @param bytes The size in bytes.
     * @returns The size in KB or MB.
     */
    private static string FormatSize(long bytes)
    {
        const double BYTES_PER_KILOBYTE = 1024;
        const double BYTES_PER_MEGABYTE = BYTES_PER_KILOBYTE * 1024;
        return bytes >= BYTES_PER_MEGABYTE ? $"{bytes / BYTES_PER_MEGABYTE:0.0} MB" : $"{bytes / BYTES_PER_KILOBYTE:0} KB";
    }

    /**
     * Reads the value after an option on the editor's command line.
     * @param option The option, e.g. "-only".
     * @returns Its value, or null when it is not given.
     */
    private static string ReadCommandLineOption(string option)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        int optionIndex = Array.IndexOf(commandLine, option);
        return optionIndex >= 0 && optionIndex + 1 < commandLine.Length ? commandLine[optionIndex + 1] : null;
    }
}
#endif

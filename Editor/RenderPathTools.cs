#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

public static class RenderPathTools
{
    // Friendly GPU Resident Drawer labels. The backing enum is GPUResidentDrawerMode
    // { Disabled, InstancedDrawing }, introduced in Unity 6 (URP 17 / HDRP 17). Older
    // pipeline versions report DRAWER_UNSUPPORTED because the member does not exist.
    private const string DRAWER_DISABLED = "Disabled";
    private const string DRAWER_INSTANCED = "Instanced Drawing";
    private const string DRAWER_UNSUPPORTED = "Unsupported";

    // Where the GPU Resident Drawer mode is serialized, by pipeline. Probed in order, and
    // followed by a structural scan so a renamed or relocated field still resolves.
    private static readonly string[] DRAWER_MODE_PATHS =
    {
        "m_RenderPipelineSettings.gpuResidentDrawerSettings.mode",  // HDRP 17 (Unity 6)
        "m_GPUResidentDrawerMode",                                  // URP 17 (Unity 6)
        "gpuResidentDrawerSettings.mode",
    };

    /// <summary>
    /// Which scriptable render pipeline a given asset belongs to. Detected structurally so this
    /// file compiles in a project that has neither URP nor HDRP installed.
    /// </summary>
    public enum PipelineKind
    {
        BuiltIn,
        Universal,
        HighDefinition,
        Unknown
    }

    /// <summary>
    /// A single renderer data asset that needs its rendering path changed. URP only.
    /// </summary>
    private class RendererPathChange
    {
        public UnityEngine.Object rendererData;
        public RenderPipelineAsset owner;
        public string currentPath;
        public string targetPath;

        public override string ToString()
        {
            return $"{owner.name} / {rendererData.name}: {currentPath} → {targetPath}";
        }
    }

    /// <summary>
    /// A single pipeline asset that needs its GPU Resident Drawer mode changed.
    /// </summary>
    private class DrawerModeChange
    {
        public RenderPipelineAsset asset;
        public PipelineKind kind;
        public string currentMode;
        public string targetMode;

        public override string ToString()
        {
            return $"{asset.name} ({kind}): {currentMode} → {targetMode}";
        }
    }

    // =========================================================
    // Initialization / Menu
    // =========================================================
    [InitializeOnLoadMethod]
    static void InitializeRenderPathTools()
    {
        // Keep user's existing hook if available at compile time
        // Wrapped in try/catch in case CanvasTools is not defined in some projects.
        try
        {
            CanvasTools.EditorHooks.MainWindowOnEnable += ValidateRenderSettings;
        }
        catch { /* optional dependency */ }
    }

    [MenuItem("Tools/Babylon Toolkit/Rendering Options/Check Rendering Path", false, 51)]
    public static void CheckRenderSettings()
    {
        ValidateRenderPipeline(false);
    }

    [MenuItem("Tools/Babylon Toolkit/Rendering Options/Disable Resident Drawer", false, 52)]
    public static void DisableGPUResidentDrawer()
    {
        var assets = CollectConfiguredPipelineAssets();
        if (assets.Count == 0)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                "No Scriptable Render Pipeline Assets are configured (Built-in Render Pipeline).",
                "OK");
            return;
        }

        // Bail out early when no configured asset even exposes the feature
        bool anySupported = false;
        foreach (var asset in assets)
        {
            if (!GetGPUResidentDrawerMode(asset).Equals(DRAWER_UNSUPPORTED, System.StringComparison.OrdinalIgnoreCase))
            {
                anySupported = true;
                break;
            }
        }
        if (!anySupported)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                "This render pipeline version does not support the GPU Resident Drawer.",
                "OK");
            return;
        }

        var drawerChanges = CollectDrawerModeChanges(assets);
        if (drawerChanges.Count == 0)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                $"The GPU Resident Drawer is already disabled on all {assets.Count} configured pipeline asset(s).",
                "OK");
            return;
        }

        int changed = ApplyDrawerModeChanges(drawerChanges);
        if (changed > 0)
        {
            var message = new StringBuilder();
            message.AppendLine($"Disabled the GPU Resident Drawer on {changed} pipeline asset(s):");
            message.AppendLine();
            foreach (var change in drawerChanges)
            {
                message.AppendLine($"  • {change}");
            }
            message.AppendLine();
            message.Append(ReloadOpenScenes(true)
                ? "Open scenes were reloaded so their renderers re-register on the normal draw path."
                : "Reload your open scenes (or restart the Editor) so their renderers re-register on the normal draw path. Objects that the drawer already claimed stay invisible until you do.");

            EditorUtility.DisplayDialog("GPU Resident Drawer", message.ToString(), "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                "Failed to disable the GPU Resident Drawer. Check console for details.",
                "OK");
        }
    }

    public static void ValidateRenderSettings()
    {
        ValidateRenderPipeline(true);
    }

    // =========================================================
    // Public scripting API (no dialogs - safe for agents, CI and batch mode)
    // =========================================================
    /// <summary>
    /// Disables the GPU Resident Drawer on every configured pipeline asset, whichever pipeline is
    /// in use, and returns the number of assets actually changed. Never shows a dialog, so it is
    /// safe from -batchmode, `unity command eval` and [CliCommand] bridges.
    ///
    /// Set reloadScenes to reload the open scenes afterwards. This matters: disabling the drawer
    /// does NOT hand already-registered renderers back to the normal draw path, so anything the
    /// drawer had claimed stays invisible until its scene is reloaded. Scenes with unsaved changes
    /// are never reloaded (the edits would be lost) - save first, or reload by hand.
    /// </summary>
    public static int DisableResidentDrawer(bool reloadScenes = true)
    {
        var assets = CollectConfiguredPipelineAssets();
        if (assets.Count == 0) return 0;

        var changes = CollectDrawerModeChanges(assets);
        if (changes.Count == 0) return 0;

        int changed = ApplyDrawerModeChanges(changes);
        if (changed > 0 && reloadScenes) ReloadOpenScenes(false);
        return changed;
    }

    /// <summary>
    /// Same as DisableResidentDrawer, but returns a human readable multi-line report instead of a
    /// count - the convenient form for `unity command eval 'return RenderPathTools.DisableResidentDrawerReport();'`.
    /// </summary>
    public static string DisableResidentDrawerReport(bool reloadScenes = true)
    {
        var report = new StringBuilder();
        report.AppendLine("pipeline : " + GetActivePipelineName());

        var assets = CollectConfiguredPipelineAssets();
        if (assets.Count == 0)
        {
            report.Append("result   : no Scriptable Render Pipeline Assets configured (Built-in Render Pipeline)");
            return report.ToString();
        }

        var changes = CollectDrawerModeChanges(assets);
        if (changes.Count == 0)
        {
            report.AppendLine($"scanned  : {assets.Count} asset(s)");
            foreach (var asset in assets)
            {
                report.AppendLine($"  • {asset.name} ({DetectPipelineKind(asset)}): {GetGPUResidentDrawerMode(asset)}");
            }
            report.Append("result   : already disabled everywhere - nothing to do");
            return report.ToString();
        }

        int changed = ApplyDrawerModeChanges(changes);
        report.AppendLine($"scanned  : {assets.Count} asset(s)");
        foreach (var change in changes)
        {
            report.AppendLine($"  • {change}");
        }
        report.AppendLine($"changed  : {changed}");

        if (changed > 0 && reloadScenes)
        {
            report.Append(ReloadOpenScenes(false)
                ? "reloaded : open scenes reloaded - renderers are back on the normal draw path"
                : "reloaded : NO - reload the open scenes yourself (unsaved changes, or play mode). Claimed renderers stay invisible until you do");
        }
        else
        {
            report.Append("reloaded : skipped - claimed renderers stay invisible until their scene is reloaded");
        }

        return report.ToString();
    }

    /// <summary>
    /// Reports the GPU Resident Drawer state of every configured pipeline asset without changing
    /// anything. Useful as a pre-flight check in a build or export script.
    /// </summary>
    public static string GetResidentDrawerReport()
    {
        var report = new StringBuilder();
        report.AppendLine("pipeline : " + GetActivePipelineName());

        var assets = CollectConfiguredPipelineAssets();
        if (assets.Count == 0)
        {
            report.Append("assets   : none (Built-in Render Pipeline)");
            return report.ToString();
        }

        foreach (var asset in assets)
        {
            report.AppendLine($"  • {asset.name} ({DetectPipelineKind(asset)}): {GetGPUResidentDrawerMode(asset)}");
        }
        report.Append("enabled  : " + CollectDrawerModeChanges(assets).Count + " asset(s) still have it on");
        return report.ToString();
    }

    /// <summary>
    /// Name and kind of the render pipeline currently driving the Editor.
    /// </summary>
    public static string GetActivePipelineName()
    {
        var active = GetActiveRenderPipelineAsset();
        if (active == null) return "Built-in Render Pipeline";
        return $"{active.name} ({DetectPipelineKind(active)} / {active.GetType().Name})";
    }

    // =========================================================
    // Validation
    // =========================================================
    /// <summary>
    /// Validates every configured render pipeline asset: the GPU Resident Drawer on all pipelines,
    /// plus the URP rendering paths (Forward/Deferred/Forward+/Deferred+) where they apply.
    /// Works whether the pipeline is assigned globally, per-quality, or both.
    /// </summary>
    private static void ValidateRenderPipeline(bool silent = true)
    {
        try
        {
            // Handle Built-in Render Pipeline (no RP asset assigned anywhere)
            if (GetActiveRenderPipelineAsset() == null)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Render Pipeline Check",
                        "Built-in Render Pipeline is active (no URP or HDRP asset assigned).",
                        "OK");
                }
                return;
            }

            var assets = CollectConfiguredPipelineAssets();
            if (assets.Count == 0)
            {
                if (!silent)
                {
                    var active = GetActiveRenderPipelineAsset();
                    EditorUtility.DisplayDialog("Render Pipeline Check",
                        $"Active Render Pipeline is not recognized as URP or HDRP. Type: {active.GetType().Name}",
                        "OK");
                }
                return;
            }

            ValidatePipelineSettings(assets, silent);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Error during render pipeline validation: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates settings across every configured pipeline asset: the GPU Resident Drawer on all
    /// of them, and the rendering path of each renderer on the URP ones. Suggests changes if the
    /// current configuration may cause compatibility issues.
    /// </summary>
    private static void ValidatePipelineSettings(List<RenderPipelineAsset> assets, bool silent)
    {
        try
        {
            var pathChanges = CollectRendererPathChanges(assets);
            var drawerChanges = CollectDrawerModeChanges(assets);

            // Show prompt if any change is suggested
            if (pathChanges.Count > 0 || drawerChanges.Count > 0)
            {
                var message = new StringBuilder();
                message.AppendLine($"Scanned {assets.Count} configured pipeline asset(s).");
                message.AppendLine();

                if (pathChanges.Count > 0)
                {
                    message.AppendLine("Forward+ / Deferred+ rendering may have compatibility issues with some Babylon Toolkit features. The standard forward rendering path is recommended.");
                    message.AppendLine();
                }
                if (drawerChanges.Count > 0)
                {
                    // The GPU Resident Drawer is a Unity-only runtime batching system that plays no
                    // part in the Babylon export. Left enabled it either spams the console on URP
                    // Forward ("Disabled due to some configured Universal Renderers not using the
                    // Forward+ or Deferred+ rendering paths"), or trips the job safety checks. On
                    // HDRP that failure is worse than noisy: QueryRendererInstancesJob throws while
                    // registering the scene's MeshRenderers, the drawer has already taken them off
                    // the normal draw path, and NOTHING renders except the sky.
                    message.AppendLine("The GPU Resident Drawer is not used by the Babylon Toolkit export and logs repeated console warnings or job errors depending on the pipeline. On HDRP a failed registration can make every mesh in the scene invisible. Disabling it is recommended.");
                    message.AppendLine();
                }

                message.AppendLine("Would you like to apply the following change(s)?");
                foreach (var change in pathChanges)
                {
                    message.AppendLine($"  • Rendering Path — {change}");
                }
                foreach (var change in drawerChanges)
                {
                    message.AppendLine($"  • GPU Resident Drawer — {change}");
                }

                if (EditorUtility.DisplayDialog("Render Pipeline Recommendation", message.ToString(), "Yes, Change", "No, Keep Current"))
                {
                    var applied = new List<string>();

                    int changedPaths = ApplyRendererPathChanges(pathChanges);
                    if (changedPaths > 0)
                    {
                        applied.Add($"Rendering Path changed on {changedPaths} renderer(s)");
                    }

                    int changedDrawers = ApplyDrawerModeChanges(drawerChanges);
                    if (changedDrawers > 0)
                    {
                        applied.Add($"GPU Resident Drawer disabled on {changedDrawers} asset(s)");
                        applied.Add(ReloadOpenScenes(true)
                            ? "Open scenes reloaded so their renderers re-register"
                            : "Reload your open scenes so their renderers re-register");
                    }

                    if (applied.Count > 0)
                    {
                        EditorUtility.DisplayDialog("Render Pipeline Settings Updated",
                            "Successfully applied:\n\n  • " + string.Join("\n  • ", applied),
                            "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Render Pipeline Settings Updated",
                            "No settings could be changed. Check console for details.",
                            "OK");
                    }
                }
                else
                {
                    Debug.Log("[Babylon Toolkit] User chose to keep current render pipeline settings");
                }
            }
            else if (!silent)
            {
                // If called manually and no issues found, show confirmation
                var message = new StringBuilder();
                message.AppendLine($"Scanned {assets.Count} configured pipeline asset(s).");
                message.AppendLine();
                foreach (var asset in assets)
                {
                    message.AppendLine($"{asset.name} ({DetectPipelineKind(asset)})");
                    foreach (var rendererData in GetAllRendererData(asset))
                    {
                        message.AppendLine($"  • {rendererData.name}: {GetRendererDataRenderingPath(rendererData)}");
                    }
                    message.AppendLine($"  • GPU Resident Drawer: {GetGPUResidentDrawerMode(asset)}");
                }
                message.AppendLine();
                message.AppendLine("These settings are compatible with Babylon Toolkit.");

                EditorUtility.DisplayDialog("Render Path Check", message.ToString(), "OK");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Error during render pipeline settings validation: {ex.Message}");
        }
    }

    // =========================================================
    // Active RP discovery
    // =========================================================
    /// <summary>
    /// Returns the effective (active) RP asset in priority order:
    /// GraphicsSettings.currentRenderPipeline -> QualitySettings.renderPipeline -> GraphicsSettings.defaultRenderPipeline
    /// </summary>
    private static RenderPipelineAsset GetActiveRenderPipelineAsset()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp != null) return rp;

        rp = QualitySettings.renderPipeline;
        if (rp != null) return rp;

        return GraphicsSettings.defaultRenderPipeline;
    }

    /// <summary>
    /// Identifies which pipeline an asset belongs to without referencing URP or HDRP types, so
    /// this file still compiles in a project that has neither package installed. Matches on the
    /// type hierarchy first, then falls back to the shape of the asset.
    /// </summary>
    public static PipelineKind DetectPipelineKind(RenderPipelineAsset asset)
    {
        if (asset == null) return PipelineKind.BuiltIn;

        for (var t = asset.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            if (t.Name == "UniversalRenderPipelineAsset") return PipelineKind.Universal;
            if (t.Name == "HDRenderPipelineAsset") return PipelineKind.HighDefinition;
        }

        // Structural fallbacks for forks and renamed derivatives
        if (HasURPRendererData(asset)) return PipelineKind.Universal;
        if (HasHDRPRenderPipelineSettings(asset)) return PipelineKind.HighDefinition;

        return PipelineKind.Unknown;
    }

    /// <summary>
    /// Returns every distinct URP or HDRP asset configured for the project: the graphics default
    /// plus the per-quality-level overrides. Unity validates the GPU Resident Drawer against all of
    /// them, and - more to the point - switching quality level swaps which one is live, so a stale
    /// setting on an inactive tier is a bug waiting to happen.
    /// </summary>
    private static List<RenderPipelineAsset> CollectConfiguredPipelineAssets()
    {
        var assets = new List<RenderPipelineAsset>();
        var seen = new HashSet<RenderPipelineAsset>();

        AddPipelineAsset(GraphicsSettings.defaultRenderPipeline, assets, seen);
        AddPipelineAsset(QualitySettings.renderPipeline, assets, seen);

        // Sweep every quality level, not just the active one
        var levelNames = QualitySettings.names;
        int levelCount = levelNames != null ? levelNames.Length : 0;
        for (int i = 0; i < levelCount; i++)
        {
            try
            {
                AddPipelineAsset(QualitySettings.GetRenderPipelineAssetAt(i), assets, seen);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Babylon Toolkit] Could not read the Render Pipeline Asset for quality level {i}: {ex.Message}");
            }
        }

        return assets;
    }

    // Deduplicate on the asset reference itself rather than GetInstanceID(), which Unity 6
    // marks obsolete in favour of GetEntityId() - a member that does not exist on 2022.3.
    private static void AddPipelineAsset(RenderPipelineAsset asset, List<RenderPipelineAsset> assets, HashSet<RenderPipelineAsset> seen)
    {
        if (asset == null) return;
        if (DetectPipelineKind(asset) == PipelineKind.Unknown) return;
        if (!seen.Add(asset)) return;
        assets.Add(asset);
    }

    /// <summary>
    /// Best-effort URP detection by checking for a renderer data list presence.
    /// </summary>
    private static bool HasURPRendererData(RenderPipelineAsset rpAsset)
    {
        if (rpAsset == null) return false;
        var t = rpAsset.GetType();

        // Try private m_RendererDataList
        var listField = t.GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (listField != null)
        {
            var listObj = listField.GetValue(rpAsset) as System.Collections.IList;
            if (listObj != null && listObj.Count > 0) return true;
        }

        // Try public rendererDataList (older/newer variants)
        var listProp = t.GetProperty("rendererDataList", BindingFlags.Public | BindingFlags.Instance);
        if (listProp != null)
        {
            var listObj = listProp.GetValue(rpAsset) as System.Collections.IList;
            if (listObj != null && listObj.Count > 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Best-effort HDRP detection. HDRP keeps everything in a RenderPipelineSettings struct
    /// exposed as currentPlatformRenderPipelineSettings and serialized as m_RenderPipelineSettings.
    /// </summary>
    private static bool HasHDRPRenderPipelineSettings(RenderPipelineAsset rpAsset)
    {
        if (rpAsset == null) return false;
        var t = rpAsset.GetType();

        if (t.GetProperty("currentPlatformRenderPipelineSettings", BindingFlags.Public | BindingFlags.Instance) != null) return true;
        if (t.GetField("m_RenderPipelineSettings", BindingFlags.NonPublic | BindingFlags.Instance) != null) return true;

        return false;
    }

    // =========================================================
    // Renderer Data helpers (URP only - HDRP has no renderer data assets)
    // =========================================================
    /// <summary>
    /// Gets every ScriptableRendererData on a URP asset, not just the default one. Unity's own
    /// IsGPUResidentDrawerSupportedBySRP iterates the whole list, and a single Forward renderer
    /// anywhere in it is enough to disable the drawer for the entire asset. Returns an empty list
    /// for HDRP, which has no equivalent.
    /// </summary>
    private static List<UnityEngine.Object> GetAllRendererData(RenderPipelineAsset urpAsset)
    {
        var results = new List<UnityEngine.Object>();
        if (urpAsset == null) return results;

        var t = urpAsset.GetType();

        // IList of ScriptableRendererData
        var listField = t.GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        var listObj = listField?.GetValue(urpAsset) as System.Collections.IList;

        if (listObj == null)
        {
            // Try public rendererDataList (older/newer variants)
            var listProp = t.GetProperty("rendererDataList", BindingFlags.Public | BindingFlags.Instance);
            listObj = listProp?.GetValue(urpAsset) as System.Collections.IList;
        }

        if (listObj == null) return results;

        foreach (var entry in listObj)
        {
            var rendererData = entry as UnityEngine.Object;
            if (rendererData != null) results.Add(rendererData);
        }

        return results;
    }

    // =========================================================
    // Read / Write Rendering Mode (URP only)
    // =========================================================
    /// <summary>
    /// Gets the rendering path of a single renderer data asset via reflection.
    /// Returns user-friendly names: "Forward", "Deferred", "Forward+", "Deferred+"
    /// </summary>
    private static string GetRendererDataRenderingPath(UnityEngine.Object rendererData)
    {
        try
        {
            if (rendererData == null)
                return "Unknown";

            var rendererDataType = rendererData.GetType();

            // Try to get the renderingMode property from the renderer data
            object renderingMode = null;
            var renderingModeProperty = rendererDataType.GetProperty("renderingMode", BindingFlags.Public | BindingFlags.Instance);
            if (renderingModeProperty != null)
            {
                renderingMode = renderingModeProperty.GetValue(rendererData);
            }
            else
            {
                // Fallback: private field
                var renderingModeField = rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (renderingModeField != null)
                {
                    renderingMode = renderingModeField.GetValue(rendererData);
                }
            }

            return FormatRenderingMode(renderingMode);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Babylon Toolkit] Error determining URP rendering path: {ex.Message}\n{ex.StackTrace}");
            return "Unknown (Error during detection)";
        }
    }

    /// <summary>
    /// Finds every renderer across every configured URP asset that is on a clustered
    /// (Forward+ / Deferred+) rendering path, paired with the non-clustered path to move it to.
    /// HDRP assets contribute nothing here - they have no renderer data.
    /// </summary>
    private static List<RendererPathChange> CollectRendererPathChanges(List<RenderPipelineAsset> assets)
    {
        var changes = new List<RendererPathChange>();

        foreach (var asset in assets)
        {
            if (DetectPipelineKind(asset) != PipelineKind.Universal) continue;

            foreach (var rendererData in GetAllRendererData(asset))
            {
                var currentPath = GetRendererDataRenderingPath(rendererData);
                var targetPath = GetNonClusteredEquivalent(currentPath);
                if (targetPath == null) continue;

                changes.Add(new RendererPathChange
                {
                    rendererData = rendererData,
                    owner = asset,
                    currentPath = currentPath,
                    targetPath = targetPath
                });
            }
        }

        return changes;
    }

    /// <summary>
    /// Writes each queued rendering path change. Returns the number of renderers actually changed.
    /// </summary>
    private static int ApplyRendererPathChanges(List<RendererPathChange> changes)
    {
        int changed = 0;
        var dirtyAssets = new HashSet<RenderPipelineAsset>();

        foreach (var change in changes)
        {
            // Normalize target to enum name
            string desiredEnumName = NormalizeTargetModeName(change.targetPath);
            if (desiredEnumName == null)
            {
                Debug.LogWarning($"[Babylon Toolkit] Invalid render path: {change.targetPath}. Valid options are: Forward, Deferred, Forward+");
                continue;
            }

            if (!WriteRenderingMode(change.rendererData, desiredEnumName)) continue;

            EditorUtility.SetDirty(change.rendererData);
            if (dirtyAssets.Add(change.owner))
            {
                EditorUtility.SetDirty(change.owner);
            }

            Debug.Log($"[Babylon Toolkit] Changed render path on {change.owner.name} / {change.rendererData.name} from {change.currentPath} to {change.targetPath}");
            changed++;
        }

        if (changed > 0)
        {
            AssetDatabase.SaveAssets();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        return changed;
    }

    /// <summary>
    /// Sets m_RenderingMode on a single renderer data asset. Parses by enum name
    /// (case-insensitive) to avoid hard-coded int ordinals across URP versions.
    /// </summary>
    private static bool WriteRenderingMode(UnityEngine.Object rendererData, string desiredEnumName)
    {
        try
        {
            var rendererDataType = rendererData.GetType();

            // Discover enum type from property or field
            var renderingModeProperty = rendererDataType.GetProperty("renderingMode", BindingFlags.Public | BindingFlags.Instance);
            var renderingModeField = rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
            System.Type enumType = renderingModeProperty?.PropertyType ?? renderingModeField?.FieldType;

            if (enumType == null || !enumType.IsEnum)
            {
                Debug.LogWarning($"[Babylon Toolkit] Could not determine URP rendering mode enum type on {rendererData.name}.");
                return false;
            }

            object enumValue;
            try
            {
                enumValue = System.Enum.Parse(enumType, desiredEnumName, ignoreCase: true);
            }
            catch
            {
                Debug.LogWarning($"[Babylon Toolkit] Render mode \"{desiredEnumName}\" not supported by this URP version.");
                return false;
            }

            if (renderingModeProperty != null && renderingModeProperty.CanWrite)
            {
                renderingModeProperty.SetValue(rendererData, enumValue);
                return true;
            }

            if (renderingModeField != null)
            {
                renderingModeField.SetValue(rendererData, enumValue);
                return true;
            }

            Debug.LogWarning($"[Babylon Toolkit] No writable rendering mode member found on {rendererData.name}.");
            return false;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Failed to set URP rendering mode on {rendererData.name}: {ex.Message}");
            return false;
        }
    }

    // =========================================================
    // Read / Write GPU Resident Drawer (URP and HDRP)
    // =========================================================
    /// <summary>
    /// Gets the current GPU Resident Drawer mode from a pipeline asset.
    /// Returns user-friendly names: "Disabled", "Instanced Drawing", or "Unsupported"
    /// when the pipeline version predates the GPU Resident Drawer.
    ///
    /// URP exposes it directly on the asset (gpuResidentDrawerMode / m_GPUResidentDrawerMode).
    /// HDRP buries it inside the RenderPipelineSettings struct
    /// (m_RenderPipelineSettings.gpuResidentDrawerSettings.mode), which no top-level reflection
    /// lookup finds - hence the SerializedObject fallback.
    /// </summary>
    private static string GetGPUResidentDrawerMode(RenderPipelineAsset rpAsset)
    {
        try
        {
            if (rpAsset == null) return DRAWER_UNSUPPORTED;

            var rpAssetType = rpAsset.GetType();

            // URP: the mode lives directly on the asset
            object drawerMode = null;
            var drawerModeProperty = rpAssetType.GetProperty("gpuResidentDrawerMode", BindingFlags.Public | BindingFlags.Instance);
            if (drawerModeProperty != null)
            {
                drawerMode = drawerModeProperty.GetValue(rpAsset);
            }
            else
            {
                var drawerModeField = rpAssetType.GetField("m_GPUResidentDrawerMode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (drawerModeField != null)
                {
                    drawerMode = drawerModeField.GetValue(rpAsset);
                }
            }

            if (drawerMode != null) return FormatGPUResidentDrawerMode(drawerMode);

            // HDRP (and anything else that nests it): read it through the serialized data
            return GetGPUResidentDrawerModeSerialized(rpAsset);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Babylon Toolkit] Error determining GPU Resident Drawer mode: {ex.Message}\n{ex.StackTrace}");
            return DRAWER_UNSUPPORTED;
        }
    }

    /// <summary>
    /// Reads the drawer mode out of the asset's serialized data, wherever the pipeline keeps it.
    /// </summary>
    private static string GetGPUResidentDrawerModeSerialized(RenderPipelineAsset rpAsset)
    {
        var serialized = new SerializedObject(rpAsset);
        var prop = FindDrawerModeProperty(serialized);
        if (prop == null) return DRAWER_UNSUPPORTED;

        var names = prop.enumNames;
        if (names == null || names.Length == 0) return DRAWER_UNSUPPORTED;
        if (prop.enumValueIndex < 0 || prop.enumValueIndex >= names.Length) return DRAWER_UNSUPPORTED;

        return FormatGPUResidentDrawerMode(names[prop.enumValueIndex]);
    }

    /// <summary>
    /// Locates the GPU Resident Drawer mode inside a pipeline asset's serialized data. Tries the
    /// known paths for URP and HDRP first, then walks the whole object so a relocated or renamed
    /// field in a future package version still resolves instead of silently reporting Unsupported.
    /// </summary>
    private static SerializedProperty FindDrawerModeProperty(SerializedObject serialized)
    {
        foreach (var path in DRAWER_MODE_PATHS)
        {
            var known = serialized.FindProperty(path);
            if (known != null && known.propertyType == SerializedPropertyType.Enum) return known;
        }

        var iterator = serialized.GetIterator();
        bool enterChildren = true;
        int guard = 0;

        while (iterator.Next(enterChildren) && guard++ < 20000)
        {
            // Arrays can be enormous (shader lists, probe data) and never hold this value
            enterChildren = !iterator.isArray && iterator.propertyType != SerializedPropertyType.String;

            if (iterator.propertyType != SerializedPropertyType.Enum) continue;

            var path = iterator.propertyPath;
            if (path.EndsWith("gpuResidentDrawerSettings.mode", System.StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("m_GPUResidentDrawerMode", System.StringComparison.OrdinalIgnoreCase))
            {
                return iterator.Copy();
            }
        }

        return null;
    }

    /// <summary>
    /// Finds every configured pipeline asset with the GPU Resident Drawer enabled.
    /// </summary>
    private static List<DrawerModeChange> CollectDrawerModeChanges(List<RenderPipelineAsset> assets)
    {
        var changes = new List<DrawerModeChange>();

        foreach (var asset in assets)
        {
            var currentMode = GetGPUResidentDrawerMode(asset);
            if (!IsGPUResidentDrawerEnabled(currentMode)) continue;

            changes.Add(new DrawerModeChange
            {
                asset = asset,
                kind = DetectPipelineKind(asset),
                currentMode = currentMode,
                targetMode = DRAWER_DISABLED
            });
        }

        return changes;
    }

    /// <summary>
    /// Writes each queued GPU Resident Drawer change. Returns the number of assets actually changed.
    /// </summary>
    private static int ApplyDrawerModeChanges(List<DrawerModeChange> changes)
    {
        int changed = 0;

        foreach (var change in changes)
        {
            // Normalize target to enum name
            string desiredEnumName = NormalizeDrawerModeName(change.targetMode);
            if (desiredEnumName == null)
            {
                Debug.LogWarning($"[Babylon Toolkit] Invalid GPU Resident Drawer mode: {change.targetMode}. Valid options are: Disabled, Instanced Drawing");
                continue;
            }

            if (!WriteGPUResidentDrawerMode(change.asset, desiredEnumName)) continue;

            EditorUtility.SetDirty(change.asset);
            Debug.Log($"[Babylon Toolkit] Changed GPU Resident Drawer on {change.asset.name} ({change.kind}) from {change.currentMode} to {change.targetMode}");
            changed++;
        }

        if (changed > 0)
        {
            AssetDatabase.SaveAssets();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        return changed;
    }

    /// <summary>
    /// Sets the GPU Resident Drawer mode on a single pipeline asset. Parses by enum name
    /// (case-insensitive) to avoid hard-coded int ordinals across pipeline versions.
    /// </summary>
    private static bool WriteGPUResidentDrawerMode(RenderPipelineAsset rpAsset, string desiredEnumName)
    {
        try
        {
            var rpAssetType = rpAsset.GetType();

            // URP: discover the enum type from the top-level property or field
            var drawerModeProperty = rpAssetType.GetProperty("gpuResidentDrawerMode", BindingFlags.Public | BindingFlags.Instance);
            var drawerModeField = rpAssetType.GetField("m_GPUResidentDrawerMode", BindingFlags.NonPublic | BindingFlags.Instance);
            System.Type enumType = drawerModeProperty?.PropertyType ?? drawerModeField?.FieldType;

            if (enumType != null && enumType.IsEnum)
            {
                object enumValue;
                try
                {
                    enumValue = System.Enum.Parse(enumType, desiredEnumName, ignoreCase: true);
                }
                catch
                {
                    Debug.LogWarning($"[Babylon Toolkit] GPU Resident Drawer mode \"{desiredEnumName}\" not supported by this pipeline version.");
                    return false;
                }

                // Prefer the property: its setter calls OnValidate(), which tears down the
                // running GPU Resident Drawer immediately instead of waiting for a reload.
                if (drawerModeProperty != null && drawerModeProperty.CanWrite)
                {
                    drawerModeProperty.SetValue(rpAsset, enumValue);
                    return true;
                }

                if (drawerModeField != null)
                {
                    drawerModeField.SetValue(rpAsset, enumValue);

                    // Field writes bypass the property setter, so poke OnValidate() manually
                    var onValidate = rpAssetType.GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                    if (onValidate != null)
                    {
                        try { onValidate.Invoke(rpAsset, null); }
                        catch { /* best effort - the value is still serialized by the caller */ }
                    }
                    return true;
                }
            }

            // HDRP: the mode is a field of a struct inside a struct. Reflection would have to
            // box, mutate and write back the whole RenderPipelineSettings chain; SerializedObject
            // does that correctly for us, and ApplyModifiedProperties fires OnValidate so the
            // running drawer is disposed straight away.
            return WriteGPUResidentDrawerModeSerialized(rpAsset, desiredEnumName);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Failed to set GPU Resident Drawer mode on {rpAsset.name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes the drawer mode through the asset's serialized data. Used for HDRP, and as a
    /// fallback for any pipeline that does not expose the mode at the top level.
    /// </summary>
    private static bool WriteGPUResidentDrawerModeSerialized(RenderPipelineAsset rpAsset, string desiredEnumName)
    {
        var serialized = new SerializedObject(rpAsset);
        var prop = FindDrawerModeProperty(serialized);
        if (prop == null)
        {
            Debug.LogWarning($"[Babylon Toolkit] This pipeline version does not support the GPU Resident Drawer ({rpAsset.name}).");
            return false;
        }

        var names = prop.enumNames;
        if (names == null || names.Length == 0) return false;

        int target = -1;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], desiredEnumName, System.StringComparison.OrdinalIgnoreCase))
            {
                target = i;
                break;
            }
        }

        if (target < 0)
        {
            Debug.LogWarning($"[Babylon Toolkit] GPU Resident Drawer mode \"{desiredEnumName}\" not supported by this pipeline version.");
            return false;
        }

        if (prop.enumValueIndex == target) return true;

        prop.enumValueIndex = target;
        serialized.ApplyModifiedProperties();
        return true;
    }

    // =========================================================
    // Scene reload
    // =========================================================
    /// <summary>
    /// Reloads the open scenes, preserving the multi-scene setup.
    ///
    /// Disabling the GPU Resident Drawer does not return renderers it has already claimed to the
    /// normal draw path - they stay invisible for the rest of the session. Only a scene reload
    /// re-registers them. (Anything created AFTER the drawer is disabled renders fine, which is
    /// what makes this so confusing to diagnose: a fresh test cube appears, the scene does not.)
    ///
    /// Refuses to reload when a scene has unsaved changes and the caller is non-interactive,
    /// because reloading would discard them. Returns true only if the reload actually happened.
    /// </summary>
    private static bool ReloadOpenScenes(bool interactive)
    {
        try
        {
            if (EditorApplication.isPlaying) return false;

            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            if (setup == null || setup.Length == 0) return false;

            // An unsaved "Untitled" scene has no path and cannot be restored
            foreach (var entry in setup)
            {
                if (entry == null || string.IsNullOrEmpty(entry.path)) return false;
            }

            bool anyDirty = false;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                {
                    anyDirty = true;
                    break;
                }
            }

            if (anyDirty)
            {
                // Never silently discard the user's unsaved work
                if (!interactive) return false;
                if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            }

            UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Could not reload the open scenes: {ex.Message}");
            return false;
        }
    }

    // =========================================================
    // Helper mappers / formatters
    // =========================================================
    private static string NormalizeTargetModeName(string target)
    {
        if (string.IsNullOrEmpty(target)) return null;
        switch (target.Trim().ToLowerInvariant())
        {
            case "forward":
                return "Forward";
            case "deferred":
                return "Deferred";
            case "forward+":
            case "forwardplus":
                return "ForwardPlus";
            case "deferred+":
            case "deferredplus":
                return "DeferredPlus";
            default:
                return null;
        }
    }

    private static string NormalizeDrawerModeName(string target)
    {
        if (string.IsNullOrEmpty(target)) return null;
        switch (target.Trim().ToLowerInvariant())
        {
            case "disabled":
            case "off":
            case "none":
                return "Disabled";
            case "instanced drawing":
            case "instanceddrawing":
            case "enabled":
            case "on":
                return "InstancedDrawing";
            default:
                return null;
        }
    }

    private static string FormatRenderingMode(object renderingMode)
    {
        if (renderingMode == null) return "Unknown";
        var modeStr = renderingMode.ToString();

        // Normalize well-known enum names to user-friendly labels
        switch (modeStr)
        {
            case "Forward":
                return "Forward";
            case "Deferred":
                return "Deferred";
            case "ForwardPlus":
                return "Forward+";
            case "DeferredPlus":
                return "Deferred+";
            // Unknown / vendor modified names
            default:
                return modeStr;
        }
    }

    private static string FormatGPUResidentDrawerMode(object drawerMode)
    {
        if (drawerMode == null) return DRAWER_UNSUPPORTED;
        var modeStr = drawerMode.ToString();

        // Normalize well-known enum names to user-friendly labels
        switch (modeStr)
        {
            case "Disabled":
                return DRAWER_DISABLED;
            case "InstancedDrawing":
                return DRAWER_INSTANCED;
            // Unknown / vendor modified names
            default:
                return modeStr;
        }
    }

    /// <summary>
    /// Maps a clustered rendering path to the plain equivalent the toolkit recommends,
    /// or null when the path is already compatible.
    /// </summary>
    private static string GetNonClusteredEquivalent(string renderingPath)
    {
        if (string.IsNullOrEmpty(renderingPath)) return null;

        if (renderingPath.Equals("Forward+", System.StringComparison.OrdinalIgnoreCase))
            return "Forward";

        if (renderingPath.Equals("Deferred+", System.StringComparison.OrdinalIgnoreCase))
            return "Deferred";

        return null;
    }

    /// <summary>
    /// True when the GPU Resident Drawer is active in any mode (i.e. not Disabled and
    /// not absent from this pipeline version).
    /// </summary>
    private static bool IsGPUResidentDrawerEnabled(string drawerMode)
    {
        if (string.IsNullOrEmpty(drawerMode)) return false;
        if (drawerMode.Equals(DRAWER_DISABLED, System.StringComparison.OrdinalIgnoreCase)) return false;
        if (drawerMode.Equals(DRAWER_UNSUPPORTED, System.StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // (Optional) Descriptions for UI or logs; kept for completeness.
    private static string GetRenderingPathDescription(string renderingPath)
    {
        switch (renderingPath.ToLower())
        {
            case "forward":
                return "Forward Rendering: Lights are processed per-pixel in a single pass. Good for most scenarios with moderate light counts.";
            case "forward+":
            case "forwardplus":
                return "Forward+ Rendering: Uses tiled/clustered lighting for better performance with many lights. Requires compute shader support.";
            case "deferred":
                return "Deferred Rendering: Geometry and lighting are rendered in separate passes. Efficient for many lights but requires G-buffer support.";
            default:
                return "Rendering path details not available for this configuration.";
        }
    }
}

#endif

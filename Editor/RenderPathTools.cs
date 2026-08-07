#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

public static class RenderPathTools
{
    // Friendly GPU Resident Drawer labels. The backing URP enum is GPUResidentDrawerMode
    // { Disabled, InstancedDrawing }, introduced in URP 17 (Unity 6). Older URP versions
    // report DRAWER_UNSUPPORTED because the member does not exist.
    private const string DRAWER_DISABLED = "Disabled";
    private const string DRAWER_INSTANCED = "Instanced Drawing";
    private const string DRAWER_UNSUPPORTED = "Unsupported";

    /// <summary>
    /// A single renderer data asset that needs its rendering path changed.
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
        public string currentMode;
        public string targetMode;

        public override string ToString()
        {
            return $"{asset.name}: {currentMode} → {targetMode}";
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
        var urpAssets = CollectConfiguredURPAssets();
        if (urpAssets.Count == 0)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                "No configured URP Render Pipeline Assets found.",
                "OK");
            return;
        }

        // Bail out early when no configured asset even exposes the feature
        bool anySupported = false;
        foreach (var asset in urpAssets)
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
                "This URP version does not support the GPU Resident Drawer.",
                "OK");
            return;
        }

        var drawerChanges = CollectDrawerModeChanges(urpAssets);
        if (drawerChanges.Count == 0)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                $"The GPU Resident Drawer is already disabled on all {urpAssets.Count} configured URP asset(s).",
                "OK");
            return;
        }

        int changed = ApplyDrawerModeChanges(drawerChanges);
        if (changed > 0)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                $"Disabled the GPU Resident Drawer on {changed} URP asset(s):\n\n  • " + string.Join("\n  • ", drawerChanges),
                "OK");
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
    // Validation
    // =========================================================
    /// <summary>
    /// Validates every configured render pipeline asset and its URP rendering paths
    /// (Forward/Deferred/Forward+/Deferred+) plus the GPU Resident Drawer.
    /// Works whether URP is assigned globally, per-quality, or both.
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
                        "Built-in Render Pipeline is active (no URP asset assigned).",
                        "OK");
                }
                return;
            }

            // Determine which of the configured assets look like URP by probing renderer data
            var urpAssets = CollectConfiguredURPAssets();
            if (urpAssets.Count == 0)
            {
                if (!silent)
                {
                    var active = GetActiveRenderPipelineAsset();
                    EditorUtility.DisplayDialog("Render Pipeline Check",
                        $"Active Render Pipeline is not recognized as URP. Type: {active.GetType().Name}",
                        "OK");
                }
                return;
            }

            ValidateURPSettings(urpAssets, silent);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Error during render pipeline validation: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates URP-specific settings across every configured pipeline asset: the rendering
    /// path of each renderer and the GPU Resident Drawer of each asset. Suggests changes if the
    /// current configuration may cause compatibility issues.
    /// </summary>
    private static void ValidateURPSettings(List<RenderPipelineAsset> urpAssets, bool silent)
    {
        try
        {
            var pathChanges = CollectRendererPathChanges(urpAssets);
            var drawerChanges = CollectDrawerModeChanges(urpAssets);

            // Show prompt if any change is suggested
            if (pathChanges.Count > 0 || drawerChanges.Count > 0)
            {
                var message = new StringBuilder();
                message.AppendLine($"Scanned {urpAssets.Count} configured URP asset(s).");
                message.AppendLine();

                if (pathChanges.Count > 0)
                {
                    message.AppendLine("Forward+ / Deferred+ rendering may have compatibility issues with some Babylon Toolkit features. Forward rendering is recommended for better compatibility.");
                    message.AppendLine();
                }
                if (drawerChanges.Count > 0)
                {
                    // The GPU Resident Drawer is a Unity-only runtime batching system that plays no
                    // part in the Babylon export. Left enabled it either spams the console on Forward
                    // ("Disabled due to some configured Universal Renderers not using the Forward+ or
                    // Deferred+ rendering paths") or trips the URP job safety checks on Forward+, so
                    // recommend turning it off either way.
                    message.AppendLine("The GPU Resident Drawer is not used by the Babylon Toolkit export and logs repeated console warnings or job errors depending on the rendering path. Disabling it is recommended.");
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

                if (EditorUtility.DisplayDialog("URP Settings Recommendation", message.ToString(), "Yes, Change", "No, Keep Current"))
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
                    }

                    if (applied.Count > 0)
                    {
                        EditorUtility.DisplayDialog("URP Settings Updated",
                            "Successfully applied:\n\n  • " + string.Join("\n  • ", applied),
                            "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("URP Settings Updated",
                            "No settings could be changed. Check console for details.",
                            "OK");
                    }
                }
                else
                {
                    Debug.Log("[Babylon Toolkit] User chose to keep current URP settings");
                }
            }
            else if (!silent)
            {
                // If called manually and no issues found, show confirmation
                var message = new StringBuilder();
                message.AppendLine($"Scanned {urpAssets.Count} configured URP asset(s).");
                message.AppendLine();
                foreach (var asset in urpAssets)
                {
                    message.AppendLine($"{asset.name}");
                    foreach (var rendererData in GetAllRendererData(asset))
                    {
                        message.AppendLine($"  • {rendererData.name}: {GetRendererDataRenderingPath(rendererData)}");
                    }
                    message.AppendLine($"  • GPU Resident Drawer: {GetGPUResidentDrawerMode(asset)}");
                }
                message.AppendLine();
                message.AppendLine("These settings are compatible with Babylon Toolkit.");

                EditorUtility.DisplayDialog("URP Render Path Check", message.ToString(), "OK");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Error during URP settings validation: {ex.Message}");
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
    /// Returns every distinct URP asset configured for the project: the graphics default plus
    /// the per-quality-level overrides. Unity validates the GPU Resident Drawer against all of
    /// them, so a stale setting on an inactive quality tier still produces console warnings.
    /// </summary>
    private static List<RenderPipelineAsset> CollectConfiguredURPAssets()
    {
        var assets = new List<RenderPipelineAsset>();
        var seen = new HashSet<int>();

        AddURPAsset(GraphicsSettings.defaultRenderPipeline, assets, seen);
        AddURPAsset(QualitySettings.renderPipeline, assets, seen);

        // Sweep every quality level, not just the active one
        var levelNames = QualitySettings.names;
        int levelCount = levelNames != null ? levelNames.Length : 0;
        for (int i = 0; i < levelCount; i++)
        {
            try
            {
                AddURPAsset(QualitySettings.GetRenderPipelineAssetAt(i), assets, seen);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Babylon Toolkit] Could not read the Render Pipeline Asset for quality level {i}: {ex.Message}");
            }
        }

        return assets;
    }

    private static void AddURPAsset(RenderPipelineAsset asset, List<RenderPipelineAsset> assets, HashSet<int> seen)
    {
        if (asset == null) return;
        if (!HasURPRendererData(asset)) return;
        if (!seen.Add(asset.GetInstanceID())) return;
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

    // =========================================================
    // Renderer Data helpers
    // =========================================================
    /// <summary>
    /// Gets every ScriptableRendererData on a URP asset, not just the default one. Unity's own
    /// IsGPUResidentDrawerSupportedBySRP iterates the whole list, and a single Forward renderer
    /// anywhere in it is enough to disable the drawer for the entire asset.
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
    // Read / Write Rendering Mode
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
    /// </summary>
    private static List<RendererPathChange> CollectRendererPathChanges(List<RenderPipelineAsset> urpAssets)
    {
        var changes = new List<RendererPathChange>();

        foreach (var asset in urpAssets)
        {
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
        var dirtyAssets = new HashSet<int>();

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
            if (dirtyAssets.Add(change.owner.GetInstanceID()))
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
    // Read / Write GPU Resident Drawer
    // =========================================================
    /// <summary>
    /// Gets the current GPU Resident Drawer mode from a URP asset via reflection.
    /// Returns user-friendly names: "Disabled", "Instanced Drawing", or "Unsupported"
    /// when the active URP version predates the GPU Resident Drawer.
    /// </summary>
    private static string GetGPUResidentDrawerMode(RenderPipelineAsset urpAsset)
    {
        try
        {
            if (urpAsset == null) return DRAWER_UNSUPPORTED;

            var urpAssetType = urpAsset.GetType();

            // Try to get the gpuResidentDrawerMode property from the pipeline asset
            object drawerMode = null;
            var drawerModeProperty = urpAssetType.GetProperty("gpuResidentDrawerMode", BindingFlags.Public | BindingFlags.Instance);
            if (drawerModeProperty != null)
            {
                drawerMode = drawerModeProperty.GetValue(urpAsset);
            }
            else
            {
                // Fallback: private serialized field
                var drawerModeField = urpAssetType.GetField("m_GPUResidentDrawerMode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (drawerModeField != null)
                {
                    drawerMode = drawerModeField.GetValue(urpAsset);
                }
            }

            return FormatGPUResidentDrawerMode(drawerMode);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Babylon Toolkit] Error determining GPU Resident Drawer mode: {ex.Message}\n{ex.StackTrace}");
            return DRAWER_UNSUPPORTED;
        }
    }

    /// <summary>
    /// Finds every configured URP asset with the GPU Resident Drawer enabled.
    /// </summary>
    private static List<DrawerModeChange> CollectDrawerModeChanges(List<RenderPipelineAsset> urpAssets)
    {
        var changes = new List<DrawerModeChange>();

        foreach (var asset in urpAssets)
        {
            var currentMode = GetGPUResidentDrawerMode(asset);
            if (!IsGPUResidentDrawerEnabled(currentMode)) continue;

            changes.Add(new DrawerModeChange
            {
                asset = asset,
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
            Debug.Log($"[Babylon Toolkit] Changed GPU Resident Drawer on {change.asset.name} from {change.currentMode} to {change.targetMode}");
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
    /// Sets m_GPUResidentDrawerMode on a single URP asset. Parses by enum name
    /// (case-insensitive) to avoid hard-coded int ordinals across URP versions.
    /// </summary>
    private static bool WriteGPUResidentDrawerMode(RenderPipelineAsset urpAsset, string desiredEnumName)
    {
        try
        {
            var urpAssetType = urpAsset.GetType();

            // Discover enum type from property or field
            var drawerModeProperty = urpAssetType.GetProperty("gpuResidentDrawerMode", BindingFlags.Public | BindingFlags.Instance);
            var drawerModeField = urpAssetType.GetField("m_GPUResidentDrawerMode", BindingFlags.NonPublic | BindingFlags.Instance);
            System.Type enumType = drawerModeProperty?.PropertyType ?? drawerModeField?.FieldType;

            if (enumType == null || !enumType.IsEnum)
            {
                Debug.LogWarning($"[Babylon Toolkit] This URP version does not support the GPU Resident Drawer ({urpAsset.name}).");
                return false;
            }

            object enumValue;
            try
            {
                enumValue = System.Enum.Parse(enumType, desiredEnumName, ignoreCase: true);
            }
            catch
            {
                Debug.LogWarning($"[Babylon Toolkit] GPU Resident Drawer mode \"{desiredEnumName}\" not supported by this URP version.");
                return false;
            }

            // Prefer the property: its setter calls OnValidate(), which tears down the
            // running GPU Resident Drawer immediately instead of waiting for a reload.
            if (drawerModeProperty != null && drawerModeProperty.CanWrite)
            {
                drawerModeProperty.SetValue(urpAsset, enumValue);
                return true;
            }

            if (drawerModeField != null)
            {
                drawerModeField.SetValue(urpAsset, enumValue);

                // Field writes bypass the property setter, so poke OnValidate() manually
                var onValidate = urpAssetType.GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                if (onValidate != null)
                {
                    try { onValidate.Invoke(urpAsset, null); }
                    catch { /* best effort - the value is still serialized by the caller */ }
                }
                return true;
            }

            Debug.LogWarning($"[Babylon Toolkit] No writable GPU Resident Drawer member found on {urpAsset.name}.");
            return false;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Failed to set GPU Resident Drawer mode on {urpAsset.name}: {ex.Message}");
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
    /// not absent from this URP version).
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

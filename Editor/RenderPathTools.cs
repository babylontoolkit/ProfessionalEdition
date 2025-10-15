#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Reflection;

public static class RenderPathTools
{
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

    [MenuItem("Tools/Babylon Toolkit/Check Render Path", false, 50)]
    public static void CheckRenderSettings()
    {
        ValidateRenderPipeline(false);
    }

    public static void ValidateRenderSettings()
    {
        ValidateRenderPipeline(true);
    }

    // =========================================================
    // Validation
    // =========================================================
    /// <summary>
    /// Validates the active render pipeline and its URP rendering path (Forward/Deferred/ForwardPlus).
    /// Works whether URP is assigned globally, per-quality, or both.
    /// </summary>
    private static void ValidateRenderPipeline(bool silent = true)
    {
        try
        {
            var renderPipelineAsset = GetActiveRenderPipelineAsset();

            // Handle Built-in Render Pipeline (no active RP asset)
            if (renderPipelineAsset == null)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Render Pipeline Check",
                        "Built-in Render Pipeline is active (no URP asset assigned).",
                        "OK");
                }
                return;
            }

            // Determine if this looks like a URP asset by probing renderer data via reflection
            bool isURP = HasURPRendererData(renderPipelineAsset);
            if (!isURP)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Render Pipeline Check",
                        $"Active Render Pipeline is not recognized as URP. Type: {renderPipelineAsset.GetType().Name}",
                        "OK");
                }
                return;
            }

            ValidateURPSettings(renderPipelineAsset, silent);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Error during render pipeline validation: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates URP-specific settings, particularly the rendering path.
    /// Suggests changes if the current rendering path may cause compatibility issues.
    /// </summary>
    private static void ValidateURPSettings(RenderPipelineAsset renderPipelineAsset, bool silent)
    {
        try
        {
            // Get the current rendering path
            var currentRenderPath = GetURPRenderingPath(renderPipelineAsset);

            // Check if we need to suggest a change
            string suggestedPath = null;
            string reason = null;

            if (currentRenderPath.Equals("Forward+", System.StringComparison.OrdinalIgnoreCase))
            {
                suggestedPath = "Forward";
                reason = "Forward+ rendering may have compatibility issues with some Babylon Toolkit features. Forward rendering is recommended for better compatibility.";
            }
            else if (currentRenderPath.Equals("Deferred+", System.StringComparison.OrdinalIgnoreCase))
            {
                // "Deferred+" isn't an official public mode in most URP versions; treat as Deferred if encountered.
                suggestedPath = "Deferred";
                reason = "This project reports a 'Deferred+' mode. Standard Deferred rendering is recommended for compatibility.";
            }

            // Show prompt if a change is suggested
            if (!string.IsNullOrEmpty(suggestedPath))
            {
                string message = $"Current URP Rendering Path: {currentRenderPath}\n\n{reason}\n\nWould you like to change to {suggestedPath} rendering?";

                if (EditorUtility.DisplayDialog("URP Render Path Recommendation", message, "Yes, Change", "No, Keep Current"))
                {
                    if (SetURPRenderPath(suggestedPath))
                    {
                        Debug.Log($"[Babylon Toolkit] Automatically changed render path from {currentRenderPath} to {suggestedPath}");
                    }
                }
                else
                {
                    Debug.Log($"[Babylon Toolkit] User chose to keep current render path: {currentRenderPath}");
                }
            }
            else if (!silent)
            {
                // If called manually and no issues found, show confirmation
                string message = $"Current URP Rendering Path: {currentRenderPath}\n\nThis render path is compatible with Babylon Toolkit.";
                EditorUtility.DisplayDialog("URP Render Path Check", message, "OK");
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
    /// Gets the default ScriptableRendererData from a URP asset using m_DefaultRendererIndex.
    /// </summary>
    private static UnityEngine.Object GetDefaultRendererData(RenderPipelineAsset urpAsset)
    {
        var t = urpAsset.GetType();

        // IList of ScriptableRendererData
        var listField = t.GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        var listObj = listField?.GetValue(urpAsset) as System.Collections.IList;
        if (listObj == null || listObj.Count == 0) return null;

        int defaultIndex = 0;
        var defaultIdxField = t.GetField("m_DefaultRendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        if (defaultIdxField != null)
        {
            try
            {
                defaultIndex = (int)defaultIdxField.GetValue(urpAsset);
                if (defaultIndex < 0 || defaultIndex >= listObj.Count) defaultIndex = 0;
            }
            catch { defaultIndex = 0; }
        }

        return listObj[defaultIndex] as UnityEngine.Object;
    }

    // =========================================================
    // Read / Write Rendering Mode
    // =========================================================
    /// <summary>
    /// Gets the current URP rendering path by accessing the default renderer data via reflection.
    /// Returns user-friendly names: "Forward", "Deferred", "Forward+"
    /// </summary>
    private static string GetURPRenderingPath(RenderPipelineAsset urpAsset)
    {
        try
        {
            var firstRendererData = GetDefaultRendererData(urpAsset);
            if (firstRendererData == null)
                return "Unknown";

            var rendererDataType = firstRendererData.GetType();

            // Try to get the renderingMode property from the renderer data
            object renderingMode = null;
            var renderingModeProperty = rendererDataType.GetProperty("renderingMode", BindingFlags.Public | BindingFlags.Instance);
            if (renderingModeProperty != null)
            {
                renderingMode = renderingModeProperty.GetValue(firstRendererData);
            }
            else
            {
                // Fallback: private field
                var renderingModeField = rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (renderingModeField != null)
                {
                    renderingMode = renderingModeField.GetValue(firstRendererData);
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
    /// Sets the URP rendering path to the specified mode.
    /// Valid modes are: "Forward", "Deferred", "Forward+"
    /// </summary>
    private static bool SetURPRenderPath(string targetRenderPath)
    {
        try
        {
            var renderPipelineAsset = GetActiveRenderPipelineAsset();

            if (renderPipelineAsset == null)
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    "No active Render Pipeline Asset found (Built-in or not assigned).",
                    "OK");
                return false;
            }

            if (!HasURPRendererData(renderPipelineAsset))
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    $"The active Render Pipeline Asset does not appear to be URP. Type: {renderPipelineAsset.GetType().Name}",
                    "OK");
                return false;
            }

            var firstRendererData = GetDefaultRendererData(renderPipelineAsset);
            if (firstRendererData == null)
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    "Could not locate the default URP Renderer Data on the active asset.",
                    "OK");
                return false;
            }

            var rendererDataType = firstRendererData.GetType();

            // Normalize target to enum name
            string desiredEnumName = NormalizeTargetModeName(targetRenderPath);
            if (desiredEnumName == null)
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    $"Invalid render path: {targetRenderPath}. Valid options are: Forward, Deferred, Forward+",
                    "OK");
                return false;
            }

            // Discover enum type from property or field
            var renderingModeProperty = rendererDataType.GetProperty("renderingMode", BindingFlags.Public | BindingFlags.Instance);
            System.Type enumType = renderingModeProperty?.PropertyType 
                                   ?? rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType;

            if (enumType == null || !enumType.IsEnum)
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    "Could not determine URP rendering mode enum type.",
                    "OK");
                return false;
            }

            // Parse by enum name (case-insensitive). This avoids hard-coded int ordinals.
            object enumValue;
            try
            {
                enumValue = System.Enum.Parse(enumType, desiredEnumName, ignoreCase: true);
            }
            catch
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    $"Render mode \"{desiredEnumName}\" not supported by this URP version.",
                    "OK");
                return false;
            }

            bool written = false;
            try
            {
                if (renderingModeProperty != null && renderingModeProperty.CanWrite)
                {
                    renderingModeProperty.SetValue(firstRendererData, enumValue);
                    written = true;
                }
                else
                {
                    var renderingModeField = rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (renderingModeField != null)
                    {
                        renderingModeField.SetValue(firstRendererData, enumValue);
                        written = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Babylon Toolkit] Failed to set URP rendering mode: {ex.Message}");
            }

            if (written)
            {
                EditorUtility.SetDirty(firstRendererData as UnityEngine.Object);
                EditorUtility.SetDirty(renderPipelineAsset);
                AssetDatabase.SaveAssets();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                EditorUtility.DisplayDialog("Set Render Path",
                    $"Successfully changed URP Rendering Path to: {targetRenderPath}",
                    "OK");
                return true;
            }

            EditorUtility.DisplayDialog("Set Render Path",
                "Failed to set render path. Check console for details.",
                "OK");
            return false;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Babylon Toolkit] Error setting URP rendering path: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("Set Render Path",
                $"Error setting render path: {ex.Message}",
                "OK");
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
            // "Deferred+" is not a standard public mode; deliberately unsupported.
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
            // Unknown / vendor modified names
            default:
                return modeStr;
        }
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

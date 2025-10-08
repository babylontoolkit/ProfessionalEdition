#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Reflection;

public static class RenderPathTools
{
    [InitializeOnLoadMethod]
    static void InitializeRenderPathTools()
    {
        CanvasTools.EditorHooks.MainWindowOnEnable += ValidateRenderSettings;
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

    /// <summary>
    /// Validates the current render pipeline and its settings.
    /// If URP is detected, checks the rendering path and suggests changes if necessary.
    /// </summary>
    private static void ValidateRenderPipeline(bool silent = true)
    {
        try
        {
            var renderPipelineAsset = GraphicsSettings.defaultRenderPipeline;

            // Handle Built-in Render Pipeline (no asset assigned)
            if (renderPipelineAsset == null)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Babylon Toolkit - Render Pipeline Check",
                        "This project is using the Built-in Render Pipeline (not URP).",
                        "OK");
                }
                return;
            }

            var assetTypeName = renderPipelineAsset.GetType().Name;

            // Only validate URP projects
            if (assetTypeName.Contains("Universal") || assetTypeName.Contains("URP"))
            {
                ValidateURPSettings(renderPipelineAsset, silent);
            }
            else
            {
                // Non-URP project - just show message if manual check
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Babylon Toolkit - Render Pipeline Check",
                        $"This project is not using URP (Current: {assetTypeName}).",
                        "OK");
                }
            }
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
                suggestedPath = "Deferred";
                reason = "Deferred+ rendering may have compatibility issues with some Babylon Toolkit features. Standard Deferred rendering is recommended for better compatibility.";
            }

            // Show prompt if a change is suggested
            if (!string.IsNullOrEmpty(suggestedPath))
            {
                string message = $"Current URP Rendering Path: {currentRenderPath}\n\n{reason}\n\nWould you like to change to {suggestedPath} rendering?";

                if (EditorUtility.DisplayDialog("Babylon Toolkit - URP Render Path Recommendation", message, "Yes, Change", "No, Keep Current"))
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
                EditorUtility.DisplayDialog("Babylon Toolkit - URP Render Path Check", message, "OK");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Babylon Toolkit] Error during URP settings validation: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current URP rendering path by accessing the renderer data via reflection.
    /// </summary>
    /// <param name="urpAsset"></param>
    /// <returns>string</returns> 
    private static string GetURPRenderingPath(RenderPipelineAsset urpAsset)
    {
        try
        {
            var assetType = urpAsset.GetType();
            object rendererDataList = null;

            // Try to get the renderer data list from the URP asset
            // Start with the private field first (safer)
            try
            {
                var rendererDataListField = assetType.GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (rendererDataListField != null)
                {
                    rendererDataList = rendererDataListField.GetValue(urpAsset);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Babylon Toolkit] Failed to access m_RendererDataList field: {ex.Message}");
            }

            // If that fails, try the public property with extra caution
            if (rendererDataList == null)
            {
                try
                {
                    var rendererDataListProperty = assetType.GetProperty("rendererDataList", BindingFlags.Public | BindingFlags.Instance);
                    if (rendererDataListProperty != null)
                    {
                        rendererDataList = rendererDataListProperty.GetValue(urpAsset);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Babylon Toolkit] Failed to access rendererDataList property: {ex.Message}");
                }
            }

            if (rendererDataList != null && rendererDataList is System.Collections.IList list && list.Count > 0)
            {
                // Get the first (default) renderer data
                var firstRendererData = list[0];
                if (firstRendererData != null)
                {
                    var rendererDataType = firstRendererData.GetType();

                    // Try to get the renderingMode property from the renderer data
                    try
                    {
                        var renderingModeProperty = rendererDataType.GetProperty("renderingMode", BindingFlags.Public | BindingFlags.Instance);
                        if (renderingModeProperty != null)
                        {
                            var renderingMode = renderingModeProperty.GetValue(firstRendererData);
                            if (renderingMode != null)
                            {
                                return FormatRenderingMode(renderingMode);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[Babylon Toolkit] Failed to access renderingMode property: {ex.Message}");
                    }

                    // Fallback: try to access the private field directly
                    try
                    {
                        var renderingModeField = rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (renderingModeField != null)
                        {
                            var renderingMode = renderingModeField.GetValue(firstRendererData);
                            if (renderingMode != null)
                            {
                                return FormatRenderingMode(renderingMode);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[Babylon Toolkit] Failed to access m_RenderingMode field: {ex.Message}");
                    }
                }
            }

            // Fallback: Try other methods if renderer data access fails
            Debug.LogWarning("[Babylon Toolkit] Could not access renderer data, trying alternative detection methods");
            return TryAlternativeDetection(assetType, urpAsset);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Babylon Toolkit] Error determining URP rendering path: {ex.Message}\n{ex.StackTrace}");
            return "Unknown (Error during detection)";
        }
    }
    
    /// <summary>
    /// Sets the URP rendering path to the specified mode.
    /// Valid modes are: "Forward", "Forward+", "Deferred", "Deferred+"
    /// </summary>
    /// <param name="targetRenderPath"></param>
    /// <returns>bool</returns> 
    private static bool SetURPRenderPath(string targetRenderPath)
    {
        try
        {
            var renderPipelineAsset = GraphicsSettings.defaultRenderPipeline;

            if (renderPipelineAsset == null)
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    "No Render Pipeline Asset is assigned. This project is using the Built-in Render Pipeline.",
                    "OK");
                return false;
            }

            var assetTypeName = renderPipelineAsset.GetType().Name;

            if (!assetTypeName.Contains("Universal") && !assetTypeName.Contains("URP"))
            {
                EditorUtility.DisplayDialog("Set Render Path",
                    $"The current Render Pipeline Asset is not URP. Type: {assetTypeName}",
                    "OK");
                return false;
            }

            var assetType = renderPipelineAsset.GetType();
            object rendererDataList = null;

            // Get the renderer data list
            try
            {
                var rendererDataListField = assetType.GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (rendererDataListField != null)
                {
                    rendererDataList = rendererDataListField.GetValue(renderPipelineAsset);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Babylon Toolkit] Failed to access renderer data list: {ex.Message}");
            }

            if (rendererDataList != null && rendererDataList is System.Collections.IList list && list.Count > 0)
            {
                // Get the first (default) renderer data
                var firstRendererData = list[0];
                if (firstRendererData != null)
                {
                    var rendererDataType = firstRendererData.GetType();

                    // Convert target render path to enum value
                    int renderModeValue = ConvertRenderPathToEnum(targetRenderPath);
                    if (renderModeValue == -1)
                    {
                        EditorUtility.DisplayDialog("Set Render Path",
                            $"Invalid render path: {targetRenderPath}. Valid options are: Forward, Forward+, Deferred, Deferred+",
                            "OK");
                        return false;
                    }

                    // Try to set via public property first
                    try
                    {
                        var renderingModeProperty = rendererDataType.GetProperty("renderingMode", BindingFlags.Public | BindingFlags.Instance);
                        if (renderingModeProperty != null && renderingModeProperty.CanWrite)
                        {
                            // Create enum value - we need to get the actual enum type
                            var enumType = renderingModeProperty.PropertyType;
                            var enumValue = System.Enum.ToObject(enumType, renderModeValue);

                            renderingModeProperty.SetValue(firstRendererData, enumValue);

                            // Mark the asset as dirty so changes are saved
                            EditorUtility.SetDirty(firstRendererData as UnityEngine.Object);
                            EditorUtility.SetDirty(renderPipelineAsset);

                            EditorUtility.DisplayDialog("Set Render Path",
                                $"Successfully changed URP Rendering Path to: {targetRenderPath}",
                                "OK");
                            return true;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[Babylon Toolkit] Failed to set renderingMode property: {ex.Message}");
                    }

                    // Fallback: try to set via private field
                    try
                    {
                        var renderingModeField = rendererDataType.GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (renderingModeField != null)
                        {
                            // Create enum value
                            var enumType = renderingModeField.FieldType;
                            var enumValue = System.Enum.ToObject(enumType, renderModeValue);

                            renderingModeField.SetValue(firstRendererData, enumValue);

                            // Mark the asset as dirty so changes are saved
                            EditorUtility.SetDirty(firstRendererData as UnityEngine.Object);
                            EditorUtility.SetDirty(renderPipelineAsset);

                            EditorUtility.DisplayDialog("Set Render Path",
                                $"Successfully changed URP Rendering Path to: {targetRenderPath}",
                                "OK");
                            return true;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[Babylon Toolkit] Failed to set m_RenderingMode field: {ex.Message}");
                    }
                }
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

    ////////////////////////////////////////////////////////////////////////////////////////////////
    /// PRIVATE RENDER PATH HELPER FUNCTIONS
    ////////////////////////////////////////////////////////////////////////////////////////////////

    private static int ConvertRenderPathToEnum(string renderPath)
    {
        switch (renderPath.ToLower())
        {
            case "forward":
                return 0;
            case "deferred":
                return 1;
            case "forward+":
            case "forwardplus":
                return 2;
            case "deferred+":
            case "deferredplus":
                return 3;
            default:
                return -1;
        }
    }
    
    private static string FormatRenderingMode(object renderingMode)
    {
        if (renderingMode == null) return "Unknown";

        var modeStr = renderingMode.ToString();

        // Handle the RenderingMode enum values
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
            default:
                // Handle enum values that might be numbers
                if (int.TryParse(modeStr, out int enumValue))
                {
                    switch (enumValue)
                    {
                        case 0: return "Forward";
                        case 1: return "Deferred";
                        case 2: return "Forward+";
                        case 3: return "Deferred+";
                        default: return $"Unknown Mode ({enumValue})";
                    }
                }
                return modeStr;
        }
    }
    
    private static string TryAlternativeDetection(System.Type assetType, RenderPipelineAsset urpAsset)
    {
        // Log all available properties for debugging
        var allProperties = assetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var allFields = assetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Debug.Log($"[Babylon Toolkit] URP Asset Type: {assetType.FullName}");
        // Debug.Log($"[Babylon Toolkit] Available Properties: {string.Join(", ", System.Array.ConvertAll(allProperties, p => p.Name))}");
        // Debug.Log($"[Babylon Toolkit] Available Fields: {string.Join(", ", System.Array.ConvertAll(allFields, f => f.Name))}");
        
        // Try some other possible property names
        string[] possibleProperties = { 
            "renderingPath", "RenderingPath", "m_RenderingPath", 
            "defaultRenderer", "m_DefaultRenderer",
            "rendererType", "m_RendererType"
        };
        
        foreach (var propName in possibleProperties)
        {
            var prop = assetType.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                var value = prop.GetValue(urpAsset);
                if (value != null)
                {
                    // Debug.Log($"[Babylon Toolkit] Found property '{propName}': {value} (Type: {value.GetType()})");
                }
            }
            
            var field = assetType.GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var value = field.GetValue(urpAsset);
                if (value != null)
                {
                    // Debug.Log($"[Babylon Toolkit] Found field '{propName}': {value} (Type: {value.GetType()})");
                }
            }
        }
        
        return "Forward (Could not determine - check console for details)";
    }
    
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
            
            case "deferred+":
            case "deferredplus":
                return "Deferred+ Rendering: Enhanced deferred rendering with additional optimizations and features.";
            
            default:
                return "Rendering path details not available for this configuration.";
        }
    }
}

#endif
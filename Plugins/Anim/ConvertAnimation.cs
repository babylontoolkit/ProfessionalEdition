using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class ConvertAnimation : Editor
{
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Animation Utilities/Convert Animation Clip", false, 52)]
    public static void Convert()
    {
        string dialog_warning = "Unity does not support converting animation clip formats. The Animation Converter package on the Unity Asset Store is recommended to convert animation clip formats.";
        string console_warning = "Unity does not support converting animation clip flormats. The <a href=\"https://assetstore.unity.com/packages/tools/animation/animation-converter-107688\">Animation Converter</a> package on the Unity Asset Store is recommended to convert animation clip formats.";
        UnityEngine.Debug.LogWarning(console_warning);
        EditorUtility.DisplayDialog("Babylon Toolkit", dialog_warning, "OK");
    }
}

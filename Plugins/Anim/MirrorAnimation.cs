using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class MirrorAnimation : Editor
{
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Animation Utilities/Mirror Animation Clip", false, 51)]
    public static void Mirror()
    {
        string dialog_warning = "Unity does not support creating mirrored animation clips. The Very Animation package on the Unity Asset Store is recommended to mirror animation clips.";
        string console_warning = "Unity does not support creating mirrored animation clips. The <a href=\"https://assetstore.unity.com/packages/tools/animation/very-animation-96826\">Very Animation</a> package on the Unity Asset Store is recommended to mirror animation clips.";
        UnityEngine.Debug.LogWarning(console_warning);
        EditorUtility.DisplayDialog("Babylon Toolkit", dialog_warning, "OK");
    }
}

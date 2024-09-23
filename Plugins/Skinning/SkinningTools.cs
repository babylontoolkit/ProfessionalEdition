using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class SkinningTools : Editor
{
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Pro Skinning Tool", false, 50)]
    public static void Skinning()
    {
        string dialog_warning = "Unity does not support combining skin meshes. The Skinn Pro package on the Unity Asset Store is recommended to combine skin meshes.";
        string console_warning = "Unity does not support combining skin meshes. The <a href=\"https://assetstore.unity.com/packages/tools/modeling/skinn-pro-86532\">Skinn Pro</a> package on the Unity Asset Store is recommended to combine skin meshes.";
        UnityEngine.Debug.LogWarning(console_warning);
        EditorUtility.DisplayDialog("Babylon Toolkit", dialog_warning, "OK");
    }
}

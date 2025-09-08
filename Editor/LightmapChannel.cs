// Assets/Editor/LightmapChannel.cs
using UnityEditor;
using UnityEngine;
using System.IO;

public class LightmapChannel : EditorWindow
{
    private Object _target; // Mesh OR GameObject with MeshFilter/SkinnedMeshRenderer
    private Mesh _resolvedMesh;

    // Unwrap params (sane speed-friendly defaults)
    private float hardAngle  = 75f;  // Fewer splits with higher angles
    private float angleError = 10f;  // Distortion tolerance
    private float areaError  = 20f;  // Area deviation tolerance
    private float packMargin = 2f;   // In pixels (relative to target lightmap res)

    private enum SaveMode { Auto, OverwriteAsset, NewAssetCopy }
    private SaveMode saveMode = SaveMode.Auto;

    private bool assignNewAssetToRenderers = true; // if we create a copy, assign it back

    [MenuItem("Window/Lightmaps/Lightmap UV Regenerator")]
    public static void ShowWindow()
    {
        var w = GetWindow<LightmapChannel>("Lightmap UVs");
        w.minSize = new Vector2(420, 320);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _target = EditorGUILayout.ObjectField("Mesh / GameObject", _target, typeof(Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            _resolvedMesh = ResolveMesh(_target);
        }

        using (new EditorGUI.DisabledScope(_resolvedMesh == null))
        {
            DrawResolvedMeshInfo();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Unwrap Parameters", EditorStyles.boldLabel);

            hardAngle  = EditorGUILayout.Slider(new GUIContent("Hard Angle", "Angle for chart splitting"), hardAngle, 1f, 180f);
            angleError = EditorGUILayout.Slider(new GUIContent("Angle Error", "Angular distortion allowed"), angleError, 1f, 75f);
            areaError  = EditorGUILayout.Slider(new GUIContent("Area Error", "Area distortion allowed"), areaError, 1f, 75f);
            packMargin = EditorGUILayout.Slider(new GUIContent("Pack Margin (px)", "Padding between charts in pixels"), packMargin, 0f, 16f);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Save Options", EditorStyles.boldLabel);
            saveMode = (SaveMode)EditorGUILayout.EnumPopup(new GUIContent("Save Mode",
                "Auto: overwrite Mesh assets, create copy for FBX/model meshes.\nOverwriteAsset: write UV2 directly to Mesh asset (fails for FBX).\nNewAssetCopy: duplicate Mesh to a new .asset file."), saveMode);

            if (saveMode == SaveMode.NewAssetCopy || saveMode == SaveMode.Auto)
            {
                assignNewAssetToRenderers = EditorGUILayout.Toggle(new GUIContent("Assign New Asset To Selection",
                    "If a new Mesh asset is created, reassign it to the MeshFilter/SkinnedMeshRenderer on the selected object."), assignNewAssetToRenderers);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Generate & Save UV2", GUILayout.Height(40)))
            {
                GenerateAndSave();
            }
        }

        if (_resolvedMesh == null)
        {
            EditorGUILayout.HelpBox("Drag a Mesh, or a GameObject with a MeshFilter/SkinnedMeshRenderer.", MessageType.Info);
        }
    }

    private void DrawResolvedMeshInfo()
    {
        if (_resolvedMesh == null) return;

        var path = AssetDatabase.GetAssetPath(_resolvedMesh);
        var isAsset = AssetDatabase.Contains(_resolvedMesh);
        var isModelSubAsset = IsModelSubAsset(_resolvedMesh);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Resolved Mesh", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Name:", _resolvedMesh.name);
            EditorGUILayout.LabelField("Triangles:", _resolvedMesh.triangles != null ? (_resolvedMesh.triangles.Length / 3).ToString() : "N/A");
            EditorGUILayout.LabelField("Asset Path:", string.IsNullOrEmpty(path) ? "(not an asset)" : path);
            EditorGUILayout.LabelField("Asset Type:", isModelSubAsset ? "Model (FBX) Sub-Asset" : (isAsset ? "Mesh Asset" : "Scene-only Mesh"));
        }
    }

    private Mesh ResolveMesh(Object target)
    {
        if (target == null) return null;

        if (target is Mesh mesh) return mesh;

        if (target is GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null) return smr.sharedMesh;
        }

        return null;
    }

    private void GenerateAndSave()
    {
        if (_resolvedMesh == null)
        {
            ShowNotification(new GUIContent("No mesh resolved."));
            return;
        }

        // Build unwrap params
        var p = new UnwrapParam();
        UnwrapParam.SetDefaults(out p);
        p.hardAngle  = hardAngle;
        p.angleError = angleError;
        p.areaError  = areaError;
        p.packMargin = packMargin;

        // Decide save path/mode
        var isModelSubAsset = IsModelSubAsset(_resolvedMesh);
        var isAsset = AssetDatabase.Contains(_resolvedMesh);

        SaveMode chosen = saveMode;
        if (saveMode == SaveMode.Auto)
        {
            chosen = isModelSubAsset || !isAsset ? SaveMode.NewAssetCopy : SaveMode.OverwriteAsset;
        }

        try
        {
            if (chosen == SaveMode.OverwriteAsset && isAsset && !isModelSubAsset)
            {
                // Overwrite UV2 on the existing Mesh asset.
                Undo.RecordObject(_resolvedMesh, "Regenerate Lightmap UVs");
                Unwrapping.GenerateSecondaryUVSet(_resolvedMesh, p);
                EditorUtility.SetDirty(_resolvedMesh);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Lightmap UVs", $"UV2 regenerated and saved on asset:\n{AssetDatabase.GetAssetPath(_resolvedMesh)}", "OK");
            }
            else
            {
                // Create a new Mesh asset, copy geometry, generate UV2 on the copy, and (optionally) assign.
                var newMesh = DuplicateMesh(_resolvedMesh);
                newMesh.name = _resolvedMesh.name + "_LMUV";

                // Generate on the copy
                Unwrapping.GenerateSecondaryUVSet(newMesh, p);

                // Choose save location
                string defaultDir = "Assets";
                string baseName = newMesh.name + ".asset";
                string targetPath = EditorUtility.SaveFilePanelInProject("Save New Mesh Asset", baseName, "asset", "Choose location to save the new Mesh asset", defaultDir);
                if (string.IsNullOrEmpty(targetPath))
                {
                    DestroyImmediate(newMesh);
                    return; // user canceled
                }

                AssetDatabase.CreateAsset(newMesh, targetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Assign to selected renderers if requested and the user selected a GO
                if (assignNewAssetToRenderers && _target is GameObject go)
                {
                    var mf = go.GetComponent<MeshFilter>();
                    if (mf != null)
                    {
                        Undo.RecordObject(mf, "Assign New Mesh");
                        mf.sharedMesh = newMesh;
                        EditorUtility.SetDirty(mf);
                    }
                    var smr = go.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        Undo.RecordObject(smr, "Assign New Mesh");
                        smr.sharedMesh = newMesh;
                        EditorUtility.SetDirty(smr);
                    }
                }

                EditorUtility.DisplayDialog("Lightmap UVs", $"Created new Mesh asset with regenerated UV2:\n{targetPath}", "OK");
            }
        }
        catch (System.SystemException ex)
        {
            Debug.LogError($"Lightmap UV Regenerator error: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("Error", "Failed to regenerate or save UVs. See Console for details.", "OK");
        }
    }

    private static bool IsModelSubAsset(Mesh m)
    {
        // FBX/model meshes are sub-assets where the importer is a ModelImporter
        string path = AssetDatabase.GetAssetPath(m);
        if (string.IsNullOrEmpty(path)) return false;
        var importer = AssetImporter.GetAtPath(path);
        return importer != null && importer.GetType().Name == "ModelImporter";
    }

    private static Mesh DuplicateMesh(Mesh src)
    {
        var dst = new Mesh();
        dst.indexFormat = src.indexFormat; // keep 16/32-bit
        dst.vertices = src.vertices;
        dst.normals = src.normals;
        dst.tangents = src.tangents;
        dst.colors = src.colors;
#if UNITY_2020_1_OR_NEWER
        dst.colors32 = src.colors32;
#endif
        // Copy UV channels
        dst.uv  = src.uv;
        dst.uv2 = src.uv2;
        dst.uv3 = src.uv3;
        dst.uv4 = src.uv4;
#if UNITY_2018_2_OR_NEWER
        dst.uv5 = src.uv5;
        dst.uv6 = src.uv6;
        dst.uv7 = src.uv7;
        dst.uv8 = src.uv8;
#endif
        dst.subMeshCount = src.subMeshCount;
        for (int i = 0; i < src.subMeshCount; i++)
            dst.SetIndices(src.GetIndices(i), src.GetTopology(i), i);

        dst.bindposes = src.bindposes;
        dst.boneWeights = src.boneWeights;
        dst.name = src.name + "_Copy";

        // Recalculate bounds just in case
        dst.bounds = src.bounds;

        return dst;
    }
}

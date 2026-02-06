// Assets/Editor/VertexAnimation.cs
using UnityEditor;
using UnityEngine;
using System.IO;

public class VertexAnimation : EditorWindow
{
    private Object _target; // Mesh OR GameObject with MeshFilter/SkinnedMeshRenderer
    private Mesh _resolvedMesh;

    private enum SaveMode { Auto, OverwriteAsset, NewAssetCopy }
    private SaveMode saveMode = SaveMode.Auto;

    private bool assignNewAssetToRenderers = true; // if we create a copy, assign it back

    [MenuItem("Tools/Babylon Toolkit/UVSet Generators/Vertex Animation Channel", false, 52)]
    public static void ShowWindow()
    {
        VertexAnimation window = ScriptableObject.CreateInstance<VertexAnimation>();
        window.OnInitialize(); 
        window.ShowUtility();
    }

    public void OnInitialize()
    {
        maxSize = new Vector2(420, 246);
        minSize = this.maxSize;
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Vertex Animation Channel");
    }
    private void OnGUI()
    {
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
            EditorGUILayout.LabelField("Save Options", EditorStyles.boldLabel);
            saveMode = (SaveMode)EditorGUILayout.EnumPopup(new GUIContent("Save Mesh Mode", "Auto: overwrite Mesh assets, create copy for FBX/model meshes.\nOverwriteAsset: write UV2 directly to Mesh asset (fails for FBX).\nNewAssetCopy: duplicate Mesh to a new .asset file."), saveMode);
            if (saveMode == SaveMode.NewAssetCopy || saveMode == SaveMode.Auto)
            {
                assignNewAssetToRenderers = EditorGUILayout.Toggle(new GUIContent("Assign To Selection", "If a new Mesh asset is created, reassign it to the MeshFilter/SkinnedMeshRenderer on the selected object."), assignNewAssetToRenderers);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Generate & Save UV3"/*, GUILayout.Height(40)*/))
            {
                GenerateAndSave();
            }
        }
    }

    private void DrawResolvedMeshInfo()
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.Height(110)))
        {
            if (_resolvedMesh == null)
            {
                EditorGUILayout.Space(25);
                EditorGUILayout.HelpBox("Drag a Mesh, or a GameObject with a MeshFilter/SkinnedMeshRenderer.", MessageType.Info);
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(_resolvedMesh);
                var isAsset = AssetDatabase.Contains(_resolvedMesh);
                var isModelSubAsset = IsModelSubAsset(_resolvedMesh);
                EditorGUILayout.LabelField("Mesh Information", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Name:", _resolvedMesh.name);
                EditorGUILayout.LabelField("Triangles:", _resolvedMesh.triangles != null ? (_resolvedMesh.triangles.Length / 3).ToString() : "N/A");
                EditorGUILayout.LabelField("Asset Path:", string.IsNullOrEmpty(path) ? "(not an asset)" : path);
                EditorGUILayout.LabelField("Asset Type:", isModelSubAsset ? "Model (FBX) Sub-Asset" : (isAsset ? "Mesh Asset" : "Scene-only Mesh"));
            }
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
                Undo.RecordObject(_resolvedMesh, "Generate Vertex Animation UVs");
                System.UnityTools.PrepareVertexAnimationUV(_resolvedMesh);
                EditorUtility.SetDirty(_resolvedMesh);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Vertex Animation UVs", $"UV3 regenerated and saved on asset:\n{AssetDatabase.GetAssetPath(_resolvedMesh)}", "OK");
            }
            else
            {
                // Create a new Mesh asset, copy geometry, generate UV3 on the copy, and (optionally) assign.
                var newMesh = DuplicateMesh(_resolvedMesh);
                newMesh.name = _resolvedMesh.name + "_VAnimUV";

                // Generate on the copy
                System.UnityTools.PrepareVertexAnimationUV(newMesh);

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

                EditorUtility.DisplayDialog("Vertex Animation UVs", $"Created new Mesh asset with regenerated UV3:\n{targetPath}", "OK");
            }
        }
        catch (System.SystemException ex)
        {
            Debug.LogError($"Vertex Animation UV Regenerator error: {ex.Message}\n{ex.StackTrace}");
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

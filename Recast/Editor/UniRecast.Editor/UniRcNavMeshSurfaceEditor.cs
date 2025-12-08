////////////////////////////////////////////////////////////////////////////////////////////////
// Mackey Kinard - Babylon Toolkit Modifications
////////////////////////////////////////////////////////////////////////////////////////////////

using UniRecast.Core;
using DotRecast.Detour;
using DotRecast.Recast.Toolset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniRecast.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(UniRcNavMeshSurface))]
    public class UniRcNavMeshSurfaceEditor : UniRcToolEditor
    {
        // Sources
        private SerializedProperty _collectObjects;
        private SerializedProperty _useGeometry;
        private SerializedProperty _includeLayers;
        private SerializedProperty _useTagFilter;
        private SerializedProperty _includeTags;
        private SerializedProperty _volumeSize;

        // Rasterization
        private SerializedProperty _cellSize;
        private SerializedProperty _cellHeight;
        private SerializedProperty _verticalOffset;
        private SerializedProperty _showPreview;

        // Agent
        private SerializedProperty _agentHeight;
        private SerializedProperty _agentRadius;
        private SerializedProperty _agentMaxClimb;
        private SerializedProperty _agentMaxSlope;
        private SerializedProperty _agentMaxAcceleration;
        private SerializedProperty _agentMaxSpeed;

        // Region
        private SerializedProperty _minRegionSize;
        private SerializedProperty _mergedRegionSize;

        // Filtering
        private SerializedProperty _filterLowHangingObstacles;
        private SerializedProperty _filterLedgeSpans;
        private SerializedProperty _filterWalkableLowHeightSpans;

        // Polygonization
        private SerializedProperty _edgeMaxLen;
        private SerializedProperty _edgeMaxError;
        private SerializedProperty _vertsPerPoly;

        // Detail Mesh
        private SerializedProperty _detailSampleDist;
        private SerializedProperty _detailSampleMaxError;
        private SerializedProperty _buildHeightMesh;

        // Tiles
        private SerializedProperty _tileSize;

        // NavMesh overlay color (blue, similar to Unity)
        private static readonly Color s_NavMeshColor = new Color(0.0f, 0.75f, 1f, 0.5f);
        // Height mesh overlay color (pink, similar to Unity)
        private static readonly Color s_HeightMeshColor = new Color(1f, 0.4f, 0.7f, 0.3f);
        // Vertex dot color
        private static readonly Color s_VertexColor = Color.black;

        private void OnDisable()
        {
        }

        private void OnEnable()
        {
            // Sources
            _collectObjects = serializedObject.FindProperty(nameof(_collectObjects));
            _useGeometry = serializedObject.FindProperty(nameof(_useGeometry));
            _includeLayers = serializedObject.FindProperty(nameof(_includeLayers));
            _useTagFilter = serializedObject.FindProperty(nameof(_useTagFilter));
            _includeTags = serializedObject.FindProperty(nameof(_includeTags));
            _volumeSize = serializedObject.FindProperty(nameof(_volumeSize));

            // Rasterization
            _cellSize = serializedObject.FindProperty(nameof(_cellSize));
            _cellHeight = serializedObject.FindProperty(nameof(_cellHeight));
            _verticalOffset = serializedObject.FindProperty(nameof(_verticalOffset));
            _showPreview = serializedObject.FindProperty(nameof(_showPreview));

            // Agent
            _agentHeight = serializedObject.FindProperty(nameof(_agentHeight));
            _agentRadius = serializedObject.FindProperty(nameof(_agentRadius));
            _agentMaxClimb = serializedObject.FindProperty(nameof(_agentMaxClimb));
            _agentMaxSlope = serializedObject.FindProperty(nameof(_agentMaxSlope));
            _agentMaxAcceleration = serializedObject.FindProperty(nameof(_agentMaxAcceleration));
            _agentMaxSpeed = serializedObject.FindProperty(nameof(_agentMaxSpeed));

            // Region
            _minRegionSize = serializedObject.FindProperty(nameof(_minRegionSize));
            _mergedRegionSize = serializedObject.FindProperty(nameof(_mergedRegionSize));

            // Filtering
            _filterLowHangingObstacles = serializedObject.FindProperty(nameof(_filterLowHangingObstacles));
            _filterLedgeSpans = serializedObject.FindProperty(nameof(_filterLedgeSpans));
            _filterWalkableLowHeightSpans = serializedObject.FindProperty(nameof(_filterWalkableLowHeightSpans));

            // Polygonization
            _edgeMaxLen = serializedObject.FindProperty(nameof(_edgeMaxLen));
            _edgeMaxError = serializedObject.FindProperty(nameof(_edgeMaxError));
            _vertsPerPoly = serializedObject.FindProperty(nameof(_vertsPerPoly));

            // Detail Mesh
            _detailSampleDist = serializedObject.FindProperty(nameof(_detailSampleDist));
            _detailSampleMaxError = serializedObject.FindProperty(nameof(_detailSampleMaxError));
            _buildHeightMesh = serializedObject.FindProperty(nameof(_buildHeightMesh));

            // Tiles
            _tileSize = serializedObject.FindProperty(nameof(_tileSize));
        }

        private void Clear()
        {
            var surface = target as UniRcNavMeshSurface;
            if (null == surface)
            {
                Debug.LogError($"not found UniRc NavMesh Surface");
                return;
            }

            surface.Clear();

            // DEPRECATED: Do not reset to Recast defaults on clear
            // var bs = new RcNavMeshBuildSettings();
            // // Sources
            // _useTagFilter.boolValue = false;
            // _includeTags.arraySize = 0;
            // _collectObjects.enumValueIndex = (int)CollectObjects.All;
            // _useGeometry.enumValueIndex = (int)UseGeometry.RenderMeshes;
            // _volumeSize.vector3Value = new Vector3(50f, 25f, 50f);
            // _includeLayers.intValue = ~0;
            // // Rasterization
            // _cellSize.floatValue = bs.cellSize;
            // _cellHeight.floatValue = bs.cellHeight;
            // _verticalOffset.floatValue = 0f;
            // // Agent
            // _agentHeight.floatValue = bs.agentHeight;
            // _agentRadius.floatValue = bs.agentRadius;
            // _agentMaxClimb.floatValue = bs.agentMaxClimb;
            // _agentMaxSlope.floatValue = bs.agentMaxSlope;
            // _agentMaxAcceleration.floatValue = bs.agentMaxAcceleration;
            // _agentMaxSpeed.floatValue = bs.agentMaxSpeed;
            // // Region
            // _minRegionSize.intValue = bs.minRegionSize;
            // _mergedRegionSize.intValue = bs.mergedRegionSize;
            // // Filtering
            // _filterLowHangingObstacles.boolValue = bs.filterLowHangingObstacles;
            // _filterLedgeSpans.boolValue = bs.filterLedgeSpans;
            // _filterWalkableLowHeightSpans.boolValue = bs.filterWalkableLowHeightSpans;
            // // Polygonization
            // _edgeMaxLen.floatValue = bs.edgeMaxLen;
            // _edgeMaxError.floatValue = bs.edgeMaxError;
            // _vertsPerPoly.intValue = bs.vertsPerPoly;
            // // Detail Mesh
            // _detailSampleDist.floatValue = bs.detailSampleDist;
            // _detailSampleMaxError.floatValue = bs.detailSampleMaxError;
            // _buildHeightMesh.boolValue = false;
            // // Tiles
            // _tileSize.intValue = bs.tileSize;
        }

        private void Bake()
        {
            var surface = target as UniRcNavMeshSurface;
            if (null == surface)
            {
                Debug.LogError($"not found UniRc NavMesh Surface");
                return;
            }

            surface.Bake();
        }

        protected override void Layout()
        {
            var surface = target as UniRcNavMeshSurface;
            if (surface is null)
                return;

            // Draw image
            const float diagramHeight = 80.0f;

            //UniRcGui.Text("Sources");
            //UniRcGui.Separator();

            // Collect Objects dropdown
            EditorGUILayout.PropertyField(_collectObjects, new GUIContent("Collect Objects", "Defines what objects to collect for NavMesh generation."));
            
            // Show Volume Size only when CollectObjects is Volume
            if ((CollectObjects)_collectObjects.enumValueIndex == CollectObjects.Volume)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_volumeSize, new GUIContent("Volume Size", "The size of the volume to collect objects from."));
                EditorGUI.indentLevel--;
            }

            // Use Geometry dropdown
            EditorGUILayout.PropertyField(_useGeometry, new GUIContent("Use Geometry", "Defines what geometry to use for NavMesh generation."));

            // Include Layers mask
            EditorGUILayout.PropertyField(_includeLayers, new GUIContent("Include Layers", "Only objects on these layers will be included in NavMesh generation."));

            // Tag filtering includes
            if (_includeLayers.intValue != UniRcConst.EverythingMask)
            {
                // Pseudocode generated by codewrx.ai
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_useTagFilter, new GUIContent("Use Tag Filter", "When enabled, objects with specified tags will be included."));
                if (EditorGUI.EndChangeCheck())
                {
                    // If the toggle was turned off, clear the IncludeTags list so no tags are used
                    if (!_useTagFilter.boolValue)
                    {
                        _includeTags.arraySize = 0;
                    }
                    else
                    {
                        // If the toggle was turned on and there are no tags, add the default tag
                        if (_includeTags.arraySize == 0)
                        {
                            _includeTags.arraySize = 1;
                            _includeTags.GetArrayElementAtIndex(0).stringValue = UniRcConst.Tag;
                        }
                    }
                }

                if (_useTagFilter.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_includeTags, new GUIContent("Include Tags", "Tags to include in NavMesh generation."), true);
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                _useTagFilter.boolValue = false;
                _includeTags.arraySize = 0;
            }
            UniRcGui.NewLine();

            UniRcGui.Text("Navigation");
            UniRcGui.Separator();
            Rect agentDiagramRect = EditorGUILayout.GetControlRect(false, diagramHeight);
            UniRcGui.DrawAgentDiagram(agentDiagramRect, _agentRadius.floatValue, _agentHeight.floatValue, _agentMaxClimb.floatValue, _agentMaxSlope.floatValue);
            UniRcGui.SliderFloat("Agent Height", _agentHeight, 0.1f, 5f, 0.001f);
            UniRcGui.SliderFloat("Agent Radius", _agentRadius, 0.1f, 5f, 0.001f);
            UniRcGui.SliderFloat("Max Climb", _agentMaxClimb, 0.1f, 5f, 0.001f);
            UniRcGui.SliderFloat("Max Slope", _agentMaxSlope, 1f, 90f, 1);
            UniRcGui.SliderFloat("Max Acceleration", _agentMaxAcceleration, 8f, 999f, 0.001f);
            UniRcGui.SliderFloat("Max Move Speed", _agentMaxSpeed, 1f, 10f, 0.001f);
            UniRcGui.NewLine();

            UniRcGui.Text("Rasterize");
            UniRcGui.Separator();
            UniRcGui.SliderFloat("Cell Size", _cellSize, 0.01f, 1f, 0.001f);
            UniRcGui.SliderFloat("Cell Height", _cellHeight, 0.01f, 1f, 0.001f);
            UniRcGui.Checkbox("Show Preview", _showPreview);
            //UniRcEditorHelpers.Text($"Voxels {voxels[0]} x {voxels[1]}");
            UniRcGui.NewLine();

            UniRcGui.Text("Region");
            UniRcGui.Separator();
            UniRcGui.SliderInt("Min Region Size", _minRegionSize, 1, 150);
            UniRcGui.SliderInt("Merged Region Size", _mergedRegionSize, 1, 150);
            UniRcGui.NewLine();

            UniRcGui.Text("Filtering");
            UniRcGui.Separator();
            UniRcGui.Checkbox("Low Hanging Obstacles", _filterLowHangingObstacles);
            UniRcGui.Checkbox("Ledge Spans", _filterLedgeSpans);
            UniRcGui.Checkbox("Walkable Low Height Spans", _filterWalkableLowHeightSpans);
            UniRcGui.NewLine();

            UniRcGui.Text("Polygons");
            UniRcGui.Separator();
            UniRcGui.SliderFloat("Max Edge Length", _edgeMaxLen, 0f, 50f, 0.001f);
            UniRcGui.SliderFloat("Max Edge Error", _edgeMaxError, 0.1f, 3f, 0.001f);
            UniRcGui.SliderInt("Vert Per Poly", _vertsPerPoly, 3, 12);
            UniRcGui.SliderFloat("Mesh Offset", _verticalOffset, -0.1f, 0.1f, 0.001f);

            UniRcGui.NewLine();

            UniRcGui.Text("Detail Mesh");
            UniRcGui.Separator();
            UniRcGui.SliderFloat("Sample Distance", _detailSampleDist, 0f, 16f, 0.001f);
            UniRcGui.SliderFloat("Max Sample Error", _detailSampleMaxError, 0f, 16f, 0.001f);
            UniRcGui.Checkbox("Build Height Mesh", _buildHeightMesh);
            UniRcGui.NewLine();

            UniRcGui.Text("Tiling");
            UniRcGui.Separator();
            UniRcGui.SliderInt("Tile Size", _tileSize, 32, 64, 8);
            UniRcGui.NewLine();

            // UniRcEditorHelpers.Text($"Tiles {tiles[0]} x {tiles[1]}");
            // UniRcEditorHelpers.Text($"Max Tiles {maxTiles}");
            // UniRcEditorHelpers.Text($"Max Polys {maxPolys}");
            //}

            // NavMesh Data Status
            UniRcGui.Text("NavMesh Data");
            UniRcGui.Separator();
            
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Has NavMesh", surface.HasNavMeshData());
                EditorGUILayout.Toggle("Has Height Mesh", surface.HasHeightMeshData());
            }

            UniRcGui.NewLine();

            serializedObject.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (UniRcGui.Button("Clear"))
                {
                    if (EditorUtility.DisplayDialog("Clear Bake Settings", "Are you sure you want to clear all bake settings?", "Clear", "Cancel"))
                    {
                        Clear();
                        serializedObject.ApplyModifiedProperties();
                    }
                }

                if (UniRcGui.Button("Bake"))
                {
                    if (EditorUtility.DisplayDialog("Bake Navigation Mesh", "Are you sure you want to bake the navigation mesh?", "Bake", "Cancel"))
                    {
                        Bake();
                    }
                }
            }
        }

        [DrawGizmo(GizmoType.InSelectionHierarchy | GizmoType.Active | GizmoType.Pickable)]
        static void RenderGizmoSelected(UniRcNavMeshSurface navSurface, GizmoType gizmoType)
        {
            // Only render if ShowPreview is enabled
            if (!navSurface.ShowPreview)
            {
                Gizmos.DrawIcon(navSurface.transform.position, "NavMeshSurface Icon", true);
                return;
            }

            var zTestOld = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;

            DrawNavMeshPreview(navSurface, true);
            Handles.zTest = zTestOld;
        }

        [DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Pickable)]
        static void RenderGizmoNotSelected(UniRcNavMeshSurface navSurface, GizmoType gizmoType)
        {
            // Only show icon when not selected
            Gizmos.DrawIcon(navSurface.transform.position, "NavMeshSurface Icon", true);
        }

        /// <summary>
        /// Draws a single NavMesh polygon with the specified color.
        /// </summary>
        static void DrawPoly(DtMeshTile tile, int index, Vector3 offset, Color color)
        {
            Handles.color = color;
            var polygonVertices = new Vector3[3];

            DtPoly p = tile.data.polys[index];
            if (tile.data.detailMeshes != null)
            {
                DtPolyDetail pd = tile.data.detailMeshes[index];
                for (int j = 0; j < pd.triCount; ++j)
                {
                    int t = (pd.triBase + j) * 4;
                    for (int k = 0; k < 3; ++k)
                    {
                        int v = tile.data.detailTris[t + k];
                        if (v < p.vertCount)
                        {
                            // Left-handed coordinates (no X negation needed for Unity display)
                            polygonVertices[k] = new Vector3(
                                tile.data.verts[p.verts[v] * 3],
                                tile.data.verts[p.verts[v] * 3 + 1],
                                tile.data.verts[p.verts[v] * 3 + 2]);
                        }
                        else
                        {
                            // Left-handed coordinates (no X negation needed for Unity display)
                            polygonVertices[k] = new Vector3(
                                tile.data.detailVerts[(pd.vertBase + v - p.vertCount) * 3],
                                tile.data.detailVerts[(pd.vertBase + v - p.vertCount) * 3 + 1],
                                tile.data.detailVerts[(pd.vertBase + v - p.vertCount) * 3 + 2]);
                        }
                    }

                    Handles.DrawAAConvexPolygon(polygonVertices);
                }
            }
            else
            {
                for (int j = 1; j < p.vertCount - 1; ++j)
                {
                    // Left-handed coordinates (no X negation needed for Unity display)
                    var v0 = new Vector3(
                        tile.data.verts[p.verts[0] * 3],
                        tile.data.verts[p.verts[0] * 3 + 1],
                        tile.data.verts[p.verts[0] * 3 + 2]
                    );
                    polygonVertices[0] = v0;

                    for (int k = 0; k < 2; ++k)
                    {
                        // Left-handed coordinates (no X negation needed for Unity display)
                        var vn = new Vector3(
                            tile.data.verts[p.verts[j + k] * 3],
                            tile.data.verts[p.verts[j + k] * 3 + 1],
                            tile.data.verts[p.verts[j + k] * 3 + 2]
                        );
                        polygonVertices[k + 1] = vn;
                    }

                    Handles.DrawAAConvexPolygon(polygonVertices);
                }
            }
        }

        static void DrawNavMeshPreview(UniRcNavMeshSurface navSurface, bool selected)
        {
            var oldColor = Gizmos.color;
            var oldMatrix = Gizmos.matrix;

            // Use the unscaled matrix for the NavMeshSurface
            var localToWorld = Matrix4x4.TRS(navSurface.transform.position, navSurface.transform.rotation, Vector3.one);
            Gizmos.matrix = localToWorld;

            var handleOldColor = Handles.color;

            // Draw height mesh overlay (pink) if BuildHeightMesh is enabled and data exists
            if (navSurface.BuildHeightMesh && navSurface.HasHeightMeshData())
            {
                DrawHeightMeshOverlay(navSurface);
            }

            // Draw NavMesh overlay (blue)
            if (navSurface.HasNavMeshData())
            {
                DrawNavMeshOverlay(navSurface);
            }

            Handles.color = handleOldColor;
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;

            Gizmos.DrawIcon(navSurface.transform.position, "NavMeshSurface Icon", true);
        }

        /// <summary>
        /// Draws the blue NavMesh polygon overlay.
        /// </summary>
        static void DrawNavMeshOverlay(UniRcNavMeshSurface navSurface)
        {
            var navMesh = navSurface.GetNavMeshData();
            if (navMesh == null)
                return;

            int count = navMesh.GetMaxTiles();
            
            // Draw polygons
            for (int i = 0; i < count; ++i)
            {
                var tile = navMesh.GetTile(i);
                if (null == tile.data)
                    continue;

                for (int ii = 0; ii < tile.data.header.polyCount; ++ii)
                {
                    DtPoly p = tile.data.polys[ii];
                    if (p.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    {
                        continue;
                    }

                    DrawPoly(tile, ii, navSurface.transform.position, s_NavMeshColor);
                }
            }

            // Draw vertex dots
            Handles.color = s_VertexColor;
            for (int i = 0; i < count; ++i)
            {
                var tile = navMesh.GetTile(i);
                if (null == tile.data)
                    continue;

                for (int ii = 0; ii < tile.data.header.vertCount; ++ii)
                {
                    int v = ii * 3;
                    var pt = new Vector3(tile.data.verts[v + 0], tile.data.verts[v + 1], tile.data.verts[v + 2]);

                    Handles.DotHandleCap(
                        0,
                        pt + navSurface.transform.position,
                        Quaternion.identity,
                        HandleUtility.GetHandleSize(navSurface.transform.position + pt) * 0.015f,
                        EventType.Repaint
                    );
                }
            }
        }

        /// <summary>
        /// Draws the pink height mesh overlay.
        /// </summary>
        static void DrawHeightMeshOverlay(UniRcNavMeshSurface navSurface)
        {
            var heightMesh = navSurface.GetHeightMesh();
            if (heightMesh == null)
                return;

            var vertices = heightMesh.vertices;
            var triangles = heightMesh.triangles;

            if (vertices == null || triangles == null || triangles.Length == 0)
                return;

            Handles.color = s_HeightMeshColor;

            // Small Y offset to avoid Z-fighting with geometry
            Vector3 zFightOffset = new Vector3(0f, 0.01f, 0f);

            // Draw triangles
            var polygonVertices = new Vector3[3];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (i + 2 >= triangles.Length)
                    break;

                int idx0 = triangles[i];
                int idx1 = triangles[i + 1];
                int idx2 = triangles[i + 2];

                if (idx0 >= vertices.Length || idx1 >= vertices.Length || idx2 >= vertices.Length)
                    continue;

                polygonVertices[0] = vertices[idx0] + navSurface.transform.position + zFightOffset;
                polygonVertices[1] = vertices[idx1] + navSurface.transform.position + zFightOffset;
                polygonVertices[2] = vertices[idx2] + navSurface.transform.position + zFightOffset;

                Handles.DrawAAConvexPolygon(polygonVertices);
            }
        }


        [MenuItem("GameObject/UniRecast/UniRc NavMesh Surface", false, 2000)]
        public static void CreateNavMeshSurface(MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;
            var go = UniRcGuiUtility.CreateAndSelectGameObject("UniRc NavMesh Surface", parent);
            go.AddComponent<UniRcNavMeshSurface>();
            var view = SceneView.lastActiveSceneView;
            if (view != null)
                view.MoveToView(go.transform);
        }
    }
}
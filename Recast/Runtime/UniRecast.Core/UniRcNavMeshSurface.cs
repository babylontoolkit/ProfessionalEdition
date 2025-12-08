////////////////////////////////////////////////////////////////////////////////////////////////
// Mackey Kinard - Babylon Toolkit Modifications
////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast.Toolset;
using UnityEngine;

namespace UniRecast.Core
{
    /// <summary>
    /// Defines what objects to collect for NavMesh generation.
    /// </summary>
    public enum CollectObjects
    {
        /// <summary>Collect all objects in the scene that match the layer and tag filters.</summary>
        All = 0,
        /// <summary>Collect objects within a volume defined by the NavMesh Surface bounds.</summary>
        Volume = 1,
        /// <summary>Collect only child objects of this NavMesh Surface.</summary>
        Children = 2
    }

    /// <summary>
    /// Defines what geometry to use for NavMesh generation.
    /// </summary>
    public enum UseGeometry
    {
        /// <summary>Use render meshes (MeshFilter components).</summary>
        RenderMeshes = 0,
        /// <summary>Use physics colliders (Collider components).</summary>
        PhysicsColliders = 1
    }

    /// <summary>
    /// NavMesh Surface component for baking and managing navigation meshes.
    /// Data is stored in binary files (NavigationMesh.bin) and Unity Mesh assets (NavigationMesh.asset).
    /// </summary>
    public class UniRcNavMeshSurface : MonoBehaviour
    {
        // =====================================================================
        // Source Settings
        // =====================================================================

        [SerializeField]
        private CollectObjects _collectObjects = CollectObjects.All;

        [SerializeField]
        private UseGeometry _useGeometry = UseGeometry.RenderMeshes;

        [SerializeField]
        private LayerMask _includeLayers = ~0; // All layers by default

        [SerializeField]
        private bool _useTagFilter = false;

        [SerializeField]
        private List<string> _includeTags = new List<string> { UniRcConst.Tag };

        [SerializeField]
        private Vector3 _volumeSize = new Vector3(50f, 25f, 50f);

        // =====================================================================
        // Serialized Settings
        // =====================================================================
        
        [SerializeField]
        private float _cellSize = 0.1f;

        [SerializeField]
        private float _cellHeight = 0.2f;
        
        [SerializeField]
        private float _verticalOffset = 0f;

        [SerializeField]
        private bool _showPreview = true;

        // Agent
        [SerializeField]
        private float _agentHeight = 2.0f;

        [SerializeField]
        private float _agentRadius = 0.4f;

        [SerializeField]
        private float _agentMaxClimb = 0.4f;

        [SerializeField]
        private float _agentMaxSlope = 45f;

        [SerializeField]
        private float _agentMaxAcceleration = 8.0f;

        [SerializeField]
        private float _agentMaxSpeed = 3.5f;

        // Region
        [SerializeField]
        private int _minRegionSize = 8;

        [SerializeField]
        private int _mergedRegionSize = 20;

        // Filtering
        [SerializeField]
        private bool _filterLowHangingObstacles = true;

        [SerializeField]
        private bool _filterLedgeSpans = true;

        [SerializeField]
        private bool _filterWalkableLowHeightSpans = true;

        // Polygonization
        [SerializeField]
        private float _edgeMaxLen = 12f;

        [SerializeField]
        private float _edgeMaxError = 1.3f;

        [SerializeField]
        private int _vertsPerPoly = 6;

        // Detail Mesh
        [SerializeField]
        private float _detailSampleDist = 6f;

        [SerializeField]
        private float _detailSampleMaxError = 1f;

        [SerializeField]
        private bool _buildHeightMesh = false;

        // Tiles
        [SerializeField]
        private int _tileSize = 32;

        // =====================================================================
        // Runtime Cache (not serialized - loaded from files on demand)
        // =====================================================================
        
        [NonSerialized]
        private DtNavMesh _cachedNavMesh;

        [NonSerialized]
        private Mesh _cachedHeightMesh;

        // =====================================================================
        // Public Properties
        // =====================================================================

        public bool ShowPreview => _showPreview;
        public bool BuildHeightMesh => _buildHeightMesh;
        public CollectObjects CollectObjects => _collectObjects;
        public UseGeometry UseGeometry => _useGeometry;
        public LayerMask IncludeLayers => _includeLayers;
        public bool UseTagFilter => _useTagFilter;
        public List<string> IncludeTags => _includeTags;
        public Vector3 VolumeSize => _volumeSize;

        // =====================================================================
        // Build Settings
        // =====================================================================

        private RcNavMeshBuildSettings ToBuildSettings()
        {
            var bs = new RcNavMeshBuildSettings();

            // Rasterization
            bs.cellSize = _cellSize;
            bs.cellHeight = _cellHeight;

            // Agent
            bs.agentHeight = _agentHeight;
            bs.agentRadius = _agentRadius;
            bs.agentMaxClimb = _agentMaxClimb;
            bs.agentMaxSlope = _agentMaxSlope;
            bs.agentMaxAcceleration = _agentMaxAcceleration;
            bs.agentMaxSpeed = _agentMaxSpeed;

            // Region
            bs.minRegionSize = _minRegionSize;
            bs.mergedRegionSize = _mergedRegionSize;

            // Filtering
            bs.filterLowHangingObstacles = _filterLowHangingObstacles;
            bs.filterLedgeSpans = _filterLedgeSpans;
            bs.filterWalkableLowHeightSpans = _filterWalkableLowHeightSpans;

            // Polygonization
            bs.edgeMaxLen = _edgeMaxLen;
            bs.edgeMaxError = _edgeMaxError;
            bs.vertsPerPoly = _vertsPerPoly;

            // Detail Mesh
            bs.detailSampleDist = _detailSampleDist;
            bs.detailSampleMaxError = _detailSampleMaxError;

            // Tiles
            bs.tiled = true;
            bs.tileSize = _tileSize;

            return bs;
        }

        // =====================================================================
        // Bake Methods
        // =====================================================================

        public void Bake()
        {
            var setting = ToBuildSettings();
            BakeFrom(setting);
        }

        public void BakeFrom(RcNavMeshBuildSettings setting, bool clearFirst = true)
        {
            if (clearFirst)
            {
                this.Clear(); // Note: Clear existing data before baking new
            }

            var currentScene = gameObject.scene;
            GameObject[] allGameObjects = currentScene.GetRootGameObjects();
            var targets = GetNavMeshSurfaceTargets(allGameObjects);
            
            if (targets.Count == 0)
            {
                Debug.LogError("No navmesh targets found");
                return;
            }

            var sceneFolderPath = UnityTools.GetCurrentSceneFolder();
            if (string.IsNullOrEmpty(sceneFolderPath))
            {
                Debug.LogError("Scene folder path is empty. Please save the scene first.");
                return;
            }

            var combinedTarget = targets.ToCombinedNavMeshSurfaceTarget(currentScene.name, _verticalOffset, _buildHeightMesh);
            var mesh = combinedTarget.ToMeshLeftHanded();
            
            // Save height mesh as Unity Mesh asset
            var heightMesh = combinedTarget.GetHeightMesh();
            if (heightMesh != null)
            {
                mesh.SaveUnityMeshFile(heightMesh, sceneFolderPath);
            }
    
            // Build and save NavMesh as binary file
            var navMesh = mesh.Build(setting);
            navMesh.SaveSerdesNavMeshFile(sceneFolderPath);
            
            // Cache the results
            _cachedNavMesh = navMesh;
            _cachedHeightMesh = heightMesh;

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif

            Debug.Log("NavMesh baked successfully");
        }

        // =====================================================================
        // Target Collection
        // =====================================================================

        public List<UniRcNavMeshSurfaceTarget> GetNavMeshSurfaceTargets(IList<GameObject> gameObjects)
        {
            var targets = new List<UniRcNavMeshSurfaceTarget>();

            // Determine which objects to collect based on CollectObjects setting
            IEnumerable<GameObject> sourceObjects;
            switch (_collectObjects)
            {
                case CollectObjects.Children:
                    // Only collect children of this NavMesh Surface
                    sourceObjects = gameObject.ToHierarchyList();
                    break;
                case CollectObjects.Volume:
                    // Collect objects within the volume bounds
                    var volumeBounds = new Bounds(transform.position, _volumeSize);
                    sourceObjects = gameObjects
                        .SelectMany(x => x.ToHierarchyList())
                        .Where(x => volumeBounds.Contains(x.transform.position) || 
                                   (x.TryGetComponent<Renderer>(out var renderer) && volumeBounds.Intersects(renderer.bounds)));
                    break;
                case CollectObjects.All:
                default:
                    // Collect all objects in the scene
                    sourceObjects = gameObjects.SelectMany(x => x.ToHierarchyList());
                    break;
            }

            // Filter by layer mask
            sourceObjects = sourceObjects.Where(x => ((_includeLayers.value >> x.layer) & 1) == 1);

            // Filter by tag if enabled
            if (_useTagFilter && _includeTags != null && _includeTags.Count > 0)
            {
                sourceObjects = sourceObjects.Where(x => _includeTags.Contains(x.tag));
            }

            // Filter by active in hierarchy (must be enabled AND all parents enabled)
            sourceObjects = sourceObjects.Where(x => x.activeInHierarchy);

            var filteredObjects = sourceObjects.ToList();

            // Collect terrain targets
            var terrainTargets = filteredObjects
                .SelectMany(x => x.GetComponents<Terrain>())
                .Where(x => x.isActiveAndEnabled)
                .Distinct()
                .Select(x => x.ToUniRcSurfaceSource());
            targets.AddRange(terrainTargets);

            // Collect geometry based on UseGeometry setting
            if (_useGeometry == UseGeometry.RenderMeshes)
            {
                // Use render meshes (MeshFilter components)
                var meshFilterTargets = filteredObjects
                    .SelectMany(x => x.GetComponents<MeshFilter>())
                    .Where(x => x.sharedMesh != null)
                    .Distinct()
                    .Select(x => x.ToUniRcSurfaceSource());
                targets.AddRange(meshFilterTargets);
            }
            else // UseGeometry.PhysicsColliders
            {
                // Use physics colliders
                var colliderTargets = filteredObjects
                    .SelectMany(x => x.GetComponents<Collider>())
                    .Where(x => x.enabled && !x.isTrigger)
                    .Distinct()
                    .Select(x => x.ToUniRcSurfaceSource())
                    .Where(x => x != null);
                targets.AddRange(colliderTargets);
            }

            return targets;
        }

        // =====================================================================
        // Clear
        // =====================================================================

        public void Clear()
        {
            _cachedNavMesh = null;
            
            if (_cachedHeightMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_cachedHeightMesh);
                else
                    DestroyImmediate(_cachedHeightMesh, true);
                _cachedHeightMesh = null;
            }

            var sceneFolderPath = UnityTools.GetCurrentSceneFolder();
            if (string.IsNullOrEmpty(sceneFolderPath))
                return;

            var meshFilename = Path.Combine(sceneFolderPath, "NavigationMesh.asset");
            var binFilename = Path.Combine(sceneFolderPath, "NavigationMesh.bin");

            DeleteFileIfExists(meshFilename);
            DeleteFileIfExists(binFilename);

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        private void DeleteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (File.Exists(path + ".meta"))
                    File.Delete(path + ".meta");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to delete file {path}: {ex.Message}");
            }
        }

        // =====================================================================
        // Data Access - Single Source: Binary Files
        // =====================================================================

        public bool HasNavMeshData()
        {
            if (_cachedNavMesh != null)
                return true;

            var binPath = GetNavMeshBinaryPath();
            return !string.IsNullOrEmpty(binPath) && File.Exists(binPath);
        }

        public bool HasHeightMeshData()
        {
            if (_cachedHeightMesh != null)
                return true;

            var meshPath = GetHeightMeshAssetPath();
            if (string.IsNullOrEmpty(meshPath))
                return false;

#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null;
#else
            return false;
#endif
        }

        public DtNavMesh GetNavMeshData()
        {
            if (_cachedNavMesh != null)
                return _cachedNavMesh;

            _cachedNavMesh = LoadNavMeshFromBinaryFile();
            return _cachedNavMesh;
        }

        public Mesh GetHeightMesh()
        {
            if (_cachedHeightMesh != null)
                return _cachedHeightMesh;

            _cachedHeightMesh = LoadHeightMeshFromAssetFile();
            return _cachedHeightMesh;
        }

        // =====================================================================
        // File Paths
        // =====================================================================

        private string GetNavMeshBinaryPath()
        {
            var sceneFolderPath = UnityTools.GetCurrentSceneFolder();
            if (string.IsNullOrEmpty(sceneFolderPath))
                return null;
            return (sceneFolderPath + "/NavigationMesh.bin").Replace("\\", "/");
        }

        private string GetHeightMeshAssetPath()
        {
            var sceneFolderPath = UnityTools.GetCurrentSceneFolder();
            if (string.IsNullOrEmpty(sceneFolderPath))
                return null;
            return (sceneFolderPath + "/NavigationMesh.asset").Replace("\\", "/");
        }

        // =====================================================================
        // Binary File Loading - Must match SaveSerdesNavMeshFile format exactly
        // =====================================================================

        private const int NAVMESHSET_MAGIC = ('M' << 24) | ('S' << 16) | ('E' << 8) | 'T';  // 0x4D534554
        private const int NAVMESHSET_VERSION = 1;
        private const int DT_VERTS_PER_POLYGON = 6;

        private DtNavMesh LoadNavMeshFromBinaryFile()
        {
            var binPath = GetNavMeshBinaryPath();
            if (string.IsNullOrEmpty(binPath) || !File.Exists(binPath))
                return null;

            try
            {
                using var fs = new FileStream(binPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);
                
                // Read RecastHeader (12 bytes)
                int magic = br.ReadInt32();
                int version = br.ReadInt32();
                int numTiles = br.ReadInt32();
                
                if (magic != NAVMESHSET_MAGIC)
                {
                    Debug.LogError($"Invalid NavMesh file magic: 0x{magic:X8}, expected 0x{NAVMESHSET_MAGIC:X8}");
                    return null;
                }
                
                if (version != NAVMESHSET_VERSION)
                {
                    Debug.LogError($"Unsupported NavMesh file version: {version}");
                    return null;
                }
                
                // Read dtNavMeshParams (28 bytes)
                var navParams = new DtNavMeshParams();
                navParams.orig.X = br.ReadSingle();
                navParams.orig.Y = br.ReadSingle();
                navParams.orig.Z = br.ReadSingle();
                navParams.tileWidth = br.ReadSingle();
                navParams.tileHeight = br.ReadSingle();
                navParams.maxTiles = br.ReadInt32();
                navParams.maxPolys = br.ReadInt32();
                
                int vertsPerPoly = _vertsPerPoly > 0 ? _vertsPerPoly : DT_VERTS_PER_POLYGON;
                var navMesh = new DtNavMesh();
                navMesh.Init(navParams, vertsPerPoly);
                
                // Use DtMeshDataReader for proper tile deserialization
                var tileReader = new DtMeshDataReader();
                
                // Read tiles
                for (int i = 0; i < numTiles; i++)
                {
                    // NavMeshTileHeader: 4-byte tileRef + 4-byte dataSize (32-bit WASM format)
                    // Note: tileRef is for WASM export, not used by DotRecast (different bit layout)
                    uint tileRef = br.ReadUInt32();  // Read but don't use
                    int tileDataSize = br.ReadInt32();
                    
                    if (tileDataSize <= 0)
                        continue;
                    
                    byte[] tileData = br.ReadBytes(tileDataSize);
                    
                    // Use DtMeshDataReader to deserialize tile data (it handles readonly fields properly)
                    var tileBuf = new RcByteBuffer(tileData);
                    tileBuf.Order(RcByteOrder.LITTLE_ENDIAN);
                    var meshData = tileReader.Read32Bit(tileBuf, vertsPerPoly);
                    
                    if (meshData != null)
                    {
                        // Pass 0 for lastRef - let DtNavMesh allocate tile indices
                        // The tileRef from file is 32-bit WASM format, incompatible with DotRecast 64-bit format
                        navMesh.AddTile(meshData, 0, 0, out _);
                    }
                }
                
                return navMesh;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load NavMesh: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private Mesh LoadHeightMeshFromAssetFile()
        {
#if UNITY_EDITOR
            var meshPath = GetHeightMeshAssetPath();
            if (string.IsNullOrEmpty(meshPath))
                return null;
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
#else
            return null;
#endif
        }
    }
}

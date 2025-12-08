////////////////////////////////////////////////////////////////////////////////////////////////
// Mackey Kinard - Babylon Toolkit Modifications
////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.IO;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniRecast.Core
{
    // https://github.com/highfidelity/unity-to-hifi-exporter/blob/master/Assets/UnityToHiFiExporter/Editor/TerrainObjExporter.cs
    public static class UniRcExtensions
    {
        public static RcVec3f ToRightHand(this Vector3 v)
        {
            return new RcVec3f(-v.x, v.y, v.z);
        }
        
        /// <summary>
        /// Converts Vector3 to RcVec3f keeping left-handed coordinates (for Babylon.js export).
        /// No coordinate flip - just a direct conversion.
        /// </summary>
        public static RcVec3f ToLeftHand(this Vector3 v)
        {
            return new RcVec3f(v.x, v.y, v.z);
        }

        public static UniRcNavMeshSurfaceTarget ToUniRcSurfaceSource(this MeshFilter meshFilter)
        {
            return new UniRcNavMeshSurfaceTarget(meshFilter.name, meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix);
        }

        public static UniRcNavMeshSurfaceTarget ToUniRcSurfaceSource(this Terrain terrain)
        {
            var mesh = terrain.terrainData.ToMesh(terrain.transform.position);
            return new UniRcNavMeshSurfaceTarget(terrain.name, mesh, terrain.transform.localToWorldMatrix);
        }

        /// <summary>
        /// Converts a Collider to a UniRcNavMeshSurfaceTarget by generating a mesh from its shape.
        /// Supports BoxCollider, SphereCollider, CapsuleCollider, and MeshCollider.
        /// </summary>
        public static UniRcNavMeshSurfaceTarget ToUniRcSurfaceSource(this Collider collider)
        {
            Mesh mesh = ColliderToMesh(collider);
            if (mesh == null)
                return null;

            return new UniRcNavMeshSurfaceTarget(collider.name, mesh, collider.transform.localToWorldMatrix);
        }

        /// <summary>
        /// Converts a Collider to a Mesh representation.
        /// </summary>
        private static Mesh ColliderToMesh(Collider collider)
        {
            if (collider is BoxCollider boxCollider)
            {
                return CreateBoxMesh(boxCollider);
            }
            else if (collider is SphereCollider sphereCollider)
            {
                return CreateSphereMesh(sphereCollider);
            }
            else if (collider is CapsuleCollider capsuleCollider)
            {
                return CreateCapsuleMesh(capsuleCollider);
            }
            else if (collider is MeshCollider meshCollider)
            {
                return meshCollider.sharedMesh;
            }
            // Unsupported collider types (WheelCollider, TerrainCollider handled separately)
            return null;
        }

        /// <summary>
        /// Creates a box mesh from a BoxCollider.
        /// </summary>
        private static Mesh CreateBoxMesh(BoxCollider box)
        {
            Vector3 size = box.size;
            Vector3 center = box.center;

            Vector3[] vertices = new Vector3[8];
            Vector3 halfSize = size * 0.5f;

            // Bottom face
            vertices[0] = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
            vertices[1] = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
            vertices[2] = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
            vertices[3] = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
            // Top face
            vertices[4] = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
            vertices[5] = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
            vertices[6] = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
            vertices[7] = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);

            int[] triangles = new int[]
            {
                // Bottom
                0, 2, 1, 0, 3, 2,
                // Top
                4, 5, 6, 4, 6, 7,
                // Front
                0, 1, 5, 0, 5, 4,
                // Back
                2, 3, 7, 2, 7, 6,
                // Left
                0, 4, 7, 0, 7, 3,
                // Right
                1, 2, 6, 1, 6, 5
            };

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Creates a sphere mesh from a SphereCollider.
        /// </summary>
        private static Mesh CreateSphereMesh(SphereCollider sphere, int segments = 16, int rings = 12)
        {
            float radius = sphere.radius;
            Vector3 center = sphere.center;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // Generate vertices
            for (int lat = 0; lat <= rings; lat++)
            {
                float theta = lat * Mathf.PI / rings;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= segments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / segments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 vertex = new Vector3(
                        cosPhi * sinTheta,
                        cosTheta,
                        sinPhi * sinTheta
                    ) * radius + center;
                    vertices.Add(vertex);
                }
            }

            // Generate triangles
            for (int lat = 0; lat < rings; lat++)
            {
                for (int lon = 0; lon < segments; lon++)
                {
                    int first = lat * (segments + 1) + lon;
                    int second = first + segments + 1;

                    triangles.Add(first);
                    triangles.Add(second);
                    triangles.Add(first + 1);

                    triangles.Add(second);
                    triangles.Add(second + 1);
                    triangles.Add(first + 1);
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Creates a capsule mesh from a CapsuleCollider.
        /// </summary>
        private static Mesh CreateCapsuleMesh(CapsuleCollider capsule, int segments = 16, int rings = 8)
        {
            float radius = capsule.radius;
            float height = capsule.height;
            Vector3 center = capsule.center;
            int direction = capsule.direction; // 0=X, 1=Y, 2=Z

            // Ensure height is at least 2 * radius
            float cylinderHeight = Mathf.Max(0, height - 2 * radius);

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // Generate top hemisphere
            for (int lat = 0; lat <= rings / 2; lat++)
            {
                float theta = lat * Mathf.PI / rings;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= segments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / segments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 vertex = new Vector3(
                        cosPhi * sinTheta * radius,
                        cosTheta * radius + cylinderHeight / 2,
                        sinPhi * sinTheta * radius
                    );
                    vertices.Add(RotateCapsuleVertex(vertex, direction) + center);
                }
            }

            int topHemisphereVertexCount = vertices.Count;

            // Generate cylinder
            for (int i = 0; i <= 1; i++)
            {
                float y = (i == 0) ? cylinderHeight / 2 : -cylinderHeight / 2;
                for (int lon = 0; lon <= segments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / segments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 vertex = new Vector3(
                        cosPhi * radius,
                        y,
                        sinPhi * radius
                    );
                    vertices.Add(RotateCapsuleVertex(vertex, direction) + center);
                }
            }

            int cylinderVertexCount = vertices.Count - topHemisphereVertexCount;

            // Generate bottom hemisphere
            for (int lat = rings / 2; lat <= rings; lat++)
            {
                float theta = lat * Mathf.PI / rings;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= segments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / segments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 vertex = new Vector3(
                        cosPhi * sinTheta * radius,
                        cosTheta * radius - cylinderHeight / 2,
                        sinPhi * sinTheta * radius
                    );
                    vertices.Add(RotateCapsuleVertex(vertex, direction) + center);
                }
            }

            // Generate triangles for top hemisphere
            for (int lat = 0; lat < rings / 2; lat++)
            {
                for (int lon = 0; lon < segments; lon++)
                {
                    int first = lat * (segments + 1) + lon;
                    int second = first + segments + 1;

                    triangles.Add(first);
                    triangles.Add(second);
                    triangles.Add(first + 1);

                    triangles.Add(second);
                    triangles.Add(second + 1);
                    triangles.Add(first + 1);
                }
            }

            // Generate triangles for cylinder
            int cylinderStart = topHemisphereVertexCount;
            for (int lon = 0; lon < segments; lon++)
            {
                int first = cylinderStart + lon;
                int second = first + segments + 1;

                triangles.Add(first);
                triangles.Add(second);
                triangles.Add(first + 1);

                triangles.Add(second);
                triangles.Add(second + 1);
                triangles.Add(first + 1);
            }

            // Generate triangles for bottom hemisphere
            int bottomStart = topHemisphereVertexCount + cylinderVertexCount;
            for (int lat = 0; lat < rings / 2; lat++)
            {
                for (int lon = 0; lon < segments; lon++)
                {
                    int first = bottomStart + lat * (segments + 1) + lon;
                    int second = first + segments + 1;

                    triangles.Add(first);
                    triangles.Add(second);
                    triangles.Add(first + 1);

                    triangles.Add(second);
                    triangles.Add(second + 1);
                    triangles.Add(first + 1);
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Rotates capsule vertices based on the capsule direction axis.
        /// </summary>
        private static Vector3 RotateCapsuleVertex(Vector3 vertex, int direction)
        {
            switch (direction)
            {
                case 0: // X-axis
                    return new Vector3(vertex.y, vertex.z, vertex.x);
                case 2: // Z-axis
                    return new Vector3(vertex.x, vertex.z, vertex.y);
                default: // Y-axis (default)
                    return vertex;
            }
        }

        public static UniRcNavMeshSurfaceTarget ToCombinedNavMeshSurfaceTarget(this IList<UniRcNavMeshSurfaceTarget> sources, string name, float heightOffset = 0f, bool buildHeightMesh = false)
        {
            CombineInstance[] combineInstances = new CombineInstance[sources.Count];

            for (int i = 0; i < sources.Count; i++)
            {
                combineInstances[i].mesh = sources[i].GetMesh();
                combineInstances[i].transform = sources[i].GetMatrix4();
            }

            // Combine meshes with UInt32 index format
            Mesh combinedMesh = new Mesh();
            combinedMesh.indexFormat = IndexFormat.UInt32;
            combinedMesh.CombineMeshes(combineInstances, true, true, false);

            // // debug code
            // // Create a new GameObject to hold the combined mesh
            // var combinedMeshObject = new GameObject(name);
            // var meshFilter = combinedMeshObject.AddComponent<MeshFilter>();
            // var meshRenderer = combinedMeshObject.AddComponent<MeshRenderer>();
            //
            // // Assign the combined mesh to the new GameObject
            // meshFilter.sharedMesh = combinedMesh;

            // Build height mesh from original combined mesh data
            Mesh heightMesh = (buildHeightMesh == true) ? combinedMesh.Copy() : null;
            if (heightMesh != null)
            {
                UnityTools.GenerateNavigationHeightMeshData(heightMesh, 0.001f, 0.5f);
            }

            // Apply height offset to combined mesh vertices
            if (!Mathf.Approximately(heightOffset, 0f))
            {
                Vector3[] vertices = combinedMesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i].y += heightOffset;
                }
                combinedMesh.vertices = vertices;
                combinedMesh.RecalculateBounds();
            }

            return new UniRcNavMeshSurfaceTarget(name, combinedMesh, heightMesh, Matrix4x4.identity);
        }

        public static Mesh ToMesh(this TerrainData terrainData, Vector3 terrainPos)
        {
            int w = terrainData.heightmapResolution;
            int h = terrainData.heightmapResolution;
            Vector3 meshScale = terrainData.size;
            int tRes = (int)Mathf.Pow(2, 1);
            meshScale = new Vector3(meshScale.x / (w - 1) * tRes, meshScale.y, meshScale.z / (h - 1) * tRes);
            float[,] heights = terrainData.GetHeights(0, 0, w, h);

            w = (w - 1) / tRes + 1;
            h = (h - 1) / tRes + 1;

            Vector3[] vertices = new Vector3[w * h];
            int[] triangles = new int[(w - 1) * (h - 1) * 6];

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    vertices[z * w + x] = Vector3.Scale(meshScale, new Vector3(x, heights[z * tRes, x * tRes], z));
                }
            }

            int index = 0;

            // Build triangle indices: 3 indices into vertex array for each triangle
            for (int z = 0; z < h - 1; z++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    // For each grid cell output two triangles
                    triangles[index++] = (z * w) + x;
                    triangles[index++] = ((z + 1) * w) + x;
                    triangles[index++] = (z * w) + x + 1;

                    triangles[index++] = ((z + 1) * w) + x;
                    triangles[index++] = ((z + 1) * w) + x + 1;
                    triangles[index++] = (z * w) + x + 1;
                }
            }

            // Create a new mesh
            // Assign vertices and triangles to the mesh
            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;

            // Calculate normals and other mesh attributes if needed
            mesh.RecalculateNormals();

            return mesh;
        }

        public static void SaveNavMeshFile(this DtNavMesh navMesh, string fileName)
        {
            var navMeshFileName = $"{fileName}.bytes";
            using var fs = new FileStream(navMeshFileName, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            DtMeshSetWriter writer = new DtMeshSetWriter();
            writer.Write(bw, navMesh, RcByteOrder.BIG_ENDIAN, true);
        }

        // =====================================================================
        // Mackey Kinard - Save Recast Serdes Format
        // EXACT match to NavMeshSerdes.cpp exportNavMesh function
        // =====================================================================
        // recast-navigation-js SERDES FORMAT CONSTANTS
        // These MUST match NavMeshSerdes.cpp exactly
        // =====================================================================

        private const int NAVMESHSET_MAGIC = ('M' << 24) | ('S' << 16) | ('E' << 8) | 'T';  // 0x4D534554
        private const int NAVMESHSET_VERSION = 1;
        
        // Tile data magic/version from Detour (dtNavMesh.h)
        private const int DT_NAVMESH_MAGIC = ('D' << 24) | ('N' << 16) | ('A' << 8) | 'V';  // 0x444E4156
        private const int DT_NAVMESH_VERSION = 7;

        public static void SaveSerdesNavMeshFile(this DtNavMesh navMesh, string rootPath)
        {
            var navMeshFileName = Path.Combine(rootPath, "NavigationMesh.bin");
            using var fs = new FileStream(navMeshFileName, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // Write in LITTLE ENDIAN (native C++ format) - this is critical!
            // The C++ code uses memcpy which preserves native byte order
            
            // =====================================================================
            // 1. Count valid tiles first (matching C++ loop)
            // =====================================================================
            int numTiles = 0;
            for (int i = 0; i < navMesh.GetMaxTiles(); ++i)
            {
                DtMeshTile tile = navMesh.GetTile(i);
                if (tile == null || tile.data == null || tile.data.header == null)
                    continue;
                numTiles++;
            }
            
            // =====================================================================
            // 2. Write RecastHeader (12 bytes) - LITTLE ENDIAN
            // struct RecastHeader { int magic; int version; int numTiles; };
            // =====================================================================
            bw.Write(NAVMESHSET_MAGIC);           // 4 bytes - little endian
            bw.Write(NAVMESHSET_VERSION);         // 4 bytes - little endian  
            bw.Write(numTiles);                   // 4 bytes - little endian
            
            // =====================================================================
            // 3. Write NavMeshSetHeader (dtNavMeshParams - 28 bytes) - LITTLE ENDIAN
            // struct dtNavMeshParams {
            //     float orig[3];      // 12 bytes
            //     float tileWidth;    // 4 bytes
            //     float tileHeight;   // 4 bytes
            //     int maxTiles;       // 4 bytes
            //     int maxPolys;       // 4 bytes
            // };
            // =====================================================================
            DtNavMeshParams navParams = navMesh.GetParams();
            bw.Write(navParams.orig.X);           // float - little endian
            bw.Write(navParams.orig.Y);           // float - little endian
            bw.Write(navParams.orig.Z);           // float - little endian
            bw.Write(navParams.tileWidth);        // float - little endian
            bw.Write(navParams.tileHeight);       // float - little endian
            bw.Write(navParams.maxTiles);         // int - little endian
            bw.Write(navParams.maxPolys);         // int - little endian
            
            // =====================================================================
            // Calculate bit layout for 32-bit tileRef encoding (matching C++ Detour)
            // tileRef = (salt << (polyBits + tileBits)) | (tileIdx << polyBits) | polyIdx
            // =====================================================================
            int tileBits = ILog2(navParams.maxTiles);
            int polyBits = ILog2(navParams.maxPolys);
            // Note: salt always starts at 1 in C++ Detour (0 means invalid)
            
            // =====================================================================
            // 4. Write each tile: NavMeshTileHeader + raw tile data
            // CRITICAL: WASM uses 32-bit tileRef (4 bytes), NOT 64-bit!
            // =====================================================================
            int tileIndex = 0;
            for (int i = 0; i < navMesh.GetMaxTiles(); ++i)
            {
                DtMeshTile tile = navMesh.GetTile(i);
                if (tile == null || tile.data == null || tile.data.header == null)
                    continue;
                
                // Serialize tile data to raw bytes (matching C++ dtMeshTile::data format)
                byte[] tileData = SerializeTileData(tile.data);
                
                // Write NavMeshTileHeader (8 bytes for 32-bit WASM)
                // struct NavMeshTileHeader {
                //     dtTileRef tileRef;  // 4 bytes (unsigned int for 32-bit WASM)
                //     int dataSize;       // 4 bytes
                // };
                // 
                // Compute tileRef using 32-bit encoding:
                // tileRef = (salt << (polyBits + tileBits)) | (tileIdx << polyBits) | 0
                // salt = 1 (minimum valid), tileIdx = tile.index, polyIdx = 0 (for tile base ref)
                uint salt = 1; // Must be >= 1 for valid tileRef
                uint tileRef = (salt << (polyBits + tileBits)) | ((uint)tileIndex << polyBits);
                
                bw.Write(tileRef);                // 4 bytes - little endian
                bw.Write(tileData.Length);        // 4 bytes - little endian (int)
                
                // Write raw tile data
                bw.Write(tileData);
                
                tileIndex++;
            }
        }
        
        /// <summary>
        /// Serializes DtMeshData to raw bytes matching the C++ dtMeshTile::data format EXACTLY.
        /// This must match how Detour C++ stores tile data in memory.
        /// The format is defined in DetourNavMesh.cpp dtNavMesh::addTile()
        /// </summary>
        private static byte[] SerializeTileData(DtMeshData data)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            
            DtMeshHeader header = data.header;
            
            // =====================================================================
            // dtMeshHeader (100 bytes in C++ with standard packing)
            // =====================================================================
            bw.Write(DT_NAVMESH_MAGIC);           // int magic
            bw.Write(DT_NAVMESH_VERSION);         // int version
            bw.Write(header.x);                   // int x
            bw.Write(header.y);                   // int y
            bw.Write(header.layer);               // int layer
            bw.Write(header.userId);              // unsigned int userId
            bw.Write(header.polyCount);           // int polyCount
            bw.Write(header.vertCount);           // int vertCount
            bw.Write(header.maxLinkCount);        // int maxLinkCount
            bw.Write(header.detailMeshCount);     // int detailMeshCount
            bw.Write(header.detailVertCount);     // int detailVertCount
            bw.Write(header.detailTriCount);      // int detailTriCount
            bw.Write(header.bvNodeCount);         // int bvNodeCount
            bw.Write(header.offMeshConCount);     // int offMeshConCount
            bw.Write(header.offMeshBase);         // int offMeshBase
            bw.Write(header.walkableHeight);      // float walkableHeight
            bw.Write(header.walkableRadius);      // float walkableRadius
            bw.Write(header.walkableClimb);       // float walkableClimb
            bw.Write(header.bmin.X);              // float bmin[0]
            bw.Write(header.bmin.Y);              // float bmin[1]
            bw.Write(header.bmin.Z);              // float bmin[2]
            bw.Write(header.bmax.X);              // float bmax[0]
            bw.Write(header.bmax.Y);              // float bmax[1]
            bw.Write(header.bmax.Z);              // float bmax[2]
            bw.Write(header.bvQuantFactor);       // float bvQuantFactor
            
            // =====================================================================
            // Vertices: float[3] * vertCount
            // =====================================================================
            for (int i = 0; i < header.vertCount * 3; i++)
            {
                bw.Write(data.verts[i]);
            }
            
            // =====================================================================
            // Polygons: dtPoly * polyCount
            // struct dtPoly {
            //     unsigned int firstLink;     // 4 bytes (index to first link in linked list)
            //     unsigned short verts[DT_VERTS_PER_POLYGON]; // 2*6 = 12 bytes
            //     unsigned short neis[DT_VERTS_PER_POLYGON];  // 2*6 = 12 bytes
            //     unsigned short flags;       // 2 bytes
            //     unsigned char vertCount;    // 1 byte
            //     unsigned char areaAndtype;  // 1 byte
            // }; // Total: 32 bytes per poly
            // =====================================================================
            int maxVertsPerPoly = 6; // DT_VERTS_PER_POLYGON = 6 in Detour
            for (int i = 0; i < header.polyCount; i++)
            {
                DtPoly poly = data.polys[i];
                
                // firstLink - set to 0xFFFFFFFF (DT_NULL_LINK) as it's rebuilt at runtime
                bw.Write((uint)0xFFFFFFFF);
                
                // verts array - always write maxVertsPerPoly entries
                for (int j = 0; j < maxVertsPerPoly; j++)
                {
                    if (j < poly.verts.Length)
                        bw.Write((ushort)poly.verts[j]);
                    else
                        bw.Write((ushort)0);
                }
                
                // neis array - always write maxVertsPerPoly entries
                for (int j = 0; j < maxVertsPerPoly; j++)
                {
                    if (j < poly.neis.Length)
                        bw.Write((ushort)poly.neis[j]);
                    else
                        bw.Write((ushort)0);
                }
                
                bw.Write((ushort)poly.flags);     // unsigned short flags
                bw.Write((byte)poly.vertCount);   // unsigned char vertCount
                bw.Write((byte)poly.areaAndtype); // unsigned char areaAndtype
            }
            
            // =====================================================================
            // Links: dtLink * maxLinkCount
            // struct dtLink {
            //     dtPolyRef ref;          // 4 bytes (or 8 on some platforms, but WASM uses 4)
            //     unsigned int next;      // 4 bytes
            //     unsigned char edge;     // 1 byte
            //     unsigned char side;     // 1 byte
            //     unsigned char bmin;     // 1 byte
            //     unsigned char bmax;     // 1 byte
            // }; // Total: 12 bytes per link (32-bit) or 16 bytes (64-bit)
            // For WASM 32-bit: 12 bytes per link
            // =====================================================================
            int linkSize = 12; // 32-bit WASM uses 12 bytes per link
            byte[] linkPlaceholder = new byte[header.maxLinkCount * linkSize];
            bw.Write(linkPlaceholder);
            
            // =====================================================================
            // Detail meshes: dtPolyDetail * detailMeshCount
            // struct dtPolyDetail {
            //     unsigned int vertBase;  // 4 bytes
            //     unsigned int triBase;   // 4 bytes
            //     unsigned char vertCount;// 1 byte
            //     unsigned char triCount; // 1 byte
            //     // 2 bytes padding for alignment
            // }; // Total: 12 bytes
            // =====================================================================
            for (int i = 0; i < header.detailMeshCount; i++)
            {
                DtPolyDetail detail = data.detailMeshes[i];
                bw.Write((uint)detail.vertBase);
                bw.Write((uint)detail.triBase);
                bw.Write((byte)detail.vertCount);
                bw.Write((byte)detail.triCount);
                bw.Write((short)0); // 2 bytes padding
            }
            
            // =====================================================================
            // Detail vertices: float[3] * detailVertCount
            // NOTE: Flip X coordinate to undo ToRightHand() conversion
            // =====================================================================
            // =====================================================================
            // Detail vertices: float[3] * detailVertCount
            // =====================================================================
            for (int i = 0; i < header.detailVertCount * 3; i++)
            {
                bw.Write(data.detailVerts[i]);
            }
            
            // =====================================================================
            // Detail triangles: unsigned char[4] * detailTriCount
            // =====================================================================
            for (int i = 0; i < header.detailTriCount * 4; i++)
            {
                bw.Write((byte)data.detailTris[i]);
            }
            
            // =====================================================================
            // BV Tree nodes: dtBVNode * bvNodeCount
            // struct dtBVNode {
            //     unsigned short bmin[3]; // 6 bytes
            //     unsigned short bmax[3]; // 6 bytes
            //     int i;                  // 4 bytes
            // }; // Total: 16 bytes
            // =====================================================================
            for (int i = 0; i < header.bvNodeCount; i++)
            {
                DtBVNode node = data.bvTree[i];
                bw.Write((ushort)node.bmin.X);
                bw.Write((ushort)node.bmin.Y);
                bw.Write((ushort)node.bmin.Z);
                bw.Write((ushort)node.bmax.X);
                bw.Write((ushort)node.bmax.Y);
                bw.Write((ushort)node.bmax.Z);
                bw.Write(node.i);
            }
            
            // =====================================================================
            // Off-mesh connections: dtOffMeshConnection * offMeshConCount
            // struct dtOffMeshConnection {
            //     float pos[6];           // 24 bytes (start + end positions)
            //     float rad;              // 4 bytes
            //     unsigned short poly;    // 2 bytes
            //     unsigned char flags;    // 1 byte
            //     unsigned char side;     // 1 byte
            //     unsigned int userId;    // 4 bytes
            // }; // Total: 36 bytes
            // =====================================================================
            for (int i = 0; i < header.offMeshConCount; i++)
            {
                DtOffMeshConnection con = data.offMeshCons[i];
                // pos[0-2] = start position
                bw.Write(con.pos[0].X);
                bw.Write(con.pos[0].Y);
                bw.Write(con.pos[0].Z);
                // pos[3-5] = end position
                bw.Write(con.pos[1].X);
                bw.Write(con.pos[1].Y);
                bw.Write(con.pos[1].Z);
                bw.Write(con.rad);
                bw.Write((ushort)con.poly);
                bw.Write((byte)con.flags);
                bw.Write((byte)con.side);
                bw.Write((uint)con.userId);
            }
            
            return ms.ToArray();
        }
        
        /// <summary>
        /// Computes integer log base 2 (floor). Returns the bit position of the highest set bit.
        /// </summary>
        private static int ILog2(int value)
        {
            if (value <= 0) return 0;
            int result = 0;
            while (value > 1)
            {
                value >>= 1;
                result++;
            }
            return result;
        }
    }
}
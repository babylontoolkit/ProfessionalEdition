# UniRecast Babylon Toolkit Integration Guide (Mackey Kinard)

This document details all modifications made to UniRecast to enable proper navigation mesh export for Babylon.js. These changes are necessary because both Unity and Babylon.js use **left-handed** coordinate systems, while the original UniRecast code converts to **right-handed** coordinates (for traditional Recast/Detour compatibility).

## Overview

The key insight is that Babylon.js is left-handed like Unity, so the `ToRightHand()` coordinate conversion that UniRecast performs is unnecessary and actually causes the navmesh to appear flipped/rotated in Babylon.js.

### Files Modified

1. `UniRcExtensions.cs` - Added left-handed coordinate conversion
2. `UniRcNavMeshSurfaceTarget.cs` - Added left-handed mesh conversion method
3. `UniRcNavMeshSurface.cs` - Changed to use left-handed mesh for baking
4. `UniRcNavMeshSurfaceEditor.cs` - Removed X negation from visual display
5. `UniRcExtensions.cs` - Added `SaveSerdesNavMeshFile()` for recast-navigation-js export

---

## Detailed Changes

### 1. UniRcExtensions.cs

**Location:** `Packages/com.babylontoolkit.editor/Recast/Runtime/UniRecast.Core/UniRcExtensions.cs`

#### Change 1.1: Add `ToLeftHand()` Extension Method

Add the following method after the existing `ToRightHand()` method:

```csharp
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
```

#### Change 1.2: Add `SaveSerdesNavMeshFile()` Method

Add the following constants and method for exporting navmesh in recast-navigation-js format:

```csharp
// Constants for Serdes NavMesh format (recast-navigation-js compatible)
private const int NAVMESHSET_MAGIC = 0x4D534554; // 'MSET' in little-endian
private const int NAVMESHSET_VERSION = 1;
private const int DT_NAVMESH_MAGIC = 0x444E4156; // 'DNAV' -> reads as 'VAND' in little-endian
private const int DT_NAVMESH_VERSION = 7;

/// <summary>
/// Saves the navigation mesh in the Serdes format compatible with recast-navigation-js.
/// This format can be loaded by NavMeshSerdes.importNavMesh() in the WASM library.
/// Format specification from: recast-navigation-js/packages/recast-navigation-wasm/src/NavMeshSerdes.cpp
/// </summary>
public static void SaveSerdesNavMeshFile(this DtNavMesh navMesh, string rootPath)
{
    var beginTicks = RcFrequency.Ticks;
    var filePath = Path.Combine(rootPath, "NavigationMesh.bin");
    
    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
    using (var bw = new BinaryWriter(fs))
    {
        WriteSerdesFormat(bw, navMesh);
    }
    
    var elapsedTicks = RcFrequency.Ticks - beginTicks;
    Debug.Log($"save serdes to {filePath} {elapsedTicks / TimeSpan.TicksPerMillisecond} ms");
}

private static void WriteSerdesFormat(BinaryWriter bw, DtNavMesh navMesh)
{
    // Count valid tiles
    int numTiles = 0;
    for (int i = 0; i < navMesh.GetMaxTiles(); ++i)
    {
        DtMeshTile tile = navMesh.GetTile(i);
        if (tile == null || tile.data == null || tile.data.header == null)
            continue;
        numTiles++;
    }
    
    // Write RecastHeader (12 bytes)
    bw.Write(NAVMESHSET_MAGIC);
    bw.Write(NAVMESHSET_VERSION);
    bw.Write(numTiles);
    
    // Write dtNavMeshParams (28 bytes)
    DtNavMeshParams navParams = navMesh.GetParams();
    bw.Write(navParams.orig.X);
    bw.Write(navParams.orig.Y);
    bw.Write(navParams.orig.Z);
    bw.Write(navParams.tileWidth);
    bw.Write(navParams.tileHeight);
    bw.Write(navParams.maxTiles);
    bw.Write(navParams.maxPolys);
    
    // Calculate bit layout for 32-bit tileRef encoding
    int tileBits = ILog2(navParams.maxTiles);
    int polyBits = ILog2(navParams.maxPolys);
    
    // Write each tile
    int tileIndex = 0;
    for (int i = 0; i < navMesh.GetMaxTiles(); ++i)
    {
        DtMeshTile tile = navMesh.GetTile(i);
        if (tile == null || tile.data == null || tile.data.header == null)
            continue;
        
        byte[] tileData = SerializeTileData(tile.data);
        
        // Compute 32-bit tileRef (WASM uses 32-bit, not 64-bit)
        uint salt = 1;
        uint tileRef = (salt << (polyBits + tileBits)) | ((uint)tileIndex << polyBits);
        
        bw.Write(tileRef);
        bw.Write(tileData.Length);
        bw.Write(tileData);
        
        tileIndex++;
    }
}

private static byte[] SerializeTileData(DtMeshData data)
{
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);
    
    DtMeshHeader header = data.header;
    
    // dtMeshHeader (100 bytes)
    bw.Write(DT_NAVMESH_MAGIC);
    bw.Write(DT_NAVMESH_VERSION);
    bw.Write(header.x);
    bw.Write(header.y);
    bw.Write(header.layer);
    bw.Write(header.userId);
    bw.Write(header.polyCount);
    bw.Write(header.vertCount);
    bw.Write(header.maxLinkCount);
    bw.Write(header.detailMeshCount);
    bw.Write(header.detailVertCount);
    bw.Write(header.detailTriCount);
    bw.Write(header.bvNodeCount);
    bw.Write(header.offMeshConCount);
    bw.Write(header.offMeshBase);
    bw.Write(header.walkableHeight);
    bw.Write(header.walkableRadius);
    bw.Write(header.walkableClimb);
    bw.Write(header.bmin.X);
    bw.Write(header.bmin.Y);
    bw.Write(header.bmin.Z);
    bw.Write(header.bmax.X);
    bw.Write(header.bmax.Y);
    bw.Write(header.bmax.Z);
    bw.Write(header.bvQuantFactor);
    
    // Vertices: float[3] * vertCount
    for (int i = 0; i < header.vertCount * 3; i++)
    {
        bw.Write(data.verts[i]);
    }
    
    // Polygons: dtPoly * polyCount (32 bytes each)
    int maxVertsPerPoly = 6;
    for (int i = 0; i < header.polyCount; i++)
    {
        DtPoly poly = data.polys[i];
        
        bw.Write((uint)0xFFFFFFFF); // firstLink (rebuilt at runtime)
        
        for (int j = 0; j < maxVertsPerPoly; j++)
        {
            if (j < poly.verts.Length)
                bw.Write((ushort)poly.verts[j]);
            else
                bw.Write((ushort)0);
        }
        
        for (int j = 0; j < maxVertsPerPoly; j++)
        {
            if (j < poly.neis.Length)
                bw.Write((ushort)poly.neis[j]);
            else
                bw.Write((ushort)0);
        }
        
        bw.Write((ushort)poly.flags);
        bw.Write((byte)poly.vertCount);
        bw.Write((byte)poly.areaAndtype);
    }
    
    // Links placeholder (12 bytes per link for 32-bit WASM)
    int linkSize = 12;
    byte[] linkPlaceholder = new byte[header.maxLinkCount * linkSize];
    bw.Write(linkPlaceholder);
    
    // Detail meshes (12 bytes each)
    for (int i = 0; i < header.detailMeshCount; i++)
    {
        DtPolyDetail detail = data.detailMeshes[i];
        bw.Write((uint)detail.vertBase);
        bw.Write((uint)detail.triBase);
        bw.Write((byte)detail.vertCount);
        bw.Write((byte)detail.triCount);
        bw.Write((short)0); // padding
    }
    
    // Detail vertices
    for (int i = 0; i < header.detailVertCount * 3; i++)
    {
        bw.Write(data.detailVerts[i]);
    }
    
    // Detail triangles
    for (int i = 0; i < header.detailTriCount * 4; i++)
    {
        bw.Write((byte)data.detailTris[i]);
    }
    
    // BV Tree nodes (16 bytes each)
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
    
    // Off-mesh connections (36 bytes each)
    for (int i = 0; i < header.offMeshConCount; i++)
    {
        DtOffMeshConnection con = data.offMeshCons[i];
        bw.Write(con.pos[0].X);
        bw.Write(con.pos[0].Y);
        bw.Write(con.pos[0].Z);
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
```

---

### 2. UniRcNavMeshSurfaceTarget.cs

**Location:** `Packages/com.babylontoolkit.editor/Recast/Runtime/UniRecast.Core/UniRcNavMeshSurfaceTarget.cs`

#### Change 2.1: Add `ToMeshLeftHanded()` Method

Add the following method after the existing `ToMesh()` method:

```csharp
/// <summary>
/// Converts to UniRcMesh keeping left-handed coordinates (for Babylon.js export).
/// No X flip and no winding order reversal.
/// </summary>
public UniRcMesh ToMeshLeftHanded()
{
    var vertices = _mesh
        .vertices
        .Select(_matrix4.MultiplyPoint3x4)
        .Select(v => v.ToLeftHand())
        .ToArray();

    int[] faces = new int[_mesh.triangles.Length];
    Array.Copy(_mesh.triangles, faces, _mesh.triangles.Length);

    // Keep original winding order for left-handed systems (Unity/Babylon.js)
    // No triangle index swapping needed

    var mesh = new UniRcMesh(_name, vertices, faces);
    return mesh;
}
```

**Note:** The original `ToMesh()` method:
- Calls `ToRightHand()` which negates X
- Reverses triangle winding order (swaps indices)

The new `ToMeshLeftHanded()` method:
- Calls `ToLeftHand()` which keeps X unchanged
- Keeps original triangle winding order

---

### 3. UniRcNavMeshSurface.cs

**Location:** `Packages/com.babylontoolkit.editor/Recast/Runtime/UniRecast.Core/UniRcNavMeshSurface.cs`

#### Change 3.1: Use `ToMeshLeftHanded()` in `BakeFrom()`

In the `BakeFrom()` method, change:

```csharp
// BEFORE:
var mesh = combinedTarget.ToMesh();

// AFTER:
// Use ToMeshLeftHanded() for Babylon.js export (both Unity and Babylon.js are left-handed)
var mesh = combinedTarget.ToMeshLeftHanded();
```

---

### 4. UniRcNavMeshSurfaceEditor.cs

**Location:** `Packages/com.babylontoolkit.editor/Recast/Editor/UniRecast.Editor/UniRcNavMeshSurfaceEditor.cs`

#### Change 4.1: Remove X Negation in `DrawPoly()`

The `DrawPoly()` method draws the navmesh polygons in the Unity editor. Since the navmesh data is now stored in left-handed coordinates, we no longer need to negate X for display.

**BEFORE:**
```csharp
polygonVertices[k] = new Vector3(
    -tile.data.verts[p.verts[v] * 3],          // X negated
    tile.data.verts[p.verts[v] * 3 + 1],
    tile.data.verts[p.verts[v] * 3 + 2]);
```

**AFTER:**
```csharp
// Left-handed coordinates (no X negation needed for Unity display)
polygonVertices[k] = new Vector3(
    tile.data.verts[p.verts[v] * 3],           // X NOT negated
    tile.data.verts[p.verts[v] * 3 + 1],
    tile.data.verts[p.verts[v] * 3 + 2]);
```

Apply this change to ALL vertex accesses in `DrawPoly()`:
1. Main vertices from `tile.data.verts` (2 locations)
2. Detail vertices from `tile.data.detailVerts` (1 location)
3. Fallback vertices when no detail meshes (2 locations)

#### Change 4.2: Remove X Negation in Vertex Debug Drawing

In `DrawBoundingBoxGizmoAndIcon()`, there's vertex debug drawing that also negates X:

**BEFORE:**
```csharp
var pt = new Vector3(-tile.data.verts[v + 0], tile.data.verts[v + 1], tile.data.verts[v + 2]);
```

**AFTER:**
```csharp
// Left-handed coordinates (no X negation needed for Unity display)
var pt = new Vector3(tile.data.verts[v + 0], tile.data.verts[v + 1], tile.data.verts[v + 2]);
```

---

## Binary Format Reference

The `SaveSerdesNavMeshFile()` method exports in the format expected by `recast-navigation-js`:

### File Structure

```
RecastHeader (12 bytes)
├── magic: int32 (0x4D534554 = 'MSET')
├── version: int32 (1)
└── numTiles: int32

dtNavMeshParams (28 bytes)
├── orig[3]: float32[3] (origin X, Y, Z)
├── tileWidth: float32
├── tileHeight: float32
├── maxTiles: int32
└── maxPolys: int32

For each tile:
├── NavMeshTileHeader (8 bytes)
│   ├── tileRef: uint32 (32-bit for WASM!)
│   └── dataSize: int32
└── Tile Data (variable)
    ├── dtMeshHeader (100 bytes)
    ├── vertices: float32[vertCount * 3]
    ├── polys: dtPoly[polyCount] (32 bytes each)
    ├── links: dtLink[maxLinkCount] (12 bytes each, placeholder zeros)
    ├── detailMeshes: dtPolyDetail[detailMeshCount] (12 bytes each)
    ├── detailVerts: float32[detailVertCount * 3]
    ├── detailTris: uint8[detailTriCount * 4]
    ├── bvTree: dtBVNode[bvNodeCount] (16 bytes each)
    └── offMeshCons: dtOffMeshConnection[offMeshConCount] (36 bytes each)
```

### Critical Notes

1. **32-bit tileRef**: WASM uses 32-bit `dtTileRef`, NOT 64-bit. This is crucial for compatibility.

2. **tileRef encoding**: `tileRef = (salt << (polyBits + tileBits)) | (tileIdx << polyBits)`
   - salt must be >= 1 (0 means invalid reference)

3. **Link size**: 12 bytes for 32-bit WASM (not 16 bytes as in 64-bit builds)

4. **Little-endian**: All values are written in little-endian format (BinaryWriter default)

---

## Why These Changes Were Necessary

### The Coordinate System Problem

1. **Unity** uses a **left-handed** coordinate system
2. **Babylon.js** uses a **left-handed** coordinate system
3. **Traditional Recast/Detour** uses a **right-handed** coordinate system

The original UniRecast code was designed to convert Unity's left-handed coordinates to right-handed for Recast. This is done by:
- Negating the X coordinate (`ToRightHand()`)
- Reversing triangle winding order

However, since Babylon.js is ALSO left-handed, this conversion is unnecessary and causes the navmesh to appear flipped/rotated when displayed in Babylon.js.

### The Solution

By keeping the coordinates in left-handed format:
1. The navmesh data matches Unity's coordinate system
2. The navmesh data matches Babylon.js's coordinate system
3. All internal Detour data structures (BV tree, etc.) are consistent
4. Pathfinding works correctly
5. Visual display in both Unity and Babylon.js is correct

---

## Testing Checklist

After applying these changes:

- [ ] Unity navmesh bakes correctly
- [ ] Blue navmesh visualization in Unity editor matches the scene geometry
- [ ] NavigationMesh.bin exports to scene folder
- [ ] Babylon.js loads the navmesh without errors
- [ ] Debug navmesh visualization in Babylon.js matches the scene
- [ ] Navigation agents move correctly when `setDestination()` is called
- [ ] Agents navigate to clicked positions accurately

---

## Version Information

- **UniRecast Version**: (Check your package version)
- **Babylon Toolkit Version**: Professional Edition
- **recast-navigation-js**: Compatible with NavMeshSerdes format
- **Date Modified**: December 2024

---

## Contact

For questions about these modifications, refer to the Babylon Toolkit documentation or support channels.

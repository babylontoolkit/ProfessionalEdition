#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

using CanvasTools;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class ApplicationManager
{
    static ApplicationManager()
    {
        // CanvasToolsExporter.OnExportNavigationMesh += Application_GenerateNavigationMesh;
        // UnityEngine.Debug.LogWarning("@@@ DEBUG: Initialized Runtime Application Manager");
    }

    // private static void Application_GenerateNavigationMesh(Mesh mesh, string arg2)
    // {
    //     UnityEngine.Debug.LogWarning("@@@ DEBUG: Generating Navigation Mesh...");
    // }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // KEEP FOR REFERENCE - IS A MASTER COPY OF THE EXPORT METHOD IN UniRcNavMeshSurfaceEditor
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// Export the navmesh from a baked UniRcNavMeshSurface to a .bin file that
    /// recast-navigation-js / BabylonJS V2 Recast can consume.
    ///
    /// The file format is:
    ///   RecastHeader (MSET, version, numTiles)
    ///   NavMeshSetHeader (dtNavMeshParams)
    ///   [NavMeshTileHeader + dtMeshData blob] * numTiles
    ///
    /// All numeric fields are written little-endian, using the exact C struct
    /// layout when cCompatibility == true.
    /// </summary>
    /// <param name="surface">The UniRecast navmesh surface, already baked.</param>
    /// <param name="outputPath">Absolute or project-relative file path for the .bin.</param>
    // public static void CopyExport(UniRcNavMeshSurface surface, string outputPath)
    // {
    //     if (surface == null)
    //         throw new ArgumentNullException(nameof(surface), "UniRcNavMeshSurface is null.");
    //     if (!surface.HasNavMeshData())
    //         throw new InvalidOperationException(
    //             "UniRcNavMeshSurface has no baked navmesh. " +
    //             "Call surface.Bake() first before exporting.");
    //     DtNavMesh navMesh = surface.GetNavMeshData();
    //     if (navMesh == null)
    //         throw new InvalidOperationException(
    //             "UniRcNavMeshSurface.GetNavMeshData() returned null. " +
    //             "Make sure Bake() completed successfully.");
    //     // Ensure destination directory exists
    //     string directory = Path.GetDirectoryName(outputPath);
    //     if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    //     {
    //         Directory.CreateDirectory(directory);
    //     }
    //     // Use DotRecast's DtMeshSetWriter, which mirrors recast4j's writer and has
    //     // a C-compatibility mode (cCompatibility = true) that matches the C dtNavMesh
    //     // binary layout used by recast-navigation-js.
    //     var meshSetWriter = new DtMeshSetWriter();
    //     // Open the file and write with:
    //     //   - RcByteOrder.LITTLE_ENDIAN  → matches WASM / JS expectations
    //     //   - cCompatibility: true       → match C dtNavMesh struct layout/version
    //     using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
    //     using (var binaryWriter = new BinaryWriter(fileStream))
    //     {
    //         meshSetWriter.Write(binaryWriter, navMesh, RcByteOrder.LITTLE_ENDIAN, cCompatibility: true);
    //     }
    //     Debug.Log($"[RecastJsNavMeshExporter] Exported navmesh to:\n{outputPath}");
    // }
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
}
#endif

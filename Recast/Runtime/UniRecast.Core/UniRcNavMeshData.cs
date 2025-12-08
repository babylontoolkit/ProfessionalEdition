////////////////////////////////////////////////////////////////////////////////////////////////
// Mackey Kinard - Babylon Toolkit Modifications
////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using DotRecast.Detour;

namespace UniRecast.Core
{
    /// <summary>
    /// Simple runtime container for NavMesh data.
    /// Data persistence is handled via binary files (NavigationMesh.bin).
    /// </summary>
    [Serializable]
    public class UniRcNavMeshData
    {
        [NonSerialized]
        public DtNavMesh NavMesh;
    }
}

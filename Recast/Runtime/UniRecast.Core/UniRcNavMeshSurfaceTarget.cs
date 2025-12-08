////////////////////////////////////////////////////////////////////////////////////////////////
// Mackey Kinard - Babylon Toolkit Modifications
////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Linq;
using UnityEngine;

namespace UniRecast.Core
{
    public class UniRcNavMeshSurfaceTarget
    {
        private readonly string _name;
        private readonly Mesh _mesh;
        private readonly Mesh _height;
        private readonly Matrix4x4 _matrix4;

        public UniRcNavMeshSurfaceTarget(string name, Mesh mesh, Matrix4x4 matrix4)
        {
            _name = name;
            _mesh = mesh;
            _height = null;
            _matrix4 = matrix4;
        }

        public UniRcNavMeshSurfaceTarget(string name, Mesh mesh, Mesh height, Matrix4x4 matrix4)
        {
            _name = name;
            _mesh = mesh;
            _height = height;
            _matrix4 = matrix4;
        }

        public string GetName()
        {
            return _name;
        }

        public Mesh GetMesh()
        {
            return _mesh;
        }

        public Mesh GetHeightMesh()
        {
            return _height;
        }

        public Matrix4x4 GetMatrix4()
        {
            return _matrix4;
        }

        public UniRcMesh ToMesh()
        {
            var vertices = _mesh
                .vertices
                .Select(_matrix4.MultiplyPoint3x4)
                .Select(v => v.ToRightHand())
                .ToArray();

            int[] faces = new int[_mesh.triangles.Length];
            Array.Copy(_mesh.triangles, faces, _mesh.triangles.Length);

            for (int i = 0; i < faces.Length; i += 3)
            {
                int i0 = faces[i + 2];
                int i1 = faces[i + 1];
                int i2 = faces[i + 0];

                faces[i + 0] = i0;
                faces[i + 1] = i1;
                faces[i + 2] = i2;
            }

            var mesh = new UniRcMesh(_name, vertices, faces);
            return mesh;
        }
        
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
    }
}
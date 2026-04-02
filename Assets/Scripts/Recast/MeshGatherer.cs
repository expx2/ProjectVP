using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

public struct GatheredMeshes : IDisposable
{
    public NativeArray<int> Indexes;
    public NativeArray<float3> Vertices;
    public NativeArray<int> Tris;
    public NativeArray<float4x4> Matrices;

    public void Dispose()
    {
        Indexes.Dispose();
        Vertices.Dispose();
        Tris.Dispose();
        Matrices.Dispose();
    }
    
    public void DrawDebugLines(float4x4 matrix, Color? color = null, float duration = 0f, bool depthTest = true)
    {
        color ??= Color.white;
        
        var count = Indexes.Length;
        var lastVertexIndex = -1;
        for (var i = 0; i < count; i++)
        {
            var meshMatrix = Matrices[i];
            var end = i + 1 >= count ? Tris.Length : Indexes[i + 1];
            var startIndex = lastVertexIndex + 1;
            for (var j = Indexes[i]; j < end; j += 3)
            {
                var t0 = startIndex + Tris[j];
                var t1 = startIndex + Tris[j + 1];
                var t2 = startIndex + Tris[j + 2];
                lastVertexIndex = math.max(lastVertexIndex, math.max(t0, math.max(t1, t2)));
                
                var p0 = math.transform(meshMatrix, Vertices[t0]);
                var p1 = math.transform(meshMatrix, Vertices[t1]);
                var p2 = math.transform(meshMatrix, Vertices[t2]);

                p0 = matrix.MultiplyPoint3x4(p0);
                p1 = matrix.MultiplyPoint3x4(p1);
                p2 = matrix.MultiplyPoint3x4(p2);
                    
                Debug.DrawLine(p0, p1, color.Value, duration, depthTest);
                Debug.DrawLine(p1, p2, color.Value, duration, depthTest);
                Debug.DrawLine(p2, p0, color.Value, duration, depthTest);
            }
        }
    }
}

public static class MeshGatherer
{
    public static GatheredMeshes GatherMeshes()
    {
        var meshFilters = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        var indexes = new List<int>();
        var vertices = new List<Vector3>();
        var tris = new List<int>();
        var matrices = new List<Matrix4x4>();
        
        foreach (var meshFilter in meshFilters)
        {
            var mesh = meshFilter.sharedMesh;
            
            if (!mesh)
            {
                continue;
            }

            if (!meshFilter.TryGetComponent<Renderer>(out var renderer))
            {
                continue;
            }
            
            indexes.Add(tris.Count);
            vertices.AddRange(mesh.vertices);
            tris.AddRange(mesh.triangles);
            matrices.Add(renderer.localToWorldMatrix);
        }

        // Convert to NativeArray
        var nIndexes = new NativeArray<int>(indexes.Count, Allocator.TempJob);
        for (var i = 0; i < nIndexes.Length; i++)
        {
            nIndexes[i] = indexes[i];
        }
        
        var nVerts = new NativeArray<float3>(vertices.Count, Allocator.TempJob);
        for (var i = 0; i < nVerts.Length; i++)
        {
            nVerts[i] = vertices[i];
        }
        
        var nTris = new NativeArray<int>(tris.Count, Allocator.TempJob);
        for (var i = 0; i < nTris.Length; i++)
        {
            nTris[i] = tris[i];
        }
        
        var nMatrices = new NativeArray<float4x4>(matrices.Count, Allocator.TempJob);
        for (var i = 0; i < nMatrices.Length; i++)
        {
            nMatrices[i] = matrices[i];
        }

        return new GatheredMeshes()
        {
            Indexes = nIndexes,
            Vertices = nVerts,
            Tris = nTris,
            Matrices = nMatrices
        };
    }
}

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct JobRasterize : IJob
{
    public int Width, Height, Depth;
    public float VoxelSize, VoxelHeight, WalkableSlopeAngle; 
    public GatheredMeshes GatheredMeshes;
    public Heightfield Heightfield;
    
    public void Execute()
    {
         if (Width < 0) throw new ArgumentOutOfRangeException(nameof(Width));
         if (Depth < 0) throw new ArgumentOutOfRangeException(nameof(Depth));

        var halfSize = VoxelSize * 0.5f;
        var halfHeight = VoxelHeight * 0.5f;
        var zeroPosition = -new float3(Width * halfSize, Height * halfHeight, Depth * halfSize);

        // 복셀의 사이즈가 (1, 1, 1)이 되도록 정규화
        var voxelMatrix = math.mul(
            math.inverse(float4x4.Scale(VoxelSize, VoxelHeight, VoxelSize)),
            float4x4.Translate(-zeroPosition));

        var slopeLimitCos = math.cos(WalkableSlopeAngle * math.TORADIANS);

        // 삼각형 클리핑 시 최대 7개의 정점을 가짐
        var input = new NativeArray<float3>(7, Allocator.Temp);
        var inRow = new NativeArray<float3>(7, Allocator.Temp);
        var out1 = new NativeArray<float3>(7, Allocator.Temp);
        var out2 = new NativeArray<float3>(7, Allocator.Temp);
        
        var indexCount = GatheredMeshes.Indexes.Length;
        var lastVertexIndex = -1;
        for (var i = 0; i < indexCount; i++)
        {
            var meshMatrix = GatheredMeshes.Matrices[i];
            var endIndex = i + 1 >= indexCount ? GatheredMeshes.Tris.Length : GatheredMeshes.Indexes[i + 1];
            var startIndex = lastVertexIndex + 1;
            for (var j = GatheredMeshes.Indexes[i]; j < endIndex; j += 3)
            {
                // 메쉬 위치, 스케일 등에 따른 매트릭스 적용
                var t0 = startIndex + GatheredMeshes.Tris[j];
                var t1 = startIndex + GatheredMeshes.Tris[j + 1];
                var t2 = startIndex + GatheredMeshes.Tris[j + 2];
                lastVertexIndex = math.max(lastVertexIndex, math.max(t0, math.max(t1, t2)));

                var p0 = math.transform(meshMatrix, GatheredMeshes.Vertices[t0]);
                var p1 = math.transform(meshMatrix, GatheredMeshes.Vertices[t1]);
                var p2 = math.transform(meshMatrix, GatheredMeshes.Vertices[t2]);
                var normal = math.normalize(math.cross(p1 - p0, p2 - p0));

                // 정규화
                p0 = voxelMatrix.MultiplyPoint3x4(p0);
                p1 = voxelMatrix.MultiplyPoint3x4(p1);
                p2 = voxelMatrix.MultiplyPoint3x4(p2);

                var minX = (int)math.floor(math.min(math.min(p0.x, p1.x), p2.x));
                var maxX = (int)math.floor(math.max(math.max(p0.x, p1.x), p2.x));
                var minZ = (int)math.floor(math.min(math.min(p0.z, p1.z), p2.z));
                var maxZ = (int)math.floor(math.max(math.max(p0.z, p1.z), p2.z));

                if (minX >= Width || minZ >= Depth || maxX < 0 || maxZ < 0)
                {
                    // triangle is completely out of bounds
                    continue;
                }

                // area 구분 사용하지 않아 단순 bool타입으로 처리.
                var isSolid = math.abs(normal.y) < slopeLimitCos;

                // (0, 0)에 위치한 폴리를 0에서 잘라야 하기 때문에 -1을 최솟값으로 잡음.
                minZ = math.clamp(minZ, -1, Depth - 1);
                maxZ = math.clamp(maxZ, 0, Depth - 1);

                input[0] = p0;
                input[1] = p1;
                input[2] = p2;
                var inCount = 3;

                for (var z = minZ; z <= maxZ; z++)
                {
                    DividePoly(input, inCount, inRow, out var inRowCount, out1, out var out1Count, 2, z + 1);
                    // 나머지 값을 다음 input으로 사용
                    (input, out1) = (out1, input);
                    inCount = out1Count;

                    if (inRowCount < 3)
                    {
                        continue;
                    }

                    if (z < 0)
                    {
                        // -1 에서 시작하여 bounds에 맞춰 자른 경우 추가로 x축으로 자를 필요 없음
                        continue;
                    }

                    // 해당 row의 minX, minY
                    var fMinX = inRow[0].x;
                    var fMaxX = inRow[0].x;
                    for (var k = 1; k < inRowCount; k++)
                    {
                        if (fMinX > inRow[k].x)
                        {
                            fMinX = inRow[k].x;
                        }

                        if (fMaxX < inRow[k].x)
                        {
                            fMaxX = inRow[k].x;
                        }
                    }

                    minX = math.max(-1, (int)math.floor(fMinX));
                    maxX = math.min(Width - 1, (int)math.floor(fMaxX));

                    for (var x = minX; x <= maxX; x++)
                    {
                        DividePoly(inRow, inRowCount, out1, out out1Count, out2, out var out2Count, 0, x + 1);
                        // 나머지 값을 다음 input(inRow)으로 사용
                        (inRow, out2) = (out2, inRow);
                        inRowCount = out2Count;

                        if (out1Count < 3)
                        {
                            continue;
                        }

                        if (x < 0)
                        {
                            // -1 에서 시작하여 bounds에 맞춰 자른 경우 span 설정 필요 없음.
                            continue;
                        }
                        
                        // Add span with out1.
                        // 잘라진 폴리에서 y값 min, max 구해서 span 설정.
                        var fMinY = out1[0].y;
                        var fMaxY = out1[0].y;
                        for (var k = 0; k < out1Count; k++)
                        {
                            if (fMinY > out1[k].y)
                            {
                                fMinY = out1[k].y;
                            }

                            if (fMaxY < out1[k].y)
                            {
                                fMaxY = out1[k].y;
                            }
                        }
                        
                        var minY = (int)math.floor(fMinY);
                        var maxY = (int)math.ceil(fMaxY);

                        // span is out of bounds
                        if (maxY < 0 || minY >= Height)
                        {
                            continue;
                        }

                        // clamp
                        minY = math.clamp(minY, 0, Height - 1);
                        maxY = math.clamp(maxY, minY + 1, Height);

                        // add span
                        Heightfield.AddSpan(x, z, minY, maxY, isSolid);
                    }
                }
            }
        }
        
        input.Dispose();
        inRow.Dispose();
        out1.Dispose();
        out2.Dispose();
    }
    
    private static void DividePoly(NativeArray<float3> input, int inCount,
        NativeArray<float3> out1, out int out1Count,
        NativeArray<float3> out2, out int out2Count,
        int axis, float offset)
    {
        out1Count = 0;
        out2Count = 0;
        if (inCount < 3)
        {
            return;
        }

        var prev = input[inCount - 1];
        var prevValue = prev[axis] - offset;
        var isPrevAbove = prevValue >= 0;
        for (var i = 0; i < inCount; i++)
        {
            var current = input[i];
            var currentValue = current[axis] - offset;
            var isCurrentAbove = currentValue >= 0;
            // 교차 점 추가
            if (isPrevAbove != isCurrentAbove)
            {
                var s = prevValue / (prevValue - currentValue);
                var newPoint = prev + s * (current - prev);
                out1[out1Count++] = newPoint;
                out2[out2Count++] = newPoint;
            }

            if (isCurrentAbove)
            {
                out2[out2Count++] = current;
            }
            else
            {
                out1[out1Count++] = current;
            }

            prev = current;
            prevValue = currentValue;
            isPrevAbove = isCurrentAbove;
        }
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public struct Vertex
{
    // 생성 시 퍼지는 효과를 주기 위해 버텍스를 구조체로 저장
    public int Index;
    public float Distance;
    public float3 Position;
}

public static class MarchingSquaresGenerator
{
    /// <param name="bottomY">밑면의 로컬 포지션 Y값</param>
    /// <param name="height">높이 (_height - _bottomY == 로컬 포지션 Y값)</param>
    /// <param name="contourRound">외곽 버텍스를 일정만큼 내려서 둥글게 만듦</param>
    /// <param name="interpolation">메쉬를 조금 더 둥글게 만들도록 함. 버텍스의 weight 최솟값이 0.25이므로 0.25이상의 값을 넘지 않도록 하는게 좋음.</param>
    /// <param name="flattenCount">정점 평탄화 횟수</param>
    /// <param name="wallUVMultiplier">생성된 기둥의 벽의 UV.x에 곱해지는 값</param>
    public static void Generate(NativeArray<FloodFillData> floodFillDatas, float4x4 matrix, MeshFilter meshFilter,
        float bottomY, float height, float contourRound, 
        float interpolation, int flattenCount, float wallUVMultiplier)
    {
        var minVertexCount = floodFillDatas.Length * 4;
        using var nVertices = new NativeList<float3>(minVertexCount * 2, Allocator.Persistent);
        using var nTris = new NativeList<int>(minVertexCount * 6, Allocator.Persistent);
        using var nTris2 = new NativeList<int>(minVertexCount * 6, Allocator.Persistent);
        using var nContours = new NativeHashMap<int, int>(minVertexCount, Allocator.Persistent);
        using var nUVs = new NativeList<float2>(minVertexCount * 2, Allocator.Persistent);
        
        new JobMarchingSquares()
        {
            FloodFillDatas = floodFillDatas,
            BottomY = bottomY,
            ContourRound = contourRound,
            Height = height,
            Interpolation = interpolation,
            FlattenCount = flattenCount,
            WallUVMultiplier = wallUVMultiplier,
            Matrix = matrix,
            
            Vertices = nVertices,
            Tris = nTris,
            Tris2 = nTris2,
            Contours = nContours,
            UVs = nUVs,
        }.Schedule().Complete();
        
        // Mesh 설정
        // Copy
        var length = nVertices.Length;
        // _vertices = new Vertex[length];
        var verts = new Vector3[length];
        for (var i = 0; i < length; i++)
        {
            // _vertices[i] = nVertices[i];
            verts[i] = nVertices[i];
        }
        length = nTris.Length;
        var tris = new int[length];
        for (var i = 0; i < length; i++)
        {
            tris[i] = nTris[i];
        }
        length = nTris2.Length;
        var tris2 = new int[length];
        for (var i = 0; i < length; i++)
        {
            tris2[i] = nTris2[i];
        }
        length = nUVs.Length;
        var uvs = new Vector2[length];
        for (var i = 0; i < length; i++)
        {
            uvs[i] = nUVs[i];
        }
        
        
        var mesh = new Mesh()
        {
            name = "MarchingSquares",
            subMeshCount = 2,
        };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetTriangles(tris2, 1);
        mesh.SetUVs(0, uvs);

        meshFilter.mesh = mesh;
    }
}

[BurstCompile]
public struct JobMarchingSquares : IJob
{
    public NativeArray<FloodFillData> FloodFillDatas;
    public float BottomY;
    public float ContourRound;
    public float Height;
    public float Interpolation;
    public int FlattenCount;
    public float WallUVMultiplier;
    public float4x4 Matrix;
    
    public NativeList<float3> Vertices;
    public NativeList<int> Tris;
    public NativeList<int> Tris2;
    public NativeHashMap<int, int> Contours;
    public NativeList<float2> UVs;
    
    public void Execute()
    {
        // 1. 그리드 설정
        var minX = FloodFillDatas[0].X;
        var maxX = FloodFillDatas[0].X;
        var minZ = FloodFillDatas[0].Z;
        var maxZ = FloodFillDatas[0].Z;

        for (var i = 1; i < FloodFillDatas.Length; i++)
        {
            minX = math.min(minX, FloodFillDatas[i].X);
            maxX = math.max(maxX, FloodFillDatas[i].X);
            minZ = math.min(minZ, FloodFillDatas[i].Z);
            maxZ = math.max(maxZ, FloodFillDatas[i].Z);
        }
        
        // 1칸 씩 확장
        minX -= 1;
        minZ -= 1;
        // 셀 좌표가 좌하단의 버텍스 좌표가 되므로 우상단의 버텍스를 위해 +1
        maxX += 2;
        maxZ += 2;

        var width = maxX - minX;
        var height = maxZ - minZ;
        
        // 2. Marching Squares의 정점으로 사용할 Corner들의 값 설정
        var cornerWidth = width + 1;
        var cornerHeight = height + 1;
        var cornerLength = cornerWidth * cornerHeight;
        var cornerValues = new NativeArray<float>(cornerLength, Allocator.Temp); 
        var cornerHeights = new NativeArray<float>(cornerLength, Allocator.Temp); 
        var cornerCounts = new NativeArray<int>(cornerLength, Allocator.Temp); // height를 설정한 횟수 
        var cornerDistanceFields = new NativeArray<float>(cornerLength, Allocator.Temp); // height를 설정한 횟수 
        
        foreach (var fillData in FloodFillDatas)
        {
            var localX = fillData.X - minX;
            var localZ = fillData.Z - minZ;
            
            var aIndex = localX + localZ * cornerWidth;
            var bIndex = aIndex + cornerWidth;
            var cIndex = bIndex + 1;
            var dIndex = aIndex + 1;
            
            cornerValues[aIndex] += 0.25f;
            cornerHeights[aIndex] += fillData.Corner0;
            cornerCounts[aIndex]++;
            if (cornerDistanceFields[aIndex] < 1)
            {
                cornerDistanceFields[aIndex] = fillData.Distance;
            }
            else
            {
                cornerDistanceFields[aIndex] = math.min(cornerDistanceFields[aIndex], fillData.Distance);
            }
            
            cornerValues[bIndex] += 0.25f;
            cornerHeights[bIndex] += fillData.Corner1;
            cornerCounts[bIndex]++;
            if (cornerDistanceFields[bIndex] < 1)
            {
                cornerDistanceFields[bIndex] = fillData.Distance;
            }
            else
            {
                cornerDistanceFields[bIndex] = math.min(cornerDistanceFields[bIndex], fillData.Distance);
            }
            
            cornerValues[cIndex] += 0.25f;
            cornerHeights[cIndex] += fillData.Corner2;
            cornerCounts[cIndex]++;
            if (cornerDistanceFields[cIndex] < 1)
            {
                cornerDistanceFields[cIndex] = fillData.Distance;
            }
            else
            {
                cornerDistanceFields[cIndex] = math.min(cornerDistanceFields[cIndex], fillData.Distance);
            }
            
            cornerValues[dIndex] += 0.25f;
            cornerHeights[dIndex] += fillData.Corner3;
            cornerCounts[dIndex]++;
            if (cornerDistanceFields[dIndex] < 1)
            {
                cornerDistanceFields[dIndex] = fillData.Distance;
            }
            else
            {
                cornerDistanceFields[dIndex] = math.min(cornerDistanceFields[dIndex], fillData.Distance);
            }
        }

        // 한 정점에서 여러 값이 있을 경우 평균 값으로 설정
        // Marching Squares는 2D 평면을 기준으로 하기 때문에 서로 다른 층의 셀이 2D 좌표 상 연결될 경우 코너의 값이 크게 차이날 수 있음
        // 월드 스케일 -> 복셀 스케일로 변환
        var bottomY = BottomY * (1f/ math.length(Matrix.c1.xyz));
        for (var i = 0; i < cornerLength; i++)
        {
            if (cornerCounts[i] > 1)
            {
                cornerHeights[i] /= cornerCounts[i];
            }

            // 땅에 묻히도록 전체적으로 내림
            cornerHeights[i] += bottomY;
        }
        
        // 평탄화
        for (var i = 0; i < FlattenCount; i++)
        {
            Flatten(cornerHeights, width, height, cornerWidth, cornerHeight);
        }

        // 3. 면을 만들 때 사용되는 버텍스 설정
        var vertexCount = 0;
        // 버텍스 재사용 위한 캐시
        var nodeCache = new NativeArray<int>(cornerLength, Allocator.Temp);
        var horizontalCache = new NativeArray<int>(cornerLength, Allocator.Temp);
        var verticalCache = new NativeArray<int>(cornerLength, Allocator.Temp);
        
        for (var z = 0; z < height; z++)
        {
            for (var x = 0; x < width; x++)
            {
                // 좌하단 버텍스를 기준으로 오른쪽과 위로 향하는 엣지를 캐싱
                var edgeIndex = x + z * width;
                var selfIndex = x + z * cornerWidth;
                var rightIndex = selfIndex + 1;
                var upIndex = selfIndex + cornerWidth;
                
                var selfValue = cornerValues[selfIndex];
                var rightValue = cornerValues[rightIndex];
                var upValue = cornerValues[upIndex];
                
                var isSelfTrue = selfValue > math.EPSILON;
                var isRightTrue = rightValue > math.EPSILON;
                var isUpTrue = upValue > math.EPSILON;

                if (isSelfTrue)
                {
                    // 정점
                    var index = vertexCount++;
                    Vertices.Add(new float3(x, cornerHeights[selfIndex], z));
                    nodeCache[x + z * cornerWidth] = index;
                }
                
                // self와 right 값이 다를 경우 rightEdge에 점 생성
                if ((isSelfTrue && !isRightTrue) ||
                    (!isSelfTrue && isRightTrue))
                {
                    var index = vertexCount++;
                    // interpolated
                    var value = (Interpolation - selfValue) / (rightValue - selfValue);
                    var trueIndex = isSelfTrue ? selfIndex : rightIndex;
                    Vertices.Add(new float3(x + value, cornerHeights[trueIndex], z));
                    horizontalCache[edgeIndex] = index;
                }
                
                // self와 up 값이 다를 경우 upEdge에 점 생성
                if ((isSelfTrue && !isUpTrue) ||
                    (!isSelfTrue && isUpTrue))
                {
                    var index = vertexCount++;
                    // interpolated
                    var value = (Interpolation - selfValue) / (upValue - selfValue);
                    var trueIndex = isSelfTrue ? selfIndex : upIndex;
                    Vertices.Add(new float3(x, cornerHeights[trueIndex], z + value));
                    verticalCache[edgeIndex] = index;
                }
            }
        }

        for (var i = 0; i < vertexCount; i++)
        {
            // var vertex = Vertices[i];
            var position = Vertices[i];
            // UV 설정
            UVs.Add(new Vector2(position.x, position.z));
            
            // MarchingSquares 좌표 -> Recast 좌표
            position = new Vector3(minX + position.x, position.y,  minZ + position.z);
            
            
            // Recast position -> World position
            position = Matrix.MultiplyPoint3x4(position);
            
            // vertex.Position = position;
            Vertices[i] = position;
        }

        // 4. Marching Squares로 셀마다 메쉬 생성 및 외곽선 캐싱
        for (var z = 0; z < height; z++)
        {
            for (var x = 0; x < width; x++)
            {
                Triangulate(width, height, cornerWidth, x, z, cornerValues, nodeCache, horizontalCache, verticalCache, Tris, Contours);
            }
        }

        // 5. Contour에 따라 ring 생성
        var contourKeys = Contours.GetKeyArray(Allocator.Temp);
        var contourCount = Contours.Count;
        var rings = new NativeArray<int>(contourCount, Allocator.Temp);
        var seamIndexes = new NativeList<int>(Allocator.Temp);
        var linkedIndexes = new NativeHashSet<int>(contourCount, Allocator.Temp);
        var ringCount = 0; // 링(seam)의 갯수
        var linkCount = 0;
        for (var i = 0; i < contourCount; i++)
        {
            // 이미 연결 됨
            if (linkedIndexes.Contains(contourKeys[i]))
            {
                continue;
            }

            // 이전 루프를 돌면서 linked 되지 않았으면 새로운 seam임
            ringCount++;
            seamIndexes.Add(linkCount);
            var current = contourKeys[i];
            while (true)
            {
                linkedIndexes.Add(current);
                rings[linkCount++] = current;
                if (Contours.TryGetValue(current, out var next))
                {
                    if (next == contourKeys[i])
                    {
                        // Closed
                        break;
                    }
                    current = next;
                    continue;
                }
                
                throw new System.Exception("Contour not linked");
            }
        }

        contourKeys.Dispose();
        linkedIndexes.Dispose();

        // 6. Ring이 면과 벽에서 uv 상 다른 위치를 가지기 때문에 정점 복사
        var faceVertexCount = vertexCount;  // 면의 버텍스 카운트
        var uvSetCount = 0;
        // seam은 버텍스가 같은 위치에 2개라 ringCount 만큼 +
        var ringsOnWall = new NativeArray<int>(contourCount + ringCount, Allocator.Temp);
        
        for (var i = 0; i < ringCount; i++)
        {
            var uvX = 0f;
            var end = i + 1 < ringCount ? seamIndexes[i + 1] : contourCount;
            var last = Vertices[rings[seamIndexes[i]]];
            for (var j = seamIndexes[i]; j < end; j++)
            {
                var newIndex = vertexCount++;
                var vertex = Vertices[rings[j]];
                Vertices.Add(vertex);
                ringsOnWall[j + i] = newIndex;
                if (j > seamIndexes[i])
                {
                    var distance = math.sqrt(math.pow(vertex.x - last.x, 2) +
                                             math.pow(vertex.z - last.z, 2));
                    uvX += distance * WallUVMultiplier;
                }
                UVs.Add(new Vector2(uvX, 1f));
                last = vertex;
            }

            {
                var seamIndex = vertexCount++; // == newIndex
                var vertex = Vertices[rings[seamIndexes[i]]];
                Vertices.Add(vertex);
                ringsOnWall[end + i] = seamIndex;
                var distance = math.sqrt(math.pow(vertex.x - last.x, 2) +
                                         math.pow(vertex.z - last.z, 2));
                uvX += distance;
                UVs.Add(new Vector2(uvX, 1f));
            }

            // 심에서 잘리지 않도록 uv 정수로 떨어지게 재설정
            var roundedX = math.round(uvX);
            var xRatio = roundedX / uvX;
            for (var j = seamIndexes[i] + i; j <= end; j++)
            {
                // ring이 여러개일 때 j + faceVertexCount는 2번 링이 1번 링의 uv를 설정함
                var uv = UVs[uvSetCount + faceVertexCount];
                uv.x *= xRatio;
                UVs[uvSetCount + faceVertexCount] = uv;
                uvSetCount++;
            }
        }
        
        // 7. 아랫면 + Ring 복사
        Vertices.AddRange(Vertices.AsArray());
        var trisCount = Tris.Length;
        Tris2.SetCapacity(trisCount);
        for (var i = 0; i < trisCount; i++)
        {
            Tris2.Add(Tris[i] + vertexCount);
        }
        
        // 아랫면 uv
        for (var i = 0; i < faceVertexCount; i++)
        {
            UVs.Add(Vector2.zero);
        }

        // 아래 Ring uv 복사
        var ringVertexCount = vertexCount - faceVertexCount; // 면 + Ring
        for (var i = 0; i < ringVertexCount; i++)
        {
            var upVertexValue = UVs[faceVertexCount + i];
            upVertexValue.y = 0f;
            UVs.Add(upVertexValue);
        }
        
        // 윗면 y값 설정
        for (var i = 0; i < vertexCount; i++)
        {
            var vertex = Vertices[i];
            vertex.y += Height;
            Vertices[i] = vertex;
        }
        
        // 8. 벽 생성
        for (var i = 0; i < ringCount; i++)
        {
            var end = i + 1 < ringCount ? seamIndexes[i + 1] : contourCount + ringCount - 1;
            for (var j = seamIndexes[i] + i + 1; j <= end; j++)
            {
                AddQuad(Tris2,
                    ringsOnWall[j] + vertexCount,
                    ringsOnWall[j],
                    ringsOnWall[j - 1],
                    ringsOnWall[j - 1] + vertexCount);
            }
        }

        seamIndexes.Dispose();
        
        // 9. Ring y값 조절하여 둥글게 표시
        for (var i = 0; i < contourCount; i++)
        {
            var top = Vertices[rings[i]];
            top.y -= ContourRound;
            Vertices[rings[i]] = top;
            var bottom = Vertices[rings[i] + vertexCount];
            bottom.y += ContourRound;
            Vertices[rings[i] + vertexCount] = bottom;
        }
        for (var i = 0; i < contourCount + ringCount; i++)
        {
            var top = Vertices[ringsOnWall[i]];
            top.y -= ContourRound;
            Vertices[ringsOnWall[i]] = top;
            var bottom = Vertices[ringsOnWall[i] + vertexCount];
            bottom.y += ContourRound;
            Vertices[ringsOnWall[i] + vertexCount] = bottom;
        }
        
        // 10. 종료
        rings.Dispose();
        ringsOnWall.Dispose();
    }

    private static void Triangulate(int width, int height, int cornerWidth,
        int x, int z,
        NativeArray<float> cornerValues,
        NativeArray<int> nodeCache, NativeArray<int> horizontalCache, NativeArray<int> verticalCache,
        NativeList<int> tris, NativeHashMap<int, int> contours)
    {
        var index = x + z * cornerWidth;
        var a = nodeCache[index];
        var b = nodeCache[index + cornerWidth];
        var c = nodeCache[index + cornerWidth + 1];
        var d = nodeCache[index + 1];

        var edgeIndex = x + z * width;
        // index의 점을 좌하단으로 두고 있는 셀의 엣지
        var left = verticalCache[edgeIndex];
        var top = z < height - 1 ? horizontalCache[edgeIndex + width] : 0;
        var right = x < width - 1 ? verticalCache[edgeIndex + 1] : 0;
        var bottom = horizontalCache[edgeIndex];

        switch (GetVoxelType(cornerValues, index, cornerWidth))
        {
            case 0:
            {
                // empty
                return;
            }
            // Triangle
            case 1:
            {
                // left bottom tri
                AddTriangle(tris, contours, a, left, bottom);
                return;
            }
            case 2:
            {
                // right bottom tri
                AddTriangle(tris, contours, d, bottom, right);
                return;
            }
            case 4:
            {
                // left top tri
                AddTriangle(tris, contours, b, top, left);
                return;
            }
            case 8:
            {
                // right top tri
                AddTriangle(tris, contours, c, right, top);
                return;
            }
            // Quad
            case 3:
            {
                // bottom
                AddQuad(tris, contours, d, a, left, right);
                break;
            }
            case 5:
            {
                // left
                AddQuad(tris, contours, a, b, top, bottom);
                break;
            }
            case 10:
            {
                // right
                AddQuad(tris, contours, c, d, bottom, top);
                break;
            }
            case 12:
            {
                // top
                AddQuad(tris, contours, b, c, right, left);
                break;
            }
            case 15:
            {
                // full
                // no contours
                AddQuad(tris, a, b, c, d);
                break;
            }
            // Pentagon
            case 7:
            {
                // left bottom
                // AddPentagon(a, c, top, right, b);
                AddPentagon(tris, contours, d, a, b, top, right);
                break;
            }
            case 11:
            {
                // right bottom
                AddPentagon(tris, contours, c, d, a, left, top);
                break;
            }
            case 13:
            {
                // left top
                AddPentagon(tris, contours, a, b, c, right, bottom);
                break;
            }
            case 14:
            {
                // right top
                // AddPentagon(d, b, bottom, left, c);
                AddPentagon(tris, contours, b, c, d, bottom, left);
                break;
            }
            // Hexagon
            case 6:
            {
                // // left top, right bottom
                AddHexagon(tris, contours, b, top, right, d, bottom, left);
                break;
            }
            case 9:
            {
                // right top, left bottom
                AddHexagon(tris, contours, c, right, bottom, a, left, top);
                break;
            }
        }
    }
    
    private static byte GetVoxelType(NativeArray<float> cornerValues, int index, int width)
    {
        byte cellType = 0;
        if (cornerValues[index] > 0)
        {
            cellType |= 1;
        }
        if (cornerValues[index + 1] > 0)
        {
            cellType |= 2;
        }
        if (cornerValues[index + width] > 0)
        {
            cellType |= 4;
        }
        if (cornerValues[index + width + 1] > 0)
        {
            cellType |= 8;
        }
        return cellType;
    }
    
    private static void AddTriangle(NativeList<int> tris, NativeHashMap<int, int> contours, int a, int b, int c)
    {
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);

        contours.Add(b, c);
    }

    private static void AddQuad(NativeList<int> tris, NativeHashMap<int, int> contours, int a, int b, int c, int d)
    {
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
        tris.Add(a);
        tris.Add(c);
        tris.Add(d);

        contours.Add(c, d);
    }

    private static void AddQuad(NativeList<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
        tris.Add(a);
        tris.Add(c);
        tris.Add(d);
    }

    private static void AddPentagon(NativeList<int> tris, NativeHashMap<int, int> contours, int a, int b, int c, int d, int e)
    {
        // abc
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
        // acd
        tris.Add(a);
        tris.Add(c);
        tris.Add(d);
        // ade
        tris.Add(a);
        tris.Add(d);
        tris.Add(e);

        contours.Add(d, e);
    }

    private static void AddHexagon(NativeList<int> tris, NativeHashMap<int, int> contours, int a, int b, int c, int d, int e, int f)
    {
        // tri
        tris.Add(a);
        tris.Add(b);
        tris.Add(f);
        // tri
        tris.Add(d);
        tris.Add(e);
        tris.Add(c);

        // quad (connect)
        tris.Add(b);
        tris.Add(c);
        tris.Add(e);
        tris.Add(b);
        tris.Add(e);
        tris.Add(f);

        contours.Add(b, c);
        contours.Add(e, f);
    }
    
    private static void Flatten(NativeArray<float> values, int width, int height, int cornerWidth, int cornerHeight)
    {
        for (var y = 0; y < cornerHeight; y++)
        {
            for (var x = 0; x < cornerWidth; x++)
            {
                var index = x + y * cornerWidth;

                var value = values[index];
                if (value <= math.EPSILON)
                {
                    continue;
                }
                
                var neighborsValue = 0f;
                var neighborCount = 0;
                if (x > 0)
                {
                    // left
                    var v = values[index - 1];
                    if (v > math.EPSILON)
                    {
                        neighborsValue += v;
                        neighborCount++;
                    }
                }
                if (x < width)
                {
                    // right
                    var v = values[index + 1];
                    if (v > math.EPSILON)
                    {
                        neighborsValue += v;
                        neighborCount++;
                    }
                }
                if (y < height)
                {
                    // top
                    var v = values[index + cornerWidth];
                    if (v > math.EPSILON)
                    {
                        neighborsValue += v;
                        neighborCount++;
                    }
                }
                if (y > 0)
                {
                    // bottom
                    var v = values[index - cornerWidth];
                    if (v > math.EPSILON)
                    {
                        neighborsValue += v;
                        neighborCount++;
                    }
                }
                
                if (neighborCount < 1)
                {
                    continue;
                }
                neighborsValue /= neighborCount;

                values[index] = (value + neighborsValue) * 0.5f;
            }
        }
    }
}
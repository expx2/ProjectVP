# ProjectVP

개발 환경
- Unity 6000.3.9f1

## 개요
게임 `발로란트`에 등장하는 `독사의 구덩이`는 사용한 캐릭터를 중심으로 연기(구역)를 생성하는 스킬입니다.

이 연기는 지형을 타고 퍼지며, 내부에 진입하면 시야가 제한되고 캐릭터가 강조되며 적군은 디버프를 받습니다.

이 프로젝트에서는 연기의 생성만을 다룹니다.

![Image](https://github.com/user-attachments/assets/875f39a4-ed4a-48f0-b42e-4ee28b29e8a1)

`Counter-Strike 2`의 [Responsive Smokes](https://youtu.be/_y9MpNcAitQ?si=HJbmUXFp2Tz5nWQA), `Battlefield 6`의 연막도 위와 같이 지형에 반응하여 생성됩니다.

## 결과물

[영상](https://youtu.be/MrCXZ84EqHI)

<img src="https://github.com/user-attachments/assets/0a04a10b-efd2-42f1-b71a-101caf8c2eac" width="600">
<img src="https://github.com/user-attachments/assets/efeabdf8-3bcd-436b-ae0a-b0891a7d1833" width="600">


## 구현

### Recast

구현 방법을 연구하면서 `CS2`의 연막을 참고했는데, 3D 환경의 맵을 Voxelize하여 그리드를 만들고 연막을 생성하는 것으로 보였습니다.

`발로란트`와 `CS2` 모두 공정한 환경이 중요한 게임이다 보니 실시간으로 환경을 파악하기보다는 미리 맵 전체를 스캔하여 저장하는 방식이 더 나았을 것으로 생각됩니다.

`독사의 구덩이`는 `CS2`의 연막과 다르게 캐릭터가 이동할 수 있는 공간에만 연기가 퍼지는 것을 확인해 Unity의 Navigation 시스템을 사용하려 했습니다.

하지만 Unity의 NavMesh는 생성된 그리드에 접근할 수 없어 NavMesh의 생성 알고리즘인 Recast를 사용했습니다.

C++로 작성된 Recast를 C#으로 옮겼고, 이후에 BurstCompile을 사용해서 성능을 개선했습니다.

>60 x 120 x 60 복셀의 월드와 Tris 100k 환경에서의 실행 시간 102ms => 41ms<br>이 성능 개선은 월드가 커질수록, Tris의 양이 많아질수록 더 뚜렷할 것으로 예상됩니다.

핵심 스크립트 [RecastBuilder.cs](https://github.com/expx2/ProjectVP/blob/main/Assets/Scripts/Recast/RecastBuilder.cs)

<details>
<summary>Rasterizer.cs</summary>

```csharp
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
```

</details>

### Flood Fill

맵을 그리드화하는 단계부터 생성될 영역을 구성할 때 사용할 방법으로 떠올렸고, Recast로 생성된 이웃 정보를 이용해 비교적 쉽게 구현할 수 있었습니다.

원점으로부터 DistanceField를 만들면서, Distance를 Priority로 사용하는 Priority Queue를 이용해 Fill합니다.

핵심 스크립트 [FloodFiller.cs](https://github.com/expx2/ProjectVP/blob/main/Assets/Scripts/FloodFill/FloodFiller.cs)

<details>
<summary>이웃 CompactSpan을 Queue에 등록하는 코드</summary>

```csharp
// (-1 , 0)
var con = currentSpan.GetConnection(0);
if (con != CompactSpan.NotConnected)
{
    var aX = x + RecastBuilder.DirectionOffsetX[0];
    var aZ = z + RecastBuilder.DirectionOffsetZ[0];
    var aIndex = (int)graph.Cells[aX + aZ * width].Index + con;
    if (Enqueue(aX, aZ, aIndex, (ushort)(current.Distance + 2)))
    {
        // (-1, 0) + (0, -1) = (-1, -1)
        con = graph.CompactSpans[aIndex].GetConnection(3);
        if (con != CompactSpan.NotConnected)
        {
            var bX = aX + RecastBuilder.DirectionOffsetX[3];
            var bZ = aZ + RecastBuilder.DirectionOffsetZ[3];
            var bIndex = (int)graph.Cells[bX + bZ * width].Index + con;
            Enqueue(bX, bZ, bIndex, (ushort)(current.Distance + 3));
        }
    }
}

bool Enqueue(int x, int z, int spanIndex, ushort distance)
{
    var idx = x + z * width;

    if (!graph.IsWalkables[spanIndex])
    {
        // Eroded
        return false;
    }
            
    if (distanceFields[idx] >= 1)
    {
        // 이미 들림
        return false;
    }
            
    queue.Enqueue(new FloodFillData()
    {
        X = x,
        Z = z,
        Corner0 = graph.GetCornerHeight(x, z, spanIndex, 0),
        Corner1 = graph.GetCornerHeight(x, z, spanIndex, 1),
        Corner2 = graph.GetCornerHeight(x, z, spanIndex, 2),
        Corner3 = graph.GetCornerHeight(x, z, spanIndex, 3),
        SpanIndex = spanIndex,
        Distance = distance,
    }, distance);
    distanceFields[idx] = distance;
    queueCount++;
    return true;
}
```

</details>


### Marching Squares

Recast에서 사용하는 메쉬(poly) 생성은 외곽선을 구하고, Ear Clipping으로 메쉬를 생성하는 것으로 파악했습니다.

하지만 `독사의 구덩이`는 콘셉트상 메쉬가 출렁이는 효과가 있고, 생성될 때도 기준점으로부터 점진적으로 바닥에서 올라오는 방식이기 때문에 버텍스가 균일하게 분포되어 있어야 합니다.

다양한 메쉬 생성 알고리즘을 연구하던 중 인게임상에서 생성된 메쉬를 위에서 본 모양을 보고 힌트를 얻어 Marching Squares 알고리즘을 사용하기로 했습니다.

<img src="https://github.com/user-attachments/assets/69f3f5b0-486a-4b08-b5fe-8b0307e76cc1" height="360">
<img src="https://github.com/user-attachments/assets/1c0a6724-20ed-4dbc-80a6-a71d46ecce9b" height="360">

Marching Squares는 평면을 만드는 알고리즘이기에, 아래와 같은 방식을 거쳐 원통형 메쉬로 생성했습니다.

1. Marching Squares를 진행하여 윗면 생성하며 외곽선을 기록, 복사 (UV 위치가 다르기 때문에 외곽선을 복사했습니다.)
2. 윗면을 복사하여 아랫면 생성
3. 복사된 외곽선을 이어 벽면 생성

핵심 스크립트 [MarchingSquaresGenerator.cs](https://github.com/silsuwoang/ProjectVP/blob/main/Assets/Scripts/MarchingSquares/MarchingSquaresGenerator.cs)

<details>
<summary>기록된 외곽점으로 외곽선 Ring을 만드는 코드</summary>

```csharp
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
```
</details>

### Shader Graph

생성된 메쉬의 출렁임과 연기 느낌을 내기 위해 Shader Graph를 이용해 면과 벽 두 개의 쉐이더를 작성했습니다.

<img src="https://github.com/user-attachments/assets/02ca6535-469f-451a-9783-e3f5b9561252" width="600">
<img src="https://github.com/user-attachments/assets/7360ca88-ed9e-4141-b933-6233aacd9f8f" width="600">

## 포스트모템

경력을 이어오며 다양한 기능과 콘텐츠를 만들었지만, 어느 한 기능을 시간을 들여 깊게 연구하고 개발한 경험은 없었습니다.

그러한 경험을 쌓기 위해 시작한 프로젝트로 개인적으로 의미 있는 시간이었습니다.

만족한 점
- Recast를 C#으로 옮기는 과정에서 단순히 옮기는 것이 아닌 동작 방식을 이해하고 제 프로젝트에 맞게 수정하는 데 노력했습니다.<br>그 과정이 프로젝트 시작 목적과 부합해 만족스러웠습니다.
- Recast에서 남긴 데이터를 이용해 Flood Fill과 Marching Squares가 수월하게 구현되었습니다.

부족한 점
- 프로젝트 진행 당시에는 몰랐지만, 이후 C#으로 작성된 Recast 오픈 소스가 있는 것을 확인했습니다.<br>업무였다면 개발 시간을 줄일 수 있는 부분으로 서칭이 부족했습니다.
- 인게임에서는 복도 형태의 지형에서 옆에 문이 있더라도 연기가 문으로 빠지지 않고 복도를 따라가는 경향이 있습니다.<br>Fill 부분에서 다른 알고리즘을 연구하거나 Priority 값을 조정하는 기능을 고민하는 시간을 가졌다면 좋았을 것 같습니다.

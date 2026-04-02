using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Solid 스판이 밑에 있는 Non-Solid 스판과 Max값의 차이가 적으면 Non-Solid로 설정. <br/>
/// Marks non-walkable spans as walkable if their maximum is within walkableClimb of a walkable neighbor.
/// </summary>
[BurstCompile]
public struct JobFilterLowHangingWalkableObstacles : IJob
{
    public Heightfield Heightfield;
    public int UpWalkableClimb;
    public int DownWalkableClimb;
    
    public void Execute()
    {
        for (var z = 0; z < Heightfield.Height; ++z)
        {
            for (var x = 0; x < Heightfield.Width; ++x)
            {
                var previous = -1;
                var isPreviousSolid = true;

                var index = x + z * Heightfield.Width;
                while (index >= 0)
                {
                    var current = Heightfield.Spans[index];
                    // 첫 루프에선 !isPreviousSolid가 false로 스킵됨.
                    if (!isPreviousSolid && previous >= 0)
                    {
                        var walkableClimb = current.Top > Heightfield.Spans[previous].Top ? DownWalkableClimb : UpWalkableClimb;
                        if (current.IsSolid &&
                            current.Top - Heightfield.Spans[previous].Top <= walkableClimb)
                        {
                            current.IsSolid = false;
                            Heightfield.Spans[index] = current;
                        }
                    }

                    previous = index;
                    isPreviousSolid = current.IsSolid;
                    index = Heightfield.Spans[index].Next;
                }
            }
        }
    }
}

/// <summary>
/// 이웃 스판이 없거나 일정 높이 차이가 날 경우, 절벽으로 판단하여 Solid로 설정 <br/>
/// Marks spans that are ledges as not-walkable.
/// </summary>
[BurstCompile]
public struct JobFilterLedgeSpans : IJob
{
    public Heightfield Heightfield;
    public int WalkableHeight;
    public int UpWalkableClimb;
    public int DownWalkableClimb;
    
    public void Execute()
    {
        for (var z = 0; z < Heightfield.Height; ++z)
        {
            for (var x = 0; x < Heightfield.Width; ++x)
            {
                for (var index = x + z * Heightfield.Width; index >= 0; index = Heightfield.Spans[index].Next)
                {
                    var current = Heightfield.Spans[index];
                    // Skip non-walkable spans.
                    if (current.IsSolid)
                    {
                        continue;
                    }

                    var floor = current.Top;
                    var ceiling = current.Next >= 0
                        ? Heightfield.Spans[current.Next].Bottom
                        : Heightfield.MaxHeightfieldHeight;

                    // 현재 스판의 top - 이웃 스판의 top
                    // 값이 일정 값 이상이면 절벽임. 
                    var lowestNeighborFloorDifference = Heightfield.MaxHeightfieldHeight;
                    var maxWalkable = math.max(UpWalkableClimb, DownWalkableClimb);
                    // 이웃들의 바닥 높이 차이가 크면 급경사(steep slope)로 ledge로 처리함
                    var lowestTraversableNeighborFloor = current.Top;
                    var highestTraversableNeighborFloor = current.Top;

                    for (var direction = 0; direction < 4; ++direction)
                    {
                        var curX = x + RecastBuilder.DirectionOffsetX[direction];
                        var curZ = z + RecastBuilder.DirectionOffsetZ[direction];

                        // 이웃이 범위 밖일 경우 ledge 처리
                        if (curX < 0 || curZ < 0 || curX >= Heightfield.Width || curZ >= Heightfield.Height)
                        {
                            lowestNeighborFloorDifference = -maxWalkable - 1;
                            break;
                        }

                        // 커넥션을 연결하기 전이라, 주변 Cell의 모든 Span을 검사함

                        // 현재 컬럼의 clearance 공간이 좁거나 (ceiling - floor) << 엄밀히 말하면 Ledge체크 아니지만 설정
                        // 이웃 루트 스판이 한참 위에 있을 경우 (neighborCeiling - floor)
                        // 이웃 스판의 min이 현재 스판의 max보다 낮을 경우 음수가 되기 때문에 skip되지 않음 
                        var neighborIndex = curX + curZ * Heightfield.Width;
                        var neighborCeiling = Heightfield.Spans[neighborIndex].IsDefault
                            ? Heightfield.MaxHeightfieldHeight
                            : Heightfield.Spans[neighborIndex].Bottom;
                        
                        var walkableClimb = current.Top > Heightfield.Spans[neighborIndex].Top ? DownWalkableClimb : UpWalkableClimb;
                        // Skip neighbour if the gap between the spans is too small.
                        if (math.min(ceiling, neighborCeiling) - floor >= WalkableHeight)
                        {
                            lowestNeighborFloorDifference = -walkableClimb - 1;
                            break;
                        }

                        // For each span in the neighboring column...
                        for (; neighborIndex >= 0; neighborIndex = Heightfield.Spans[neighborIndex].Next)
                        {
                            var neighborSpan = Heightfield.Spans[neighborIndex];
                            var neighborFloor = neighborSpan.Top;
                            neighborCeiling = neighborSpan.Next >= 0
                                ? Heightfield.Spans[neighborSpan.Next].Bottom
                                : Heightfield.MaxHeightfieldHeight;
                            // 공간이 좁은 경우는 체크하지 않음
                            // 빈 공간 (next.bottom - current.top)이 겹치는 부분이 서있을 수 있는 공간이 되어야 체크.
                            // 예시) 내리막 계단일 경우 다음 계단의 천장과 현재 계단의 바닥 공간이 충분해야 ㄱ모양으로 이동 가능함 
                            // Only consider neighboring areas that have enough overlap to be potentially traversable.
                            if (math.min(ceiling, neighborCeiling) - math.max(floor, neighborFloor) < WalkableHeight)
                            {
                                // 이웃으로 연결되지 않는 스판
                                // No space to traverse between them.
                                continue;
                            }

                            // 현재 스판 top과 이웃 스판 top의 차이
                            var neighborFloorDifference = neighborFloor - floor;
                            lowestNeighborFloorDifference =
                                math.min(lowestNeighborFloorDifference, neighborFloorDifference);

                            walkableClimb = current.Top > Heightfield.Spans[neighborIndex].Top ? DownWalkableClimb : UpWalkableClimb;
                            // 걸어 이동할 수 있을 수준의 높이일 경우
                            if (math.abs(neighborFloorDifference) <= walkableClimb)
                            {
                                lowestTraversableNeighborFloor =
                                    math.min(lowestTraversableNeighborFloor, neighborFloor);
                                highestTraversableNeighborFloor =
                                    math.max(highestTraversableNeighborFloor, neighborFloor);
                            }
                            else if (neighborFloorDifference > walkableClimb)
                            {
                                // 큰 낙차가 감지됨
                                break;
                            }
                        }
                    }

                    // 이웃의 바닥 - 바닥 < -walkableClimb
                    // 차이가 크면 ledge로 설정
                    if (lowestNeighborFloorDifference < -maxWalkable)
                    {
                        current.IsSolid = true;
                        Heightfield.Spans[index] = current;
                    }
                    // 이웃끼리 차이가 크면 급경사(steep slope)로 ledge로 설정함
                    else if (highestTraversableNeighborFloor - lowestTraversableNeighborFloor > maxWalkable)
                    {
                        current.IsSolid = true;
                        Heightfield.Spans[index] = current;
                    }
                }
            }
        }
    }
}

/// <summary>
/// 현재 스판의 Max와 다음 스판의 Min이 일정 값 이하면 (== 천장이 낮으면) Solid로 설정  <br/>
/// Marks walkable spans as not walkable if the clearance above the span is less than the specified height.
/// </summary>
[BurstCompile]
public struct JobFilterWalkableLowHeightSpans : IJob
{
    public Heightfield Heightfield;
    public int WalkableHeight;
    
    public void Execute()
    {
        for (var z = 0; z < Heightfield.Height; z++)
        {
            for (var x = 0; x < Heightfield.Width; x++)
            {
                for (var index = x + z * Heightfield.Width; index >= 0; index = Heightfield.Spans[index].Next)
                {
                    var current = Heightfield.Spans[index];
                    var floor = current.Top;
                    var ceiling = current.Next >= 0
                        ? Heightfield.Spans[current.Next].Bottom
                        : Heightfield.MaxHeightfieldHeight;
                    if (ceiling - floor < WalkableHeight)
                    {
                        current.IsSolid = true;
                        Heightfield.Spans[index] = current;
                    }
                }
            }
        }
    }
}

/// <summary>
/// 바운더리 침식( == 이미지 Erosion)
/// </summary>
[BurstCompile]
public struct JobErode : IJob
{
    public CompactHeightfield CompactHeightfield;
    public int WalkableRadius;
    
    public void Execute()
    {
        var width = CompactHeightfield.Width; 
        var height = CompactHeightfield.Height;
        var distanceToBoundary = new NativeArray<int>(CompactHeightfield.CompactSpans.Length, Allocator.Temp);
        
        // DistanceField 생성
        // 정수 계산을 위해 거리를 2배 스케일링하여 직선 거리를 2, 대각선 거리를 3으로 설정함.

        // Mark boundary cells.
        for (var z = 0; z < height; z++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = CompactHeightfield.Cells[x + z * width];
                if (cell.Count < 1)
                {
                    continue;
                }

                for (int spanIndex = (int)cell.Index, maxSpanIndex = (int)(cell.Index + cell.Count);
                     spanIndex < maxSpanIndex;
                     spanIndex++)
                {
                    var span = CompactHeightfield.CompactSpans[spanIndex];
                    var isBoundary = false; // equals neighborCount != 4
                    for (var d = 0; d < 4; d++)
                    {
                        var con = span.GetConnection(d);
                        if (con == CompactSpan.NotConnected)
                        {
                            isBoundary = true;
                            break;
                        }
                    }

                    distanceToBoundary[spanIndex] = isBoundary ? (byte)0 : byte.MaxValue;
                }
            }
        }


        // Pass 1
        for (var z = 0; z < height; z++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = CompactHeightfield.Cells[x + z * width];
                if (cell.Count < 1)
                {
                    continue;
                }

                for (var i = (int)cell.Index; i < cell.Index + cell.Count; i++)
                {
                    var span = CompactHeightfield.CompactSpans[i];
                    var newDistance = distanceToBoundary[i];
                    // (-1, 0)
                    var con = span.GetConnection(0);
                    if (con != CompactSpan.NotConnected)
                    {
                        var aX = x + RecastBuilder.DirectionOffsetX[0];
                        var aZ = z + RecastBuilder.DirectionOffsetZ[0];
                        var aIndex = (int)CompactHeightfield.Cells[aX + aZ * width].Index + con;
                        var aSpan = CompactHeightfield.CompactSpans[aIndex];

                        newDistance = (byte)math.min(distanceToBoundary[aIndex] + 2, newDistance);

                        // (-1, 0) + (0, -1) = (-1, -1)
                        con = aSpan.GetConnection(3);
                        if (con != CompactSpan.NotConnected)
                        {
                            var bX = aX + RecastBuilder.DirectionOffsetX[3];
                            var bZ = aZ + RecastBuilder.DirectionOffsetZ[3];
                            var bIndex = (int)CompactHeightfield.Cells[bX + bZ * width].Index + con;

                            newDistance = (byte)math.min(distanceToBoundary[bIndex] + 3, newDistance);
                        }
                    }

                    // (0, -1)
                    con = span.GetConnection(3);
                    if (con != CompactSpan.NotConnected)
                    {
                        var aX = x + RecastBuilder.DirectionOffsetX[3];
                        var aZ = z + RecastBuilder.DirectionOffsetZ[3];
                        var aIndex = (int)CompactHeightfield.Cells[aX + aZ * width].Index + con;
                        var aSpan = CompactHeightfield.CompactSpans[aIndex];

                        newDistance = (byte)math.min(distanceToBoundary[aIndex] + 2, newDistance);

                        // (0, -1) + (1, 0) = (1, -1)
                        con = aSpan.GetConnection(2);
                        if (con != CompactSpan.NotConnected)
                        {
                            var bX = aX + RecastBuilder.DirectionOffsetX[2];
                            var bZ = aZ + RecastBuilder.DirectionOffsetZ[2];
                            var bIndex = (int)CompactHeightfield.Cells[bX + bZ * width].Index + con;

                            newDistance = (byte)math.min(distanceToBoundary[bIndex] + 3, newDistance);
                        }
                    }

                    distanceToBoundary[i] = (byte)newDistance;
                }
            }
        }

        // Pass 2
        for (var z = height - 1; z >= 0; z--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var cell = CompactHeightfield.Cells[x + z * width];
                if (cell.Count < 1)
                {
                    continue;
                }

                for (var i = (int)cell.Index; i < cell.Index + cell.Count; i++)
                {
                    var span = CompactHeightfield.CompactSpans[i];
                    var newDistance = distanceToBoundary[i];
                    // (1, 0)
                    var con = span.GetConnection(2);
                    if (con != CompactSpan.NotConnected)
                    {
                        var aX = x + RecastBuilder.DirectionOffsetX[2];
                        var aZ = z + RecastBuilder.DirectionOffsetZ[2];
                        var aIndex = (int)CompactHeightfield.Cells[aX + aZ * width].Index + con;
                        var aSpan = CompactHeightfield.CompactSpans[aIndex];

                        newDistance = (byte)math.min(distanceToBoundary[aIndex] + 2, newDistance);

                        // (1, 0) + (0, 1) = (1, 1)
                        con = aSpan.GetConnection(1);
                        if (con != CompactSpan.NotConnected)
                        {
                            var bX = aX + RecastBuilder.DirectionOffsetX[1];
                            var bZ = aZ + RecastBuilder.DirectionOffsetZ[1];
                            var bIndex = (int)CompactHeightfield.Cells[bX + bZ * width].Index + con;

                            newDistance = (byte)math.min(distanceToBoundary[bIndex] + 2, newDistance);
                        }
                    }

                    // (0, 1)
                    con = span.GetConnection(1);
                    if (con != CompactSpan.NotConnected)
                    {
                        var aX = x + RecastBuilder.DirectionOffsetX[1];
                        var aZ = z + RecastBuilder.DirectionOffsetZ[1];
                        var aIndex = (int)CompactHeightfield.Cells[aX + aZ * width].Index + con;
                        var aSpan = CompactHeightfield.CompactSpans[aIndex];

                        newDistance = (byte)math.min(distanceToBoundary[aIndex] + 2, newDistance);

                        // (0, 1) + (-1, 0) = (-1, 1)
                        con = aSpan.GetConnection(0);
                        if (con != CompactSpan.NotConnected)
                        {
                            var bX = aX + RecastBuilder.DirectionOffsetX[0];
                            var bZ = aZ + RecastBuilder.DirectionOffsetZ[0];
                            var bIndex = (int)CompactHeightfield.Cells[bX + bZ * width].Index + con;

                            newDistance = (byte)math.min(distanceToBoundary[bIndex] + 2, newDistance);
                        }
                    }

                    distanceToBoundary[i] = newDistance;
                }
            }
        }

        // DistanceField가 반지름보다 작을 경우 0 (NonWalkable)로 설정
        // 거리를 2배 스케일링 했기 때문에 반지름도 2배로 설정
        var minBoundaryDistance = WalkableRadius * 2;
        for (var i = 0; i < distanceToBoundary.Length; i++)
        {
            CompactHeightfield.IsWalkables[i] = distanceToBoundary[i] >= minBoundaryDistance;
        }

        distanceToBoundary.Dispose();
    }
}
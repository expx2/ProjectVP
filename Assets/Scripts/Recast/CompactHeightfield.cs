using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 열린 공간 == 이동할 수 있는 공간.
/// </summary>
public struct CompactSpan
{
    public const uint NotConnected = 63U;

    /// <summary>
    /// The lower extent of the span. (Measured from the heightfield's base.) <br/>
    /// 'y' in recast
    /// </summary>
    public int Bottom;

    /// <summary>
    /// The height of the span.  (Measured from #Bottom.) <br/>
    /// 'h' in recast
    /// </summary>
    public int Height;

    public uint Connections; // 연결된 compact span

    public CompactSpan(int bottom, int height)
    {
        Bottom = bottom;
        Height = height;
        Connections = 0xFFFFFFFF;
    }

    public void SetConnection(int dir, int value)
    {
        var shift = dir * 6;

        Connections = (uint)((Connections & ~(0x3f << shift)) // 기존 값 지우기 
                             | (((uint)value & 0x3f) << shift)); // 새 값 넣기
    }

    /// <summary>
    /// dir 방향에 연결된 CompactSpan의 해당 셀(x, z)에서의 인덱스
    /// </summary>
    /// <param name="dir">0=서, 1=북, 2=동, 3=남<br/>
    /// <see cref="RecastBuilder.DirectionOffsetX"/></param>
    /// <returns></returns>
    public int GetConnection(int dir)
    {
        return ((int)Connections >> dir * 6) & 0x3f;
    }
}

public struct CompactCell
{
    public int Index; // 첫 CompactSpan의 index
    public int Count; // CompactSpan 개수
}

public struct CompactHeightfield : IDisposable
{
    public int Width;
    public int Height;
    public NativeArray<CompactCell> Cells;
    public NativeArray<CompactSpan> CompactSpans;
    public NativeArray<bool> IsWalkables;
    //// Areas는 Erode에서 설정

    public CompactHeightfield(Heightfield heightfield)
    {
        Width =  heightfield.Width;
        Height = heightfield.Height;
        Cells = new NativeArray<CompactCell>(Width * Height, Allocator.Persistent);
        var spanCount = heightfield.GetSpanCount();
        CompactSpans = new NativeArray<CompactSpan>(spanCount, Allocator.Persistent);
        IsWalkables = new NativeArray<bool>(spanCount, Allocator.Persistent);
    }

    public void Dispose()
    {
        Cells.Dispose();
        CompactSpans.Dispose();
        IsWalkables.Dispose();
    }
}

/// <summary>
/// Heightfield를 이용하여 CompactHeightfield 생성 <br/>
/// <see href="https://github.com/recastnavigation/recastnavigation/blob/40ec6fcd6c0263a3d7798452aee531066072d15d/Recast/Source/Recast.cpp#L403"/>
/// </summary>
[BurstCompile]
public struct JobBuildCompactHeightfield : IJob
{
    public Heightfield heightfield;
    public CompactHeightfield compactHeightfield;
    public int walkableHeight;
    public int upWalkable;
    public int downWalkable;
    
    public void Execute()
    {
        var length = compactHeightfield.Width * compactHeightfield.Height;
        // Fill in cells and spans
        var spanCount = 0;
        for (var columnIndex = 0; columnIndex < length; columnIndex++)
        {
            if (heightfield.Spans[columnIndex].IsDefault)
            {
                // 빈 칼럼
                continue;
            }

            var cell = compactHeightfield.Cells[columnIndex];
            cell.Index = spanCount;
            cell.Count = 0;
                
            for (var i = columnIndex; i >= 0; i = heightfield.Spans[i].Next)
            {
                var span = heightfield.Spans[i];
                if (span.IsSolid)
                {
                    continue;
                }

                var bot = span.Top;
                var top = span.Next >= 0 
                    ? heightfield.Spans[span.Next].Bottom 
                    : Heightfield.MaxHeightfieldHeight;
                compactHeightfield.CompactSpans[spanCount] = new CompactSpan(
                    math.clamp(bot, 0, ushort.MaxValue),
                    math.clamp(top - bot, 0, ushort.MaxValue));
                compactHeightfield.IsWalkables[spanCount] = true; 
                spanCount++;
                cell.Count++;
            }
                
            compactHeightfield.Cells[columnIndex] = cell;
        }

        // Find neighbours
        // 한 셀에서 CompactSpan의 최대 수
        const uint maxLayers = ushort.MaxValue - 1;
        var maxLayerIndex = 0;
        for (var z = 0; z < compactHeightfield.Height; z++)
        {
            for (var x = 0; x < compactHeightfield.Width; x++)
            {
                var cell = compactHeightfield.Cells[x + z * compactHeightfield.Width];
                for (int i = cell.Index, ni = cell.Index + cell.Count; i < ni; ++i)
                {
                    var span = compactHeightfield.CompactSpans[i];

                    for (var dir = 0; dir < 4; dir++)
                    {
                        var neighborX = x + RecastBuilder.DirectionOffsetX[dir];
                        var neighborZ = z + RecastBuilder.DirectionOffsetZ[dir];
                        // First check that the neighbour cell is in bounds.
                        if (neighborX < 0 || neighborZ < 0 || neighborX >= compactHeightfield.Width || neighborZ >= compactHeightfield.Height)
                        {
                            continue;
                        }

                        // Iterate over all neighbour spans and check if any of the is
                        // accessible from current cell.
                        // 주변 셀의 모든 스판 검사
                        var neighborCell = compactHeightfield.Cells[neighborX + neighborZ * compactHeightfield.Width];
                        for (int k = neighborCell.Index, nk = neighborCell.Index + neighborCell.Count;
                             k < nk;
                             k++)
                        {
                            var neighborSpan = compactHeightfield.CompactSpans[k];
                            var bot = math.max(span.Bottom, neighborSpan.Bottom);
                            var top = math.min(span.Bottom + span.Height,
                                neighborSpan.Bottom + neighborSpan.Height);
                            var walkableClimb = span.Bottom > neighborSpan.Bottom ? downWalkable : upWalkable;
                            // Check that the gap between the spans is walkable,
                            // and that the climb height between the gaps is not too high.
                            if ((top - bot) >= walkableHeight &&
                                math.abs(neighborSpan.Bottom - span.Bottom) <= walkableClimb)
                            {
                                // Mark direction as walkable.
                                var layerIndex = k - neighborCell.Index;
                                if (layerIndex < 0 || layerIndex > maxLayers)
                                {
                                    maxLayerIndex = math.max(maxLayerIndex, layerIndex);
                                    continue;
                                }

                                span.SetConnection(dir, layerIndex);
                                compactHeightfield.CompactSpans[i] = span;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
using System;
using UnityEngine;

[System.Serializable]
public struct FloodFillData
{
    public int X;
    public int Z;
    public int SpanIndex;
    // Burst로 쓰기 위해 배열 쓰지 않음
    // NativeArray 쓰기엔 FloodFill 결과에 포함되지 않는 스판도 생성되기 때문에 Dispose 시점이 애매함
    public int Corner0; 
    public int Corner1; 
    public int Corner2; 
    public int Corner3; 
    /// <summary>
    /// 시작 스판으로부터의 거리
    /// </summary>
    public ushort Distance;
    

    public int GetCornerHeight(int index)
    {
        return index switch
        {
            0 => Corner0,
            1 => Corner1,
            2 => Corner2,
            3 => Corner3,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, null)
        };
    }
}

public static class FloodFiller
{
    public static FloodFillData[] GetFloodFillData(Vector3 position, int volume, RecastGraph graph)
    {
        if (!graph.TryGetFloodFillData(position, out var startFillData))
        {
            return null;
        }

        var fillDatas = new FloodFillData[volume];
        var width = graph.Width;

        // distanceFields를 span단위가 아닌 cell단위로 해서 한 위치에 여러 층으로 fill하지 않도록 함
        var distanceFields = new ushort[graph.Cells.Length];
        var queue = new PriorityQueue<FloodFillData>();
        queue.Enqueue(startFillData, 1);
        distanceFields[startFillData.X + startFillData.Z * width] = 1;
        var count = 0;
        var queueCount = 1;
        while (queueCount > 0)
        {
            var current = queue.Dequeue();
            queueCount--;
            fillDatas[count++] = current;
            if (count >= volume)
            {
                break;
            }
            var x = current.X;
            var z = current.Z;

            var currentSpan = graph.CompactSpans[current.SpanIndex];

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

            // (0, -1)
            con = currentSpan.GetConnection(3);
            if (con != CompactSpan.NotConnected)
            {
                var aX = x + RecastBuilder.DirectionOffsetX[3];
                var aZ = z + RecastBuilder.DirectionOffsetZ[3];
                var aIndex = (int)graph.Cells[aX + aZ * width].Index + con;
                if (Enqueue(aX, aZ, aIndex, (ushort)(current.Distance + 2)))
                {
                    // (0, -1) + (1, 0) = (1, -1)
                    con = graph.CompactSpans[aIndex].GetConnection(2);
                    if (con != CompactSpan.NotConnected)
                    {
                        var bX = aX + RecastBuilder.DirectionOffsetX[2];
                        var bZ = aZ + RecastBuilder.DirectionOffsetZ[2];
                        var bIndex = (int)graph.Cells[bX + bZ * width].Index + con;
                        Enqueue(bX, bZ, bIndex, (ushort)(current.Distance + 3));
                    }
                }
            }

            // (1, 0)
            con = currentSpan.GetConnection(2);
            if (con != CompactSpan.NotConnected)
            {
                var aX = x + RecastBuilder.DirectionOffsetX[2];
                var aZ = z + RecastBuilder.DirectionOffsetZ[2];
                var aIndex = (int)graph.Cells[aX + aZ * width].Index + con;
                if (Enqueue(aX, aZ, aIndex, (ushort)(current.Distance + 2)))
                {
                    // (1, 0) + (0, 1) = (1, 1)
                    con = graph.CompactSpans[aIndex].GetConnection(1);
                    if (con != CompactSpan.NotConnected)
                    {
                        var bX = aX + RecastBuilder.DirectionOffsetX[1];
                        var bZ = aZ + RecastBuilder.DirectionOffsetZ[1];
                        var bIndex = (int)graph.Cells[bX + bZ * width].Index + con;
                        Enqueue(bX, bZ, bIndex, (ushort)(current.Distance + 3));
                    }
                }
            }

            // (0, 1)
            con = currentSpan.GetConnection(1);
            if (con != CompactSpan.NotConnected)
            {
                var aX = x + RecastBuilder.DirectionOffsetX[1];
                var aZ = z + RecastBuilder.DirectionOffsetZ[1];
                var aIndex = (int)graph.Cells[aX + aZ * width].Index + con;
                if (Enqueue(aX, aZ, aIndex, (ushort)(current.Distance + 2)))
                {
                    // (0, 1) + (-1, 0) = (-1, 1)
                    con = graph.CompactSpans[aIndex].GetConnection(0);
                    if (con != CompactSpan.NotConnected)
                    {
                        var bX = aX + RecastBuilder.DirectionOffsetX[0];
                        var bZ = aZ + RecastBuilder.DirectionOffsetZ[0];
                        var bIndex = (int)graph.Cells[bX + bZ * width].Index + con;
                        Enqueue(bX, bZ, bIndex, (ushort)(current.Distance + 3));
                    }
                }
            }
        }

        return fillDatas;

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
    }
}

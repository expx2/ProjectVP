using System;
using Unity.Mathematics;
using UnityEngine;

public struct RecastGraph : IEquatable<RecastGraph>
{
    public int Width; // x
    public int Height; // x
    public int Depth; // z

    public float VoxelSize;
    public float VoxelHeight;
    
    public CompactCell[] Cells;
    public CompactSpan[] CompactSpans;
    public bool[] IsWalkables;
    
    public Vector3 ZeroPosition
    {
        get
        {
            var halfSize = VoxelSize * 0.5f;
            var halfHeight = VoxelHeight * 0.5f;
            return -new Vector3(Width * halfSize, Height * halfHeight, Depth * halfSize);
        }
    }
    
    /// <summary>
    /// (x, z)셀 index스판의 사각형 코너의 높이값 리턴
    /// </summary>
    /// <param name="dir">0=서, 1=북, 2=동, 3=남</param>
    public int GetCornerHeight(int x, int z, int index, int dir)
    {
        var span = CompactSpans[index];
        var spanHeight = span.Bottom;
        var dirp = (dir + 1) & 0x3;

        if (span.GetConnection(dir) != CompactSpan.NotConnected)
        {
            var aX = x + RecastBuilder.DirectionOffsetX[dir];
            var aZ = z + RecastBuilder.DirectionOffsetZ[dir];
            var aIndex = (int)Cells[aX + aZ * Width].Index + span.GetConnection(dir);
            var aSpan = CompactSpans[aIndex];
            if (IsWalkables[aIndex])
            {
                spanHeight = math.max(spanHeight, aSpan.Bottom);
            }

            if (aSpan.GetConnection(dirp) != CompactSpan.NotConnected)
            {
                var bX = aX + RecastBuilder.DirectionOffsetX[dirp];
                var bY = aZ + RecastBuilder.DirectionOffsetZ[dirp];
                var bIndex = (int)Cells[bX + bY * Width].Index + aSpan.GetConnection(dirp);
                var bSpan = CompactSpans[bIndex];
                if (IsWalkables[bIndex])
                {
                    spanHeight = math.max(spanHeight, bSpan.Bottom);
                }
            }
        }
        
        if (span.GetConnection(dirp) != CompactSpan.NotConnected)
        {
            var aX = x + RecastBuilder.DirectionOffsetX[dirp];
            var aZ = z + RecastBuilder.DirectionOffsetZ[dirp];
            var aIndex = (int)Cells[aX + aZ * Width].Index + span.GetConnection(dirp);
            var aSpan = CompactSpans[aIndex];
            if (IsWalkables[aIndex])
            {
                spanHeight = math.max(spanHeight, aSpan.Bottom);
            }
            
            if (aSpan.GetConnection(dir) != CompactSpan.NotConnected)
            {
                var bX = aX + RecastBuilder.DirectionOffsetX[dir];
                var bY = aZ + RecastBuilder.DirectionOffsetZ[dir];
                var bIndex = (int)Cells[bX + bY * Width].Index + aSpan.GetConnection(dir);
                var bSpan = CompactSpans[bIndex];
                if (IsWalkables[bIndex])
                {
                    spanHeight = math.max(spanHeight, bSpan.Bottom);
                }
            }
        }

        return spanHeight;
    }
    
    public bool TryGetFloodFillData(Vector3 position, out FloodFillData floodFillData)
    {
        floodFillData = default;
        
        var localPosition = position - ZeroPosition;
        var coord = new Vector3Int((int)math.floor(localPosition.x / VoxelSize),
            (int)math.floor(localPosition.y / VoxelHeight),
            (int)math.floor(localPosition.z / VoxelSize));

        if (coord.x < 0 || coord.y < 0 || coord.z < 0 ||
            coord.x >= Width || coord.y >= Height || coord.z >= Depth)
        {
            // out of bounds
            return false;
        }
        
        var cell = Cells[coord.x + coord.z * Width];
        if (cell.Count < 1)
        {
            // has no compact span
            Debug.Log($"({coord.x}, {coord.z}) has no compact span");
            return false;
        }

        for (var i = cell.Index + cell.Count - 1; i >= cell.Index; i--)
        {
            if (!IsWalkables[(int)i])
            {
                // Eroded
                continue;
            }
            
            if (math.abs(CompactSpans[(int)i].Bottom - coord.y) > 2)
            {
                continue;
            }
            
            // set span
            floodFillData = new FloodFillData()
            {
                X = coord.x,
                Z = coord.z,
                Corner0 = GetCornerHeight(coord.x, coord.z, (int)i, 0),
                Corner1 = GetCornerHeight(coord.x, coord.z, (int)i, 1),
                Corner2 = GetCornerHeight(coord.x, coord.z, (int)i, 2),
                Corner3 = GetCornerHeight(coord.x, coord.z, (int)i, 3),
                SpanIndex = (int)i,
                Distance = 1
            };
            return true;
        }

        return false;
    }
    
    public void DrawDebugLines(bool includeErosion, float4x4 matrix, Color? color = null, float duration = 0f, bool depthTest = true)
    {
        color ??= Color.white;

        for (var z = 0; z < Depth; z++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = Cells[x + z * Width];
                var position = new Vector3(x + 0.5f, 0f, z + 0.5f);
                for (var i = 0; i < cell.Count; i++)
                {
                    if (!includeErosion && !IsWalkables[(int)cell.Index + i])
                    {
                        continue;
                    }
                    var span = CompactSpans[(int)cell.Index + i];
                    position.y = span.Bottom + 0.5f;
                    DebugUtility.DrawCube(matrix, position,
                        new Vector3(1, 1, 1),
                        color,
                        duration, depthTest);
                }
            }
        }
    }
    
    public bool Equals(RecastGraph other)
    {
        return Width == other.Width && Height == other.Height && Depth == other.Depth;
    }

    public override bool Equals(object obj)
    {
        return obj is RecastGraph other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height, Depth);
    }
}

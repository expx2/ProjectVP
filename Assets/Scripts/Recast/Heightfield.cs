using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct Span
{
    public int Bottom; // min
    public int Top;    // max
    public bool IsSolid;
    /// <summary>
    /// 다음 (위) Span의 index.
    /// 없을 경우 -1
    /// </summary>
    public int Next; // index of next span
    
    public bool IsDefault => Top <= Bottom; // Top은 항상 Bottom보다 큼

    public Span(int bottom, int top, bool isSolid)
    {
        Bottom = bottom;
        Top = top;
        IsSolid = isSolid;
        Next = -1;
    }
    
    public Span(int bottom, int top, bool isSolid, int next)
    {
        Bottom = bottom;
        Top = top;
        IsSolid = isSolid;
        Next = next;
    }
}

public struct Heightfield : IDisposable
{
    /// <summary>
    /// Solid한 span끼리 Bottom과 Top의 차이가 이 값 이하일 경우 머지
    /// </summary>
    public const int SolidMergeThreshold = 1;
    public const int MaxHeightfieldHeight = ushort.MaxValue; // == 0xffff

    public int Width;
    public int Height;
    public NativeList<int> IndexPool;
    public NativeList<Span> Spans;

    public Heightfield(int width, int height)
    {
        Width = width;
        Height = height;
        IndexPool = new NativeList<int>(128, Allocator.Persistent);
        var length = Width * Height;
        Spans = new NativeList<Span>(length, Allocator.Persistent);
        for (var i = 0; i < length; i++)
        {
            Spans.Add(new Span(0, 0, true));
        }
    }

    /// <summary>
    /// 좌표에 새 스판을 추가하며, 기존에 있는 스판과 겹칠 경우 병합
    /// <para>
    /// 참고 <see href="https://github.com/recastnavigation/recastnavigation/blob/main/Recast/Source/RecastRasterization.cpp#L191"/>
    /// </para>
    /// </summary>
    public void AddSpan(int x, int z, int bottom, int top, bool isSolid)
    {
        var baseIndex = x + z * Width;
        
        if (Spans[baseIndex].IsDefault)
        {
            // 해당 위치에 스판(루트 스판) 없음
            Spans[baseIndex] = new Span(bottom, top, isSolid);
            return;
        }

        var prevIndex = -1;
        var currentIndex = baseIndex;
        // Insert the new span, possibly merging it with existing spans.
        while (currentIndex >= 0)
        {
            var currentSpan = Spans[currentIndex];
            if (currentSpan.Bottom > top)
            {
                // Current span is completely after the new span, break.
                break;
            }

            if (currentSpan.Top < bottom)
            {
                // Current span is completely before the new span. Keep going.
                prevIndex = currentIndex;
                currentIndex = currentSpan.Next;
                continue;
            }
            
            // The new span overlaps with an existing span.  Merge them.
            bottom = math.min(bottom, currentSpan.Bottom);
            top = math.max(top, currentSpan.Top);

            // Merge flags.
            if (math.abs(top - currentSpan.Top) <= SolidMergeThreshold)
            {
                isSolid &= currentSpan.IsSolid;
            }
            
            // Remove the current span since it's now merged with newSpan.
            // Keep going because there might be other overlapping spans that also need to be merged.
            var nextIndex = currentSpan.Next;
            if (prevIndex >= 0)
            {
                // 현재 스판을 임시로 제거하기 때문에 이전 스판의 next를 현재 스판의 next로 설정
                var prev = Spans[prevIndex];
                prev.Next = nextIndex;
                Spans[prevIndex] = prev;
                IndexPool.Add(currentIndex);
                currentIndex = nextIndex;
                continue;
            }
            
            // 현재 스판이 루트 스판이었을 경우 (== prev가 없음)
            if (nextIndex >= 0)
            {
                // 현재 스판을 임시로 제거하기 때문에 다음 스판을 루트 스판으로 설정
                Spans[baseIndex] = Spans[nextIndex];
                IndexPool.Add(nextIndex);
                currentIndex = baseIndex;
                continue;
            }
            
            // 현재 스판이 루트 스판이고 next도 없을 경우 바로 머지
            Spans[currentIndex] = new Span(bottom, top, isSolid);
            return;
        }
        
        // Insert new span after prev
        int newIndex;
        var isNewIndex = false;
        if (IndexPool.Length > 0)
        {
            // pop
            newIndex = IndexPool[^1];
            IndexPool.RemoveAt(IndexPool.Length - 1);
        }
        else
        {
            newIndex = Spans.Length;
            isNewIndex = true;
        }
        
        if (prevIndex >= 0)
        {
            var prev = Spans[prevIndex];
            // 이전 스판의 Next를 새 스판으로 설정
            if (isNewIndex)
            {
                Spans.Add(new Span(bottom, top, isSolid, prev.Next));
            }
            else
            {
                Spans[newIndex] = new Span(bottom, top, isSolid, prev.Next);
            }
            prev.Next = newIndex;
            Spans[prevIndex] = prev;
            return;
        }
        
        // This span should go before the others in the list
        // 루트 스판으로 설정
        if (isNewIndex)
        {
            Spans.Add(Spans[baseIndex]);
        }
        else
        {
            Spans[newIndex] = Spans[baseIndex];
        }

        Spans[baseIndex] = new Span(bottom, top, isSolid, newIndex);
    }
    
    /// <summary>
    /// Solid가 아닌 스판들의 수
    /// <para>
    /// 참고 <see href="https://github.com/recastnavigation/recastnavigation/blob/40ec6fcd6c0263a3d7798452aee531066072d15d/Recast/Source/Recast.cpp#L384"/>
    /// </para>
    /// </summary>
    public int GetSpanCount()
    {
        var length = Width * Height;
        var count = 0;
        for (var i = 0; i < length; i++)
        {
            for (var idx = i; idx >= 0; idx = Spans[idx].Next)
            {
                if (!Spans[idx].IsSolid)
                {
                    count++;
                }
            }
        }

        return count;
    }
    
    public void Dispose()
    {
        IndexPool.Dispose();
        Spans.Dispose();
    }

    public void DrawDebugLines(float4x4 matrix, Color? solidColor = null, Color? nonSolidColor = null, float duration = 0f, bool depthTest = true)
    {
        solidColor ??= Color.red;
        nonSolidColor ??= Color.green;
        
        for (var z = 0; z < Height; z++)
        {
            for (var x = 0; x < Width; x++)
            {
                var position = new Vector3(x + 0.5f, 0f, z + 0.5f);
                for (var index = x + z * Width; index >= 0; index = Spans[index].Next)
                {
                    var span = Spans[index];
                    if (span.IsDefault)
                    {
                        continue;
                    }
                        
                    var height = span.Top - span.Bottom;
                    position.y = span.Bottom + height * 0.5f;
                    DebugUtility.DrawCube(matrix, position, 
                        new Vector3(1, height, 1),
                        span.IsSolid ? solidColor.Value : nonSolidColor.Value, 
                        duration, depthTest);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;

public class PriorityQueue<T>
{
    private List<(T item, int priority)> _elements = new();

    public int Count => _elements.Count;

    public void Enqueue(T item, int priority)
    {
        _elements.Add((item, priority));
        HeapifyUp(_elements.Count - 1);
    }

    public T Dequeue()
    {
        if (_elements.Count == 0)
        {
            throw new InvalidOperationException("PriorityQueue is empty.");
        }

        var result = _elements[0].item;

        // 마지막 원소를 루트로 옮기고 힙 정렬
        _elements[0] = _elements[^1];
        _elements.RemoveAt(_elements.Count - 1);
        HeapifyDown(0);

        return result;
    }

    public T Peek()
    {
        if (_elements.Count == 0)
        {
            throw new InvalidOperationException("PriorityQueue is empty.");
        }

        return _elements[0].item;
    }

    private void HeapifyUp(int index)
    {
        while (index > 0)
        {
            var parentIndex = (index - 1) / 2;
            if (_elements[index].priority >= _elements[parentIndex].priority)
                break;

            Swap(index, parentIndex);
            index = parentIndex;
        }
    }

    private void HeapifyDown(int index)
    {
        var lastIndex = _elements.Count - 1;

        while (true)
        {
            var leftChild = index * 2 + 1;
            var rightChild = index * 2 + 2;
            var smallest = index;

            if (leftChild <= lastIndex && 
                _elements[leftChild].priority < _elements[smallest].priority)
            {
                smallest = leftChild;
            }

            if (rightChild <= lastIndex &&
                _elements[rightChild].priority < _elements[smallest].priority)
            {
                smallest = rightChild;
            }

            if (smallest == index)
            {
                break;
            }

            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int i, int j)
    {
        (_elements[i], _elements[j]) = (_elements[j], _elements[i]);
    }
}
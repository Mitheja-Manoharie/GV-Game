using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Flanker.Goap
{
    /// <summary>
    /// Binary min-heap for the planner's open list. Unity's .NET Standard 2.1 profile has no PriorityQueue,
    /// and A* needs "take the cheapest node" in O(log n) rather than a linear scan.
    /// </summary>
    internal sealed class MinHeap<T>
    {
        private readonly List<T> items = new List<T>();
        private readonly Comparison<T> compare;

        public MinHeap(Comparison<T> compare) => this.compare = compare ?? throw new ArgumentNullException(nameof(compare));

        public int Count => items.Count;

        public void Push(T item)
        {
            items.Add(item);
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (compare(items[i], items[parent]) >= 0) break;
                Swap(i, parent);
                i = parent;
            }
        }

        public T Pop()
        {
            if (items.Count == 0) throw new InvalidOperationException("The heap is empty.");

            T top = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = 2 * i + 1;
                if (left >= items.Count) break;
                int right = left + 1;
                int smallest = right < items.Count && compare(items[right], items[left]) < 0 ? right : left;
                if (compare(items[smallest], items[i]) >= 0) break;
                Swap(i, smallest);
                i = smallest;
            }

            return top;
        }

        private void Swap(int a, int b)
        {
            T temp = items[a];
            items[a] = items[b];
            items[b] = temp;
        }
    }
}

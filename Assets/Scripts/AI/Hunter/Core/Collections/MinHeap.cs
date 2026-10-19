using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Hunter.Collections
{
    /// <summary>
    /// Binary min-heap keyed by a float priority. Used as the open list of A* so that taking the
    /// cheapest node costs O(log n) instead of scanning every open node.
    /// Items with equal priority leave in insertion order (FIFO), which keeps searches deterministic.
    /// Pure C#: no UnityEngine dependency.
    /// </summary>
    public sealed class MinHeap<T>
    {
        private struct Entry
        {
            public T Item;
            public float Priority;
            public long Order;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private long insertions;

        public int Count => entries.Count;

        public void Push(T item, float priority)
        {
            if (float.IsNaN(priority))
                throw new ArgumentException("Priority must not be NaN.", nameof(priority));

            entries.Add(new Entry { Item = item, Priority = priority, Order = insertions++ });
            SiftUp(entries.Count - 1);
        }

        /// <summary>Removes and returns the item with the lowest priority.</summary>
        public T Pop()
        {
            if (!TryPop(out T item))
                throw new InvalidOperationException("The heap is empty.");
            return item;
        }

        public bool TryPop(out T item)
        {
            if (entries.Count == 0)
            {
                item = default(T);
                return false;
            }

            item = entries[0].Item;
            int last = entries.Count - 1;
            entries[0] = entries[last];
            entries.RemoveAt(last);
            if (entries.Count > 0) SiftDown(0);
            return true;
        }

        public T Peek()
        {
            if (entries.Count == 0)
                throw new InvalidOperationException("The heap is empty.");
            return entries[0].Item;
        }

        public void Clear()
        {
            entries.Clear();
            insertions = 0;
        }

        private static bool Less(Entry a, Entry b)
        {
            if (a.Priority != b.Priority) return a.Priority < b.Priority;
            return a.Order < b.Order;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(entries[index], entries[parent])) break;
                Swap(index, parent);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            int count = entries.Count;
            while (true)
            {
                int left = 2 * index + 1;
                int right = left + 1;
                int smallest = index;

                if (left < count && Less(entries[left], entries[smallest])) smallest = left;
                if (right < count && Less(entries[right], entries[smallest])) smallest = right;
                if (smallest == index) return;

                Swap(index, smallest);
                index = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            Entry temp = entries[a];
            entries[a] = entries[b];
            entries[b] = temp;
        }
    }
}

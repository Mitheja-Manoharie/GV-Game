using System;
using System.Collections.Generic;
using LabLockdown.AI.Hunter.Collections;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class MinHeapTests
    {
        [Test]
        public void Pop_ReturnsItemsInPriorityOrder()
        {
            var heap = new MinHeap<string>();
            heap.Push("c", 3f);
            heap.Push("a", 1f);
            heap.Push("d", 4f);
            heap.Push("b", 2f);

            Assert.AreEqual("a", heap.Pop());
            Assert.AreEqual("b", heap.Pop());
            Assert.AreEqual("c", heap.Pop());
            Assert.AreEqual("d", heap.Pop());
            Assert.AreEqual(0, heap.Count);
        }

        [Test]
        public void EqualPriorities_LeaveInInsertionOrder()
        {
            var heap = new MinHeap<int>();
            for (int i = 0; i < 20; i++) heap.Push(i, 5f);

            for (int i = 0; i < 20; i++) Assert.AreEqual(i, heap.Pop());
        }

        [Test]
        public void Pop_OnEmptyHeap_Throws()
        {
            var heap = new MinHeap<int>();
            Assert.Throws<InvalidOperationException>(() => heap.Pop());
            Assert.Throws<InvalidOperationException>(() => heap.Peek());
        }

        [Test]
        public void TryPop_OnEmptyHeap_ReturnsFalse()
        {
            var heap = new MinHeap<int>();
            Assert.IsFalse(heap.TryPop(out int item));
            Assert.AreEqual(0, item);
        }

        [Test]
        public void Push_RejectsNaN()
        {
            var heap = new MinHeap<int>();
            Assert.Throws<ArgumentException>(() => heap.Push(1, float.NaN));
        }

        [Test]
        public void InfinitePriority_SortsLast()
        {
            var heap = new MinHeap<string>();
            heap.Push("never", float.PositiveInfinity);
            heap.Push("soon", 1f);

            Assert.AreEqual("soon", heap.Pop());
            Assert.AreEqual("never", heap.Pop());
        }

        [Test]
        public void Peek_DoesNotRemove()
        {
            var heap = new MinHeap<int>();
            heap.Push(7, 2f);
            heap.Push(9, 1f);

            Assert.AreEqual(9, heap.Peek());
            Assert.AreEqual(2, heap.Count);
        }

        [Test]
        public void Clear_EmptiesTheHeap()
        {
            var heap = new MinHeap<int>();
            heap.Push(1, 1f);
            heap.Clear();

            Assert.AreEqual(0, heap.Count);
            Assert.IsFalse(heap.TryPop(out int _));
        }

        [Test]
        public void RandomisedPopOrder_MatchesSortAndIsStable()
        {
            var random = new Random(12345);
            var heap = new MinHeap<int>();
            var priorityOf = new float[500];

            for (int i = 0; i < 500; i++)
            {
                priorityOf[i] = random.Next(0, 100);   // many duplicates on purpose
                heap.Push(i, priorityOf[i]);
            }

            var sorted = new List<float>(priorityOf);
            sorted.Sort();

            int previousItem = -1;
            for (int i = 0; i < 500; i++)
            {
                int item = heap.Pop();
                Assert.AreEqual(sorted[i], priorityOf[item]);

                // Equal priorities must come out in insertion order.
                if (i > 0 && sorted[i] == sorted[i - 1])
                    Assert.Greater(item, previousItem);
                previousItem = item;
            }
            Assert.AreEqual(0, heap.Count);
        }
    }
}

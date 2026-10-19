using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using LabLockdown.AI.Hunter.Collections;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// Cheapest walking cost from one node to every other node under the live cost model
    /// (single-source Dijkstra). The Hunter's target choice needs "how far is each room" for all
    /// candidate rooms at once; running A* once per room would repeat the same work.
    /// Unreachable nodes get infinity.
    /// </summary>
    public static class CostField
    {
        public static float[] From(LevelGraph graph, EdgeCostModel costs, int start, IDoorStateProvider doors)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (doors == null) throw new ArgumentNullException(nameof(doors));
            if (start < 0 || start >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(start));

            var best = new float[graph.NodeCount];
            var done = new bool[graph.NodeCount];
            for (int i = 0; i < best.Length; i++) best[i] = float.PositiveInfinity;

            var open = new MinHeap<int>();
            best[start] = 0f;
            open.Push(start, 0f);

            while (open.TryPop(out int current))
            {
                if (done[current]) continue;
                done[current] = true;

                foreach (GraphEdge edge in graph.EdgesOf(current))
                {
                    int next = edge.Other(current);
                    if (done[next]) continue;

                    float step = costs.Cost(edge, doors);
                    if (float.IsPositiveInfinity(step)) continue;

                    float candidate = best[current] + step;
                    if (candidate < best[next])
                    {
                        best[next] = candidate;
                        open.Push(next, candidate);
                    }
                }
            }
            return best;
        }
    }
}

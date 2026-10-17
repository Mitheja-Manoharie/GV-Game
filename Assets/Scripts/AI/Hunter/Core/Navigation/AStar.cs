using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using LabLockdown.AI.Hunter.Collections;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// A* over the level graph with live edge costs (closed doors cost extra, locked or blocked doors
    /// cannot be used). Costs are read every time a path is planned, so the same instance always
    /// reflects the current door states.
    ///
    ///   f(n) = g(n) + h(n)      g = cost so far, h = straight-line distance to the goal
    ///
    /// h never overestimates (see EdgeCostModel.IsHeuristicAdmissible) and is consistent, so the first time
    /// a node is taken from the open list its cost is already the cheapest, and nodes are never re-opened.
    ///
    /// Scratch arrays are reused between calls to avoid allocating every replan. An instance is therefore
    /// not thread safe, and each Hunter should own its own.
    /// </summary>
    public sealed class AStar
    {
        private readonly LevelGraph graph;
        private readonly EdgeCostModel costs;
        private readonly MinHeap<int> open = new MinHeap<int>();
        private readonly float[] g;
        private readonly int[] parent;
        private readonly bool[] closed;

        public AStar(LevelGraph graph, EdgeCostModel costs)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            this.costs = costs ?? throw new ArgumentNullException(nameof(costs));
            g = new float[graph.NodeCount];
            parent = new int[graph.NodeCount];
            closed = new bool[graph.NodeCount];
        }

        /// <summary>
        /// When false the heuristic is zero and the search becomes Dijkstra. Only used to measure how many
        /// nodes A* saves; gameplay always leaves this true.
        /// </summary>
        public bool UseHeuristic { get; set; } = true;

        public PathResult FindPath(int start, int goal, IDoorStateProvider doors)
        {
            Validate(start);
            Validate(goal);
            if (doors == null) throw new ArgumentNullException(nameof(doors));

            for (int i = 0; i < g.Length; i++)
            {
                g[i] = float.PositiveInfinity;
                parent[i] = -1;
                closed[i] = false;
            }
            open.Clear();

            g[start] = 0f;
            open.Push(start, Heuristic(start, goal));
            int expanded = 0;

            while (open.TryPop(out int current))
            {
                // The same node can be queued more than once; only its cheapest entry is useful.
                if (closed[current]) continue;
                closed[current] = true;
                expanded++;

                if (current == goal) return Rebuild(start, goal, expanded);

                foreach (GraphEdge edge in graph.EdgesOf(current))
                {
                    int next = edge.Other(current);
                    if (closed[next]) continue;

                    float step = costs.Cost(edge, doors);
                    if (float.IsPositiveInfinity(step)) continue;

                    float candidate = g[current] + step;
                    if (candidate < g[next])
                    {
                        g[next] = candidate;
                        parent[next] = current;
                        open.Push(next, candidate + Heuristic(next, goal));
                    }
                }
            }

            return PathResult.Failure(expanded);
        }

        private float Heuristic(int node, int goal)
        {
            return UseHeuristic ? EdgeCostModel.Euclidean(graph.Nodes[node], graph.Nodes[goal]) : 0f;
        }

        private PathResult Rebuild(int start, int goal, int expanded)
        {
            var nodes = new List<int>();
            for (int n = goal; n != -1; n = parent[n]) nodes.Add(n);
            nodes.Reverse();
            return PathResult.Success(nodes, g[goal], expanded);
        }

        private void Validate(int node)
        {
            if (node < 0 || node >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(node), "Node " + node + " is not in the graph.");
        }
    }
}

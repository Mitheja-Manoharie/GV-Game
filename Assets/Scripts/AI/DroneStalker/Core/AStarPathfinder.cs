using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>Result of a path search.</summary>
    public sealed class StalkerPath
    {
        /// <summary>True if a route to the goal was found.</summary>
        public bool Found;

        /// <summary>Node ids from start to goal (inclusive). Empty when no route was found.</summary>
        public int[] Nodes = Array.Empty<int>();

        /// <summary>Total cost of the route under the visibility cost model.</summary>
        public float Cost = float.PositiveInfinity;

        /// <summary>How many nodes the search expanded (useful to show the heuristic's effect).</summary>
        public int Expanded;

        /// <summary>True if the search stopped because it hit the node budget.</summary>
        public bool BudgetExceeded;

        public static StalkerPath NotFound(int expanded, bool budgetExceeded) =>
            new StalkerPath { Found = false, Expanded = expanded, BudgetExceeded = budgetExceeded };
    }

    /// <summary>
    /// A* search over the shared level graph, using the <see cref="VisibilityCostModel"/> for edge costs and
    /// the straight-line heuristic. With the default heuristic weight of 1 the route is optimal under that cost,
    /// so "shortest" means "sneakiest": it trades extra walking for staying dark and out of the player's view.
    /// </summary>
    public sealed class AStarPathfinder
    {
        readonly VisibilityCostModel costs;
        readonly StalkerSettings settings;

        public AStarPathfinder(VisibilityCostModel costs, StalkerSettings settings)
        {
            this.costs = costs ?? throw new ArgumentNullException(nameof(costs));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        LevelGraph Graph => costs.Graph;

        bool IsValidNode(int id) => id >= 0 && id < Graph.NodeCount;

        /// <summary>Finds the cheapest route from <paramref name="start"/> to <paramref name="goal"/>.</summary>
        public StalkerPath FindPath(int start, int goal)
        {
            if (!IsValidNode(start) || !IsValidNode(goal)) return StalkerPath.NotFound(0, false);
            if (start == goal) return new StalkerPath { Found = true, Nodes = new[] { start }, Cost = 0f, Expanded = 0 };

            int n = Graph.NodeCount;
            var g = new float[n];
            var parent = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.PositiveInfinity; parent[i] = -1; }

            var open = new MinHeap(n);
            g[start] = 0f;
            open.Push(start, costs.Heuristic(start, goal));
            int expanded = 0;

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current]) continue;          // stale heap entry
                if (current == goal) return Build(parent, start, goal, g[goal], expanded);

                closed[current] = true;
                expanded++;
                if (expanded > settings.SearchNodeBudget) return StalkerPath.NotFound(expanded, true);

                foreach (GraphEdge edge in Graph.EdgesOf(current))
                {
                    int next = edge.Other(current);
                    if (next < 0 || closed[next]) continue;

                    float step = costs.EdgeCost(edge, current);
                    if (float.IsPositiveInfinity(step)) continue;   // locked door

                    float tentative = g[current] + step;
                    if (tentative < g[next])
                    {
                        g[next] = tentative;
                        parent[next] = current;
                        open.Push(next, tentative + costs.Heuristic(next, goal));
                    }
                }
            }

            return StalkerPath.NotFound(expanded, false);
        }

        /// <summary>
        /// Cost from <paramref name="start"/> to every node under the same visibility costs (Dijkstra, no heuristic).
        /// Unreachable nodes are positive infinity. Used to rate many candidate hiding or ambush spots in one pass.
        /// </summary>
        public float[] CostsFrom(int start)
        {
            int n = Graph.NodeCount;
            var dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            if (!IsValidNode(start)) return dist;

            var done = new bool[n];
            var open = new MinHeap(n);
            dist[start] = 0f;
            open.Push(start, 0f);

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (done[current]) continue;
                done[current] = true;

                foreach (GraphEdge edge in Graph.EdgesOf(current))
                {
                    int next = edge.Other(current);
                    if (next < 0 || done[next]) continue;
                    float step = costs.EdgeCost(edge, current);
                    if (float.IsPositiveInfinity(step)) continue;
                    float d = dist[current] + step;
                    if (d < dist[next]) { dist[next] = d; open.Push(next, d); }
                }
            }
            return dist;
        }

        static StalkerPath Build(int[] parent, int start, int goal, float cost, int expanded)
        {
            var route = new List<int>();
            for (int at = goal; at != -1; at = parent[at])
            {
                route.Add(at);
                if (at == start) break;
            }
            route.Reverse();
            return new StalkerPath { Found = true, Nodes = route.ToArray(), Cost = cost, Expanded = expanded };
        }

        /// <summary>Binary min-heap of (node, priority). Duplicates are allowed; stale entries are skipped on pop.</summary>
        sealed class MinHeap
        {
            int[] nodes;
            float[] keys;
            int count;

            public MinHeap(int capacity)
            {
                capacity = Math.Max(4, capacity);
                nodes = new int[capacity];
                keys = new float[capacity];
            }

            public int Count => count;

            public void Push(int node, float key)
            {
                if (count == nodes.Length)
                {
                    Array.Resize(ref nodes, count * 2);
                    Array.Resize(ref keys, count * 2);
                }
                int i = count++;
                nodes[i] = node; keys[i] = key;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= keys[i]) break;
                    Swap(i, p); i = p;
                }
            }

            public int Pop()
            {
                int top = nodes[0];
                count--;
                if (count > 0)
                {
                    nodes[0] = nodes[count]; keys[0] = keys[count];
                    int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = l + 1, s = i;
                        if (l < count && keys[l] < keys[s]) s = l;
                        if (r < count && keys[r] < keys[s]) s = r;
                        if (s == i) break;
                        Swap(i, s); i = s;
                    }
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (nodes[a], nodes[b]) = (nodes[b], nodes[a]);
                (keys[a], keys[b]) = (keys[b], keys[a]);
            }
        }
    }
}

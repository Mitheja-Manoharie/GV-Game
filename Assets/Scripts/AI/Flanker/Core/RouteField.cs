using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.Flanker
{
    /// <summary>
    /// Threat-weighted walking costs from one tile to every tile of the level graph (Dijkstra).
    /// Entering a tile the player is watching costs (1 + exposure penalty) times the step length, so the cheapest
    /// route is the one that stays out of the player's view, not simply the shortest one. One search prices every
    /// destination at once, which is how the planner costs all of its move actions with a single pass.
    /// Locked doors are impassable, exactly as in <see cref="LevelGraph.IsPassable"/>.
    /// </summary>
    public sealed class RouteField
    {
        private readonly LevelGraph graph;
        private readonly float[] cost;
        private readonly float[] length;
        private readonly float[] exposedLength;
        private readonly int[] previous;

        private RouteField(LevelGraph graph, int origin)
        {
            this.graph = graph;
            Origin = origin;
            int count = graph.NodeCount;
            cost = new float[count];
            length = new float[count];
            exposedLength = new float[count];
            previous = new int[count];
        }

        public int Origin { get; }

        /// <summary>Threat-weighted cost from the origin (+infinity if unreachable).</summary>
        public float CostTo(int node) => cost[node];

        /// <summary>Real walking distance of the cheapest route, in metres.</summary>
        public float LengthTo(int node) => length[node];

        /// <summary>Metres of the cheapest route that run through tiles the player is watching.</summary>
        public float ExposedLengthTo(int node) => exposedLength[node];

        public bool CanReach(int node) => !float.IsPositiveInfinity(cost[node]);

        /// <summary>Node ids from the origin to <paramref name="node"/> (both included); empty if unreachable.</summary>
        public int[] PathTo(int node)
        {
            if (!CanReach(node)) return Array.Empty<int>();

            var path = new List<int>();
            for (int current = node; current != -1; current = previous[current]) path.Add(current);
            path.Reverse();
            return path.ToArray();
        }

        /// <param name="watched">Tiles the player is holding (indexed by node); null means no threat.</param>
        /// <param name="exposurePenalty">
        /// Extra cost per metre walked into a watched tile. <see cref="float.PositiveInfinity"/> forbids watched tiles
        /// entirely, giving hidden routes only.
        /// </param>
        public static RouteField Compute(LevelGraph graph, int origin, ICollection<string> lockedDoors = null,
            IReadOnlyList<bool> watched = null, float exposurePenalty = 0f)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (origin < 0 || origin >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(origin), $"No node with id {origin}.");
            if (!(exposurePenalty >= 0f))
                throw new ArgumentOutOfRangeException(nameof(exposurePenalty), "Must not be negative.");
            if (watched != null && watched.Count != graph.NodeCount)
                throw new ArgumentException("Need one watched flag per node.", nameof(watched));

            var field = new RouteField(graph, origin);
            int count = graph.NodeCount;
            var settled = new bool[count];
            for (int i = 0; i < count; i++)
            {
                field.cost[i] = float.PositiveInfinity;
                field.previous[i] = -1;
            }

            field.cost[origin] = 0f;

            // O(V²) array Dijkstra, as in LevelGraph: 45 nodes, no priority queue needed.
            for (int step = 0; step < count; step++)
            {
                int current = -1;
                float smallest = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!settled[i] && field.cost[i] < smallest)
                    {
                        smallest = field.cost[i];
                        current = i;
                    }
                }

                if (current == -1) break;
                settled[current] = true;

                foreach (var edge in graph.EdgesOf(current))
                {
                    if (!LevelGraph.IsPassable(edge, lockedDoors)) continue;

                    int next = edge.Other(current);
                    bool exposed = watched != null && watched[next];
                    if (exposed && float.IsPositiveInfinity(exposurePenalty)) continue;

                    float candidate = field.cost[current] + edge.Cost * (exposed ? 1f + exposurePenalty : 1f);
                    if (candidate < field.cost[next])
                    {
                        field.cost[next] = candidate;
                        field.previous[next] = current;
                        field.length[next] = field.length[current] + edge.Cost;
                        field.exposedLength[next] = field.exposedLength[current] + (exposed ? edge.Cost : 0f);
                    }
                }
            }

            return field;
        }

        /// <summary>The reachable node matching <paramref name="isTarget"/> with the lowest cost (ties: lowest id), or -1.</summary>
        public int Cheapest(Func<int, bool> isTarget)
        {
            int best = -1;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (!CanReach(i) || !isTarget(i)) continue;
                if (best == -1 || cost[i] < cost[best]) best = i;
            }

            return best;
        }
    }
}

using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.Hunter.Tests
{
    /// <summary>Helpers shared by the search tests: an independent oracle and random door states.</summary>
    internal static class SearchTestSupport
    {
        /// <summary>
        /// Floyd-Warshall over the same live edge costs. It shares no code with A* or Dijkstra,
        /// so agreement between them is real evidence of correctness.
        /// </summary>
        public static float[,] AllPairs(LevelGraph graph, EdgeCostModel costs, IDoorStateProvider doors)
        {
            int n = graph.NodeCount;
            var d = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    d[i, j] = i == j ? 0f : float.PositiveInfinity;

            foreach (GraphEdge edge in graph.Edges)
            {
                float c = costs.Cost(edge, doors);
                if (c < d[edge.A, edge.B]) d[edge.A, edge.B] = c;
                if (c < d[edge.B, edge.A]) d[edge.B, edge.A] = c;
            }

            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (d[i, k] + d[k, j] < d[i, j]) d[i, j] = d[i, k] + d[k, j];
            return d;
        }

        public static DoorStateTable RandomDoorStates(Random random, LevelGraph graph)
        {
            var table = new DoorStateTable(DoorState.Open);
            var states = (DoorState[])Enum.GetValues(typeof(DoorState));
            foreach (string id in graph.DoorIds)
                table.Set(id, states[random.Next(states.Length)]);
            return table;
        }

        public static GraphEdge EdgeBetween(LevelGraph graph, int a, int b)
        {
            foreach (GraphEdge edge in graph.EdgesOf(a))
                if (edge.Other(a) == b) return edge;
            return null;
        }

        public static bool UsesDoor(LevelGraph graph, PathResult path, string doorId)
        {
            for (int i = 0; i + 1 < path.Nodes.Count; i++)
            {
                GraphEdge edge = EdgeBetween(graph, path.Nodes[i], path.Nodes[i + 1]);
                if (edge != null && edge.DoorId == doorId) return true;
            }
            return false;
        }
    }
}

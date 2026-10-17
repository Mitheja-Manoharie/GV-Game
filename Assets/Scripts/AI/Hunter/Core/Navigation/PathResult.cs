using System.Collections.Generic;

namespace LabLockdown.AI.Hunter
{
    /// <summary>Outcome of a path search over the level graph.</summary>
    public sealed class PathResult
    {
        private static readonly int[] NoNodes = new int[0];

        private PathResult(bool found, IReadOnlyList<int> nodes, float cost, int nodesExpanded)
        {
            Found = found;
            Nodes = nodes;
            Cost = cost;
            NodesExpanded = nodesExpanded;
        }

        public bool Found { get; }

        /// <summary>Node ids from start to goal, inclusive. Empty when no path exists.</summary>
        public IReadOnlyList<int> Nodes { get; }

        /// <summary>Total cost under the cost model that was used. Infinity when no path exists.</summary>
        public float Cost { get; }

        /// <summary>How many nodes the search expanded. Used to compare A* with Dijkstra.</summary>
        public int NodesExpanded { get; }

        public static PathResult Success(IReadOnlyList<int> nodes, float cost, int nodesExpanded)
            => new PathResult(true, nodes, cost, nodesExpanded);

        public static PathResult Failure(int nodesExpanded)
            => new PathResult(false, NoNodes, float.PositiveInfinity, nodesExpanded);
    }
}

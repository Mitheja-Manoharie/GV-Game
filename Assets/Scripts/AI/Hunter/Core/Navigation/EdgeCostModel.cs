using System;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// Turns a graph edge plus the current door state into a walking cost.
    ///
    ///   plain edge        : cost = edge.Cost                      (metres)
    ///   open door         : cost = edge.Cost
    ///   closed door       : cost = edge.Cost + ClosedDoorPenalty  (time spent opening it, in metres)
    ///   locked or blocked : cost = infinity                       (cannot be walked)
    ///
    /// Every cost is at least edge.Cost, so as long as edge.Cost is at least the straight-line distance
    /// between the two nodes, the Euclidean heuristic used by A* stays admissible and consistent.
    /// </summary>
    public sealed class EdgeCostModel
    {
        /// <summary>Opening takes about 1 s and the Hunter walks at about 3 m/s, so a closed door costs about 3 m.</summary>
        public const float DefaultClosedDoorPenalty = 3f;

        public EdgeCostModel(float closedDoorPenalty = DefaultClosedDoorPenalty)
        {
            if (closedDoorPenalty < 0f || float.IsNaN(closedDoorPenalty))
                throw new ArgumentOutOfRangeException(nameof(closedDoorPenalty), "The penalty must be zero or positive.");
            ClosedDoorPenalty = closedDoorPenalty;
        }

        public float ClosedDoorPenalty { get; }

        public float Cost(GraphEdge edge, IDoorStateProvider doors)
        {
            if (edge == null) throw new ArgumentNullException(nameof(edge));
            if (!edge.IsDoor) return edge.Cost;
            if (doors == null) throw new ArgumentNullException(nameof(doors));

            switch (doors.GetState(edge.DoorId))
            {
                case DoorState.Open:
                    return edge.Cost;
                case DoorState.Closed:
                    return edge.Cost + ClosedDoorPenalty;
                default:
                    return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// True when no edge is cheaper than the straight line between its nodes. This is exactly the
        /// condition that makes the Euclidean heuristic admissible, so it is checked in a unit test.
        /// </summary>
        public static bool IsHeuristicAdmissible(LevelGraph graph, float tolerance = 1e-3f)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.Cost + tolerance < Euclidean(graph.Nodes[edge.A], graph.Nodes[edge.B]))
                    return false;
            }
            return true;
        }

        public static float Euclidean(GraphNode a, GraphNode b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}

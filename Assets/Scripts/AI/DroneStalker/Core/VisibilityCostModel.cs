using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>
    /// Turns light and the player's line of sight into a path cost, so A* finds the sneakiest route
    /// rather than the shortest one.
    ///
    /// Cost of moving along an edge into node n:
    ///     edge length + LightCostWeight * light(n) + SeenCostWeight * seen(n)
    /// where light(n) is 0 (dark) to 1 (lit) and seen(n) is 1 if the player can see n, else 0.
    ///
    /// Both penalties are never negative, so every edge costs at least its length. The straight-line
    /// distance is therefore never more than the true remaining cost, which keeps the A* heuristic
    /// admissible (the found route is optimal under this cost).
    /// Closed and locked doors are impassable (infinite cost): drones cannot open doors.
    /// </summary>
    public sealed class VisibilityCostModel
    {
        readonly LevelGraph graph;
        readonly StalkerSettings settings;
        StalkerPercepts percepts;
        bool playerKnown = true;

        public VisibilityCostModel(LevelGraph graph, StalkerSettings settings)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>The graph the costs are computed on.</summary>
        public LevelGraph Graph => graph;

        /// <summary>
        /// Uses the latest percepts for light, the player's view and door states. Null means "nothing known".
        /// When <paramref name="isPlayerKnown"/> is false the drone does not know where the player is, so it cannot
        /// know what the player sees either, and the "seen" penalty is ignored.
        /// </summary>
        public void SetPercepts(StalkerPercepts latest, bool isPlayerKnown = true)
        {
            percepts = latest;
            playerKnown = isPlayerKnown;
        }

        /// <summary>The doors the drone cannot pass right now (closed or locked), read live from the percepts.</summary>
        public ICollection<string> BlockedDoors => percepts != null ? percepts.BlockedDoors() : new HashSet<string>();

        /// <summary>True if the edge can be used right now (not a closed or locked door). Reads the door states live.</summary>
        public bool IsPassable(GraphEdge edge)
        {
            if (edge == null) return false;
            if (percepts == null) return true;
            return LevelGraph.IsPassable(edge, percepts.LockedDoors) && LevelGraph.IsPassable(edge, percepts.ClosedDoors);
        }

        /// <summary>Light level of a node, 0 to 1 (neutral when no light data is known).</summary>
        public float LightAt(int nodeId) => percepts != null ? percepts.LightAt(nodeId) : StalkerPercepts.NeutralLight;

        /// <summary>True if the player can see the node right now (always false while the player's position is unknown).</summary>
        public bool IsSeen(int nodeId) => playerKnown && percepts != null && percepts.IsSeenByPlayer(nodeId);

        /// <summary>Extra cost, in metres, for standing in a node: its light plus whether the player sees it.</summary>
        public float NodePenalty(int nodeId)
        {
            float penalty = settings.LightCostWeight * LightAt(nodeId);
            if (IsSeen(nodeId)) penalty += settings.SeenCostWeight;
            return penalty;
        }

        /// <summary>
        /// Cost of moving along <paramref name="edge"/> from <paramref name="fromNode"/> to the other end.
        /// Returns positive infinity for a locked door or an edge that does not touch the node.
        /// </summary>
        public float EdgeCost(GraphEdge edge, int fromNode)
        {
            if (!IsPassable(edge)) return float.PositiveInfinity;
            int toNode = edge.Other(fromNode);
            if (toNode < 0) return float.PositiveInfinity;
            return edge.Cost + NodePenalty(toNode);
        }

        /// <summary>Straight-line distance between two nodes, scaled by the heuristic weight.</summary>
        public float Heuristic(int fromNode, int toNode)
        {
            GraphNode a = graph.Nodes[fromNode];
            GraphNode b = graph.Nodes[toNode];
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return settings.HeuristicWeight * (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// How exposed a node is, 0 (dark and unseen) to 1 (lit and seen), weighted the same way as the path cost.
        /// Used when rating places to hide or wait in ambush.
        /// </summary>
        public float Exposure(int nodeId)
        {
            float total = settings.LightCostWeight + settings.SeenCostWeight;
            if (total <= 0f) return 0f;
            return NodePenalty(nodeId) / total;
        }
    }
}

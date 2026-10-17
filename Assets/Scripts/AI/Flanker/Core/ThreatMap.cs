using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.Flanker
{
    /// <summary>
    /// How the player threatens each tile at one moment:
    /// <list type="bullet">
    /// <item><b>visible</b>: the player has line of sight to the tile (a raycast in Unity);</item>
    /// <item><b>watched</b>: visible and inside the player's view cone, so the player is "holding" that tile.</item>
    /// </list>
    /// A tile that is visible but not watched is the flanker's opportunity: it can attack from there while the
    /// player is looking the other way. A tile that is not visible at all is cover.
    /// </summary>
    public sealed class ThreatMap
    {
        private readonly LevelGraph graph;
        private readonly bool[] visible;
        private readonly bool[] watched;
        private readonly float playerX, playerZ;
        private readonly float attackRange;

        public ThreatMap(LevelGraph graph, FlankerPercepts percepts, FlankerSettings settings)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            if (percepts == null) throw new ArgumentNullException(nameof(percepts));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (percepts.PlayerNode < 0 || percepts.PlayerNode >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(percepts), $"No node with id {percepts.PlayerNode} for the player.");

            PlayerNode = percepts.PlayerNode;
            playerX = percepts.PlayerX;
            playerZ = percepts.PlayerZ;
            attackRange = settings.AttackRange;

            int count = graph.NodeCount;
            visible = new bool[count];
            watched = new bool[count];

            float forwardLength = MathF.Sqrt(percepts.PlayerForwardX * percepts.PlayerForwardX + percepts.PlayerForwardZ * percepts.PlayerForwardZ);
            bool facingKnown = forwardLength > 1e-4f;
            float fx = facingKnown ? percepts.PlayerForwardX / forwardLength : 0f;
            float fz = facingKnown ? percepts.PlayerForwardZ / forwardLength : 0f;
            float minCos = MathF.Cos(settings.ViewHalfAngleDegrees * MathF.PI / 180f);

            for (int i = 0; i < count; i++)
            {
                visible[i] = i == PlayerNode || (percepts.VisibleNodes != null && percepts.VisibleNodes.Contains(i));
                if (!visible[i]) continue;

                float dx = graph.Nodes[i].X - playerX;
                float dz = graph.Nodes[i].Z - playerZ;
                float distance = MathF.Sqrt(dx * dx + dz * dz);

                // The player's own tile, and every visible tile when the facing is unknown, count as watched.
                watched[i] = i == PlayerNode || !facingKnown || distance < 1e-3f
                             || (dx * fx + dz * fz) / distance >= minCos;
            }
        }

        public int PlayerNode { get; }

        public IReadOnlyList<bool> Visible => visible;
        public IReadOnlyList<bool> Watched => watched;

        public bool IsVisible(int node) => visible[node];
        public bool IsWatched(int node) => watched[node];

        /// <summary>A tile the player cannot see.</summary>
        public bool IsCover(int node) => !visible[node];

        /// <summary>A tile from which the flanker can attack: line of sight to the player and within range.</summary>
        public bool IsAttackPosition(int node) => visible[node] && DistanceToPlayer(node) <= attackRange;

        /// <summary>An attack position the player is not looking at: the reason to flank.</summary>
        public bool IsFlankPosition(int node) => IsAttackPosition(node) && !watched[node];

        public float DistanceToPlayer(int node)
        {
            float dx = graph.Nodes[node].X - playerX;
            float dz = graph.Nodes[node].Z - playerZ;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}

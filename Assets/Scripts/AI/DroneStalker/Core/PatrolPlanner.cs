using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>
    /// Chooses patrol destinations while the drone has no player in sight. The drone lurks rather than searches:
    /// it wanders between tiles at random, but dark tiles are much more likely, so it naturally keeps to the shadows.
    ///
    /// Each candidate tile within the leg length (in walking metres, through open doors only) gets a weight:
    ///     weight = 1 + PatrolDarkWeight * (1 - light)
    /// times PatrolTowardLastSeenFactor if it does not lead away from where the player was last seen
    /// (so after losing the player behind a closed door, the drone patrols away from that door).
    /// Recently visited tiles are skipped. One tile is then drawn at random in proportion to its weight.
    /// The random generator is seeded, so behaviour is reproducible in tests.
    /// </summary>
    public sealed class PatrolPlanner
    {
        readonly LevelGraph graph;
        readonly StalkerSettings s;
        readonly Random random;
        readonly Queue<int> recent = new Queue<int>();
        readonly HashSet<int> recentSet = new HashSet<int>();

        public PatrolPlanner(LevelGraph graph, StalkerSettings settings)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            s = settings ?? throw new ArgumentNullException(nameof(settings));
            random = new Random(s.PatrolSeed);
        }

        /// <summary>Remembers a tile the drone passed through, so patrol does not keep returning to it.</summary>
        public void MarkVisited(int node)
        {
            if (node < 0 || recentSet.Contains(node)) return;
            recent.Enqueue(node);
            recentSet.Add(node);
            while (recent.Count > s.PatrolRecentTiles)
                recentSet.Remove(recent.Dequeue());
        }

        /// <summary>True if the tile was visited recently.</summary>
        public bool WasRecentlyVisited(int node) => recentSet.Contains(node);

        /// <summary>
        /// Picks the next patrol destination from <paramref name="self"/>. <paramref name="awayFrom"/> is the tile the
        /// player was last seen on (or -1). Returns -1 only when no other tile can be reached at all.
        /// </summary>
        public int ChooseNext(int self, StalkerPercepts percepts, ICollection<string> blockedDoors, int awayFrom = -1)
        {
            if (self < 0 || self >= graph.NodeCount) return -1;

            float[] fromSelf = graph.ShortestDistances(self, blockedDoors);
            float[] fromLastSeen = awayFrom >= 0 && awayFrom < graph.NodeCount ? graph.ShortestDistances(awayFrom) : null;
            float selfToLastSeen = fromLastSeen != null ? fromLastSeen[self] : 0f;

            var candidates = new List<int>();
            var weights = new List<float>();
            float total = 0f;

            // First try tiles within the preferred leg length and not visited recently; relax step by step if none qualify.
            for (int pass = 0; pass < 3 && candidates.Count == 0; pass++)
            {
                for (int i = 0; i < graph.NodeCount; i++)
                {
                    if (i == self || float.IsPositiveInfinity(fromSelf[i])) continue;
                    if (pass < 2 && WasRecentlyVisited(i)) continue;
                    if (pass < 1 && (fromSelf[i] < s.PatrolMinLegMetres || fromSelf[i] > s.PatrolMaxLegMetres)) continue;

                    float light = percepts != null ? percepts.LightAt(i) : StalkerPercepts.NeutralLight;
                    float w = 1f + s.PatrolDarkWeight * (1f - light);
                    if (fromLastSeen != null && fromLastSeen[i] <= selfToLastSeen) w *= s.PatrolTowardLastSeenFactor;
                    if (w <= 0f) continue;

                    candidates.Add(i);
                    weights.Add(w);
                    total += w;
                }
            }

            if (candidates.Count == 0 || total <= 0f) return -1;

            double pick = random.NextDouble() * total;
            for (int k = 0; k < candidates.Count; k++)
            {
                pick -= weights[k];
                if (pick <= 0) return candidates[k];
            }
            return candidates[candidates.Count - 1];
        }
    }
}

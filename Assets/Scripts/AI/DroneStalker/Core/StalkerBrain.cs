using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>What the brain decided this tick: the tactic, where to go, the route, and whether to strike now.</summary>
    public sealed class StalkerDecision
    {
        public StalkerAction Action = StalkerAction.Stalk;

        /// <summary>Node the Stalker is heading for, or -1 when it has nowhere to go.</summary>
        public int TargetNode = -1;

        /// <summary>Route from the current node to the target (both included). A single node means "stay here".</summary>
        public int[] Path = Array.Empty<int>();

        /// <summary>True on the tick the Stalker should deliver its strike. The body plays the attack and deals damage.</summary>
        public bool StrikeNow;

        /// <summary>The utility scores behind the decision (null before the first decision).</summary>
        public ActionScores Scores;

        /// <summary>Short human-readable reason, shown by the debug overlay.</summary>
        public string Note = "";

        public bool HasRoute => Path != null && Path.Length > 1;
    }

    /// <summary>
    /// The Drone Stalker's brain: a utility-based agent that plans with A* over a visibility cost.
    ///
    /// It only acts on what it perceives. It knows where the player is only while it can see the player with its
    /// own sight (walls and closed doors block it), plus a short grace period after losing sight. Otherwise it
    /// patrols, preferring dark tiles; after losing the player it first patrols away from where it lost them.
    ///
    /// While aware of the player, each decision it (1) updates its memory (when it was last seen, when it last
    /// struck), (2) builds the situation (walking distance, light where it is, the best ambush spot on the player's
    /// predicted route), (3) scores the five tactics and picks the best, with hysteresis so it does not flip-flop,
    /// (4) picks a target node for that tactic, and (5) plans a route there with A*. Between decisions it only
    /// replans the current route, and does so at once if a door on it is closed or locked.
    ///
    /// Pure C#: no UnityEngine, so every behaviour is unit tested in EditMode.
    /// </summary>
    public sealed class StalkerBrain
    {
        readonly LevelGraph graph;
        readonly StalkerSettings s;
        readonly VisibilityCostModel costs;
        readonly AStarPathfinder search;
        readonly UtilityScorer scorer;
        readonly PatrolPlanner patrol;

        StalkerDecision current = new StalkerDecision();
        bool hasDecided;
        double lastDecision = double.NegativeInfinity;
        double lastReplan = double.NegativeInfinity;
        double actionSince;
        double lastSpotted = double.NegativeInfinity;
        double lastStrike = double.NegativeInfinity;

        // Awareness: the drone's memory of the player, built only from its own sight.
        int lastSeenNode = -1;
        double lastSeenTime = double.NegativeInfinity;
        int lastSelfNode = -1;
        int patrolAwayFrom = -1;

        public StalkerBrain(LevelGraph graph, StalkerSettings settings)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            s = settings ?? throw new ArgumentNullException(nameof(settings));
            costs = new VisibilityCostModel(graph, s);
            search = new AStarPathfinder(costs, s);
            scorer = new UtilityScorer(s);
            patrol = new PatrolPlanner(graph, s);
        }

        /// <summary>The latest decision (also kept between decisions).</summary>
        public StalkerDecision Current => current;

        public StalkerSettings Settings => s;

        /// <summary>True while the drone knows where the player is (in sight, or lost within the grace period).</summary>
        public bool IsAwareOfPlayer { get; private set; }

        /// <summary>The tile the player was last seen on, or -1 if never seen.</summary>
        public int LastSeenPlayerNode => lastSeenNode;

        /// <summary>Main entry point. Call every frame (or less); the brain throttles itself.</summary>
        public StalkerDecision Think(StalkerPercepts p)
        {
            if (p == null) return current;
            s.Validate();

            if (!IsValid(p.SelfNode))
            {
                current = Hold(current.Action, -1, "position unknown");
                return current;
            }

            if (p.SelfNode != lastSelfNode)
            {
                patrol.MarkVisited(p.SelfNode);
                lastSelfNode = p.SelfNode;
            }

            // Awareness comes only from the drone's own sight; after losing sight it keeps the last known tile for a grace period.
            bool seesPlayer = p.PlayerInSight && IsValid(p.PlayerNode);
            if (seesPlayer)
            {
                lastSeenNode = p.PlayerNode;
                lastSeenTime = p.Time;
            }
            IsAwareOfPlayer = seesPlayer || (IsValid(lastSeenNode) && p.Time - lastSeenTime <= s.LostSightGraceSeconds);
            costs.SetPercepts(p, seesPlayer);

            if (!IsAwareOfPlayer)
            {
                Patrol(p);
                return current;
            }

            int playerNode = seesPlayer ? p.PlayerNode : lastSeenNode;
            if (p.PlayerSeesMe) lastSpotted = p.Time;

            // Spotting the player, or being seen, is urgent: react at once instead of waiting for the next decision tick.
            bool urgent = current.Action == StalkerAction.Patrol ||
                          (p.PlayerSeesMe && current.Action != StalkerAction.Hide && current.Action != StalkerAction.Retreat);
            bool due = !hasDecided || urgent || p.Time - lastDecision >= s.DecisionIntervalSeconds;
            if (!due)
            {
                if (MaybeReplan(p)) Decide(p, playerNode, seesPlayer);
                return current;
            }

            Decide(p, playerNode, seesPlayer);
            return current;
        }

        // ---------------- patrol (no player in sight) ----------------

        void Patrol(StalkerPercepts p)
        {
            bool starting = current.Action != StalkerAction.Patrol || !hasDecided;
            if (starting)
            {
                actionSince = p.Time;
                patrolAwayFrom = lastSeenNode;   // -1 if the player was never seen
            }

            bool arrived = current.TargetNode == p.SelfNode;
            bool needLeg = starting || !IsValid(current.TargetNode) || arrived;
            if (!needLeg && !MaybeReplan(p)) return;

            int target = patrol.ChooseNext(p.SelfNode, p, costs.BlockedDoors, patrolAwayFrom);
            StalkerPath route = target >= 0 ? search.FindPath(p.SelfNode, target) : null;
            bool found = route != null && route.Found;

            string note = IsValid(patrolAwayFrom) ? "Patrol: lost the player, moving away" : "Patrol: no player in sight";
            if (!found) note = "Patrol: no open route, holding";

            current = new StalkerDecision
            {
                Action = StalkerAction.Patrol,
                TargetNode = found ? target : p.SelfNode,
                Path = found ? route.Nodes : new[] { p.SelfNode },
                Note = note,
            };
            hasDecided = true;
            lastDecision = p.Time;
            lastReplan = p.Time;
            if (!starting) patrolAwayFrom = -1;   // only the first leg after losing the player is pushed away
        }

        // ---------------- tactics (player in sight or just lost) ----------------

        void Decide(StalkerPercepts p, int playerNode, bool seesPlayer)
        {
            lastDecision = p.Time;
            ICollection<string> blocked = costs.BlockedDoors;

            float[] fromPlayer = graph.ShortestDistances(playerNode, blocked);   // walking metres
            float[] fromSelf = graph.ShortestDistances(p.SelfNode, blocked);     // walking metres
            float[] sneakCost = search.CostsFrom(p.SelfNode);                  // visibility cost

            int ambushNode = FindAmbushSpot(p, playerNode, fromSelf, sneakCost, out float ambushQuality);

            var situation = new StalkerSituation
            {
                WalkDistanceToPlayer = fromPlayer[p.SelfNode],
                StraightDistanceToPlayer = seesPlayer ? p.DistanceToPlayerMetres : float.PositiveInfinity,
                PlayerHealth01 = p.PlayerHealth01,
                SeenNow = p.PlayerSeesMe,
                PlayerInSight = seesPlayer,
                LightHere = p.LightAt(p.SelfNode),
                SecondsSinceSpotted = Since(p.Time, lastSpotted),
                SecondsSinceStrike = Since(p.Time, lastStrike),
                AmbushSpotQuality = ambushQuality,
            };

            ActionScores scores = scorer.Score(situation);
            StalkerAction choice = ApplyHysteresis(scores, p, ambushQuality);

            int target;
            switch (choice)
            {
                case StalkerAction.Hide: target = HideTarget(p, playerNode, fromSelf, sneakCost); break;
                case StalkerAction.Ambush: target = ambushNode; break;
                case StalkerAction.Strike: target = playerNode; break;
                case StalkerAction.Retreat: target = RetreatTarget(fromPlayer, sneakCost); break;
                default: target = StalkTarget(p, playerNode, fromPlayer, sneakCost); break;
            }

            bool strikeNow = false;
            if (choice == StalkerAction.Strike && scorer.CanStrike(situation))
            {
                strikeNow = true;
                lastStrike = p.Time;
            }

            string note = Describe(choice, scores);
            StalkerPath route = target >= 0 ? search.FindPath(p.SelfNode, target) : null;
            if (route == null || !route.Found)
            {
                // The chosen target is unreachable (e.g. behind a closed door): fall back to hiding nearby.
                int fallback = HideTarget(p, playerNode, fromSelf, sneakCost);
                route = fallback >= 0 ? search.FindPath(p.SelfNode, fallback) : null;
                target = route != null && route.Found ? fallback : p.SelfNode;
                note += " (target unreachable, hiding nearby)";
            }

            if (!hasDecided || choice != current.Action) actionSince = p.Time;
            hasDecided = true;

            current = new StalkerDecision
            {
                Action = choice,
                TargetNode = target,
                Path = route != null && route.Found ? route.Nodes : new[] { p.SelfNode },
                StrikeNow = strikeNow,
                Scores = scores,
                Note = note,
            };
            lastReplan = p.Time;
        }

        /// <summary>
        /// Keeps the current tactic unless the new best is clearly better (by SwitchMargin) or the commit time is over.
        /// Exceptions: being seen (hide or retreat at once), a strike opportunity, or the current tactic becoming impossible.
        /// </summary>
        StalkerAction ApplyHysteresis(ActionScores scores, StalkerPercepts p, float ambushQuality)
        {
            StalkerAction best = scores.Best;
            if (!hasDecided || best == current.Action) return best;

            bool urgent = best == StalkerAction.Strike ||
                          (p.PlayerSeesMe && (best == StalkerAction.Hide || best == StalkerAction.Retreat));
            bool currentImpossible = current.Action == StalkerAction.Strike ||
                                     current.Action == StalkerAction.Patrol ||
                                     (current.Action == StalkerAction.Ambush && ambushQuality <= 0f);
            if (urgent || currentImpossible) return best;

            bool committed = p.Time - actionSince < s.ActionCommitSeconds;
            bool clearlyBetter = scores[best] >= scores[current.Action] + s.SwitchMargin;
            return committed && !clearlyBetter ? current.Action : best;
        }

        // ---------------- target selection ----------------

        bool IsDark(StalkerPercepts p, int node) => p.LightAt(node) <= s.DarkLightLevel;

        /// <summary>Nearest dark node the player cannot see, within the hide search radius; otherwise the least exposed reachable node.</summary>
        int HideTarget(StalkerPercepts p, int playerNode, float[] fromSelf, float[] sneakCost)
        {
            int best = -1; float bestCost = float.PositiveInfinity;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (i == playerNode || float.IsPositiveInfinity(sneakCost[i]) || fromSelf[i] > s.HideSearchMetres) continue;
                if (!IsDark(p, i) || costs.IsSeen(i)) continue;
                if (sneakCost[i] < bestCost) { bestCost = sneakCost[i]; best = i; }
            }
            if (best >= 0) return best;

            // No proper hiding place: take the least exposed reachable node, preferring closer ones.
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (i == playerNode || float.IsPositiveInfinity(sneakCost[i])) continue;
                float score = costs.Exposure(i) * 100f + sneakCost[i];
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>
        /// A dark, unseen node at the follow distance from the player (a quarter of the way from "near" to "far"),
        /// preferring nodes that are cheap to reach.
        /// </summary>
        int StalkTarget(StalkerPercepts p, int playerNode, float[] fromPlayer, float[] sneakCost)
        {
            float follow = s.NearDistanceMetres + 0.25f * (s.FarDistanceMetres - s.NearDistanceMetres);
            int best = -1; float bestScore = float.PositiveInfinity;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (i == playerNode || float.IsPositiveInfinity(sneakCost[i]) || float.IsPositiveInfinity(fromPlayer[i])) continue;
                float score = Math.Abs(fromPlayer[i] - follow)
                              + s.LightCostWeight * p.LightAt(i)
                              + (costs.IsSeen(i) ? s.SeenCostWeight : 0f)
                              + 0.25f * sneakCost[i];
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>A reachable node at least the retreat distance from the player, as dark and unseen as possible; else the farthest one.</summary>
        int RetreatTarget(float[] fromPlayer, float[] sneakCost)
        {
            int best = -1; float bestScore = float.PositiveInfinity;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (float.IsPositiveInfinity(sneakCost[i]) || fromPlayer[i] < s.RetreatMinDistanceMetres) continue;
                float score = sneakCost[i] + 20f * costs.Exposure(i);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) return best;

            float farthest = -1f;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (float.IsPositiveInfinity(sneakCost[i]) || float.IsPositiveInfinity(fromPlayer[i])) continue;
                if (fromPlayer[i] > farthest) { farthest = fromPlayer[i]; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Predicts the player's route (to a heal node when hurt, else to the exit), then rates the dark, unseen nodes
        /// on or beside the next few steps of it. Quality = (1 - exposure) / (1 + walk distance / 40 m), so a dark spot
        /// the Stalker can reach quickly scores close to 1. Returns -1 (quality 0) when no route can be predicted.
        /// </summary>
        int FindAmbushSpot(StalkerPercepts p, int playerNode, float[] fromSelf, float[] sneakCost, out float quality)
        {
            quality = 0f;
            int goal = PredictPlayerGoal(p, playerNode);
            if (goal < 0 || goal == playerNode) return -1;

            int[] route = graph.ShortestPath(playerNode, goal, costs.BlockedDoors);
            if (route == null || route.Length < 2) return -1;

            var candidates = new HashSet<int>();
            int end = Math.Min(route.Length - 1, s.AmbushLookaheadNodes);
            for (int k = 1; k <= end; k++)
            {
                candidates.Add(route[k]);
                foreach (GraphEdge edge in graph.EdgesOf(route[k]))
                    if (costs.IsPassable(edge)) candidates.Add(edge.Other(route[k]));
            }

            int best = -1;
            foreach (int c in candidates)
            {
                if (c < 0 || c == playerNode || float.IsPositiveInfinity(sneakCost[c])) continue;
                if (!IsDark(p, c) || costs.IsSeen(c)) continue;
                float q = (1f - costs.Exposure(c)) / (1f + fromSelf[c] / 40f);
                if (q > quality) { quality = q; best = c; }
            }
            return best;
        }

        int PredictPlayerGoal(StalkerPercepts p, int playerNode)
        {
            bool hurt = p.PlayerHealth01 < 0.5f;
            if (hurt && p.HealNodes != null && p.HealNodes.Count > 0)
            {
                float[] fromPlayer = graph.ShortestDistances(playerNode, costs.BlockedDoors);
                int nearest = -1; float d = float.PositiveInfinity;
                foreach (int h in p.HealNodes)
                    if (IsValid(h) && fromPlayer[h] < d) { d = fromPlayer[h]; nearest = h; }
                if (nearest >= 0) return nearest;
            }
            return IsValid(p.ExitNode) ? p.ExitNode : -1;
        }

        // ---------------- replanning between decisions ----------------

        /// <summary>
        /// Refreshes the current route if it is due or a door on it was closed. Returns true when the target can no
        /// longer be reached, so the caller should make a fresh decision (or pick a new patrol leg) right away.
        /// </summary>
        bool MaybeReplan(StalkerPercepts p)
        {
            if (!IsValid(current.TargetNode)) return true;
            bool blocked = RouteBlocked(current.Path);
            if (!blocked && p.Time - lastReplan < s.ReplanIntervalSeconds) return false;

            lastReplan = p.Time;
            StalkerPath route = search.FindPath(p.SelfNode, current.TargetNode);
            if (!route.Found) return true;
            current.Path = route.Nodes;
            return false;
        }

        bool RouteBlocked(int[] path)
        {
            if (path == null) return false;
            for (int k = 0; k + 1 < path.Length; k++)
            {
                bool open = false;
                foreach (GraphEdge edge in graph.EdgesOf(path[k]))
                    if (edge.Other(path[k]) == path[k + 1] && costs.IsPassable(edge)) { open = true; break; }
                if (!open) return true;
            }
            return false;
        }

        // ---------------- helpers ----------------

        bool IsValid(int node) => node >= 0 && node < graph.NodeCount;

        static float Since(double now, double then) =>
            double.IsNegativeInfinity(then) ? float.PositiveInfinity : (float)Math.Max(0.0, now - then);

        static StalkerDecision Hold(StalkerAction action, int node, string note) => new StalkerDecision
        {
            Action = action,
            TargetNode = node,
            Path = node >= 0 ? new[] { node } : Array.Empty<int>(),
            Note = note,
        };

        static string Describe(StalkerAction choice, ActionScores scores)
        {
            float top = 0f; string reason = "";
            foreach (var term in scores.Terms)
                if (term.action == choice && term.value > top) { top = term.value; reason = term.term; }
            return reason.Length > 0 ? $"{choice}: {reason}" : choice.ToString();
        }
    }
}

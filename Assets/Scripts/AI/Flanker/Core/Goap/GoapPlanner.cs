using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Flanker.Goap
{
    public sealed class PlannerOptions
    {
        /// <summary>
        /// A* with the "unsatisfied goal facts" heuristic. When false the planner is uniform-cost search
        /// (Dijkstra over world states): the baseline the heuristic is measured against.
        /// </summary>
        public bool UseHeuristic { get; set; } = true;

        /// <summary>Hard cap on expanded states so planning can never stall a frame.</summary>
        public int MaxNodes { get; set; } = 10_000;

        public static PlannerOptions UniformCost() => new PlannerOptions { UseHeuristic = false };
    }

    public enum PlanStatus
    {
        Found,
        NoPlan,

        /// <summary>The node budget ran out first; no conclusion about whether a plan exists.</summary>
        NodeLimitReached,
    }

    public sealed class PlannerStats
    {
        /// <summary>States taken off the open list and expanded (goal test included).</summary>
        public int NodesExpanded { get; internal set; }

        /// <summary>Successor states pushed onto the open list.</summary>
        public int NodesGenerated { get; internal set; }

        public override string ToString() => $"expanded {NodesExpanded}, generated {NodesGenerated}";
    }

    public sealed class PlanResult
    {
        internal PlanResult(PlanStatus status, List<PlanAction> actions, List<WorldState> states, float cost, PlannerStats stats)
        {
            Status = status;
            Actions = actions;
            States = states;
            Cost = cost;
            Stats = stats;
        }

        public PlanStatus Status { get; }
        public bool IsFound => Status == PlanStatus.Found;

        /// <summary>The actions in execution order; empty when the goal already holds or no plan was found.</summary>
        public IReadOnlyList<PlanAction> Actions { get; }

        /// <summary>The start state followed by the state after each action (one more entry than <see cref="Actions"/>).</summary>
        public IReadOnlyList<WorldState> States { get; }

        /// <summary>Total action cost, or +infinity when no plan was found.</summary>
        public float Cost { get; }

        public PlannerStats Stats { get; }

        public override string ToString()
        {
            if (!IsFound) return Status.ToString();
            return Actions.Count == 0 ? "(goal already holds)" : string.Join(" → ", Actions);
        }
    }

    /// <summary>
    /// STRIPS planner: forward A* search through world states. Each search node is a state, each edge an
    /// applicable action weighted by its cost, and the search stops at the first state that satisfies the goal.
    ///
    /// Heuristic: h(s) = ceil(u / k) · c_min, where u is the number of goal facts not yet satisfied, k the most
    /// goal facts any single action can satisfy, and c_min the cheapest action cost. One action fixes at most k
    /// of the u facts, so at least ceil(u / k) more actions are needed, each costing at least c_min: h never
    /// overestimates (admissible), and it changes by at most c_min per step (consistent). A* with a consistent
    /// heuristic returns the cheapest plan without re-opening states, which is what the closed set relies on.
    ///
    /// The planner keeps no state between calls, so one instance can be reused.
    /// </summary>
    public sealed class GoapPlanner
    {
        private struct SearchNode
        {
            public WorldState State;
            public int Parent;
            public PlanAction Action;
            public float G;
        }

        private struct OpenEntry
        {
            public float F;
            public float G;
            public int Order;
            public int Node;
        }

        public PlanResult Plan(WorldState start, Goal goal, IReadOnlyList<PlanAction> actions, PlannerOptions options = null)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            options = options ?? new PlannerOptions();
            var stats = new PlannerStats();

            float cheapest = float.PositiveInfinity;
            int mostFixed = 0;
            foreach (var action in actions)
            {
                if (action == null) throw new ArgumentException("The action list contains null.", nameof(actions));
                cheapest = Math.Min(cheapest, action.Cost);
                mostFixed = Math.Max(mostFixed,
                    Facts.Count(action.Add & goal.MustBeTrue) + Facts.Count(action.Delete & goal.MustBeFalse));
            }

            float Heuristic(WorldState state)
            {
                if (!options.UseHeuristic || mostFixed == 0) return 0f;
                int unsatisfied = goal.CountUnsatisfied(state);
                return unsatisfied == 0 ? 0f : (unsatisfied + mostFixed - 1) / mostFixed * cheapest;
            }

            var nodes = new List<SearchNode> { new SearchNode { State = start, Parent = -1, G = 0f } };
            var bestG = new Dictionary<WorldState, float> { [start] = 0f };
            var closed = new HashSet<WorldState>();
            var open = new MinHeap<OpenEntry>(CompareEntries);
            int order = 0;
            open.Push(new OpenEntry { F = Heuristic(start), G = 0f, Order = order++, Node = 0 });

            while (open.Count > 0)
            {
                var entry = open.Pop();
                var node = nodes[entry.Node];
                if (closed.Contains(node.State)) continue; // a cheaper copy of this state was already expanded

                if (goal.IsSatisfiedBy(node.State)) return Reconstruct(nodes, entry.Node, stats);

                if (stats.NodesExpanded >= options.MaxNodes)
                    return Failed(PlanStatus.NodeLimitReached, start, stats);

                closed.Add(node.State);
                stats.NodesExpanded++;

                foreach (var action in actions)
                {
                    if (!action.IsApplicable(node.State)) continue;

                    var next = action.Apply(node.State);
                    if (next == node.State || closed.Contains(next)) continue;

                    float g = node.G + action.Cost;
                    if (bestG.TryGetValue(next, out float known) && known <= g) continue;

                    bestG[next] = g;
                    nodes.Add(new SearchNode { State = next, Parent = entry.Node, Action = action, G = g });
                    open.Push(new OpenEntry { F = g + Heuristic(next), G = g, Order = order++, Node = nodes.Count - 1 });
                    stats.NodesGenerated++;
                }
            }

            return Failed(PlanStatus.NoPlan, start, stats);
        }

        private static int CompareEntries(OpenEntry a, OpenEntry b)
        {
            int byF = a.F.CompareTo(b.F);
            if (byF != 0) return byF;
            int byG = b.G.CompareTo(a.G); // equal f: prefer the deeper node, it is closer to a goal
            if (byG != 0) return byG;
            return a.Order.CompareTo(b.Order); // then first in, first out, so plans are deterministic
        }

        private static PlanResult Reconstruct(List<SearchNode> nodes, int goalNode, PlannerStats stats)
        {
            var actions = new List<PlanAction>();
            var states = new List<WorldState>();
            for (int i = goalNode; i != -1; i = nodes[i].Parent)
            {
                states.Add(nodes[i].State);
                if (nodes[i].Action != null) actions.Add(nodes[i].Action);
            }

            actions.Reverse();
            states.Reverse();
            return new PlanResult(PlanStatus.Found, actions, states, nodes[goalNode].G, stats);
        }

        private static PlanResult Failed(PlanStatus status, WorldState start, PlannerStats stats) =>
            new PlanResult(status, new List<PlanAction>(), new List<WorldState> { start }, float.PositiveInfinity, stats);
    }
}

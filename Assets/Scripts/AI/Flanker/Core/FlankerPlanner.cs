using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using LabLockdown.AI.Flanker.Goap;

namespace LabLockdown.AI.Flanker
{
    /// <summary>The STRIPS facts of the Flanker domain (bit indices in a <see cref="WorldState"/>).</summary>
    public static class FlankerFacts
    {
        /// <summary>Standing on a tile the player cannot see.</summary>
        public const int InCover = 0;

        /// <summary>Standing on a tile the player is watching.</summary>
        public const int Exposed = 1;

        /// <summary>In range of the player with line of sight.</summary>
        public const int AtAttackPosition = 2;

        /// <summary>At an attack position outside the player's view cone.</summary>
        public const int OnFlank = 3;

        /// <summary>The attack goal: the flanker is attacking the player.</summary>
        public const int PlayerEngaged = 4;

        /// <summary>Health at or below <see cref="FlankerSettings.LowHealth01"/>. Sensed only; no action changes it.</summary>
        public const int LowHealth = 5;

        public static readonly IReadOnlyList<string> Names = new[]
        {
            "InCover", "Exposed", "AtAttackPosition", "OnFlank", "PlayerEngaged", "LowHealth",
        };
    }

    public enum FlankerGoal
    {
        /// <summary>Attack the player (goal: PlayerEngaged).</summary>
        EngagePlayer,

        /// <summary>Low health, or no way to reach the player: get out of sight (goal: InCover).</summary>
        StayAlive,
    }

    public enum FlankerStepKind
    {
        /// <summary>Move to the nearest tile the player cannot see.</summary>
        TakeCover,

        /// <summary>Move, out of the player's view, to an attack position the player is not looking at.</summary>
        Flank,

        /// <summary>Move straight to an attack position the player is looking at.</summary>
        Assault,

        AttackFromFlank,
        AttackHeadOn,
    }

    /// <summary>One step of a flanker plan, with the tile route the Agent Controller's movement follows.</summary>
    public sealed class FlankerStep
    {
        internal FlankerStep(FlankerStepKind kind, int targetNode, int[] path, string via)
        {
            Kind = kind;
            TargetNode = targetNode;
            Path = path;
            Via = via;
        }

        public FlankerStepKind Kind { get; }

        /// <summary>Destination tile for moves; the player's tile for attacks.</summary>
        public int TargetNode { get; }

        /// <summary>Tile ids from where the step starts to <see cref="TargetNode"/>; empty for attacks.</summary>
        public IReadOnlyList<int> Path { get; }

        /// <summary>The areas the route passes through, e.g. "MaintenanceBay → Infirmary → WestCorridor".</summary>
        public string Via { get; }

        public bool IsMove => Kind == FlankerStepKind.TakeCover || Kind == FlankerStepKind.Flank || Kind == FlankerStepKind.Assault;

        public override string ToString() => string.IsNullOrEmpty(Via) ? Kind.ToString() : $"{Kind} via {Via}";
    }

    public sealed class FlankerDecision
    {
        internal FlankerDecision(FlankerGoal goal, bool usedFallback, PlanResult plan, IReadOnlyList<FlankerStep> steps,
            WorldState start, ThreatMap threat, FlankerPercepts percepts)
        {
            Goal = goal;
            UsedFallback = usedFallback;
            Plan = plan;
            Steps = steps;
            StartState = start;
            Threat = threat;
            Percepts = percepts;
        }

        public FlankerGoal Goal { get; }

        /// <summary>True when attacking was impossible (e.g. the player is behind locked doors) and the flanker fell back to hiding.</summary>
        public bool UsedFallback { get; }

        public PlanResult Plan { get; }
        public IReadOnlyList<FlankerStep> Steps { get; }
        public bool HasPlan => Plan.IsFound;
        public WorldState StartState { get; }
        public ThreatMap Threat { get; }

        /// <summary>The percepts the decision was made from (a copy).</summary>
        public FlankerPercepts Percepts { get; }

        /// <summary>One line for the debug overlay, e.g. "EngagePlayer: TakeCover → Flank via … → AttackFromFlank (cost 49.0)".</summary>
        public string Summary
        {
            get
            {
                string plan = !HasPlan ? $"no plan ({Plan.Status})"
                    : Steps.Count == 0 ? "hold (goal already holds)"
                    : string.Join(" → ", Steps);
                string cost = HasPlan && Steps.Count > 0 ? $" (cost {Plan.Cost:0.0})" : string.Empty;
                return $"{Goal}{(UsedFallback ? " [fallback]" : string.Empty)}: {plan}{cost}";
            }
        }
    }

    /// <summary>
    /// The Tactical Flanker's brain: goal-oriented action planning (GOAP).
    /// <list type="number">
    /// <item><b>Sense</b>: build a <see cref="ThreatMap"/> (which tiles the player sees and which it is looking at).</item>
    /// <item><b>Ground</b>: price the move actions with routes over the level graph (<see cref="RouteField"/>): the
    /// cheapest cover tile, the cheapest flank position by a hidden route, and the cheapest head-on position.</item>
    /// <item><b>Plan</b>: A* over the STRIPS actions (<see cref="GoapPlanner"/>) to the goal, e.g.
    /// TakeCover → Flank → AttackFromFlank.</item>
    /// </list>
    /// A flank route never crosses a tile the player holds, so when the player holds one route the flank goes round
    /// by the other one. The planner is pure C# (no Unity), so every behaviour is unit tested.
    /// </summary>
    public sealed class FlankerPlanner
    {
        private readonly LevelGraph graph;
        private readonly GoapPlanner planner = new GoapPlanner();

        public FlankerPlanner(LevelGraph graph, FlankerSettings settings = null)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            Settings = settings ?? new FlankerSettings();
            Settings.Validate();
        }

        public FlankerSettings Settings { get; }

        /// <summary>Planner options; switch to <see cref="PlannerOptions.UniformCost"/> to compare against A*.</summary>
        public PlannerOptions PlannerOptions { get; set; } = new PlannerOptions();

        public FlankerDecision Decide(FlankerPercepts percepts)
        {
            if (percepts == null) throw new ArgumentNullException(nameof(percepts));
            if (percepts.AgentNode < 0 || percepts.AgentNode >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(percepts), $"No node with id {percepts.AgentNode} for the flanker.");

            var snapshot = percepts.Clone();
            var threat = new ThreatMap(graph, snapshot, Settings);
            var start = StartState(snapshot, threat);
            var actions = GroundActions(snapshot, threat, start);

            if (!start.Has(FlankerFacts.LowHealth))
            {
                var engage = planner.Plan(start, new Goal(Facts.Bit(FlankerFacts.PlayerEngaged)), actions, PlannerOptions);
                if (engage.IsFound) return Build(FlankerGoal.EngagePlayer, false, engage, start, threat, snapshot);
            }

            // Low health, or no way to engage (e.g. the player is behind locked doors): get out of sight and wait.
            // The runtime re-plans when a door changes, the player moves, or health changes.
            var hide = planner.Plan(start, new Goal(Facts.Bit(FlankerFacts.InCover)), actions, PlannerOptions);
            return Build(FlankerGoal.StayAlive, !start.Has(FlankerFacts.LowHealth), hide, start, threat, snapshot);
        }

        /// <summary>
        /// False when the situation the plan was made for has changed: the player moved to another tile, doors
        /// locked or unlocked, health crossed the low-health threshold, or the player now holds a tile on a
        /// planned route. In each case the flanker should call <see cref="Decide"/> again.
        /// </summary>
        public bool IsStillValid(FlankerDecision decision, FlankerPercepts now)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (now == null) throw new ArgumentNullException(nameof(now));

            var then = decision.Percepts;
            if (now.PlayerNode != then.PlayerNode) return false;
            if (IsLow(now.AgentHealth01) != IsLow(then.AgentHealth01)) return false;
            if (!SameDoors(then.LockedDoors, now.LockedDoors)) return false;

            var threatNow = new ThreatMap(graph, now, Settings);
            foreach (var step in decision.Steps)
            {
                if (step.Kind != FlankerStepKind.TakeCover && step.Kind != FlankerStepKind.Flank) continue;
                foreach (int node in step.Path)
                    if (threatNow.IsWatched(node) && !decision.Threat.IsWatched(node)) return false; // the player turned to hold this route
            }

            return true;
        }

        private bool IsLow(float health01) => health01 <= Settings.LowHealth01;

        private WorldState StartState(FlankerPercepts p, ThreatMap threat)
        {
            int agent = p.AgentNode;
            bool atAttack = threat.IsAttackPosition(agent);
            return new WorldState()
                .With(FlankerFacts.InCover, threat.IsCover(agent))
                .With(FlankerFacts.Exposed, threat.IsWatched(agent))
                .With(FlankerFacts.AtAttackPosition, atAttack)
                .With(FlankerFacts.OnFlank, atAttack && !threat.IsWatched(agent))
                .With(FlankerFacts.LowHealth, IsLow(p.AgentHealth01));
        }

        private List<PlanAction> GroundActions(FlankerPercepts p, ThreatMap threat, WorldState start)
        {
            var actions = new List<PlanAction>();
            var fromAgent = RouteField.Compute(graph, p.AgentNode, p.LockedDoors, threat.Watched, Settings.ExposurePenalty);

            // TakeCover: the cheapest tile the player cannot see. Leaving cover is implied by the move actions.
            int cover = fromAgent.Cheapest(threat.IsCover);
            if (cover >= 0 && cover != p.AgentNode)
            {
                actions.Add(Move(FlankerStepKind.TakeCover, fromAgent, cover,
                    requireFalse: Facts.Mask(FlankerFacts.InCover),
                    add: Facts.Mask(FlankerFacts.InCover),
                    delete: Facts.Mask(FlankerFacts.Exposed, FlankerFacts.AtAttackPosition, FlankerFacts.OnFlank)));
            }

            // Flank: a hidden route (never through a tile the player holds) to an attack position the player is not
            // looking at. It only starts out of the player's view, so the player loses track of the flanker; that is
            // why an exposed flanker plans TakeCover first, and why the route is priced from the cover tile then.
            // Tiles the player holds are walls for this search, so a held route is never used: the flank goes
            // round by the other route or does not exist.
            int flankOrigin = start.Has(FlankerFacts.Exposed) && cover >= 0 ? cover : p.AgentNode;
            var hidden = RouteField.Compute(graph, flankOrigin, p.LockedDoors, threat.Watched, float.PositiveInfinity);

            int flank = hidden.Cheapest(n => n != flankOrigin && threat.IsFlankPosition(n));
            if (flank >= 0)
            {
                actions.Add(Move(FlankerStepKind.Flank, hidden, flank,
                    requireFalse: Facts.Mask(FlankerFacts.Exposed, FlankerFacts.AtAttackPosition, FlankerFacts.LowHealth),
                    add: Facts.Mask(FlankerFacts.AtAttackPosition, FlankerFacts.OnFlank),
                    delete: Facts.Mask(FlankerFacts.InCover)));
            }

            // Assault: straight to the cheapest attack position the player is looking at.
            int assault = fromAgent.Cheapest(n => n != p.AgentNode && threat.IsAttackPosition(n) && threat.IsWatched(n));
            if (assault >= 0)
            {
                actions.Add(Move(FlankerStepKind.Assault, fromAgent, assault,
                    requireFalse: Facts.Mask(FlankerFacts.AtAttackPosition, FlankerFacts.LowHealth),
                    add: Facts.Mask(FlankerFacts.AtAttackPosition, FlankerFacts.Exposed),
                    delete: Facts.Mask(FlankerFacts.InCover, FlankerFacts.OnFlank)));
            }

            actions.Add(new PlanAction(nameof(FlankerStepKind.AttackFromFlank), Settings.AttackCost,
                requireTrue: Facts.Mask(FlankerFacts.OnFlank),
                requireFalse: Facts.Mask(FlankerFacts.LowHealth),
                add: Facts.Mask(FlankerFacts.PlayerEngaged),
                data: new FlankerStep(FlankerStepKind.AttackFromFlank, p.PlayerNode, Array.Empty<int>(), null)));

            actions.Add(new PlanAction(nameof(FlankerStepKind.AttackHeadOn), Settings.AttackCost + Settings.HeadOnPenalty,
                requireTrue: Facts.Mask(FlankerFacts.AtAttackPosition),
                requireFalse: Facts.Mask(FlankerFacts.OnFlank, FlankerFacts.LowHealth),
                add: Facts.Mask(FlankerFacts.PlayerEngaged),
                data: new FlankerStep(FlankerStepKind.AttackHeadOn, p.PlayerNode, Array.Empty<int>(), null)));

            return actions;
        }

        private PlanAction Move(FlankerStepKind kind, RouteField field, int target, ulong requireFalse, ulong add, ulong delete)
        {
            int[] path = field.PathTo(target);
            var step = new FlankerStep(kind, target, path, DescribeRoute(path));
            float cost = Math.Max(field.CostTo(target), Settings.MinStepCost);
            return new PlanAction($"{kind}({graph.Nodes[target].Area})", cost,
                requireFalse: requireFalse, add: add, delete: delete, data: step);
        }

        private FlankerDecision Build(FlankerGoal goal, bool usedFallback, PlanResult plan, WorldState start,
            ThreatMap threat, FlankerPercepts percepts)
        {
            var steps = new List<FlankerStep>();
            foreach (var action in plan.Actions) steps.Add((FlankerStep)action.Data);
            return new FlankerDecision(goal, usedFallback, plan, steps, start, threat, percepts);
        }

        /// <summary>The areas along a route, without repeats: "Crossroads → MaintenanceBay → Infirmary".</summary>
        private string DescribeRoute(int[] path)
        {
            var areas = new List<string>();
            foreach (int node in path)
            {
                string area = graph.Nodes[node].Area;
                if (areas.Count == 0 || areas[areas.Count - 1] != area) areas.Add(area);
            }

            return string.Join(" → ", areas);
        }

        private static bool SameDoors(ICollection<string> a, ICollection<string> b)
        {
            int countA = a?.Count ?? 0, countB = b?.Count ?? 0;
            if (countA != countB) return false;
            if (countA == 0) return true;
            foreach (var door in a)
                if (!b.Contains(door)) return false;
            return true;
        }
    }
}

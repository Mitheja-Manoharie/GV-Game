using System;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// The Hunter's decision layer. Pure C#: the Unity side feeds it percepts and carries out its command.
    ///
    ///   noise / shot / sighting  -->  BeliefMap  -->  TargetSelector  -->  target node  -->  (A* on the Unity side)
    ///
    /// States:
    ///   Attack      the player is visible and in range: stand still, aim, shoot
    ///   Chase       the player is visible but out of range, or was seen less than ChaseMemorySeconds ago
    ///   Investigate the belief has a clear peak (the Hunter heard something): walk to the likeliest place
    ///   Patrol      the belief is flat: sweep the level (nodes searched recently are unlikely, stale ones recover)
    ///   Dead        health gone
    /// The state follows from the belief, not from a script, and Investigate/Patrol use hysteresis
    /// (enter at a higher peak than exit) so the Hunter does not flap at the boundary.
    ///
    /// The brain never reads the player's true position. It only sees it when HasLineOfSight is true.
    /// </summary>
    public sealed class HunterBrain
    {
        private readonly LevelGraph graph;
        private readonly HunterBrainParameters parameters;
        private readonly SoundModel sound;
        private readonly EdgeCostModel edgeCosts;
        private readonly AttackModel attack;
        private readonly DoorStateTable openDoors = new DoorStateTable(DoorState.Open);

        private float lastNow;
        private bool hasThought;
        private int lastHunterNode = -1;
        private float lastSeenTime;
        private bool hasSeen;
        private float lastReselect;
        private bool forceReselect = true;

        public HunterBrain(LevelGraph graph, BeliefMap belief, SoundModel sound, EdgeCostModel edgeCosts,
                           AttackModel attack, HunterBrainParameters parameters = null)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            Belief = belief ?? throw new ArgumentNullException(nameof(belief));
            this.sound = sound ?? throw new ArgumentNullException(nameof(sound));
            this.edgeCosts = edgeCosts ?? throw new ArgumentNullException(nameof(edgeCosts));
            this.attack = attack ?? throw new ArgumentNullException(nameof(attack));
            this.parameters = parameters ?? new HunterBrainParameters();

            if (belief.Count != graph.NodeCount)
                throw new ArgumentException("The belief must have one entry per graph node.", nameof(belief));

            State = HunterState.Patrol;
            TargetNode = -1;
        }

        public BeliefMap Belief { get; }
        public HunterState State { get; private set; }
        public int TargetNode { get; private set; }
        public AttackModel Attack => attack;

        /// <summary>
        /// A noise happened at sourceNode. The Hunter hears it only if enough loudness survives the trip through
        /// the level (closed doors muffle it). Belief spreads over walking distance with every door treated as
        /// open: sound passes a locked door and the player may be anywhere on its far side.
        /// </summary>
        public void OnNoise(int sourceNode, float loudness, IDoorStateProvider doors)
        {
            if (doors == null) throw new ArgumentNullException(nameof(doors));
            if (lastHunterNode < 0) return;   // has not been placed in the level yet

            float[] level = sound.Propagate(graph, sourceNode, loudness, doors);
            float heard = level[lastHunterNode];
            if (heard <= 0f) return;

            float[] distance = CostField.From(graph, edgeCosts, sourceNode, openDoors);
            Belief.ApplyNoise(distance, heard, loudness);
            forceReselect = true;
        }

        /// <summary>The Hunter was shot from shooterNode: the shooter's position is known fairly well.</summary>
        public void OnShot(int shooterNode)
        {
            float[] distance = CostField.From(graph, edgeCosts, shooterNode, openDoors);
            Belief.ApplyShot(distance);
            forceReselect = true;
        }

        public HunterCommand Think(HunterPercepts p, IDoorStateProvider doors)
        {
            if (doors == null) throw new ArgumentNullException(nameof(doors));
            if (p.HunterNode < 0 || p.HunterNode >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(p), "HunterNode is not in the graph.");

            float dt = hasThought ? Math.Max(0f, p.Now - lastNow) : 0f;
            hasThought = true;
            lastNow = p.Now;

            if (!p.IsAlive)
            {
                State = HunterState.Dead;
                TargetNode = -1;
                attack.Reset();
                return Command(HunterState.Dead, false, true, default(AttackResult));
            }

            Belief.Decay(dt);

            bool sees = p.HasLineOfSight;
            if (sees)
            {
                Belief.ApplySighting(p.PlayerNode);
                lastSeenTime = p.Now;
                hasSeen = true;
                if (p.PlayerNode != TargetNode) forceReselect = true;
            }

            // Walking into a node without seeing the player there means the player is probably not there.
            if (p.HunterNode != lastHunterNode)
            {
                if (!sees) Belief.MarkSearched(p.HunterNode);
                lastHunterNode = p.HunterNode;
                if (p.HunterNode == TargetNode)
                {
                    TargetNode = -1;
                    forceReselect = true;
                }
            }

            AttackResult shot = attack.Update(p.Now, sees, sees ? p.PlayerDistance : float.MaxValue);
            bool engaged = sees && p.PlayerDistance <= attack.Parameters.Range;

            HunterState next;
            if (engaged)
            {
                next = HunterState.Attack;
            }
            else if (sees || (hasSeen && p.Now - lastSeenTime <= parameters.ChaseMemorySeconds))
            {
                next = HunterState.Chase;
            }
            else
            {
                float ratio = Belief.MaxProbability * Belief.Count;   // how many times above a flat belief
                float needed = State == HunterState.Investigate ? parameters.InvestigateExitRatio : parameters.InvestigateEnterRatio;
                next = ratio >= needed ? HunterState.Investigate : HunterState.Patrol;
            }
            State = next;

            bool targetChanged = false;
            if (next != HunterState.Attack)
            {
                bool due = forceReselect || TargetNode < 0 || p.Now - lastReselect >= parameters.ReselectInterval;
                if (due)
                {
                    targetChanged = Reselect(p, sees, doors);
                    lastReselect = p.Now;
                    forceReselect = false;
                }
            }

            return Command(next, targetChanged, next == HunterState.Attack, shot);
        }

        private bool Reselect(HunterPercepts p, bool sees, IDoorStateProvider doors)
        {
            float[] cost = CostField.From(graph, edgeCosts, p.HunterNode, doors);
            int candidate = -1;
            float utility = 0f;

            // If the likeliest place is the node the Hunter is already standing in and the player is not in
            // sight, that node has just been checked: lower it and look again (bounded, so it cannot loop).
            for (int attempt = 0; attempt < 3; attempt++)
            {
                candidate = TargetSelector.Choose(Belief, cost, out utility);
                if (candidate == p.HunterNode && !sees) Belief.MarkSearched(p.HunterNode);
                else break;
            }

            if (candidate < 0)
            {
                bool changed = TargetNode != -1;
                TargetNode = -1;
                return changed;
            }
            if (candidate == TargetNode) return false;

            // Hysteresis: only abandon the current target if the new one is clearly better.
            if (TargetNode >= 0 && !float.IsPositiveInfinity(cost[TargetNode]))
            {
                float current = Belief[TargetNode] / (cost[TargetNode] + TargetSelector.DefaultEpsilon);
                if (utility <= current * (1f + parameters.SwitchMargin)) return false;
            }

            TargetNode = candidate;
            return true;
        }

        private HunterCommand Command(HunterState state, bool targetChanged, bool hold, AttackResult attackResult)
        {
            HunterGait gait = state == HunterState.Investigate ? HunterGait.Investigate
                            : state == HunterState.Chase || state == HunterState.Attack ? HunterGait.Chase
                            : HunterGait.Patrol;

            return new HunterCommand
            {
                State = state,
                Gait = gait,
                TargetNode = TargetNode,
                TargetChanged = targetChanged,
                HoldPosition = hold,
                Attack = attackResult
            };
        }
    }
}

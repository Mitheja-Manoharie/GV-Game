using System;
using System.Collections.Generic;
using System.Linq;
using LabLockdown.AI.Facility;
using LabLockdown.AI.Flanker.Goap;
using NUnit.Framework;

namespace LabLockdown.AI.Flanker.Tests
{
    public class FlankerPlannerTests
    {
        private static readonly (float, float) East = (1f, 0f), West = (-1f, 0f), North = (0f, 1f), South = (0f, -1f);

        private LevelGraph graph;
        private FlankerPlanner planner;

        [SetUp]
        public void SetUp()
        {
            graph = TestLevel.Graph();
            planner = new FlankerPlanner(graph);
        }

        private FlankerDecision Decide((float, float) agent, (float, float) player, (float, float) facing,
            float health01 = 1f, params string[] locked) =>
            planner.Decide(TestLevel.Percepts(graph, agent, player, facing, health01, locked));

        private static FlankerStepKind[] Kinds(FlankerDecision decision) => decision.Steps.Select(s => s.Kind).ToArray();

        private List<string> Areas(FlankerStep step) => step.Path.Select(n => graph.Nodes[n].Area).ToList();

        // ---------- The two-route behaviour ----------

        [Test]
        public void PlayerHoldsTheLabRoute_FlankerGoesRoundThroughTheInfirmary()
        {
            // Player in the control room looking east through the east door: the lab route is held.
            var decision = Decide((10f, 14f), (2f, 30f), East);

            Assert.AreEqual(FlankerGoal.EngagePlayer, decision.Goal);
            CollectionAssert.AreEqual(new[] { FlankerStepKind.Flank, FlankerStepKind.AttackFromFlank }, Kinds(decision));

            var flank = decision.Steps[0];
            CollectionAssert.Contains(Areas(flank), "Infirmary");
            CollectionAssert.DoesNotContain(Areas(flank), "ResearchLab");
            Assert.IsTrue(decision.Threat.IsFlankPosition(flank.TargetNode));
            foreach (int node in flank.Path) Assert.IsFalse(decision.Threat.IsWatched(node), $"{graph.Nodes[node]} is held by the player");
        }

        [Test]
        public void PlayerTurnsToHoldTheOtherRoute_PlanIsInvalidated_AndTheFlankerSwitchesRoutes()
        {
            var first = Decide((10f, 14f), (2f, 30f), East);
            var turned = TestLevel.Percepts(graph, (10f, 14f), (2f, 30f), West);

            Assert.IsFalse(planner.IsStillValid(first, turned), "The player now holds the planned route");

            var second = planner.Decide(turned);
            CollectionAssert.AreEqual(new[] { FlankerStepKind.Flank, FlankerStepKind.AttackFromFlank }, Kinds(second));
            CollectionAssert.Contains(Areas(second.Steps[0]), "ResearchLab");
            CollectionAssert.DoesNotContain(Areas(second.Steps[0]), "Infirmary");
        }

        [Test]
        public void ExposedFlanker_TakesCoverBeforeFlanking()
        {
            // The player, in the south-west corner of the maintenance bay, looks east and sees the flanker in the
            // middle of the room. It ducks west into the infirmary, out of sight, then comes back to the
            // infirmary corridor beside the player, outside the view cone, and attacks from there.
            var decision = Decide((2f, 14f), (-2f, 10f), East);

            Assert.IsTrue(decision.StartState.Has(FlankerFacts.Exposed));
            CollectionAssert.AreEqual(
                new[] { FlankerStepKind.TakeCover, FlankerStepKind.Flank, FlankerStepKind.AttackFromFlank }, Kinds(decision));

            var cover = decision.Steps[0];
            var flank = decision.Steps[1];
            Assert.IsTrue(decision.Threat.IsCover(cover.TargetNode));
            Assert.AreEqual(cover.TargetNode, flank.Path[0], "The flank starts from the cover tile");
            Assert.AreEqual("Infirmary", graph.Nodes[cover.TargetNode].Area);
            Assert.AreEqual("InfirmaryCorridor", graph.Nodes[flank.TargetNode].Area);
        }

        [Test]
        public void AlreadyBehindThePlayer_AttacksStraightAway()
        {
            var decision = Decide((-6f, 30f), (2f, 30f), East);

            Assert.IsTrue(decision.StartState.Has(FlankerFacts.OnFlank));
            CollectionAssert.AreEqual(new[] { FlankerStepKind.AttackFromFlank }, Kinds(decision));
        }

        [Test]
        public void NoHiddenRouteLeft_AttacksHeadOn()
        {
            // The infirmary door is locked, so the only way to the player is the lab route the player is holding.
            var decision = Decide((10f, 14f), (2f, 30f), East, 1f, LabLockdownLayout.DoorInfirmaryNorth);

            CollectionAssert.AreEqual(new[] { FlankerStepKind.Assault, FlankerStepKind.AttackHeadOn }, Kinds(decision));
            CollectionAssert.Contains(Areas(decision.Steps[0]), "ResearchLab");
        }

        // ---------- Goals and fallbacks ----------

        [Test]
        public void LowHealth_SeeksCover_AndNeverAttacks()
        {
            var decision = Decide((18f, 30f), (2f, 30f), East, 0.2f);

            Assert.AreEqual(FlankerGoal.StayAlive, decision.Goal);
            Assert.IsFalse(decision.UsedFallback);
            CollectionAssert.AreEqual(new[] { FlankerStepKind.TakeCover }, Kinds(decision));
            Assert.IsTrue(decision.Threat.IsCover(decision.Steps[0].TargetNode));
        }

        [Test]
        public void PlayerBehindLockedDoors_FlankerHoldsInCover()
        {
            var decision = Decide((10f, 14f), (2f, 30f), East, 1f,
                LabLockdownLayout.DoorControlWest, LabLockdownLayout.DoorControlEast);

            Assert.AreEqual(FlankerGoal.StayAlive, decision.Goal);
            Assert.IsTrue(decision.UsedFallback);
            Assert.IsTrue(decision.HasPlan);
            CollectionAssert.IsEmpty(decision.Steps, "Already in cover: hold until a door opens");
        }

        // ---------- Re-planning ----------

        [Test]
        public void Plan_StaysValid_UntilSomethingRelevantChanges()
        {
            var percepts = TestLevel.Percepts(graph, (10f, 14f), (2f, 30f), East);
            var decision = planner.Decide(percepts);
            Assert.IsTrue(planner.IsStillValid(decision, percepts), "Nothing changed");

            var moved = TestLevel.Percepts(graph, (10f, 14f), (6f, 30f), East);
            Assert.IsFalse(planner.IsStillValid(decision, moved), "The player moved to another tile");

            var locked = TestLevel.Percepts(graph, (10f, 14f), (2f, 30f), East, 1f, LabLockdownLayout.DoorLabSouth);
            Assert.IsFalse(planner.IsStillValid(decision, locked), "A door was locked");

            var hurt = TestLevel.Percepts(graph, (10f, 14f), (2f, 30f), East, 0.1f);
            Assert.IsFalse(planner.IsStillValid(decision, hurt), "Health dropped below the threshold");
        }

        [Test]
        public void Decision_KeepsACopyOfItsPercepts()
        {
            var percepts = TestLevel.Percepts(graph, (10f, 14f), (2f, 30f), East);
            var decision = planner.Decide(percepts);

            percepts.LockedDoors.Add(LabLockdownLayout.DoorLabSouth);
            percepts.PlayerNode = TestLevel.Node(graph, 6f, 30f);

            Assert.AreEqual(0, decision.Percepts.LockedDoors.Count);
            Assert.AreEqual(TestLevel.Node(graph, 2f, 30f), decision.Percepts.PlayerNode);
        }

        // ---------- Every situation ----------

        /// <summary>
        /// Every flanker tile × every player tile × 8 facings, with random locked doors and health: 15,840 decisions.
        /// After each one, checks the rules the flanker must never break.
        /// </summary>
        [Test]
        public void EverySituation_PlansAreValidAndFollowTheRules()
        {
            var random = new Random(3062);
            var doors = LabLockdownLayout.Doors.Select(d => d.Id).ToArray();
            var facings = Enumerable.Range(0, 8)
                .Select(i => (MathF.Cos(i * MathF.PI / 4f), MathF.Sin(i * MathF.PI / 4f))).ToArray();
            int flanks = 0, headOn = 0, total = 0;

            foreach (var player in graph.Nodes)
            {
                foreach (var agent in graph.Nodes)
                {
                    if (agent.Id == player.Id) continue;
                    var locked = doors.Where(_ => random.NextDouble() < 0.2).ToArray();
                    var visible = TestLevel.VisibleFrom(graph, player.X, player.Z, locked);

                    foreach (var (fx, fz) in facings)
                    {
                        var percepts = new FlankerPercepts
                        {
                            AgentNode = agent.Id, PlayerNode = player.Id, PlayerX = player.X, PlayerZ = player.Z,
                            PlayerForwardX = fx, PlayerForwardZ = fz, VisibleNodes = visible,
                            AgentHealth01 = random.NextDouble() < 0.1 ? 0.2f : 1f,
                            LockedDoors = new HashSet<string>(locked),
                        };

                        var decision = planner.Decide(percepts);
                        string where = $"agent {agent}, player {player}, facing ({fx:0.0}, {fz:0.0}), locked [{string.Join(",", locked)}]";
                        CheckRules(decision, percepts, where);

                        total++;
                        if (decision.Steps.Any(s => s.Kind == FlankerStepKind.AttackFromFlank)) flanks++;
                        if (decision.Steps.Any(s => s.Kind == FlankerStepKind.AttackHeadOn)) headOn++;
                    }
                }
            }

            Assert.AreEqual(45 * 44 * 8, total);
            Assert.Greater(flanks, headOn * 2, "A flanker should mostly flank");
            Assert.Greater(headOn, 0, "…but still fight head-on when no hidden route exists");
        }

        private void CheckRules(FlankerDecision decision, FlankerPercepts percepts, string where)
        {
            var threat = decision.Threat;
            bool lowHealth = percepts.AgentHealth01 <= planner.Settings.LowHealth01;

            // Low health never attacks; a found engage plan always ends with exactly one attack.
            if (lowHealth) Assert.AreEqual(FlankerGoal.StayAlive, decision.Goal, where);
            if (decision.Goal == FlankerGoal.EngagePlayer)
            {
                Assert.IsTrue(decision.HasPlan, where);
                var last = decision.Steps[decision.Steps.Count - 1].Kind;
                Assert.IsTrue(last == FlankerStepKind.AttackFromFlank || last == FlankerStepKind.AttackHeadOn, where);
                Assert.AreEqual(1, decision.Steps.Count(s => !s.IsMove), where);
            }
            else
            {
                Assert.IsTrue(decision.Steps.All(s => s.IsMove), where);
            }

            // Moves chain up: each starts where the previous one ended, uses neighbouring tiles and no locked door.
            int at = percepts.AgentNode;
            foreach (var step in decision.Steps.Where(s => s.IsMove))
            {
                Assert.AreEqual(at, step.Path[0], where);
                Assert.AreEqual(step.TargetNode, step.Path[step.Path.Count - 1], where);
                for (int i = 0; i + 1 < step.Path.Count; i++)
                {
                    var edge = graph.EdgesOf(step.Path[i]).Single(e => e.Other(step.Path[i]) == step.Path[i + 1]);
                    Assert.IsTrue(LevelGraph.IsPassable(edge, percepts.LockedDoors), $"{where}: through locked {edge.DoorId}");
                }

                switch (step.Kind)
                {
                    case FlankerStepKind.TakeCover:
                        Assert.IsTrue(threat.IsCover(step.TargetNode), where);
                        break;
                    case FlankerStepKind.Flank:
                        Assert.IsTrue(threat.IsFlankPosition(step.TargetNode), where);
                        Assert.IsTrue(step.Path.All(n => !threat.IsWatched(n)), $"{where}: flank crosses a held tile");
                        break;
                    case FlankerStepKind.Assault:
                        Assert.IsTrue(threat.IsAttackPosition(step.TargetNode) && threat.IsWatched(step.TargetNode), where);
                        break;
                }

                at = step.TargetNode;
            }

            // A flank only starts out of the player's view.
            for (int i = 0; i < decision.Steps.Count; i++)
                if (decision.Steps[i].Kind == FlankerStepKind.Flank)
                    Assert.IsFalse(decision.Plan.States[i].Has(FlankerFacts.Exposed), where);
        }

        [Test]
        public void AStar_And_UniformCost_ChooseEquallyGoodPlans()
        {
            var uniform = new FlankerPlanner(graph) { PlannerOptions = PlannerOptions.UniformCost() };
            foreach (var player in graph.Nodes.Where((_, i) => i % 3 == 0))
            {
                var visible = TestLevel.VisibleFrom(graph, player.X, player.Z);
                foreach (var agent in graph.Nodes)
                {
                    if (agent.Id == player.Id) continue;
                    foreach (var facing in new[] { East, West, North, South })
                    {
                        var percepts = new FlankerPercepts
                        {
                            AgentNode = agent.Id, PlayerNode = player.Id, PlayerX = player.X, PlayerZ = player.Z,
                            PlayerForwardX = facing.Item1, PlayerForwardZ = facing.Item2, VisibleNodes = visible,
                        };

                        var a = planner.Decide(percepts);
                        var b = uniform.Decide(percepts);
                        Assert.AreEqual(b.Plan.Cost, a.Plan.Cost, 1e-3f);
                        Assert.LessOrEqual(a.Plan.Stats.NodesExpanded, b.Plan.Stats.NodesExpanded);
                    }
                }
            }
        }

        [Test]
        public void InvalidInput_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new FlankerPlanner(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlankerPlanner(graph, new FlankerSettings { AttackRange = 0f }));
            Assert.Throws<ArgumentNullException>(() => planner.Decide(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => planner.Decide(new FlankerPercepts { AgentNode = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => planner.Decide(new FlankerPercepts { PlayerNode = 999 }));
        }
    }
}

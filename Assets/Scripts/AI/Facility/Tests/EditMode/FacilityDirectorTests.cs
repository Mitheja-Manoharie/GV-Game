using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Director;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    public class FacilityDirectorTests
    {
        private LevelGraph graph;
        private FacilityLevel level;
        private List<int> candidates;

        [SetUp]
        public void SetUp()
        {
            graph = LabLockdownLayout.CreateGraph();
            candidates = new List<int>();
            foreach (var spawn in LabLockdownLayout.DefaultSpawnPoints)
                candidates.Add(graph.FindNearestNode(spawn.X, spawn.Z));

            int exit = graph.FindNearestNode(LabLockdownLayout.ExitX, LabLockdownLayout.ExitZ);
            int start = graph.FindNearestNode(LabLockdownLayout.PlayerStartX, LabLockdownLayout.PlayerStartZ);
            level = new FacilityLevel(graph, candidates, exit, start);
        }

        private int Node(float x, float z) => graph.FindNearestNode(x, z);

        private FacilityDirector NewDirector(DirectorSettings settings = null) => new FacilityDirector(level, settings ?? new DirectorSettings());

        /// <summary>A healthy, calm player who has made progress: the Director should push hard.</summary>
        private FacilityPercepts PeakPercepts(int playerNode, double time = 100) => new FacilityPercepts
        {
            Time = time,
            PlayerNode = playerNode,
            Health01 = 1f,
            CalmSeconds = 30f,
        };

        // ---------- Timing ----------

        [Test]
        public void Tick_WaitsForTheFirstDecisionInterval()
        {
            var director = NewDirector();
            var percepts = PeakPercepts(Node(10, 14), time: 1);
            Assert.IsNull(director.Tick(percepts));

            percepts.Time = director.Settings.DecisionIntervalCalm;
            Assert.IsNotNull(director.Tick(percepts));
        }

        // ---------- Spawn constraints ----------

        [Test]
        public void Peak_SpawnsAFullWave_ThatObeysEveryRule()
        {
            int player = Node(10, 14); // crossroads
            var decision = NewDirector().Decide(PeakPercepts(player));

            Assert.AreEqual(DirectorPhase.Peak, decision.Evaluation.Phase);
            Assert.AreEqual(decision.RequestedSpawns, decision.SpawnNodes.Count);
            AssertDecisionIsFair(decision, PeakPercepts(player), new DirectorSettings());
        }

        [Test]
        public void VisibleSpawnPoints_AreNeverUsed()
        {
            int player = Node(10, 14);
            var percepts = PeakPercepts(player);
            foreach (int node in candidates) percepts.VisibleNodes.Add(node);
            int hidden = Node(-14, 26);
            percepts.VisibleNodes.Remove(hidden);

            var decision = NewDirector().Decide(percepts);

            CollectionAssert.AreEqual(new[] { hidden }, decision.SpawnNodes);
            StringAssert.Contains("Only 1 of", string.Join(" | ", decision.Notes));
        }

        [Test]
        public void NoSpawnIsPossible_DegradesToZeroSpawns_WithoutFallback()
        {
            var percepts = PeakPercepts(Node(10, 14));
            foreach (int node in candidates) percepts.VisibleNodes.Add(node);

            var decision = NewDirector().Decide(percepts);

            Assert.AreEqual(0, decision.SpawnNodes.Count);
            Assert.IsFalse(decision.UsedFallback);
        }

        // ---------- Doors ----------

        [Test]
        public void Peak_LocksOnlyDoorsThatLengthenThePlayersRoute()
        {
            int player = Node(14, 18); // lab corridor, heading for Door_LabSouth
            var decision = NewDirector().Decide(PeakPercepts(player));
            var baseRoute = graph.ShortestDistances(player)[level.ExitNode];

            Assert.Greater(decision.DoorsToLock.Count, 0, "A peak should use at least one door");
            foreach (string door in decision.DoorsToLock)
            {
                var route = graph.ShortestDistances(player, new HashSet<string> { door })[level.ExitNode];
                Assert.Greater(route, baseRoute, $"{door} was locked but does not change the route");
            }
        }

        [Test]
        public void LowHealth_OpensEveryDoor_EvenDuringCooldown()
        {
            var director = NewDirector();
            int player = Node(14, 18);
            var first = director.Decide(PeakPercepts(player));
            Assume.That(first.DoorsToLock.Count > 0);

            var hurt = PeakPercepts(Node(10, 14), time: 102);
            hurt.Health01 = 0.1f;
            foreach (string door in first.DoorsToLock) hurt.LockedDoors.Add(door);

            var decision = director.Decide(hurt);

            Assert.AreEqual(DirectorPhase.Relief, decision.Evaluation.Phase);
            Assert.AreEqual(0, decision.SpawnNodes.Count);
            CollectionAssert.AreEquivalent(first.DoorsToLock, decision.DoorsToUnlock);
        }

        [Test]
        public void Cooldown_KeepsALockedDoorLocked_WhenPressureDrops()
        {
            var director = NewDirector();
            int player = Node(14, 18);
            var first = director.Decide(PeakPercepts(player));
            Assume.That(first.DoorsToLock.Count > 0);

            // 5 s later, the player is slightly hurt (not in relief) and pressure is low.
            var later = new FacilityPercepts { Time = 105, PlayerNode = Node(10, 14), Health01 = 0.6f, RecentDamage01 = 0.5f };
            foreach (string door in first.DoorsToLock) later.LockedDoors.Add(door);

            var decision = director.Decide(later);

            Assert.IsFalse(decision.Evaluation.LocksAllowed);
            foreach (string door in first.DoorsToLock) Assert.IsTrue(decision.DoorLocks[door], $"{door} should stay locked during its cooldown");
        }

        [Test]
        public void LockedTooLong_IsReopened()
        {
            var settings = new DirectorSettings();
            var director = NewDirector(settings);
            int player = Node(14, 18);
            var first = director.Decide(PeakPercepts(player));
            Assume.That(first.DoorsToLock.Count > 0);

            var later = PeakPercepts(Node(10, 14), time: 100 + settings.MaxLockSeconds + 1);
            foreach (string door in first.DoorsToLock) later.LockedDoors.Add(door);

            var decision = director.Decide(later);
            foreach (string door in first.DoorsToLock) Assert.IsFalse(decision.DoorLocks[door], $"{door} exceeded the maximum lock time");
        }

        [Test]
        public void DoorNextToThePlayer_IsNeverLocked()
        {
            int player = Node(14, 22); // standing at Door_LabSouth
            var decision = NewDirector().Decide(PeakPercepts(player));
            Assert.IsFalse(decision.DoorLocks[LabLockdownLayout.DoorLabSouth]);
        }

        // ---------- Pacing ----------

        [Test]
        public void PeakWave_IsFollowedByRelief()
        {
            var director = NewDirector();
            var peak = director.Decide(PeakPercepts(Node(10, 14)));
            Assume.That(peak.Evaluation.Phase == DirectorPhase.Peak && peak.SpawnNodes.Count > 0);

            var next = director.Decide(PeakPercepts(Node(10, 14), time: 105));
            Assert.AreEqual(DirectorPhase.Relief, next.Evaluation.Phase);
            Assert.AreEqual(0, next.SpawnNodes.Count);
        }

        [Test]
        public void RecentSpawnPoints_ArePenalised()
        {
            var settings = new DirectorSettings { ReliefAfterPeakSeconds = 0 };
            var director = NewDirector(settings);
            int player = Node(10, 14);

            var first = director.Decide(PeakPercepts(player));
            var second = director.Decide(PeakPercepts(player, time: 200));

            Assume.That(first.SpawnNodes.Count > 0 && second.SpawnNodes.Count > 0);
            CollectionAssert.AreNotEquivalent(first.SpawnNodes, second.SpawnNodes);
        }

        // ---------- Utility ----------

        [Test]
        public void FlankScore_PrefersBehindAtHighIntensity_AndAheadAtLowIntensity()
        {
            int player = Node(14, 14);  // east crossroads tile: 28 m from the exit
            int behind = Node(14, 10);  // service corridor: 32 m, further from the exit
            int ahead = Node(14, 22);   // lab corridor: 20 m, closer to the exit
            Assume.That(level.ExitDistances[behind] > level.ExitDistances[player]);
            Assume.That(level.ExitDistances[ahead] < level.ExitDistances[player]);

            var settings = new DirectorSettings { DistanceUtilityWeight = 0, VarietyUtilityWeight = 0 };
            var percepts = PeakPercepts(player);
            var problem = FacilityProblem.Build(level, percepts, new IntensityEvaluation { Intensity = 0.9f }, settings, 1,
                new Dictionary<string, DoorMemory>(), new List<int>());
            var playerDistance = graph.ShortestDistances(player);

            var objective = problem.Objective;
            Assert.Greater(objective.SpawnBaseScore(behind, player, playerDistance, 0.9f, null),
                           objective.SpawnBaseScore(ahead, player, playerDistance, 0.9f, null));
            Assert.Less(objective.SpawnBaseScore(behind, player, playerDistance, 0.2f, null),
                        objective.SpawnBaseScore(ahead, player, playerDistance, 0.2f, null));
        }

        [Test]
        public void Decisions_AreDeterministic()
        {
            var a = NewDirector().Decide(PeakPercepts(Node(14, 18)));
            var b = NewDirector().Decide(PeakPercepts(Node(14, 18)));
            CollectionAssert.AreEqual(a.SpawnNodes, b.SpawnNodes);
            CollectionAssert.AreEquivalent(a.DoorsToLock, b.DoorsToLock);
        }

        // ---------- The big one: fairness can never be broken ----------

        [Test]
        public void RandomGames_NeverBreakTheFairnessRules()
        {
            var random = new Random(3032);
            var settings = new DirectorSettings();
            int decisions = 0, spawned = 0, locks = 0;

            for (int game = 0; game < 25; game++)
            {
                var director = NewDirector(settings);
                var locked = new HashSet<string>();
                double time = 0;

                for (int step = 0; step < 20; step++)
                {
                    time += 2 + random.NextDouble() * 8;
                    var percepts = new FacilityPercepts
                    {
                        Time = time,
                        PlayerNode = random.Next(graph.NodeCount),
                        Health01 = (float)random.NextDouble(),
                        RecentDamage01 = (float)(random.NextDouble() * 0.5),
                        CalmSeconds = (float)(random.NextDouble() * 40),
                        EnemiesAlive = random.Next(0, 7),
                        LockedDoors = new HashSet<string>(locked),
                    };
                    foreach (int node in candidates)
                    {
                        if (random.NextDouble() < 0.3) percepts.VisibleNodes.Add(node);
                        if (random.NextDouble() < 0.15) percepts.OccupiedSpawnNodes.Add(node);
                    }
                    foreach (string door in settings.LockableDoors)
                        if (!locked.Contains(door) && random.NextDouble() < 0.15) percepts.ObstructedDoors.Add(door);

                    var decision = director.Decide(percepts);
                    AssertDecisionIsFair(decision, percepts, settings);

                    locked.Clear();
                    foreach (var door in decision.DoorLocks)
                        if (door.Value) locked.Add(door.Key);

                    decisions++;
                    spawned += decision.SpawnNodes.Count;
                    locks += decision.DoorsToLock.Count;
                }
            }

            // The rules must not be satisfied by doing nothing. Many random states are relief states
            // (low health, safe areas), so on average at least one robot per four decisions is expected.
            Assert.Greater(spawned, decisions / 4, "The Director should still spawn regularly");
            Assert.Greater(locks, 0, "The Director should lock doors at least sometimes");
        }

        private void AssertDecisionIsFair(DirectorDecision decision, FacilityPercepts percepts, DirectorSettings settings)
        {
            var lockedDoors = new HashSet<string>();
            foreach (var door in decision.DoorLocks)
                if (door.Value) lockedDoors.Add(door.Key);

            string context = $"player {graph.Nodes[percepts.PlayerNode]}, {decision}";
            Assert.LessOrEqual(lockedDoors.Count, settings.MaxLockedDoors, context);
            Assert.IsTrue(graph.IsReachable(percepts.PlayerNode, level.ExitNode, lockedDoors), "Exit cut off: " + context);

            float[] playerDistance = graph.ShortestDistances(percepts.PlayerNode);
            foreach (string door in lockedDoors)
            {
                var edge = graph.FindDoorEdge(door);
                Assert.Greater(Math.Min(playerDistance[edge.A], playerDistance[edge.B]), edge.Cost, $"{door} locked next to the player: {context}");
                Assert.IsFalse(percepts.ObstructedDoors.Contains(door), $"{door} locked while obstructed: {context}");
            }

            var seen = new HashSet<int>();
            foreach (int node in decision.SpawnNodes)
            {
                var tile = graph.Nodes[node];
                Assert.IsTrue(seen.Add(node), "Duplicate spawn: " + context);
                Assert.IsFalse(percepts.VisibleNodes.Contains(node), "Visible spawn: " + context);
                Assert.IsFalse(percepts.OccupiedSpawnNodes.Contains(node), "Occupied spawn: " + context);
                Assert.IsFalse(LabLockdownLayout.IsSafeArea(tile.Area), "Spawn in safe area: " + context);
                Assert.AreNotEqual(graph.Nodes[percepts.PlayerNode].Area, tile.Area, "Spawn in player's area: " + context);
                Assert.That(playerDistance[node], Is.InRange(settings.MinSpawnDistance, settings.MaxSpawnDistance), "Distance: " + context);
                Assert.IsTrue(graph.IsReachable(node, percepts.PlayerNode, lockedDoors), "Spawn cannot reach player: " + context);
            }

            Assert.LessOrEqual(decision.SpawnNodes.Count, settings.MaxSpawnPerWave, context);
        }
    }
}

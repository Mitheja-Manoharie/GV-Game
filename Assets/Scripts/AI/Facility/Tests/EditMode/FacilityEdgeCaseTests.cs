using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Director;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    /// <summary>Week 4: situations the first version of the Director did not handle.</summary>
    public class FacilityEdgeCaseTests
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

        private FacilityDirector NewDirector() => new FacilityDirector(level, new DirectorSettings());

        private static FacilityPercepts Calm(int playerNode, double time = 100, float calmSeconds = 30f) => new FacilityPercepts
        {
            Time = time,
            PlayerNode = playerNode,
            Health01 = 1f,
            CalmSeconds = calmSeconds,
        };

        [Test]
        public void OccupiedSpawnPoints_AreNotUsed()
        {
            int player = Node(10, 14);
            var first = NewDirector().Decide(Calm(player));
            Assume.That(first.SpawnNodes.Count > 0);

            var percepts = Calm(player);
            foreach (int node in first.SpawnNodes) percepts.OccupiedSpawnNodes.Add(node);
            var decision = NewDirector().Decide(percepts);

            Assert.Greater(decision.SpawnNodes.Count, 0, "Other free spawn points exist");
            foreach (int node in decision.SpawnNodes)
                Assert.IsFalse(percepts.OccupiedSpawnNodes.Contains(node), $"Spawned on occupied node {graph.Nodes[node]}");
        }

        [Test]
        public void ObstructedDoors_AreNotLocked()
        {
            int player = Node(14, 18);
            var first = NewDirector().Decide(Calm(player));
            Assume.That(first.DoorsToLock.Count > 0);

            var percepts = Calm(player);
            foreach (string door in first.DoorsToLock) percepts.ObstructedDoors.Add(door);
            var decision = NewDirector().Decide(percepts);

            foreach (string door in first.DoorsToLock)
            {
                Assert.IsFalse(decision.DoorLocks[door], $"{door} has something in the doorway");
                StringAssert.Contains($"{door} must be open: something is in the doorway", string.Join(" | ", decision.Rules));
            }
        }

        [Test]
        public void TimeInTheSafeArea_DoesNotCountAsCalm()
        {
            int armoury = level.StartNode;
            Assume.That(LabLockdownLayout.IsSafeArea(graph.Nodes[armoury].Area));
            int crossroads = Node(10, 14);

            // The player spent 60 s choosing gear, then walked out. Only the 4 s since leaving count.
            var director = NewDirector();
            director.Decide(Calm(armoury, time: 60, calmSeconds: 60));
            var afterGear = director.Decide(Calm(crossroads, time: 64, calmSeconds: 64));

            var noSafeArea = NewDirector().Decide(Calm(crossroads, time: 64, calmSeconds: 64));

            Assert.Less(afterGear.Evaluation.Intensity, noSafeArea.Evaluation.Intensity);
            Assert.AreNotEqual(DirectorPhase.Peak, afterGear.Evaluation.Phase, "Leaving the armoury should not trigger a peak");
            Assert.AreEqual(DirectorPhase.Peak, noSafeArea.Evaluation.Phase);
        }

        [Test]
        public void CalmTime_StillGrowsAfterLeavingTheSafeArea()
        {
            int armoury = level.StartNode;
            int crossroads = Node(10, 14);
            var director = NewDirector();
            director.Decide(Calm(armoury, time: 60, calmSeconds: 60));

            var later = Calm(crossroads, time: 60 + 30, calmSeconds: 90);
            var decision = director.Decide(later);

            Assert.AreEqual(30f, later.CalmSeconds, 0.001f);
            Assert.AreEqual(DirectorPhase.Peak, decision.Evaluation.Phase);
        }

        [Test]
        public void PreviewIntensity_MatchesTheDecision_WithoutChangingMemory()
        {
            var director = NewDirector();
            director.Decide(Calm(level.StartNode, time: 60, calmSeconds: 60));
            double next = director.NextDecisionTime;

            var preview = Calm(Node(10, 14), time: 64, calmSeconds: 64);
            float intensity = director.PreviewIntensity(preview);

            Assert.AreEqual(64f, preview.CalmSeconds, "Preview must not change the percepts");
            Assert.AreEqual(next, director.NextDecisionTime, "Preview must not change the schedule");
            Assert.AreEqual(director.Decide(Calm(Node(10, 14), time: 64, calmSeconds: 64)).Evaluation.Intensity, intensity, 1e-5f);
        }

        [Test]
        public void PlayerOffTheGraph_IsRejected()
        {
            var director = NewDirector();
            Assert.Throws<ArgumentOutOfRangeException>(() => director.Decide(Calm(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => director.Decide(Calm(graph.NodeCount)));
        }

        [Test]
        public void NullCollections_AreTreatedAsEmpty()
        {
            var percepts = Calm(Node(10, 14));
            percepts.VisibleNodes = null;
            percepts.LockedDoors = null;
            percepts.OccupiedSpawnNodes = null;
            percepts.ObstructedDoors = null;

            var decision = NewDirector().Decide(percepts);
            Assert.Greater(decision.SpawnNodes.Count, 0);
        }

        [Test]
        public void EverySpawnPointOccupied_SpawnsNothing_ButStaysValid()
        {
            var percepts = Calm(Node(10, 14));
            foreach (int node in candidates) percepts.OccupiedSpawnNodes.Add(node);

            var decision = NewDirector().Decide(percepts);

            Assert.AreEqual(0, decision.SpawnNodes.Count);
            Assert.IsFalse(decision.UsedFallback, "Zero spawns is a valid configuration, not a failure");
        }
    }
}

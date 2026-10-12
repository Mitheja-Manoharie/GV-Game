using System.Collections.Generic;
using System.Linq;
using LabLockdown.AI.Facility.Director;
using LabLockdown.AI.Facility.Director.Simulation;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    /// <summary>
    /// Week 4: whole-run pacing tests. Each test plays complete simulated runs of the level against the real
    /// Director, so they check behaviour over time rather than single decisions.
    /// </summary>
    public class PacingTests
    {
        private const int Seeds = 20;
        private FacilityLevel level;

        [SetUp]
        public void SetUp()
        {
            var graph = LabLockdownLayout.CreateGraph();
            var candidates = LabLockdownLayout.DefaultSpawnPoints.Select(s => graph.FindNearestNode(s.X, s.Z)).ToList();
            int exit = graph.FindNearestNode(LabLockdownLayout.ExitX, LabLockdownLayout.ExitZ);
            int start = graph.FindNearestNode(LabLockdownLayout.PlayerStartX, LabLockdownLayout.PlayerStartZ);
            level = new FacilityLevel(graph, candidates, exit, start);
        }

        private List<PacingResult> RunMany(SimulatedPlayer player, DirectorSettings settings = null) =>
            Enumerable.Range(1, Seeds).Select(seed => PacingSimulator.Run(level, settings ?? new DirectorSettings(), player, seed)).ToList();

        private static DirectorSettings FixedPressure(float intensity) => new DirectorSettings
        {
            BaseIntensity = intensity, HealthWeight = 0, ProgressWeight = 0, CalmWeight = 0, DamageWeight = 0, CrowdWeight = 0,
            ReliefHealth = 0, ReliefAfterPeakSeconds = 0,
        };

        [Test]
        public void EveryRun_KeepsTheExitReachable_AndTheEnemyCap()
        {
            var settings = new DirectorSettings();
            foreach (var player in new[] { SimulatedPlayer.Weak(), SimulatedPlayer.Average(), SimulatedPlayer.Strong() })
            foreach (var run in RunMany(player, settings))
            {
                Assert.IsTrue(run.ExitAlwaysReachable, $"{player.Name}: exit was cut off");
                Assert.LessOrEqual(run.MaxAlive, settings.MaxEnemiesAlive, $"{player.Name}: too many robots alive");
            }
        }

        [Test]
        public void AverageAndStrongPlayers_AlwaysReachTheExit()
        {
            foreach (var player in new[] { SimulatedPlayer.Average(), SimulatedPlayer.Strong() })
                Assert.IsTrue(RunMany(player).All(r => r.ReachedExit), $"{player.Name} player failed a run");
        }

        [Test]
        public void WeakPlayer_SurvivesAlmostEveryRun()
        {
            int survived = RunMany(SimulatedPlayer.Weak()).Count(r => r.ReachedExit);
            Assert.GreaterOrEqual(survived, Seeds * 9 / 10);
        }

        [Test]
        public void AdaptivePressure_IsFairerThanFixedPressure_ForTheWeakPlayer()
        {
            int adaptive = RunMany(SimulatedPlayer.Weak()).Count(r => r.ReachedExit);
            int fixedPressure = RunMany(SimulatedPlayer.Weak(), FixedPressure(0.5f)).Count(r => r.ReachedExit);
            Assert.Greater(adaptive, fixedPressure);
        }

        [Test]
        public void WeakPlayer_MeetsFewerRobotsPerMinute_ThanStrongPlayer()
        {
            double weak = RunMany(SimulatedPlayer.Weak()).Average(r => r.SpawnsPerMinute);
            double strong = RunMany(SimulatedPlayer.Strong()).Average(r => r.SpawnsPerMinute);
            Assert.Less(weak, strong * 0.75, $"weak {weak:0.0}/min, strong {strong:0.0}/min");
        }

        [Test]
        public void EveryRun_HasAClimax_FollowedByRelief()
        {
            var settings = new DirectorSettings();
            foreach (var run in RunMany(SimulatedPlayer.Average(), settings))
            {
                Assert.GreaterOrEqual(run.Peaks, 1, "No climax");

                var peak = run.Decisions.First(d => d.Evaluation.Phase == DirectorPhase.Peak && d.SpawnNodes.Count > 0);
                foreach (var later in run.Decisions.Where(d => d.Time > peak.Time && d.Time < peak.Time + settings.ReliefAfterPeakSeconds))
                    Assert.AreEqual(0, later.SpawnNodes.Count, $"Spawned during the relief window at t={later.Time:0.0}");
            }
        }

        [Test]
        public void StrongPlayer_IsKeptUnderMorePressure_ThanWithEasyFixedPressure()
        {
            double adaptive = RunMany(SimulatedPlayer.Strong()).Average(r => r.MinHealth);
            double easy = RunMany(SimulatedPlayer.Strong(), FixedPressure(0.35f)).Average(r => r.MinHealth);
            Assert.Less(adaptive, easy, "Adaptive pacing should challenge a strong player more than an easy fixed setting");
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            var a = PacingSimulator.Run(level, new DirectorSettings(), SimulatedPlayer.Average(), 42);
            var b = PacingSimulator.Run(level, new DirectorSettings(), SimulatedPlayer.Average(), 42);

            Assert.AreEqual(a.Duration, b.Duration);
            Assert.AreEqual(a.TotalSpawned, b.TotalSpawned);
            CollectionAssert.AreEqual(a.Samples.Select(s => s.Intensity).ToList(), b.Samples.Select(s => s.Intensity).ToList());
        }
    }
}

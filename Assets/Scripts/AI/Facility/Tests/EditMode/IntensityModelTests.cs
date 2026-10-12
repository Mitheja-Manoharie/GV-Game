using LabLockdown.AI.Facility.Director;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    public class IntensityModelTests
    {
        private readonly DirectorSettings settings = new DirectorSettings();

        private static FacilityPercepts Percepts(float health = 1f, float damage = 0f, float calm = 0f, int alive = 0) =>
            new FacilityPercepts { Health01 = health, RecentDamage01 = damage, CalmSeconds = calm, EnemiesAlive = alive };

        [Test]
        public void LowHealth_TriggersRelief()
        {
            var evaluation = IntensityModel.Evaluate(Percepts(health: 0.2f, calm: 60f), 0.9f, settings, false);
            Assert.AreEqual(DirectorPhase.Relief, evaluation.Phase);
            Assert.AreEqual(0, evaluation.SpawnBudget);
            Assert.IsFalse(evaluation.LocksAllowed);
        }

        [Test]
        public void ReliefWindow_TriggersRelief_EvenForAHealthyPlayer()
        {
            var evaluation = IntensityModel.Evaluate(Percepts(calm: 60f), 0.9f, settings, inReliefWindow: true);
            Assert.AreEqual(DirectorPhase.Relief, evaluation.Phase);
            Assert.AreEqual(0, evaluation.SpawnBudget);
        }

        [Test]
        public void PlayerInSafeArea_GetsNoPressure()
        {
            var evaluation = IntensityModel.Evaluate(Percepts(calm: 60f), 0f, settings, false, playerInSafeArea: true);
            Assert.AreEqual(DirectorPhase.Relief, evaluation.Phase);
            Assert.AreEqual(0, evaluation.SpawnBudget);
            Assert.IsFalse(evaluation.LocksAllowed);
        }

        [Test]
        public void WaveSize_GrowsWithIntensity()
        {
            // Same enemy count (none alive), different pressure: build-up waves must be smaller than peak waves.
            var buildUp = IntensityModel.Evaluate(Percepts(health: 0.5f), 0.2f, settings, false);
            var peak = IntensityModel.Evaluate(Percepts(calm: 30f), 0.9f, settings, false);
            Assert.AreEqual(DirectorPhase.BuildUp, buildUp.Phase);
            Assert.AreEqual(DirectorPhase.Peak, peak.Phase);
            Assert.Less(buildUp.SpawnBudget, peak.SpawnBudget);
        }

        [Test]
        public void HealthyCalmProgressingPlayer_GetsAPeak()
        {
            var evaluation = IntensityModel.Evaluate(Percepts(calm: 30f), 0.8f, settings, false);
            Assert.AreEqual(DirectorPhase.Peak, evaluation.Phase);
            Assert.IsTrue(evaluation.LocksAllowed);
            Assert.AreEqual(settings.MaxLockedDoors, evaluation.MaxLocks);
            Assert.AreEqual(settings.MaxSpawnPerWave, evaluation.SpawnBudget);
        }

        [Test]
        public void RecentDamage_LowersIntensity()
        {
            float calm = IntensityModel.ComputeIntensity(Percepts(calm: 10f), 0.5f, settings);
            float hurt = IntensityModel.ComputeIntensity(Percepts(calm: 10f, damage: 0.4f), 0.5f, settings);
            Assert.Less(hurt, calm);
        }

        [Test]
        public void EnemiesAlreadyAlive_ReduceTheSpawnBudget()
        {
            var none = IntensityModel.Evaluate(Percepts(calm: 30f), 0.8f, settings, false);
            var many = IntensityModel.Evaluate(Percepts(calm: 30f, alive: 5), 0.8f, settings, false);
            Assert.Less(many.SpawnBudget, none.SpawnBudget);
            Assert.GreaterOrEqual(many.SpawnBudget, 0);
        }

        [Test]
        public void Intensity_IsAlwaysBetweenZeroAndOne()
        {
            Assert.AreEqual(0f, IntensityModel.ComputeIntensity(Percepts(health: 0f, damage: 1f, alive: 6), 0f, settings));
            Assert.AreEqual(1f, IntensityModel.ComputeIntensity(Percepts(calm: 1000f), 1f, new DirectorSettings { BaseIntensity = 2f }));
        }

        [Test]
        public void HigherIntensity_MeansFasterDecisions()
        {
            var calm = IntensityModel.Evaluate(Percepts(calm: 0f, damage: 0.5f), 0f, settings, false);
            var tense = IntensityModel.Evaluate(Percepts(calm: 30f), 1f, settings, false);
            Assert.Greater(calm.DecisionDelay, tense.DecisionDelay);
        }
    }
}

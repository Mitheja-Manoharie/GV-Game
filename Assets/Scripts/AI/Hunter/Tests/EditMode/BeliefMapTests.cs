using System;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class BeliefMapTests
    {
        // Walking distances from a source at node 0 along a row of 10 nodes, 4 m apart.
        private static float[] RowDistances(int source = 0, int count = 10)
        {
            var d = new float[count];
            for (int i = 0; i < count; i++) d[i] = Math.Abs(i - source) * 4f;
            return d;
        }

        private static float MaxProbability(BeliefMap map)
        {
            float max = 0f;
            for (int i = 0; i < map.Count; i++) max = Math.Max(max, map[i]);
            return max;
        }

        [Test]
        public void StartsUniform_AndSumsToOne()
        {
            var map = new BeliefMap(10);
            Assert.AreEqual(1f, map.Total, 1e-5);
            for (int i = 0; i < 10; i++) Assert.AreEqual(0.1f, map[i], 1e-6);
        }

        [Test]
        public void HeardNoise_ConcentratesTheBeliefAroundTheSource()
        {
            var map = new BeliefMap(10);
            map.ApplyNoise(RowDistances(3), heardLevel: 20f, loudness: 30f);

            Assert.AreEqual(3, map.MostLikelyNode());
            Assert.Greater(map[3], map[1]);
            Assert.Greater(map[3], map[8]);
            Assert.AreEqual(1f, map.Total, 1e-4);
        }

        [Test]
        public void MuffledNoise_GivesAFlatterBelief_ThanAClearOne()
        {
            var clear = new BeliefMap(10);
            clear.ApplyNoise(RowDistances(), heardLevel: 28f, loudness: 30f);

            var muffled = new BeliefMap(10);
            muffled.ApplyNoise(RowDistances(), heardLevel: 2f, loudness: 30f);

            Assert.Less(MaxProbability(muffled), MaxProbability(clear));
        }

        [Test]
        public void InaudibleNoise_ChangesNothing()
        {
            var map = new BeliefMap(10);
            map.ApplyNoise(RowDistances(), heardLevel: 0f, loudness: 30f);

            for (int i = 0; i < 10; i++) Assert.AreEqual(0.1f, map[i], 1e-6);
        }

        [Test]
        public void RepeatedNoise_ConvergesToTheLikelihood_AndNeverToCertainty()
        {
            // Footsteps fire over and over from the same place. Blending must not pin the belief to one node.
            var map = new BeliefMap(10);
            for (int i = 0; i < 300; i++) map.ApplyNoise(RowDistances(), heardLevel: 8f, loudness: 10f);

            float sigma = BeliefParameters.DefaultSigma0 + BeliefParameters.DefaultKappa * (10f - 8f);
            float[] fixedPoint = BeliefMap.Likelihood(RowDistances(), sigma);

            for (int i = 0; i < 10; i++) Assert.AreEqual(fixedPoint[i], map[i], 1e-3);
            Assert.Less(MaxProbability(map), 0.6f);
        }

        [Test]
        public void Sighting_CollapsesTheBeliefOntoThatNode()
        {
            var map = new BeliefMap(10);
            map.ApplySighting(6);

            Assert.AreEqual(1f, map[6], 1e-6);
            Assert.AreEqual(0f, map[5], 1e-6);
            Assert.AreEqual(1f, map.Total, 1e-6);
        }

        [Test]
        public void BeingShot_PullsTheBeliefTowardTheShooter()
        {
            var map = new BeliefMap(10);
            map.ApplyShot(RowDistances(7));

            Assert.AreEqual(7, map.MostLikelyNode());
            Assert.AreEqual(1f, map.Total, 1e-4);

            // Expected: 90% of the shooter likelihood blended with 10% of the old uniform belief.
            float[] likelihood = BeliefMap.Likelihood(RowDistances(7), BeliefParameters.DefaultSigma0);
            float expected = BeliefParameters.DefaultShotWeight * likelihood[7] + (1f - BeliefParameters.DefaultShotWeight) * 0.1f;
            Assert.AreEqual(expected, map[7], 1e-4);
        }

        [Test]
        public void SearchingANode_LowersIt_AndStaysNormalised()
        {
            var map = new BeliefMap(10);
            map.MarkSearched(4);

            Assert.Less(map[4], map[3]);
            Assert.AreEqual(1f, map.Total, 1e-5);
            // 0.1 * 0.1 = 0.01 before normalising; the other nodes share the rest.
            Assert.AreEqual(0.01f / 0.91f, map[4], 1e-5);
        }

        [Test]
        public void Decay_SpreadsAColdTrailBackToUniform()
        {
            var map = new BeliefMap(10);
            map.ApplySighting(2);

            map.Decay(1f);   // 5% moves back to "anywhere"
            Assert.AreEqual(0.95f + 0.005f, map[2], 1e-5);
            Assert.AreEqual(0.005f, map[3], 1e-5);

            map.Decay(1000f);   // long enough that nothing is remembered
            for (int i = 0; i < 10; i++) Assert.AreEqual(0.1f, map[i], 1e-5);
        }

        [Test]
        public void Likelihood_GivesUnreachableNodesZero_AndSumsToOne()
        {
            float[] d = { 0f, 4f, float.PositiveInfinity, 8f };
            float[] l = BeliefMap.Likelihood(d, 4f);

            Assert.AreEqual(0f, l[2], 1e-9);
            Assert.AreEqual(1f, l[0] + l[1] + l[2] + l[3], 1e-5);
        }

        [Test]
        public void EvidencePointingNowhereReachable_IsIgnored()
        {
            var map = new BeliefMap(3);
            var unreachable = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            map.ApplyNoise(unreachable, heardLevel: 10f, loudness: 10f);

            for (int i = 0; i < 3; i++) Assert.AreEqual(1f / 3f, map[i], 1e-6);
        }

        [Test]
        public void FromProbabilities_NormalisesTheWeights()
        {
            var map = BeliefMap.FromProbabilities(new[] { 2f, 6f, 0f, 2f });
            Assert.AreEqual(0.2f, map[0], 1e-6);
            Assert.AreEqual(0.6f, map[1], 1e-6);
            Assert.AreEqual(1, map.MostLikelyNode());
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            var map = new BeliefMap(5);

            Assert.Throws<ArgumentException>(() => map.ApplyNoise(new float[3], 5f, 10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.ApplySighting(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.MarkSearched(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeliefMap(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeliefParameters(sigma0: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeliefParameters(searchedFactor: 1.5f));
        }
    }
}

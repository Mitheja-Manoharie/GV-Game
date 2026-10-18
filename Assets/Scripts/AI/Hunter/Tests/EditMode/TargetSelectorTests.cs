using System;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class TargetSelectorTests
    {
        [Test]
        public void FlatBelief_PicksTheNearestNode()
        {
            var belief = new BeliefMap(4);
            int target = TargetSelector.Choose(belief, new[] { 12f, 4f, 20f, 8f }, out float utility);

            Assert.AreEqual(1, target);
            Assert.AreEqual(0.25f / 5f, utility, 1e-6);
        }

        [Test]
        public void ANearbyNodeCanBeatAFarOneOfEqualBelief()
        {
            var belief = BeliefMap.FromProbabilities(new[] { 0.5f, 0.5f });
            Assert.AreEqual(1, TargetSelector.Choose(belief, new[] { 10f, 1f }, out _));
        }

        [Test]
        public void AVeryLikelyFarNode_BeatsAnUnlikelyNearOne()
        {
            var belief = BeliefMap.FromProbabilities(new[] { 0.95f, 0.05f });
            // U(far) = 0.95 / 11 = 0.086   U(near) = 0.05 / 1 = 0.05
            Assert.AreEqual(0, TargetSelector.Choose(belief, new[] { 10f, 0f }, out _));
        }

        [Test]
        public void ZeroBeliefNodes_AreNeverChosen_EvenWhenFree()
        {
            var belief = new BeliefMap(4);
            belief.ApplySighting(2);

            Assert.AreEqual(2, TargetSelector.Choose(belief, new[] { 0f, 0f, 10f, 0f }, out _));
        }

        [Test]
        public void UnreachableNodes_AreNeverChosen()
        {
            var belief = BeliefMap.FromProbabilities(new[] { 0.9f, 0.1f });
            int target = TargetSelector.Choose(belief, new[] { float.PositiveInfinity, 6f }, out _);

            Assert.AreEqual(1, target);
        }

        [Test]
        public void WhenNothingIsReachable_ReturnsMinusOne()
        {
            var belief = new BeliefMap(3);
            var costs = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };

            Assert.AreEqual(-1, TargetSelector.Choose(belief, costs, out float utility));
            Assert.AreEqual(0f, utility, 1e-9);
        }

        [Test]
        public void Ties_KeepTheLowestNodeId()
        {
            var belief = new BeliefMap(3);
            Assert.AreEqual(0, TargetSelector.Choose(belief, new[] { 4f, 4f, 4f }, out _));
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            var belief = new BeliefMap(3);

            Assert.Throws<ArgumentException>(() => TargetSelector.Choose(belief, new float[2], out _));
            Assert.Throws<ArgumentNullException>(() => TargetSelector.Choose(null, new float[3], out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => TargetSelector.Choose(belief, new float[3], out _, 0f));
        }
    }
}

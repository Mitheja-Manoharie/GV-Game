using System.Collections.Generic;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Flanker.Tests
{
    public class ThreatMapTests
    {
        private LevelGraph graph;
        private readonly FlankerSettings settings = new FlankerSettings();

        [SetUp]
        public void SetUp() => graph = TestLevel.Graph();

        private ThreatMap Threat((float x, float z) player, (float x, float z) facing) =>
            new ThreatMap(graph, TestLevel.Percepts(graph, (10f, 14f), player, facing), settings);

        [Test]
        public void ViewCone_SplitsVisibleTilesIntoWatchedAndUnwatched()
        {
            // Player in the control room (2, 30), looking east through the east door.
            var threat = Threat((2f, 30f), (1f, 0f));
            int labLink = TestLevel.Node(graph, 10f, 30f);       // ahead, through the open door
            int westCorridor = TestLevel.Node(graph, -6f, 30f);  // behind, through the west door
            int crossroads = TestLevel.Node(graph, 10f, 14f);    // behind walls

            Assert.IsTrue(threat.IsVisible(labLink));
            Assert.IsTrue(threat.IsWatched(labLink));
            Assert.IsTrue(threat.IsAttackPosition(labLink));
            Assert.IsFalse(threat.IsFlankPosition(labLink));

            Assert.IsTrue(threat.IsVisible(westCorridor));
            Assert.IsFalse(threat.IsWatched(westCorridor), "Behind the player: visible but not held");
            Assert.IsTrue(threat.IsFlankPosition(westCorridor));

            Assert.IsTrue(threat.IsCover(crossroads));
            Assert.IsFalse(threat.IsAttackPosition(crossroads));
        }

        [Test]
        public void TurningAround_SwapsWhichRouteIsHeld()
        {
            int labLink = TestLevel.Node(graph, 10f, 30f);
            int westCorridor = TestLevel.Node(graph, -6f, 30f);

            var facingWest = Threat((2f, 30f), (-1f, 0f));
            Assert.IsFalse(facingWest.IsWatched(labLink));
            Assert.IsTrue(facingWest.IsWatched(westCorridor));
        }

        [Test]
        public void PlayerTile_IsAlwaysVisibleAndWatched()
        {
            var threat = new ThreatMap(graph, new FlankerPercepts
            {
                PlayerNode = TestLevel.Node(graph, 2f, 30f), PlayerX = 2f, PlayerZ = 30f,
                PlayerForwardX = 1f, VisibleNodes = new HashSet<int>(),
            }, settings);

            int playerNode = TestLevel.Node(graph, 2f, 30f);
            Assert.IsTrue(threat.IsVisible(playerNode));
            Assert.IsTrue(threat.IsWatched(playerNode));
        }

        [Test]
        public void UnknownFacing_TreatsEveryVisibleTileAsWatched()
        {
            var threat = Threat((2f, 30f), (0f, 0f));
            for (int i = 0; i < graph.NodeCount; i++)
                Assert.AreEqual(threat.IsVisible(i), threat.IsWatched(i), graph.Nodes[i].ToString());
        }

        [Test]
        public void AttackRange_LimitsAttackPositions()
        {
            var threat = Threat((2f, 30f), (1f, 0f));
            int researchLabFar = TestLevel.Node(graph, 18f, 30f); // 16 m away, visible through the east door

            Assert.IsTrue(threat.IsVisible(researchLabFar));
            Assert.IsFalse(threat.IsAttackPosition(researchLabFar));
        }
    }
}

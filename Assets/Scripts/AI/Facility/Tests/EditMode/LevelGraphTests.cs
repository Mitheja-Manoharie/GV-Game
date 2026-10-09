using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    /// <summary>Generic behaviour of LevelGraph on small hand-built graphs.</summary>
    public class LevelGraphTests
    {
        // A - B - C   with a door between B and C, and a longer bypass A - D - C.
        private static LevelGraph CreateDiamond()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0, "A");   // 0
            g.AddNode(4, 0, "B");   // 1
            g.AddNode(8, 0, "C");   // 2
            g.AddNode(4, 8, "D");   // 3
            g.AddEdge(0, 1, 4);
            g.AddEdge(1, 2, 4);
            g.AddEdge(0, 3, 9);
            g.AddEdge(3, 2, 9);
            Assert.IsTrue(g.TagDoor("Door_BC", 6, 0, 1));
            return g;
        }

        [Test]
        public void AddEdge_RejectsInvalidInput()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0);
            g.AddNode(4, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.AddEdge(0, 1, 0f));
            Assert.Throws<ArgumentException>(() => g.AddEdge(0, 0, 4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.AddEdge(0, 5, 4f));
        }

        [Test]
        public void Other_ThrowsForNodeNotOnEdge()
        {
            var edge = new GraphEdge(0, 1, 4);
            Assert.AreEqual(1, edge.Other(0));
            Assert.Throws<ArgumentException>(() => edge.Other(2));
        }

        [Test]
        public void TagDoor_ReturnsFalseWhenNoEdgeIsClose()
        {
            var g = CreateDiamond();
            Assert.IsFalse(g.TagDoor("Door_Nowhere", 100, 100, 1));
        }

        [Test]
        public void ShortestDistances_UsesDoorWhenOpen()
        {
            var g = CreateDiamond();
            Assert.AreEqual(8f, g.ShortestDistances(0)[2], 1e-4f);
        }

        [Test]
        public void ShortestDistances_TakesBypassWhenDoorLocked()
        {
            var g = CreateDiamond();
            var locked = new HashSet<string> { "Door_BC" };
            Assert.AreEqual(18f, g.ShortestDistances(0, locked)[2], 1e-4f);
            Assert.IsTrue(g.IsReachable(0, 2, locked));
        }

        [Test]
        public void IsReachable_FalseWhenOnlyRouteIsLocked()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0);
            g.AddNode(4, 0);
            g.AddEdge(0, 1, 4);
            g.TagDoor("Door", 2, 0, 1);

            var locked = new HashSet<string> { "Door" };
            Assert.IsFalse(g.IsReachable(0, 1, locked));
            Assert.IsTrue(float.IsPositiveInfinity(g.ShortestDistances(0, locked)[1]));
        }

        [Test]
        public void FindNearestNode_ReturnsClosestNode()
        {
            var g = CreateDiamond();
            Assert.AreEqual(3, g.FindNearestNode(5, 7));
            Assert.AreEqual(-1, new LevelGraph().FindNearestNode(0, 0));
        }

        [Test]
        public void Builder_RejectsDuplicateTilesAndMisplacedDoors()
        {
            var duplicate = new[] { new TileInfo(0, 0, "A"), new TileInfo(0, 0, "A") };
            Assert.Throws<ArgumentException>(() => LevelGraphBuilder.Build(duplicate, null, 4));

            var tiles = new[] { new TileInfo(0, 0, "A"), new TileInfo(4, 0, "A") };
            var door = new[] { new DoorInfo("Door_Bad", 0, 10) };
            Assert.Throws<ArgumentException>(() => LevelGraphBuilder.Build(tiles, door, 4));
        }
    }

    /// <summary>The Lab Lockdown level graph matches the real layout.</summary>
    public class LabLockdownLayoutTests
    {
        private LevelGraph graph;
        private int start;
        private int exit;

        [SetUp]
        public void SetUp()
        {
            graph = LabLockdownLayout.CreateGraph();
            start = graph.FindNearestNode(LabLockdownLayout.PlayerStartX, LabLockdownLayout.PlayerStartZ);
            exit = graph.FindNearestNode(LabLockdownLayout.ExitX, LabLockdownLayout.ExitZ);
        }

        [Test]
        public void Graph_HasExpectedSize()
        {
            Assert.AreEqual(45, graph.NodeCount);
            Assert.AreEqual(55, graph.Edges.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    LabLockdownLayout.DoorArmouryNorth, LabLockdownLayout.DoorInfirmaryNorth,
                    LabLockdownLayout.DoorLabSouth, LabLockdownLayout.DoorControlWest,
                    LabLockdownLayout.DoorControlEast,
                },
                graph.DoorIds);
        }

        [Test]
        public void AllNodes_ConnectedWhenDoorsOpen()
        {
            Assert.AreEqual(graph.NodeCount, graph.CountReachable(start));
        }

        [Test]
        public void ShortestRouteToExit_Is60Metres()
        {
            Assert.AreEqual(60f, graph.ShortestDistances(start)[exit], 1e-4f);
        }

        [Test]
        public void LockingInfirmaryNorth_KeepsLabRoute()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorInfirmaryNorth };
            Assert.AreEqual(60f, graph.ShortestDistances(start, locked)[exit], 1e-4f);
        }

        [Test]
        public void LockingLabSouth_ForcesLongerInfirmaryRoute()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorLabSouth };
            Assert.AreEqual(92f, graph.ShortestDistances(start, locked)[exit], 1e-4f);
        }

        [Test]
        public void LockingBothRoutes_CutsOffExit()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorInfirmaryNorth, LabLockdownLayout.DoorLabSouth };
            Assert.IsFalse(graph.IsReachable(start, exit, locked));
        }

        [Test]
        public void LockingBothControlRoomDoors_CutsOffExit()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorControlWest, LabLockdownLayout.DoorControlEast };
            Assert.IsFalse(graph.IsReachable(start, exit, locked));
        }

        [Test]
        public void LockingArmouryDoor_TrapsPlayerInSafeArea()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorArmouryNorth };
            // Quarantine cell (4) + corridor (1) + armoury (4)
            Assert.AreEqual(9, graph.CountReachable(start, locked));
        }

        [Test]
        public void DefaultSpawnPoints_AreOnDistinctTilesOutsideSafeAreas()
        {
            var used = new HashSet<int>();
            foreach (var spawn in LabLockdownLayout.DefaultSpawnPoints)
            {
                int node = graph.FindNearestNode(spawn.X, spawn.Z);
                var tile = graph.Nodes[node];
                Assert.IsTrue(used.Add(node), $"{spawn.Id} shares tile {tile} with another spawn point");
                Assert.IsFalse(LabLockdownLayout.IsSafeArea(tile.Area), $"{spawn.Id} is in safe area {tile.Area}");
                Assert.IsTrue(graph.IsReachable(node, start), $"{spawn.Id} cannot reach the player start");
            }
        }

        [Test]
        public void BfsAndDijkstra_AgreeOnReachability_ForEveryDoorCombination()
        {
            var doors = new List<string>(graph.DoorIds);
            for (int mask = 0; mask < 1 << doors.Count; mask++)
            {
                var locked = new HashSet<string>();
                for (int i = 0; i < doors.Count; i++)
                    if ((mask & (1 << i)) != 0) locked.Add(doors[i]);

                var distances = graph.ShortestDistances(start, locked);
                for (int node = 0; node < graph.NodeCount; node++)
                {
                    bool bfs = graph.IsReachable(start, node, locked);
                    bool dijkstra = !float.IsPositiveInfinity(distances[node]);
                    Assert.AreEqual(bfs, dijkstra, $"Mismatch at {graph.Nodes[node]} with locked doors [{string.Join(", ", locked)}]");
                }
            }
        }
    }
}

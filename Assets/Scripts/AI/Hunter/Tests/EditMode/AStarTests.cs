using System;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class AStarTests
    {
        // A(0,0) - B(4,0) - C(8,0) with a door on B-C, and a longer bypass A - D(4,8) - C.
        //   direct, door open   : 4 + 4      = 8
        //   direct, door closed : 4 + 4 + 3  = 11
        //   bypass              : 9 + 9      = 18
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

        private static DoorStateTable Doors(DoorState state)
        {
            var table = new DoorStateTable(DoorState.Open);
            table.Set("Door_BC", state);
            return table;
        }

        private static int[] ToArray(PathResult path)
        {
            var array = new int[path.Nodes.Count];
            for (int i = 0; i < array.Length; i++) array[i] = path.Nodes[i];
            return array;
        }

        [Test]
        public void StartEqualsGoal_ReturnsSingleNodePathWithZeroCost()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            PathResult path = astar.FindPath(1, 1, Doors(DoorState.Open));

            Assert.IsTrue(path.Found);
            Assert.AreEqual(new[] { 1 }, ToArray(path));
            Assert.AreEqual(0f, path.Cost, 1e-6);
        }

        [Test]
        public void OpenDoor_TakesTheDirectRoute()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            PathResult path = astar.FindPath(0, 2, Doors(DoorState.Open));

            Assert.AreEqual(new[] { 0, 1, 2 }, ToArray(path));
            Assert.AreEqual(8f, path.Cost, 1e-5);
        }

        [Test]
        public void ClosedDoor_StillWorthOpening_WhenTheDetourIsLonger()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            PathResult path = astar.FindPath(0, 2, Doors(DoorState.Closed));

            Assert.AreEqual(new[] { 0, 1, 2 }, ToArray(path));
            Assert.AreEqual(11f, path.Cost, 1e-5);
        }

        [Test]
        public void ClosedDoor_IsAvoided_WhenThePenaltyMakesTheDetourCheaper()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel(20f));   // 8 + 20 = 28 > 18
            PathResult path = astar.FindPath(0, 2, Doors(DoorState.Closed));

            Assert.AreEqual(new[] { 0, 3, 2 }, ToArray(path));
            Assert.AreEqual(18f, path.Cost, 1e-5);
        }

        [Test]
        public void LockedDoor_ForcesTheBypass()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            PathResult path = astar.FindPath(0, 2, Doors(DoorState.Locked));

            Assert.AreEqual(new[] { 0, 3, 2 }, ToArray(path));
        }

        [Test]
        public void BlockedDoor_ForcesTheBypass()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            PathResult path = astar.FindPath(0, 2, Doors(DoorState.Blocked));

            Assert.AreEqual(new[] { 0, 3, 2 }, ToArray(path));
        }

        [Test]
        public void NoRoute_ReturnsNotFound()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0);
            g.AddNode(4, 0);
            g.AddEdge(0, 1, 4);
            g.TagDoor("Door_Only", 2, 0, 1);

            var astar = new AStar(g, new EdgeCostModel());
            var table = new DoorStateTable(DoorState.Open);
            table.Set("Door_Only", DoorState.Locked);
            PathResult path = astar.FindPath(0, 1, table);

            Assert.IsFalse(path.Found);
            Assert.AreEqual(0, path.Nodes.Count);
            Assert.IsTrue(float.IsPositiveInfinity(path.Cost));
        }

        [Test]
        public void InvalidNodes_Throw()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            DoorStateTable doors = Doors(DoorState.Open);

            Assert.Throws<ArgumentOutOfRangeException>(() => astar.FindPath(-1, 2, doors));
            Assert.Throws<ArgumentOutOfRangeException>(() => astar.FindPath(0, 99, doors));
        }

        [Test]
        public void ReusingTheInstance_ReflectsChangedDoorStates()
        {
            var astar = new AStar(CreateDiamond(), new EdgeCostModel());
            var doors = Doors(DoorState.Open);

            Assert.AreEqual(new[] { 0, 1, 2 }, ToArray(astar.FindPath(0, 2, doors)));
            doors.Set("Door_BC", DoorState.Locked);
            Assert.AreEqual(new[] { 0, 3, 2 }, ToArray(astar.FindPath(0, 2, doors)));
            doors.Set("Door_BC", DoorState.Open);
            Assert.AreEqual(new[] { 0, 1, 2 }, ToArray(astar.FindPath(0, 2, doors)));
        }

        [Test]
        public void RealLevel_MatchesFloydWarshall_ForEveryPair_UnderRandomDoorStates()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var costs = new EdgeCostModel();
            var astar = new AStar(graph, costs);
            var random = new Random(2026);

            for (int round = 0; round < 40; round++)
            {
                DoorStateTable doors = SearchTestSupport.RandomDoorStates(random, graph);
                float[,] oracle = SearchTestSupport.AllPairs(graph, costs, doors);

                for (int a = 0; a < graph.NodeCount; a++)
                {
                    for (int b = 0; b < graph.NodeCount; b++)
                    {
                        PathResult path = astar.FindPath(a, b, doors);
                        if (float.IsPositiveInfinity(oracle[a, b]))
                        {
                            Assert.IsFalse(path.Found);
                        }
                        else
                        {
                            Assert.IsTrue(path.Found);
                            Assert.AreEqual(oracle[a, b], path.Cost, 1e-3);
                        }
                    }
                }
            }
        }

        [Test]
        public void RealLevel_PathsAreWalkable_AndTheirCostIsTheSumOfTheirEdges()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var costs = new EdgeCostModel();
            var astar = new AStar(graph, costs);
            var doors = new DoorStateTable(DoorState.Closed);

            for (int a = 0; a < graph.NodeCount; a++)
            {
                for (int b = 0; b < graph.NodeCount; b++)
                {
                    PathResult path = astar.FindPath(a, b, doors);
                    Assert.IsTrue(path.Found);
                    Assert.AreEqual(a, path.Nodes[0]);
                    Assert.AreEqual(b, path.Nodes[path.Nodes.Count - 1]);

                    float sum = 0f;
                    for (int i = 0; i + 1 < path.Nodes.Count; i++)
                    {
                        GraphEdge edge = SearchTestSupport.EdgeBetween(graph, path.Nodes[i], path.Nodes[i + 1]);
                        Assert.IsNotNull(edge);
                        sum += costs.Cost(edge, doors);
                    }
                    Assert.AreEqual(sum, path.Cost, 1e-3);
                }
            }
        }

        [Test]
        public void RealLevel_LockingADoor_RemovesItFromEveryPath()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var costs = new EdgeCostModel();
            var astar = new AStar(graph, costs);

            foreach (string doorId in new System.Collections.Generic.HashSet<string>(graph.DoorIds))
            {
                var open = new DoorStateTable(DoorState.Open);
                var locked = new DoorStateTable(DoorState.Open);
                locked.Set(doorId, DoorState.Locked);

                int rerouted = 0;
                for (int a = 0; a < graph.NodeCount; a++)
                {
                    for (int b = 0; b < graph.NodeCount; b++)
                    {
                        PathResult before = astar.FindPath(a, b, open);
                        if (!SearchTestSupport.UsesDoor(graph, before, doorId)) continue;

                        PathResult after = astar.FindPath(a, b, locked);
                        if (after.Found)
                        {
                            Assert.IsFalse(SearchTestSupport.UsesDoor(graph, after, doorId));
                            Assert.GreaterOrEqual(after.Cost, before.Cost - 1e-3);
                        }
                        rerouted++;
                    }
                }
                Assert.Greater(rerouted, 0);   // every door really sits on some shortest path
            }
        }

        [Test]
        public void AStar_FindsTheSameCostAsDijkstra_WhileExpandingFewerNodes()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var costs = new EdgeCostModel();
            var withHeuristic = new AStar(graph, costs);
            var dijkstra = new AStar(graph, costs) { UseHeuristic = false };
            var doors = new DoorStateTable(DoorState.Open);

            long astarExpanded = 0, dijkstraExpanded = 0;
            for (int a = 0; a < graph.NodeCount; a++)
            {
                for (int b = 0; b < graph.NodeCount; b++)
                {
                    PathResult p1 = withHeuristic.FindPath(a, b, doors);
                    PathResult p2 = dijkstra.FindPath(a, b, doors);

                    Assert.AreEqual(p2.Cost, p1.Cost, 1e-3);
                    astarExpanded += p1.NodesExpanded;
                    dijkstraExpanded += p2.NodesExpanded;
                }
            }

            Assert.Less(astarExpanded, dijkstraExpanded);
            // Recorded for the viva: total nodes expanded over all 45 x 45 start/goal pairs.
            Console.WriteLine("A* expanded " + astarExpanded + " nodes; Dijkstra expanded " + dijkstraExpanded + ".");
        }
    }
}

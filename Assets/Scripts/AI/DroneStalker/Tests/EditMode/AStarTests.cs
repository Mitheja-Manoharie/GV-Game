using System.Collections.Generic;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.DroneStalker.Tests
{
    public class AStarTests
    {
        //  A small level with two routes from S (0) to G (5):
        //
        //      S(0) ---- L(1) ---- G(5)        short route, 8 m, through a lit node L
        //       |                   |
        //      D1(2) -- D2(3) -- D3(4)          long route, 16 m, all dark
        //
        static LevelGraph TwoRoutes()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0, "S");    // 0
            g.AddNode(4, 0, "L");    // 1
            g.AddNode(0, -4, "D1");  // 2
            g.AddNode(4, -4, "D2");  // 3
            g.AddNode(8, -4, "D3");  // 4
            g.AddNode(8, 0, "G");    // 5
            g.AddEdge(0, 1, 4f); g.AddEdge(1, 5, 4f);
            g.AddEdge(0, 2, 4f); g.AddEdge(2, 3, 4f); g.AddEdge(3, 4, 4f); g.AddEdge(4, 5, 4f);
            return g;
        }

        static (AStarPathfinder search, StalkerPercepts percepts) Setup(LevelGraph graph, float[] light, StalkerSettings settings = null)
        {
            settings ??= new StalkerSettings();
            var percepts = new StalkerPercepts { LightByNode = light };
            var costs = new VisibilityCostModel(graph, settings);
            costs.SetPercepts(percepts);
            return (new AStarPathfinder(costs, settings), percepts);
        }

        [Test]
        public void TakesTheShortRouteWhenEverythingIsDark()
        {
            var (search, _) = Setup(TwoRoutes(), new float[6]);
            var path = search.FindPath(0, 5);
            Assert.IsTrue(path.Found);
            CollectionAssert.AreEqual(new[] { 0, 1, 5 }, path.Nodes);
            Assert.AreEqual(8f, path.Cost, 1e-4f);
        }

        [Test]
        public void PrefersTheLongerDarkRouteOverAShortLitOne()
        {
            var light = new float[6];
            light[1] = 1f;                       // L fully lit: short route 4 + (4 + 10) = 18, dark route 16
            var settings = new StalkerSettings { LightCostWeight = 10f };
            var (search, _) = Setup(TwoRoutes(), light, settings);
            var path = search.FindPath(0, 5);
            Assert.IsTrue(path.Found);
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 4, 5 }, path.Nodes);
            Assert.AreEqual(16f, path.Cost, 1e-4f);
        }

        [Test]
        public void AvoidsANodeThePlayerCanSee()
        {
            var (search, percepts) = Setup(TwoRoutes(), new float[6]);
            percepts.PlayerVisibleNodes = new HashSet<int> { 1 };
            var path = search.FindPath(0, 5);
            CollectionAssert.DoesNotContain(path.Nodes, 1);
        }

        [Test]
        public void RoutesAroundALockedDoor()
        {
            var graph = TwoRoutes();
            Assert.IsTrue(graph.TagDoor("Door_L", 6, 0, 0.6f));   // the edge L-G is a door
            var (search, percepts) = Setup(graph, new float[6]);
            percepts.LockedDoors = new HashSet<string> { "Door_L" };
            var path = search.FindPath(0, 5);
            Assert.IsTrue(path.Found);
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 4, 5 }, path.Nodes);
        }

        [Test]
        public void ReportsNoPathWhenTheGoalIsSealedOff()
        {
            var graph = TwoRoutes();
            graph.TagDoor("Door_L", 6, 0, 0.6f);
            graph.TagDoor("Door_D", 8, -2, 0.6f);
            var (search, percepts) = Setup(graph, new float[6]);
            percepts.LockedDoors = new HashSet<string> { "Door_L", "Door_D" };
            var path = search.FindPath(0, 5);
            Assert.IsFalse(path.Found);
            Assert.IsEmpty(path.Nodes);
            Assert.IsFalse(path.BudgetExceeded);
        }

        [Test]
        public void StartEqualsGoalIsAnEmptyMove()
        {
            var (search, _) = Setup(TwoRoutes(), new float[6]);
            var path = search.FindPath(3, 3);
            Assert.IsTrue(path.Found);
            CollectionAssert.AreEqual(new[] { 3 }, path.Nodes);
            Assert.AreEqual(0f, path.Cost);
        }

        [Test]
        public void InvalidNodesNeverThrow()
        {
            var (search, _) = Setup(TwoRoutes(), new float[6]);
            Assert.IsFalse(search.FindPath(-1, 5).Found);
            Assert.IsFalse(search.FindPath(0, 99).Found);
        }

        [Test]
        public void MissingLightDataStillFindsARoute()
        {
            var (search, _) = Setup(TwoRoutes(), null);
            Assert.IsTrue(search.FindPath(0, 5).Found);
        }

        [Test]
        public void AStarMatchesDijkstraCostEverywhere()
        {
            var light = new float[] { 0f, 1f, 0.2f, 0.7f, 0f, 0.4f };
            var (search, percepts) = Setup(TwoRoutes(), light);
            percepts.PlayerVisibleNodes = new HashSet<int> { 3 };
            float[] dijkstra = search.CostsFrom(0);
            for (int goal = 0; goal < 6; goal++)
                Assert.AreEqual(dijkstra[goal], search.FindPath(0, goal).Cost, 1e-4f, $"goal {goal}");
        }

        [Test]
        public void StopsAtTheNodeBudget()
        {
            var graph = new LevelGraph();
            for (int i = 0; i < 200; i++) graph.AddNode(i * 4f, 0, "C");
            for (int i = 0; i < 199; i++) graph.AddEdge(i, i + 1, 4f);
            var settings = new StalkerSettings { SearchNodeBudget = 50 };
            var (search, _) = Setup(graph, null, settings);
            var path = search.FindPath(0, 199);
            Assert.IsFalse(path.Found);
            Assert.IsTrue(path.BudgetExceeded);
        }
    }
}

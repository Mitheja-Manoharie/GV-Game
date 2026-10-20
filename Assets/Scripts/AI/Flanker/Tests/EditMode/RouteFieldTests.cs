using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Flanker.Tests
{
    public class RouteFieldTests
    {
        private LevelGraph graph;

        [SetUp]
        public void SetUp() => graph = TestLevel.Graph();

        /// <summary>Walks a path edge by edge: neighbours only, no locked doors. Returns its length.</summary>
        private float Walk(IReadOnlyList<int> path, ICollection<string> locked = null)
        {
            float total = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                GraphEdge step = null;
                foreach (var edge in graph.EdgesOf(path[i]))
                    if (edge.Other(path[i]) == path[i + 1]) step = edge;

                Assert.IsNotNull(step, $"{graph.Nodes[path[i]]} and {graph.Nodes[path[i + 1]]} are not neighbours");
                Assert.IsTrue(LevelGraph.IsPassable(step, locked), $"Path goes through locked door {step.DoorId}");
                total += step.Cost;
            }

            return total;
        }

        [Test]
        public void WithoutThreat_CostsMatchDijkstra_ForEveryPairOfNodes()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorLabSouth };
            foreach (var doors in new[] { null, locked })
            {
                for (int a = 0; a < graph.NodeCount; a++)
                {
                    float[] expected = graph.ShortestDistances(a, doors);
                    var field = RouteField.Compute(graph, a, doors);
                    for (int b = 0; b < graph.NodeCount; b++)
                    {
                        if (float.IsPositiveInfinity(expected[b]))
                        {
                            Assert.IsFalse(field.CanReach(b));
                            CollectionAssert.IsEmpty(field.PathTo(b));
                            continue;
                        }

                        Assert.AreEqual(expected[b], field.CostTo(b), 0.01f);
                        Assert.AreEqual(expected[b], field.LengthTo(b), 0.01f);
                        Assert.AreEqual(expected[b], Walk(field.PathTo(b), doors), 0.01f);
                        Assert.AreEqual(0f, field.ExposedLengthTo(b));
                    }
                }
            }
        }

        [Test]
        public void WatchedRoute_IsAvoided_WhenTheDetourIsWorthIt()
        {
            // The player in the control room looks east, holding the lab route. From the crossroads, the
            // shortest way into the west corridor (just behind the player) is still through the lab;
            // the threat-weighted route goes round through the infirmary instead.
            int from = TestLevel.Node(graph, 10f, 14f);
            int to = TestLevel.Node(graph, -6f, 30f);
            var threat = new ThreatMap(graph, TestLevel.Percepts(graph, (10f, 14f), (2f, 30f), (1f, 0f)), new FlankerSettings());

            var shortest = RouteField.Compute(graph, from, null, threat.Watched, 0f);
            var careful = RouteField.Compute(graph, from, null, threat.Watched, 2f);

            CollectionAssert.Contains(Areas(shortest.PathTo(to)), "ResearchLab");
            CollectionAssert.DoesNotContain(Areas(careful.PathTo(to)), "ResearchLab");
            CollectionAssert.Contains(Areas(careful.PathTo(to)), "Infirmary");

            Assert.Greater(shortest.ExposedLengthTo(to), 0f);
            Assert.AreEqual(0f, careful.ExposedLengthTo(to));
            Assert.Greater(careful.LengthTo(to), shortest.LengthTo(to), "Avoiding the player's view costs extra walking");
            Assert.AreEqual(careful.LengthTo(to), Walk(careful.PathTo(to)), 0.01f);
        }

        [Test]
        public void LockedDoors_AreNeverCrossed()
        {
            int from = TestLevel.Node(graph, 10f, 14f);
            int to = TestLevel.Node(graph, 2f, 30f);
            var locked = new HashSet<string> { LabLockdownLayout.DoorControlEast };

            var field = RouteField.Compute(graph, from, locked);
            Walk(field.PathTo(to), locked);
            CollectionAssert.Contains(Areas(field.PathTo(to)), "WestCorridor");
        }

        [Test]
        public void Cheapest_PicksTheLowestCostMatch()
        {
            var field = RouteField.Compute(graph, TestLevel.Node(graph, 10f, 14f));
            int nearestInfirmary = field.Cheapest(n => graph.Nodes[n].Area == "Infirmary");

            Assert.AreEqual(TestLevel.Node(graph, -10f, 14f), nearestInfirmary);
            Assert.AreEqual(-1, field.Cheapest(n => false));
        }

        [Test]
        public void InvalidInput_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => RouteField.Compute(null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RouteField.Compute(graph, graph.NodeCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => RouteField.Compute(graph, 0, null, null, -1f));
            Assert.Throws<ArgumentException>(() => RouteField.Compute(graph, 0, null, new bool[3], 1f));
        }

        private List<string> Areas(IEnumerable<int> path)
        {
            var areas = new List<string>();
            foreach (int node in path) areas.Add(graph.Nodes[node].Area);
            return areas;
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    /// <summary>Route queries used by the robots (Agent Controller) to move along the level graph.</summary>
    public class ShortestPathTests
    {
        private LevelGraph graph;
        private int start, exit;

        [SetUp]
        public void SetUp()
        {
            graph = LabLockdownLayout.CreateGraph();
            start = graph.FindNearestNode(LabLockdownLayout.PlayerStartX, LabLockdownLayout.PlayerStartZ);
            exit = graph.FindNearestNode(LabLockdownLayout.ExitX, LabLockdownLayout.ExitZ);
        }

        private float Length(int[] path, ICollection<string> locked = null)
        {
            float total = 0f;
            for (int i = 0; i + 1 < path.Length; i++)
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
        public void StartToExit_Is60Metres_AndStartsAndEndsCorrectly()
        {
            int[] path = graph.ShortestPath(start, exit);

            Assert.AreEqual(start, path[0]);
            Assert.AreEqual(exit, path[path.Length - 1]);
            Assert.AreEqual(60f, Length(path), 0.01f);
        }

        [Test]
        public void LockedDoor_IsAvoided()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorLabSouth };
            int[] path = graph.ShortestPath(start, exit, locked);

            Assert.AreEqual(92f, Length(path, locked), 0.01f);
        }

        [Test]
        public void Unreachable_ReturnsEmpty()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorInfirmaryNorth, LabLockdownLayout.DoorLabSouth };
            CollectionAssert.IsEmpty(graph.ShortestPath(start, exit, locked));
        }

        [Test]
        public void SameNode_ReturnsThatNode()
        {
            CollectionAssert.AreEqual(new[] { exit }, graph.ShortestPath(exit, exit));
        }

        [Test]
        public void EveryPath_MatchesDijkstraDistance_ForEveryPairOfNodes()
        {
            var locked = new HashSet<string> { LabLockdownLayout.DoorLabSouth };
            foreach (var doors in new[] { null, locked })
            {
                for (int a = 0; a < graph.NodeCount; a++)
                {
                    float[] distances = graph.ShortestDistances(a, doors);
                    for (int b = 0; b < graph.NodeCount; b++)
                    {
                        int[] path = graph.ShortestPath(a, b, doors);
                        if (float.IsPositiveInfinity(distances[b])) CollectionAssert.IsEmpty(path);
                        else Assert.AreEqual(distances[b], Length(path, doors), 0.01f, $"{graph.Nodes[a]} → {graph.Nodes[b]}");
                    }
                }
            }
        }
    }
}

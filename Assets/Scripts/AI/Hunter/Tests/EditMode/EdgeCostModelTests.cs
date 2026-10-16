using System;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class EdgeCostModelTests
    {
        // DoorId can only be set through LevelGraph.TagDoor, exactly as in the real level.
        private static GraphEdge DoorEdge(float cost = 4f)
        {
            var graph = new LevelGraph();
            graph.AddNode(0f, 0f);
            graph.AddNode(cost, 0f);
            GraphEdge edge = graph.AddEdge(0, 1, cost);
            Assert.IsTrue(graph.TagDoor("Door_Test", cost / 2f, 0f, 0.5f));
            return edge;
        }

        private sealed class ThrowingProvider : IDoorStateProvider
        {
            public DoorState GetState(string doorId)
            {
                throw new InvalidOperationException("A plain edge must not ask about doors.");
            }
        }

        [Test]
        public void PlainEdge_CostsItsLength_WithoutAskingTheProvider()
        {
            var model = new EdgeCostModel();
            Assert.AreEqual(4f, model.Cost(new GraphEdge(0, 1, 4f), new ThrowingProvider()), 1e-6);
        }

        [Test]
        public void OpenDoor_CostsItsLength()
        {
            var doors = new DoorStateTable(DoorState.Open);
            Assert.AreEqual(4f, new EdgeCostModel().Cost(DoorEdge(), doors), 1e-6);
        }

        [Test]
        public void ClosedDoor_AddsThePenalty()
        {
            var doors = new DoorStateTable(DoorState.Closed);
            Assert.AreEqual(4f + EdgeCostModel.DefaultClosedDoorPenalty, new EdgeCostModel().Cost(DoorEdge(), doors), 1e-6);
            Assert.AreEqual(4f + 10f, new EdgeCostModel(10f).Cost(DoorEdge(), doors), 1e-6);
        }

        [Test]
        public void LockedAndBlockedDoors_AreImpassable()
        {
            var model = new EdgeCostModel();
            var locked = new DoorStateTable(DoorState.Locked);
            var blocked = new DoorStateTable(DoorState.Blocked);

            Assert.IsTrue(float.IsPositiveInfinity(model.Cost(DoorEdge(), locked)));
            Assert.IsTrue(float.IsPositiveInfinity(model.Cost(DoorEdge(), blocked)));
        }

        [Test]
        public void UnknownDoor_UsesTheTableDefault()
        {
            var table = new DoorStateTable(DoorState.Closed);
            table.Set("Door_A", DoorState.Open);

            Assert.AreEqual(DoorState.Open, table.GetState("Door_A"));
            Assert.AreEqual(DoorState.Closed, table.GetState("Door_B"));
        }

        [Test]
        public void NegativePenalty_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeCostModel(-1f));
        }

        [Test]
        public void RealLevelGraph_EveryEdgeIsAtLeastTheStraightLine()
        {
            // This is the proof that the Euclidean heuristic is admissible on the real level.
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            Assert.IsTrue(EdgeCostModel.IsHeuristicAdmissible(graph));
        }

        [Test]
        public void AdmissibilityCheck_DetectsAnEdgeShorterThanTheStraightLine()
        {
            var graph = new LevelGraph();
            graph.AddNode(0, 0);
            graph.AddNode(10, 0);
            graph.AddEdge(0, 1, 5f);   // 5 < 10: would make the heuristic overestimate

            Assert.IsFalse(EdgeCostModel.IsHeuristicAdmissible(graph));
        }
    }
}

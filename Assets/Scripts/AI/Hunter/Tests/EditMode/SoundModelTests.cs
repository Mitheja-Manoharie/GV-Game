using System;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class SoundModelTests
    {
        // Four nodes in a row, 4 m apart: 0 - 1 - 2 - 3. The edge 1-2 carries a door.
        private static LevelGraph CreateLine()
        {
            var g = new LevelGraph();
            for (int i = 0; i < 4; i++) g.AddNode(i * 4f, 0f);
            g.AddEdge(0, 1, 4);
            g.AddEdge(1, 2, 4);
            g.AddEdge(2, 3, 4);
            Assert.IsTrue(g.TagDoor("Door_Mid", 6f, 0f, 1f));
            return g;
        }

        // A(0,0) - B(4,0) - C(8,0) with a door on B-C, and a longer bypass A - D(4,8) - C (9 + 9).
        private static LevelGraph CreateDiamond()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0);
            g.AddNode(4, 0);
            g.AddNode(8, 0);
            g.AddNode(4, 8);
            g.AddEdge(0, 1, 4);
            g.AddEdge(1, 2, 4);
            g.AddEdge(0, 3, 9);
            g.AddEdge(3, 2, 9);
            Assert.IsTrue(g.TagDoor("Door_BC", 6f, 0f, 1f));
            return g;
        }

        private static DoorStateTable Doors(string id, DoorState state)
        {
            var table = new DoorStateTable(DoorState.Open);
            table.Set(id, state);
            return table;
        }

        [Test]
        public void SourceNode_HearsTheFullLoudness()
        {
            float[] level = new SoundModel().Propagate(CreateLine(), 0, 10f, Doors("Door_Mid", DoorState.Open));
            Assert.AreEqual(10f, level[0], 1e-5);
        }

        [Test]
        public void OpenAir_LosesOneUnitPerMetre_AndStopsBelowTheThreshold()
        {
            float[] level = new SoundModel().Propagate(CreateLine(), 0, 10f, Doors("Door_Mid", DoorState.Open));

            Assert.AreEqual(10f, level[0], 1e-5);
            Assert.AreEqual(6f, level[1], 1e-5);
            Assert.AreEqual(2f, level[2], 1e-5);
            Assert.AreEqual(0f, level[3], 1e-5);   // 10 - 12 is below zero: inaudible
        }

        [Test]
        public void ThresholdIsInclusive()
        {
            float[] level = new SoundModel().Propagate(CreateLine(), 0, 9f, Doors("Door_Mid", DoorState.Open));
            Assert.AreEqual(1f, level[2], 1e-5);   // exactly the threshold: still heard
        }

        [Test]
        public void NoiseBelowTheThreshold_IsSilentEverywhere()
        {
            float[] level = new SoundModel().Propagate(CreateLine(), 0, 0.5f, Doors("Door_Mid", DoorState.Open));
            foreach (float l in level) Assert.AreEqual(0f, l, 1e-6);
        }

        [Test]
        public void ClosedDoor_MufflesTheSound()
        {
            var model = new SoundModel();
            float[] open = model.Propagate(CreateLine(), 0, 10f, Doors("Door_Mid", DoorState.Open));
            float[] closed = model.Propagate(CreateLine(), 0, 10f, Doors("Door_Mid", DoorState.Closed));

            Assert.AreEqual(2f, open[2], 1e-5);
            Assert.AreEqual(0f, closed[2], 1e-5);   // 10 - 4 - (4 + 3) = -1: inaudible behind the door
        }

        [Test]
        public void LockedAndBlockedDoors_MuffleLikeAClosedOne()
        {
            var model = new SoundModel();
            foreach (DoorState state in new[] { DoorState.Closed, DoorState.Locked, DoorState.Blocked })
            {
                float[] level = model.Propagate(CreateLine(), 0, 20f, Doors("Door_Mid", state));
                Assert.AreEqual(9f, level[2], 1e-5);   // 20 - 4 - (4 + 3)
            }
            Assert.AreEqual(12f, model.Propagate(CreateLine(), 0, 20f, Doors("Door_Mid", DoorState.Open))[2], 1e-5);
        }

        [Test]
        public void TheLoudestRouteWins_NotTheShortest()
        {
            var doors = Doors("Door_BC", DoorState.Closed);

            // Door loss 3: direct 4 + (4 + 3) = 11 beats the bypass 18, so C hears 40 - 11.
            Assert.AreEqual(29f, new SoundModel().Propagate(CreateDiamond(), 0, 40f, doors)[2], 1e-4);

            // Door loss 20: direct 4 + (4 + 20) = 28 is now worse than the bypass 18, so C hears 40 - 18.
            Assert.AreEqual(22f, new SoundModel(1f, 20f, 1f).Propagate(CreateDiamond(), 0, 40f, doors)[2], 1e-4);
        }

        [Test]
        public void RealLevel_MatchesAnIndependentOracle_UnderRandomDoorStates()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var model = new SoundModel();
            var random = new Random(404);

            foreach (float loudness in new[] { 6f, 8f, 12f, 30f })
            {
                for (int round = 0; round < 10; round++)
                {
                    DoorStateTable doors = SearchTestSupport.RandomDoorStates(random, graph);
                    float[,] loss = AllPairsLoss(graph, model, doors);

                    for (int source = 0; source < graph.NodeCount; source++)
                    {
                        float[] level = model.Propagate(graph, source, loudness, doors);
                        for (int n = 0; n < graph.NodeCount; n++)
                        {
                            float expected = loudness - loss[source, n];
                            if (expected < model.Threshold) expected = 0f;
                            Assert.AreEqual(expected, level[n], 1e-3);
                            Assert.IsTrue(level[n] <= loudness + 1e-4);   // never louder than the source
                        }
                    }
                }
            }
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            var model = new SoundModel();
            LevelGraph graph = CreateLine();
            DoorStateTable doors = Doors("Door_Mid", DoorState.Open);

            Assert.Throws<ArgumentOutOfRangeException>(() => model.Propagate(graph, 99, 10f, doors));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Propagate(graph, 0, -1f, doors));
            Assert.Throws<ArgumentNullException>(() => model.Propagate(graph, 0, 10f, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundModel(0f, 3f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundModel(1f, -1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundModel(1f, 3f, 0f));
        }

        // Floyd-Warshall on the loss of every edge: smallest total loss between every pair of nodes.
        private static float[,] AllPairsLoss(LevelGraph graph, SoundModel model, IDoorStateProvider doors)
        {
            int n = graph.NodeCount;
            var d = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    d[i, j] = i == j ? 0f : float.PositiveInfinity;

            foreach (GraphEdge edge in graph.Edges)
            {
                float loss = model.Loss(edge, doors);
                if (loss < d[edge.A, edge.B]) d[edge.A, edge.B] = loss;
                if (loss < d[edge.B, edge.A]) d[edge.B, edge.A] = loss;
            }

            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (d[i, k] + d[k, j] < d[i, j]) d[i, j] = d[i, k] + d[k, j];
            return d;
        }
    }
}

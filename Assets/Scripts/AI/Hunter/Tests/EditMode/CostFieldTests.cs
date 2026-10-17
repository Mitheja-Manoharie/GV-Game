using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class CostFieldTests
    {
        [Test]
        public void StartNodeCostsZero()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            float[] field = CostField.From(graph, new EdgeCostModel(), 7, new DoorStateTable(DoorState.Open));
            Assert.AreEqual(0f, field[7], 1e-6);
        }

        [Test]
        public void MatchesFloydWarshall_UnderRandomDoorStates()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var costs = new EdgeCostModel();
            var random = new Random(77);

            for (int round = 0; round < 25; round++)
            {
                DoorStateTable doors = SearchTestSupport.RandomDoorStates(random, graph);
                float[,] oracle = SearchTestSupport.AllPairs(graph, costs, doors);

                for (int start = 0; start < graph.NodeCount; start++)
                {
                    float[] field = CostField.From(graph, costs, start, doors);
                    for (int n = 0; n < graph.NodeCount; n++)
                    {
                        if (float.IsPositiveInfinity(oracle[start, n]))
                            Assert.IsTrue(float.IsPositiveInfinity(field[n]));
                        else
                            Assert.AreEqual(oracle[start, n], field[n], 1e-3);
                    }
                }
            }
        }

        [Test]
        public void FiniteCostCount_AgreesWithTheFacilityGraphReachability()
        {
            // Cross-check against Student 1's own reachability code with every door locked.
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            var allLocked = new HashSet<string>(graph.DoorIds);
            var doors = new DoorStateTable(DoorState.Locked);

            for (int start = 0; start < graph.NodeCount; start++)
            {
                float[] field = CostField.From(graph, new EdgeCostModel(), start, doors);
                int finite = 0;
                foreach (float c in field) if (!float.IsPositiveInfinity(c)) finite++;

                Assert.AreEqual(graph.CountReachable(start, allLocked), finite);
            }
        }

        [Test]
        public void InvalidStart_Throws()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CostField.From(graph, new EdgeCostModel(), 999, new DoorStateTable()));
        }
    }
}

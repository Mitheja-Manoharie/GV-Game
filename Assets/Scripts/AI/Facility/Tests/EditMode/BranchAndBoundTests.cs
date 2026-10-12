using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Csp;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    public class BranchAndBoundTests
    {
        private readonly BacktrackingSolver solver = new BacktrackingSolver();

        /// <summary>Score = sum of a weight per (variable, value); bound = assigned weights + best weight of each unassigned variable.</summary>
        private sealed class WeightObjective : ISolutionObjective
        {
            private readonly Dictionary<(int, int), double> weights;
            private readonly double[] best;

            public WeightObjective(CspProblem problem, Random random)
            {
                weights = new Dictionary<(int, int), double>();
                best = new double[problem.Variables.Count];
                for (int v = 0; v < problem.Variables.Count; v++)
                {
                    best[v] = double.NegativeInfinity;
                    foreach (int value in problem.Variables[v].Domain)
                    {
                        double w = Math.Round(random.NextDouble() * 10, 3);
                        weights[(v, value)] = w;
                        if (w > best[v]) best[v] = w;
                    }
                }
            }

            public double Weight(int variable, int value) => weights[(variable, value)];

            public double Score(IAssignment complete)
            {
                double total = 0;
                for (int v = 0; v < complete.VariableCount; v++) total += weights[(v, complete[v])];
                return total;
            }

            public double UpperBound(IAssignment partial)
            {
                double total = 0;
                for (int v = 0; v < partial.VariableCount; v++)
                    total += partial.IsAssigned(v) ? weights[(v, partial[v])] : best[v];
                return total;
            }
        }

        [Test]
        public void SolveBest_FindsTheOptimum_OnRandomProblems()
        {
            var random = new Random(4242);
            for (int instance = 0; instance < 150; instance++)
            {
                var problem = RandomProblem(random);
                var objective = new WeightObjective(problem, random);
                double bruteForceBest = BruteForceBest(problem, objective);

                var result = solver.SolveBest(problem, objective, new SolverOptions { ValueScore = (v, value, _) => objective.Weight(v, value) });

                if (double.IsNegativeInfinity(bruteForceBest))
                {
                    Assert.AreEqual(SolveStatus.NoSolution, result.Status, $"Instance {instance}");
                    continue;
                }

                Assert.IsTrue(result.IsSolved, $"Instance {instance}");
                Assert.IsTrue(result.IsProvenOptimal, $"Instance {instance}");
                Assert.IsTrue(problem.IsSolution(result.Values), $"Instance {instance}");
                Assert.AreEqual(bruteForceBest, result.Score, 1e-9, $"Instance {instance}");
            }
        }

        [Test]
        public void SolveBest_PrunesBranchesThatCannotWin()
        {
            // 6 variables, 4 values each, no constraints: 4^6 = 4096 complete assignments.
            var problem = new CspProblem();
            for (int i = 0; i < 6; i++) problem.AddVariable($"X{i}", new[] { 0, 1, 2, 3 });
            var objective = new WeightObjective(problem, new Random(7));

            var result = solver.SolveBest(problem, objective, new SolverOptions { ValueScore = (v, value, _) => objective.Weight(v, value) });

            Assert.IsTrue(result.IsProvenOptimal);
            Assert.Greater(result.Stats.BoundPrunes, 0);
            Assert.Less(result.Stats.SolutionsFound, 4096, "Branch and bound should not enumerate every assignment");
        }

        [Test]
        public void SolveBest_ReturnsBestSoFar_WhenBudgetRunsOut()
        {
            var problem = new CspProblem();
            for (int i = 0; i < 8; i++) problem.AddVariable($"X{i}", new[] { 0, 1, 2, 3, 4 });
            var objective = new WeightObjective(problem, new Random(11));

            var result = solver.SolveBest(problem, objective, new SolverOptions { MaxNodes = 20 });

            Assert.IsTrue(result.IsSolved);
            Assert.IsFalse(result.IsProvenOptimal);
            Assert.IsTrue(problem.IsSolution(result.Values));
        }

        private static CspProblem RandomProblem(Random random)
        {
            var p = new CspProblem();
            int n = random.Next(2, 6);
            for (int i = 0; i < n; i++)
            {
                var domain = new List<int>();
                for (int value = 0; value < 4; value++)
                    if (random.NextDouble() < 0.8) domain.Add(value);
                p.AddVariable($"X{i}", domain);
            }

            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (random.NextDouble() > 0.5) continue;
                var forbidden = new HashSet<(int, int)>();
                for (int k = 0; k < 4; k++) forbidden.Add((random.Next(4), random.Next(4)));
                p.AddBinary($"X{i}-X{j}", i, j, (a, b) => !forbidden.Contains((a, b)));
            }

            return p;
        }

        private static double BruteForceBest(CspProblem problem, WeightObjective objective)
        {
            int n = problem.Variables.Count;
            var values = new int[n];
            double best = double.NegativeInfinity;

            void Try(int v)
            {
                if (v == n)
                {
                    if (!problem.IsSolution(values)) return;
                    var assignment = new Assignment(n);
                    for (int i = 0; i < n; i++) assignment.Assign(i, values[i]);
                    best = Math.Max(best, objective.Score(assignment));
                    return;
                }

                foreach (int value in problem.Variables[v].Domain)
                {
                    values[v] = value;
                    Try(v + 1);
                }
            }

            Try(0);
            return best;
        }
    }
}

using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Csp;
using NUnit.Framework;

namespace LabLockdown.AI.Facility.Tests
{
    public class BacktrackingSolverTests
    {
        private const int Red = 0, Green = 1, Blue = 2;
        private static readonly int[] Colours = { Red, Green, Blue };

        private readonly BacktrackingSolver solver = new BacktrackingSolver();

        /// <summary>Every combination of the solver's features, so each test runs against all of them.</summary>
        private static IEnumerable<SolverOptions> AllVariants()
        {
            foreach (bool mrv in new[] { false, true })
            foreach (bool fc in new[] { false, true })
            foreach (var ordering in new[] { ValueOrdering.DomainOrder, ValueOrdering.LeastConstrainingValue })
                yield return new SolverOptions
                {
                    UseMrv = mrv,
                    UseDegreeTieBreak = mrv,
                    UseForwardChecking = fc,
                    ValueOrdering = ordering,
                };
        }

        private static string Describe(SolverOptions o) =>
            $"MRV={o.UseMrv}, FC={o.UseForwardChecking}, {o.ValueOrdering}";

        // ---------- Lecture 11 example: map colouring of Australia ----------

        private static CspProblem Australia(out Dictionary<string, int> v)
        {
            var p = new CspProblem();
            v = new Dictionary<string, int>();
            foreach (var name in new[] { "WA", "NT", "Q", "NSW", "V", "SA", "T" })
                v[name] = p.AddVariable(name, Colours);

            var borders = new[]
            {
                ("WA", "NT"), ("WA", "SA"), ("NT", "SA"), ("NT", "Q"), ("SA", "Q"),
                ("SA", "NSW"), ("SA", "V"), ("Q", "NSW"), ("NSW", "V"),
            };
            foreach (var (a, b) in borders) p.AddNotEqual(v[a], v[b]);
            return p;
        }

        [Test]
        public void Australia_IsColouredCorrectly_ByEveryVariant()
        {
            var problem = Australia(out _);
            foreach (var options in AllVariants())
            {
                var result = solver.Solve(problem, options);
                Assert.AreEqual(SolveStatus.Solved, result.Status, Describe(options));
                Assert.IsTrue(problem.IsSolution(result.Values), Describe(options));
            }
        }

        [Test]
        public void DegreeHeuristic_PicksSouthAustraliaFirst()
        {
            // At the start every domain has 3 values, so MRV ties and the degree heuristic decides:
            // SA borders 5 regions, more than any other.
            var problem = Australia(out var v);
            var result = solver.Solve(problem, new SolverOptions());
            Assert.AreEqual(v["SA"], result.AssignmentOrder[0]);
        }

        [Test]
        public void Australia_WithMrvAndForwardChecking_NeverBacktracks()
        {
            var result = solver.Solve(Australia(out _), new SolverOptions());
            Assert.AreEqual(0, result.Stats.Backtracks);
            Assert.AreEqual(7, result.Stats.NodesExpanded);
        }

        [Test]
        public void Mrv_PicksVariableWithFewestRemainingValues()
        {
            // Lecture slide: with WA = red and NT = green, SA has only blue left, so MRV picks SA next,
            // even though Q comes first in the static order.
            var problem = new CspProblem();
            int wa = problem.AddVariable("WA", new[] { Red });
            int nt = problem.AddVariable("NT", new[] { Green });
            int q = problem.AddVariable("Q", Colours);
            int sa = problem.AddVariable("SA", Colours);
            problem.AddNotEqual(wa, nt);
            problem.AddNotEqual(wa, sa);
            problem.AddNotEqual(nt, sa);
            problem.AddNotEqual(nt, q);
            problem.AddNotEqual(q, sa);

            var result = solver.Solve(problem, new SolverOptions());
            Assert.IsTrue(result.IsSolved);

            int saPosition = IndexOf(result.AssignmentOrder, sa);
            int qPosition = IndexOf(result.AssignmentOrder, q);
            Assert.Less(saPosition, qPosition, "MRV should assign SA (1 value left) before Q (2 values left)");
            Assert.AreEqual(Blue, result.Values[sa]);
        }

        [Test]
        public void Lcv_ChoosesValueThatLeavesNeighboursMostOptions()
        {
            // Lecture slide: with WA = red and NT = green, choosing blue for Q would leave SA with no colour.
            // Static order WA, NT, Q, SA so Q is chosen before SA.
            CspProblem Build(out int q)
            {
                var p = new CspProblem();
                int wa = p.AddVariable("WA", new[] { Red });
                int nt = p.AddVariable("NT", new[] { Green });
                q = p.AddVariable("Q", new[] { Blue, Red });
                int sa = p.AddVariable("SA", Colours);
                p.AddNotEqual(wa, nt);
                p.AddNotEqual(wa, sa);
                p.AddNotEqual(nt, sa);
                p.AddNotEqual(nt, q);
                p.AddNotEqual(q, sa);
                return p;
            }

            var staticOrder = new SolverOptions { UseMrv = false, UseDegreeTieBreak = false };

            // Domain order tries Q = blue first: forward checking empties SA's domain, so the solver
            // wastes one try before Q = red (5 nodes for 4 variables).
            var domainOrder = solver.Solve(Build(out int q1), staticOrder);
            Assert.AreEqual(Red, domainOrder.Values[q1]);
            Assert.AreEqual(5, domainOrder.Stats.NodesExpanded);

            // LCV sees that blue rules out SA's last value and red rules out nothing, so it tries red
            // first: exactly one node per variable.
            staticOrder.ValueOrdering = ValueOrdering.LeastConstrainingValue;
            var lcv = solver.Solve(Build(out int q2), staticOrder);
            Assert.AreEqual(Red, lcv.Values[q2]);
            Assert.AreEqual(4, lcv.Stats.NodesExpanded);
        }

        // ---------- Failure cases ----------

        [Test]
        public void CompleteGraphOfFour_CannotBeThreeColoured()
        {
            var problem = new CspProblem();
            var nodes = new int[4];
            for (int i = 0; i < 4; i++) nodes[i] = problem.AddVariable($"N{i}", Colours);
            problem.AddAllDifferent(nodes);

            foreach (var options in AllVariants())
                Assert.AreEqual(SolveStatus.NoSolution, solver.Solve(problem, options).Status, Describe(options));
        }

        [Test]
        public void NodeConsistency_DetectsEmptyDomainBeforeSearching()
        {
            var problem = new CspProblem();
            int x = problem.AddVariable("X", new[] { 1, 2, 3 });
            problem.AddVariable("Y", new[] { 1, 2, 3 });
            problem.AddUnary("X > 5", x, value => value > 5);

            var result = solver.Solve(problem, new SolverOptions());
            Assert.AreEqual(SolveStatus.NoSolution, result.Status);
            Assert.AreEqual(0, result.Stats.NodesExpanded);
        }

        [Test]
        public void ForwardChecking_DetectsDeadEndOneLevelEarlier()
        {
            // X in {1}, Y in {1}, X != Y. Plain backtracking only notices when Y is tried;
            // forward checking empties Y's domain as soon as X = 1 and never tries Y.
            var problem = new CspProblem();
            int x = problem.AddVariable("X", new[] { 1 });
            int y = problem.AddVariable("Y", new[] { 1 });
            problem.AddNotEqual(x, y);

            var plain = solver.Solve(problem, SolverOptions.PlainBacktracking());
            var fc = solver.Solve(problem, new SolverOptions { UseMrv = false, UseDegreeTieBreak = false });

            Assert.AreEqual(SolveStatus.NoSolution, fc.Status);
            Assert.AreEqual(2, plain.Stats.NodesExpanded);
            Assert.AreEqual(1, fc.Stats.NodesExpanded);
        }

        [Test]
        public void NodeLimit_StopsSearchWithoutAConclusion()
        {
            var result = solver.Solve(Queens(10), new SolverOptions { UseMrv = false, UseForwardChecking = false, MaxNodes = 5 });
            Assert.AreEqual(SolveStatus.NodeLimitReached, result.Status);
            Assert.IsNull(result.Values);
        }

        // ---------- N-Queens: heuristics reduce the search ----------

        private static CspProblem Queens(int n)
        {
            // Variable i = row of the queen in column i.
            var p = new CspProblem();
            var rows = new int[n];
            for (int i = 0; i < n; i++)
            {
                var domain = new int[n];
                for (int r = 0; r < n; r++) domain[r] = r;
                rows[i] = p.AddVariable($"Q{i}", domain);
            }

            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                int gap = j - i;
                p.AddBinary($"Q{i} safe from Q{j}", rows[i], rows[j], (a, b) => a != b && Math.Abs(a - b) != gap);
            }

            return p;
        }

        [Test]
        public void EightQueens_IsSolvedByEveryVariant()
        {
            var problem = Queens(8);
            foreach (var options in AllVariants())
            {
                var result = solver.Solve(problem, options);
                Assert.AreEqual(SolveStatus.Solved, result.Status, Describe(options));
                Assert.IsTrue(problem.IsSolution(result.Values), Describe(options));
            }
        }

        [Test]
        public void EightQueens_ForwardCheckingAndMrv_ExpandFewerNodes()
        {
            var problem = Queens(8);
            int plain = solver.Solve(problem, SolverOptions.PlainBacktracking()).Stats.NodesExpanded;
            int fcOnly = solver.Solve(problem, new SolverOptions { UseMrv = false, UseDegreeTieBreak = false }).Stats.NodesExpanded;
            int mrvFc = solver.Solve(problem, new SolverOptions()).Stats.NodesExpanded;

            Assert.Less(fcOnly, plain);
            Assert.Less(mrvFc, plain);
        }

        [Test]
        public void Mrv_WithoutForwardChecking_StillCountsLegalValues()
        {
            // Without forward checking domains never shrink, so MRV must count values that are still
            // consistent with the assignment. If it only looked at domain sizes it would match plain backtracking.
            var problem = Queens(8);
            int plain = solver.Solve(problem, SolverOptions.PlainBacktracking()).Stats.NodesExpanded;
            int mrvOnly = solver.Solve(problem, new SolverOptions { UseForwardChecking = false }).Stats.NodesExpanded;
            Assert.Less(mrvOnly, plain);
        }

        // ---------- Other constraint kinds and value ordering ----------

        [Test]
        public void GlobalConstraint_IsEnforced()
        {
            var problem = new CspProblem();
            var vars = new int[3];
            for (int i = 0; i < 3; i++) vars[i] = problem.AddVariable($"V{i}", new[] { 0, 1, 2, 3 });
            // Three different values from {0..3} summing to 6 must be 1, 2 and 3 in some order.
            problem.AddGlobal("sum = 6", vars, a => a[vars[0]] + a[vars[1]] + a[vars[2]] == 6);
            problem.AddAllDifferent(vars);

            foreach (var options in AllVariants())
            {
                var result = solver.Solve(problem, options);
                Assert.IsTrue(result.IsSolved, Describe(options));
                Assert.IsTrue(problem.IsSolution(result.Values), Describe(options));
            }

            // With a target no assignment can reach, every variant must report no solution.
            var impossible = new CspProblem();
            var others = new int[3];
            for (int i = 0; i < 3; i++) others[i] = impossible.AddVariable($"W{i}", new[] { 0, 1, 2, 3 });
            impossible.AddGlobal("sum = 8", others, a => a[others[0]] + a[others[1]] + a[others[2]] == 8);
            impossible.AddAllDifferent(others);
            foreach (var options in AllVariants())
                Assert.AreEqual(SolveStatus.NoSolution, solver.Solve(impossible, options).Status, Describe(options));
        }

        [Test]
        public void ValueScore_TriesHighestScoringValueFirst()
        {
            var problem = new CspProblem();
            int x = problem.AddVariable("X", new[] { 1, 2, 3, 4 });
            problem.AddUnary("X != 4", x, value => value != 4);

            var result = solver.Solve(problem, new SolverOptions { ValueScore = (variable, value, _) => value });
            Assert.AreEqual(3, result.Values[x]);
            Assert.AreEqual(1, result.Stats.NodesExpanded, "The pruned value 4 should never be tried");
        }

        [Test]
        public void AddConstraint_RejectsInvalidScopes()
        {
            var problem = new CspProblem();
            int x = problem.AddVariable("X", new[] { 1 });
            Assert.Throws<ArgumentOutOfRangeException>(() => problem.AddNotEqual(x, 7));
            Assert.Throws<ArgumentException>(() => problem.AddGlobal("empty", new int[0], _ => true));
            Assert.Throws<ArgumentException>(() => problem.AddGlobal("dup", new[] { x, x }, _ => true));
        }

        [Test]
        public void IsSolution_RejectsValuesOutsideTheDomain()
        {
            var problem = new CspProblem();
            problem.AddVariable("X", new[] { 1, 2 });
            Assert.IsTrue(problem.IsSolution(new[] { 2 }));
            Assert.IsFalse(problem.IsSolution(new[] { 5 }));
        }

        // ---------- Correctness against brute force ----------

        [Test]
        public void RandomProblems_EveryVariantAgreesWithBruteForce()
        {
            var random = new Random(3062);
            for (int instance = 0; instance < 200; instance++)
            {
                var problem = RandomProblem(random);
                bool solvable = BruteForceSolvable(problem);

                foreach (var options in AllVariants())
                {
                    var result = solver.Solve(problem, options);
                    Assert.AreEqual(solvable, result.IsSolved, $"Instance {instance}, {Describe(options)}");
                    if (result.IsSolved)
                        Assert.IsTrue(problem.IsSolution(result.Values), $"Instance {instance}, {Describe(options)}");
                }
            }
        }

        private static CspProblem RandomProblem(Random random)
        {
            var p = new CspProblem();
            int n = random.Next(3, 7);
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

            if (random.NextDouble() < 0.5) p.AddUnary("X0 odd", 0, value => value % 2 == 1);

            if (n >= 3 && random.NextDouble() < 0.5)
            {
                int target = random.Next(3, 9);
                p.AddGlobal("X0+X1+X2 <= target", new[] { 0, 1, 2 }, a => a[0] + a[1] + a[2] <= target);
            }

            return p;
        }

        private static bool BruteForceSolvable(CspProblem problem)
        {
            int n = problem.Variables.Count;
            var values = new int[n];

            bool Try(int v)
            {
                if (v == n) return problem.IsSolution(values);
                foreach (int value in problem.Variables[v].Domain)
                {
                    values[v] = value;
                    if (Try(v + 1)) return true;
                }

                return false;
            }

            return Try(0);
        }

        private static int IndexOf(IReadOnlyList<int> list, int item)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == item) return i;
            return -1;
        }
    }
}

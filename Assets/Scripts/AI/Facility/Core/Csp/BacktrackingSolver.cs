using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Facility.Csp
{
    public enum ValueOrdering
    {
        /// <summary>Try values in the order they appear in the domain.</summary>
        DomainOrder,

        /// <summary>LCV: try first the value that rules out the fewest values of neighbouring unassigned variables.</summary>
        LeastConstrainingValue,
    }

    /// <summary>Score for trying <paramref name="value"/> on <paramref name="variable"/>; higher scores are tried first.</summary>
    public delegate double ValueScore(int variable, int value, IAssignment assignment);

    /// <summary>
    /// Objective for <see cref="BacktrackingSolver.SolveBest"/> (branch and bound): the solver returns the
    /// consistent complete assignment with the highest <see cref="Score"/>.
    /// </summary>
    public interface ISolutionObjective
    {
        /// <summary>Score of a complete, consistent assignment. Higher is better.</summary>
        double Score(IAssignment complete);

        /// <summary>
        /// An optimistic estimate: no completion of <paramref name="partial"/> may score higher than this.
        /// The solver skips any branch whose upper bound cannot beat the best solution found so far,
        /// so an estimate that is too low would wrongly discard good solutions.
        /// </summary>
        double UpperBound(IAssignment partial);
    }

    public sealed class SolverOptions
    {
        /// <summary>MRV ("most constrained variable", fail-first): pick the variable with the fewest legal values left.</summary>
        public bool UseMrv { get; set; } = true;

        /// <summary>Degree heuristic as MRV tie-breaker: prefer the variable in the most constraints with unassigned variables.</summary>
        public bool UseDegreeTieBreak { get; set; } = true;

        /// <summary>After each assignment, remove values from neighbours that are now inconsistent; backtrack on an empty domain.</summary>
        public bool UseForwardChecking { get; set; } = true;

        public ValueOrdering ValueOrdering { get; set; } = ValueOrdering.DomainOrder;

        /// <summary>Optional problem-specific value ordering (e.g. utility). When set, it replaces <see cref="ValueOrdering"/>.</summary>
        public ValueScore ValueScore { get; set; }

        /// <summary>Hard cap on search nodes so a decision can never stall the game.</summary>
        public int MaxNodes { get; set; } = 100_000;

        /// <summary>Chronological backtracking with static variable order and no inference (the lecture's baseline).</summary>
        public static SolverOptions PlainBacktracking() => new SolverOptions
        {
            UseMrv = false,
            UseDegreeTieBreak = false,
            UseForwardChecking = false,
        };
    }

    public enum SolveStatus
    {
        Solved,
        NoSolution,

        /// <summary>The node budget ran out before the search finished; no conclusion about solvability.</summary>
        NodeLimitReached,
    }

    public sealed class SolverStats
    {
        /// <summary>Number of (variable, value) pairs tried.</summary>
        public int NodesExpanded { get; internal set; }

        /// <summary>Number of times a variable ran out of values and the search returned to the previous level.</summary>
        public int Backtracks { get; internal set; }

        /// <summary>Number of constraint evaluations, including those made by forward checking and LCV.</summary>
        public int ConstraintChecks { get; internal set; }

        /// <summary>Number of domain values removed by node consistency and forward checking.</summary>
        public int ValuesPruned { get; internal set; }

        /// <summary>Complete solutions found (more than one only when optimising).</summary>
        public int SolutionsFound { get; internal set; }

        /// <summary>Branches skipped by branch and bound because they could not beat the best solution.</summary>
        public int BoundPrunes { get; internal set; }

        public override string ToString() =>
            $"nodes {NodesExpanded}, backtracks {Backtracks}, checks {ConstraintChecks}, pruned {ValuesPruned}, " +
            $"solutions {SolutionsFound}, bound prunes {BoundPrunes}";
    }

    public sealed class SolveResult
    {
        internal SolveResult(SolveStatus status, int[] values, List<int> assignmentOrder, SolverStats stats,
            double score = double.NaN, bool isProvenOptimal = false)
        {
            Status = status;
            Values = values;
            AssignmentOrder = assignmentOrder;
            Stats = stats;
            Score = score;
            IsProvenOptimal = isProvenOptimal;
        }

        public SolveStatus Status { get; }
        public bool IsSolved => Status == SolveStatus.Solved;

        /// <summary>Objective score of <see cref="Values"/> (only set by <see cref="BacktrackingSolver.SolveBest"/>).</summary>
        public double Score { get; }

        /// <summary>
        /// True when branch and bound searched the whole space, so no better solution exists.
        /// False when the node budget ran out first: <see cref="Values"/> is then the best found so far.
        /// </summary>
        public bool IsProvenOptimal { get; }

        /// <summary>Value of each variable (indexed by variable), or null if not solved.</summary>
        public IReadOnlyList<int> Values { get; }

        /// <summary>The order in which variables were assigned on the path to the solution (shows MRV/degree at work).</summary>
        public IReadOnlyList<int> AssignmentOrder { get; }

        public SolverStats Stats { get; }
    }

    /// <summary>
    /// Backtracking search for CSPs (lecture 11, Algorithm 1) with:
    /// node consistency for unary constraints, MRV variable selection with the degree heuristic as
    /// tie-breaker, LCV or a custom score for value ordering, and forward checking.
    /// The solver keeps no state between calls, so one instance can be reused.
    /// </summary>
    public sealed class BacktrackingSolver
    {
        /// <summary>Finds the first solution (or proves there is none).</summary>
        public SolveResult Solve(CspProblem problem, SolverOptions options = null)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            return new Search(problem, options ?? new SolverOptions(), null).Run();
        }

        /// <summary>
        /// Branch and bound: keeps searching after the first solution and returns the highest-scoring one.
        /// Branches whose <see cref="ISolutionObjective.UpperBound"/> cannot beat the current best are skipped.
        /// If the node budget runs out, the best solution found so far is returned with IsProvenOptimal = false.
        /// </summary>
        public SolveResult SolveBest(CspProblem problem, ISolutionObjective objective, SolverOptions options = null)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            return new Search(problem, options ?? new SolverOptions(), objective).Run();
        }

        private sealed class NodeLimitException : Exception
        {
        }

        /// <summary>The state of one solve call.</summary>
        private sealed class Search
        {
            private readonly CspProblem problem;
            private readonly SolverOptions options;
            private readonly int count;

            // Current domains, stored as "is this original domain value still alive?" flags so that
            // pruning and restoring never changes the order values are tried in.
            private readonly int[][] domainValues;
            private readonly bool[][] alive;
            private readonly int[] aliveCount;

            // Constraints with two or more variables, per variable. Unary constraints are applied once,
            // up front, by node consistency, so the search never needs to check them again.
            private readonly List<IConstraint>[] nAryConstraints;

            private readonly Assignment assignment;
            private readonly Stack<(int variable, int index)> trail = new Stack<(int variable, int index)>();
            private readonly List<int> order = new List<int>();
            private readonly SolverStats stats = new SolverStats();

            // Branch and bound state (objective == null means "first solution" mode).
            private readonly ISolutionObjective objective;
            private double bestScore = double.NegativeInfinity;
            private int[] bestValues;
            private List<int> bestOrder;

            public Search(CspProblem problem, SolverOptions options, ISolutionObjective objective)
            {
                this.problem = problem;
                this.options = options;
                this.objective = objective;
                count = problem.Variables.Count;
                assignment = new Assignment(count);

                domainValues = new int[count][];
                alive = new bool[count][];
                aliveCount = new int[count];
                nAryConstraints = new List<IConstraint>[count];

                for (int v = 0; v < count; v++)
                {
                    var domain = problem.Variables[v].Domain;
                    domainValues[v] = new int[domain.Count];
                    for (int i = 0; i < domain.Count; i++) domainValues[v][i] = domain[i];

                    alive[v] = new bool[domain.Count];
                    for (int i = 0; i < domain.Count; i++) alive[v][i] = true;
                    aliveCount[v] = domain.Count;

                    nAryConstraints[v] = new List<IConstraint>();
                    foreach (var constraint in problem.ConstraintsOf(v))
                        if (constraint.Scope.Count > 1) nAryConstraints[v].Add(constraint);
                }
            }

            public SolveResult Run()
            {
                if (!ApplyNodeConsistency())
                    return new SolveResult(SolveStatus.NoSolution, null, new List<int>(), stats);

                bool exhausted;
                bool found;
                try
                {
                    found = Backtrack();
                    exhausted = true;
                }
                catch (NodeLimitException)
                {
                    found = false;
                    exhausted = false;
                }

                if (objective == null)
                {
                    if (found)
                        return new SolveResult(SolveStatus.Solved, assignment.ToArray(), new List<int>(order), stats);
                    return exhausted
                        ? new SolveResult(SolveStatus.NoSolution, null, new List<int>(), stats)
                        : new SolveResult(SolveStatus.NodeLimitReached, null, new List<int>(), stats);
                }

                // Branch and bound: Backtrack() never stops early, so the result is the best seen.
                if (bestValues != null)
                    return new SolveResult(SolveStatus.Solved, bestValues, bestOrder, stats, bestScore, isProvenOptimal: exhausted);
                return exhausted
                    ? new SolveResult(SolveStatus.NoSolution, null, new List<int>(), stats)
                    : new SolveResult(SolveStatus.NodeLimitReached, null, new List<int>(), stats);
            }

            /// <summary>Removes every value that breaks a unary constraint. Returns false if any domain becomes empty.</summary>
            private bool ApplyNodeConsistency()
            {
                foreach (var constraint in problem.Constraints)
                {
                    if (constraint.Scope.Count != 1) continue;
                    int v = constraint.Scope[0];

                    for (int i = 0; i < domainValues[v].Length; i++)
                    {
                        if (!alive[v][i]) continue;
                        if (!Check(constraint, v, domainValues[v][i])) Prune(v, i, record: false);
                    }
                }

                for (int v = 0; v < count; v++)
                    if (aliveCount[v] == 0) return false;

                return true;
            }

            private bool Backtrack()
            {
                if (assignment.IsComplete)
                {
                    stats.SolutionsFound++;
                    if (objective == null) return true;

                    double score = objective.Score(assignment);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestValues = assignment.ToArray();
                        bestOrder = new List<int>(order);
                    }

                    return false; // keep searching for a better solution
                }

                int variable = SelectUnassignedVariable();
                foreach (int index in OrderDomainValues(variable))
                {
                    if (++stats.NodesExpanded > options.MaxNodes) throw new NodeLimitException();

                    assignment.Assign(variable, domainValues[variable][index]);
                    if (IsConsistent(variable))
                    {
                        int mark = trail.Count;
                        if (!options.UseForwardChecking || ForwardCheck(variable))
                        {
                            if (objective != null && objective.UpperBound(assignment) <= bestScore)
                            {
                                stats.BoundPrunes++; // this branch cannot beat the best solution so far
                            }
                            else
                            {
                                order.Add(variable);
                                if (Backtrack()) return true;
                                order.RemoveAt(order.Count - 1);
                            }
                        }

                        Restore(mark);
                    }

                    assignment.Unassign(variable);
                }

                stats.Backtracks++;
                return false;
            }

            /// <summary>
            /// MRV: the unassigned variable with the fewest remaining values (fail-first).
            /// Ties: the degree heuristic (most constraints with other unassigned variables), then lowest index.
            /// Without MRV: the first unassigned variable (static order).
            /// </summary>
            private int SelectUnassignedVariable()
            {
                int best = -1;
                int bestRemaining = int.MaxValue;
                int bestDegree = -1;

                for (int v = 0; v < count; v++)
                {
                    if (assignment.IsAssigned(v)) continue;
                    if (!options.UseMrv) return v;

                    int remaining = LegalValueCount(v);
                    if (remaining < bestRemaining)
                    {
                        best = v;
                        bestRemaining = remaining;
                        bestDegree = -1; // computed lazily, only when a tie needs it
                        continue;
                    }

                    if (remaining == bestRemaining && options.UseDegreeTieBreak)
                    {
                        if (bestDegree < 0) bestDegree = Degree(best);
                        int degree = Degree(v);
                        if (degree > bestDegree)
                        {
                            best = v;
                            bestDegree = degree;
                        }
                    }
                }

                return best;
            }

            /// <summary>
            /// MRV's "legal values left". With forward checking the current domain already holds only values
            /// consistent with the assignment, so its size is the answer. Without it, domains never shrink,
            /// so each value is checked against the constraints whose other variables are all assigned.
            /// </summary>
            private int LegalValueCount(int variable)
            {
                if (options.UseForwardChecking) return aliveCount[variable];

                int legal = 0;
                for (int i = 0; i < domainValues[variable].Length; i++)
                {
                    if (!alive[variable][i]) continue;
                    assignment.Assign(variable, domainValues[variable][i]);
                    bool consistent = IsConsistent(variable);
                    assignment.Unassign(variable);
                    if (consistent) legal++;
                }

                return legal;
            }

            /// <summary>Number of constraints linking <paramref name="variable"/> to at least one other unassigned variable.</summary>
            private int Degree(int variable)
            {
                int degree = 0;
                foreach (var constraint in nAryConstraints[variable])
                {
                    foreach (int other in constraint.Scope)
                    {
                        if (other != variable && !assignment.IsAssigned(other))
                        {
                            degree++;
                            break;
                        }
                    }
                }

                return degree;
            }

            /// <summary>Indices of the variable's remaining values, in the order they should be tried.</summary>
            private List<int> OrderDomainValues(int variable)
            {
                var indices = new List<int>(aliveCount[variable]);
                for (int i = 0; i < domainValues[variable].Length; i++)
                    if (alive[variable][i]) indices.Add(i);

                if (options.ValueScore != null)
                {
                    var scores = new Dictionary<int, double>();
                    foreach (int i in indices)
                        scores[i] = options.ValueScore(variable, domainValues[variable][i], assignment);

                    // Highest score first; ties keep domain order (stable).
                    indices.Sort((x, y) => scores[y] != scores[x] ? scores[y].CompareTo(scores[x]) : x.CompareTo(y));
                }
                else if (options.ValueOrdering == ValueOrdering.LeastConstrainingValue)
                {
                    var ruledOut = new Dictionary<int, int>();
                    foreach (int i in indices) ruledOut[i] = CountRuledOut(variable, i);

                    // Fewest values ruled out first; ties keep domain order (stable).
                    indices.Sort((x, y) => ruledOut[x] != ruledOut[y] ? ruledOut[x].CompareTo(ruledOut[y]) : x.CompareTo(y));
                }

                return indices;
            }

            /// <summary>LCV: how many neighbour values would become illegal if the variable took this value.</summary>
            private int CountRuledOut(int variable, int index)
            {
                assignment.Assign(variable, domainValues[variable][index]);
                int ruledOut = 0;

                foreach (var constraint in nAryConstraints[variable])
                {
                    int other = SingleUnassigned(constraint);
                    if (other < 0) continue;

                    for (int i = 0; i < domainValues[other].Length; i++)
                        if (alive[other][i] && !Check(constraint, other, domainValues[other][i])) ruledOut++;
                }

                assignment.Unassign(variable);
                return ruledOut;
            }

            /// <summary>Checks every constraint on the variable whose scope is now fully assigned.</summary>
            private bool IsConsistent(int variable)
            {
                foreach (var constraint in nAryConstraints[variable])
                {
                    if (!AllAssigned(constraint)) continue;
                    stats.ConstraintChecks++;
                    if (!constraint.IsSatisfied(assignment)) return false;
                }

                return true;
            }

            /// <summary>
            /// Forward checking: for every constraint on the variable that now has exactly one unassigned
            /// variable left, remove that variable's values which would break the constraint.
            /// Returns false as soon as a domain is wiped out, so the search backtracks early.
            /// </summary>
            private bool ForwardCheck(int variable)
            {
                foreach (var constraint in nAryConstraints[variable])
                {
                    int other = SingleUnassigned(constraint);
                    if (other < 0) continue;

                    for (int i = 0; i < domainValues[other].Length; i++)
                    {
                        if (!alive[other][i]) continue;
                        if (!Check(constraint, other, domainValues[other][i])) Prune(other, i, record: true);
                    }

                    if (aliveCount[other] == 0) return false;
                }

                return true;
            }

            /// <summary>Evaluates a constraint with <paramref name="variable"/> temporarily set to <paramref name="value"/>.</summary>
            private bool Check(IConstraint constraint, int variable, int value)
            {
                assignment.Assign(variable, value);
                stats.ConstraintChecks++;
                bool satisfied = constraint.IsSatisfied(assignment);
                assignment.Unassign(variable);
                return satisfied;
            }

            /// <summary>The only unassigned variable in the constraint's scope, or -1 if there are zero or several.</summary>
            private int SingleUnassigned(IConstraint constraint)
            {
                int found = -1;
                foreach (int v in constraint.Scope)
                {
                    if (assignment.IsAssigned(v)) continue;
                    if (found >= 0) return -1;
                    found = v;
                }

                return found;
            }

            private bool AllAssigned(IConstraint constraint)
            {
                foreach (int v in constraint.Scope)
                    if (!assignment.IsAssigned(v)) return false;
                return true;
            }

            private void Prune(int variable, int index, bool record)
            {
                alive[variable][index] = false;
                aliveCount[variable]--;
                stats.ValuesPruned++;
                if (record) trail.Push((variable, index));
            }

            /// <summary>Undoes forward-checking prunes back to <paramref name="mark"/> (the trail length before the assignment).</summary>
            private void Restore(int mark)
            {
                while (trail.Count > mark)
                {
                    var (variable, index) = trail.Pop();
                    alive[variable][index] = true;
                    aliveCount[variable]++;
                }
            }
        }
    }
}

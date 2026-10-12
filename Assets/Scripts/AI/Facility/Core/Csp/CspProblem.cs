using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Facility.Csp
{
    /// <summary>Read-only view of a (possibly partial) assignment of values to variables.</summary>
    public interface IAssignment
    {
        int VariableCount { get; }
        bool IsAssigned(int variable);

        /// <summary>Value of an assigned variable. Throws if the variable is unassigned.</summary>
        int this[int variable] { get; }
    }

    /// <summary>
    /// A constraint over a set of variables (its scope). The solver only calls <see cref="IsSatisfied"/>
    /// once every variable in the scope is assigned, so implementations never see a half-filled scope.
    /// </summary>
    public interface IConstraint
    {
        string Name { get; }
        IReadOnlyList<int> Scope { get; }
        bool IsSatisfied(IAssignment assignment);
    }

    /// <summary>Mutable assignment used by the solver: value plus an "is assigned" flag per variable.</summary>
    public sealed class Assignment : IAssignment
    {
        private readonly int[] values;
        private readonly bool[] assigned;

        public Assignment(int variableCount)
        {
            values = new int[variableCount];
            assigned = new bool[variableCount];
        }

        public int VariableCount => values.Length;
        public int AssignedCount { get; private set; }
        public bool IsComplete => AssignedCount == values.Length;

        public bool IsAssigned(int variable) => assigned[variable];

        public int this[int variable] => assigned[variable]
            ? values[variable]
            : throw new InvalidOperationException($"Variable {variable} is not assigned.");

        public void Assign(int variable, int value)
        {
            if (!assigned[variable]) AssignedCount++;
            assigned[variable] = true;
            values[variable] = value;
        }

        public void Unassign(int variable)
        {
            if (assigned[variable]) AssignedCount--;
            assigned[variable] = false;
        }

        public int[] ToArray() => (int[])values.Clone();
    }

    /// <summary>A CSP variable with a finite domain of integer values.</summary>
    public sealed class CspVariable
    {
        public CspVariable(int index, string name, IReadOnlyList<int> domain)
        {
            Index = index;
            Name = name;
            Domain = domain;
        }

        public int Index { get; }
        public string Name { get; }
        public IReadOnlyList<int> Domain { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// A constraint satisfaction problem: variables X, domains D and constraints C.
    /// Values are integers so one problem can mix kinds of variables, e.g. spawn slots
    /// (value = graph node id) and doors (value = 0 open / 1 locked).
    /// </summary>
    public sealed class CspProblem
    {
        private readonly List<CspVariable> variables = new List<CspVariable>();
        private readonly List<IConstraint> constraints = new List<IConstraint>();
        private readonly List<List<IConstraint>> constraintsByVariable = new List<List<IConstraint>>();

        public IReadOnlyList<CspVariable> Variables => variables;
        public IReadOnlyList<IConstraint> Constraints => constraints;

        /// <summary>Adds a variable and returns its index. Duplicate domain values are ignored.</summary>
        public int AddVariable(string name, IEnumerable<int> domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));

            var values = new List<int>();
            var seen = new HashSet<int>();
            foreach (int value in domain)
                if (seen.Add(value)) values.Add(value);

            var variable = new CspVariable(variables.Count, name ?? $"X{variables.Count}", values);
            variables.Add(variable);
            constraintsByVariable.Add(new List<IConstraint>());
            return variable.Index;
        }

        public void AddConstraint(IConstraint constraint)
        {
            if (constraint == null) throw new ArgumentNullException(nameof(constraint));
            if (constraint.Scope == null || constraint.Scope.Count == 0)
                throw new ArgumentException($"Constraint {constraint.Name} has an empty scope.");

            var seen = new HashSet<int>();
            foreach (int v in constraint.Scope)
            {
                if (v < 0 || v >= variables.Count)
                    throw new ArgumentOutOfRangeException(nameof(constraint), $"Constraint {constraint.Name} uses unknown variable {v}.");
                if (!seen.Add(v))
                    throw new ArgumentException($"Constraint {constraint.Name} lists variable {v} twice.");
            }

            constraints.Add(constraint);
            foreach (int v in constraint.Scope) constraintsByVariable[v].Add(constraint);
        }

        public IReadOnlyList<IConstraint> ConstraintsOf(int variable) => constraintsByVariable[variable];

        public void AddUnary(string name, int variable, Func<int, bool> predicate)
            => AddConstraint(new UnaryConstraint(name, variable, predicate));

        public void AddBinary(string name, int a, int b, Func<int, int, bool> predicate)
            => AddConstraint(new BinaryConstraint(name, a, b, predicate));

        public void AddNotEqual(int a, int b)
            => AddBinary($"{variables[a].Name} != {variables[b].Name}", a, b, (x, y) => x != y);

        /// <summary>All-different, expressed as pairwise not-equal constraints so forward checking can prune it.</summary>
        public void AddAllDifferent(IReadOnlyList<int> scope)
        {
            for (int i = 0; i < scope.Count; i++)
                for (int j = i + 1; j < scope.Count; j++)
                    AddNotEqual(scope[i], scope[j]);
        }

        public void AddGlobal(string name, IReadOnlyList<int> scope, Func<IAssignment, bool> predicate)
            => AddConstraint(new GlobalConstraint(name, scope, predicate));

        /// <summary>
        /// True if <paramref name="values"/> is a complete, consistent assignment: every value is in its
        /// variable's domain and every constraint is satisfied. Used by tests and as a runtime safety check.
        /// </summary>
        public bool IsSolution(IReadOnlyList<int> values)
        {
            if (values == null || values.Count != variables.Count) return false;

            var assignment = new Assignment(variables.Count);
            for (int v = 0; v < values.Count; v++)
            {
                if (!Contains(variables[v].Domain, values[v])) return false;
                assignment.Assign(v, values[v]);
            }

            foreach (var constraint in constraints)
                if (!constraint.IsSatisfied(assignment)) return false;

            return true;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            foreach (int item in list)
                if (item == value) return true;
            return false;
        }
    }

    /// <summary>Constraint on a single variable, e.g. "spawn point not visible to the player".</summary>
    public sealed class UnaryConstraint : IConstraint
    {
        private readonly int variable;
        private readonly Func<int, bool> predicate;

        public UnaryConstraint(string name, int variable, Func<int, bool> predicate)
        {
            Name = name;
            this.variable = variable;
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Scope = new[] { variable };
        }

        public string Name { get; }
        public IReadOnlyList<int> Scope { get; }
        public bool IsSatisfied(IAssignment assignment) => predicate(assignment[variable]);
    }

    /// <summary>Constraint between two variables, e.g. "two enemies may not share a spawn point".</summary>
    public sealed class BinaryConstraint : IConstraint
    {
        private readonly int a;
        private readonly int b;
        private readonly Func<int, int, bool> predicate;

        public BinaryConstraint(string name, int a, int b, Func<int, int, bool> predicate)
        {
            Name = name;
            this.a = a;
            this.b = b;
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Scope = new[] { a, b };
        }

        public string Name { get; }
        public IReadOnlyList<int> Scope { get; }
        public bool IsSatisfied(IAssignment assignment) => predicate(assignment[a], assignment[b]);
    }

    /// <summary>
    /// Constraint over any number of variables, e.g. "with these doors locked, the exit is still reachable".
    /// </summary>
    public sealed class GlobalConstraint : IConstraint
    {
        private readonly Func<IAssignment, bool> predicate;

        public GlobalConstraint(string name, IReadOnlyList<int> scope, Func<IAssignment, bool> predicate)
        {
            Name = name;
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public string Name { get; }
        public IReadOnlyList<int> Scope { get; }
        public bool IsSatisfied(IAssignment assignment) => predicate(assignment);
    }
}

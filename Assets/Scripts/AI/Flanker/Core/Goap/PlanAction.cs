using System;

namespace LabLockdown.AI.Flanker.Goap
{
    /// <summary>
    /// A grounded STRIPS operator. It is applicable when every fact in <see cref="RequireTrue"/> holds and
    /// none in <see cref="RequireFalse"/> does; applying it removes <see cref="Delete"/>, then adds <see cref="Add"/>.
    /// "Grounded" means the action is already bound to concrete data (e.g. "flank to tile 31"), so the planner
    /// never handles variables: the domain creates one action per useful binding before each planning call.
    /// </summary>
    public sealed class PlanAction
    {
        public PlanAction(string name, float cost, ulong requireTrue = 0, ulong requireFalse = 0,
            ulong add = 0, ulong delete = 0, object data = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("An action needs a name.", nameof(name));

            // A* needs strictly positive costs: the heuristic is built on the cheapest action cost.
            if (!(cost > 0f) || float.IsInfinity(cost))
                throw new ArgumentOutOfRangeException(nameof(cost), $"Cost of {name} must be positive and finite.");

            if ((requireTrue & requireFalse) != 0)
                throw new ArgumentException($"{name} requires a fact to be both true and false.");
            if ((add & delete) != 0)
                throw new ArgumentException($"{name} both adds and deletes the same fact.");

            Name = name;
            Cost = cost;
            RequireTrue = requireTrue;
            RequireFalse = requireFalse;
            Add = add;
            Delete = delete;
            Data = data;
        }

        public string Name { get; }
        public float Cost { get; }

        /// <summary>Positive preconditions: facts that must be true.</summary>
        public ulong RequireTrue { get; }

        /// <summary>Negative preconditions: facts that must be false.</summary>
        public ulong RequireFalse { get; }

        /// <summary>Add list: facts made true.</summary>
        public ulong Add { get; }

        /// <summary>Delete list: facts made false.</summary>
        public ulong Delete { get; }

        /// <summary>Domain data carried through the plan (e.g. the target tile and route); ignored by the planner.</summary>
        public object Data { get; }

        public bool IsApplicable(WorldState state) =>
            (state.Bits & RequireTrue) == RequireTrue && (state.Bits & RequireFalse) == 0;

        public WorldState Apply(WorldState state) => new WorldState((state.Bits & ~Delete) | Add);

        public override string ToString() => Name;
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace LabLockdown.AI.Flanker.Goap
{
    /// <summary>
    /// A STRIPS world state: the set of facts that are true. Every fact not in the set is false
    /// (closed-world assumption). Stored as one bit per fact in a 64-bit mask, so copying, comparing and
    /// hashing a state are single integer operations, and applying an action is two bit operations.
    /// </summary>
    public readonly struct WorldState : IEquatable<WorldState>
    {
        public const int MaxFacts = 64;

        public WorldState(ulong bits) => Bits = bits;

        public ulong Bits { get; }

        public static WorldState Of(params int[] trueFacts) => new WorldState(Facts.Mask(trueFacts));

        public bool Has(int fact) => (Bits & Facts.Bit(fact)) != 0;

        public WorldState With(int fact, bool value = true) =>
            new WorldState(value ? Bits | Facts.Bit(fact) : Bits & ~Facts.Bit(fact));

        /// <summary>The true facts by name, e.g. "{InCover, LowHealth}".</summary>
        public string Describe(IReadOnlyList<string> factNames) => Facts.Describe(Bits, factNames);

        public bool Equals(WorldState other) => Bits == other.Bits;
        public override bool Equals(object obj) => obj is WorldState other && Equals(other);
        public override int GetHashCode() => Bits.GetHashCode();
        public static bool operator ==(WorldState a, WorldState b) => a.Bits == b.Bits;
        public static bool operator !=(WorldState a, WorldState b) => a.Bits != b.Bits;

        public override string ToString() => $"0x{Bits:X}";
    }

    /// <summary>Helpers for fact masks: fact i is bit i (0–63).</summary>
    public static class Facts
    {
        public static ulong Bit(int fact)
        {
            if (fact < 0 || fact >= WorldState.MaxFacts)
                throw new ArgumentOutOfRangeException(nameof(fact), $"Fact index must be 0–{WorldState.MaxFacts - 1}.");
            return 1UL << fact;
        }

        public static ulong Mask(params int[] facts)
        {
            ulong mask = 0;
            if (facts != null)
                foreach (int fact in facts) mask |= Bit(fact);
            return mask;
        }

        /// <summary>Number of facts in the mask (BitOperations.PopCount is not in Unity's .NET Standard 2.1).</summary>
        public static int Count(ulong mask)
        {
            int count = 0;
            while (mask != 0)
            {
                mask &= mask - 1; // clears the lowest set bit
                count++;
            }

            return count;
        }

        public static string Describe(ulong mask, IReadOnlyList<string> factNames)
        {
            var text = new StringBuilder("{");
            bool first = true;
            for (int i = 0; i < WorldState.MaxFacts; i++)
            {
                if ((mask & (1UL << i)) == 0) continue;
                if (!first) text.Append(", ");
                first = false;
                text.Append(factNames != null && i < factNames.Count && factNames[i] != null ? factNames[i] : $"#{i}");
            }

            return text.Append('}').ToString();
        }
    }

    /// <summary>
    /// What the planner must achieve: facts that must be true and facts that must be false.
    /// Facts in neither mask are "don't care", so a goal describes a set of states, not a single state.
    /// </summary>
    public readonly struct Goal
    {
        public Goal(ulong mustBeTrue, ulong mustBeFalse = 0)
        {
            if ((mustBeTrue & mustBeFalse) != 0)
                throw new ArgumentException("A fact cannot be required to be both true and false.");

            MustBeTrue = mustBeTrue;
            MustBeFalse = mustBeFalse;
        }

        public ulong MustBeTrue { get; }
        public ulong MustBeFalse { get; }

        public bool IsSatisfiedBy(WorldState state) =>
            (state.Bits & MustBeTrue) == MustBeTrue && (state.Bits & MustBeFalse) == 0;

        /// <summary>Goal facts that do not hold in <paramref name="state"/> yet (the heuristic counts these).</summary>
        public int CountUnsatisfied(WorldState state) =>
            Facts.Count(MustBeTrue & ~state.Bits) + Facts.Count(MustBeFalse & state.Bits);

        public string Describe(IReadOnlyList<string> factNames) =>
            MustBeFalse == 0
                ? Facts.Describe(MustBeTrue, factNames)
                : $"{Facts.Describe(MustBeTrue, factNames)} and not {Facts.Describe(MustBeFalse, factNames)}";
    }
}

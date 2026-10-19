using System;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// Decides where the Hunter goes next by trading "how likely is the player there" against "how far is it".
    ///
    ///     U(n) = belief(n) / (walkingCost(n) + epsilon)        target = the node with the largest U
    ///
    /// Dividing by cost makes a nearby 20% node beat a far-away 25% node, but a very likely far node still wins
    /// over an unlikely near one. epsilon keeps the Hunter's own node finite (cost 0).
    /// Nodes that cannot be reached (locked doors) are never chosen.
    /// With a flat belief the rule picks the nearest unsearched node, so patrol sweeps the level by itself.
    /// </summary>
    public static class TargetSelector
    {
        public const float DefaultEpsilon = 1f;

        /// <summary>Returns the best node id, or -1 when no reachable node has any belief.</summary>
        public static int Choose(BeliefMap belief, float[] costFromHunter, out float utility, float epsilon = DefaultEpsilon)
        {
            if (belief == null) throw new ArgumentNullException(nameof(belief));
            if (costFromHunter == null) throw new ArgumentNullException(nameof(costFromHunter));
            if (costFromHunter.Length != belief.Count)
                throw new ArgumentException("Expected one cost per node.", nameof(costFromHunter));
            if (epsilon <= 0f) throw new ArgumentOutOfRangeException(nameof(epsilon));

            int best = -1;
            utility = 0f;
            for (int n = 0; n < belief.Count; n++)
            {
                float cost = costFromHunter[n];
                if (float.IsPositiveInfinity(cost) || float.IsNaN(cost)) continue;
                if (belief[n] <= 0f) continue;

                float u = belief[n] / (cost + epsilon);
                if (u > utility)   // strict: ties keep the lowest node id, so the choice is deterministic
                {
                    utility = u;
                    best = n;
                }
            }
            return best;
        }
    }
}

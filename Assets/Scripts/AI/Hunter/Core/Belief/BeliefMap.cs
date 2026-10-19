using System;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// What the Hunter believes about where the player is: a probability for every graph node, summing to 1.
    ///
    /// The Hunter never reads the player's true position. It only changes this belief through percepts:
    ///   ApplyNoise    - heard something: probability moves toward nodes near the source
    ///   ApplyShot     - was shot: the shooter's position is known fairly well
    ///   ApplySighting - saw the player: the belief collapses onto that node
    ///   MarkSearched  - reached a node and found nothing: that node becomes unlikely
    ///   Decay         - time passes: an old trail goes cold and the belief drifts back toward "anywhere"
    ///
    /// A noise update BLENDS the new evidence in instead of multiplying it:
    ///     b  <-  (1 - w) * b  +  w * likelihood
    /// Footsteps fire many times a second. Multiplying every repeat would pin the belief to a single node
    /// within a few steps. Blending converges to the likelihood itself, so repeated noise cannot make the
    /// Hunter overconfident.
    /// </summary>
    public sealed class BeliefMap
    {
        private readonly float[] p;
        private readonly BeliefParameters parameters;

        public BeliefMap(int nodeCount, BeliefParameters parameters = null)
        {
            if (nodeCount <= 0) throw new ArgumentOutOfRangeException(nameof(nodeCount));
            this.parameters = parameters ?? new BeliefParameters();
            p = new float[nodeCount];
            Reset();
        }

        /// <summary>Builds a belief from explicit weights (normalised). Used by tests and debug tools.</summary>
        public static BeliefMap FromProbabilities(float[] weights, BeliefParameters parameters = null)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            var map = new BeliefMap(weights.Length, parameters);
            for (int i = 0; i < weights.Length; i++) map.p[i] = Math.Max(0f, weights[i]);
            map.Normalise();
            return map;
        }

        public int Count => p.Length;

        public float this[int node] => p[node];

        public BeliefParameters Parameters => parameters;

        public float Total
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < p.Length; i++) sum += p[i];
                return sum;
            }
        }

        public void Reset()
        {
            float uniform = 1f / p.Length;
            for (int i = 0; i < p.Length; i++) p[i] = uniform;
        }

        public int MostLikelyNode()
        {
            int best = 0;
            for (int i = 1; i < p.Length; i++)
                if (p[i] > p[best]) best = i;
            return best;
        }

        public float[] Snapshot()
        {
            var copy = new float[p.Length];
            Array.Copy(p, copy, p.Length);
            return copy;
        }

        /// <summary>
        /// A noise was heard. distanceFromSource is the walking distance from the noise's node to every node
        /// (infinity if unreachable). heardLevel is the loudness that reached the Hunter; loudness is what was
        /// emitted. Inaudible noise (heardLevel of 0) changes nothing.
        /// </summary>
        public void ApplyNoise(float[] distanceFromSource, float heardLevel, float loudness)
        {
            CheckLength(distanceFromSource);
            if (heardLevel <= 0f) return;

            float sigma = parameters.Sigma0 + parameters.Kappa * Math.Max(0f, loudness - heardLevel);
            float weight = heardLevel / (heardLevel + parameters.NoiseWeightHalf);
            Blend(Likelihood(distanceFromSource, sigma), weight);
        }

        /// <summary>The Hunter was shot. distanceFromShooter is the walking distance from the shooter's node.</summary>
        public void ApplyShot(float[] distanceFromShooter)
        {
            CheckLength(distanceFromShooter);
            Blend(Likelihood(distanceFromShooter, parameters.Sigma0), parameters.ShotWeight);
        }

        /// <summary>The player was seen at this node: all probability moves there.</summary>
        public void ApplySighting(int node)
        {
            CheckNode(node);
            for (int i = 0; i < p.Length; i++) p[i] = 0f;
            p[node] = 1f;
        }

        /// <summary>The Hunter reached this node and the player was not there.</summary>
        public void MarkSearched(int node)
        {
            CheckNode(node);
            p[node] *= parameters.SearchedFactor;
            Normalise();
        }

        /// <summary>Time passes: part of the belief spreads back to every node equally.</summary>
        public void Decay(float seconds)
        {
            if (seconds <= 0f) return;
            float f = Math.Min(1f, parameters.DecayRate * seconds);
            float uniform = 1f / p.Length;
            for (int i = 0; i < p.Length; i++) p[i] = (1f - f) * p[i] + f * uniform;
        }

        /// <summary>Gaussian in walking distance, normalised to sum to 1. Unreachable nodes get 0.</summary>
        public static float[] Likelihood(float[] distance, float sigma)
        {
            var result = new float[distance.Length];
            float sum = 0f;
            float twoSigmaSquared = 2f * sigma * sigma;
            for (int i = 0; i < distance.Length; i++)
            {
                float d = distance[i];
                if (float.IsInfinity(d) || float.IsNaN(d)) continue;
                result[i] = (float)Math.Exp(-(d * d) / twoSigmaSquared);
                sum += result[i];
            }
            if (sum > 0f)
                for (int i = 0; i < result.Length; i++) result[i] /= sum;
            return result;
        }

        private void Blend(float[] likelihood, float weight)
        {
            float sum = 0f;
            for (int i = 0; i < likelihood.Length; i++) sum += likelihood[i];
            if (sum <= 0f) return;   // evidence points nowhere reachable: ignore it

            for (int i = 0; i < p.Length; i++) p[i] = (1f - weight) * p[i] + weight * likelihood[i];
            Normalise();
        }

        private void Normalise()
        {
            float sum = Total;
            if (sum <= 0f || float.IsNaN(sum))
            {
                Reset();
                return;
            }
            for (int i = 0; i < p.Length; i++) p[i] /= sum;
        }

        private void CheckLength(float[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length != p.Length) throw new ArgumentException("Expected one value per node.", nameof(array));
        }

        private void CheckNode(int node)
        {
            if (node < 0 || node >= p.Length) throw new ArgumentOutOfRangeException(nameof(node));
        }
    }
}

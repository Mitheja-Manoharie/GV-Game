using System;

namespace LabLockdown.AI.Hunter
{
    /// <summary>Constants of the belief model. Every value is justified in Docs/HunterWeights.md.</summary>
    public sealed class BeliefParameters
    {
        public const float DefaultSigma0 = 4f;
        public const float DefaultKappa = 0.5f;
        public const float DefaultDecayRate = 0.05f;
        public const float DefaultSearchedFactor = 0.1f;
        public const float DefaultNoiseWeightHalf = 5f;
        public const float DefaultShotWeight = 0.9f;

        public BeliefParameters(
            float sigma0 = DefaultSigma0,
            float kappa = DefaultKappa,
            float decayRate = DefaultDecayRate,
            float searchedFactor = DefaultSearchedFactor,
            float noiseWeightHalf = DefaultNoiseWeightHalf,
            float shotWeight = DefaultShotWeight)
        {
            if (sigma0 <= 0f) throw new ArgumentOutOfRangeException(nameof(sigma0));
            if (kappa < 0f) throw new ArgumentOutOfRangeException(nameof(kappa));
            if (decayRate < 0f) throw new ArgumentOutOfRangeException(nameof(decayRate));
            if (searchedFactor < 0f || searchedFactor > 1f) throw new ArgumentOutOfRangeException(nameof(searchedFactor));
            if (noiseWeightHalf <= 0f) throw new ArgumentOutOfRangeException(nameof(noiseWeightHalf));
            if (shotWeight < 0f || shotWeight > 1f) throw new ArgumentOutOfRangeException(nameof(shotWeight));

            Sigma0 = sigma0;
            Kappa = kappa;
            DecayRate = decayRate;
            SearchedFactor = searchedFactor;
            NoiseWeightHalf = noiseWeightHalf;
            ShotWeight = shotWeight;
        }

        /// <summary>Base localisation error in metres: one tile, the finest the graph can resolve.</summary>
        public float Sigma0 { get; }

        /// <summary>Extra error in metres per unit of loudness lost on the way: muffled sound is vaguer.</summary>
        public float Kappa { get; }

        /// <summary>Fraction of the belief that drifts back to "anywhere" per second.</summary>
        public float DecayRate { get; }

        /// <summary>Multiplier applied to a node the Hunter has searched without finding the player.</summary>
        public float SearchedFactor { get; }

        /// <summary>A noise heard at this level gets weight 0.5 (weight = heard / (heard + this)).</summary>
        public float NoiseWeightHalf { get; }

        /// <summary>Weight of the information in being shot: the shooter's direction is known well.</summary>
        public float ShotWeight { get; }
    }
}

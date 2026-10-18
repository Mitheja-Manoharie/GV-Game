using System;

namespace LabLockdown.AI.Hunter
{
    /// <summary>Constants of the Hunter's decision layer. Justified in Docs/HunterWeights.md.</summary>
    public sealed class HunterBrainParameters
    {
        public const float DefaultChaseMemorySeconds = 4f;
        public const float DefaultInvestigateEnterRatio = 2f;
        public const float DefaultInvestigateExitRatio = 1.5f;
        public const float DefaultReselectInterval = 0.5f;
        public const float DefaultSwitchMargin = 0.25f;

        public HunterBrainParameters(
            float chaseMemorySeconds = DefaultChaseMemorySeconds,
            float investigateEnterRatio = DefaultInvestigateEnterRatio,
            float investigateExitRatio = DefaultInvestigateExitRatio,
            float reselectInterval = DefaultReselectInterval,
            float switchMargin = DefaultSwitchMargin)
        {
            if (chaseMemorySeconds < 0f) throw new ArgumentOutOfRangeException(nameof(chaseMemorySeconds));
            if (investigateExitRatio < 1f) throw new ArgumentOutOfRangeException(nameof(investigateExitRatio));
            if (investigateEnterRatio < investigateExitRatio) throw new ArgumentOutOfRangeException(nameof(investigateEnterRatio), "Enter must not be below exit, or the state would flap.");
            if (reselectInterval <= 0f) throw new ArgumentOutOfRangeException(nameof(reselectInterval));
            if (switchMargin < 0f) throw new ArgumentOutOfRangeException(nameof(switchMargin));

            ChaseMemorySeconds = chaseMemorySeconds;
            InvestigateEnterRatio = investigateEnterRatio;
            InvestigateExitRatio = investigateExitRatio;
            ReselectInterval = reselectInterval;
            SwitchMargin = switchMargin;
        }

        /// <summary>After losing sight the Hunter keeps running at chase speed for this long.</summary>
        public float ChaseMemorySeconds { get; }

        /// <summary>Patrol becomes Investigate when the belief's peak is this many times the flat level.</summary>
        public float InvestigateEnterRatio { get; }

        /// <summary>Investigate falls back to Patrol only when the peak drops below this many times the flat level.</summary>
        public float InvestigateExitRatio { get; }

        /// <summary>The target is re-evaluated at most this often (seconds) unless something new was perceived.</summary>
        public float ReselectInterval { get; }

        /// <summary>A new target must beat the current one's utility by this fraction before the Hunter switches.</summary>
        public float SwitchMargin { get; }
    }
}

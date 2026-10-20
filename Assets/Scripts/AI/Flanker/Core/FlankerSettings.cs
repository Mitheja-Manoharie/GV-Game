using System;

namespace LabLockdown.AI.Flanker
{
    /// <summary>Tuning for the Tactical Flanker. Distances and costs are in metres of walking.</summary>
    public sealed class FlankerSettings
    {
        /// <summary>
        /// Half-angle of the player's attention cone; visible tiles inside it are "held" by the player. 55° matches the
        /// player camera: a 75° vertical field of view is about 108° horizontally at 16:9.
        /// </summary>
        public float ViewHalfAngleDegrees { get; set; } = 55f;

        /// <summary>Straight-line distance from which the flanker can attack the player (with line of sight).</summary>
        public float AttackRange { get; set; } = 12f;

        /// <summary>
        /// Extra cost per metre walked through tiles the player holds: with 2, a metre in the player's view costs
        /// as much as 3 metres out of it. This is what makes the flanker go round by the other route.
        /// </summary>
        public float ExposurePenalty { get; set; } = 2f;

        /// <summary>
        /// Cost of attacking from a position the player is looking at, on top of <see cref="AttackCost"/>: the flanker
        /// accepts up to about 30 m of detour (12 s of walking) to avoid a head-on fight. Chosen from a sweep over every
        /// flanker tile, player tile and facing (see Docs/TacticalFlanker.md).
        /// </summary>
        public float HeadOnPenalty { get; set; } = 30f;

        public float AttackCost { get; set; } = 1f;

        /// <summary>At or below this health the flanker stops attacking and only seeks cover.</summary>
        public float LowHealth01 { get; set; } = 0.3f;

        /// <summary>Smallest cost given to a move, so a zero-length move still has a positive cost (A* requires it).</summary>
        public float MinStepCost { get; set; } = 0.1f;

        public void Validate()
        {
            if (!(ViewHalfAngleDegrees > 0f && ViewHalfAngleDegrees <= 180f))
                throw new ArgumentOutOfRangeException(nameof(ViewHalfAngleDegrees), "Must be in (0, 180].");
            if (!(AttackRange > 0f)) throw new ArgumentOutOfRangeException(nameof(AttackRange), "Must be positive.");
            if (!(ExposurePenalty >= 0f)) throw new ArgumentOutOfRangeException(nameof(ExposurePenalty), "Must not be negative.");
            if (!(HeadOnPenalty >= 0f)) throw new ArgumentOutOfRangeException(nameof(HeadOnPenalty), "Must not be negative.");
            if (!(AttackCost > 0f)) throw new ArgumentOutOfRangeException(nameof(AttackCost), "Must be positive.");
            if (!(LowHealth01 >= 0f && LowHealth01 <= 1f)) throw new ArgumentOutOfRangeException(nameof(LowHealth01), "Must be in [0, 1].");
            if (!(MinStepCost > 0f)) throw new ArgumentOutOfRangeException(nameof(MinStepCost), "Must be positive.");
        }
    }
}

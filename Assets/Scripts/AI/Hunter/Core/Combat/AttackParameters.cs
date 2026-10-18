using System;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// Constants of the Hunter's ranged hitscan attack. Justified in Docs/HunterWeights.md.
    /// Calibrated to the group's agreed balance: 6 damage, one shot every 2.5 s, 50% average hit chance.
    /// </summary>
    public sealed class AttackParameters
    {
        public const float DefaultRange = 12f;
        public const float DefaultWindUp = 0.6f;
        public const float DefaultCooldown = 1.9f;
        public const float DefaultMaxAccuracy = 0.8f;
        public const float DefaultMinAccuracy = 0.2f;
        public const float DefaultDamage = 6f;

        public AttackParameters(
            float range = DefaultRange,
            float windUp = DefaultWindUp,
            float cooldown = DefaultCooldown,
            float maxAccuracy = DefaultMaxAccuracy,
            float minAccuracy = DefaultMinAccuracy,
            float damage = DefaultDamage)
        {
            if (range <= 0f) throw new ArgumentOutOfRangeException(nameof(range));
            if (windUp < 0f) throw new ArgumentOutOfRangeException(nameof(windUp));
            if (cooldown < 0f) throw new ArgumentOutOfRangeException(nameof(cooldown));
            if (maxAccuracy < 0f || maxAccuracy > 1f) throw new ArgumentOutOfRangeException(nameof(maxAccuracy));
            if (minAccuracy < 0f || minAccuracy > maxAccuracy) throw new ArgumentOutOfRangeException(nameof(minAccuracy));
            if (damage < 0f) throw new ArgumentOutOfRangeException(nameof(damage));

            Range = range;
            WindUp = windUp;
            Cooldown = cooldown;
            MaxAccuracy = maxAccuracy;
            MinAccuracy = minAccuracy;
            Damage = damage;
        }

        /// <summary>Furthest distance in metres the Hunter will shoot from.</summary>
        public float Range { get; }

        /// <summary>Seconds the Hunter stands and aims before the shot. Gives the player time to take cover.</summary>
        public float WindUp { get; }

        /// <summary>Seconds after a shot before a new wind-up can start. Time between shots = WindUp + Cooldown (2.5 s by default).</summary>
        public float Cooldown { get; }

        /// <summary>Chance to hit at point-blank range.</summary>
        public float MaxAccuracy { get; }

        /// <summary>Chance to hit at maximum range.</summary>
        public float MinAccuracy { get; }

        /// <summary>Health taken from the player by one hit.</summary>
        public float Damage { get; }
    }
}

using UnityEngine;

namespace GVGame.Combat
{
    /// <summary>Data describing one hit. Passed to IDamageable.TakeDamage.</summary>
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;       // world position of the hit
        public Vector3 direction;   // direction the bullet / attack was travelling
        public GameObject source;   // who caused it (player, robot...)

        public DamageInfo(float amount, Vector3 point, Vector3 direction, GameObject source)
        {
            this.amount = amount;
            this.point = point;
            this.direction = direction;
            this.source = source;
        }
    }

    /// <summary>
    /// Anything that can be shot or hurt: the player, robots, training dummies.
    /// Robots (Intelligent Systems) implement this so the player's weapon can damage them,
    /// and call PlayerHealth.TakeDamage to hurt the player.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(DamageInfo info);
    }

    /// <summary>Anything that can be healed (the player). Used by health packs.</summary>
    public interface IHealable
    {
        bool NeedsHealing { get; }
        void Heal(float amount);
    }
}

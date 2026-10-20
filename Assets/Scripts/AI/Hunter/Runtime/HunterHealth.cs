using System;
using UnityEngine;
using GVGame.Combat;

namespace LabLockdown.AI.Hunter.Runtime
{
    /// <summary>
    /// The Hunter's health. Implementing IDamageable is all the player's weapon needs to hurt it:
    /// WeaponController raycasts, finds this component with GetComponentInParent, and calls TakeDamage.
    /// Group balance: 125 health, so five rifle hits of 25 kill it.
    /// </summary>
    public class HunterHealth : MonoBehaviour, IDamageable
    {
        [Min(1f)] public float maxHealth = 125f;
        [Tooltip("Seconds the body stays after death before it is removed.")]
        public float destroyDelay = 2f;

        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;

        /// <summary>Raised on every hit that lands. The agent uses the source to learn where the shot came from.</summary>
        public event Action<DamageInfo> Damaged;
        public event Action Died;

        void Awake()
        {
            Health = maxHealth;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive || info.amount <= 0f) return;

            Health = Mathf.Max(0f, Health - info.amount);
            Damaged?.Invoke(info);

            if (Health <= 0f)
            {
                Died?.Invoke();
                Destroy(gameObject, destroyDelay);
            }
        }
    }
}

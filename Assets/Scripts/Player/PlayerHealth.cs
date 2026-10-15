using System;
using UnityEngine;
using LabLockdown.AI.Facility.Runtime;
using GVGame.Combat;

namespace GVGame.Player
{
    /// <summary>
    /// Player health. Implements:
    ///   IDamageable           - robots call TakeDamage(DamageInfo)
    ///   IHealable             - health packs call Heal(amount)
    ///   IPlayerStatusSource   - the Facility AI reads Health01 (0..1) to adapt difficulty
    /// Death disables movement and shooting; press R to respawn at the start position.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable, IHealable, IPlayerStatusSource
    {
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Seconds of invulnerability after taking a hit (stops a burst killing instantly).")]
        public float hitCooldown = 0.15f;

        public float Health { get; private set; }
        public float Health01 => Mathf.Clamp01(Health / maxHealth);
        public bool IsAlive => Health > 0f;
        public bool NeedsHealing => IsAlive && Health < maxHealth;

        public event Action<DamageInfo> Damaged;
        public event Action<float> Healed;
        public event Action Died;
        public event Action Respawned;

        float _lastHitTime = -10f;
        Vector3 _spawnPos;
        Quaternion _spawnRot;
        PlayerController _controller;
        Combat.WeaponController _weapon;

        void Awake()
        {
            Health = maxHealth;
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
            _controller = GetComponent<PlayerController>();
            _weapon = GetComponent<Combat.WeaponController>();
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive || info.amount <= 0f) return;
            if (Time.time - _lastHitTime < hitCooldown) return;
            _lastHitTime = Time.time;

            Health = Mathf.Max(0f, Health - info.amount);
            Damaged?.Invoke(info);
            if (Health <= 0f) Die();
        }

        public void Heal(float amount)
        {
            if (!NeedsHealing || amount <= 0f) return;
            float before = Health;
            Health = Mathf.Min(maxHealth, Health + amount);
            Healed?.Invoke(Health - before);
        }

        void Die()
        {
            if (_controller != null) _controller.InputEnabled = false;
            if (_weapon != null) _weapon.enabled = false;
            Died?.Invoke();
        }

        /// <summary>Back to full health at the start position.</summary>
        public void Respawn()
        {
            Health = maxHealth;
            if (_controller != null)
            {
                _controller.Teleport(_spawnPos, _spawnRot);
                _controller.InputEnabled = true;
            }
            if (_weapon != null) { _weapon.enabled = true; _weapon.RefillAmmo(); }
            Respawned?.Invoke();
        }
    }
}

using GVGame.Combat;
using LabLockdown.AI.Hunter.Runtime;
using UnityEngine;

namespace GVGame.Agents
{
    /// <summary>
    /// Puts the Security Hunter (Viduna) in the shared robot body. The Hunter brings its own legs, gun and health
    /// (<see cref="HunterMover"/>, <see cref="HunterShooter"/>, <see cref="HunterHealth"/>); this connects their events
    /// to the body: firing, hit and death animations, and on death the colliders are switched off, as
    /// <see cref="RobotHealth"/> does for the other robots (bullets pass through, no longer blocks doorways).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HunterHealth), typeof(HunterShooter), typeof(RobotAnimator))]
    public class HunterBody : MonoBehaviour
    {
        HunterHealth _health;
        HunterShooter _shooter;
        RobotAnimator _animator;

        void Awake()
        {
            _health = GetComponent<HunterHealth>();
            _shooter = GetComponent<HunterShooter>();
            _animator = GetComponent<RobotAnimator>();
        }

        void OnEnable()
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            _shooter.Fired += OnFired;
        }

        void OnDisable()
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
            _shooter.Fired -= OnFired;
        }

        void OnFired(bool hurt) => _animator.PlayShoot();

        void OnDamaged(DamageInfo info)
        {
            if (_health.IsAlive) _animator.PlayHit();
        }

        void OnDied()
        {
            _animator.PlayDeath();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }
    }
}

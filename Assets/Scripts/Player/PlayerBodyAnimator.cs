using GVGame.Combat;
using UnityEngine;

namespace GVGame.Player
{
    /// <summary>
    /// Animates the player's Alien body with the robots' animator (Robot.controller, built by GV Game > Agent Setup):
    /// idle/walk/run from the CharacterController's ground speed, Shoot when the weapon fires, Hit when hurt,
    /// Death on death, and back to idle on respawn. The rifle in the body's hand shows once the player has picked
    /// up the weapon in the armoury. Movement never comes from the animation (root motion off).
    /// Set up by GV Game > Agent Setup > 7.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerBodyAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int ShootId = Animator.StringToHash("Shoot");
        static readonly int HitId = Animator.StringToHash("Hit");
        static readonly int DeadId = Animator.StringToHash("Dead");

        [Tooltip("The Alien body's Animator.")]
        public Animator animator;
        [Tooltip("The rifle in the body's right hand. Hidden until the player picks up the weapon.")]
        public GameObject rifle;
        [Tooltip("Smoothing time for the Speed parameter (s), so the blend does not flicker.")]
        public float speedDamping = 0.1f;
        [Tooltip("Ground speed (m/s) of the run clip. Running faster plays the clips faster, so the feet keep up.")]
        public float runClipSpeed = 3f;

        CharacterController _controller;
        PlayerHealth _health;
        WeaponController _weapon;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            _weapon = GetComponentInChildren<WeaponController>();

            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // the body may only cast shadows
            }
        }

        void OnEnable()
        {
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.Died += OnDied;
                _health.Respawned += OnRespawned;
            }
            if (_weapon != null) _weapon.Fired += OnFired;
        }

        void OnDisable()
        {
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
                _health.Respawned -= OnRespawned;
            }
            if (_weapon != null) _weapon.Fired -= OnFired;
        }

        void Update()
        {
            bool armed = _weapon != null && _weapon.IsEquipped;
            if (rifle != null && rifle.activeSelf != armed) rifle.SetActive(armed);

            if (animator == null || (_health != null && !_health.IsAlive)) return;

            float speed = 0f;
            if (_controller != null && _controller.enabled)
            {
                Vector3 v = _controller.velocity;
                v.y = 0f;
                speed = v.magnitude;
            }

            animator.SetFloat(SpeedId, speed, speedDamping, Time.deltaTime);
            animator.speed = Mathf.Max(1f, speed / runClipSpeed);
        }

        void OnFired()
        {
            if (animator != null) animator.SetTrigger(ShootId);
        }

        void OnDamaged(DamageInfo info)
        {
            if (animator != null && _health.IsAlive) animator.SetTrigger(HitId);
        }

        void OnDied()
        {
            if (animator == null) return;
            animator.speed = 1f;
            animator.SetFloat(SpeedId, 0f);
            animator.SetBool(DeadId, true);
        }

        void OnRespawned()
        {
            if (animator == null) return;
            animator.Rebind(); // back to idle with every parameter reset (Death has no way out in the controller)
            animator.Update(0f);
        }
    }
}

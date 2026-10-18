using GVGame.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Agents
{
    /// <summary>
    /// Feeds the robot's Animator from what the body is doing. Movement comes from the NavMeshAgent, never from
    /// the animation (root motion off), so the robot stays exactly on its path; the animation is matched to the
    /// real speed instead, which keeps the feet from sliding.
    ///
    /// Animator parameters (built by GV Game > Agent Setup):
    ///   Speed  (float, m/s)  idle 0 / walk 1.5 / run 3 blend
    ///   Aiming (bool)        facing a target
    ///   Shoot, Hit (trigger)
    ///   Dead   (bool)
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int AimingId = Animator.StringToHash("Aiming");
        static readonly int ShootId = Animator.StringToHash("Shoot");
        static readonly int HitId = Animator.StringToHash("Hit");
        static readonly int DeadId = Animator.StringToHash("Dead");

        [Tooltip("The model's Animator. Found in children if empty.")]
        public Animator animator;
        [Tooltip("Smoothing time for the Speed parameter (s), so the blend does not flicker.")]
        public float speedDamping = 0.1f;

        NavMeshAgent _agent;
        RobotPathFollower _follower;
        RobotGun _gun;
        RobotHealth _health;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            _follower = GetComponent<RobotPathFollower>();
            _gun = GetComponent<RobotGun>();
            _health = GetComponent<RobotHealth>();

            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // off-screen robots skip bone updates
            }
        }

        void OnEnable()
        {
            if (_gun != null) _gun.Fired += OnFired;
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.Died += OnDied;
            }
        }

        void OnDisable()
        {
            if (_gun != null) _gun.Fired -= OnFired;
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }
        }

        void Update()
        {
            if (animator == null || (_health != null && !_health.IsAlive)) return;

            float speed = 0f;
            if (_agent != null && _agent.isActiveAndEnabled)
            {
                Vector3 v = _agent.velocity;
                v.y = 0f;
                speed = v.magnitude;
            }

            animator.SetFloat(SpeedId, speed, speedDamping, Time.deltaTime);
            animator.SetBool(AimingId, _follower != null && _follower.FacingTarget != null);
        }

        void OnFired(bool hit)
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
            animator.SetFloat(SpeedId, 0f);
            animator.SetBool(DeadId, true);
        }
    }
}

using System.Collections.Generic;
using GVGame.Combat;
using LabLockdown.AI.Agents;
using LabLockdown.AI.DroneStalker.Runtime;
using UnityEngine;

namespace GVGame.Agents
{
    /// <summary>
    /// The Drone Stalker's body (brain: Ravindu's <see cref="StalkerAgent"/>). It carries out what the brain publishes:
    ///   - <see cref="StalkerAgent.PathChanged"/> → <see cref="RobotPathFollower.SetPath"/>, the same legs as the robots,
    ///     with <see cref="StalkerMoveSpeed"/> mapped onto <see cref="MoveSpeed"/> (Stop stops);
    ///   - <see cref="StalkerAgent.LookTarget"/> → face the player while the drone knows where they are;
    ///   - <see cref="StalkerAgent.StrikeRequested"/> → a lunge and <see cref="strikeDamage"/> to the player.
    /// The drone hovers, so it has no walk animation: the root stays on the NavMesh and only the model floats at
    /// <see cref="hoverHeight"/>, bobbing gently. On death it stops thinking and sinks to the floor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StalkerAgent), typeof(RobotPathFollower), typeof(RobotHealth))]
    public class DroneBody : MonoBehaviour
    {
        [Tooltip("The drone's mesh. Floats, bobs, and lunges forward on a strike.")]
        public Transform model;
        [Tooltip("Height of the model above the floor (m). Same as the StalkerSensor's hover height.")]
        public float hoverHeight = 1.4f;
        public float bobAmplitude = 0.08f;
        public float bobSpeed = 2f;

        [Header("Strike")]
        [Tooltip("Damage of one strike. 15 with the brain's 12 s cooldown is about the group's agreed 1.2 HP/s.")]
        public float strikeDamage = 15f;
        [Tooltip("The strike only lands within this distance of the player (m). The brain strikes from 2.5 m.")]
        public float strikeReach = 3f;
        [Tooltip("How far the model lunges forward when it strikes (m).")]
        public float lungeDistance = 0.6f;

        StalkerAgent _brain;
        RobotPathFollower _follower;
        RobotHealth _health;
        Vector3 _modelBase;
        float _phase;
        float _lunge; // 1 at the moment of a strike, back to 0 in a quarter of a second
        float _sink;  // 0 hovering, 1 on the floor
        bool _dead;

        void Awake()
        {
            _brain = GetComponent<StalkerAgent>();
            _follower = GetComponent<RobotPathFollower>();
            _health = GetComponent<RobotHealth>();
            if (model != null) _modelBase = model.localPosition;
            _phase = Random.value * 2f * Mathf.PI; // drones do not bob in step
        }

        void OnEnable()
        {
            _brain.PathChanged += OnPathChanged;
            _brain.StrikeRequested += OnStrike;
            _health.Died += OnDied;
        }

        void OnDisable()
        {
            _brain.PathChanged -= OnPathChanged;
            _brain.StrikeRequested -= OnStrike;
            _health.Died -= OnDied;
        }

        void Update()
        {
            if (!_dead) _follower.FaceTarget(_brain.LookTarget.HasValue ? _brain.Sensor.Player : null);

            _lunge = Mathf.MoveTowards(_lunge, 0f, 4f * Time.deltaTime);
            if (_dead) _sink = Mathf.MoveTowards(_sink, 1f, 2f * Time.deltaTime);
            if (model == null) return;

            float bob = _dead ? 0f : Mathf.Sin(Time.time * bobSpeed + _phase) * bobAmplitude;
            float drop = _sink * _sink * Mathf.Max(0f, hoverHeight - 0.3f);
            model.localPosition = _modelBase + new Vector3(0f, bob - drop, _lunge * lungeDistance);
        }

        void OnPathChanged(IReadOnlyList<Vector3> path, StalkerMoveSpeed speed)
        {
            if (_dead) return;
            if (speed == StalkerMoveSpeed.Stop || path.Count == 0)
            {
                _follower.Stop();
                return;
            }

            _follower.SetPath(path, ToMoveSpeed(speed));
        }

        static MoveSpeed ToMoveSpeed(StalkerMoveSpeed speed)
        {
            switch (speed)
            {
                case StalkerMoveSpeed.Patrol: return MoveSpeed.Patrol;
                case StalkerMoveSpeed.Investigate: return MoveSpeed.Investigate;
                default: return MoveSpeed.Chase;
            }
        }

        void OnStrike(Transform target)
        {
            if (_dead || target == null) return;
            _lunge = 1f;

            Vector3 from = model != null ? model.position : transform.position + Vector3.up * hoverHeight;
            var col = target.GetComponent<Collider>();
            Vector3 aim = col != null ? col.bounds.center : target.position + Vector3.up;
            if (Vector3.Distance(from, aim) > strikeReach) return;

            var victim = target.GetComponentInParent<IDamageable>();
            if (victim == null || !victim.IsAlive) return;
            victim.TakeDamage(new DamageInfo(strikeDamage, aim, (aim - from).normalized, gameObject));
        }

        void OnDied()
        {
            // RobotHealth has already stopped the legs, switched the colliders off and timed the removal.
            _dead = true;
            _brain.enabled = false;
            _follower.FaceTarget(null);
        }
    }
}

using System;
using System.Collections;
using GVGame.Combat;
using LabLockdown.AI.Agents;
using UnityEngine;

namespace GVGame.Agents
{
    /// <summary>
    /// The robots' rifle. Numbers agreed with the Facility AI's pacing model: 6 damage, one shot every 2.5 s and a
    /// fixed 50 % hit chance, so an engaging robot deals 6 × 0.5 / 2.5 = 1.2 HP/s, the average-player rate the
    /// Director was tuned for. The hit is a dice roll rather than aim physics, so damage per second is predictable.
    /// It only fires within range and with a clear line of fire.
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotGun : MonoBehaviour, IRobotWeapon
    {
        public float damage = 6f;
        [Tooltip("Seconds between shots.")]
        public float fireInterval = 2.5f;
        [Range(0f, 1f)] public float hitChance = 0.5f;
        [Tooltip("Metres. Same as the Tactical Flanker's attack range.")]
        public float range = 12f;

        [Header("Aim")]
        [Tooltip("Where shots start. Defaults to 1.4 m above the robot's pivot.")]
        public Transform muzzle;
        [Tooltip("Height above the target's pivot that is aimed at (chest).")]
        public float aimHeight = 1.2f;
        [Tooltip("What blocks a shot. Set to Default + Doors when the component is added.")]
        public LayerMask blockers;

        [Header("Effects (optional)")]
        public LineRenderer tracer;
        public float tracerSeconds = 0.05f;
        [Tooltip("How far a missed shot lands from the target (m), for the tracer.")]
        public float missOffset = 0.8f;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        float _nextShot;

        /// <summary>Raised for every shot; true if it hit.</summary>
        public event Action<bool> Fired;

        public float Range => range;

        void Reset() => blockers = LayerMask.GetMask("Default", "Doors");

        void Awake()
        {
            if (blockers.value == 0) blockers = LayerMask.GetMask("Default", "Doors");
            if (tracer != null) tracer.enabled = false;
        }

        Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.4f;
        Vector3 AimPoint(Transform target) => target.position + Vector3.up * aimHeight;

        public bool CanHit(Transform target)
        {
            if (target == null || !isActiveAndEnabled) return false;

            Vector3 from = MuzzlePosition, to = AimPoint(target);
            float distance = Vector3.Distance(from, to);
            if (distance > range) return false;

            // The nearest solid thing on the line, ignoring the robot itself, must be the target.
            int count = Physics.RaycastNonAlloc(from, (to - from) / distance, _hits, distance, blockers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Transform blocker = null;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    blocker = hit.collider.transform;
                }
            }

            return blocker == null || blocker.IsChildOf(target);
        }

        public bool TryFire(Transform target)
        {
            if (Time.time < _nextShot || !CanHit(target)) return false;
            _nextShot = Time.time + fireInterval;

            Vector3 from = MuzzlePosition, to = AimPoint(target);
            bool hit = UnityEngine.Random.value < hitChance;
            if (hit)
            {
                var damageable = target.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(new DamageInfo(damage, to, (to - from).normalized, gameObject));
            }
            else
            {
                to += UnityEngine.Random.onUnitSphere * missOffset;
            }

            if (tracer != null) StartCoroutine(ShowTracer(from, to));
            Fired?.Invoke(hit);
            return true;
        }

        IEnumerator ShowTracer(Vector3 from, Vector3 to)
        {
            tracer.positionCount = 2;
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.enabled = true;
            yield return new WaitForSeconds(tracerSeconds);
            tracer.enabled = false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}

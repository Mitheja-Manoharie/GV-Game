using UnityEngine;
using GVGame.Combat;
using GVGame.Player;

namespace LabLockdown.AI.Hunter.Runtime
{
    /// <summary>
    /// Carries out one hitscan shot. The decision (when to shoot and whether the roll hits) comes from
    /// AttackModel in Core; this class only casts the ray and applies the damage, the same way the player's
    /// WeaponController does.
    ///
    /// A shot the model rolled as a miss is aimed visibly to the side, so the player sees it miss.
    /// Damage is applied only when the model said hit AND the ray really reaches the player: a wall or a
    /// closed door in between stops it.
    /// </summary>
    public class HunterShooter : MonoBehaviour
    {
        [Tooltip("Height of the gun above the feet (metres).")]
        public float eyeHeight = 1.5f;
        public float range = 40f;
        [Tooltip("How far a missed shot is aimed off to the side (metres). Larger than the player's width.")]
        public float missOffset = 1.0f;
        public LayerMask hitMask = ~0;
        [Tooltip("Optional unlit material for the tracer. No material = no tracer.")]
        public Material tracerMaterial;

        private static readonly RaycastHit[] Buffer = new RaycastHit[16];

        /// <summary>Returns true when the player was actually hurt.</summary>
        public bool Fire(PlayerHealth player, float damage, bool hit)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 aim = AimPoint(player.transform);

            if (!hit)
            {
                Vector3 side = Vector3.Cross(Vector3.up, aim - eye).normalized;
                Vector2 jitter = Random.insideUnitCircle.normalized * missOffset;
                aim += side * jitter.x + Vector3.up * jitter.y;
            }

            Vector3 direction = (aim - eye).normalized;
            Vector3 end = eye + direction * range;
            bool hurt = false;

            if (NearestHit(transform, eye, direction, range, hitMask, out RaycastHit h))
            {
                end = h.point;
                if (hit && h.collider.GetComponentInParent<PlayerHealth>() == player)
                {
                    player.TakeDamage(new DamageInfo(damage, h.point, direction, gameObject));
                    hurt = true;
                }
            }

            SpawnTracer(eye, end);
            return hurt;
        }

        /// <summary>Centre of the target's collider (the middle of the body), or a point above its pivot.</summary>
        public static Vector3 AimPoint(Transform target)
        {
            Collider col = target.GetComponent<Collider>();
            return col != null ? col.bounds.center : target.position + Vector3.up;
        }

        /// <summary>Nearest solid thing along a ray that is not part of 'self'. Does not allocate.</summary>
        public static bool NearestHit(Transform self, Vector3 origin, Vector3 direction, float distance, LayerMask mask, out RaycastHit best)
        {
            best = default(RaycastHit);
            bool found = false;
            float bestDistance = float.MaxValue;

            int count = Physics.RaycastNonAlloc(origin, direction, Buffer, distance, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (Buffer[i].collider.transform.IsChildOf(self)) continue;
                if (Buffer[i].distance < bestDistance)
                {
                    bestDistance = Buffer[i].distance;
                    best = Buffer[i];
                    found = true;
                }
            }
            return found;
        }

        private void SpawnTracer(Vector3 from, Vector3 to)
        {
            if (tracerMaterial == null) return;

            var go = new GameObject("HunterTracer");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = tracerMaterial;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.03f;
            line.endWidth = 0.015f;
            Destroy(go, 0.06f);
        }
    }
}

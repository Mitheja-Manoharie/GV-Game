using GVGame.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace LabLockdown.AI.Facility.Integration
{
    /// <summary>
    /// Test-only stand-in robot, used by <see cref="RobotSpawnService"/> while no robot prefabs are assigned.
    /// A capsule that walks to the player on the NavMesh, hurts the player at close range, and can be shot.
    /// Its numbers match the Facility AI's pacing simulator (3 m/s, 1.2 health per second at 100 max health),
    /// so the whole loop — spawn, fight, damage, relief — can be tested before the real robots exist.
    /// Not part of the final game: the Agent Controller's robots replace it.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class TestRobot : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public float damagePerSecond = 1.2f;
        public float attackRange = 2f;
        [Tooltip("Damage is dealt in chunks this often (the player ignores hits closer than 0.15 s apart).")]
        public float attackInterval = 0.5f;

        private NavMeshAgent agent;
        private Transform player;
        private IDamageable playerHealth;
        private Renderer body;
        private float health, nextRepath, nextAttack, flashUntil;

        public bool IsAlive => health > 0f;

        /// <summary>Builds a test robot from primitives (no prefab needed).</summary>
        public static GameObject Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            var robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            robot.transform.SetParent(parent, false);
            robot.transform.SetPositionAndRotation(position + Vector3.up, rotation); // capsule pivot is at its centre

            var agent = robot.AddComponent<NavMeshAgent>();
            agent.speed = 3f;
            agent.radius = 0.5f;
            agent.height = 2f;
            agent.baseOffset = 1f;
            agent.stoppingDistance = 1.5f;

            robot.AddComponent<TestRobot>();
            return robot;
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            body = GetComponent<Renderer>();
            health = maxHealth;
            Tint(new Color(0.85f, 0.2f, 0.2f));
        }

        private void Update()
        {
            if (!IsAlive) return;

            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged == null) return;
                player = tagged.transform;
                playerHealth = tagged.GetComponentInParent<IDamageable>();
            }

            if (Time.time >= nextRepath && agent.isOnNavMesh)
            {
                nextRepath = Time.time + 0.5f;
                agent.SetDestination(player.position);
            }

            bool inRange = (player.position - transform.position).sqrMagnitude <= attackRange * attackRange;
            if (inRange && Time.time >= nextAttack && playerHealth != null && playerHealth.IsAlive)
            {
                nextAttack = Time.time + attackInterval;
                Vector3 direction = (player.position - transform.position).normalized;
                playerHealth.TakeDamage(new DamageInfo(damagePerSecond * attackInterval, player.position, direction, gameObject));
            }

            if (flashUntil > 0f && Time.time >= flashUntil)
            {
                flashUntil = 0f;
                Tint(new Color(0.85f, 0.2f, 0.2f));
            }
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            health -= info.amount;
            Tint(Color.white);
            flashUntil = Time.time + 0.1f;
            if (IsAlive) return;

            // Dead: stop counting at once, then disappear.
            if (agent.isOnNavMesh) agent.isStopped = true;
            Tint(new Color(0.3f, 0.3f, 0.3f));
            Destroy(gameObject, 1f);
        }

        private void Tint(Color color)
        {
            if (body == null) return;
            var material = body.material;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
        }
    }
}

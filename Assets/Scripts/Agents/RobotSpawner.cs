using System.Collections.Generic;
using GVGame.Combat;
using GVGame.Player;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Agents
{
    /// <summary>
    /// Spawns real robots for the Facility AI (issue #24). The Director decides when and where; this decides which
    /// robot. Tactical Flankers are strongest in small numbers (a flank only surprises if most robots come head-on),
    /// so at most <see cref="maxFlankersAlive"/> are alive at once and the rest of each wave are Security Hunters.
    /// Assign this to FacilityAI > Facility Director Runner > Spawn Service.
    /// Every robot prefab needs an <see cref="IDamageable"/> (to be shot and counted as dead) and a solid, non-trigger
    /// collider (to be hit, and for the Director to see it standing on a spawn point or in a doorway).
    /// When the player respawns, every robot (alive or dying) is removed and the Director is reset.
    /// </summary>
    public class RobotSpawner : MonoBehaviour, ISpawnService
    {
        [Tooltip("Robot with the Tactical Flanker brain.")]
        public GameObject flankerPrefab;
        [Tooltip("Robot with the Security Hunter brain. If empty, only flankers are spawned.")]
        public GameObject hunterPrefab;
        [Tooltip("1: the second robot of every wave is already a Hunter, so both brains are met even in small waves.")]
        [Min(0)] public int maxFlankersAlive = 1;
        [Tooltip("Parent for spawned robots, to keep the hierarchy tidy. Optional.")]
        public Transform container;
        [Tooltip("The player's health. Found by the Player tag if empty. On respawn, robots are removed and the Director resets.")]
        public PlayerHealth playerHealth;

        struct Spawned
        {
            public GameObject Root;
            public IDamageable Health; // null if the prefab has none: never counted as alive
            public bool IsFlanker;
        }

        readonly List<Spawned> _robots = new List<Spawned>();
        readonly HashSet<GameObject> _checkedPrefabs = new HashSet<GameObject>();
        FacilityDirectorRunner _runner;

        /// <summary>Robots still alive. Dead robots stop counting immediately, not when their body is removed.</summary>
        public int AliveCount => Count(flankersOnly: false);

        public int FlankersAlive => Count(flankersOnly: true);

        void Awake()
        {
            _runner = GetComponentInParent<FacilityDirectorRunner>();
            if (_runner == null) _runner = FindAnyObjectByType<FacilityDirectorRunner>();

            if (playerHealth == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) playerHealth = player.GetComponentInParent<PlayerHealth>();
            }
        }

        void OnEnable()
        {
            if (playerHealth != null) playerHealth.Respawned += OnPlayerRespawned;
        }

        void OnDisable()
        {
            if (playerHealth != null) playerHealth.Respawned -= OnPlayerRespawned;
        }

        public void Spawn(Vector3 position, Quaternion rotation, string spawnPointId)
        {
            bool flanker = hunterPrefab == null || (flankerPrefab != null && FlankersAlive < maxFlankersAlive);
            var prefab = flanker ? flankerPrefab : hunterPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[RobotSpawner] No robot prefab assigned.", this);
                return;
            }

            if (NavMesh.SamplePosition(position, out var onMesh, 2f, NavMesh.AllAreas)) position = onMesh.position;

            var robot = Instantiate(prefab, position, rotation, container);
            robot.name = $"{prefab.name} ({spawnPointId})";

            var agent = robot.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(position);

            if (_checkedPrefabs.Add(prefab)) CheckBody(robot, prefab.name);

            _robots.Add(new Spawned { Root = robot, Health = robot.GetComponent<IDamageable>(), IsFlanker = flanker });
        }

        /// <summary>Removes every spawned robot, including bodies still playing their death animation.</summary>
        [ContextMenu("Despawn All")]
        public void DespawnAll()
        {
            foreach (var robot in _robots)
                if (robot.Root != null) Destroy(robot.Root);
            _robots.Clear();
        }

        void OnPlayerRespawned()
        {
            DespawnAll();
            if (_runner != null) _runner.ResetDirector();
        }

        int Count(bool flankersOnly)
        {
            _robots.RemoveAll(r => r.Root == null); // bodies removed after their death animation

            int count = 0;
            foreach (var robot in _robots)
                if (robot.Health != null && robot.Health.IsAlive && (!flankersOnly || robot.IsFlanker)) count++;
            return count;
        }

        /// <summary>Warns once per prefab if the robot could not be shot, counted as dead, or seen by the Director.</summary>
        static void CheckBody(GameObject robot, string prefabName)
        {
            if (robot.GetComponent<IDamageable>() == null)
                Debug.LogWarning($"[RobotSpawner] {prefabName} has no IDamageable (e.g. RobotHealth) on its root: " +
                                 "it cannot be shot and is never counted as alive.", robot);

            bool solid = false;
            foreach (var c in robot.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger) { solid = true; break; }
            if (!solid)
                Debug.LogWarning($"[RobotSpawner] {prefabName} has no solid (non-trigger) collider: bullets pass through it " +
                                 "and the Director cannot see it on a spawn point or in a doorway.", robot);
        }
    }
}

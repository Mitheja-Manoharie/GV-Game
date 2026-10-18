using System.Collections.Generic;
using GVGame.Combat;
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
    /// </summary>
    public class RobotSpawner : MonoBehaviour, ISpawnService
    {
        [Tooltip("Robot with the Tactical Flanker brain.")]
        public GameObject flankerPrefab;
        [Tooltip("Robot with the Security Hunter brain. If empty, only flankers are spawned.")]
        public GameObject hunterPrefab;
        [Min(0)] public int maxFlankersAlive = 2;
        [Tooltip("Parent for spawned robots, to keep the hierarchy tidy. Optional.")]
        public Transform container;

        struct Spawned
        {
            public IDamageable Health;
            public Object Owner; // for Unity's destroyed check; the interface reference alone is never null
            public bool IsFlanker;
        }

        readonly List<Spawned> _robots = new List<Spawned>();
        readonly HashSet<GameObject> _checkedPrefabs = new HashSet<GameObject>();

        /// <summary>Robots still alive. Dead robots stop counting immediately, not when their body is removed.</summary>
        public int AliveCount
        {
            get
            {
                Prune();
                return _robots.Count;
            }
        }

        public int FlankersAlive
        {
            get
            {
                Prune();
                int count = 0;
                foreach (var robot in _robots)
                    if (robot.IsFlanker) count++;
                return count;
            }
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

            var health = robot.GetComponent<IDamageable>();
            if (health == null) return;

            _robots.Add(new Spawned { Health = health, Owner = (Object)health, IsFlanker = flanker });
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

        void Prune() => _robots.RemoveAll(r => r.Owner == null || !r.Health.IsAlive);
    }
}

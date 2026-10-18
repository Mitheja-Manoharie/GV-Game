using System.Collections.Generic;
using GVGame.Combat;
using GVGame.Player;
using LabLockdown.AI.Facility.Director;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace LabLockdown.AI.Facility.Integration
{
    /// <summary>
    /// Spawns real robots for the Facility AI. The Director decides when, where and how many; this component
    /// decides which prefab, places it on the NavMesh, and counts the robots that are still alive.
    ///
    /// Robot type by pacing phase: <see cref="buildUpRobots"/> during build-up, <see cref="peakRobots"/> during a
    /// peak (the Director already prefers spawn points behind the player at a peak, which suits flankers).
    /// Each list is used in turn (round robin), so a list with a Hunter and a Flanker alternates them.
    ///
    /// A robot counts as alive until it is destroyed, deactivated, or its <see cref="IDamageable.IsAlive"/>
    /// becomes false, so dead robots stop counting immediately, even while a death animation plays.
    /// With no prefabs assigned, <see cref="TestRobot"/> capsules are used so the loop can be tested now.
    ///
    /// Put it on the FacilityAI object next to FacilityDirectorRunner; the runner finds it automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotSpawnService : MonoBehaviour, ISpawnService
    {
        [Header("Robot prefabs")]
        [Tooltip("Spawned during build-up, used in turn. E.g. the Hunter.")]
        [SerializeField] private GameObject[] buildUpRobots = new GameObject[0];

        [Tooltip("Spawned at a peak, used in turn. E.g. the Flanker. Empty = use the build-up list.")]
        [SerializeField] private GameObject[] peakRobots = new GameObject[0];

        [Header("Placement")]
        [Tooltip("Parent for spawned robots (keeps the Hierarchy tidy). Created if empty.")]
        [SerializeField] private Transform robotsParent;

        [Tooltip("How far from the spawn marker to search for the NavMesh.")]
        [SerializeField] private float navMeshSearchRadius = 2f;

        [Header("Respawn")]
        [Tooltip("The player's health. Found by the Player tag if empty. On respawn, robots are removed and the Director resets.")]
        [SerializeField] private PlayerHealth playerHealth;

        private readonly List<SpawnedRobot> robots = new List<SpawnedRobot>();
        private FacilityDirectorRunner runner;
        private int nextBuildUp, nextPeak;
        private bool warnedNoPrefabs;

        private struct SpawnedRobot
        {
            public GameObject Root;
            public IDamageable Health;
        }

        public int AliveCount
        {
            get
            {
                robots.RemoveAll(robot => !IsAlive(robot));
                return robots.Count;
            }
        }

        /// <summary>Robots spawned so far that are still alive.</summary>
        public IEnumerable<GameObject> AliveRobots
        {
            get
            {
                foreach (var robot in robots)
                    if (IsAlive(robot)) yield return robot.Root;
            }
        }

        private void Awake()
        {
            runner = GetComponent<FacilityDirectorRunner>();
            if (runner == null) runner = FindAnyObjectByType<FacilityDirectorRunner>();

            if (playerHealth == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) playerHealth = player.GetComponentInParent<PlayerHealth>();
            }

            if (robotsParent == null) robotsParent = new GameObject("Robots").transform;
        }

        private void OnEnable()
        {
            if (playerHealth != null) playerHealth.Respawned += OnPlayerRespawned;
        }

        private void OnDisable()
        {
            if (playerHealth != null) playerHealth.Respawned -= OnPlayerRespawned;
        }

        public void Spawn(Vector3 position, Quaternion rotation, string spawnPointId)
        {
            DirectorPhase phase = runner != null && runner.LastDecision != null
                ? runner.LastDecision.Evaluation.Phase
                : DirectorPhase.BuildUp;

            Vector3 point = position;
            if (NavMesh.SamplePosition(position, out var hit, navMeshSearchRadius, NavMesh.AllAreas)) point = hit.position;
            else Debug.LogWarning($"[FacilityAI] Spawn point {spawnPointId} is not on the NavMesh.", this);

            var prefab = ChoosePrefab(phase);
            var robot = prefab != null ? Instantiate(prefab, point, rotation, robotsParent) : TestRobot.Create(point, rotation, robotsParent);
            robot.name = $"{(prefab != null ? prefab.name : "TestRobot")} @ {spawnPointId}";

            // Instantiating at the sampled point is not always enough for NavMeshAgents: warp to be sure.
            var agent = robot.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled) agent.Warp(point);

            foreach (var listener in robot.GetComponentsInChildren<IFacilitySpawned>())
                listener.OnFacilitySpawn(spawnPointId, phase);

            robots.Add(new SpawnedRobot { Root = robot, Health = robot.GetComponentInChildren<IDamageable>() });
        }

        /// <summary>Removes every spawned robot (e.g. when the player respawns).</summary>
        [ContextMenu("Despawn All")]
        public void DespawnAll()
        {
            foreach (var robot in robots)
                if (robot.Root != null) Destroy(robot.Root);
            robots.Clear();
        }

        private void OnPlayerRespawned()
        {
            DespawnAll();
            if (runner != null) runner.ResetDirector();
        }

        private GameObject ChoosePrefab(DirectorPhase phase)
        {
            bool usePeakList = phase == DirectorPhase.Peak && HasAny(peakRobots);
            var list = usePeakList ? peakRobots : buildUpRobots;
            if (!HasAny(list))
            {
                if (!warnedNoPrefabs) Debug.LogWarning("[FacilityAI] RobotSpawnService has no robot prefabs; spawning test capsules.", this);
                warnedNoPrefabs = true;
                return null;
            }

            // Round robin over the assigned (non-empty) entries.
            for (int tries = 0; tries < list.Length; tries++)
            {
                int index = usePeakList ? nextPeak++ % list.Length : nextBuildUp++ % list.Length;
                if (list[index] != null) return list[index];
            }

            return null;
        }

        private static bool HasAny(GameObject[] list)
        {
            if (list == null) return false;
            foreach (var prefab in list)
                if (prefab != null) return true;
            return false;
        }

        private static bool IsAlive(SpawnedRobot robot) =>
            robot.Root != null && robot.Root.activeInHierarchy && (robot.Health == null || robot.Health.IsAlive);
    }

    /// <summary>
    /// Optional: implement on a robot script to be told how it was spawned, e.g. a Flanker can start its
    /// flank immediately when spawned at a peak.
    /// </summary>
    public interface IFacilitySpawned
    {
        void OnFacilitySpawn(string spawnPointId, DirectorPhase phase);
    }
}

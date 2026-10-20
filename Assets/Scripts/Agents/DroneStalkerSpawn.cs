using GVGame.Player;
using LabLockdown.AI.DroneStalker.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Agents
{
    /// <summary>
    /// Puts one Drone Stalker in the level at this position when play starts, and a fresh one whenever the player
    /// respawns (death + R, or Play Again on the escape screen), so every run starts with a patrolling drone.
    /// The drone is not part of the Facility AI's waves: it hunts on its own and does not count towards them.
    /// Added by GV Game > Agent Setup > 6.
    /// </summary>
    public class DroneStalkerSpawn : MonoBehaviour
    {
        [Tooltip("Robot_Drone (built by Agent Setup step 4).")]
        public GameObject dronePrefab;
        [Tooltip("The infirmary and the medkits. The drone predicts that a hurt player heads for the nearest one.")]
        public Transform[] healPoints = new Transform[0];
        [Tooltip("The player's health. Found by the Player tag if empty.")]
        public PlayerHealth playerHealth;

        GameObject _drone;

        void Awake()
        {
            if (playerHealth == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) playerHealth = player.GetComponentInParent<PlayerHealth>();
            }
        }

        void OnEnable()
        {
            if (playerHealth != null) playerHealth.Respawned += SpawnDrone;
        }

        void OnDisable()
        {
            if (playerHealth != null) playerHealth.Respawned -= SpawnDrone;
        }

        void Start() => SpawnDrone();

        /// <summary>Removes the current drone (alive or dying) and starts a new one here.</summary>
        [ContextMenu("Respawn Drone")]
        public void SpawnDrone()
        {
            if (_drone != null) Destroy(_drone);
            if (dronePrefab == null)
            {
                Debug.LogWarning("[DroneStalkerSpawn] No drone prefab assigned.", this);
                return;
            }

            Vector3 position = transform.position;
            if (NavMesh.SamplePosition(position, out var onMesh, 2f, NavMesh.AllAreas)) position = onMesh.position;

            _drone = Instantiate(dronePrefab, position, transform.rotation);
            _drone.name = dronePrefab.name;

            var agent = _drone.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(position);

            // Before the drone's Start, which reads them.
            var sensor = _drone.GetComponent<StalkerSensor>();
            if (sensor != null) sensor.SetHealPoints(healPoints);
        }
    }
}

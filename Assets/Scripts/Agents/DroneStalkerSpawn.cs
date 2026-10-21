using System;
using System.Collections;
using System.Collections.Generic;
using GVGame.Player;
using LabLockdown.AI.DroneStalker.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Agents
{
    /// <summary>
    /// Puts Drone Stalkers in the level when play starts: one at this position and one at each of
    /// <see cref="extraStarts"/>. Every drone is re-created when the player respawns (death + R, or Play Again on the
    /// escape screen), and a destroyed drone comes back at its own start after <see cref="respawnDelay"/>, so the same
    /// number of drones is always hunting.
    /// The drones are not part of the Facility AI's waves: each hunts on its own and does not count towards them.
    /// Added by GV Game > Agent Setup > 6.
    /// </summary>
    public class DroneStalkerSpawn : MonoBehaviour
    {
        [Tooltip("Robot_Drone (built by Agent Setup step 4).")]
        public GameObject dronePrefab;
        [Tooltip("Where the other drones start, one drone each. This object's own position is always the first start.")]
        public Transform[] extraStarts = new Transform[0];
        [Tooltip("The infirmary and the medkits. The drone predicts that a hurt player heads for the nearest one.")]
        public Transform[] healPoints = new Transform[0];
        [Tooltip("The player's health. Found by the Player tag if empty.")]
        public PlayerHealth playerHealth;
        [Tooltip("Seconds after a drone is destroyed before a new one comes from its start. 0 or less: no new drone until the player respawns.")]
        public float respawnDelay = 5f;

        // One per start: the drone there now and its pending respawn.
        sealed class Slot
        {
            public Transform Start;
            public GameObject Drone;
            public RobotHealth Health;
            public Action OnDied;
            public Coroutine Respawn;
        }

        readonly List<Slot> _slots = new List<Slot>();

        void Awake()
        {
            if (playerHealth == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) playerHealth = player.GetComponentInParent<PlayerHealth>();
            }

            _slots.Add(new Slot { Start = transform });
            foreach (var start in extraStarts)
                if (start != null) _slots.Add(new Slot { Start = start });
        }

        void OnEnable()
        {
            if (playerHealth != null) playerHealth.Respawned += SpawnDrones;
        }

        void OnDisable()
        {
            if (playerHealth != null) playerHealth.Respawned -= SpawnDrones;
            StopAllCoroutines();
        }

        void Start() => SpawnDrones();

        /// <summary>Removes every drone (alive or dying) and starts a new one at each start.</summary>
        [ContextMenu("Respawn Drones")]
        public void SpawnDrones()
        {
            foreach (var slot in _slots) Spawn(slot);
        }

        void Spawn(Slot slot)
        {
            if (slot.Respawn != null) StopCoroutine(slot.Respawn); // a respawn already waiting is replaced by this one
            slot.Respawn = null;
            if (slot.Health != null) slot.Health.Died -= slot.OnDied;
            slot.Health = null;
            if (slot.Drone != null) Destroy(slot.Drone);
            if (dronePrefab == null)
            {
                Debug.LogWarning("[DroneStalkerSpawn] No drone prefab assigned.", this);
                return;
            }

            Vector3 position = slot.Start.position;
            if (NavMesh.SamplePosition(position, out var onMesh, 2f, NavMesh.AllAreas)) position = onMesh.position;

            slot.Drone = Instantiate(dronePrefab, position, slot.Start.rotation);
            slot.Drone.name = $"{dronePrefab.name} ({slot.Start.name})";

            var agent = slot.Drone.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(position);

            // Before the drone's Start, which reads them.
            var sensor = slot.Drone.GetComponent<StalkerSensor>();
            if (sensor != null) sensor.SetHealPoints(healPoints);

            slot.Health = slot.Drone.GetComponent<RobotHealth>();
            if (slot.Health != null)
            {
                slot.OnDied = () => OnDroneDied(slot);
                slot.Health.Died += slot.OnDied;
            }
        }

        void OnDroneDied(Slot slot)
        {
            if (slot.Health != null) slot.Health.Died -= slot.OnDied;
            slot.Health = null;
            if (respawnDelay > 0f && isActiveAndEnabled) slot.Respawn = StartCoroutine(SpawnAfterDelay(slot));
        }

        IEnumerator SpawnAfterDelay(Slot slot)
        {
            yield return new WaitForSeconds(respawnDelay);
            slot.Respawn = null;
            Spawn(slot);
        }
    }
}

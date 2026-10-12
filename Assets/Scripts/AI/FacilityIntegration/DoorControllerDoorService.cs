using System.Collections.Generic;
using GVGame.Interaction;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;

namespace LabLockdown.AI.Facility.Integration
{
    /// <summary>
    /// Adapter between the Facility AI and the Systems Engineer's doors (GVGame.Interaction.DoorController).
    ///
    /// Lives outside the Facility assemblies on purpose: the Facility code cannot depend on teammates' scripts
    /// (Assembly-CSharp), but Assembly-CSharp can use the Facility's interfaces. This class is the only place
    /// where the two meet.
    ///
    /// Locking a door: close it, lock it, and enable a carving NavMeshObstacle so enemies plan around it
    /// (doors are excluded from the NavMesh bake, so a closed door alone does not stop pathfinding).
    /// Door markers without a built DoorController fall back to a solid red barrier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorControllerDoorService : MonoBehaviour, IDoorService
    {
        [Tooltip("Parent of the Door_* markers (Environment/Doors).")]
        [SerializeField] private Transform doorMarkersRoot;

        private readonly Dictionary<string, DoorController> controllers = new Dictionary<string, DoorController>();
        private readonly Dictionary<string, GameObject> barriers = new Dictionary<string, GameObject>();

        private void Reset()
        {
            var doors = GameObject.Find("Environment/Doors");
            if (doors != null) doorMarkersRoot = doors.transform;
        }

        private void Awake()
        {
            if (doorMarkersRoot == null) Reset();
            if (doorMarkersRoot == null)
            {
                Debug.LogWarning("[FacilityAI] DoorControllerDoorService has no door markers root.", this);
                return;
            }

            foreach (Transform marker in doorMarkersRoot)
            {
                if (!marker.name.StartsWith("Door_")) continue;
                var controller = marker.GetComponentInChildren<DoorController>();
                if (controller != null) controllers[marker.name] = controller;
                else Debug.LogWarning($"[FacilityAI] {marker.name} has no DoorController; a placeholder barrier will be used. " +
                                      "Run GV Game > Systems Setup > Build Doors From Markers.", marker);
            }
        }

        public bool IsLocked(string doorId)
        {
            if (controllers.TryGetValue(doorId, out var controller)) return controller.IsLocked;
            return barriers.TryGetValue(doorId, out var barrier) && barrier != null && barrier.activeSelf;
        }

        public void SetLocked(string doorId, bool locked)
        {
            var marker = doorMarkersRoot != null ? doorMarkersRoot.Find(doorId) : null;
            if (marker == null)
            {
                Debug.LogWarning($"[FacilityAI] No door marker named {doorId}.", this);
                return;
            }

            bool hasController = controllers.TryGetValue(doorId, out var controller);
            if (hasController)
            {
                if (locked) controller.Close();
                controller.SetLocked(locked);
            }

            // Real door: invisible carving obstacle only. No door yet: solid red barrier.
            if (!barriers.TryGetValue(doorId, out var barrier) || barrier == null)
            {
                barrier = DoorLockBarrier.Create(marker, solid: !hasController);
                barriers[doorId] = barrier;
            }

            barrier.SetActive(locked);
        }
    }
}

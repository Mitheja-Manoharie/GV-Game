using System.Collections.Generic;
using UnityEngine;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// Stand-in door system: a locked door is a solid red barrier that also carves the NavMesh.
    /// Lets the Facility AI be tested before the real doors are built in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceholderDoorService : MonoBehaviour, IDoorService
    {
        [Tooltip("Parent of the Door_* markers (Environment/Doors).")]
        [SerializeField] private Transform doorMarkersRoot;

        [Tooltip("Optional material for the barrier; defaults to plain red.")]
        [SerializeField] private Material barrierMaterial;

        private readonly Dictionary<string, GameObject> barriers = new Dictionary<string, GameObject>();

        private void Reset()
        {
            var doors = GameObject.Find("Environment/Doors");
            if (doors != null) doorMarkersRoot = doors.transform;
        }

        public bool IsLocked(string doorId) =>
            barriers.TryGetValue(doorId, out var barrier) && barrier != null && barrier.activeSelf;

        public void SetLocked(string doorId, bool locked)
        {
            if (!barriers.TryGetValue(doorId, out var barrier) || barrier == null)
            {
                var marker = FindMarker(doorId);
                if (marker == null)
                {
                    Debug.LogWarning($"[FacilityAI] No door marker named {doorId} under {(doorMarkersRoot ? doorMarkersRoot.name : "(unassigned)")}.", this);
                    return;
                }

                barrier = DoorLockBarrier.Create(marker, solid: true, barrierMaterial);
                barriers[doorId] = barrier;
            }

            barrier.SetActive(locked);
        }

        private Transform FindMarker(string doorId)
        {
            if (doorMarkersRoot == null)
            {
                var doors = GameObject.Find("Environment/Doors");
                if (doors != null) doorMarkersRoot = doors.transform;
            }

            return doorMarkersRoot != null ? doorMarkersRoot.Find(doorId) : null;
        }
    }
}

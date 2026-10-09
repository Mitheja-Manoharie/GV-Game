using UnityEngine;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// Marks a location where the Facility AI may spawn enemies.
    /// The Facility AI maps each marker to its nearest graph node; the CSP then chooses among them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPointMarker : MonoBehaviour
    {
        [Tooltip("Unique, readable id, e.g. SP_MaintenanceBay. Defaults to the GameObject name.")]
        [SerializeField] private string spawnId;

        public string SpawnId => string.IsNullOrEmpty(spawnId) ? name : spawnId;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.85f, 0.2f, 0.9f);
            Vector3 p = transform.position;
            Gizmos.DrawWireCube(p + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
            Gizmos.DrawLine(p, p + transform.forward * 1.2f);
        }
    }
}

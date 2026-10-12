using UnityEngine;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// How the Facility AI spawns enemies. Implemented by a placeholder now, and later by the
    /// Agent Controller's robot spawner. Any MonoBehaviour implementing this can be assigned to the runner.
    /// </summary>
    public interface ISpawnService
    {
        /// <summary>Enemies currently alive (spawned by this service).</summary>
        int AliveCount { get; }

        void Spawn(Vector3 position, Quaternion rotation, string spawnPointId);
    }

    /// <summary>How the Facility AI locks and unlocks doors, keyed by the Door_* marker name.</summary>
    public interface IDoorService
    {
        bool IsLocked(string doorId);
        void SetLocked(string doorId, bool locked);
    }

    /// <summary>The player's health, 0 to 1. Implemented by the Core Developer's player code.</summary>
    public interface IPlayerStatusSource
    {
        float Health01 { get; }
    }
}

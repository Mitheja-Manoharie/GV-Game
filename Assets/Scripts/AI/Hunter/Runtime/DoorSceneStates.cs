using System.Collections.Generic;
using UnityEngine;
using GVGame.Interaction;

namespace LabLockdown.AI.Hunter.Runtime
{
    /// <summary>
    /// Door states as the planner sees them, read live from the DoorController objects in the scene.
    /// The graph names its doors "Door_ArmouryNorth" and so on; this finds the DoorController that belongs to
    /// each name (the controller itself or the nearest parent called Door_*).
    /// A graph door with no DoorController in the scene (the markers were never built into doors) counts as
    /// open: there is nothing physically in the way.
    /// </summary>
    public sealed class DoorSceneStates : IDoorStateProvider
    {
        private readonly Dictionary<string, DoorController> doors = new Dictionary<string, DoorController>();

        public DoorSceneStates()
        {
            Refresh();
        }

        public int Count => doors.Count;

        public void Refresh()
        {
            doors.Clear();
            DoorController[] found = Object.FindObjectsByType<DoorController>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
                doors[ResolveDoorId(found[i].transform)] = found[i];
        }

        public bool TryGet(string doorId, out DoorController door)
        {
            return doors.TryGetValue(doorId, out door) && door != null;
        }

        public DoorState GetState(string doorId)
        {
            if (!TryGet(doorId, out DoorController door)) return DoorState.Open;
            if (door.IsLocked) return DoorState.Locked;

            switch (door.Current)
            {
                case DoorController.State.Closed:
                case DoorController.State.Closing:
                    return DoorState.Closed;
                default:
                    return DoorState.Open;   // Opening or Open: passable, no opening delay to pay
            }
        }

        private static string ResolveDoorId(Transform t)
        {
            for (Transform current = t; current != null; current = current.parent)
                if (current.name.StartsWith("Door_")) return current.name;
            return t.name;
        }
    }
}

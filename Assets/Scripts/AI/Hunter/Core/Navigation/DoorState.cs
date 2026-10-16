using System.Collections.Generic;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// What the Hunter believes about a door when it plans a route.
    /// Open    : walk straight through.
    /// Closed  : passable, but the Hunter has to open it first (costs time).
    /// Locked  : impassable (locked by the Facility AI).
    /// Blocked : impassable (jammed by a barricade the Hunter has run into).
    /// </summary>
    public enum DoorState
    {
        Open,
        Closed,
        Locked,
        Blocked
    }

    /// <summary>Supplies door states to the planner. The Unity side implements this over DoorController.</summary>
    public interface IDoorStateProvider
    {
        DoorState GetState(string doorId);
    }

    /// <summary>
    /// Simple in-memory provider. Doors without an explicit entry report <see cref="DefaultState"/>.
    /// Used by tests and as a fallback when no real doors exist in the scene yet.
    /// </summary>
    public sealed class DoorStateTable : IDoorStateProvider
    {
        private readonly Dictionary<string, DoorState> states = new Dictionary<string, DoorState>();

        public DoorStateTable(DoorState defaultState = DoorState.Closed)
        {
            DefaultState = defaultState;
        }

        public DoorState DefaultState { get; }

        public void Set(string doorId, DoorState state) => states[doorId] = state;

        public DoorState GetState(string doorId)
        {
            return states.TryGetValue(doorId, out DoorState state) ? state : DefaultState;
        }
    }
}

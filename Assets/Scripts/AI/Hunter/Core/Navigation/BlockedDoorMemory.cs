using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// The Hunter's memory of doors it found jammed. The door scripts give no "blocked by a barricade" signal
    /// that lasts, so when the Hunter orders a door open and it stays shut, the Hunter writes that down here.
    /// For the next ForgetSeconds this door reports Blocked (impassable) to the planner, which sends the
    /// Hunter around. After that the door is trusted again, because a barricade can be dragged away.
    /// Everything else is passed through to the real door states.
    ///
    /// This is the part of the Hunter that is model-based in the strict sense: it keeps a belief about the
    /// world that its sensors do not report directly, and the belief expires.
    /// </summary>
    public sealed class BlockedDoorMemory : IDoorStateProvider
    {
        public const float DefaultForgetSeconds = 20f;

        private readonly IDoorStateProvider inner;
        private readonly Dictionary<string, float> blockedUntil = new Dictionary<string, float>();

        public BlockedDoorMemory(IDoorStateProvider inner, float forgetSeconds = DefaultForgetSeconds)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (forgetSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(forgetSeconds));
            ForgetSeconds = forgetSeconds;
        }

        public float ForgetSeconds { get; }

        /// <summary>Current time in seconds. The caller advances this every tick.</summary>
        public float Now { get; set; }

        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (float until in blockedUntil.Values)
                    if (Now < until) count++;
                return count;
            }
        }

        /// <summary>Remember this door as jammed from now until Now + ForgetSeconds.</summary>
        public void MarkBlocked(string doorId)
        {
            if (string.IsNullOrEmpty(doorId)) throw new ArgumentException("A door id is required.", nameof(doorId));
            blockedUntil[doorId] = Now + ForgetSeconds;
        }

        public void Clear() => blockedUntil.Clear();

        public DoorState GetState(string doorId)
        {
            if (blockedUntil.TryGetValue(doorId, out float until))
            {
                if (Now < until) return DoorState.Blocked;
                blockedUntil.Remove(doorId);   // forgotten: trust the real door again
            }
            return inner.GetState(doorId);
        }
    }
}

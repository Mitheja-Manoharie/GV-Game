using System;
using UnityEngine;

namespace LabLockdown.AI.Agents
{
    /// <summary>A robot's health as a brain sees it (read only). Implemented by the robot body.</summary>
    public interface IRobotHealth
    {
        /// <summary>0 (dead) to 1 (full).</summary>
        float Health01 { get; }

        bool IsAlive { get; }

        event Action Died;
    }

    /// <summary>A robot's gun as a brain sees it. Implemented by the robot body.</summary>
    public interface IRobotWeapon
    {
        /// <summary>Maximum distance to the target, in metres.</summary>
        float Range { get; }

        /// <summary>True when the target is within range and nothing solid is in the line of fire.</summary>
        bool CanHit(Transform target);

        /// <summary>Fires at the target if the gun is ready and <see cref="CanHit"/>. Returns true if a shot was fired.</summary>
        bool TryFire(Transform target);
    }
}

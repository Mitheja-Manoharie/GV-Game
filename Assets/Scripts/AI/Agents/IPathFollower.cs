using System;
using System.Collections.Generic;
using UnityEngine;

namespace LabLockdown.AI.Agents
{
    /// <summary>
    /// How fast a robot walks a path, as a share of <see cref="MoveSpeeds.BaseSpeed"/>.
    /// The values match the animation blend: Patrol walks, Chase runs.
    /// </summary>
    public enum MoveSpeed
    {
        Patrol,
        Investigate,
        Chase,
    }

    public enum PathEndReason
    {
        /// <summary>Reached the last waypoint.</summary>
        Arrived,

        /// <summary><see cref="IPathFollower.Stop"/> was called.</summary>
        Stopped,

        /// <summary><see cref="IPathFollower.SetPath"/> was called again before this path finished.</summary>
        Replaced,

        /// <summary>No progress for a while (blocked doorway, crowd, unreachable waypoint).</summary>
        Stuck,
    }

    public static class MoveSpeeds
    {
        /// <summary>
        /// Full running speed in m/s. 3 m/s is the robot speed the Facility AI's pacing simulator was tuned with;
        /// at 3.5 m/s the average player's lowest health drops from 58 % to 46 %.
        /// </summary>
        public const float BaseSpeed = 3f;

        public static float Multiplier(MoveSpeed speed)
        {
            switch (speed)
            {
                case MoveSpeed.Patrol: return 0.5f;
                case MoveSpeed.Investigate: return 0.75f;
                default: return 1f;
            }
        }

        public static float MetresPerSecond(MoveSpeed speed) => BaseSpeed * Multiplier(speed);
    }

    /// <summary>
    /// The contract between an Intelligent Systems "brain" (Security Hunter, Tactical Flanker) and the robot's body.
    /// The brain decides where to go as a list of waypoints; the follower turns that into smooth movement,
    /// turning and animation. The waypoints are authoritative: the body only uses the NavMesh for local avoidance.
    /// </summary>
    public interface IPathFollower
    {
        /// <summary>
        /// Walks the waypoints in order. The list is copied, so the caller may reuse it. Calling this while a path
        /// is being walked switches to the new path without snapping back (and raises <see cref="PathEnded"/> with
        /// <see cref="PathEndReason.Replaced"/> for the old one).
        /// </summary>
        void SetPath(IReadOnlyList<Vector3> waypoints, MoveSpeed speed);

        /// <summary>Stops walking (raises <see cref="PathEnded"/> with <see cref="PathEndReason.Stopped"/> if a path was active).</summary>
        void Stop();

        /// <summary>Keeps the body turned towards <paramref name="target"/>, walking or not. Null clears it.</summary>
        void FaceTarget(Transform target);

        /// <summary>True once the last waypoint of the current path has been reached.</summary>
        bool HasArrived { get; }

        event Action<PathEndReason> PathEnded;
    }
}

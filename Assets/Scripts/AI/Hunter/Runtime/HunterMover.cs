using System;
using System.Collections.Generic;
using UnityEngine;

namespace LabLockdown.AI.Hunter.Runtime
{
    /// <summary>
    /// Walks the body along a list of waypoints the Hunter's A* produced. It never chooses a route: it only
    /// follows the one it is given (the search stays ours).
    ///
    /// Same shape as the shared follower the group agreed: SetPath, Stop, FaceTarget, HasArrived, PathEnded.
    /// When the Flanker owner's shared follower lands, HunterAgent can drive that instead.
    ///
    /// The body has no CharacterController on purpose: DoorController treats one as a solid and would stop
    /// doors dead against the robot.
    /// </summary>
    public class HunterMover : MonoBehaviour
    {
        [Tooltip("Degrees per second the body turns.")]
        public float turnSpeed = 360f;
        [Tooltip("A waypoint counts as reached inside this distance (metres).")]
        public float arriveDistance = 0.3f;
        [Tooltip("Stand this far from a door waypoint until the gate says the door is open (metres).")]
        public float holdDistance = 1.0f;

        private readonly List<Vector3> waypoints = new List<Vector3>();
        private int index;
        private float speed;
        private Transform facing;

        /// <summary>Asked before each approach to a waypoint: may the body go through it yet?</summary>
        public Func<int, bool> Gate { get; set; }

        public bool HasArrived { get; private set; } = true;
        public bool IsMoving { get; private set; }

        /// <summary>True while waiting at a closed door.</summary>
        public bool IsHolding { get; private set; }

        public int WaypointIndex => index;

        public Vector3 CurrentWaypoint => index < waypoints.Count ? waypoints[index] : transform.position;

        public event Action PathEnded;

        public void SetPath(IReadOnlyList<Vector3> path, float metresPerSecond)
        {
            waypoints.Clear();
            for (int i = 0; i < path.Count; i++) waypoints.Add(path[i]);   // copy: the caller reuses its list
            index = 0;
            speed = metresPerSecond;
            IsHolding = false;
            IsMoving = waypoints.Count > 0;
            HasArrived = waypoints.Count == 0;
        }

        public void SetSpeed(float metresPerSecond)
        {
            speed = metresPerSecond;
        }

        public void Stop()
        {
            waypoints.Clear();
            index = 0;
            IsMoving = false;
            IsHolding = false;
            HasArrived = true;
        }

        public void FaceTarget(Transform target)
        {
            facing = target;
        }

        public void ClearFacing()
        {
            facing = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 position = transform.position;
            Vector3 look = Vector3.zero;

            if (IsMoving && index < waypoints.Count)
            {
                Vector3 toWaypoint = waypoints[index] - position;
                toWaypoint.y = 0f;
                float distance = toWaypoint.magnitude;

                if (Gate != null && distance <= holdDistance && !Gate(index))
                {
                    IsHolding = true;
                    look = toWaypoint;
                }
                else
                {
                    IsHolding = false;
                    if (distance <= arriveDistance)
                    {
                        index++;
                        if (index >= waypoints.Count) Finish();
                    }
                    else
                    {
                        transform.position = position + toWaypoint / distance * Mathf.Min(speed * dt, distance);
                        look = toWaypoint;
                    }
                }
            }

            if (facing != null)
            {
                look = facing.position - position;
                look.y = 0f;
            }

            if (look.sqrMagnitude > 0.0001f)
            {
                Quaternion wanted = Quaternion.LookRotation(look.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * dt);
            }
        }

        private void Finish()
        {
            waypoints.Clear();
            index = 0;
            IsMoving = false;
            IsHolding = false;
            HasArrived = true;
            PathEnded?.Invoke();
        }
    }
}

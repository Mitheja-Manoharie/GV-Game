using System;
using System.Collections.Generic;
using GVGame.Interaction;
using LabLockdown.AI.Agents;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Agents
{
    /// <summary>
    /// The robots' legs: turns the waypoint lists from the Intelligent Systems brains (Security Hunter, Tactical
    /// Flanker) into smooth movement and turning.
    ///
    /// How it moves:
    ///   - The waypoints are authoritative. The NavMeshAgent only steers to the current waypoint, which gives local
    ///     avoidance (other robots, props, carved locked doors) without letting it choose its own route.
    ///   - Corners are cut: the next waypoint is taken once the robot is within <see cref="cornerRadius"/>, so it
    ///     curves round tile centres instead of stopping at each one. It only brakes for the last waypoint.
    ///   - The body is turned by this script, at most <see cref="turnSpeed"/> degrees per second, towards where it is
    ///     going or towards <see cref="IPathFollower.FaceTarget"/>, so it never snaps round.
    ///   - A new path mid-walk starts from the closest point ahead on that path, so the robot never walks back to a
    ///     waypoint it has already passed.
    /// No CharacterController: DoorController treats one as solid and would jam doors on robots.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotPathFollower : MonoBehaviour, IPathFollower
    {
        [Header("Speed")]
        [Tooltip("Acceleration of the NavMeshAgent (m/s²). Higher feels snappier, lower smoother.")]
        public float acceleration = 12f;
        [Tooltip("How fast the body turns, in degrees per second.")]
        public float turnSpeed = 360f;

        [Header("Waypoints")]
        [Tooltip("Move on to the next waypoint once this close (m). Cuts corners smoothly.")]
        public float cornerRadius = 1.2f;
        [Tooltip("Distance (m) at which the last waypoint counts as reached.")]
        public float arriveRadius = 0.4f;
        [Tooltip("A new path starts from the next waypoint ahead when the robot is within this distance (m) of the path.")]
        public float joinDistance = 2f;

        [Header("Stuck detection")]
        [Tooltip("Seconds without getting closer to the current waypoint before trying to open a door, then giving up.")]
        public float stuckSeconds = 2.5f;
        [Tooltip("Metres the robot must gain on its waypoint to count as progress.")]
        public float minProgress = 0.3f;
        [Tooltip("How far ahead (m) to look for a closed door when stuck.")]
        public float doorSearchRadius = 2.5f;

        readonly List<Vector3> _path = new List<Vector3>();
        readonly Collider[] _doorBuffer = new Collider[8];
        NavMeshAgent _agent;
        Transform _faceTarget;
        int _index;
        bool _walking;
        float _bestDistance;
        float _stuckTimer;
        bool _triedDoor;

        public event Action<PathEndReason> PathEnded;

        public bool HasArrived { get; private set; }
        public bool IsWalking => _walking;
        public MoveSpeed CurrentSpeed { get; private set; }
        public IReadOnlyList<Vector3> CurrentPath => _path;
        public int CurrentWaypoint => _index;
        public Transform FacingTarget => _faceTarget;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.updateRotation = false; // turned here: smooth, and able to face a target while walking
            _agent.autoBraking = false;    // keep speed through intermediate waypoints
            _agent.acceleration = acceleration;
            _agent.speed = MoveSpeeds.MetresPerSecond(MoveSpeed.Chase);
        }

        void OnDisable()
        {
            // Disabled (e.g. the robot died): stop quietly, there is no walk left to report.
            _walking = false;
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.ResetPath();
        }

        public void SetPath(IReadOnlyList<Vector3> waypoints, MoveSpeed speed)
        {
            // Copy first: the caller may reuse its buffer, and a PathEnded handler below may call us again.
            var copy = new List<Vector3>();
            if (waypoints != null)
                for (int i = 0; i < waypoints.Count; i++) copy.Add(waypoints[i]);

            if (_walking)
            {
                _walking = false;
                PathEnded?.Invoke(PathEndReason.Replaced);
            }

            _path.Clear();
            _path.AddRange(copy);
            CurrentSpeed = speed;
            HasArrived = false;

            if (_path.Count == 0)
            {
                HaltAgent();
                HasArrived = true;
                PathEnded?.Invoke(PathEndReason.Arrived);
                return;
            }

            _index = FirstWaypointAhead();
            _walking = true;
            if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.speed = MoveSpeeds.MetresPerSecond(speed);
            }

            ResetProgress();
            SteerTo(_index);
        }

        public void Stop()
        {
            if (!_walking) return;
            _walking = false;
            HaltAgent();
            PathEnded?.Invoke(PathEndReason.Stopped);
        }

        public void FaceTarget(Transform target) => _faceTarget = target;

        void Update()
        {
            if (_walking) Walk();
            Turn();
        }

        void Walk()
        {
            if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;

            bool last = _index >= _path.Count - 1;
            float distance = FlatDistance(transform.position, _path[_index]);

            if (!last && distance <= cornerRadius)
            {
                _index++;
                ResetProgress();
                SteerTo(_index);
                return;
            }

            if (last && distance <= arriveRadius)
            {
                Finish(PathEndReason.Arrived);
                return;
            }

            _agent.autoBraking = last; // slow down only for the final waypoint

            if (!_agent.pathPending && _agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                Finish(PathEndReason.Stuck);
                return;
            }

            if (distance < _bestDistance - minProgress)
            {
                _bestDistance = distance;
                _stuckTimer = 0f;
                return;
            }

            _stuckTimer += Time.deltaTime;
            if (_stuckTimer < stuckSeconds) return;

            // No progress: a closed (unlocked) door is the usual cause. Open it once, then give up if still stuck.
            if (!_triedDoor)
            {
                _triedDoor = true;
                _stuckTimer = 0f;
                TryOpenDoorAhead();
                return;
            }

            Finish(PathEndReason.Stuck);
        }

        void Turn()
        {
            Vector3 direction;
            if (_faceTarget != null)
                direction = _faceTarget.position - transform.position;
            else if (_agent.isActiveAndEnabled && _agent.velocity.sqrMagnitude > 0.04f)
                direction = _agent.velocity;
            else
                return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return;

            var goal = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, turnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Where to join a (new) path: after the segment closest to the robot, so it carries on forwards. If the robot
        /// is far from the path, it heads for the nearer end of that segment instead of cutting across.
        /// </summary>
        int FirstWaypointAhead()
        {
            if (_path.Count == 1) return 0;

            Vector3 position = transform.position;
            int bestSegment = 0;
            float bestT = 0f, bestDistance = float.PositiveInfinity;
            for (int i = 0; i + 1 < _path.Count; i++)
            {
                Vector3 a = Flat(_path[i]), b = Flat(_path[i + 1]), p = Flat(position);
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector3.Distance(p, a + ab * t);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestSegment = i;
                    bestT = t;
                }
            }

            if (bestDistance <= joinDistance) return bestSegment + 1;
            return bestT >= 1f ? bestSegment + 1 : bestSegment;
        }

        void SteerTo(int index)
        {
            if (_agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.SetDestination(_path[index]);
        }

        void Finish(PathEndReason reason)
        {
            _walking = false;
            HasArrived = reason == PathEndReason.Arrived;
            HaltAgent();
            PathEnded?.Invoke(reason);
        }

        void HaltAgent()
        {
            if (_agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.ResetPath();
        }

        void ResetProgress()
        {
            _bestDistance = float.PositiveInfinity;
            _stuckTimer = 0f;
            _triedDoor = false;
        }

        void TryOpenDoorAhead()
        {
            Vector3 centre = transform.position + transform.forward * (doorSearchRadius * 0.5f) + Vector3.up;
            int count = Physics.OverlapSphereNonAlloc(centre, doorSearchRadius, _doorBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var door = _doorBuffer[i].GetComponentInParent<DoorController>();
                if (door == null || door.IsLocked) continue;
                if (door.Current == DoorController.State.Closed || door.Current == DoorController.State.Closing)
                {
                    door.Open(transform);
                    return;
                }
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float FlatDistance(Vector3 a, Vector3 b) => Vector3.Distance(Flat(a), Flat(b));

        void OnDrawGizmosSelected()
        {
            if (_path.Count == 0) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i + 1 < _path.Count; i++)
                Gizmos.DrawLine(_path[i] + Vector3.up * 0.2f, _path[i + 1] + Vector3.up * 0.2f);
            if (_walking && _index < _path.Count)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_path[_index] + Vector3.up * 0.2f, cornerRadius);
            }
        }
    }
}

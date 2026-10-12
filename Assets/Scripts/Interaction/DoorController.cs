using UnityEngine;

namespace GVGame.Interaction
{
    /// <summary>
    /// Hinged door driven by a kinematic Rigidbody so it correctly pushes / blocks the player
    /// and dynamic props. Swing angle is clamped between closed (0) and openAngle, and the door
    /// always swings AWAY from whoever interacts with it.
    ///
    /// Hierarchy expected:  DoorController (this, sits on the hinge pivot)
    ///                        └─ Leaf (mesh + collider, offset from the pivot)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DoorController : MonoBehaviour, IInteractable
    {
        public enum State { Closed, Opening, Open, Closing }

        [Header("Hinge")]
        [Tooltip("Maximum swing angle in degrees (angle limit).")]
        [Range(10f, 140f)] public float openAngle = 100f;
        [Tooltip("Degrees per second the door swings at.")]
        public float swingSpeed = 140f;
        [Tooltip("Ease the motion in/out instead of a constant swing.")]
        public bool smooth = true;

        [Header("Behaviour")]
        public bool startLocked = false;
        [Tooltip("Close automatically after this many seconds. 0 = stay open.")]
        public float autoCloseDelay = 0f;

        [Header("Obstacles")]
        [Tooltip("Props up to this mass are pushed aside by the swinging door. Heavier props (and the player) stop it.")]
        public float maxPushableMass = 10f;
        [Tooltip("Swing speed multiplier while shoving a light prop.")]
        [Range(0.1f, 1f)] public float pushSpeedFactor = 0.5f;
        [Tooltip("Heavier props up to this mass block the door, but the player can FORCE it open by walking into it (slowly, limited angle). Above this nothing can move them.")]
        public float maxPryMass = 120f;
        [Tooltip("Swing speed multiplier while the player forces the door against a heavy prop.")]
        [Range(0.05f, 0.5f)] public float pryFactor = 0.12f;
        [Tooltip("Maximum angle the door can be forced open against a heavy prop: enough to squeeze through, not to clear the way.")]
        [Range(10f, 90f)] public float maxPryAngle = 60f;

        [Header("Feedback (optional)")]
        public AudioClip openClip;
        public AudioClip closeClip;
        public AudioClip lockedClip;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Loudness emitted to the AI noise bus when the door moves.")]
        public float noiseLoudness = 6f;

        public State Current { get; private set; } = State.Closed;
        public bool IsLocked { get; private set; }

        Rigidbody _rb;
        Quaternion _closedRot;
        float _angle;          // signed current angle from closed
        float _targetAngle;
        float _openSign = 1f;  // +1 / -1 swing direction
        float _closeTimer;
        AudioSource _audio;
        Collider[] _cols;
        float _blockedUntil;
        bool _pryable;          // blocked by a heavy-but-movable prop: walking into the door can force it
        bool _forcingActive;    // the player is currently shouldering the door open
        float _forceUntil;

        enum Obstacle { None, Light, Pry, Heavy }   // order matters: later = stronger

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _closedRot = transform.localRotation;
            _cols = GetComponentsInChildren<Collider>();
            IsLocked = startLocked;

            _audio = GetComponent<AudioSource>();
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.maxDistance = 20f;
            }
        }

        // ---------- IInteractable ----------
        public string GetPrompt(Transform interactor)
        {
            if (IsLocked) return "Locked";
            if (_pryable) return "Door blocked - walk into it to force it";
            if (Time.time < _blockedUntil) return "Door blocked";
            return Current == State.Closed || Current == State.Closing ? "Open door" : "Close door";
        }

        public void Interact(Transform interactor) => Toggle(interactor);

        // ---------- Public API (also usable by AI / triggers / other scripts) ----------
        public void Toggle(Transform from)
        {
            if (IsLocked) { PlayClip(lockedClip); return; }
            if (Current == State.Closed || Current == State.Closing) Open(from);
            else Close();
        }

        public void Open(Transform from = null)
        {
            if (IsLocked) { PlayClip(lockedClip); return; }
            ChooseSwingDirection(from);
            _targetAngle = _openSign * openAngle;
            if (Current != State.Opening && Current != State.Open)
            {
                PlayClip(openClip);
                InteractionNoise.Emit(transform.position, noiseLoudness, gameObject);
            }
            Current = State.Opening;
            _closeTimer = autoCloseDelay;
        }

        // Called by PlayerRigidbodyPusher while the player walks into the door. Only works when the door
        // is blocked by a heavy-but-movable prop: the player shoulders it open, slowly, up to maxPryAngle.
        public void ForceOpen(Transform pusher)
        {
            if (IsLocked || !_pryable) return;
            ChooseSwingDirection(pusher);
            _forcingActive = true;
            _forceUntil = Time.time + 0.2f;      // renewed every frame the player keeps pushing
            _targetAngle = _openSign * maxPryAngle;
            Current = State.Opening;
        }

        // Choose the swing direction only while the door is (nearly) closed.
        void ChooseSwingDirection(Transform from)
        {
            if (from == null || Mathf.Abs(_angle) >= 1f) return;
            Vector3 r = LeafRadial(out _);
            // Direction the free edge of the leaf moves for a POSITIVE angle (rotation about +Y).
            Vector3 swingDir = Vector3.Cross(Vector3.up, r).normalized;
            Vector3 toOpener = from.position - transform.position;
            toOpener.y = 0f;
            // If a positive swing would move the leaf towards the opener, swing the other way.
            _openSign = Vector3.Dot(swingDir, toOpener) < 0f ? 1f : -1f;
        }

        public void Close()
        {
            _pryable = false;
            _forcingActive = false;
            _targetAngle = 0f;
            if (Current != State.Closing && Current != State.Closed)
                PlayClip(closeClip);
            Current = State.Closing;
        }

        public void SetLocked(bool locked) => IsLocked = locked;

        // ---------- Motion ----------
        void FixedUpdate()
        {
            // The player stopped pushing: hold the door where it is.
            if (_forcingActive && Time.time >= _forceUntil)
            {
                _forcingActive = false;
                _targetAngle = _angle;
                Current = Mathf.Abs(_angle) < 0.5f ? State.Closed : State.Open;
            }

            if (Mathf.Approximately(_angle, _targetAngle))
            {
                if (Current == State.Opening) Current = State.Open;
                else if (Current == State.Closing) Current = State.Closed;

                if (Current == State.Open && autoCloseDelay > 0f)
                {
                    _closeTimer -= Time.fixedDeltaTime;
                    if (_closeTimer <= 0f) Close();
                }
                return;
            }

            float remaining = _targetAngle - _angle;
            float speed = swingSpeed;
            if (smooth)
            {
                // Slow down near the end (ease-out) but never stall completely.
                float t = Mathf.Clamp01(Mathf.Abs(remaining) / Mathf.Max(1f, openAngle * 0.35f));
                speed *= Mathf.Lerp(0.25f, 1f, t);
            }

            float step = speed * Time.fixedDeltaTime;
            float next = Mathf.MoveTowards(_angle, _targetAngle, step);

            // Look ahead along the swing (opening AND closing):
            //   light prop (<= maxPushableMass) -> shoved aside at reduced speed (small hindrance)
            //   heavy movable prop              -> the door STOPS against it ("Door blocked").
            //                                      Only if the player walks into the door does it open,
            //                                      slowly, up to maxPryAngle (squeeze-through gap)
            //   the player / immovable          -> the door stops (never crushes, never pushes the player)
            Obstacle obstacle = SweepObstacle(next - _angle);
            bool opening = Mathf.Abs(_targetAngle) > Mathf.Abs(_angle);

            if (obstacle == Obstacle.Pry)
            {
                bool allowed = _forcingActive && opening && Mathf.Abs(_angle) < maxPryAngle;
                if (!allowed)
                {
                    StopAgainstObstacle(opening && Mathf.Abs(_angle) < maxPryAngle);
                    return;
                }
                next = Mathf.MoveTowards(_angle, _targetAngle, step * pryFactor);
                if (Mathf.Abs(next) > maxPryAngle) next = Mathf.Sign(_targetAngle) * maxPryAngle;
            }
            else if (obstacle == Obstacle.Heavy)
            {
                StopAgainstObstacle(false);
                return;
            }
            else if (obstacle == Obstacle.Light)
            {
                next = Mathf.MoveTowards(_angle, _targetAngle, step * pushSpeedFactor);
            }

            _angle = next;
            // Hard angle limits.
            float lo = Mathf.Min(0f, _openSign * openAngle), hi = Mathf.Max(0f, _openSign * openAngle);
            _angle = Mathf.Clamp(_angle, lo, hi);

            Quaternion local = _closedRot * Quaternion.Euler(0f, _angle, 0f);
            Quaternion world = transform.parent != null ? transform.parent.rotation * local : local;
            _rb.MoveRotation(world);
        }

        // Horizontal vector from the hinge to the centre of the leaf's colliders, plus a safe radius.
        Vector3 LeafRadial(out float radius)
        {
            Bounds b = default;
            bool has = false;
            foreach (var c in _cols)
            {
                if (c == null || c.isTrigger) continue;
                if (!has) { b = c.bounds; has = true; }
                else b.Encapsulate(c.bounds);
            }
            Vector3 r = has ? b.center - transform.position : transform.right;
            r.y = 0f;
            if (r.sqrMagnitude < 1e-4f) r = transform.right;
            radius = has ? r.magnitude + new Vector2(b.extents.x, b.extents.z).magnitude : 1f;
            return r;
        }

        // Strongest obstacle found in the path of the next small swing step.
        Obstacle SweepObstacle(float deltaAngle)
        {
            if (Mathf.Approximately(deltaAngle, 0f)) return Obstacle.None;
            Vector3 r = LeafRadial(out float radius);
            Vector3 dir = Vector3.Cross(Vector3.up, r).normalized * Mathf.Sign(deltaAngle);
            float dist = radius * Mathf.Abs(deltaAngle) * Mathf.Deg2Rad + 0.05f;

            Obstacle worst = Obstacle.None;
            foreach (RaycastHit hit in _rb.SweepTestAll(dir, dist, QueryTriggerInteraction.Ignore))
            {
                Obstacle o = Classify(hit.collider, hit.rigidbody);
                if (o > worst) worst = o;
                if (worst == Obstacle.Heavy) break;
            }
            return worst;
        }

        Obstacle Classify(Collider col, Rigidbody body)
        {
            if (col.GetComponent<CharacterController>() != null) return Obstacle.Heavy;   // never push the player
            if (body == null || body.isKinematic) return Obstacle.None;                   // walls, frame, other doors
            if (body.mass <= maxPushableMass) return Obstacle.Light;
            return body.mass <= maxPryMass ? Obstacle.Pry : Obstacle.Heavy;
        }

        void StopAgainstObstacle(bool canBeForced)
        {
            _pryable = canBeForced;
            _forcingActive = false;
            _targetAngle = _angle;
            Current = Mathf.Abs(_angle) < 0.5f ? State.Closed : State.Open;   // stays where it is
            _blockedUntil = Time.time + 1.5f;
        }

        void PlayClip(AudioClip clip)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, volume);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 p = transform.position + Vector3.up * 0.1f;
            Quaternion baseRot = transform.parent != null ? transform.parent.rotation * transform.localRotation : transform.rotation;
            Gizmos.DrawRay(p, baseRot * Vector3.right * 1.2f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(p, baseRot * Quaternion.Euler(0, openAngle, 0) * Vector3.right * 1.2f);
            Gizmos.DrawRay(p, baseRot * Quaternion.Euler(0, -openAngle, 0) * Vector3.right * 1.2f);
        }
#endif
    }
}
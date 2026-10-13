using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Interaction
{
    /// <summary>
    /// One controller per door. Drives one or more hinged LEAVES (the Door_* markers use two panels).
    /// Each leaf is a child object with a kinematic Rigidbody + collider whose pivot is the hinge.
    /// Expected hierarchy:
    ///   Door_X (this component, on the marker; marker local X = along the opening, local Z = door normal)
    ///     |- Hinge_L  (Rigidbody, collider, panel model)
    ///     |- Hinge_R  (Rigidbody, collider, panel model, rotated 180 degrees)
    ///     |- AutoOpenTrigger (optional, for AI)
    /// A single-leaf door also works: put this component on the hinge object itself.
    ///
    /// Behaviour (per leaf, so a blocked leaf never stops the other one):
    ///   light prop (<= maxPushableMass)  -> shoved aside at reduced speed
    ///   heavy movable prop               -> door STOPS; walking into it forces it open slowly up to maxPryAngle
    ///   the player / immovable           -> door stops (never crushes, never pushes the player)
    /// The door always swings AWAY from whoever opens it.
    /// Public API used by the Facility AI adapter: Current, IsLocked, SetLocked, Open, Close, Toggle.
    /// </summary>
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
        [Tooltip("Props up to this mass are pushed aside by a swinging leaf.")]
        public float maxPushableMass = 10f;
        [Range(0.1f, 1f)] public float pushSpeedFactor = 0.5f;
        [Tooltip("Heavier props up to this mass block the leaf, but the player can FORCE it by walking into it.")]
        public float maxPryMass = 120f;
        [Range(0.05f, 0.5f)] public float pryFactor = 0.12f;
        [Tooltip("Maximum angle a leaf can be forced open against a heavy prop (enough to squeeze through).")]
        [Range(10f, 90f)] public float maxPryAngle = 60f;

        [Header("NavMesh")]
        [Tooltip("If true, every closed leaf carves the NavMesh (issue #11 option). Default off: the Facility AI " +
                 "adds its own barrier on LOCKED doors, and robots open unlocked doors.")]
        public bool blockNavMeshWhenClosed = false;

        [Header("Feedback (optional)")]
        public AudioClip openClip;
        public AudioClip closeClip;
        public AudioClip lockedClip;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Loudness emitted to the AI noise bus when the door starts opening.")]
        public float noiseLoudness = 6f;

        public State Current { get; private set; } = State.Closed;
        public bool IsLocked { get; private set; }

        class Leaf
        {
            public Rigidbody rb;
            public Quaternion closedLocal;
            public Vector3 radial0;      // horizontal hinge -> leaf centre when closed
            public float radius;         // safe swing radius
            public float sign = 1f;      // maps "swing magnitude" to a signed rotation about +Y
            public float mag;            // current opening magnitude (0 = closed)
            public float target;         // target magnitude
            public bool pryable;         // blocked by a heavy-but-movable prop
            public bool forcing;         // player is shouldering this leaf open
            public float forceUntil;
            public NavMeshObstacle obstacle;
            public bool carving;
        }

        enum Obstacle { None, Light, Pry, Heavy }   // order matters: later = stronger

        Leaf[] _leaves;
        Vector3 _closedForward;
        Vector3 _swingDir;
        bool _wantOpen;
        float _closeTimer;
        float _blockedUntil;
        AudioSource _audio;

        void Awake()
        {
            IsLocked = startLocked;

            _closedForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            _closedForward = _closedForward.sqrMagnitude > 1e-4f ? _closedForward.normalized : Vector3.forward;
            _swingDir = -_closedForward;

            var list = new List<Leaf>();
            foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                Bounds b = default;
                bool has = false;
                foreach (Collider c in rb.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
                }

                Vector3 r = has ? b.center - rb.transform.position : rb.transform.right;
                r.y = 0f;
                if (r.sqrMagnitude < 1e-4f) r = rb.transform.right;

                var leaf = new Leaf
                {
                    rb = rb,
                    closedLocal = rb.transform.localRotation,
                    radial0 = r,
                    radius = has ? r.magnitude + new Vector2(b.extents.x, b.extents.z).magnitude : 1f
                };
                if (blockNavMeshWhenClosed) AddObstacle(leaf);
                list.Add(leaf);
            }
            _leaves = list.ToArray();
            ApplySwingSigns();

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
            if (Current == State.Closed && AnyPryable()) return "Door blocked - walk into it to force it";
            if (Time.time < _blockedUntil) return "Door blocked";
            return Current == State.Closed || Current == State.Closing ? "Open door" : "Close door";
        }

        public void Interact(Transform interactor) => Toggle(interactor);

        // ---------- Public API ----------
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
            bool wasShut = Current == State.Closed || Current == State.Closing;
            _wantOpen = true;
            foreach (Leaf l in _leaves) { l.target = openAngle; l.pryable = false; l.forcing = false; }
            if (wasShut)
            {
                PlayClip(openClip);
                InteractionNoise.Emit(transform.position, noiseLoudness, gameObject);
            }
            Current = State.Opening;
            _closeTimer = autoCloseDelay;
        }

        public void Close()
        {
            _wantOpen = false;
            foreach (Leaf l in _leaves) { l.target = 0f; l.pryable = false; l.forcing = false; }
            if (Current != State.Closing && Current != State.Closed) PlayClip(closeClip);
            Current = State.Closing;
        }

        public void SetLocked(bool locked) => IsLocked = locked;

        /// <summary>
        /// Called by PlayerRigidbodyPusher while the player walks into the door. Works only on leaves that
        /// are blocked by a heavy-but-movable prop: the player shoulders that leaf open, slowly.
        /// </summary>
        public void ForceOpen(Transform pusher, Collider hit = null)
        {
            if (IsLocked) return;
            Rigidbody hitBody = hit != null ? hit.attachedRigidbody : null;
            bool any = false;
            foreach (Leaf l in _leaves)
            {
                if (!l.pryable) continue;
                if (hitBody != null && l.rb != hitBody) continue;
                l.forcing = true;
                l.forceUntil = Time.time + 0.2f;     // renewed every frame the player keeps pushing
                l.target = Mathf.Min(maxPryAngle, openAngle);
                any = true;
            }
            if (!any) return;
            ChooseSwingDirection(pusher);            // only takes effect while fully closed
            _wantOpen = true;
            Current = State.Opening;
        }

        // ---------- Direction ----------
        // The door swings away from the opener; chosen only while every leaf is (nearly) closed.
        void ChooseSwingDirection(Transform from)
        {
            if (from == null) return;
            foreach (Leaf l in _leaves) if (l.mag > 1f) return;
            Vector3 toOpener = from.position - transform.position;
            toOpener.y = 0f;
            _swingDir = Vector3.Dot(toOpener, _closedForward) >= 0f ? -_closedForward : _closedForward;
            ApplySwingSigns();
        }

        // Rotating +angle about +Y moves a leaf's free edge along Cross(up, radial).
        // Pick each leaf's sign so that movement points along the chosen swing direction.
        void ApplySwingSigns()
        {
            foreach (Leaf l in _leaves)
                l.sign = Vector3.Dot(Vector3.Cross(Vector3.up, l.radial0), _swingDir) >= 0f ? 1f : -1f;
        }

        // ---------- Motion ----------
        void FixedUpdate()
        {
            bool anyMoving = false, anyOpen = false;
            foreach (Leaf l in _leaves)
            {
                StepLeaf(l);
                if (!Mathf.Approximately(l.mag, l.target)) anyMoving = true;
                if (l.mag > 0.5f) anyOpen = true;
            }

            if (anyMoving) Current = _wantOpen ? State.Opening : State.Closing;
            else Current = anyOpen ? State.Open : State.Closed;

            if (Current == State.Open && autoCloseDelay > 0f)
            {
                _closeTimer -= Time.fixedDeltaTime;
                if (_closeTimer <= 0f) Close();
            }

            UpdateObstacles();
        }

        void StepLeaf(Leaf l)
        {
            // The player stopped pushing: hold the leaf where it is.
            if (l.forcing && Time.time >= l.forceUntil) { l.forcing = false; l.target = l.mag; }
            if (Mathf.Approximately(l.mag, l.target)) return;

            float speed = swingSpeed;
            if (smooth)
            {
                float t = Mathf.Clamp01(Mathf.Abs(l.target - l.mag) / Mathf.Max(1f, openAngle * 0.35f));
                speed *= Mathf.Lerp(0.25f, 1f, t);
            }
            float step = speed * Time.fixedDeltaTime;
            float next = Mathf.MoveTowards(l.mag, l.target, step);
            bool opening = l.target > l.mag;

            Obstacle ob = SweepObstacle(l, next - l.mag);
            if (ob == Obstacle.Pry)
            {
                bool allowed = l.forcing && opening && l.mag < maxPryAngle;
                if (!allowed) { StopLeaf(l, opening && l.mag < maxPryAngle); return; }
                next = Mathf.MoveTowards(l.mag, l.target, step * pryFactor);
                if (next > maxPryAngle) next = maxPryAngle;
            }
            else if (ob == Obstacle.Heavy)
            {
                StopLeaf(l, false);
                return;
            }
            else if (ob == Obstacle.Light)
            {
                next = Mathf.MoveTowards(l.mag, l.target, step * pushSpeedFactor);
            }

            l.mag = Mathf.Clamp(next, 0f, openAngle);

            Quaternion local = l.closedLocal * Quaternion.Euler(0f, l.sign * l.mag, 0f);
            Transform t2 = l.rb.transform;
            l.rb.MoveRotation(t2.parent != null ? t2.parent.rotation * local : local);
        }

        void StopLeaf(Leaf l, bool canBeForced)
        {
            l.pryable = canBeForced;
            l.forcing = false;
            l.target = l.mag;
            _blockedUntil = Time.time + 1.5f;
        }

        // Strongest obstacle in the path of this leaf's next small swing step.
        Obstacle SweepObstacle(Leaf l, float deltaMag)
        {
            if (Mathf.Approximately(deltaMag, 0f)) return Obstacle.None;
            Vector3 r = Quaternion.AngleAxis(l.sign * l.mag, Vector3.up) * l.radial0;
            Vector3 dir = Vector3.Cross(Vector3.up, r).normalized * Mathf.Sign(deltaMag * l.sign);
            float dist = l.radius * Mathf.Abs(deltaMag) * Mathf.Deg2Rad + 0.05f;

            Obstacle worst = Obstacle.None;
            foreach (RaycastHit hit in l.rb.SweepTestAll(dir, dist, QueryTriggerInteraction.Ignore))
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
            if (body == null || body.isKinematic) return Obstacle.None;                   // walls, frame, other leaves
            if (body.mass <= maxPushableMass) return Obstacle.Light;
            return body.mass <= maxPryMass ? Obstacle.Pry : Obstacle.Heavy;
        }

        bool AnyPryable()
        {
            foreach (Leaf l in _leaves) if (l.pryable) return true;
            return false;
        }

        // ---------- NavMesh (optional) ----------
        void AddObstacle(Leaf l)
        {
            var bc = l.rb.GetComponent<BoxCollider>();
            if (bc == null) { Debug.LogWarning("[Door] " + name + ": leaf has no BoxCollider for a NavMeshObstacle.", this); return; }
            var ob = l.rb.GetComponent<NavMeshObstacle>();
            if (ob == null) ob = l.rb.gameObject.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.center = bc.center;
            ob.size = bc.size;
            ob.carveOnlyStationary = true;
            ob.carving = false;
            l.obstacle = ob;
        }

        void UpdateObstacles()
        {
            if (!blockNavMeshWhenClosed) return;
            bool carve = Current == State.Closed;
            foreach (Leaf l in _leaves)
            {
                if (l.obstacle == null || l.carving == carve) continue;
                l.obstacle.carving = carve;
                l.carving = carve;
            }
        }

        void PlayClip(AudioClip clip)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, volume);
        }
    }
}

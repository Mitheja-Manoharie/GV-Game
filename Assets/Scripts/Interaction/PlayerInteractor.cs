using UnityEngine;
using UnityEngine.InputSystem;

namespace GVGame.Interaction
{
    /// <summary>
    /// Drop-in component for the player camera. Provides:
    ///   E           - interact (doors)
    ///   F           - pick up / drag / drop a physics prop
    ///   Q (hold)    - charge a throw, release to throw (light props only)
    ///   Mouse wheel - move the held prop nearer / further
    ///
    /// Light props (mass <= maxCarryMass) are CARRIED in front of the camera and can be thrown.
    /// Heavy props (barricades) are DRAGGED along the floor: they stay on the ground, cannot tilt,
    /// stay solid for the player, move slowly, and the player must slow down while dragging
    /// (see MaxCarrySpeed).
    /// Input actions are created in code (no edits to the shared InputSystem_Actions asset).
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("References")]
        public Camera viewCamera;
        [Tooltip("Colliders belonging to the player (ignored by carried light props).")]
        public Collider[] playerColliders;

        [Header("Reach")]
        public float interactDistance = 3f;
        public float grabDistance = 3.5f;
        public LayerMask interactMask = ~0;

        [Header("Holding")]
        public float holdDistance = 2f;
        public float minHoldDistance = 1.2f;
        public float maxHoldDistance = 3f;
        [Tooltip("Props up to this mass are carried; heavier props are dragged along the floor.")]
        public float maxCarryMass = 8f;
        [Tooltip("Heaviest prop that can be picked up / dragged at all.")]
        public float maxGrabMass = 60f;
        public float followGain = 14f;
        public float maxFollowSpeed = 14f;
        [Tooltip("Drop the prop if it gets this far from the hold point (stuck behind something).")]
        public float breakDistance = 3.5f;

        [Header("Dragging heavy props")]
        [Tooltip("Drag speed (m/s) = dragSpeedScale / mass, clamped between min and max below.")]
        public float dragSpeedScale = 30f;
        public float minDragSpeed = 0.9f;
        public float maxDragSpeed = 2.5f;

        [Header("Throwing (hold Q to charge)")]
        public float minThrowSpeed = 5f;
        public float maxThrowSpeed = 22f;
        [Tooltip("Seconds of holding Q to reach full power.")]
        public float chargeTime = 1.0f;
        [Tooltip("Heaviest prop that can still be thrown.")]
        public float maxThrowMass = 6f;

        [Header("HUD (optional)")]
        public bool showDebugPrompt = true;

        /// <summary>
        /// Maximum walking speed the player controller should allow right now.
        /// Infinity when not dragging. Player movement scripts should clamp their speed to this.
        /// </summary>
        public float MaxCarrySpeed =>
            (_held != null && IsHeavy(_held)) ? DragSpeed(_held.Body.mass) * 0.95f : float.PositiveInfinity;

        InputAction _interact, _grab, _throw, _scroll;
        PhysicsProp _held;
        IInteractable _lookTarget;
        string _prompt = "";        // line 1: what the held prop / grab target allows
        string _prompt2 = "";       // line 2: what the other hand can do (doors)
        Transform _cam;
        bool _charging;
        float _chargeStart;
        float _charge;
        float _minHold, _maxHold;   // hold-distance limits for the prop currently held

        void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
            if (viewCamera == null) viewCamera = Camera.main;
            _cam = viewCamera != null ? viewCamera.transform : transform;

            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _grab     = new InputAction("Grab",     InputActionType.Button, "<Keyboard>/f");
            _throw    = new InputAction("Throw",    InputActionType.Button, "<Keyboard>/q");
            _scroll   = new InputAction("HoldDistance", InputActionType.Value, "<Mouse>/scroll/y");
        }

        void OnEnable()  { _interact.Enable(); _grab.Enable(); _throw.Enable(); _scroll.Enable(); }
        void OnDisable() { _interact.Disable(); _grab.Disable(); _throw.Disable(); _scroll.Disable(); Drop(); }

        bool IsHeavy(PhysicsProp p) => p.Body.mass > maxCarryMass;
        float DragSpeed(float mass) => Mathf.Clamp(dragSpeedScale / Mathf.Max(1f, mass), minDragSpeed, maxDragSpeed);

        void Update()
        {
            if (_held == null) _charging = false;

            // One hand holds the prop, the other hand can still use doors.
            UpdateLookTarget();

            if (_interact.WasPressedThisFrame() && _lookTarget != null)
                _lookTarget.Interact(transform);

            if (_grab.WasPressedThisFrame())
            {
                if (_held != null) Drop();
                else TryGrab();
            }

            if (_held == null) return;

            float s = _scroll.ReadValue<float>();
            if (Mathf.Abs(s) > 0.01f)
                holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(s) * 0.2f, _minHold, _maxHold);

            bool canThrow = !IsHeavy(_held) && _held.Body.mass <= maxThrowMass;
            if (canThrow)
            {
                if (_throw.WasPressedThisFrame()) { _charging = true; _chargeStart = Time.time; }
                if (_charging)
                {
                    _charge = Mathf.Clamp01((Time.time - _chargeStart) / Mathf.Max(0.05f, chargeTime));
                    if (_throw.WasReleasedThisFrame())
                    {
                        Throw(Mathf.Lerp(minThrowSpeed, maxThrowSpeed, _charge));
                        _charging = false;
                    }
                }
                _prompt = _charging
                    ? $"Throw power: {Mathf.RoundToInt(_charge * 100f)}%  (release Q)"
                    : "[F] Drop   [Hold Q] Charge throw   [Wheel] Distance";
            }
            else
            {
                _prompt = "[F] Release   [Wheel] Distance   (heavy: dragging)";
            }
        }

        void FixedUpdate()
        {
            if (_held == null) return;
            Rigidbody rb = _held.Body;

            if (IsHeavy(_held)) DragHeavy(rb);
            else CarryLight(rb);
        }

        // Light props float in front of the camera and follow it.
        void CarryLight(Rigidbody rb)
        {
            Vector3 target = _cam.position + _cam.forward * holdDistance;
            Vector3 toTarget = target - rb.worldCenterOfMass;
            if (toTarget.magnitude > breakDistance) { Drop(); return; }

            float massFactor = Mathf.Clamp(8f / Mathf.Max(1f, rb.mass), 0.5f, 1f);
            Vector3 vel = Vector3.ClampMagnitude(toTarget * followGain * massFactor, maxFollowSpeed * massFactor);
            rb.linearVelocity = vel;
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, 10f * Time.fixedDeltaTime);
        }

        // Heavy props stay on the floor (gravity on, rotation frozen) and slide towards the hold point.
        void DragHeavy(Rigidbody rb)
        {
            Vector3 flatFwd = Vector3.ProjectOnPlane(_cam.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = transform.forward;
            flatFwd.Normalize();

            Vector3 target = _cam.position + flatFwd * holdDistance;
            Vector3 toTarget = target - rb.worldCenterOfMass;
            toTarget.y = 0f;
            if (toTarget.magnitude > breakDistance) { Drop(); return; }

            Vector3 horiz = Vector3.ClampMagnitude(toTarget * followGain, DragSpeed(rb.mass));
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);   // keep falling / resting vertically
        }

        // ---------------------------------------------------------------
        // Nearest thing in front of the camera, ignoring the prop we are holding.
        bool CastLook(float distance, out RaycastHit best)
        {
            best = default;
            bool found = false;
            float bestDist = float.MaxValue;
            RaycastHit[] hits = Physics.RaycastAll(_cam.position, _cam.forward, distance,
                                                   interactMask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (_held != null && h.collider.GetComponentInParent<PhysicsProp>() == _held) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        void UpdateLookTarget()
        {
            _lookTarget = null;
            _prompt2 = "";
            if (_held == null) _prompt = "";

            // Free hand: doors and other interactables.
            if (CastLook(interactDistance, out RaycastHit hit))
            {
                _lookTarget = hit.collider.GetComponentInParent<IInteractable>();
                if (_lookTarget != null)
                {
                    _prompt2 = "[E] " + _lookTarget.GetPrompt(transform);
                    return;
                }
            }

            // Empty hands: show the grab prompt.
            if (_held == null && CastLook(grabDistance, out RaycastHit grabHit))
            {
                var prop = grabHit.collider.GetComponentInParent<PhysicsProp>();
                if (prop != null && prop.Body.mass <= maxGrabMass)
                    _prompt = IsHeavy(prop) ? "[F] Drag barricade" : "[F] Pick up";
            }
        }

        void TryGrab()
        {
            if (!CastLook(grabDistance, out RaycastHit hit)) return;
            var prop = hit.collider.GetComponentInParent<PhysicsProp>();
            if (prop == null || prop.IsHeld || prop.Body.mass > maxGrabMass) return;

            _held = prop;
            _held.IsHeld = true;

            _minHold = minHoldDistance;
            _maxHold = maxHoldDistance;

            if (IsHeavy(_held))
            {
                // Keep a heavy prop far enough ahead that it never overlaps the player's body.
                _minHold = Mathf.Max(minHoldDistance, HorizontalRadius(_held) + 0.6f);
                _maxHold = Mathf.Max(maxHoldDistance, _minHold + 0.5f);

                _held.Body.useGravity = true;                                  // stays on the floor
                _held.Body.constraints = RigidbodyConstraints.FreezeRotation;  // cannot tip over while dragged
                // Heavy props stay SOLID for the player: you can never walk through a barricade.
            }
            else
            {
                _held.Body.useGravity = false;
                _held.Body.constraints = RigidbodyConstraints.None;
                SetIgnorePlayer(_held, true);   // a carried mug must not shove the player around
            }
            holdDistance = Mathf.Clamp(hit.distance, _minHold, _maxHold);
        }

        // Half the widest horizontal size of a prop (from its colliders).
        float HorizontalRadius(PhysicsProp p)
        {
            Bounds b = default;
            bool has = false;
            foreach (var c in p.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!has) { b = c.bounds; has = true; }
                else b.Encapsulate(c.bounds);
            }
            return has ? Mathf.Max(b.extents.x, b.extents.z) : 0.5f;
        }

        void Drop()
        {
            if (_held == null) return;
            Release();
        }

        void Throw(float speed)
        {
            if (_held == null) return;
            PhysicsProp p = _held;
            Release();
            // VelocityChange = mass-independent: the speed you see is the speed you get.
            p.Body.AddForce(_cam.forward * speed, ForceMode.VelocityChange);
            p.Body.AddTorque(Random.insideUnitSphere * 3f, ForceMode.VelocityChange);
        }

        void Release()
        {
            PhysicsProp p = _held;
            _held = null;
            _charging = false;
            p.IsHeld = false;
            p.Body.useGravity = true;
            p.Body.constraints = RigidbodyConstraints.None;
            SetIgnorePlayer(p, false);
        }

        void SetIgnorePlayer(PhysicsProp p, bool ignore)
        {
            if (playerColliders == null) return;
            var cols = p.GetComponentsInChildren<Collider>();
            foreach (var pc in playerColliders)
                foreach (var c in cols)
                    if (pc != null && c != null) Physics.IgnoreCollision(pc, c, ignore);
        }

        void OnGUI()
        {
            if (!showDebugPrompt) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            style.normal.textColor = Color.white;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 4f, cy - 12f, 20f, 24f), "+", style);
            if (!string.IsNullOrEmpty(_prompt))
                GUI.Label(new Rect(cx - 250f, cy + 40f, 500f, 30f), _prompt, style);
            if (!string.IsNullOrEmpty(_prompt2))
                GUI.Label(new Rect(cx - 250f, cy + 70f, 500f, 30f), _prompt2, style);
        }
    }
}
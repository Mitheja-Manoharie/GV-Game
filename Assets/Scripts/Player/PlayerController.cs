using UnityEngine;
using UnityEngine.InputSystem;
using GVGame.Interaction;

namespace GVGame.Player
{
    /// <summary>
    /// First-person controller on a CharacterController.
    ///   WASD move, mouse look, Shift sprint, Space jump, Ctrl (hold) crouch.
    /// Extras that tie into the other systems:
    ///   - speed is capped by PlayerInteractor.MaxCarrySpeed (dragging a barricade slows you down)
    ///   - footsteps raise noise on InteractionNoise (sprinting is loud, crouching is silent) so the
    ///     Security Hunter can hear the player
    ///   - AddRecoil() lets the weapon kick the camera
    /// Input actions are created in code, so the shared InputSystem_Actions asset is untouched.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        public Transform cameraRoot;

        [Header("Movement")]
        public float walkSpeed = 4.5f;
        public float sprintSpeed = 7f;
        public float crouchSpeed = 2.2f;
        public float acceleration = 14f;
        public float jumpHeight = 1.1f;
        public float gravity = -20f;

        [Header("Look")]
        public float lookSensitivity = 0.1f;
        public float maxPitch = 85f;
        public float recoilReturnSpeed = 9f;

        [Header("Crouch")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float cameraStandY = 1.6f;
        public float cameraCrouchY = 1.0f;

        [Header("Noise (for the AI)")]
        public float walkNoiseLoudness = 3f;
        public float sprintNoiseLoudness = 8f;
        public float noiseInterval = 0.45f;

        /// <summary>Set false when dead / in a menu: the player stops moving and looking.</summary>
        public bool InputEnabled { get; set; } = true;
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }

        CharacterController _cc;
        PlayerInteractor _interactor;
        InputAction _move, _look, _sprint, _jump, _crouch;
        Vector3 _horizontal;
        float _vy, _pitch, _noiseTimer;
        Vector2 _recoil;   // x = pitch kick (deg, up), y unused for decay (yaw is applied instantly)

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _interactor = GetComponent<PlayerInteractor>();

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                 .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _look   = new InputAction("Look",   InputActionType.Value,  "<Mouse>/delta");
            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _jump   = new InputAction("Jump",   InputActionType.Button, "<Keyboard>/space");
            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
        }

        void OnEnable()
        {
            _move.Enable(); _look.Enable(); _sprint.Enable(); _jump.Enable(); _crouch.Enable();
            LockCursor(true);
        }

        void OnDisable()
        {
            _move.Disable(); _look.Disable(); _sprint.Disable(); _jump.Disable(); _crouch.Disable();
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Kick the camera: pitchUp degrees upward, yaw degrees sideways.</summary>
        public void AddRecoil(float pitchUp, float yaw)
        {
            _recoil.x += pitchUp;
            transform.Rotate(0f, yaw, 0f);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LockCursor(false);
            if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame && InputEnabled) LockCursor(true);

            if (!InputEnabled) { ApplyGravityOnly(); return; }

            Look();
            Move();
        }

        void Look()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = _look.ReadValue<Vector2>() * lookSensitivity;
                transform.Rotate(0f, d.x, 0f);
                _pitch = Mathf.Clamp(_pitch - d.y, -maxPitch, maxPitch);
            }
            _recoil.x = Mathf.Lerp(_recoil.x, 0f, recoilReturnSpeed * Time.deltaTime);
            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(Mathf.Clamp(_pitch - _recoil.x, -maxPitch, maxPitch), 0f, 0f);
        }

        void Move()
        {
            // Crouch (cannot stand up under a low ceiling).
            bool wantCrouch = _crouch.IsPressed();
            if (!wantCrouch && IsCrouching && !CanStand()) wantCrouch = true;
            IsCrouching = wantCrouch;
            float targetH = IsCrouching ? crouchHeight : standHeight;
            _cc.height = Mathf.MoveTowards(_cc.height, targetH, 6f * Time.deltaTime);
            _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);
            if (cameraRoot != null)
            {
                Vector3 p = cameraRoot.localPosition;
                p.y = Mathf.Lerp(p.y, IsCrouching ? cameraCrouchY : cameraStandY, 10f * Time.deltaTime);
                cameraRoot.localPosition = p;
            }

            Vector2 m = _move.ReadValue<Vector2>();
            IsSprinting = _sprint.IsPressed() && m.y > 0.1f && !IsCrouching && _cc.isGrounded;

            float speed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            if (_interactor != null) speed = Mathf.Min(speed, _interactor.MaxCarrySpeed);

            Vector3 desired = (transform.right * m.x + transform.forward * m.y);
            if (desired.sqrMagnitude > 1f) desired.Normalize();
            desired *= speed;
            _horizontal = Vector3.MoveTowards(_horizontal, desired, acceleration * Time.deltaTime);

            if (_cc.isGrounded)
            {
                if (_vy < 0f) _vy = -2f;
                if (_jump.WasPressedThisFrame() && !IsCrouching) _vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            _vy += gravity * Time.deltaTime;

            _cc.Move((_horizontal + Vector3.up * _vy) * Time.deltaTime);
            EmitFootstepNoise();
        }

        void ApplyGravityOnly()
        {
            _horizontal = Vector3.MoveTowards(_horizontal, Vector3.zero, acceleration * Time.deltaTime);
            if (_cc.isGrounded && _vy < 0f) _vy = -2f;
            _vy += gravity * Time.deltaTime;
            _cc.Move((_horizontal + Vector3.up * _vy) * Time.deltaTime);
        }

        bool CanStand()
        {
            Vector3 origin = transform.position + Vector3.up * _cc.height;
            return !Physics.Raycast(origin, Vector3.up, standHeight - _cc.height + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        }

        void EmitFootstepNoise()
        {
            if (IsCrouching || !_cc.isGrounded || _horizontal.sqrMagnitude < 0.5f) { _noiseTimer = 0f; return; }
            _noiseTimer += Time.deltaTime;
            if (_noiseTimer < noiseInterval) return;
            _noiseTimer = 0f;
            InteractionNoise.Emit(transform.position, IsSprinting ? sprintNoiseLoudness : walkNoiseLoudness, gameObject);
        }

        /// <summary>Teleport (respawn). A CharacterController must be disabled to be moved directly.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _cc.enabled = true;
            _horizontal = Vector3.zero;
            _vy = 0f;
            _pitch = 0f;
        }
    }
}

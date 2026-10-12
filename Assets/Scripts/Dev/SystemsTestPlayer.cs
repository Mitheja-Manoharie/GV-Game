using UnityEngine;
using UnityEngine.InputSystem;
using GVGame.Interaction;

namespace GVGame.Dev
{
    /// <summary>
    /// MINIMAL first-person controller used ONLY by the Systems_Test scene so the interaction
    /// systems can be tested before the Core Developer's real player controller is merged.
    /// Not intended for the final game. The real player must also clamp its walking speed to
    /// PlayerInteractor.MaxCarrySpeed (see Update below) so dragging heavy props slows the player.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SystemsTestPlayer : MonoBehaviour
    {
        public Transform cameraRoot;
        public float walkSpeed = 4.5f;
        public float lookSensitivity = 0.12f;
        public float gravity = -20f;

        CharacterController _cc;
        PlayerInteractor _interactor;
        InputAction _move, _look;
        float _pitch, _vy;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _interactor = GetComponent<PlayerInteractor>();
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                 .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
        }

        void OnEnable()
        {
            _move.Enable(); _look.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDisable() { _move.Disable(); _look.Disable(); }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }

            Vector2 l = _look.ReadValue<Vector2>() * lookSensitivity;
            transform.Rotate(0f, l.x, 0f);
            _pitch = Mathf.Clamp(_pitch - l.y, -85f, 85f);
            if (cameraRoot != null) cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            // Dragging a heavy prop slows the player.
            float speed = walkSpeed;
            if (_interactor != null) speed = Mathf.Min(speed, _interactor.MaxCarrySpeed);

            Vector2 m = _move.ReadValue<Vector2>();
            Vector3 dir = transform.right * m.x + transform.forward * m.y;
            _vy = _cc.isGrounded ? -1f : _vy + gravity * Time.deltaTime;
            _cc.Move((dir * speed + Vector3.up * _vy) * Time.deltaTime);
        }
    }
}
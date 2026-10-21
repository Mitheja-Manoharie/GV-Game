using GVGame.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace GVGame.Player
{
    /// <summary>
    /// V switches between the first-person view and a third-person view over the shoulder, so the player's Alien
    /// body (and its animations) can be seen. In first person the body only casts shadows, and most lights in the
    /// level are baked, so it is otherwise invisible.
    ///   - Third person: the body and its rifle render normally, the first-person gun model is hidden, and the
    ///     camera is pulled in when a wall is behind the player.
    ///   - Aiming still goes through the crosshair (the weapon shoots from the camera and ignores the player's own
    ///     colliders). Doors and props are reached from the camera, so in third person they need to be closer.
    /// Added by GV Game > Agent Setup > 7 (or 8 on its own).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerViewToggle : MonoBehaviour
    {
        [Tooltip("The player camera (under CameraRoot). Found in the children if empty.")]
        public Camera viewCamera;
        [Tooltip("The Alien body's Animator. Taken from PlayerBodyAnimator if empty.")]
        public Animator body;
        [Tooltip("Camera position in third person, relative to CameraRoot (right, up, back).")]
        public Vector3 thirdPersonOffset = new Vector3(0.45f, 0.25f, -2.6f);
        [Tooltip("How far the camera stays from walls (m).")]
        public float collisionRadius = 0.2f;
        [Tooltip("How fast the camera moves between the two views.")]
        public float blendSpeed = 10f;
        public bool startInThirdPerson;

        public bool ThirdPerson { get; private set; }

        InputAction _toggle;
        Transform _pivot;
        Vector3 _firstPersonLocal;
        Renderer[] _bodyRenderers = new Renderer[0];
        ShadowCastingMode[] _bodyModes = new ShadowCastingMode[0];
        Renderer[] _viewModelRenderers = new Renderer[0];

        void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
            if (body == null)
            {
                var bodyAnimator = GetComponent<PlayerBodyAnimator>();
                if (bodyAnimator != null) body = bodyAnimator.animator;
            }

            if (viewCamera != null)
            {
                _pivot = viewCamera.transform.parent;
                _firstPersonLocal = viewCamera.transform.localPosition;
            }

            if (body != null)
            {
                _bodyRenderers = body.GetComponentsInChildren<Renderer>(true);
                _bodyModes = new ShadowCastingMode[_bodyRenderers.Length];
                for (int i = 0; i < _bodyRenderers.Length; i++) _bodyModes[i] = _bodyRenderers[i].shadowCastingMode;
            }

            var weapon = GetComponentInChildren<WeaponController>();
            if (weapon != null && weapon.viewModel != null)
                _viewModelRenderers = weapon.viewModel.GetComponentsInChildren<Renderer>(true);

            _toggle = new InputAction("ToggleView", InputActionType.Button, "<Keyboard>/v");
        }

        void OnEnable() => _toggle.Enable();
        void OnDisable() => _toggle.Disable();

        void Start() => SetThirdPerson(startInThirdPerson);

        void Update()
        {
            if (_toggle.WasPressedThisFrame()) SetThirdPerson(!ThirdPerson);
        }

        public void SetThirdPerson(bool on)
        {
            ThirdPerson = on;
            for (int i = 0; i < _bodyRenderers.Length; i++)
                if (_bodyRenderers[i] != null)
                    _bodyRenderers[i].shadowCastingMode = on ? ShadowCastingMode.On : _bodyModes[i];
            foreach (var r in _viewModelRenderers)
                if (r != null) r.enabled = !on;
        }

        // After PlayerController has turned and raised CameraRoot this frame.
        void LateUpdate()
        {
            if (viewCamera == null || _pivot == null) return;

            Vector3 target = _firstPersonLocal;
            if (ThirdPerson)
            {
                Vector3 from = _pivot.position;
                Vector3 to = _pivot.TransformPoint(thirdPersonOffset);
                Vector3 dir = to - from;
                float distance = dir.magnitude;
                if (distance > 0.001f)
                {
                    dir /= distance;
                    foreach (var hit in Physics.SphereCastAll(from, collisionRadius, dir, distance, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider.transform.root != transform.root && hit.distance > 0f && hit.distance < distance)
                            distance = hit.distance;
                }
                target = _pivot.InverseTransformPoint(from + dir * distance);
            }

            Transform cam = viewCamera.transform;
            if (ThirdPerson && target.sqrMagnitude < cam.localPosition.sqrMagnitude)
                cam.localPosition = target; // a wall behind the player: pull in at once, never through it
            else
                cam.localPosition = Vector3.Lerp(cam.localPosition, target, 1f - Mathf.Exp(-blendSpeed * Time.deltaTime));
        }
    }
}

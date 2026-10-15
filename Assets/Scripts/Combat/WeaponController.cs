using System;
using UnityEngine;
using UnityEngine.InputSystem;
using GVGame.Interaction;
using GVGame.Player;

namespace GVGame.Combat
{
    /// <summary>
    /// Hitscan weapon. Left mouse fires (hold for full auto), R reloads.
    /// Each shot:
    ///   - raycasts from the camera (with a little random spread), ignoring the player's own colliders
    ///   - damages anything that implements IDamageable (robots, dummies)
    ///   - pushes Rigidbody props (bullets move barrels and mugs)
    ///   - raises a gunshot on InteractionNoise (the Security Hunter hunts by sound)
    ///   - kicks the camera (recoil) and shows a muzzle flash, tracer and gun kick
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("References")]
        public Camera aimCamera;
        [Tooltip("Optional gun model parented to the camera; it kicks back when firing.")]
        public Transform viewModel;
        [Tooltip("Optional unlit material for bullet tracers. No material = no tracers.")]
        public Material tracerMaterial;

        [Header("Stats")]
        public float damage = 25f;
        public float range = 120f;
        [Tooltip("Rounds per second.")]
        public float fireRate = 6f;
        public bool automatic = true;
        public int magazineSize = 12;
        public int startReserve = 48;
        public float reloadTime = 1.5f;
        [Tooltip("Random cone half-angle in degrees.")]
        public float spreadDegrees = 0.5f;
        public LayerMask hitMask = ~0;

        [Header("Feel")]
        public float recoilPitch = 1.4f;
        public float recoilYaw = 0.25f;
        public float impactForce = 4f;
        public float gunKick = 0.06f;

        [Header("AI noise")]
        [Tooltip("Loudness raised on InteractionNoise per shot (the Hunter hears this).")]
        public float gunshotLoudness = 30f;

        [Header("Start state")]
        public bool startEquipped = true;

        public int AmmoInMag { get; private set; }
        public int Reserve { get; private set; }
        public bool IsReloading { get; private set; }
        public bool IsEquipped { get; private set; }

        /// <summary>Raised when a shot damages something; the bool is true if it killed it.</summary>
        public event Action<bool> HitConfirmed;
        public event Action Fired;

        InputAction _fire, _reload;
        PlayerController _player;
        float _nextFire, _reloadEnd;
        Light _flash;
        float _flashEnd;
        Vector3 _vmRest;
        float _vmKick;

        void Awake()
        {
            if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();
            _player = GetComponent<PlayerController>();
            _fire = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            _reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");

            if (viewModel != null) _vmRest = viewModel.localPosition;

            var flashGo = new GameObject("MuzzleFlash");
            flashGo.transform.SetParent(aimCamera != null ? aimCamera.transform : transform, false);
            flashGo.transform.localPosition = new Vector3(0.2f, -0.12f, 0.8f);
            _flash = flashGo.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.range = 7f;
            _flash.intensity = 3f;
            _flash.color = new Color(1f, 0.8f, 0.5f);
            _flash.enabled = false;

            IsEquipped = startEquipped;
            Reserve = startReserve;
            AmmoInMag = startEquipped ? magazineSize : 0;
        }

        void OnEnable()  { _fire.Enable(); _reload.Enable(); }
        void OnDisable() { _fire.Disable(); _reload.Disable(); }

        /// <summary>Called by WeaponPickup: gives the weapon and some ammo.</summary>
        public void Equip(int extraAmmo)
        {
            bool first = !IsEquipped;
            IsEquipped = true;
            Reserve += extraAmmo;
            if (first) AmmoInMag = magazineSize;
        }

        public void AddAmmo(int amount) { Reserve += amount; }

        public void RefillAmmo()
        {
            IsReloading = false;
            AmmoInMag = IsEquipped ? magazineSize : 0;
            Reserve = startReserve;
        }

        void Update()
        {
            if (_flash.enabled && Time.time >= _flashEnd) _flash.enabled = false;
            UpdateViewModel();

            if (!IsEquipped || Cursor.lockState != CursorLockMode.Locked) return;

            if (IsReloading)
            {
                if (Time.time >= _reloadEnd) FinishReload();
                return;
            }

            if (_reload.WasPressedThisFrame()) { StartReload(); return; }

            bool wantFire = automatic ? _fire.IsPressed() : _fire.WasPressedThisFrame();
            if (wantFire && Time.time >= _nextFire)
            {
                if (AmmoInMag > 0) Shoot();
                else if (Reserve > 0) StartReload();
            }
        }

        void StartReload()
        {
            if (IsReloading || AmmoInMag >= magazineSize || Reserve <= 0) return;
            IsReloading = true;
            _reloadEnd = Time.time + reloadTime;
        }

        void FinishReload()
        {
            int take = Mathf.Min(magazineSize - AmmoInMag, Reserve);
            AmmoInMag += take;
            Reserve -= take;
            IsReloading = false;
        }

        void Shoot()
        {
            _nextFire = Time.time + 1f / Mathf.Max(0.1f, fireRate);
            AmmoInMag--;

            Transform cam = aimCamera.transform;
            Vector3 origin = cam.position;
            Vector2 s = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            Vector3 dir = (cam.forward + cam.right * s.x + cam.up * s.y).normalized;

            Vector3 end = origin + dir * range;
            if (RaycastNearest(origin, dir, out RaycastHit hit))
            {
                end = hit.point;
                ApplyHit(hit, dir);
            }

            SpawnTracer(cam.position + cam.right * 0.2f - cam.up * 0.12f + cam.forward * 0.7f, end);
            _flash.enabled = true;
            _flashEnd = Time.time + 0.04f;
            _vmKick = 1f;

            if (_player != null) _player.AddRecoil(recoilPitch, UnityEngine.Random.Range(-recoilYaw, recoilYaw));
            InteractionNoise.Emit(origin, gunshotLoudness, gameObject);
            Fired?.Invoke();
        }

        // Nearest solid hit that is not part of the player.
        bool RaycastNearest(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            bool found = false;
            float bestDist = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(origin, dir, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.root == transform.root) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        void ApplyHit(RaycastHit hit, Vector3 dir)
        {
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive)
            {
                target.TakeDamage(new DamageInfo(damage, hit.point, dir, gameObject));
                HitConfirmed?.Invoke(!target.IsAlive);
            }

            Rigidbody rb = hit.rigidbody;
            if (rb != null && !rb.isKinematic)
                rb.AddForceAtPosition(dir * impactForce, hit.point, ForceMode.Impulse);
        }

        void SpawnTracer(Vector3 from, Vector3 to)
        {
            if (tracerMaterial == null) return;
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = tracerMaterial;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = 0.025f;
            lr.endWidth = 0.01f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(go, 0.05f);
        }

        void UpdateViewModel()
        {
            if (viewModel == null) return;
            _vmKick = Mathf.MoveTowards(_vmKick, 0f, Time.deltaTime * 9f);
            float reloadDip = IsReloading ? 0.12f : 0f;
            Vector3 target = _vmRest + new Vector3(0f, -reloadDip, -gunKick * _vmKick);
            viewModel.localPosition = Vector3.Lerp(viewModel.localPosition, target, 20f * Time.deltaTime);
            viewModel.gameObject.SetActive(IsEquipped);
        }
    }
}

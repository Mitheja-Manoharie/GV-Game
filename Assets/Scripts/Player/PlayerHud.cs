using UnityEngine;
using UnityEngine.InputSystem;
using GVGame.Combat;

namespace GVGame.Player
{
    /// <summary>
    /// Minimal on-screen HUD drawn with IMGUI (no Canvas assets needed):
    /// health bar, ammo counter, damage flash, hit marker, death screen (R to respawn).
    /// </summary>
    public class PlayerHud : MonoBehaviour
    {
        PlayerHealth _health;
        WeaponController _weapon;
        float _flash, _hitMarker;
        bool _killMarker;
        GUIStyle _label, _big;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _weapon = GetComponent<WeaponController>();
        }

        void OnEnable()
        {
            if (_health != null) _health.Damaged += OnDamaged;
            if (_weapon != null) _weapon.HitConfirmed += OnHitConfirmed;
        }

        void OnDisable()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
            if (_weapon != null) _weapon.HitConfirmed -= OnHitConfirmed;
        }

        void OnDamaged(DamageInfo _) => _flash = 1f;
        void OnHitConfirmed(bool killed) { _hitMarker = 0.18f; _killMarker = killed; }

        void Update()
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 1.6f);
            _hitMarker = Mathf.MoveTowards(_hitMarker, 0f, Time.deltaTime);
            if (_health != null && !_health.IsAlive && Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
                _health.Respawn();
        }

        static void Rect_(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void OnGUI()
        {
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                _label.normal.textColor = Color.white;
                _big = new GUIStyle(_label) { fontSize = 42, alignment = TextAnchor.MiddleCenter };
            }
            float w = Screen.width, h = Screen.height;

            if (_flash > 0f) Rect_(new Rect(0, 0, w, h), new Color(0.8f, 0f, 0f, _flash * 0.4f));

            if (_health != null)
            {
                Rect bar = new Rect(24, h - 44, 260, 20);
                Rect_(bar, new Color(0f, 0f, 0f, 0.55f));
                Color c = Color.Lerp(new Color(0.85f, 0.15f, 0.1f), new Color(0.25f, 0.85f, 0.3f), _health.Health01);
                Rect_(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * _health.Health01, bar.height - 4), c);
                GUI.Label(new Rect(bar.x, bar.y - 26, 260, 24), "HEALTH  " + Mathf.CeilToInt(_health.Health), _label);
            }

            if (_weapon != null && _weapon.IsEquipped)
            {
                string ammo = _weapon.IsReloading ? "RELOADING" : _weapon.AmmoInMag + " / " + _weapon.Reserve;
                GUI.Label(new Rect(w - 220, h - 44, 200, 30), ammo, _label);
            }

            if (_hitMarker > 0f)
            {
                Color m = _killMarker ? new Color(1f, 0.25f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.95f);
                float cx = w * 0.5f, cy = h * 0.5f;
                Rect_(new Rect(cx - 12, cy - 1, 8, 2), m); Rect_(new Rect(cx + 4, cy - 1, 8, 2), m);
                Rect_(new Rect(cx - 1, cy - 12, 2, 8), m); Rect_(new Rect(cx - 1, cy + 4, 2, 8), m);
            }

            if (_health != null && !_health.IsAlive)
            {
                Rect_(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(new Rect(0, h * 0.4f, w, 60), "YOU DIED", _big);
                GUI.Label(new Rect(0, h * 0.4f + 60, w, 30), "Press R to respawn",
                          new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
            }
        }
    }
}

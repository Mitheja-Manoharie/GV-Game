using GVGame.Combat;
using GVGame.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GVGame.Level
{
    /// <summary>
    /// The win condition: when the player reaches the exit in the control room, the run ends with a
    /// "YOU ESCAPED" screen showing the escape time and robots defeated.
    ///
    /// Put it on the ExitPoint marker (Environment/ControlRoom/ExitPoint). It adds and sizes its own trigger box.
    /// On escape the game is paused (Time.timeScale = 0), so robots stop and the Facility AI makes no more
    /// decisions, and the cursor is released.
    ///
    /// R plays again without reloading the scene: it uses the player's normal respawn, which already moves the
    /// player to the start, refills health and ammo, removes the robots and resets the Facility AI.
    /// Q quits a build; in the Editor, stop Play mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExitTrigger : MonoBehaviour
    {
        [Tooltip("Size of the exit zone (metres). Centred on this marker, raised half its height off the floor.")]
        [SerializeField] private Vector3 zoneSize = new Vector3(3f, 2.5f, 2f);

        private PlayerHealth player;
        private WeaponController weapon;
        private bool escaped;
        private float escapeTime, runStartTime;
        private int robotsDefeated;
        private GUIStyle big, small;

        private void Reset() => ConfigureTrigger();

        private void Awake()
        {
            ConfigureTrigger();

            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
            {
                player = tagged.GetComponentInParent<PlayerHealth>();
                weapon = tagged.GetComponentInChildren<WeaponController>();
            }

            if (player == null) Debug.LogWarning("[ExitTrigger] No PlayerHealth on the Player; the exit will not work.", this);
        }

        private void OnEnable()
        {
            if (weapon != null) weapon.HitConfirmed += OnHitConfirmed;
            if (player != null) player.Respawned += OnPlayerRespawned;
        }

        private void OnDisable()
        {
            if (weapon != null) weapon.HitConfirmed -= OnHitConfirmed;
            if (player != null) player.Respawned -= OnPlayerRespawned;
            if (escaped) Time.timeScale = 1f; // never leave the Editor paused
        }

        // A new life is a new run: the escape time and robot count start again after death too.
        private void OnPlayerRespawned()
        {
            runStartTime = Time.timeSinceLevelLoad;
            robotsDefeated = 0;
        }

        private void OnHitConfirmed(bool killed)
        {
            if (killed) robotsDefeated++;
        }

        private void ConfigureTrigger()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = zoneSize;
            box.center = new Vector3(0f, zoneSize.y * 0.5f, 0f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (escaped || player == null) return;
            if (other.GetComponentInParent<PlayerHealth>() != player || !player.IsAlive) return;

            escaped = true;
            escapeTime = Time.timeSinceLevelLoad - runStartTime;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log($"[ExitTrigger] Player escaped in {escapeTime:0} s, {robotsDefeated} robots defeated.", this);
        }

        private void Update()
        {
            if (!escaped || Keyboard.current == null) return;
            if (Keyboard.current.rKey.wasPressedThisFrame) PlayAgain();
            else if (Keyboard.current.qKey.wasPressedThisFrame) Application.Quit();
        }

        /// <summary>Starts a new run from the quarantine cell.</summary>
        public void PlayAgain()
        {
            escaped = false;
            robotsDefeated = 0;
            Time.timeScale = 1f;
            runStartTime = Time.timeSinceLevelLoad;
            player.Respawn(); // teleports to the start; robots are cleared and the Director resets on Respawned
        }

        private void OnGUI()
        {
            if (!escaped) return;

            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(0.55f, 0.95f, 0.65f);
                small = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
                small.normal.textColor = Color.white;
            }

            float w = Screen.width, h = Screen.height;
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = previous;

            int minutes = Mathf.FloorToInt(escapeTime / 60f), seconds = Mathf.FloorToInt(escapeTime % 60f);
            GUI.Label(new Rect(0, h * 0.38f, w, 60), "YOU ESCAPED", big);
            GUI.Label(new Rect(0, h * 0.38f + 64, w, 30), $"Time {minutes}:{seconds:00}    Robots defeated {robotsDefeated}", small);
            GUI.Label(new Rect(0, h * 0.38f + 100, w, 30), "Press R to play again    Q to quit", small);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(new Vector3(0f, zoneSize.y * 0.5f, 0f), zoneSize);
        }
    }
}

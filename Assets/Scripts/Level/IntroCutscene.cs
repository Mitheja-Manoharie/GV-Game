using System;
using System.Collections;
using System.Collections.Generic;
using GVGame.Agents;
using GVGame.Combat;
using GVGame.Interaction;
using GVGame.Player;
using LabLockdown.AI.Facility.Integration;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GVGame.Level
{
    /// <summary>
    /// Intro cutscene at the start of Main: the camera visits the facility in seven shots, with captions in
    /// cinema letterbox bars, then the title, then the player gets control. Space or Enter skips it.
    ///
    ///   1. Overhead       the whole facility from above (the ceiling plates face down, so they are not drawn from above),
    ///                     opening with the lockdown alarm: every AlarmBeacon spinning and a few seconds of siren
    ///   2. Lab corridor   the Facility AI locks the lab door: it slams shut and its access panel turns red
    ///   3. Control room   push-in on the exit window: where the player has to go
    ///   4. Research lab   low dolly over the scientists' desk to the specimen tanks
    ///      Keycard        a close-up of the keycard turning and glinting, wherever it is this run, with the
    ///                     background blurred so the room cannot be recognised
    ///   5. Maintenance bay  high crane across the main fight space, with a Security Hunter and a Tactical Flanker
    ///                     walking through it
    ///   6. Quarantine cell  the camera circles the player's own body and ends on a three-quarter view of the face
    ///
    /// Camera animation is keyframed: each shot has 2–4 keys (camera position + the point it looks at). Both are
    /// interpolated with a Catmull-Rom spline, which passes through every key with no corners in the path (C1
    /// continuous), and the shot's time is eased in and out (smoothstep), so the camera accelerates from rest and
    /// settles at the end instead of starting and stopping abruptly. Shots are joined by short fades through black.
    /// The keys are fields on this component and the paths are drawn in the Scene view when it is selected.
    /// The last shot is built at run time around wherever the player stands: keys on an arc round the player, closing
    /// in and dropping from above the head to just under eye level (a slightly low, heroic angle), while the aim moves
    /// from the chest to the face. Raycasts at the camera's heights find how far the arc can go on each side without
    /// meeting a wall (the walls lean inwards), so it starts on the side with more room, passes in front of the player
    /// and ends just past the front. A spot light from above and in front lights the player for this shot, because the
    /// quarantine cell is the darkest room.
    ///
    /// It borrows the player's own camera (same post-processing, field of view and culling) and gives it back at
    /// the end. Occlusion culling is off while it plays, because the overhead shot is outside the baked cells.
    /// Gameplay waits: the components named in "Hold Until End" (the Facility AI director, the drone spawner, the
    /// weapon, interaction, HUD and view toggle) are switched off before their Start runs, so the Facility AI's
    /// clocks and the drones start only when the player gets control. The player's body is shown in third-person
    /// mode for the last shot. It plays once per session (respawns and replays do not show it again).
    ///
    /// Robots only exist once the Facility AI spawns them, so the bay shot brings its own: copies of the
    /// RobotSpawner's Hunter and Flanker prefabs, made inside an inactive parent so their brains, guns and movers can be
    /// switched off before they wake up ("Robot Parts Off"). Only the body, its animation and tint stay on. The Flanker
    /// walks with its NavMeshAgent; the Hunter (which moves itself in play) is walked along a NavMesh path here. Both
    /// animate from their real speed through RobotAnimator, and both are removed before the player gets control.
    ///
    /// Lockdown alarm: for the first "Alarm Seconds" every AlarmBeacon is switched on (AlarmBeacon.Forced), whatever
    /// the Facility AI's phase, and a siren loops. The siren is made in code, like the level's other sounds: a tone
    /// that sweeps up from 420 to 980 Hz in 0.7 s with a slightly detuned second voice, softly clipped, then 0.3 s of
    /// silence, so the one-second clip loops seamlessly; a hallway reverb puts it in the building. A clip can be
    /// assigned instead. Both fade out, and the beacons go back to following the Facility AI.
    ///
    /// Door slam: a shot with "Lock Door" set (the lab corridor shot: Door_LabSouth) has that door opened when the
    /// cutscene starts, swinging away from the shot's camera, so it stands wide open when the shot begins. At "Lock At"
    /// seconds the door is locked through the Facility AI's own door service (DoorControllerDoorService, the same call
    /// a Facility AI decision makes: close, lock, and a NavMesh barrier), swinging at "Door Slam Speed" without the
    /// door's usual ease-out, so it shuts hard. Its DoorAccessPanel shows red with its blink and beep, and the camera
    /// shakes briefly when the door hits its frame. At the end the door is unlocked and closed again, its panel goes
    /// back to green and its settings are restored, so the game starts exactly as before.
    /// The keycard is hidden while the cutscene plays, so no shot can show where it is this run, except its own
    /// close-up: built at run time around the keycard's spot. The camera distance is chosen from the card's size so it
    /// fills most of the letterboxed frame; the side to film from is the one with room for the camera (raycasts) and
    /// the deepest background behind the card. A cutscene-only URP Volume adds Gaussian depth of field focused on the
    /// card (blur starting just behind it), and a small white key light makes the card glint as it spins.
    /// Added by Tools > World Builder > Add Intro Cutscene.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]   // Awake before the held components' Awake/Start
    public sealed class IntroCutscene : MonoBehaviour
    {
        [Serializable]
        public struct Key
        {
            public Vector3 position;
            public Vector3 lookAt;

            public Key(Vector3 position, Vector3 lookAt)
            {
                this.position = position;
                this.lookAt = lookAt;
            }
        }

        [Serializable]
        public class Shot
        {
            public string location = "";
            public string line = "";
            public float duration = 5f;
            public float fieldOfView = 55f;
            public Key[] keys = new Key[0];
            [Tooltip("Optional: a door (Door_* marker name) the Facility AI slams shut and locks during this shot.")]
            public string lockDoor = "";
            [Tooltip("Seconds into the shot when the door slams.")]
            public float lockAt = 1.5f;
        }

        public enum RobotKind { Hunter, Flanker }

        [Serializable]
        public struct Walker
        {
            public RobotKind robot;
            [Tooltip("Index of the shot (in Shots) the robot appears in.")]
            public int shot;
            public Vector3 from;
            public Vector3 to;

            public Walker(RobotKind robot, int shot, Vector3 from, Vector3 to)
            {
                this.robot = robot;
                this.shot = shot;
                this.from = from;
                this.to = to;
            }
        }

        [SerializeField] private Shot[] shots = DefaultShots();

        [Header("Robots shown in a shot (prefabs from the RobotSpawner, brains off)")]
        [SerializeField] private Walker[] robots =
        {
            new Walker(RobotKind.Hunter, 4, new Vector3(-2.5f, 0f, 16.5f), new Vector3(1.5f, 0f, 12.5f)),
            new Walker(RobotKind.Flanker, 4, new Vector3(2.5f, 0f, 17.5f), new Vector3(3f, 0f, 11f)),
        };
        [SerializeField] private float robotWalkSpeed = 1.5f;
        [Tooltip("Components (class name) switched off on the cutscene's robots, so they cannot think, move or shoot.")]
        [SerializeField] private string[] robotPartsOff =
        {
            "HunterAgent", "HunterMover", "HunterShooter", "FlankerAgent", "RobotPathFollower", "RobotGun", "GunshotAudio",
            "StalkerAgent", "StalkerSensor", "StalkerDebugView",
        };

        [Header("Last shot: the camera circles the player")]
        [SerializeField] private string finalLocation = "QUARANTINE CELL";
        [SerializeField] private string finalLine = "You are the breach. Find the keycard and get out.";
        [SerializeField] private float finalDuration = 7f;
        [SerializeField] private float finalFieldOfView = 45f;
        [Tooltip("Furthest the orbit starts from the front of the player (degrees). Less if a wall is in the way.")]
        [SerializeField] private float orbitDegrees = 150f;
        [Tooltip("Camera distance from the player at the start (x) and the end (y) of the orbit (m).")]
        [SerializeField] private Vector2 orbitRadius = new Vector2(2f, 1.45f);
        [Tooltip("Spot light on the player during the orbit (the quarantine cell is the darkest room). 0 = no light.")]
        [SerializeField] private float playerLightIntensity = 2.5f;

        [Header("Lockdown alarm (opening shots)")]
        [Tooltip("Seconds of alarm at the start (alarm beacons spinning and a siren, one whoop per second), then both fade out. 0 = no alarm.")]
        [SerializeField] private float alarmSeconds = 6f;
        [Tooltip("Siren sound (looped). Empty = a siren made in code.")]
        [SerializeField] private AudioClip alarmClip;
        [UnityEngine.Range(0f, 1f)] [SerializeField] private float alarmVolume = 0.35f;

        [Header("Keycard close-up")]
        [Tooltip("Index of the shot (in Shots) after which the keycard close-up plays. -1 = no close-up.")]
        [SerializeField] private int keycardAfterShot = 3;
        [SerializeField] private string keycardLocation = "LOCATION UNKNOWN";
        [SerializeField] private string keycardLine = "It could be anywhere.";
        [SerializeField] private float keycardDuration = 3.5f;
        [SerializeField] private float keycardFieldOfView = 30f;

        [Header("Door slam")]
        [Tooltip("Degrees per second a slammed door swings, at a constant speed (doors normally swing at 140 and slow down as they close).")]
        [SerializeField] private float doorSlamSpeed = 190f;

        [Header("Playback")]
        [SerializeField] private string title = "LAB LOCKDOWN";
        [Tooltip("Off: skip the cutscene when pressing Play in the Editor (builds always play it).")]
        [SerializeField] private bool playInEditor = true;
        [Tooltip("Components (class name) switched off until the player gets control. Their Start waits too.")]
        [SerializeField] private string[] holdUntilEnd =
        {
            "FacilityDirectorRunner", "DroneStalkerSpawn", "WeaponController", "PlayerInteractor", "PlayerHud", "PlayerViewToggle",
        };

        private const float Fade = 0.45f;            // s, fade through black between shots
        private const float CharsPerSecond = 38f;    // caption typing speed
        private const float Letterbox = 2.39f;       // cinema aspect ratio of the picture between the bars
        private const float OrbitHigh = 0.9f;        // m above the eye at the start of the orbit
        private const float OrbitLow = -0.15f;       // m below the eye at the end (looking slightly up at the face)
        private const float OrbitEnd = 30f;          // degrees past the front where the orbit ends (three-quarter view)
        private const float WallMargin = 0.35f;      // m kept between the camera and any wall
        private const float ShakeSeconds = 0.35f;    // camera shake after a door slam

        /// <summary>True while the intro is playing.</summary>
        public static bool IsPlaying { get; private set; }

        private static bool playedThisSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => playedThisSession = false;

        private readonly List<Behaviour> held = new List<Behaviour>();
        private PlayerController player;
        private PlayerViewToggle viewToggle;
        private GameObject viewModel;
        private bool viewToggleWasThirdPerson, viewModelWasActive;
        private Camera cam;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private float savedFieldOfView;
        private bool savedOcclusion;
        private Light playerLight;
        private readonly List<AlarmBeacon> beacons = new List<AlarmBeacon>();

        // Doors slammed by a shot, with what to put back.
        private sealed class DoorSlam
        {
            public string id;
            public DoorController door;
            public float swingSpeed, autoCloseDelay;
            public bool smooth, wasClosed, slammed;
        }

        private readonly List<DoorSlam> doorSlams = new List<DoorSlam>();
        private IDoorService doorService;
        private float shakeUntil;
        private DoorController slamming;   // the slammed door, until it hits its frame
        private Volume focusVolume;
        private VolumeProfile focusProfile;
        private DepthOfField depthOfField;
        private Vector3 focusPoint;
        private Light keycardLight;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Light> hiddenLights = new List<Light>();
        private AudioSource siren;
        private bool alarmOn;
        private float alarmUntil;

        // What is on screen this frame.
        private bool driving, showing, skipping;
        private Vector3 posePosition;
        private Quaternion poseRotation;
        private float poseFov;
        private float black = 1f, bars = 1f, titleAlpha, hintAlpha;
        private string locationText = "", lineText = "";
        private int lineChars;
        private GUIStyle locationStyle, lineStyle, hintStyle, titleStyle;
        private int styleHeight;

        // The cutscene's own robots: one parent, and the ones walked by hand (no NavMeshAgent).
        private GameObject robotRoot;
        private readonly List<(Transform body, Vector3[] path, int next)> handWalked = new List<(Transform, Vector3[], int)>();

        private void Awake()
        {
            bool skip = playedThisSession;
#if UNITY_EDITOR
            skip |= !playInEditor;
#endif
            player = FindAnyObjectByType<PlayerController>();
            if (player == null) Debug.LogWarning("[IntroCutscene] No PlayerController in the scene; the intro is skipped.", this);
            if (skip || player == null)
            {
                enabled = false;
                return;
            }

            // Hold gameplay before anything starts: a component switched off before its Start gets Start when it is
            // switched on again, so the Facility AI, the drones and the HUD all begin when the player gets control.
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour != this && behaviour.enabled && Array.IndexOf(holdUntilEnd, behaviour.GetType().Name) >= 0)
                {
                    behaviour.enabled = false;
                    held.Add(behaviour);
                }

            player.InputEnabled = false;
            playedThisSession = true;
            IsPlaying = showing = true;
        }

        private void Start()
        {
            cam = player.GetComponentInChildren<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[IntroCutscene] No player camera; the intro is skipped.", this);
                Restore();
                showing = false;
                enabled = false;
                return;
            }

            var t = cam.transform;
            savedLocalPosition = t.localPosition;
            savedLocalRotation = t.localRotation;
            savedFieldOfView = cam.fieldOfView;
            savedOcclusion = cam.useOcclusionCulling;
            cam.useOcclusionCulling = false;

            // Show the player's body for the last shot: third-person mode renders it and hides the first-person gun.
            viewToggle = player.GetComponentInChildren<PlayerViewToggle>(true);
            if (viewToggle != null)
            {
                viewToggleWasThirdPerson = viewToggle.ThirdPerson;
                viewToggle.SetThirdPerson(true);
            }
            else
            {
                var weapon = player.GetComponentInChildren<WeaponController>(true);
                if (weapon != null && weapon.viewModel != null)
                {
                    viewModel = weapon.viewModel.gameObject;
                    viewModelWasActive = viewModel.activeSelf;
                    viewModel.SetActive(false);
                }
            }

            HideKeycard();
            PrepareDoors();
            driving = true;
            if (alarmSeconds > 0f) StartAlarm();
            StartCoroutine(Play());
        }

        private void Update()
        {
            WalkRobots();
            if (alarmOn && Time.unscaledTime >= alarmUntil) StopAlarm();
            if (siren != null)
            {
                // Fade in fast, fade out slowly; stop the source once silent.
                float target = alarmOn ? alarmVolume : 0f;
                siren.volume = Mathf.MoveTowards(siren.volume, target, Time.unscaledDeltaTime * (alarmOn ? 3f : 0.9f) * Mathf.Max(alarmVolume, 0.01f));
                if (!alarmOn && siren.volume <= 0f && siren.isPlaying) siren.Stop();
            }
            if (!driving || skipping) return;
            var keyboard = Keyboard.current;
            // Not Escape: PlayerController uses it to free the mouse cursor.
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                                     keyboard.numpadEnterKey.wasPressedThisFrame))
                skipping = true;
        }

        private void LateUpdate()
        {
            if (!driving || cam == null) return;
            cam.transform.SetPositionAndRotation(posePosition, poseRotation);
            cam.fieldOfView = poseFov;
            if (depthOfField != null)
            {
                // Sharp up to just behind the card, fully blurred a metre further back.
                float distance = Vector3.Distance(posePosition, focusPoint);
                depthOfField.gaussianStart.value = distance + 0.12f;
                depthOfField.gaussianEnd.value = distance + 1f;
            }
        }

        // ---------- timeline ----------

        private IEnumerator Play()
        {
            for (int i = 0; i < shots.Length; i++)
                if (shots[i] != null && shots[i].keys != null && shots[i].keys.Length > 0)
                {
                    SpawnRobots(i);
                    yield return PlayShot(shots[i]);
                    RemoveRobots();
                    if (i == keycardAfterShot && !skipping) yield return PlayKeycardShot();
                }
            // Built now, around where the player's body actually is (it may have settled since the start), from the
            // camera's rest pose under its parent.
            StopAlarm();
            RestingEye(cam.transform.parent, savedLocalPosition, savedLocalRotation, null, out var eye, out var forward);
            LightPlayer(eye, forward);
            yield return PlayShot(FinalShot(eye, forward, player.transform));
            yield return Outro();
        }

        private IEnumerator PlayShot(Shot shot)
        {
            locationText = shot.location;
            lineText = shot.line;
            float duration = Mathf.Max(2f * Fade + 0.5f, shot.duration);
            bool slammed = string.IsNullOrEmpty(shot.lockDoor);
            for (float t = 0f; t < duration && !skipping; t += Time.unscaledDeltaTime)
            {
                if (!slammed && t >= shot.lockAt)
                {
                    slammed = true;
                    SlamDoor(shot.lockDoor);
                }
                if (slamming != null && slamming.Current == DoorController.State.Closed)
                {
                    shakeUntil = Time.unscaledTime + ShakeSeconds;   // the door has hit its frame
                    slamming = null;
                }
                Evaluate(shot, t / duration, out posePosition, out poseRotation);
                if (Time.unscaledTime < shakeUntil)
                {
                    // A short, fading camera shake when a door slams.
                    float k = (shakeUntil - Time.unscaledTime) / ShakeSeconds, n = Time.unscaledTime * 40f;
                    posePosition += new Vector3(Mathf.PerlinNoise(n, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, n) - 0.5f, 0f) * (0.07f * k);
                }
                poseFov = shot.fieldOfView;
                black = Mathf.Max(1f - t / Fade, Mathf.Clamp01((t - (duration - Fade)) / Fade));
                lineChars = Mathf.Clamp(Mathf.FloorToInt((t - Fade) * CharsPerSecond), 0, lineText.Length);
                hintAlpha = Mathf.Clamp01(hintAlpha + Time.unscaledDeltaTime);
                yield return null;
            }
        }

        private IEnumerator Outro()
        {
            // To black (quickly when skipped), then the title on black.
            hintAlpha = 0f;
            float from = black;
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                black = Mathf.Lerp(from, 1f, t / 0.3f);
                yield return null;
            }
            black = 1f;
            locationText = lineText = "";
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                titleAlpha = t / 0.5f;
                yield return null;
            }
            titleAlpha = 1f;
            float hold = skipping ? 0.4f : 1.1f;
            for (float t = 0f; t < hold; t += Time.unscaledDeltaTime) yield return null;

            // Hand over: camera back in the player's eyes, gameplay on, then the picture fades in under the title.
            Restore();
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
            {
                float k = t / 1.2f;
                black = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
                bars = 1f - Mathf.SmoothStep(0f, 1f, k);
                titleAlpha = 1f - Mathf.Clamp01((k - 0.35f) / 0.65f);
                yield return null;
            }
            if (siren != null) Destroy(siren.gameObject);
            showing = false;
            enabled = false;
        }

        /// <summary>Gives the camera back and switches gameplay on. Safe to call once.</summary>
        private void Restore()
        {
            driving = false;
            slamming = null;
            EndKeycardShot();
            StopAlarm();
            RestoreDoors();
            ShowKeycard();
            RemoveRobots();
            if (playerLight != null) Destroy(playerLight.gameObject);
            playerLight = null;
            if (cam != null)
            {
                cam.transform.localPosition = savedLocalPosition;
                cam.transform.localRotation = savedLocalRotation;
                cam.fieldOfView = savedFieldOfView;
                cam.useOcclusionCulling = savedOcclusion;
            }
            if (viewToggle != null) viewToggle.SetThirdPerson(viewToggleWasThirdPerson);
            if (viewModel != null) viewModel.SetActive(viewModelWasActive);
            foreach (var behaviour in held)
                if (behaviour != null) behaviour.enabled = true;
            held.Clear();
            if (player != null) player.InputEnabled = true;
            IsPlaying = false;
        }

        private void OnDisable()
        {
            if (IsPlaying && (driving || held.Count > 0)) Restore();   // never leave the game held
            if (siren != null) Destroy(siren.gameObject);
        }

        // ---------- door slam ----------

        /// <summary>
        /// Opens every door a shot will slam, swinging away from that shot's camera, so it stands wide open when the
        /// shot starts. Doors that are already locked are left alone.
        /// </summary>
        private void PrepareDoors()
        {
            doorService = FindAnyObjectByType<DoorControllerDoorService>();
            var doorsRoot = GameObject.Find("Environment/Doors");
            foreach (var shot in shots)
            {
                if (shot == null || string.IsNullOrEmpty(shot.lockDoor) || doorSlams.Exists(d => d.id == shot.lockDoor)) continue;
                var marker = doorsRoot != null ? doorsRoot.transform.Find(shot.lockDoor) : null;
                var door = marker != null ? marker.GetComponentInChildren<DoorController>() : null;
                if (door == null)
                {
                    Debug.LogWarning($"[IntroCutscene] No DoorController for {shot.lockDoor}; that shot's door slam is skipped.", this);
                    continue;
                }
                if (door.IsLocked) continue;

                doorSlams.Add(new DoorSlam
                {
                    id = shot.lockDoor, door = door, swingSpeed = door.swingSpeed, smooth = door.smooth,
                    autoCloseDelay = door.autoCloseDelay, wasClosed = door.Current == DoorController.State.Closed,
                });
                door.autoCloseDelay = 0f;
                var opener = new GameObject("IntroCutscene Door Opener").transform;   // the door swings away from it
                opener.position = shot.keys != null && shot.keys.Length > 0 ? shot.keys[0].position : marker.position - marker.forward * 5f;
                door.Open(opener);
                Destroy(opener.gameObject);
            }
        }

        /// <summary>The Facility AI locks the door: it swings shut hard and its panel turns red and beeps.</summary>
        private void SlamDoor(string id)
        {
            var slam = doorSlams.Find(d => d.id == id);
            if (slam == null || slam.door == null) return;
            slam.door.swingSpeed = doorSlamSpeed;
            slam.door.smooth = false;
            if (doorService != null) doorService.SetLocked(id, true);   // what a Facility AI decision does
            else
            {
                slam.door.Close();
                slam.door.SetLocked(true);
            }
            slam.slammed = true;

            foreach (var panel in FindObjectsByType<DoorAccessPanel>(FindObjectsSortMode.None))
                if (panel.DoorId == id) panel.ShowLocked(true);
            slamming = slam.door;   // the camera shakes when it hits its frame (PlayShot)
        }

        /// <summary>Unlocks and closes every slammed door, puts its settings back and its panel back to green.</summary>
        private void RestoreDoors()
        {
            foreach (var slam in doorSlams)
            {
                if (slam.door == null) continue;
                if (slam.slammed)
                {
                    if (doorService != null) doorService.SetLocked(slam.id, false);
                    else slam.door.SetLocked(false);
                }
                slam.door.swingSpeed = slam.swingSpeed;
                slam.door.smooth = slam.smooth;
                slam.door.autoCloseDelay = slam.autoCloseDelay;
                var state = slam.door.Current;
                if (slam.wasClosed && state != DoorController.State.Closed && state != DoorController.State.Closing) slam.door.Close();

                bool locked = FacilityEvents.IsDoorLocked(slam.id);
                foreach (var panel in FindObjectsByType<DoorAccessPanel>(FindObjectsSortMode.None))
                    if (panel.DoorId == slam.id) panel.ShowLocked(locked);
            }
            doorSlams.Clear();
        }

        // ---------- keycard close-up ----------

        /// <summary>
        /// A close-up of the keycard turning and glinting at this run's spot, with the background out of focus, so it
        /// pays off "someone dropped a keycard" without showing where it is.
        /// </summary>
        private IEnumerator PlayKeycardShot()
        {
            var keycard = FindAnyObjectByType<KeycardPickup>();
            if (keycard == null || keycardDuration <= 0f) yield break;
            ShowKeycard();
            var renderers = keycard.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                HideKeycard();
                yield break;
            }
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Vector3 centre = bounds.center;
            float size = Mathf.Max(0.05f, bounds.size.y);

            // Distance at which the card fills about 65 % of the picture between the letterbox bars.
            float picture = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height) / Letterbox, 0.5f, 0.82f);
            float distance = Mathf.Clamp(size / 0.65f / (2f * Mathf.Tan(keycardFieldOfView * 0.5f * Mathf.Deg2Rad) * picture), 0.35f, 2f);

            // Film from a side with room for the camera, and the deepest background behind the card (more blur, less
            // to recognise). Every 22.5°, checked at the arc's start, middle and end.
            Vector3 best = Vector3.zero;
            float bestBehind = -1f, bestFront = 0f;
            Vector3 bestFrontDirection = Vector3.forward;
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(i * 22.5f, Vector3.up) * Vector3.forward;
                float front = Mathf.Min(Clearance(centre, Quaternion.AngleAxis(-14f, Vector3.up) * direction, keycard.transform, 4f),
                                        Clearance(centre, direction, keycard.transform, 4f),
                                        Clearance(centre, Quaternion.AngleAxis(12f, Vector3.up) * direction, keycard.transform, 4f));
                if (front > bestFront)
                {
                    bestFront = front;
                    bestFrontDirection = direction;
                }
                if (front < distance * 1.3f + 0.3f) continue;
                float behind = Clearance(centre, -direction, keycard.transform, 15f);
                if (behind > bestBehind)
                {
                    bestBehind = behind;
                    best = direction;
                }
            }
            if (bestBehind < 0f)
            {
                // Tight spot: the side with the most room, closer in.
                best = bestFrontDirection;
                distance = Mathf.Max(0.3f, Mathf.Min(distance, (bestFront - 0.3f) / 1.3f));
            }

            Vector3 Around(float degrees, float radius, float height) =>
                centre + Quaternion.AngleAxis(degrees, Vector3.up) * best * radius + Vector3.up * (size * height);
            var shot = new Shot
            {
                location = keycardLocation,
                line = keycardLine,
                duration = keycardDuration,
                fieldOfView = keycardFieldOfView,
                keys = new[]
                {
                    new Key(Around(-14f, distance * 1.3f, 0.3f), centre + Vector3.up * (size * 0.05f)),
                    new Key(Around(0f, distance * 1.12f, 0.12f), centre),
                    new Key(Around(12f, distance, -0.05f), centre),
                },
            };

            // Depth of field for this shot only: a global Volume above the level's own.
            focusPoint = centre;
            focusProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            depthOfField = focusProfile.Add<DepthOfField>(true);
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(distance + 0.12f);
            depthOfField.gaussianEnd.Override(distance + 1f);
            depthOfField.gaussianMaxRadius.Override(1.5f);
            depthOfField.highQualitySampling.Override(true);
            var volumeObject = new GameObject("IntroCutscene Depth Of Field");
            focusVolume = volumeObject.AddComponent<Volume>();
            focusVolume.isGlobal = true;
            focusVolume.priority = 200f;
            focusVolume.sharedProfile = focusProfile;

            // A small white key light from the camera's side, so the turning card catches it and glints.
            var lightObject = new GameObject("IntroCutscene Keycard Light");
            lightObject.transform.position = centre + best * (distance * 0.8f) + Vector3.up * 0.35f + Vector3.Cross(Vector3.up, best) * 0.25f;
            keycardLight = lightObject.AddComponent<Light>();
            keycardLight.type = LightType.Point;
            keycardLight.range = Mathf.Max(1.5f, distance * 2f);
            keycardLight.intensity = 1.4f;
            keycardLight.color = new Color(0.863f, 0.902f, 1f);   // cold white #DCE6FF
            keycardLight.shadows = LightShadows.None;

            yield return PlayShot(shot);
            EndKeycardShot();
        }

        private void EndKeycardShot()
        {
            depthOfField = null;
            if (focusVolume != null) Destroy(focusVolume.gameObject);
            if (focusProfile != null) Destroy(focusProfile);
            if (keycardLight != null) Destroy(keycardLight.gameObject);
            focusVolume = null;
            focusProfile = null;
            keycardLight = null;
            if (driving) HideKeycard();   // hidden again for the rest of the cutscene
        }

        // ---------- keycard ----------

        /// <summary>Hides the keycard (model and glow) for the whole cutscene, so no shot gives away this run's spot.</summary>
        private void HideKeycard()
        {
            var keycard = FindAnyObjectByType<KeycardPickup>();
            if (keycard == null) return;
            foreach (var r in keycard.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
            foreach (var l in keycard.GetComponentsInChildren<Light>(true))
                if (l.enabled) { l.enabled = false; hiddenLights.Add(l); }
        }

        private void ShowKeycard()
        {
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (var l in hiddenLights) if (l != null) l.enabled = true;
            hiddenRenderers.Clear();
            hiddenLights.Clear();
        }

        // ---------- lockdown alarm ----------

        private void StartAlarm()
        {
            beacons.Clear();
            beacons.AddRange(FindObjectsByType<AlarmBeacon>(FindObjectsSortMode.None));
            foreach (var beacon in beacons) beacon.Forced = true;

            var go = new GameObject("IntroCutscene Siren");
            go.transform.SetParent(transform, false);
            siren = go.AddComponent<AudioSource>();
            siren.clip = alarmClip != null ? alarmClip : MakeSiren();
            siren.loop = true;
            siren.playOnAwake = false;
            siren.spatialBlend = 0f;   // 2D: heard the same in every shot, even from high above the facility
            siren.volume = 0f;
            go.AddComponent<AudioReverbFilter>().reverbPreset = AudioReverbPreset.Hallway;
            siren.Play();
            alarmOn = true;
            alarmUntil = Time.unscaledTime + alarmSeconds;
        }

        /// <summary>Beacons back to following the Facility AI (they fade out), siren fading out in Update.</summary>
        private void StopAlarm()
        {
            if (!alarmOn) return;
            alarmOn = false;
            foreach (var beacon in beacons)
                if (beacon != null) beacon.Forced = false;
            beacons.Clear();
        }

        /// <summary>
        /// A one-second "whoop" siren: a tone sweeping up from 420 to 980 Hz in 0.7 s (fast at first, then levelling
        /// off), with a third harmonic and a slightly detuned second voice for a harsher, electronic sound, softly
        /// clipped, then 0.3 s of silence. Silence at both ends makes the loop seamless.
        /// </summary>
        private static AudioClip MakeSiren()
        {
            const int rate = 44100;
            const float sweep = 0.7f;
            var data = new float[rate];
            double phaseA = 0.0, phaseB = 0.0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                if (t >= sweep) break;   // the rest stays silent
                float k = t / sweep;
                float frequency = Mathf.Lerp(420f, 980f, k * (2f - k));
                phaseA += 2.0 * Math.PI * frequency / rate;
                phaseB += 2.0 * Math.PI * frequency * 1.006 / rate;
                double tone = Math.Sin(phaseA) + 0.35 * Math.Sin(3.0 * phaseA) + 0.6 * Math.Sin(phaseB);
                float envelope = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((sweep - t) / 0.08f);
                data[i] = (float)Math.Tanh(tone * 0.9) * 0.55f * envelope;
            }
            var clip = AudioClip.Create("IntroCutsceneSiren", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------- robots ----------

        /// <summary>Puts the robots listed for this shot at their start, brains off, and sets them walking.</summary>
        private void SpawnRobots(int shot)
        {
            var spawner = FindAnyObjectByType<RobotSpawner>();
            if (spawner == null) return;
            GameObject root = null;
            var walking = new List<(GameObject robot, Vector3 to)>();
            foreach (var walker in robots)
            {
                if (walker.shot != shot) continue;
                var prefab = walker.robot == RobotKind.Hunter ? spawner.hunterPrefab : spawner.flankerPrefab;
                if (prefab == null) continue;
                if (root == null)
                {
                    root = new GameObject("IntroCutscene Robots");
                    root.SetActive(false);   // children made here do not wake up until their parts are switched off
                }

                Vector3 from = OnNavMesh(walker.from), to = OnNavMesh(walker.to);
                Vector3 heading = to - from;
                heading.y = 0f;
                var robot = Instantiate(prefab, from, heading.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(heading) : Quaternion.identity, root.transform);
                foreach (var part in robot.GetComponentsInChildren<MonoBehaviour>(true))
                    if (Array.IndexOf(robotPartsOff, part.GetType().Name) >= 0) part.enabled = false;
                foreach (var body in robot.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                walking.Add((robot, to));
            }
            if (root == null) return;

            robotRoot = root;
            root.SetActive(true);
            foreach (var (robot, to) in walking)
            {
                var agent = robot.GetComponent<NavMeshAgent>();
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    // Flanker: its own legs. RobotAnimator reads the agent's speed.
                    agent.updatePosition = agent.updateRotation = true;
                    agent.speed = robotWalkSpeed;
                    agent.isStopped = false;
                    agent.SetDestination(to);
                }
                else
                {
                    // Hunter: walked here along a NavMesh path. RobotAnimator reads how far it moved.
                    var path = new NavMeshPath();
                    Vector3 from = robot.transform.position;
                    bool found = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.corners.Length > 1;
                    handWalked.Add((robot.transform, found ? path.corners : new[] { from, to }, 1));
                }
            }
        }

        private void WalkRobots()
        {
            float step = robotWalkSpeed * Time.deltaTime;
            for (int i = 0; i < handWalked.Count; i++)
            {
                var (body, path, next) = handWalked[i];
                if (body == null || next >= path.Length) continue;
                Vector3 to = path[next];
                body.position = Vector3.MoveTowards(body.position, to, step);
                Vector3 heading = to - body.position;
                heading.y = 0f;
                if (heading.sqrMagnitude > 1e-4f)
                    body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(heading), 240f * Time.deltaTime);
                if ((body.position - to).sqrMagnitude < 1e-4f) next++;
                handWalked[i] = (body, path, next);
            }
        }

        private void RemoveRobots()
        {
            handWalked.Clear();
            if (robotRoot != null) Destroy(robotRoot);
            robotRoot = null;
        }

        private static Vector3 OnNavMesh(Vector3 point) =>
            NavMesh.SamplePosition(point, out var hit, 2f, NavMesh.AllAreas) ? hit.position : point;

        // ---------- camera paths ----------

        /// <summary>Camera pose at normalised time u (0–1) of a shot: eased time, Catmull-Rom position and look target.</summary>
        private static void Evaluate(Shot shot, float u, out Vector3 position, out Quaternion rotation)
        {
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));   // ease in and out
            position = Spline(shot.keys, e, false);
            Vector3 look = Spline(shot.keys, e, true) - position;
            rotation = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
        }

        /// <summary>
        /// Uniform Catmull-Rom spline through the keys' positions (or look targets) at parameter u (0–1).
        /// The two ends are extended by reflection, so the curve starts and ends exactly on the first and last key.
        /// </summary>
        private static Vector3 Spline(Key[] keys, float u, bool look)
        {
            int n = keys.Length;
            Vector3 P(int i)
            {
                if (i < 0) return 2f * P(0) - P(1);
                if (i >= n) return 2f * P(n - 1) - P(n - 2);
                return look ? keys[i].lookAt : keys[i].position;
            }

            if (n == 1) return P(0);
            float x = Mathf.Clamp01(u) * (n - 1);
            int s = Mathf.Min(Mathf.FloorToInt(x), n - 2);
            float t = x - s, t2 = t * t, t3 = t2 * t;
            Vector3 p0 = P(s - 1), p1 = P(s), p2 = P(s + 1), p3 = P(s + 2);
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// The camera circles the player. It starts high on the side with more room (up to Orbit Degrees from the
        /// front), passes in front of the player and ends just past the front, closing in and dropping to just under
        /// eye level while its aim moves from the chest to the face. Every 10° of the arc is checked with raycasts at
        /// the highest and lowest camera height, so the orbit stops short of any wall. With no room to circle (a wall
        /// right in front of the player), it falls back to a push-in over the shoulder.
        /// </summary>
        private Shot FinalShot(Vector3 eye, Vector3 forward, Transform playerRoot)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            float radius = Mathf.Max(orbitRadius.x, orbitRadius.y);
            float a = ClearArc(eye, forward, 1, radius, playerRoot), b = ClearArc(eye, forward, -1, radius, playerRoot);
            int side = a >= b ? 1 : -1;
            float startAngle = Mathf.Max(a, b), endAngle = Mathf.Min(Mathf.Min(a, b), OrbitEnd);
            if (startAngle < 0f || startAngle + Mathf.Max(0f, endAngle) < 50f) return OverShoulderShot(eye, forward, playerRoot);
            endAngle = Mathf.Max(0f, endAngle);

            int count = Mathf.Max(3, Mathf.CeilToInt((startAngle + endAngle) / 20f) + 1);   // a key every 20° or less
            var keys = new Key[count];
            for (int i = 0; i < count; i++)
            {
                float k = i / (count - 1f);
                Vector3 direction = Quaternion.AngleAxis(side * Mathf.Lerp(startAngle, -endAngle, k), Vector3.up) * forward;
                keys[i] = new Key(
                    eye + direction * Mathf.Lerp(orbitRadius.x, orbitRadius.y, k) + Vector3.up * Mathf.Lerp(OrbitHigh, OrbitLow, k),
                    eye + Vector3.down * Mathf.Lerp(0.5f, 0.1f, k));
            }
            return new Shot { location = finalLocation, line = finalLine, duration = finalDuration, fieldOfView = finalFieldOfView, keys = keys };
        }

        /// <summary>
        /// How far (degrees, in 10° steps) the camera can go round the player from the front towards one side with
        /// the given radius before a wall gets closer than WallMargin. -1 if even the front is blocked.
        /// </summary>
        private float ClearArc(Vector3 eye, Vector3 forward, int side, float radius, Transform playerRoot)
        {
            float clear = -1f;
            for (float angle = 0f; angle <= orbitDegrees + 0.01f; angle += 10f)
            {
                Vector3 direction = Quaternion.AngleAxis(side * angle, Vector3.up) * forward;
                if (Clearance(eye + Vector3.up * OrbitHigh, direction, playerRoot, radius + 1f) < radius + WallMargin ||
                    Clearance(eye + Vector3.up * OrbitLow, direction, playerRoot, radius + 1f) < radius + WallMargin)
                    break;
                clear = angle;
            }
            return clear;
        }

        /// <summary>Fallback last shot: from high behind the right shoulder, pushing in while looking ahead.</summary>
        private Shot OverShoulderShot(Vector3 eye, Vector3 forward, Transform playerRoot)
        {
            Vector3 back = -forward, right = Vector3.Cross(Vector3.up, forward);
            float behind = Mathf.Clamp(Clearance(eye, back, playerRoot, 3.5f) - 0.6f, 0.8f, 2.8f);
            float up = Mathf.Clamp(Clearance(eye, Vector3.up, playerRoot, 3f) - 0.6f, 0.4f, 1.4f);
            Vector3 ahead = eye + forward * 8f;
            Vector3 Safe(Vector3 offset)
            {
                // Pull the key towards the eye if a wall (the walls lean inwards) is in the way.
                float distance = offset.magnitude;
                float free = Clearance(eye, offset / distance, playerRoot, distance + 0.5f);
                return eye + offset / distance * Mathf.Min(distance, Mathf.Max(0.3f, free - 0.5f));
            }
            return new Shot
            {
                location = finalLocation,
                line = finalLine,
                duration = finalDuration,
                fieldOfView = finalFieldOfView,
                keys = new[]
                {
                    new Key(Safe(back * behind + Vector3.up * up + right * 0.8f), ahead + Vector3.down * 1.2f),
                    new Key(Safe(back * (behind * 0.75f) + Vector3.up * (up * 0.6f) + right * 0.65f), ahead + Vector3.down * 0.7f),
                    new Key(Safe(back * (behind * 0.5f) + Vector3.up * (up * 0.3f) + right * 0.5f), ahead + Vector3.down * 0.3f),
                },
            };
        }

        /// <summary>
        /// The player's eye: the camera's rest pose under its parent. With a player given (Scene view preview only),
        /// it is moved down to where the player will land, found by sweeping the CharacterController's radius down
        /// from the player's feet, the same way the controller itself lands on whatever is below it.
        /// </summary>
        private static void RestingEye(Transform parent, Vector3 localPosition, Quaternion localRotation, Transform landingPlayer,
                                       out Vector3 eye, out Vector3 forward)
        {
            eye = parent != null ? parent.TransformPoint(localPosition) : localPosition;
            forward = (parent != null ? parent.rotation * localRotation : localRotation) * Vector3.forward;
            if (landingPlayer == null) return;
            var controller = landingPlayer.GetComponent<CharacterController>();
            float radius = controller != null ? controller.radius * 0.95f : 0.3f;
            Vector3 from = landingPlayer.position + Vector3.up * (radius + 0.1f);
            float nearest = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(from, radius, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.root != landingPlayer.root && hit.distance > 0f && hit.distance < nearest)
                    nearest = hit.distance;
            if (nearest < float.MaxValue && nearest > 0.12f) eye += Vector3.down * (nearest - 0.1f);
        }

        /// <summary>A soft spot light from above and in front of the player for the last shot. Removed in Restore.</summary>
        private void LightPlayer(Vector3 eye, Vector3 forward)
        {
            if (playerLightIntensity <= 0f) return;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var go = new GameObject("IntroCutscene Player Light");
            Vector3 position = eye + Vector3.up * 1.2f + forward * 1.2f;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(eye + Vector3.down * 0.4f - position));
            playerLight = go.AddComponent<Light>();
            playerLight.type = LightType.Spot;
            playerLight.spotAngle = 70f;
            playerLight.innerSpotAngle = 35f;
            playerLight.range = 6f;
            playerLight.intensity = playerLightIntensity;
            playerLight.color = new Color(0.863f, 0.902f, 1f);   // the level's cold white #DCE6FF
            playerLight.shadows = LightShadows.None;
        }

        /// <summary>Free distance from a point in a direction, ignoring the player's own colliders and triggers.</summary>
        private static float Clearance(Vector3 from, Vector3 direction, Transform ignore, float max)
        {
            float nearest = max;
            foreach (var hit in Physics.RaycastAll(from, direction, max, ~0, QueryTriggerInteraction.Ignore))
                if ((ignore == null || !hit.collider.transform.IsChildOf(ignore)) && hit.distance < nearest)
                    nearest = hit.distance;
            return nearest;
        }

        private static Shot[] DefaultShots() => new[]
        {
            new Shot
            {
                location = "SUBLEVEL B   02:14", line = "Containment breach. The facility is in lockdown.",
                duration = 6.5f, fieldOfView = 50f,
                keys = new[]
                {
                    new Key(new Vector3(2f, 50f, -20f), new Vector3(2f, 0f, 16f)),
                    new Key(new Vector3(2f, 46f, -14f), new Vector3(2f, 0f, 15f)),
                    new Key(new Vector3(2f, 42f, -8f), new Vector3(2f, 0f, 14f)),
                },
            },
            new Shot
            {
                location = "LAB CORRIDOR", line = "Its AI decides which doors stay open.",
                duration = 5.5f, fieldOfView = 55f, lockDoor = "Door_LabSouth", lockAt = 1.5f,
                keys = new[]
                {
                    // East side of the corridor, clear of the crate: the double door, the hazard stripes in front
                    // of it and the access panel on the right-hand wall are all in frame.
                    new Key(new Vector3(14.9f, 1.75f, 16.8f), new Vector3(14.4f, 1.5f, 24f)),
                    new Key(new Vector3(14.75f, 1.68f, 17.7f), new Vector3(14.35f, 1.45f, 24f)),
                    new Key(new Vector3(14.6f, 1.6f, 18.6f), new Vector3(14.3f, 1.45f, 24f)),
                },
            },
            new Shot
            {
                location = "CONTROL ROOM", line = "The only way out is through the control room.",
                duration = 5f, fieldOfView = 55f,
                keys = new[]
                {
                    new Key(new Vector3(0.4f, 2.4f, 25.4f), new Vector3(3f, 1.4f, 32f)),
                    new Key(new Vector3(1.3f, 2.15f, 26.7f), new Vector3(2.4f, 1.5f, 32f)),
                    new Key(new Vector3(2f, 1.9f, 28f), new Vector3(2f, 1.6f, 32f)),
                },
            },
            new Shot
            {
                location = "RESEARCH LAB", line = "The staff left in a hurry. Someone dropped a keycard on the way out.",
                duration = 5.5f, fieldOfView = 50f,
                keys = new[]
                {
                    new Key(new Vector3(13.3f, 1.2f, 25.4f), new Vector3(16.4f, 1.1f, 29.6f)),
                    new Key(new Vector3(14.4f, 1.35f, 26f), new Vector3(17f, 1.2f, 30.2f)),
                    new Key(new Vector3(15.3f, 1.5f, 26.3f), new Vector3(17.6f, 1.3f, 30.4f)),
                },
            },
            new Shot
            {
                location = "MAINTENANCE BAY", line = "The facility's robots hunt anything that moves.",
                duration = 5f, fieldOfView = 55f,
                keys = new[]
                {
                    new Key(new Vector3(6.6f, 3.4f, 18.6f), new Vector3(0.5f, 0.6f, 12f)),
                    new Key(new Vector3(6.9f, 3.1f, 15.8f), new Vector3(-0.5f, 0.6f, 12.5f)),
                    new Key(new Vector3(6.6f, 2.8f, 13.2f), new Vector3(-1.5f, 0.8f, 13.5f)),
                },
            },
        };

        // ---------- screen ----------

        private void OnGUI()
        {
            if (!showing) return;
            GUI.depth = -1000;   // above every other IMGUI overlay (HUD, objective line)
            float w = Screen.width, h = Screen.height;
            if (locationStyle == null || styleHeight != Screen.height) BuildStyles();

            if (black > 0f) Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, black));

            float bar = Mathf.Max((h - w / Letterbox) * 0.5f, h * 0.09f) * bars;
            if (bar > 0.5f)
            {
                Fill(new Rect(0f, 0f, w, bar), Color.black);
                Fill(new Rect(0f, h - bar, w, bar), Color.black);
            }

            float pad = Mathf.Round(h * 0.045f);
            if (!string.IsNullOrEmpty(locationText) && bars > 0.99f)
                GUI.Label(new Rect(pad, 0f, w - 2f * pad, bar), Spaced(locationText), locationStyle);
            if (lineChars > 0 && bars > 0.99f)
                GUI.Label(new Rect(pad, h - bar, w - 2f * pad, bar), lineText.Substring(0, lineChars), lineStyle);
            if (hintAlpha > 0f && bars > 0.99f)
            {
                var c = hintStyle.normal.textColor;
                hintStyle.normal.textColor = new Color(c.r, c.g, c.b, 0.45f * hintAlpha);
                GUI.Label(new Rect(pad, h - bar, w - 2f * pad, bar - pad * 0.4f), "SPACE  skip", hintStyle);
            }
            if (titleAlpha > 0f)
            {
                titleStyle.normal.textColor = new Color(1f, 1f, 1f, titleAlpha);
                GUI.Label(new Rect(0f, 0f, w, h), Spaced(title), titleStyle);
            }
        }

        private void BuildStyles()
        {
            styleHeight = Screen.height;
            float s = Screen.height / 1080f;
            locationStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(15f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
            };
            locationStyle.normal.textColor = new Color(0.353f, 0.706f, 1f, 0.95f);   // screen blue #5AB4FF
            lineStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26f * s), alignment = TextAnchor.MiddleCenter };
            lineStyle.normal.textColor = new Color(0.92f, 0.94f, 1f, 0.95f);         // cold white
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13f * s), alignment = TextAnchor.LowerRight };
            hintStyle.normal.textColor = Color.white;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(64f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
        }

        private static string Spaced(string text) => string.Join(" ", text.ToCharArray());

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // ---------- Scene view ----------

        private void OnDrawGizmosSelected()
        {
            Color[] colours = { new Color(0.86f, 0.9f, 1f), new Color(1f, 0.23f, 0.19f), new Color(0.42f, 1f, 0.48f), new Color(0.35f, 0.7f, 1f) };
            for (int i = 0; i < shots.Length; i++)
                if (shots[i] != null && shots[i].keys != null && shots[i].keys.Length > 0)
                    DrawShot(shots[i], colours[i % colours.Length]);
            foreach (var walker in robots)
            {
                Gizmos.color = colours[Mathf.Abs(walker.shot) % colours.Length];
                Gizmos.DrawWireCube(walker.from + Vector3.up, new Vector3(0.6f, 2f, 0.6f));
                Gizmos.DrawLine(walker.from + Vector3.up * 0.05f, walker.to + Vector3.up * 0.05f);
            }

            var p = FindAnyObjectByType<PlayerController>();
            var c = p != null ? p.GetComponentInChildren<Camera>() : null;
            if (c != null)
            {
                RestingEye(c.transform.parent, c.transform.localPosition, c.transform.localRotation, p.transform, out var eye, out var forward);
                DrawShot(FinalShot(eye, forward, p.transform), Color.yellow);
            }
        }

        private static void DrawShot(Shot shot, Color colour)
        {
            Gizmos.color = colour;
            Vector3 previous = Spline(shot.keys, 0f, false);
            for (int i = 1; i <= 40; i++)
            {
                Vector3 next = Spline(shot.keys, i / 40f, false);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
            foreach (var key in shot.keys)
            {
                Gizmos.DrawSphere(key.position, 0.12f);
                Gizmos.DrawLine(key.position, key.position + (key.lookAt - key.position).normalized * 1.5f);
            }
        }
    }
}

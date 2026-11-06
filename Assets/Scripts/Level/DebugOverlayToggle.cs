using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GVGame.Level
{
    /// <summary>
    /// One key (F1) shows or hides every AI debug overlay at once, so normal play and the showcase build start with a
    /// clean screen and the AI internals are still one key away for the viva.
    ///
    /// It drives the existing switches on the teammates' overlays without changing their scripts: the Facility AI
    /// panel and intensity graph (FacilityDirectorRunner.showOverlay, plus its per-decision Console log), the Security
    /// Hunter panel (HunterAgent.showOverlay), the Tactical Flankers panel (FlankerAgent.showOverlay) and the Drone
    /// Stalker panel (StalkerDebugView.showPanel). The fields are found by name with reflection, so no assembly
    /// references are needed; a missing type or field is reported once and skipped. Robots spawned later are picked up
    /// by a re-check every half second. Gameplay prompts (the [E]/[F] hints) are not affected.
    /// Put it on any object in Main (e.g. FacilityAI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugOverlayToggle : MonoBehaviour
    {
        [Tooltip("Show the overlays when the game starts. Off: a clean screen until F1 is pressed.")]
        [SerializeField] private bool visibleOnStart = false;

        // Type (full name) and the bool fields that switch its overlay. FacilityDirectorRunner's decision log follows too.
        private static readonly (string type, string[] fields)[] Targets =
        {
            ("LabLockdown.AI.Facility.Runtime.FacilityDirectorRunner", new[] { "showOverlay", "logDecisions" }),
            ("LabLockdown.AI.Hunter.Runtime.HunterAgent", new[] { "showOverlay" }),
            ("LabLockdown.AI.Flanker.Runtime.FlankerAgent", new[] { "showOverlay" }),
            ("LabLockdown.AI.DroneStalker.Runtime.StalkerDebugView", new[] { "showPanel" }),
        };

        /// <summary>True while the debug overlays are shown.</summary>
        public static bool Visible { get; private set; }

        private readonly List<(Type type, FieldInfo[] fields)> resolved = new List<(Type, FieldInfo[])>();
        private float nextApply;

        private void Awake()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var (typeName, fieldNames) in Targets)
            {
                Type type = FindType(typeName);
                if (type == null)
                {
                    Debug.LogWarning($"[DebugOverlayToggle] Type {typeName} not found; its overlay is not toggled.", this);
                    continue;
                }

                var fields = new List<FieldInfo>();
                foreach (string fieldName in fieldNames)
                {
                    var field = type.GetField(fieldName, flags);
                    if (field != null && field.FieldType == typeof(bool)) fields.Add(field);
                    else Debug.LogWarning($"[DebugOverlayToggle] {type.Name}.{fieldName} not found; skipped.", this);
                }
                if (fields.Count > 0) resolved.Add((type, fields.ToArray()));
            }

            Visible = visibleOnStart;
        }

        private void Start() => Apply();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                Visible = !Visible;
                Apply();
            }
            else if (Time.unscaledTime >= nextApply)
            {
                Apply();   // robots spawned since the last check
            }
        }

        private void Apply()
        {
            nextApply = Time.unscaledTime + 0.5f;
            foreach (var (type, fields) in resolved)
                foreach (var target in FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    foreach (var field in fields)
                        if ((bool)field.GetValue(target) != Visible) field.SetValue(target, Visible);
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}

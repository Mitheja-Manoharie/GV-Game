using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: puts a MegaKit Prop_AccessPoint with a DoorAccessPanel beside each of the four doors the
    /// Facility AI can lock, under Environment/AccessPanels, on the wall the player sees when approaching the door.
    ///
    /// The panel faces its local +X with its pivot on the floor (the panel itself spans 1.1–1.6 m high and 1.3 m
    /// wide), so a yaw of 180 faces -X, and 90 faces -Z. It is shaped to sit on the leaning WallAstra walls, so its
    /// pivot goes 0.12 m in front of the wall line (the tile edge) at y 0.09, exactly like the control room's own
    /// access point (-0.54, 0.09, 31.88) on the z = 32 wall. Panels are not static and have no colliders: light probes
    /// light them, so no rebake is needed. Running the tool again rebuilds the group (undoable).
    /// </summary>
    public static class AccessPanelPlacer
    {
        private const string Model = "Assets/ThirdParty/Quaternius/MegaKit/FBX (Unity)/Props/Prop_AccessPoint.fbx";

        private static readonly (string door, Vector3 position, float yaw)[] Panels =
        {
            // Each 4 m WallAstra piece has a vertical strut in its middle, so a panel sits 1 m to one side of the
            // centre of the wall piece next to its door: between the strut and the seam, clear of the door frame.
            ("Door_LabSouth",       new Vector3(15.88f, 0.09f, 21f),    180f),  // lab corridor, east wall (x = 16), facing west
            ("Door_InfirmaryNorth", new Vector3(-11f, 0.09f, 19.88f),   90f),   // infirmary, north wall (z = 20) right of the door
            ("Door_ControlWest",    new Vector3(-7f, 0.09f, 31.88f),    90f),   // west corridor, north wall (z = 32)
            ("Door_ControlEast",    new Vector3(11f, 0.09f, 31.88f),    90f),   // lab link, north wall (z = 32)
        };

        [MenuItem("Tools/World Builder/Place Door Access Panels")]
        public static void Place()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                EditorUtility.DisplayDialog("Place Door Access Panels", "Open Main.unity first: no 'Environment' object found.", "OK");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (asset == null)
            {
                Debug.LogError($"[AccessPanelPlacer] Model not found: {Model}");
                return;
            }

            var old = environment.transform.Find("AccessPanels");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("AccessPanels");
            Undo.RegisterCreatedObjectUndo(root, "Place Door Access Panels");
            root.transform.SetParent(environment.transform, false);

            foreach (var (door, position, yaw) in Panels)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.scene);
                go.name = "AccessPanel_" + door.Substring("Door_".Length);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f) * asset.transform.rotation);

                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                }

                var panel = go.AddComponent<GVGame.Level.DoorAccessPanel>();
                var so = new SerializedObject(panel);
                so.FindProperty("doorId").stringValue = door;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[AccessPanelPlacer] Placed {Panels.Length} door access panels under Environment/AccessPanels.");
        }
    }
}

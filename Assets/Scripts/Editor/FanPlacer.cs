using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: makes the maintenance bay's wall fan spin (FanSpin) and adds two more spinning
    /// Prop_Fan_Small fans on corridor walls, under Environment/Fans.
    ///
    /// The new fans copy the bay fan's mounting: same height (2.1 m), 0.5 m in front of the wall line, and the bay
    /// fan's rotation turned about the vertical to face away from their own wall (the bay fan is on a west wall and
    /// faces +X). Each sits 1 m off the centre of its 4 m wall piece, clear of the piece's middle strut, like the
    /// bay fan. The new fans are not static and use light probes, so no rebake is needed.
    /// "Remove Corridor Fans" deletes them and stops the bay fan (both undoable).
    /// </summary>
    public static class FanPlacer
    {
        private const string Model = "Assets/ThirdParty/Quaternius/MegaKit/FBX (Unity)/Props/Prop_Fan_Small.fbx";
        private static readonly Vector3 BayFan = new Vector3(-3.5f, 2.1f, 11f);

        // Position and the yaw added to the bay fan's rotation (bay fan faces +X: 90 faces -Z, 180 faces -X).
        private static readonly (string name, Vector3 position, float yaw)[] Fans =
        {
            ("Fan_WestCorridor", new Vector3(-11f, 2.1f, 31.5f), 90f),    // west corridor, north wall (z = 32)
            ("Fan_Crossroads",   new Vector3(15.5f, 2.1f, 13f), 180f),    // crossroads, east wall (x = 16)
        };

        [MenuItem("Tools/World Builder/Place Corridor Fans")]
        public static void Place()
        {
            var environment = GameObject.Find("Environment");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var bay = FindBayFan(environment, asset);
            if (environment == null || asset == null || bay == null)
            {
                EditorUtility.DisplayDialog("Place Corridor Fans", "Open Main.unity first (the maintenance bay fan at (-3.5, 2.1, 11) is needed).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Place Corridor Fans");
            RemoveGroup(environment);
            if (bay.GetComponent<GVGame.Level.FanSpin>() == null) Undo.AddComponent<GVGame.Level.FanSpin>(bay);

            var root = new GameObject("Fans");
            Undo.RegisterCreatedObjectUndo(root, "Place Corridor Fans");
            root.transform.SetParent(environment.transform, false);

            foreach (var (name, position, yaw) in Fans)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.scene);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f) * bay.transform.rotation);
                go.transform.localScale = bay.transform.lossyScale;

                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                }
                go.AddComponent<GVGame.Level.FanSpin>();
            }

            EditorSceneManager.MarkSceneDirty(environment.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[FanPlacer] The bay fan spins and {Fans.Length} corridor fans were placed under Environment/Fans.");
        }

        [MenuItem("Tools/World Builder/Remove Corridor Fans")]
        public static void Remove()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null) return;
            Undo.SetCurrentGroupName("Remove Corridor Fans");
            RemoveGroup(environment);
            var bay = FindBayFan(environment, AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            var spin = bay != null ? bay.GetComponent<GVGame.Level.FanSpin>() : null;
            if (spin != null) Undo.DestroyObjectImmediate(spin);
            EditorSceneManager.MarkSceneDirty(environment.scene);
        }

        private static void RemoveGroup(GameObject environment)
        {
            var old = environment.transform.Find("Fans");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        private static GameObject FindBayFan(GameObject environment, GameObject asset)
        {
            if (environment == null || asset == null) return null;
            foreach (var t in environment.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                    PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == asset &&
                    Vector3.Distance(t.position, BayFan) < 0.5f)
                    return t.gameObject;
            return null;
        }
    }
}

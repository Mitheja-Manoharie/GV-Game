using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: places MegaKit floor decals in Main under Environment/Decals.
    ///
    ///   - Hazard stripes (red): on the approach side of the four doors the Facility AI can lock, so the player
    ///     can tell which doors may trap them.
    ///
    /// Decals are flat quads 1.4 cm above the floor (y = 0). They are not static, cast no shadows and have no
    /// colliders, so they need no lighting, occlusion or NavMesh rebake: light probes light them.
    /// Running the tool again replaces the whole Decals group (undoable).
    /// </summary>
    public static class DecalPlacer
    {
        private const string Fbx = "Assets/ThirdParty/Quaternius/MegaKit/FBX (Unity)/Decals/";
        private const string RedMaterial = "Assets/Materials/Decals/M_Decal_Red.mat";

        private const float HazardY = 0.014f;

        [MenuItem("Tools/World Builder/Place Decals")]
        public static void PlaceDecals()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                EditorUtility.DisplayDialog("Place Decals", "Open Main.unity first: no 'Environment' object found.", "OK");
                return;
            }

            var old = environment.transform.Find("Decals");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("Decals");
            Undo.RegisterCreatedObjectUndo(root, "Place Decals");
            root.transform.SetParent(environment.transform, false);

            var red = AssetDatabase.LoadAssetAtPath<Material>(RedMaterial);
            int count = 0;

            // ---- Hazard stripes on the approach side of the lockable doors (Decal_Dashes is about 1.07 x 0.33 m).
            var hazard = Group(root, "HazardStripes");
            foreach (float x in new[] { 13f, 14.1f, 15.2f })
            {
                count += Place(hazard, "Decal_Dashes", x, HazardY, 23.3f, 0f, 1f, red);        // Door_LabSouth
                count += Place(hazard, "Decal_Dashes", x - 28f, HazardY, 19.3f, 0f, 1f, red);  // Door_InfirmaryNorth
            }
            foreach (float z in new[] { 28.9f, 30f, 31.1f })
            {
                count += Place(hazard, "Decal_Dashes", -4.7f, HazardY, z, 90f, 1f, red);       // Door_ControlWest
                count += Place(hazard, "Decal_Dashes", 8.7f, HazardY, z, 90f, 1f, red);        // Door_ControlEast
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[DecalPlacer] Placed {count} decals under Environment/Decals.");
        }

        private static Transform Group(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go.transform;
        }

        private static int Place(Transform parent, string model, float x, float y, float z, float rotY,
                                 float scale = 1f, Material material = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx + model + ".fbx");
            if (asset == null)
            {
                Debug.LogWarning($"[DecalPlacer] Missing model {model}.");
                return 0;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, z);
            // Keep the importer's own root rotation and scale, then turn about the vertical.
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f) * asset.transform.rotation;
            go.transform.localScale = asset.transform.localScale * scale;

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                if (material != null) r.sharedMaterial = material;
            }
            return 1;
        }
    }
}

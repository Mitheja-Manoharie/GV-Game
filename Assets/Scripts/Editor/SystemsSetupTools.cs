#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GVGame.Interaction;
using GVGame.Dev;

namespace GVGame.EditorTools
{
    /// <summary>
    /// Systems Engineer setup window:  menu  GV Game > Systems Setup
    ///   1. Setup Prop Prefabs  – adds Rigidbody + PhysicsProp to Barricade_* / Throwable_* prefabs
    ///   2. Build Doors         – turns the Door_* marker objects in the open scene into working doors
    ///   3. Add Player Rig      – adds PlayerInteractor + pusher to the player in the open scene
    ///   4. Build Test Scene    – creates Assets/Scenes/Dev/Systems_Test.unity to demo everything
    /// </summary>
    public class SystemsSetupTools : EditorWindow
    {
        const string PropsFolder = "Assets/Prefabs/Props";
        const string MatPath = "Assets/Art/Materials/DoorPlaceholder.mat";

        float doorWidth = 2.0f;
        float doorHeight = 2.8f;
        float doorThickness = 0.15f;
        float doorOpenAngle = 100f;
        float doorAutoCloseDelay = 0f;
        GameObject doorLeafPrefab;   // optional: real door model (e.g. Quaternius Door_Simple)

        [MenuItem("GV Game/Systems Setup")]
        static void Open() => GetWindow<SystemsSetupTools>("Systems Setup");

        void OnGUI()
        {
            EditorGUILayout.LabelField("1. Physics props", EditorStyles.boldLabel);
            if (GUILayout.Button("Setup Prop Prefabs (Barricade_* / Throwable_*)")) SetupPropPrefabs();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Doors (open scene)", EditorStyles.boldLabel);
            doorWidth = EditorGUILayout.FloatField("Opening width (m)", doorWidth);
            doorHeight = EditorGUILayout.FloatField("Door height (m)", doorHeight);
            doorThickness = EditorGUILayout.FloatField("Thickness (m)", doorThickness);
            doorOpenAngle = EditorGUILayout.Slider("Open angle limit", doorOpenAngle, 10f, 140f);
            doorAutoCloseDelay = EditorGUILayout.FloatField("Auto close (s, 0 = off)", doorAutoCloseDelay);
            doorLeafPrefab = (GameObject)EditorGUILayout.ObjectField("Leaf model (optional)", doorLeafPrefab, typeof(GameObject), false);
            EditorGUILayout.HelpBox(
                "Doors are built on every object named Door_* in the open scene. The door opening is assumed to run " +
                "along the marker's local X axis; rotate the marker 90° (Y) for doors in north-south walls, then rebuild.",
                MessageType.Info);
            if (GUILayout.Button("Build Doors From Markers")) BuildDoors();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Player", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Interaction To Selected Player (needs CharacterController)")) AddPlayerRig();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("4. Test scene", EditorStyles.boldLabel);
            if (GUILayout.Button("Build Systems_Test Scene")) BuildTestScene();
        }

        // ------------------------------------------------------------------ 1. Props
        static void SetupPropPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PropsFolder });
            int count = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                bool barricade = name.StartsWith("Barricade_");
                bool throwable = name.StartsWith("Throwable_");
                if (!barricade && !throwable) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var rb = root.GetComponent<Rigidbody>();
                    if (rb == null) rb = root.AddComponent<Rigidbody>();

                    var prop = root.GetComponent<PhysicsProp>();
                    if (prop == null) prop = root.AddComponent<PhysicsProp>();
                    prop.kind = barricade ? PhysicsProp.Kind.Barricade : PhysicsProp.Kind.Throwable;
                    prop.ApplyDefaultsForKind();
                    prop.mass = MassFor(name, prop.mass);
                    prop.carveNavMesh = barricade;

                    int layer = LayerMask.NameToLayer("DynamicProps");
                    if (layer >= 0) SetLayerRecursive(root, layer);

                    // Prefabs only have a BoxCollider; make sure one exists so physics works.
                    if (root.GetComponentInChildren<Collider>() == null) root.AddComponent<BoxCollider>();

                    // Physics props must be dynamic for the lightmap bake / occlusion to ignore them.
                    GameObjectUtility.SetStaticEditorFlags(root, 0);
                    foreach (Transform t in root.GetComponentsInChildren<Transform>())
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Systems] Configured {count} prop prefabs with Rigidbody + PhysicsProp.");
        }

        static float MassFor(string name, float fallback)
        {
            if (name.Contains("Barrel")) return 25f;
            if (name.Contains("Crate")) return 45f;
            if (name.Contains("Mug")) return 0.3f;
            if (name.Contains("AmmoSmall")) return 0.6f;
            if (name.Contains("AmmoClosed")) return 1.5f;
            return fallback;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        // ------------------------------------------------------------------ 2. Doors
        void BuildDoors()
        {
            Scene scene = SceneManager.GetActiveScene();
            int built = 0;
            Material mat = GetOrCreateDoorMaterial();
            int doorsLayer = LayerMask.NameToLayer("Doors");

            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (!t.gameObject.scene.IsValid() || t.gameObject.scene != scene) continue;
                if (EditorUtility.IsPersistent(t)) continue;
                if (!t.name.StartsWith("Door_")) continue;
                if (t.GetComponentInChildren<DoorController>() != null) continue;   // already built

                BuildDoor(t, mat, doorsLayer);
                built++;
            }

            if (built > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Systems] Built {built} door(s). Doors already built were skipped.");
        }

        void BuildDoor(Transform marker, Material mat, int doorsLayer)
        {
            // Hinge pivot on the left edge of the opening.
            var hinge = new GameObject("Hinge");
            Undo.RegisterCreatedObjectUndo(hinge, "Build door");
            hinge.transform.SetParent(marker, false);
            hinge.transform.localPosition = new Vector3(-doorWidth * 0.5f, 0f, 0f);
            if (doorsLayer >= 0) hinge.layer = doorsLayer;

            var rb = hinge.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var door = hinge.AddComponent<DoorController>();
            door.openAngle = doorOpenAngle;
            door.autoCloseDelay = doorAutoCloseDelay;

            // Leaf (placeholder cube unless a model was supplied).
            GameObject leaf;
            if (doorLeafPrefab != null)
            {
                leaf = (GameObject)PrefabUtility.InstantiatePrefab(doorLeafPrefab);
                leaf.transform.SetParent(hinge.transform, false);
                leaf.transform.localPosition = new Vector3(doorWidth * 0.5f, 0f, 0f);
                if (leaf.GetComponentInChildren<Collider>() == null)
                {
                    var bc = leaf.AddComponent<BoxCollider>();   // fallback collider
                    bc.center = new Vector3(0f, doorHeight * 0.5f, 0f);
                    bc.size = new Vector3(doorWidth, doorHeight, doorThickness);
                }
            }
            else
            {
                leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leaf.name = "Leaf";
                leaf.transform.SetParent(hinge.transform, false);
                leaf.transform.localPosition = new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, 0f);
                leaf.transform.localScale = new Vector3(doorWidth, doorHeight, doorThickness);
                leaf.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Undo.RegisterCreatedObjectUndo(leaf, "Build door");
            if (doorsLayer >= 0) SetLayerRecursive(leaf, doorsLayer);
            foreach (Transform c in hinge.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0);   // doors are dynamic

            // Trigger volume so AI agents open doors automatically.
            var trig = new GameObject("AutoOpenTrigger");
            Undo.RegisterCreatedObjectUndo(trig, "Build door");
            trig.transform.SetParent(marker, false);
            trig.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(doorWidth + 1.0f, 2.5f, 3.0f);
            var auto = trig.AddComponent<DoorAutoOpenTrigger>();
            auto.door = door;
            trig.layer = 2; // Ignore Raycast: the player's interaction ray must not hit the trigger
        }

        static Material GetOrCreateDoorMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null) return mat;
            Directory.CreateDirectory(Path.GetDirectoryName(MatPath));
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh != null ? sh : Shader.Find("Standard")) { name = "DoorPlaceholder" };
            mat.SetColor("_BaseColor", new Color(0.35f, 0.38f, 0.42f));
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Smoothness", 0.5f);
            AssetDatabase.CreateAsset(mat, MatPath);
            return mat;
        }

        // ------------------------------------------------------------------ 3. Player
        static void AddPlayerRig()
        {
            GameObject sel = Selection.activeGameObject;
            if (sel == null || sel.GetComponent<CharacterController>() == null)
            {
                EditorUtility.DisplayDialog("Systems Setup",
                    "Select the player GameObject (it needs a CharacterController) first.", "OK");
                return;
            }
            Undo.AddComponent<PlayerRigidbodyPusher>(sel);
            var interactor = sel.GetComponent<PlayerInteractor>() ?? Undo.AddComponent<PlayerInteractor>(sel);
            interactor.playerColliders = sel.GetComponentsInChildren<Collider>();
            interactor.viewCamera = sel.GetComponentInChildren<Camera>();
            EditorUtility.SetDirty(sel);
            Debug.Log("[Systems] PlayerInteractor and PlayerRigidbodyPusher added to " + sel.name);
        }

        // ------------------------------------------------------------------ 4. Test scene
        void BuildTestScene()
        {
            SetupPropPrefabs();   // make sure prefabs are physics-ready first

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Material floorMat = GetOrCreateDoorMaterial();

            // Floor and walls: a 12 x 20 room split by a wall with a door gap.
            MakeBox("Floor", new Vector3(0, -0.25f, 8), new Vector3(14, 0.5f, 26), floorMat);
            MakeBox("Wall_N", new Vector3(0, 1.5f, 21), new Vector3(14, 3, 0.4f), floorMat);
            MakeBox("Wall_S", new Vector3(0, 1.5f, -5), new Vector3(14, 3, 0.4f), floorMat);
            MakeBox("Wall_W", new Vector3(-7, 1.5f, 8), new Vector3(0.4f, 3, 26), floorMat);
            MakeBox("Wall_E", new Vector3(7, 1.5f, 8), new Vector3(0.4f, 3, 26), floorMat);
            // Divider wall at z = 8 with a 2 m opening centred on x = 0
            MakeBox("Divider_L", new Vector3(-4.0f, 1.5f, 8), new Vector3(6.0f, 3, 0.4f), floorMat);
            MakeBox("Divider_R", new Vector3(4.0f, 1.5f, 8), new Vector3(6.0f, 3, 0.4f), floorMat);
            MakeBox("Divider_Top", new Vector3(0f, 2.9f, 8), new Vector3(2.0f, 0.2f, 0.4f), floorMat);

            // Door marker (named Door_* so the normal builder handles it)
            var doorMarker = new GameObject("Door_TestDivider");
            doorMarker.transform.position = new Vector3(0, 0, 8);
            BuildDoor(doorMarker.transform, floorMat, LayerMask.NameToLayer("Doors"));

            // Player rig
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0, 1.1f, 0);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0, 0);
            var camRoot = new GameObject("CameraRoot");
            camRoot.transform.SetParent(player.transform, false);
            camRoot.transform.localPosition = new Vector3(0, 0.7f, 0);
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.transform.SetParent(camRoot.transform, false);
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            var tp = player.AddComponent<SystemsTestPlayer>();
            tp.cameraRoot = camRoot.transform;
            player.AddComponent<PlayerRigidbodyPusher>();
            var inter = player.AddComponent<PlayerInteractor>();
            inter.viewCamera = cam.GetComponent<Camera>();
            inter.playerColliders = new Collider[] { cc };

            // Props from the project's own prefabs
            SpawnPrefabs("Barricade_Barrel", 3, new Vector3(-3, 0, 3), new Vector3(1.4f, 0, 0));
            SpawnPrefabs("Barricade_CrateTarp", 2, new Vector3(3, 0, 3), new Vector3(0, 0, 2.2f));
            SpawnPrefabs("Throwable_Mug", 4, new Vector3(-1.5f, 1.0f, 4.5f), new Vector3(0.5f, 0, 0));
            SpawnPrefabs("Throwable_AmmoSmall", 3, new Vector3(1.0f, 1.0f, 4.5f), new Vector3(0.6f, 0, 0));
            SpawnPrefabs("Throwable_AmmoClosed", 3, new Vector3(-1.5f, 1.0f, 6f), new Vector3(0.6f, 0, 0));
            // A table surface for the throwables to sit on
            MakeBox("Table", new Vector3(-0.3f, 0.45f, 5.2f), new Vector3(3.6f, 0.1f, 2.4f), floorMat);

            Directory.CreateDirectory("Assets/Scenes/Dev");
            string scenePath = "Assets/Scenes/Dev/Systems_Test.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[Systems] Test scene saved to " + scenePath);
        }

        static GameObject MakeBox(string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void SpawnPrefabs(string prefabName, int count, Vector3 start, Vector3 step)
        {
            string[] guids = AssetDatabase.FindAssets(prefabName + " t:Prefab", new[] { PropsFolder });
            if (guids.Length == 0) { Debug.LogWarning("[Systems] Prefab not found: " + prefabName); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            for (int i = 0; i < count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = start + step * i;
            }
        }
    }
}
#endif

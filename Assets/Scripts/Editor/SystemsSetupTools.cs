#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using GVGame.Combat;
using GVGame.Interaction;
using GVGame.Player;

namespace GVGame.EditorTools
{
    /// <summary>
    /// Systems Engineer setup window:  menu  GV Game > Systems Setup
    ///   1. Setup Prop Prefabs   - Rigidbody + PhysicsProp (+ NavMeshObstacle on barricades) on Barricade_* / Throwable_*,
    ///                              trigger + HealthPickup on Pickup_HealthPack
    ///   2. Build Doors          - turns every Door_* marker in the open scene into a two-panel working door
    ///   3. Create Player Prefab - Assets/Prefabs/Player/Player.prefab (controller, health, weapon, HUD, interaction)
    ///   4. Build Systems_Test   - standalone scene that exercises everything
    /// </summary>
    public class SystemsSetupTools : EditorWindow
    {
        const string PropsFolder = "Assets/Prefabs/Props";
        const string MatFolder = "Assets/Art/Materials";
        const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

        // Door dimensions from issue #11 (opening 4 m, two Door_DarkMetal panels of 2.1 x 4.05 x 0.2 m).
        float openingWidth = 4f;
        float panelWidth = 2.1f;
        float panelHeight = 4.05f;
        float panelThickness = 0.2f;
        float floorClearance = 0.02f;
        float openAngle = 100f;
        float autoCloseDelay = 0f;
        bool blockNavMeshWhenClosed = false;
        GameObject panelPrefab;

        [MenuItem("GV Game/Systems Setup")]
        static void Open() => GetWindow<SystemsSetupTools>("Systems Setup");

        void OnEnable()
        {
            if (panelPrefab == null)
            {
                foreach (string guid in AssetDatabase.FindAssets("Door_DarkMetal t:GameObject"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(p) == "Door_DarkMetal")
                    {
                        panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        break;
                    }
                }
            }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("1. Props", EditorStyles.boldLabel);
            if (GUILayout.Button("Setup Prop Prefabs (barricades, throwables, health pack)")) SetupPropPrefabs();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Doors (open scene)", EditorStyles.boldLabel);
            openingWidth = EditorGUILayout.FloatField("Opening width (m)", openingWidth);
            panelWidth = EditorGUILayout.FloatField("Panel width (m)", panelWidth);
            panelHeight = EditorGUILayout.FloatField("Panel height (m)", panelHeight);
            panelThickness = EditorGUILayout.FloatField("Panel thickness (m)", panelThickness);
            openAngle = EditorGUILayout.Slider("Open angle limit", openAngle, 10f, 140f);
            autoCloseDelay = EditorGUILayout.FloatField("Auto close (s, 0 = off)", autoCloseDelay);
            blockNavMeshWhenClosed = EditorGUILayout.Toggle("Closed doors carve NavMesh", blockNavMeshWhenClosed);
            panelPrefab = (GameObject)EditorGUILayout.ObjectField("Panel model", panelPrefab, typeof(GameObject), false);
            EditorGUILayout.HelpBox(
                "Builds two hinged panels (hinges at the outer edges of the opening) on every empty Door_* marker (door frame models are skipped). " +
                "The marker's rotation decides the direction: 0 = door spans east-west, 90 = north-south. " +
                "No panel model = grey placeholder boxes.", MessageType.Info);
            if (GUILayout.Button("Build Doors From Markers")) BuildDoors();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Player", EditorStyles.boldLabel);
            if (GUILayout.Button("Create Player Prefab")) CreatePlayerPrefab();
            if (GUILayout.Button("Add Interaction To Selected Player (custom player)")) AddInteractionToSelected();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("4. Test scene", EditorStyles.boldLabel);
            if (GUILayout.Button("Build Systems_Test Scene")) BuildTestScene();
        }

        // ------------------------------------------------------------------ 1. Props
        static void SetupPropPrefabs()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                bool barricade = name.StartsWith("Barricade_");
                bool throwable = name.StartsWith("Throwable_");
                bool healthPack = name.StartsWith("Pickup_HealthPack");
                if (!barricade && !throwable && !healthPack) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (healthPack) SetupHealthPack(root);
                    else SetupPhysicsProp(root, name, barricade);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Systems] Configured {count} prop prefabs.");
        }

        static void SetupPhysicsProp(GameObject root, string name, bool barricade)
        {
            if (root.GetComponent<Rigidbody>() == null) root.AddComponent<Rigidbody>();
            var prop = root.GetComponent<PhysicsProp>();
            if (prop == null) prop = root.AddComponent<PhysicsProp>();
            prop.kind = barricade ? PhysicsProp.Kind.Barricade : PhysicsProp.Kind.Throwable;
            prop.ApplyDefaultsForKind();
            prop.mass = MassFor(name, prop.mass);
            prop.carveNavMesh = barricade;

            int layer = LayerMask.NameToLayer("DynamicProps");
            if (layer >= 0) SetLayerRecursive(root, layer);
            if (root.GetComponentInChildren<Collider>() == null) root.AddComponent<BoxCollider>();

            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // physics props are dynamic

            if (barricade)
            {
                // Serialized on the prefab so it is visible in the Inspector (issue #17).
                var ob = root.GetComponent<NavMeshObstacle>();
                if (ob == null) ob = root.AddComponent<NavMeshObstacle>();
                var box = root.GetComponent<BoxCollider>();
                ob.shape = NavMeshObstacleShape.Box;
                if (box != null) { ob.center = box.center; ob.size = box.size; }
                ob.carving = true;
                ob.carveOnlyStationary = true;
                ob.carvingMoveThreshold = 0.1f;
                ob.carvingTimeToStationary = 0.5f;
            }
        }

        static void SetupHealthPack(GameObject root)
        {
            Collider trigger = null;
            foreach (Collider c in root.GetComponentsInChildren<Collider>()) if (c.isTrigger) trigger = c;
            if (trigger == null)
            {
                var sc = root.AddComponent<SphereCollider>();
                sc.isTrigger = true;
                sc.radius = 0.8f;
                sc.center = new Vector3(0f, 0.3f, 0f);
            }
            if (root.GetComponent<HealthPickup>() == null) root.AddComponent<HealthPickup>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // it spins and bobs
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
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (EditorUtility.IsPersistent(t)) continue;
                if (t.gameObject.scene != scene) continue;
                if (!t.name.StartsWith("Door_")) continue;
                if (t.name.Contains("Frame")) continue;                              // door FRAME models, not markers
                if (t.GetComponentInChildren<Renderer>() != null) continue;          // markers are empty objects
                if (t.GetComponentInChildren<DoorController>() != null) continue;   // already built
                BuildDoor(t);
                built++;
            }
            if (built > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Systems] Built {built} door(s). Doors that already had a DoorController were skipped.");
        }

        void BuildDoor(Transform marker)
        {
            int doorsLayer = LayerMask.NameToLayer("Doors");

            var door = marker.gameObject.AddComponent<DoorController>();
            Undo.RegisterCreatedObjectUndo(door, "Build door");
            door.openAngle = openAngle;
            door.autoCloseDelay = autoCloseDelay;
            door.blockNavMeshWhenClosed = blockNavMeshWhenClosed;

            BuildLeaf(marker, "Hinge_L", new Vector3(-openingWidth * 0.5f, 0f, 0f), Quaternion.identity, doorsLayer);
            BuildLeaf(marker, "Hinge_R", new Vector3(openingWidth * 0.5f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f), doorsLayer);

            // Trigger in front of the door so AI agents can open it.
            var trig = new GameObject("AutoOpenTrigger");
            Undo.RegisterCreatedObjectUndo(trig, "Build door");
            trig.transform.SetParent(marker, false);
            trig.transform.localPosition = new Vector3(0f, panelHeight * 0.5f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(openingWidth + 1f, panelHeight, 3.5f);
            var auto = trig.AddComponent<DoorAutoOpenTrigger>();
            auto.door = door;
            trig.layer = 2;   // Ignore Raycast, so the player's interaction ray never hits it
        }

        void BuildLeaf(Transform marker, string name, Vector3 localPos, Quaternion localRot, int doorsLayer)
        {
            var hinge = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(hinge, "Build door");
            hinge.transform.SetParent(marker, false);
            hinge.transform.localPosition = localPos;
            hinge.transform.localRotation = localRot;
            if (doorsLayer >= 0) hinge.layer = doorsLayer;

            var rb = hinge.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            // Collider on the hinge object: panel extends along the hinge's local +X.
            var bc = hinge.AddComponent<BoxCollider>();
            bc.size = new Vector3(panelWidth, panelHeight - floorClearance, panelThickness);
            bc.center = new Vector3(panelWidth * 0.5f, floorClearance + (panelHeight - floorClearance) * 0.5f, 0f);

            GameObject visual;
            if (panelPrefab != null)
            {
                visual = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab);
                visual.transform.SetParent(hinge.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                foreach (Collider c in visual.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "PanelPlaceholder";
                DestroyImmediate(visual.GetComponent<Collider>());
                visual.transform.SetParent(hinge.transform, false);
                visual.transform.localPosition = new Vector3(panelWidth * 0.5f, panelHeight * 0.5f, 0f);
                visual.transform.localScale = new Vector3(panelWidth, panelHeight, panelThickness);
                visual.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("DoorPlaceholder", new Color(0.35f, 0.38f, 0.42f), 0.6f);
            }
            Undo.RegisterCreatedObjectUndo(visual, "Build door");

            foreach (Transform t in hinge.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // doors are dynamic
                if (doorsLayer >= 0) t.gameObject.layer = doorsLayer;
            }
        }

        static Material GetOrCreateMaterial(string name, Color color, float metallic)
        {
            string path = MatFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(MatFolder);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh != null ? sh : Shader.Find("Standard")) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material GetOrCreateTracerMaterial()
        {
            string path = MatFolder + "/Tracer.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(MatFolder);
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return null;
            mat = new Material(sh) { name = "Tracer" };
            mat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.55f));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------------ 3. Player
        static GameObject CreatePlayerPrefab()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefabPath));

            var go = new GameObject("Player");
            go.tag = "Player";
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f; cc.skinWidth = 0.03f;

            var camRoot = new GameObject("CameraRoot");
            camRoot.transform.SetParent(go.transform, false);
            camRoot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(camRoot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            camGo.AddComponent<AudioListener>();

            var pc = go.AddComponent<PlayerController>();
            pc.cameraRoot = camRoot.transform;
            go.AddComponent<PlayerHealth>();

            var interactor = go.AddComponent<PlayerInteractor>();
            interactor.viewCamera = cam;
            interactor.playerColliders = new Collider[] { cc };
            go.AddComponent<PlayerRigidbodyPusher>();

            // Placeholder gun model.
            var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "GunViewModel";
            UnityEngine.Object.DestroyImmediate(gun.GetComponent<Collider>());
            gun.transform.SetParent(camGo.transform, false);
            gun.transform.localPosition = new Vector3(0.22f, -0.2f, 0.45f);
            gun.transform.localScale = new Vector3(0.07f, 0.1f, 0.4f);
            gun.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("GunPlaceholder", new Color(0.12f, 0.12f, 0.14f), 0.4f);

            var weapon = go.AddComponent<WeaponController>();
            weapon.aimCamera = cam;
            weapon.viewModel = gun.transform;
            weapon.tracerMaterial = GetOrCreateTracerMaterial();

            go.AddComponent<PlayerHud>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("[Systems] Player prefab saved to " + PlayerPrefabPath);
            return prefab;
        }

        static void AddInteractionToSelected()
        {
            GameObject sel = Selection.activeGameObject;
            if (sel == null || sel.GetComponent<CharacterController>() == null)
            {
                EditorUtility.DisplayDialog("Systems Setup", "Select a player GameObject with a CharacterController first.", "OK");
                return;
            }
            if (sel.GetComponent<PlayerRigidbodyPusher>() == null) Undo.AddComponent<PlayerRigidbodyPusher>(sel);
            var interactor = sel.GetComponent<PlayerInteractor>();
            if (interactor == null) interactor = Undo.AddComponent<PlayerInteractor>(sel);
            interactor.playerColliders = sel.GetComponentsInChildren<Collider>();
            interactor.viewCamera = sel.GetComponentInChildren<Camera>();
            EditorUtility.SetDirty(sel);
        }

        // ------------------------------------------------------------------ 4. Test scene
        void BuildTestScene()
        {
            SetupPropPrefabs();
            GameObject playerPrefab = CreatePlayerPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Material wall = GetOrCreateMaterial("DoorPlaceholder", new Color(0.35f, 0.38f, 0.42f), 0.6f);

            // Room x -9..9, z -6..28, 5 m walls; divider at z = 10 with a 4 m door opening (x -2..2).
            MakeBox("Floor", new Vector3(0, -0.25f, 11), new Vector3(20, 0.5f, 36), wall);
            MakeBox("Wall_N", new Vector3(0, 2.5f, 28), new Vector3(20, 5, 0.4f), wall);
            MakeBox("Wall_S", new Vector3(0, 2.5f, -6), new Vector3(20, 5, 0.4f), wall);
            MakeBox("Wall_W", new Vector3(-9.8f, 2.5f, 11), new Vector3(0.4f, 5, 36), wall);
            MakeBox("Wall_E", new Vector3(9.8f, 2.5f, 11), new Vector3(0.4f, 5, 36), wall);
            MakeBox("Divider_L", new Vector3(-5.5f, 2.5f, 10), new Vector3(7f, 5, 0.4f), wall);
            MakeBox("Divider_R", new Vector3(5.5f, 2.5f, 10), new Vector3(7f, 5, 0.4f), wall);
            MakeBox("Divider_Top", new Vector3(0, 4.55f, 10), new Vector3(4f, 0.9f, 0.4f), wall);

            var marker = new GameObject("Door_TestDivider");
            marker.transform.position = new Vector3(0, 0, 10);
            BuildDoor(marker.transform);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.position = new Vector3(0, 0, 0);

            SpawnPrefabs("Barricade_Barrel", 3, new Vector3(-6, 0, 3), new Vector3(1.4f, 0, 0));
            SpawnPrefabs("Barricade_CrateTarp", 2, new Vector3(6, 0, 3), new Vector3(0, 0, 2.5f));
            SpawnPrefabs("Barricade_Barrel", 1, new Vector3(-1.0f, 0, 11.4f), Vector3.zero);   // inside the door's swing zone
            SpawnPrefabs("Throwable_Mug", 4, new Vector3(-3.5f, 1.0f, 4.5f), new Vector3(0.5f, 0, 0));
            SpawnPrefabs("Throwable_AmmoSmall", 3, new Vector3(-3.5f, 1.0f, 5.3f), new Vector3(0.6f, 0, 0));
            SpawnPrefabs("Throwable_AmmoClosed", 3, new Vector3(-0.8f, 1.0f, 4.5f), new Vector3(0.6f, 0, 0));
            MakeBox("Table", new Vector3(-2.2f, 0.45f, 4.9f), new Vector3(4.4f, 0.1f, 2.0f), wall);
            SpawnPrefabs("Pickup_HealthPack", 1, new Vector3(3f, 0.25f, 3f), Vector3.zero);

            // Training dummies behind the door.
            for (int i = 0; i < 3; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                d.name = "TrainingDummy_" + (i + 1);
                d.transform.position = new Vector3(-4f + i * 4f, 1f, 22f);
                d.AddComponent<TrainingDummy>();
            }

            Directory.CreateDirectory("Assets/Scenes/Dev");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Dev/Systems_Test.unity");
            Debug.Log("[Systems] Test scene saved to Assets/Scenes/Dev/Systems_Test.unity");
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

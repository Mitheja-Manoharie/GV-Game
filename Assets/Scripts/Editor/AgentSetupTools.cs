#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GVGame.Agents;
using LabLockdown.AI.Facility.Runtime;
using LabLockdown.AI.Flanker.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.EditorTools
{
    /// <summary>
    /// Agent Controller setup:  menu  GV Game > Agent Setup
    ///   1. Configure Android Rig      - Humanoid avatar with the Core Developer's bone mapping (DEF-* Rigify bones)
    ///   2. Configure Robot Clips      - Mixamo clips in Assets/Art/Animations/Robot: Humanoid, in place, looping where needed
    ///   3. Build Robot Animator       - Robot.controller: idle/walk/run blend on Speed, Shoot, Hit, Death
    ///   4. Build Robot Prefabs        - Robot_Base (body: agent, collider, health, gun, follower, animator)
    ///                                   and Robot_Flanker (base + Tactical Flanker brain)
    ///   5. Add Robot Spawner To Scene - RobotSpawner under FacilityAI, assigned as the Director's Spawn Service
    /// Every step can be run again; it rebuilds what it made.
    /// </summary>
    public static class AgentSetupTools
    {
        const string AndroidModelPath = "Assets/Models/Characters/Android/Android.fbx";
        const string ClipsFolder = "Assets/Art/Animations/Robot";
        const string ControllerPath = ClipsFolder + "/Robot.controller";
        const string PrefabFolder = "Assets/Prefabs/Robots";
        const string BasePrefabPath = PrefabFolder + "/Robot_Base.prefab";
        const string FlankerPrefabPath = PrefabFolder + "/Robot_Flanker.prefab";
        const string HunterPrefabPath = PrefabFolder + "/Robot_Hunter.prefab";
        const string RiflePath = "Assets/ThirdParty/Quaternius/Essentials/FBX (Unity)/Gun_Rifle.fbx";
        const string TracerMaterialPath = "Assets/Art/Materials/Tracer.mat";
        const string RightHandBone = "DEF-hand.R";

        // Animation slots, in matching order (Death before Hit, so "Hit Death" style names land on Death).
        static readonly (string slot, string[] keywords, bool loop)[] Slots =
        {
            ("Death", new[] { "death", "dying", "die" }, false),
            ("Hit", new[] { "hit", "react" }, false),
            ("Shoot", new[] { "fir", "shoot" }, false),
            ("Run", new[] { "run" }, true),
            ("Walk", new[] { "walk" }, true),
            ("Idle", new[] { "idle" }, true),
        };

        // ---------- 1. Rig ----------

        /// <summary>Unity humanoid bone → bone in the Android/Alien rig (from the Core Developer). Right side mirrors with .R.</summary>
        static IEnumerable<(string human, string bone)> BoneMapping()
        {
            yield return ("Hips", "DEF-spine");
            yield return ("Spine", "DEF-spine.001");
            yield return ("Chest", "DEF-spine.002");
            yield return ("UpperChest", "DEF-spine.003");
            yield return ("Neck", "DEF-spine.004");
            yield return ("Head", "DEF-spine.006");

            foreach (var (side, s) in new[] { ("Left", "L"), ("Right", "R") })
            {
                yield return ($"{side}Shoulder", $"DEF-shoulder.{s}");
                yield return ($"{side}UpperArm", $"DEF-upper_arm.{s}");
                yield return ($"{side}LowerArm", $"DEF-forearm.{s}");
                yield return ($"{side}Hand", $"DEF-hand.{s}");
                yield return ($"{side}UpperLeg", $"DEF-thigh.{s}");
                yield return ($"{side}LowerLeg", $"DEF-shin.{s}");
                yield return ($"{side}Foot", $"DEF-foot.{s}");
                yield return ($"{side}Toes", $"DEF-toe.{s}");

                var phalanges = new[] { ("Proximal", "01"), ("Intermediate", "02"), ("Distal", "03") };
                foreach (var (part, n) in phalanges)
                {
                    yield return ($"{side} Thumb {part}", $"DEF-thumb.{n}.{s}");
                    yield return ($"{side} Index {part}", $"DEF-f_index.{n}.{s}");
                    yield return ($"{side} Middle {part}", $"DEF-f_middle.{n}.{s}");
                    yield return ($"{side} Ring {part}", $"DEF-f_ring.{n}.{s}");
                    yield return ($"{side} Little {part}", $"DEF-f_pinky.{n}.{s}");
                }
            }
        }

        [MenuItem("GV Game/Agent Setup/1. Configure Android Rig (Humanoid)")]
        static void ConfigureAndroidRig() => ConfigureHumanoidRig(AndroidModelPath);

        /// <summary>
        /// Sets the model to Humanoid with an explicit bone mapping. Unity's automatic mapping can pick the Rigify
        /// twist bones (DEF-upper_arm.L.001 …) instead of the real ones, so every bone is assigned by name.
        /// </summary>
        static void ConfigureHumanoidRig(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (importer == null || model == null)
            {
                Debug.LogError($"[Agent Setup] No model at {modelPath}.");
                return;
            }

            var transforms = model.GetComponentsInChildren<Transform>(true);
            var boneNames = new HashSet<string>(transforms.Select(t => t.name));
            var humanNames = new HashSet<string>(HumanTrait.BoneName);

            var human = new List<HumanBone>();
            foreach (var (humanName, boneName) in BoneMapping())
            {
                if (!humanNames.Contains(humanName)) { Debug.LogError($"[Agent Setup] '{humanName}' is not a Unity humanoid bone."); continue; }
                if (!boneNames.Contains(boneName)) { Debug.LogWarning($"[Agent Setup] {model.name} has no bone '{boneName}' for {humanName}; left unmapped."); continue; }

                var bone = new HumanBone { humanName = humanName, boneName = boneName };
                bone.limit.useDefaultValues = true;
                human.Add(bone);
            }

            var skeleton = transforms.Select(t => new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            }).ToArray();

            var description = importer.humanDescription;
            description.human = human.ToArray();
            description.skeleton = skeleton;
            description.upperArmTwist = 0.5f;
            description.lowerArmTwist = 0.5f;
            description.upperLegTwist = 0.5f;
            description.lowerLegTwist = 0.5f;
            description.armStretch = 0.05f;
            description.legStretch = 0.05f;
            description.feetSpacing = 0f;
            description.hasTranslationDoF = false;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = description;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar != null && avatar.isValid && avatar.isHuman)
                Debug.Log($"[Agent Setup] {model.name}: Humanoid avatar OK ({human.Count} bones mapped). " +
                          "Open Rig > Configure to check every bone is green; if the model is not in a T-pose, use Pose > Enforce T-Pose.");
            else
                Debug.LogError($"[Agent Setup] {model.name}: the Humanoid avatar is not valid. Open Rig > Configure to see which bones are red.");
        }

        // ---------- 2. Clips ----------

        [MenuItem("GV Game/Agent Setup/2. Configure Robot Clips")]
        static void ConfigureClips()
        {
            EnsureFolder(ClipsFolder);
            var files = Directory.GetFiles(ClipsFolder, "*.fbx").Select(p => p.Replace('\\', '/')).ToList();
            if (files.Count == 0)
            {
                Debug.LogWarning($"[Agent Setup] Put the Mixamo clips (FBX for Unity, Without Skin) in {ClipsFolder}: " +
                                 "idle, walk, run, firing, hit reaction and death. Then run this again.");
                return;
            }

            foreach (var path in files)
            {
                var slot = SlotFor(Path.GetFileNameWithoutExtension(path));
                if (slot == null)
                {
                    Debug.LogWarning($"[Agent Setup] {Path.GetFileName(path)} does not match idle/walk/run/fire/hit/death; skipped.");
                    continue;
                }

                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.SaveAndReimport(); // creates the avatar and the default take

                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.name = slot.Value.slot;
                    clip.loopTime = slot.Value.loop;
                    // In place: the NavMeshAgent moves the robot, so the clip must not drift or turn the body.
                    clip.lockRootRotation = true;
                    clip.keepOriginalOrientation = true;
                    clip.lockRootHeightY = true;
                    clip.keepOriginalPositionY = true;
                    clip.lockRootPositionXZ = true;
                    clip.keepOriginalPositionXZ = true;
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                Debug.Log($"[Agent Setup] {Path.GetFileName(path)} → {slot.Value.slot}{(slot.Value.loop ? " (loop)" : string.Empty)}");
            }
        }

        static (string slot, string[] keywords, bool loop)? SlotFor(string fileName)
        {
            string name = fileName.ToLowerInvariant();
            foreach (var slot in Slots)
                if (slot.keywords.Any(name.Contains)) return slot;
            return null;
        }

        static AnimationClip FindClip(string slot)
        {
            if (!AssetDatabase.IsValidFolder(ClipsFolder)) return null;
            foreach (var path in Directory.GetFiles(ClipsFolder, "*.fbx"))
            {
                var assetPath = path.Replace('\\', '/');
                var match = SlotFor(Path.GetFileNameWithoutExtension(assetPath));
                if (match == null || match.Value.slot != slot) continue;

                var clip = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip != null) return clip;
            }

            return null;
        }

        // ---------- 3. Animator ----------

        [MenuItem("GV Game/Agent Setup/3. Build Robot Animator")]
        static void BuildAnimator()
        {
            var idle = FindClip("Idle");
            var walk = FindClip("Walk");
            var run = FindClip("Run");
            if (idle == null || walk == null || run == null)
            {
                Debug.LogError("[Agent Setup] Idle, Walk and Run clips are required. Run step 2 first.");
                return;
            }

            EnsureFolder(ClipsFolder);
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            var root = controller.layers[0].stateMachine;

            // Thresholds are the robot speeds: Patrol 1.5 m/s walks, Chase 3 m/s runs, so the feet match the ground.
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.5f);
            tree.AddChild(run, 3f);
            root.defaultState = locomotion;

            var shootClip = FindClip("Shoot");
            if (shootClip != null)
            {
                var shoot = root.AddState("Shoot");
                shoot.motion = shootClip;
                var toShoot = locomotion.AddTransition(shoot);
                toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
                toShoot.hasExitTime = false;
                toShoot.duration = 0.05f;
                var back = shoot.AddTransition(locomotion);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.1f;
            }

            var hitClip = FindClip("Hit");
            if (hitClip != null)
            {
                var hit = root.AddState("Hit");
                hit.motion = hitClip;
                var toHit = root.AddAnyStateTransition(hit);
                toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                toHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
                toHit.hasExitTime = false;
                toHit.duration = 0.05f;
                toHit.canTransitionToSelf = false;
                var back = hit.AddTransition(locomotion);
                back.hasExitTime = true;
                back.exitTime = 0.85f;
                back.duration = 0.1f;
            }

            var deathClip = FindClip("Death");
            if (deathClip != null)
            {
                var death = root.AddState("Death");
                death.motion = deathClip;
                var toDeath = root.AddAnyStateTransition(death);
                toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
                toDeath.hasExitTime = false;
                toDeath.duration = 0.1f;
                toDeath.canTransitionToSelf = false;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Agent Setup] {ControllerPath} built" +
                      $"{(shootClip == null ? " (no Shoot clip)" : "")}{(hitClip == null ? " (no Hit clip)" : "")}{(deathClip == null ? " (no Death clip)" : "")}.");
        }

        // ---------- 4. Prefabs ----------

        [MenuItem("GV Game/Agent Setup/4. Build Robot Prefabs")]
        static void BuildPrefabs()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AndroidModelPath);
            if (model == null)
            {
                Debug.LogError($"[Agent Setup] No model at {AndroidModelPath}.");
                return;
            }

            if (File.Exists(BasePrefabPath) && !EditorUtility.DisplayDialog("Rebuild robot prefabs?",
                    "Robot_Base and Robot_Flanker will be rebuilt. Manual changes to them (e.g. gun position) are lost.", "Rebuild", "Cancel"))
                return;

            EnsureFolder(PrefabFolder);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null) Debug.LogWarning("[Agent Setup] No Robot.controller yet (step 3); the robot will not animate.");

            var root = new GameObject("Robot_Base");
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                body.name = "Model";
                body.transform.SetParent(root.transform, false);

                // Not "??": a missing component can be Unity's fake null in the editor.
                var animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AndroidModelPath).OfType<Avatar>().FirstOrDefault();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                float height = MeasureHeight(body);

                // Solid, not a trigger: the rifle's raycasts and the Director's spawn/doorway overlaps ignore triggers.
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.isTrigger = false;
                capsule.height = height;
                capsule.radius = 0.35f;
                capsule.center = new Vector3(0f, height * 0.5f, 0f);

                // Kinematic: moved by the agent, but still fires triggers (DoorAutoOpenTrigger).
                var rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.speed = 3f;
                agent.acceleration = 12f;
                agent.angularSpeed = 720f;
                agent.stoppingDistance = 0f;
                agent.autoBraking = false;
                agent.radius = 0.5f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

                root.AddComponent<RobotHealth>();
                var gun = root.AddComponent<RobotGun>();
                gun.blockers = LayerMask.GetMask("Default", "Doors");
                root.AddComponent<RobotPathFollower>();
                var robotAnimator = root.AddComponent<RobotAnimator>();
                robotAnimator.animator = animator;

                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = new Vector3(0.25f, height * 0.7f, 0.6f);
                gun.muzzle = muzzle;

                var tracer = muzzle.gameObject.AddComponent<LineRenderer>();
                tracer.widthMultiplier = 0.03f;
                tracer.positionCount = 2;
                tracer.useWorldSpace = true;
                tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tracer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(TracerMaterialPath);
                tracer.enabled = false;
                gun.tracer = tracer;

                AttachRifle(body);

                PrefabUtility.SaveAsPrefabAsset(root, BasePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            // Flanker = a variant of the base with the Tactical Flanker brain, so body changes reach every robot type.
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            var flanker = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                flanker.name = "Robot_Flanker";
                flanker.AddComponent<FlankerAgent>();
                PrefabUtility.SaveAsPrefabAsset(flanker, FlankerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(flanker);
            }

            Debug.Log($"[Agent Setup] Built {BasePrefabPath} and {FlankerPrefabPath}. For the Security Hunter, make a prefab variant " +
                      $"of Robot_Base named Robot_Hunter with the Hunter brain. Check the rifle's position in the right hand.");
        }

        static float MeasureHeight(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 2f;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float height = bounds.max.y - model.transform.position.y;
            if (height < 1f || height > 3f)
            {
                Debug.LogWarning($"[Agent Setup] The model is {height:0.00} m tall; expected about 2 m. Check its import scale.");
                return Mathf.Clamp(height, 1f, 3f);
            }

            return height;
        }

        static void AttachRifle(GameObject body)
        {
            var hand = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == RightHandBone);
            var rifle = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
            if (hand == null || rifle == null)
            {
                Debug.LogWarning($"[Agent Setup] Rifle not attached (bone {RightHandBone} or {RiflePath} missing).");
                return;
            }

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(rifle);
            gun.name = "Rifle";
            gun.transform.SetParent(hand, false);
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            foreach (var c in gun.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        // ---------- 5. Scene ----------

        [MenuItem("GV Game/Agent Setup/5. Add Robot Spawner To Scene")]
        static void AddSpawner()
        {
            var runner = Object.FindAnyObjectByType<FacilityDirectorRunner>(FindObjectsInactive.Include);
            if (runner == null)
            {
                Debug.LogError("[Agent Setup] No FacilityDirectorRunner in the open scene.");
                return;
            }

            var spawner = runner.GetComponentInChildren<RobotSpawner>(true);
            if (spawner == null)
            {
                var go = new GameObject("RobotSpawner");
                Undo.RegisterCreatedObjectUndo(go, "Add Robot Spawner");
                go.transform.SetParent(runner.transform, false);
                spawner = Undo.AddComponent<RobotSpawner>(go);
            }

            Undo.RecordObject(spawner, "Assign robot prefabs");
            spawner.flankerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlankerPrefabPath);
            spawner.hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HunterPrefabPath);

            var serialized = new SerializedObject(runner);
            serialized.FindProperty("spawnService").objectReferenceValue = spawner;
            serialized.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(runner.gameObject.scene);
            Selection.activeGameObject = spawner.gameObject;
            Debug.Log($"[Agent Setup] RobotSpawner assigned to {runner.name} (flanker: {(spawner.flankerPrefab != null ? "yes" : "MISSING")}, " +
                      $"hunter: {(spawner.hunterPrefab != null ? "yes" : "none yet, flankers only")}). " +
                      $"{(runner.gameObject.activeInHierarchy ? "" : "FacilityAI is disabled in this scene; enable it to run the Director.")}");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif

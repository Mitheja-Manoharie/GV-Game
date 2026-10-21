#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GVGame.Agents;
using GVGame.Combat;
using GVGame.Interaction;
using GVGame.Player;
using LabLockdown.AI.DroneStalker.Runtime;
using LabLockdown.AI.Facility.Integration;
using LabLockdown.AI.Facility.Runtime;
using LabLockdown.AI.Flanker.Runtime;
using LabLockdown.AI.Hunter.Runtime;
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
    ///   4. Build Robot Prefabs        - Robot_Base (body: agent, collider, health, gun, follower, animator),
    ///                                   Robot_Flanker (base + Tactical Flanker brain),
    ///                                   Robot_Hunter (same body, with the Security Hunter's own legs, gun and health)
    ///                                   and Robot_Drone (Eye Drone body + Drone Stalker brain)
    ///   5. Add Robot Spawner To Scene - RobotSpawner under FacilityAI, assigned as the Director's Spawn Service;
    ///                                   removes the Facility AI's test RobotSpawnService
    ///   6. Add Drone Stalker To Scene - DroneStalkerSpawn: 4 drones (maintenance bay, infirmary, research lab, west corridor)
    ///   7. Set Up Player Body         - Alien Humanoid rig, Animator, rifle in the hand and PlayerBodyAnimator in Player.prefab
    ///   8. Add Third-Person View      - PlayerViewToggle in Player.prefab (V shows the Alien body); also part of step 7
    ///   9. Update Existing Assets     - upper-body shooting layer, HeldRifle on every hand rifle, the rifle as the
    ///                                   first-person gun, the drone laser and the Hunter's
    ///                                   tint, in the existing assets (steps 3, 4 and 7 do this when they rebuild)
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
        const string DronePrefabPath = PrefabFolder + "/Robot_Drone.prefab";
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string AlienModelPath = "Assets/Models/Characters/Alien/Alien.fbx";
        const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
        const string DroneModelPath = "Assets/ThirdParty/Quaternius/Essentials/FBX (Unity)/Enemy_EyeDrone.fbx";
        const float DroneHoverHeight = 2.2f; // above head height, below door frames; the StalkerSensor samples light and sight here
        static readonly Vector3 DroneStart = new Vector3(-2f, 0f, 18f);     // maintenance bay, away from the safe areas
        static readonly Vector3 InfirmaryCentre = new Vector3(-12f, 0f, 16f);
        const string RiflePath = "Assets/ThirdParty/Quaternius/Essentials/FBX (Unity)/Gun_Rifle.fbx";
        const string TracerMaterialPath = "Assets/Art/Materials/Tracer.mat";
        const string RightHandBone = "DEF-hand.R";
        const string LeftHandBone = "DEF-hand.L";
        const string UpperBodyLayer = "UpperBody";
        const string UpperBodyMaskPath = ClipsFolder + "/RobotUpperBody.mask";
        static readonly Vector3 FirstPersonRiflePosition = new Vector3(0f, -0.08f, 0f); // grip, under GunViewModel
        const float RifleScale = 0.65f; // Gun_Rifle is 1.06 m long; 0.69 m suits the 1.65 m Alien and Android
        static readonly Color HunterTint = new Color(0.45f, 0.62f, 1f); // steel blue: the Hunter vs the white Flanker

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

            // The World Builder's import settings (Character Setup: size and URP material remap) must survive this step.
            float scale = importer.globalScale;
            var remaps = importer.GetExternalObjectMap();

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = description;
            importer.SaveAndReimport();

            bool kept = Mathf.Approximately(importer.globalScale, scale) && importer.GetExternalObjectMap().Count == remaps.Count;
            if (!kept)
            {
                importer.globalScale = scale;
                foreach (var remap in remaps) importer.AddRemap(remap.Key, remap.Value);
                importer.SaveAndReimport();
                Debug.LogWarning($"[Agent Setup] {model.name}: import scale or material remap changed; restored.");
            }
            Debug.Log($"[Agent Setup] {model.name}: import scale {importer.globalScale} and {importer.GetExternalObjectMap().Count} material remap(s) kept.");

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
            if (shootClip != null) AddUpperBodyShooting(controller, shootClip);

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

        /// <summary>
        /// Shooting on an upper-body layer (arms, head and spine), so the legs keep walking or running while the rifle
        /// is up, instead of a full-body state that froze the legs. Shoot (a shot) or Aiming (a robot facing its target,
        /// the player firing) raises the rifle; while Aiming stays true the firing clip repeats, and the layer lets go
        /// once Aiming is false and the clip is nearly over, or at once on death. Replaces an older full-body Shoot state.
        /// </summary>
        static void AddUpperBodyShooting(AnimatorController controller, AnimationClip shootClip)
        {
            var baseMachine = controller.layers[0].stateMachine;
            foreach (var old in baseMachine.states.Where(s => s.state.name == "Shoot").ToArray())
                baseMachine.RemoveState(old.state);
            for (int i = controller.layers.Length - 1; i >= 1; i--)
                if (controller.layers[i].name == UpperBodyLayer) controller.RemoveLayer(i);
            foreach (var p in new[] { "Shoot", "Aiming", "Dead" })
                if (controller.parameters.All(x => x.name != p))
                    controller.AddParameter(p, p == "Shoot" ? AnimatorControllerParameterType.Trigger : AnimatorControllerParameterType.Bool);

            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                                                     part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                                                     part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                                                     part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
            EditorUtility.SetDirty(mask);

            controller.AddLayer(UpperBodyLayer);
            var layers = controller.layers; // a copy: changes are written back below
            var layer = layers[layers.Length - 1];
            layer.avatarMask = mask;
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            var machine = layer.stateMachine;
            var empty = machine.AddState("Empty"); // no motion and Write Defaults on: the base layer shows through
            empty.writeDefaultValues = true; // off, an empty state freezes the arms in the last aiming pose
            machine.defaultState = empty;
            var shoot = machine.AddState("Shoot");
            shoot.motion = shootClip;

            foreach (string trigger in new[] { "Shoot", "Aiming" })
            {
                var up = empty.AddTransition(shoot);
                up.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
                up.hasExitTime = false;
                up.duration = 0.1f;
            }

            var again = shoot.AddTransition(shoot); // keep firing while aiming
            again.AddCondition(AnimatorConditionMode.If, 0f, "Aiming");
            again.hasExitTime = true;
            again.exitTime = 0.95f;
            again.duration = 0.05f;

            var down = shoot.AddTransition(empty);
            down.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
            down.hasExitTime = true;
            down.exitTime = 0.9f;
            down.duration = 0.2f;

            var dead = shoot.AddTransition(empty);
            dead.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            dead.hasExitTime = false;
            dead.duration = 0.1f;
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

            if (File.Exists(BasePrefabPath) && !Application.isBatchMode && !EditorUtility.DisplayDialog("Rebuild robot prefabs?",
                    "Robot_Base, Robot_Flanker, Robot_Hunter and Robot_Drone will be rebuilt. Manual changes to them " +
                    "(e.g. gun position) are lost.", "Rebuild", "Cancel"))
                return;

            EnsureFolder(PrefabFolder);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null) Debug.LogWarning("[Agent Setup] No Robot.controller yet (step 3); the robot will not animate.");

            var root = new GameObject("Robot_Base");
            try
            {
                float height = AddAndroidBody(root, model, controller);

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

            BuildHunter(model, controller);
            BuildDrone();

            Debug.Log($"[Agent Setup] Built {BasePrefabPath}, {FlankerPrefabPath} and {HunterPrefabPath}" +
                      $"{(File.Exists(DronePrefabPath) ? $" and {DronePrefabPath}" : "")}. " +
                      "Check the rifle's position in the right hand.");
        }

        /// <summary>
        /// The Android body every humanoid robot shares: model with its Animator, solid capsule, kinematic Rigidbody,
        /// RobotAnimator and the rifle in the right hand. Returns the model's height.
        /// </summary>
        static float AddAndroidBody(GameObject root, GameObject model, RuntimeAnimatorController controller)
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

            // Kinematic: moved by script, but still fires triggers (DoorAutoOpenTrigger).
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            var robotAnimator = root.AddComponent<RobotAnimator>();
            robotAnimator.animator = animator;

            AttachRifle(body);
            return height;
        }

        /// <summary>
        /// Robot_Hunter: the Android body with Viduna's Security Hunter. The Hunter moves, shoots and takes damage with
        /// its own components (HunterMover, HunterShooter, HunterHealth), so it is built beside Robot_Base rather than
        /// as a variant: Robot_Base's NavMeshAgent would fight HunterMover, and RobotHealth would be a second health.
        /// </summary>
        static void BuildHunter(GameObject model, RuntimeAnimatorController controller)
        {
            var root = new GameObject("Robot_Hunter");
            try
            {
                AddAndroidBody(root, model, controller);

                root.AddComponent<HunterAgent>(); // RequireComponent adds HunterHealth, HunterMover and HunterShooter
                root.GetComponent<HunterShooter>().tracerMaterial = AssetDatabase.LoadAssetAtPath<Material>(TracerMaterialPath);
                root.GetComponent<HunterHealth>().destroyDelay = 6f; // time for the death animation, as RobotHealth
                root.AddComponent<HunterBody>();
                root.AddComponent<RobotTint>().tint = HunterTint;

                PrefabUtility.SaveAsPrefabAsset(root, HunterPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Robot_Drone: Ravindu's Drone Stalker in the Eye Drone body. The root walks the NavMesh with the same
        /// RobotPathFollower as the robots; only the model and the eye sit at the hover height.
        /// </summary>
        static void BuildDrone()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DroneModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Agent Setup] No drone model at {DroneModelPath}; Robot_Drone not built.");
                return;
            }

            var root = new GameObject("Robot_Drone");
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                body.name = "Model";
                body.transform.SetParent(root.transform, false);
                CentreDroneModel(body);

                // The sensor looks along the eye's forward, so it is a plain child facing the same way as the root.
                var eye = new GameObject("Eye").transform;
                eye.SetParent(root.transform, false);
                eye.localPosition = new Vector3(0f, DroneHoverHeight, 0f);

                var sphere = root.AddComponent<SphereCollider>();
                sphere.isTrigger = false;
                sphere.center = new Vector3(0f, DroneHoverHeight, 0f);
                sphere.radius = 0.45f;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.speed = 3f;
                agent.acceleration = 12f;
                agent.angularSpeed = 720f;
                agent.stoppingDistance = 0f;
                agent.autoBraking = false;
                agent.radius = 0.5f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

                var health = root.AddComponent<RobotHealth>();
                health.maxHealth = 75f; // three rifle hits: it relies on not being seen, not on armour
                health.removeAfter = 3f;
                root.AddComponent<RobotPathFollower>();

                root.AddComponent<StalkerAgent>(); // RequireComponent adds the StalkerSensor
                var sensor = new SerializedObject(root.GetComponent<StalkerSensor>());
                sensor.FindProperty("eye").objectReferenceValue = eye;
                sensor.FindProperty("hoverHeight").floatValue = DroneHoverHeight;
                sensor.ApplyModifiedPropertiesWithoutUndo();

                var droneBody = root.AddComponent<DroneBody>();
                droneBody.model = body.transform;
                droneBody.hoverHeight = DroneHoverHeight;
                droneBody.laserMaterial = AssetDatabase.LoadAssetAtPath<Material>(TracerMaterialPath);
                TuneDroneAttack(root);

                PrefabUtility.SaveAsPrefabAsset(root, DronePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Scales the drone mesh to about 0.8 m across (whatever its import scale) and centres it at the hover height.</summary>
        static void CentreDroneModel(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds Measure()
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                return b;
            }

            var bounds = Measure();
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (size > 0.01f && (size < 0.4f || size > 1.5f))
            {
                body.transform.localScale *= 0.8f / size;
                bounds = Measure();
            }

            // The root is at the origin while building, so world bounds are root-local.
            body.transform.localPosition += new Vector3(0f, DroneHoverHeight, 0f) - bounds.center;
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

        static GameObject AttachRifle(GameObject body)
        {
            var hand = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == RightHandBone);
            var rifle = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
            if (hand == null || rifle == null)
            {
                Debug.LogWarning($"[Agent Setup] Rifle not attached (bone {RightHandBone} or {RiflePath} missing).");
                return null;
            }

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(rifle);
            gun.name = "Rifle";
            gun.transform.SetParent(hand, false);
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            foreach (var c in gun.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            HoldRifle(body, gun);
            return gun;
        }

        /// <summary>HeldRifle on the rifle: grip in the right palm, barrel towards the left hand, every frame.</summary>
        static void HoldRifle(GameObject body, GameObject rifle)
        {
            var held = rifle.GetComponent<HeldRifle>();
            if (held == null) held = rifle.AddComponent<HeldRifle>();
            var bones = body.GetComponentsInChildren<Transform>(true);
            held.rightHand = bones.FirstOrDefault(t => t.name == RightHandBone);
            held.leftHand = bones.FirstOrDefault(t => t.name == LeftHandBone);
            held.body = body.transform;
            rifle.transform.localScale = Vector3.one * RifleScale;
        }

        /// <summary>
        /// The first-person gun becomes the same rifle model the body holds, instead of the Systems setup's
        /// placeholder cube. The GunViewModel transform stays (WeaponController kicks and shows it); only its cube
        /// mesh is removed and the rifle is added under it, pointing at the crosshair.
        /// </summary>
        static bool SetUpFirstPersonRifle(GameObject playerRoot)
        {
            var weapon = playerRoot.GetComponentInChildren<WeaponController>(true);
            var rifle = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
            if (weapon == null || weapon.viewModel == null || rifle == null)
            {
                Debug.LogWarning("[Agent Setup] First-person rifle not set up (no WeaponController view model, or the rifle model is missing).");
                return false;
            }

            Transform viewModel = weapon.viewModel;
            var cube = viewModel.GetComponent<MeshRenderer>();
            if (cube != null) Object.DestroyImmediate(cube);
            var cubeMesh = viewModel.GetComponent<MeshFilter>();
            if (cubeMesh != null) Object.DestroyImmediate(cubeMesh);
            viewModel.localScale = Vector3.one;

            var old = viewModel.Find("Rifle");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(rifle);
            gun.name = "Rifle";
            gun.transform.SetParent(viewModel, false);
            gun.transform.localPosition = FirstPersonRiflePosition;
            gun.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); // barrel (+X) to the camera's forward
            gun.transform.localScale = Vector3.one * RifleScale; // the same size as the rifle in the body's hands
            foreach (var c in gun.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in gun.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // the body's rifle casts the shadow
            return true;
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

            // One spawner only: the Facility AI's test spawner (red capsules) goes.
            int removed = 0;
            foreach (var old in Object.FindObjectsByType<RobotSpawnService>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(old);
                removed++;
            }

            EditorSceneManager.MarkSceneDirty(runner.gameObject.scene);
            Selection.activeGameObject = spawner.gameObject;
            Debug.Log($"[Agent Setup] RobotSpawner assigned to {runner.name} (flanker: {(spawner.flankerPrefab != null ? "yes" : "MISSING")}, " +
                      $"hunter: {(spawner.hunterPrefab != null ? "yes" : "none yet, flankers only")})" +
                      $"{(removed > 0 ? $"; removed {removed} RobotSpawnService" : "")}. " +
                      $"{(runner.gameObject.activeInHierarchy ? "" : "FacilityAI is disabled in this scene; enable it to run the Director.")}");
        }

        // ---------- 6. Drone ----------

        [MenuItem("GV Game/Agent Setup/6. Add Drone Stalker To Scene (optional)")]
        static void AddDrone()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DronePrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Agent Setup] No {DronePrefabPath}. Run step 4 first.");
                return;
            }

            var spawn = Object.FindAnyObjectByType<DroneStalkerSpawn>(FindObjectsInactive.Include);
            if (spawn == null)
            {
                var go = new GameObject("DroneStalkerSpawn");
                Vector3 position = DroneStart;
                if (NavMesh.SamplePosition(position, out var onMesh, 3f, NavMesh.AllAreas)) position = onMesh.position;
                go.transform.position = position;
                Undo.RegisterCreatedObjectUndo(go, "Add Drone Stalker");
                spawn = Undo.AddComponent<DroneStalkerSpawn>(go);
            }

            Undo.RecordObject(spawn, "Assign drone");
            spawn.dronePrefab = prefab;
            spawn.healPoints = HealPoints(spawn.transform);
            spawn.extraStarts = DroneExtraStarts(spawn.transform);

            EditorSceneManager.MarkSceneDirty(spawn.gameObject.scene);
            Selection.activeGameObject = spawn.gameObject;
            Debug.Log($"[Agent Setup] {spawn.name} at {spawn.transform.position}: {1 + spawn.extraStarts.Length} Drone Stalkers, " +
                      $"{spawn.healPoints.Length} heal point(s). Move it or its DroneStart_* children to change where they start.");
        }

        /// <summary>
        /// Start markers for the other drones, made once under the spawn (kept if they exist, so moved ones stay put):
        /// one on each route besides the maintenance bay, away from the safe start and the exit room.
        /// </summary>
        static Transform[] DroneExtraStarts(Transform parent)
        {
            var starts = new List<Transform>();
            foreach (var (name, x, z) in new[] { ("DroneStart_Infirmary", -10f, 18f), ("DroneStart_ResearchLab", 18f, 26f), ("DroneStart_WestCorridor", -6f, 30f) })
            {
                var start = parent.Find(name);
                if (start == null)
                {
                    start = new GameObject(name).transform;
                    start.SetParent(parent, false);
                    Vector3 position = new Vector3(x, parent.position.y, z);
                    if (NavMesh.SamplePosition(position, out var onMesh, 3f, NavMesh.AllAreas)) position = onMesh.position;
                    start.position = position;
                    Undo.RegisterCreatedObjectUndo(start.gameObject, "Add drone start");
                }
                starts.Add(start);
            }
            return starts.ToArray();
        }

        /// <summary>
        /// Step 6 on Main.unity from the command line, then saves the scene:
        ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; -executeMethod GVGame.EditorTools.AgentSetupTools.AddDronesToMain
        /// </summary>
        public static void AddDronesToMain()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath);
            AddDrone();
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>The infirmary (a marker made once under the spawn) and every medkit in the scene.</summary>
        static Transform[] HealPoints(Transform parent)
        {
            var points = new List<Transform>();

            var infirmary = parent.Find("HealPoint_Infirmary");
            if (infirmary == null)
            {
                infirmary = new GameObject("HealPoint_Infirmary").transform;
                infirmary.SetParent(parent, false);
                infirmary.position = new Vector3(InfirmaryCentre.x, parent.position.y, InfirmaryCentre.z);
                Undo.RegisterCreatedObjectUndo(infirmary.gameObject, "Add heal point");
            }
            points.Add(infirmary);

            foreach (var pickup in Object.FindObjectsByType<HealthPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                points.Add(pickup.transform);
            return points.ToArray();
        }

        // ---------- 7. Player body ----------

        [MenuItem("GV Game/Agent Setup/7. Set Up Player Body (Alien)")]
        static void SetUpPlayerBodyMenu() => SetUpPlayerBody();

        /// <summary>
        /// The Alien in Player.prefab gets the robots' animator, the rifle in its right hand (shown once the weapon is
        /// picked up) and PlayerBodyAnimator on the Player root. Uses the Alien the World Builder placed under the
        /// Player; if there is none, adds one at the capsule's feet, casting shadows only (first-person view).
        /// </summary>
        static bool SetUpPlayerBody()
        {
            ConfigureHumanoidRig(AlienModelPath);
            var alienModel = AssetDatabase.LoadAssetAtPath<GameObject>(AlienModelPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (alienModel == null || controller == null)
            {
                Debug.LogError($"[Agent Setup] Player body needs {AlienModelPath} and {ControllerPath} (step 3).");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                GameObject body = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                    if (source != null && AssetDatabase.GetAssetPath(source) == AlienModelPath) { body = t.gameObject; break; }
                }

                if (body == null)
                {
                    body = (GameObject)PrefabUtility.InstantiatePrefab(alienModel);
                    body.name = "Body";
                    body.transform.SetParent(root.transform, false);
                    var capsule = root.GetComponent<CharacterController>();
                    body.transform.localPosition = capsule != null ? capsule.center - Vector3.up * capsule.height * 0.5f : Vector3.zero;
                    foreach (var r in body.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    Debug.Log("[Agent Setup] No Alien under the Player; added one (Shadows Only).");
                }

                var animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AlienModelPath).OfType<Avatar>().FirstOrDefault();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // Run again safely: replace the rifle this step added last time.
                var hand = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == RightHandBone);
                var oldRifle = hand != null ? hand.Find("Rifle") : null;
                if (oldRifle != null) Object.DestroyImmediate(oldRifle.gameObject);

                var rifle = AttachRifle(body);
                var bodyRenderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
                if (rifle != null && bodyRenderer != null)
                    foreach (var r in rifle.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = bodyRenderer.shadowCastingMode; // the rifle shows the same way as the body

                var bodyAnimator = root.GetComponent<PlayerBodyAnimator>();
                if (bodyAnimator == null) bodyAnimator = root.AddComponent<PlayerBodyAnimator>();
                bodyAnimator.animator = animator;
                bodyAnimator.rifle = rifle;

                var view = root.GetComponent<PlayerViewToggle>();
                if (view == null) view = root.AddComponent<PlayerViewToggle>();
                view.body = animator;

                SetUpFirstPersonRifle(root);

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[Agent Setup] {PlayerPrefabPath}: Alien animated, rifle in {RightHandBone}. " +
                          "Open the prefab and check the rifle sits in the palm.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Only adds PlayerViewToggle to Player.prefab (step 7 does this too), without rebuilding the body or the rifle.
        ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; -executeMethod GVGame.EditorTools.AgentSetupTools.AddPlayerViewToggle
        /// </summary>
        [MenuItem("GV Game/Agent Setup/8. Add Third-Person View To Player")]
        public static void AddPlayerViewToggle()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var bodyAnimator = root.GetComponent<PlayerBodyAnimator>();
                if (bodyAnimator == null || bodyAnimator.animator == null)
                    throw new System.InvalidOperationException("[Agent Setup] Player has no body yet: run step 7.");

                var view = root.GetComponent<PlayerViewToggle>();
                if (view == null) view = root.AddComponent<PlayerViewToggle>();
                view.body = bodyAnimator.animator;

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[Agent Setup] {PlayerPrefabPath}: PlayerViewToggle added (V switches to third person).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The drone attacks more often than the Drone Stalker's defaults (play tests: it almost never struck). Only the
        /// prefab's values change; the brain's code, defaults and unit tests stay as they are, and it still strikes only
        /// while unseen. Strike always wins once possible (0.45 → 0.8 + 0.5 × weak; unseen, the others reach at most
        /// Ambush 0.5 + 0.4 × weak, Stalk 0.6, Retreat 0.6 and Hide 0.45 outside the 6 s just after a strike), from 3.5 m
        /// instead of 2.5, every 6 s instead of 12; 8 damage per strike keeps about the agreed 1.2 HP/s (8 / 6 s). The body's reach
        /// is above the brain's range so every strike it starts lands.
        /// </summary>
        static void TuneDroneAttack(GameObject drone)
        {
            var settings = drone.GetComponent<StalkerAgent>().Settings;
            settings.StrikeBase = 0.8f;
            settings.StrikeRangeMetres = 3.5f;
            settings.StrikeCooldownSeconds = 6f;
            var body = drone.GetComponent<DroneBody>();
            body.strikeDamage = 8f;
            body.strikeReach = 4f;
        }

        /// <summary>
        /// Brings the existing animator and prefabs up to date without rebuilding them (steps 3, 4 and 7 do the same
        /// when they rebuild): shooting on the upper-body layer of Robot.controller, HeldRifle on the hand rifle of
        /// Robot_Base (and so Robot_Flanker), Robot_Hunter and the player's Alien, the rifle model as the first-person
        /// gun, the Hunter's tint and the drone's laser.
        ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; -executeMethod GVGame.EditorTools.AgentSetupTools.UpdateExistingPrefabs
        /// </summary>
        [MenuItem("GV Game/Agent Setup/9. Update Existing Animator And Prefabs")]
        public static void UpdateExistingPrefabs()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var shootClip = FindClip("Shoot");
            if (controller == null || shootClip == null)
                throw new System.InvalidOperationException($"[Agent Setup] {ControllerPath} or the Shoot clip is missing (steps 2 and 3).");
            AddUpperBodyShooting(controller, shootClip);
            EditorUtility.SetDirty(controller);
            Debug.Log($"[Agent Setup] {ControllerPath}: shooting on the {UpperBodyLayer} layer.");

            var drone = PrefabUtility.LoadPrefabContents(DronePrefabPath);
            try
            {
                var body = drone.GetComponent<DroneBody>();
                if (body == null) throw new System.InvalidOperationException($"[Agent Setup] {DronePrefabPath} has no DroneBody (step 4).");
                body.laserMaterial = AssetDatabase.LoadAssetAtPath<Material>(TracerMaterialPath);
                TuneDroneAttack(drone);
                PrefabUtility.SaveAsPrefabAsset(drone, DronePrefabPath);
                Debug.Log($"[Agent Setup] {DronePrefabPath}: strike laser and attack tuning.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(drone);
            }

            foreach (string path in new[] { BasePrefabPath, HunterPrefabPath, PlayerPrefabPath })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var hand = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == RightHandBone);
                    var rifle = hand != null ? hand.Find("Rifle") : null;
                    var body = root.GetComponentInChildren<Animator>(true);
                    if (rifle == null || body == null)
                        throw new System.InvalidOperationException($"[Agent Setup] {path} has no rifle in {RightHandBone} (steps 4 and 7).");
                    HoldRifle(body.gameObject, rifle.gameObject);

                    if (path == HunterPrefabPath)
                    {
                        var tint = root.GetComponent<RobotTint>();
                        if (tint == null) tint = root.AddComponent<RobotTint>();
                        tint.tint = HunterTint;
                    }
                    if (path == PlayerPrefabPath && !SetUpFirstPersonRifle(root))
                        throw new System.InvalidOperationException("[Agent Setup] First-person rifle not set up.");

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[Agent Setup] {path}: rifle held in both hands" +
                              (path == HunterPrefabPath ? ", Hunter tint" : path == PlayerPrefabPath ? ", first-person rifle" : "") + ".");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        // ---------- All steps from the command line ----------

        /// <summary>
        /// Steps 1 to 6 without opening the Unity window, then saves Main.unity:
        ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; -executeMethod GVGame.EditorTools.AgentSetupTools.RunAll -logFile agent-setup.log
        /// Throws (exit code 1) when something the robots need is missing, so a failed run is obvious.
        /// </summary>
        public static void RunAll()
        {
            ConfigureAndroidRig();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(AndroidModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new System.InvalidOperationException("[Agent Setup] Android Humanoid avatar is not valid (step 1).");

            ConfigureClips();
            if (FindClip("Idle") == null || FindClip("Walk") == null || FindClip("Run") == null)
                throw new System.InvalidOperationException($"[Agent Setup] Idle, Walk and Run clips are missing from {ClipsFolder} (step 2).");

            BuildAnimator();
            BuildPrefabs();
            if (!File.Exists(FlankerPrefabPath) || !File.Exists(HunterPrefabPath))
                throw new System.InvalidOperationException("[Agent Setup] Robot prefabs were not built (step 4).");
            if (!SetUpPlayerBody())
                throw new System.InvalidOperationException("[Agent Setup] Player body was not set up (step 7).");

            var scene = EditorSceneManager.OpenScene(MainScenePath);
            AddSpawner();
            AddDrone();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Agent Setup] Steps 1 to 6 done; Main.unity saved.");
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

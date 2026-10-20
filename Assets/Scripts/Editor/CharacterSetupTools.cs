using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// GV Game > Character Setup > Configure Character Imports.
    /// Gets the Core Developer's two custom models ready to drop into prefabs:
    ///   - import scale so the Alien is 1.8 m (the player capsule) and the Android about 2 m (what the robot setup expects),
    ///   - texture import settings (normal maps as Normal map, metallic/smoothness and AO as linear data),
    ///   - one URP Lit material per model, remapped onto the FBX, with the painted textures,
    ///   - no animation import (the models carry no clips) and no colliders.
    /// The rig (Humanoid avatar) is left to the Agent Setup tool. Safe to run again.
    ///
    /// GV Game > Character Setup > Place Alien In Player.
    /// Puts the Alien under the Player root as the player's body: facing the player's forward direction, feet on the
    /// bottom of the capsule, no colliders, root motion off, Shadows Only (the first-person camera never draws it).
    /// The Agent Setup tool then adds its rig, animation and rifle. Safe to run again: it replaces the body it made.
    /// </summary>
    public static class CharacterSetupTools
    {
        const string AlienModel = "Assets/Models/Characters/Alien/Alien.fbx";
        const string AndroidModel = "Assets/Models/Characters/Android/Android.fbx";
        const string AlienTextures = "Assets/Textures/Characters/Alien/";
        const string AndroidTextures = "Assets/Textures/Characters/Android/";
        const string MaterialFolder = "Assets/Materials/Characters";

        const string PlayerPrefab = "Assets/Prefabs/Player/Player.prefab";
        const string BodyName = "AlienBody";

        const float AlienHeight = 1.8f;
        const float AndroidHeight = 2.0f;

        // Lighting notes from the World Builder: keep albedo mid-grey or darker (ACES + albedo boost),
        // emissive parts bloom above an intensity of about 1.
        static readonly Color AndroidArmourTint = new Color(0.85f, 0.85f, 0.85f, 1f);
        const float AndroidEmission = 1.8f;

        [MenuItem("GV Game/Character Setup/Configure Character Imports")]
        public static void ConfigureCharacterImports() => Run();

        /// <summary>Entry point for batch mode (-executeMethod); exits with 0 on success, 1 on failure.</summary>
        public static void ConfigureCharacterImportsBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        [MenuItem("GV Game/Character Setup/Place Alien In Player")]
        public static void PlaceAlienInPlayer() => PlaceAlien();

        /// <summary>Entry point for batch mode (-executeMethod); exits with 0 on success, 1 on failure.</summary>
        public static void PlaceAlienInPlayerBatch() => EditorApplication.Exit(PlaceAlien() ? 0 : 1);

        static bool PlaceAlien()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AlienModel);
            if (model == null) { Debug.LogError($"[CharacterSetup] Model not found: {AlienModel}"); return false; }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                Transform old = root.transform.Find(BodyName);
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                body.name = BodyName;
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.transform.localScale = Vector3.one;   // the import scale already makes it 1.80 m

                // Face the player's forward direction: the toes must be ahead of the ankle.
                Transform ankle = FindDeep(body.transform, "DEF-foot.L");
                Transform toe = FindDeep(body.transform, "DEF-toe.L");
                if (ankle != null && toe != null &&
                    root.transform.InverseTransformPoint(toe.position).z < root.transform.InverseTransformPoint(ankle.position).z)
                    body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                // Feet on the bottom of the capsule.
                var capsule = root.GetComponent<CharacterController>();
                float footY = capsule != null ? capsule.center.y - capsule.height * 0.5f : 0f;
                (float minY, float maxY) = VertexRange(body, root.transform);
                body.transform.localPosition = new Vector3(0f, footY - minY, 0f);

                foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

                Animator animator = body.GetComponent<Animator>();
                if (animator != null) animator.applyRootMotion = false;

                foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    r.receiveShadows = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
                bool turned = body.transform.localEulerAngles.y > 90f;
                Debug.Log($"[CharacterSetup] Alien placed in Player.prefab: height {maxY - minY:0.00} m, feet at {footY:0.00}, " +
                          $"{(turned ? "turned 180 degrees to face forward" : "facing forward")}, Shadows Only, no colliders.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform FindDeep(Transform parent, string name) =>
            parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        /// <summary>Lowest and highest vertex of <paramref name="go"/>, in the space of <paramref name="space"/>.</summary>
        static (float min, float max) VertexRange(GameObject go, Transform space)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            var baked = new Mesh();
            try
            {
                foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.BakeMesh(baked, true);
                    Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    foreach (Vector3 v in baked.vertices)
                    {
                        float y = space.InverseTransformPoint(toWorld.MultiplyPoint3x4(v)).y;
                        if (y < min) min = y;
                        if (y > max) max = y;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
            return (min, max);
        }

        static bool Run()
        {
            // Textures first, so the materials pick up the right import types.
            SetTexture(AlienTextures + "Alien_Game_BaseColor.png", TextureImporterType.Default, true);
            SetTexture(AlienTextures + "Alien_Game_Normal.png", TextureImporterType.NormalMap, false);
            SetTexture(AndroidTextures + "Android_BaseColor.png", TextureImporterType.Default, true);
            SetTexture(AndroidTextures + "Android_Normal.png", TextureImporterType.NormalMap, false);
            SetTexture(AndroidTextures + "Android_MetallicSmoothness.png", TextureImporterType.Default, false);
            SetTexture(AndroidTextures + "Android_AO.png", TextureImporterType.Default, false);
            SetTexture(AndroidTextures + "Android_Emission.png", TextureImporterType.Default, true);

            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Characters");

            Material alien = MakeLit(MaterialFolder + "/Alien.mat",
                baseMap: AlienTextures + "Alien_Game_BaseColor.png", tint: Color.white,
                normal: AlienTextures + "Alien_Game_Normal.png",
                metallicSmoothness: null, occlusion: null, emission: null, emissionIntensity: 0f,
                metallic: 0f, smoothness: 0.4f);

            Material android = MakeLit(MaterialFolder + "/Android.mat",
                baseMap: AndroidTextures + "Android_BaseColor.png", tint: AndroidArmourTint,
                normal: AndroidTextures + "Android_Normal.png",
                metallicSmoothness: AndroidTextures + "Android_MetallicSmoothness.png",
                occlusion: AndroidTextures + "Android_AO.png",
                emission: AndroidTextures + "Android_Emission.png", emissionIntensity: AndroidEmission,
                metallic: 1f, smoothness: 1f);

            AssetDatabase.SaveAssets();

            bool ok = ConfigureModel(AlienModel, AlienHeight, alien);
            ok &= ConfigureModel(AndroidModel, AndroidHeight, android);
            AssetDatabase.SaveAssets();
            Debug.Log(ok ? "[CharacterSetup] Character imports configured." : "[CharacterSetup] Finished with errors (see above).");
            return ok;
        }

        static void SetTexture(string path, TextureImporterType type, bool sRGB)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError($"[CharacterSetup] Texture not found: {path}"); return; }
            if (importer.textureType == type && importer.sRGBTexture == sRGB) return;
            importer.textureType = type;
            importer.sRGBTexture = type == TextureImporterType.NormalMap ? false : sRGB;
            importer.SaveAndReimport();
        }

        static Material MakeLit(string path, string baseMap, Color tint, string normal, string metallicSmoothness,
                                string occlusion, string emission, float emissionIntensity, float metallic, float smoothness)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;

            mat.SetTexture("_BaseMap", Load(baseMap));
            mat.SetColor("_BaseColor", tint);

            Texture n = Load(normal);
            mat.SetTexture("_BumpMap", n);
            mat.SetFloat("_BumpScale", 1f);
            Toggle(mat, "_NORMALMAP", n != null);

            Texture ms = Load(metallicSmoothness);
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the metallic map's alpha
            mat.SetFloat("_Metallic", ms != null ? 1f : metallic);
            mat.SetFloat("_Smoothness", ms != null ? 1f : smoothness);
            Toggle(mat, "_METALLICSPECGLOSSMAP", ms != null);

            Texture ao = Load(occlusion);
            mat.SetTexture("_OcclusionMap", ao);
            mat.SetFloat("_OcclusionStrength", 1f);
            Toggle(mat, "_OCCLUSIONMAP", ao != null);

            Texture em = Load(emission);
            mat.SetTexture("_EmissionMap", em);
            mat.SetColor("_EmissionColor", em != null ? Color.white * emissionIntensity : Color.black);
            Toggle(mat, "_EMISSION", em != null);
            mat.globalIlluminationFlags = em != null ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                                                     : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture Load(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture>(path);

        static void Toggle(Material mat, string keyword, bool on)
        {
            if (on) mat.EnableKeyword(keyword); else mat.DisableKeyword(keyword);
        }

        /// <summary>Sets the import scale so the model is <paramref name="targetHeight"/> metres tall, and remaps its material.</summary>
        static bool ConfigureModel(string path, float targetHeight, Material material)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { Debug.LogError($"[CharacterSetup] Model not found: {path}"); return false; }

            importer.importAnimation = false;
            importer.addCollider = false;
            importer.useFileScale = true;

            foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
            importer.SaveAndReimport();

            float height = MeasureHeight(path);
            if (height <= 0.01f) { Debug.LogError($"[CharacterSetup] Could not measure {path}."); return false; }

            float native = height / Mathf.Max(1e-4f, importer.globalScale);
            importer.globalScale = targetHeight / native;
            importer.SaveAndReimport();

            float final = MeasureHeight(path);
            bool ok = Mathf.Abs(final - targetHeight) < 0.05f;
            Debug.Log($"[CharacterSetup] {System.IO.Path.GetFileName(path)}: scale factor {importer.globalScale:0.0000}, height {final:0.00} m (target {targetHeight:0.00} m).");
            return ok;
        }

        /// <summary>
        /// Height of the model from its actual vertices (skinned meshes are baked in their bind pose). Renderer bounds
        /// are not used: a skinned mesh's bounds box is often larger than the mesh itself.
        /// </summary>
        static float MeasureHeight(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return 0f;
            GameObject temp = Object.Instantiate(asset);
            var baked = new Mesh();
            try
            {
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (SkinnedMeshRenderer smr in temp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.BakeMesh(baked, true);
                    Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    foreach (Vector3 v in baked.vertices)
                    {
                        float y = toWorld.MultiplyPoint3x4(v).y;
                        if (y < min) min = y;
                        if (y > max) max = y;
                    }
                }
                foreach (MeshFilter mf in temp.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        float y = mf.transform.TransformPoint(v).y;
                        if (y < min) min = y;
                        if (y > max) max = y;
                    }
                }
                return max > min ? max - min : 0f;
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(temp);
            }
        }
    }
}

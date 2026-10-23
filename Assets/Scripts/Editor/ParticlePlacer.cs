using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: adds small particle effects to Main under Environment/Effects.
    ///
    ///   - Sparks falling from two damaged ceiling lights, one on each route, bouncing off the floor. A FaultyLight on
    ///     each makes the fixture flicker and flash with every shower.
    ///   - Bubbles rising in the four specimen tanks.
    ///
    /// Every system is capped at 30–60 particles, casts no shadows and uses an unlit material, so the whole set
    /// costs a few hundred particles at most and needs no rebake. Particle systems are culled when off screen.
    /// The three materials are created in Assets/Materials/Particles the first time (copies of URP's ParticlesUnlit).
    /// Running the tool again replaces the whole Effects group (undoable).
    /// </summary>
    public static class ParticlePlacer
    {
        private const string UrpParticles = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";
        private const string Folder = "Assets/Materials/Particles";

        [MenuItem("Tools/World Builder/Place Particle Effects")]
        public static void PlaceEffects()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                EditorUtility.DisplayDialog("Place Particle Effects", "Open Main.unity first: no 'Environment' object found.", "OK");
                return;
            }

            var sparkMat = GetMaterial("M_Particle_Spark", new Color(3f, 1.4f, 0.45f, 1f), additive: true, queue: 3000);   // HDR: blooms
            var bubbleMat = GetMaterial("M_Particle_Bubble", new Color(1f, 1f, 1f, 1f), additive: false, queue: 3050);    // after the tank glass
            if (sparkMat == null || bubbleMat == null) return;

            var old = environment.transform.Find("Effects");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("Effects");
            Undo.RegisterCreatedObjectUndo(root, "Place Particle Effects");
            root.transform.SetParent(environment.transform, false);

            // ---- Sparks under two ceiling lights (y 5): service corridor (east route), infirmary north corridor (west).
            var sparks = Group(root, "Sparks");
            // Each one also makes the fixture above it short out (FaultyLight), in step with the sparks.
            Sparks(sparks, "Sparks_ServiceCorridor", new Vector3(14f, 4.75f, 9f), sparkMat).AddComponent<GVGame.Level.FaultyLight>();
            Sparks(sparks, "Sparks_InfirmaryNorth", new Vector3(-14f, 4.75f, 25.5f), sparkMat).AddComponent<GVGame.Level.FaultyLight>();

            // ---- Bubbles in every specimen tank in the scene.
            var bubbles = Group(root, "TankBubbles");
            int tanks = 0;
            foreach (var t in environment.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Prop_SpecimenTank") || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                Bubbles(bubbles, "Bubbles_" + (++tanks), new Vector3(t.position.x, 0.35f, t.position.z), bubbleMat);
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[ParticlePlacer] Placed 2 spark points and bubbles in {tanks} tanks under Environment/Effects.");
        }

        // ------------------------------------------------------------------ effects

        private static GameObject Sparks(Transform parent, string name, Vector3 position, Material material)
        {
            var ps = Create(parent, name, position, Quaternion.Euler(90f, 0f, 0f), material, out var renderer);   // emits downwards

            var main = ps.main;
            main.duration = 0.6f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.startColor = new Color(1f, 0.85f, 0.55f, 1f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            // Irregular bursts: every 0.6 s there is a 30 % chance of a shower of 8–18 sparks.
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var burst = new ParticleSystem.Burst(0f, 8, 18) { probability = 0.3f };
            emission.SetBursts(new[] { burst });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.03f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.2f), 0.5f), new GradientColorKey(new Color(0.8f, 0.15f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            // Bounce off the floor and props (cheap, low-quality world collision for 60 particles at most).
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.bounce = 0.3f;
            collision.dampen = 0.4f;
            collision.lifetimeLoss = 0.2f;

            // Streaks rather than dots.
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1f;
            return ps.gameObject;
        }

        private static void Bubbles(Transform parent, string name, Vector3 position, Material material)
        {
            var ps = Create(parent, name, position, Quaternion.Euler(-90f, 0f, 0f), material, out var renderer);  // emits upwards

            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 4.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.5f);   // rises about 2 m, the height of the glass
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new Color(0.75f, 1f, 0.85f, 0.55f);         // lab green tint
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 30;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2f;
            shape.radius = 0.38f;                                          // inside the 0.6 m glass

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = Fade(Color.white, 0.1f, 0.85f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 1f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        // ------------------------------------------------------------------ helpers

        private static ParticleSystem Create(Transform parent, string name, Vector3 position, Quaternion rotation,
                                             Material material, out ParticleSystemRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var ps = go.AddComponent<ParticleSystem>();

            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;          // unlit material
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var main = ps.main;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.Automatic;  // paused when off screen
            return ps;
        }

        /// <summary>Alpha fades in until <paramref name="inEnd"/>, holds, and fades out from <paramref name="outStart"/>.</summary>
        private static Gradient Fade(Color color, float inEnd, float outStart)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inEnd), new GradientAlphaKey(1f, outStart), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private static Transform Group(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go.transform;
        }

        /// <summary>Loads the material, or creates it once as a copy of URP's ParticlesUnlit.</summary>
        private static Material GetMaterial(string name, Color baseColor, bool additive, int queue)
        {
            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var source = AssetDatabase.LoadAssetAtPath<Material>(UrpParticles);
            if (source == null)
            {
                Debug.LogError($"[ParticlePlacer] URP particle material not found at {UrpParticles}.");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Materials", "Particles");

            var m = new Material(source) { name = name };
            m.SetColor("_BaseColor", baseColor);
            if (additive)
            {
                m.SetFloat("_Blend", 2f);                          // URP particles: Additive
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            }
            m.renderQueue = queue;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}

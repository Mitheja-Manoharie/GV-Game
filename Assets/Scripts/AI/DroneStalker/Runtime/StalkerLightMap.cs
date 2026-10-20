using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabLockdown.AI.DroneStalker.Runtime
{
    /// <summary>
    /// How bright each tile of the level graph is, from 0 (darkest in the level) to 1 (among the brightest).
    /// This is what lets the Stalker prefer dark routes and dark hiding places.
    ///
    /// Sources:
    ///   1. The baked light probes. They already hold the baked lights and their bounced (indirect) light, so one
    ///      lookup per tile captures the level's lighting as the player sees it.
    ///   2. Realtime lights (the fill lights), which are not in the probes: added with a smooth range falloff, and
    ///      only if nothing solid stands between the light and the tile.
    /// If the scene has no light probes, every enabled light is used instead, so the map still works.
    ///
    /// Raw brightness is then rescaled across the level (5th percentile = 0, 90th percentile = 1): the drone cares
    /// about "darker than elsewhere", not absolute values, and this keeps the weights in StalkerSettings meaningful
    /// whatever the light intensities are.
    /// </summary>
    public sealed class StalkerLightMap
    {
        static readonly Vector3[] SampleDirections =
        {
            Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        };

        /// <summary>Light level per node id, 0 to 1.</summary>
        public float[] Levels { get; }

        /// <summary>Unscaled brightness per node (luminance), for debugging.</summary>
        public float[] Raw { get; }

        /// <summary>True if the baked light probes were used.</summary>
        public bool UsedLightProbes { get; }

        /// <summary>How many lights were added on top of the probes.</summary>
        public int AddedLights { get; }

        StalkerLightMap(float[] levels, float[] raw, bool usedProbes, int addedLights)
        {
            Levels = levels;
            Raw = raw;
            UsedLightProbes = usedProbes;
            AddedLights = addedLights;
        }

        /// <summary>
        /// Measures every node of <paramref name="graph"/>.
        /// </summary>
        /// <param name="nodePosition">World position of a node on the floor.</param>
        /// <param name="sampleHeight">Height above the floor to measure at (about where the drone hovers).</param>
        /// <param name="lights">Lights to consider; null finds every light in the loaded scenes.</param>
        /// <param name="occluders">Layers that block light (walls and doors).</param>
        public static StalkerLightMap Build(LevelGraph graph, Func<int, Vector3> nodePosition, float sampleHeight,
                                            IEnumerable<Light> lights, LayerMask occluders)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (nodePosition == null) throw new ArgumentNullException(nameof(nodePosition));

            int n = graph.NodeCount;
            var raw = new float[n];
            var lightList = new List<Light>(lights ?? UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None));
            LightProbes probes = LightmapSettings.lightProbes;
            bool useProbes = probes != null && probes.count > 0;
            var colours = new Color[SampleDirections.Length];

            int added = 0;
            foreach (Light light in lightList)
                if (Counts(light, useProbes)) added++;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = nodePosition(i) + Vector3.up * sampleHeight;
                float lum = 0f;

                if (useProbes)
                {
                    LightProbes.GetInterpolatedProbe(p, null, out SphericalHarmonicsL2 sh);
                    sh.Evaluate(SampleDirections, colours);
                    for (int d = 0; d < colours.Length; d++) lum += Luminance(colours[d]);
                    lum /= colours.Length;
                }

                foreach (Light light in lightList)
                    if (Counts(light, useProbes)) lum += Contribution(light, p, occluders);

                raw[i] = lum;
            }

            return new StalkerLightMap(Rescale(raw), raw, useProbes, added);
        }

        /// <summary>A light is added on top of the probes only if it is on and not already baked into them.</summary>
        static bool Counts(Light light, bool useProbes)
        {
            if (light == null || !light.isActiveAndEnabled || light.intensity <= 0f) return false;
            if (!useProbes) return true;
            return light.bakingOutput.lightmapBakeType != LightmapBakeType.Baked;
        }

        static float Contribution(Light light, Vector3 point, LayerMask occluders)
        {
            float strength = light.intensity * Luminance(light.color);
            if (light.type == LightType.Directional) return strength;
            if (light.type != LightType.Point && light.type != LightType.Spot) return 0f;   // area lights are baked only

            Vector3 from = light.transform.position;
            Vector3 toPoint = point - from;
            float distance = toPoint.magnitude;
            if (distance > light.range) return 0f;
            if (light.type == LightType.Spot && Vector3.Angle(light.transform.forward, toPoint) > light.spotAngle * 0.5f) return 0f;
            if (Physics.Linecast(from, point, occluders, QueryTriggerInteraction.Ignore)) return 0f;

            float falloff = 1f - distance / Mathf.Max(0.01f, light.range);
            return strength * falloff * falloff;
        }

        static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Maps the 5th percentile to 0 and the 90th to 1, clamped. A flat level gets 0.5 everywhere.</summary>
        static float[] Rescale(float[] raw)
        {
            var levels = new float[raw.Length];
            if (raw.Length == 0) return levels;

            var sorted = (float[])raw.Clone();
            Array.Sort(sorted);
            float lo = sorted[(int)((sorted.Length - 1) * 0.05f)];
            float hi = sorted[(int)((sorted.Length - 1) * 0.90f)];
            if (hi - lo < 1e-5f)
            {
                for (int i = 0; i < levels.Length; i++) levels[i] = 0.5f;
                return levels;
            }
            for (int i = 0; i < raw.Length; i++) levels[i] = Mathf.Clamp01((raw[i] - lo) / (hi - lo));
            return levels;
        }
    }
}

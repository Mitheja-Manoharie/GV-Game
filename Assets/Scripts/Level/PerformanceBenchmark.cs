using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GVGame.Level
{
    /// <summary>
    /// Repeatable performance measurement for the World Builder's optimisation evidence. Press F4 in Play mode.
    ///
    /// The game is paused and the player camera is moved to four fixed viewpoints (corridor, maintenance bay, the bay
    /// seen from the crossroads, control room). At each one it renders the view four ways — occlusion culling on/off
    /// × LOD culling on/off (LOD off = QualitySettings.lodBias 1000, so nothing is culled by distance) — waits 20
    /// frames, then averages 60 frames of: frame time, batches, SetPass calls, draw calls, triangles, vertices and,
    /// where Unity provides them, static/instanced batching counters. It also counts the active Realtime and Baked
    /// lights. Results go to the Console and to Docs/Performance/benchmark.csv (Editor) or the persistent data folder
    /// (build). Everything is restored afterwards.
    ///
    /// Render counters are recorded by ProfilerRecorder, which works in the Editor and in development builds. Frame
    /// times in the Editor include Editor overhead, so compare rows with each other, not with a build. Keep the
    /// Scene view closed while it runs: it renders too and would add to the counts.
    /// Put it on any object in Main (e.g. FacilityAI).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]   // LateUpdate after the player's camera code, so the fixed view wins
    public sealed class PerformanceBenchmark : MonoBehaviour
    {
        [System.Serializable]
        public struct Viewpoint
        {
            public string name;
            public Vector3 position;
            public float yaw;     // degrees, 0 = looking north (+Z)
            public float pitch;   // degrees, positive = looking down
        }

        [SerializeField] private Viewpoint[] viewpoints =
        {
            new Viewpoint { name = "Service corridor (armoury door, looking north)", position = new Vector3(14f, 1.6f, 4.6f), yaw = 0f },
            new Viewpoint { name = "Maintenance bay (SE corner, looking NW)", position = new Vector3(7f, 1.6f, 9f), yaw = 315f },
            new Viewpoint { name = "Crossroads (looking west into the bay)", position = new Vector3(13f, 1.6f, 14f), yaw = 270f },
            new Viewpoint { name = "Control room (from the lab link, looking west)", position = new Vector3(11f, 1.6f, 30f), yaw = 270f },
        };
        [SerializeField] private int warmupFrames = 20;
        [SerializeField] private int sampleFrames = 60;

        private static readonly string[] Counters =
        {
            "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Vertices Count",
            "Static Batched Draw Calls Count", "Static Batches Count", "Instanced Batched Draw Calls Count",
        };

        private bool running;
        private string status = "";
        private Transform camTransform;
        private Vector3 viewPosition;
        private Quaternion viewRotation;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (!running && keyboard != null && keyboard.f4Key.wasPressedThisFrame) StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Benchmark] No main camera.", this); yield break; }

            running = true;
            camTransform = cam.transform;
            Vector3 savedLocalPos = camTransform.localPosition;
            Quaternion savedLocalRot = camTransform.localRotation;
            bool savedOcclusion = cam.useOcclusionCulling;
            float savedLodBias = QualitySettings.lodBias;
            float savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            var recorders = new List<(string name, ProfilerRecorder recorder)>();
            foreach (string counter in Counters)
                recorders.Add((counter, ProfilerRecorder.StartNew(ProfilerCategory.Render, counter)));

            var (realtime, baked) = CountLights();
            int beacons = FindObjectsByType<AlarmBeacon>(FindObjectsSortMode.None).Length;

            var csv = new StringBuilder("viewpoint,occlusion,lod_culling,frame_ms,fps");
            foreach (var (name, recorder) in recorders) if (recorder.Valid) csv.Append(',').Append(name.Replace(" Count", "").Replace(' ', '_').ToLowerInvariant());
            csv.Append($",realtime_lights,baked_lights\n");

            foreach (var vp in viewpoints)
                foreach (bool occlusion in new[] { true, false })
                    foreach (bool lod in new[] { true, false })
                    {
                        cam.useOcclusionCulling = occlusion;
                        QualitySettings.lodBias = lod ? savedLodBias : 1000f;
                        status = $"Benchmark: {vp.name} | occlusion {(occlusion ? "on" : "off")} | LOD culling {(lod ? "on" : "off")}";

                        var sums = new double[recorders.Count];
                        double frameSum = 0;
                        for (int f = 0; f < warmupFrames + sampleFrames; f++)
                        {
                            viewPosition = vp.position;
                            viewRotation = Quaternion.Euler(vp.pitch, vp.yaw, 0f);
                            yield return null;
                            if (f < warmupFrames) continue;
                            frameSum += Time.unscaledDeltaTime;
                            for (int i = 0; i < recorders.Count; i++)
                                if (recorders[i].recorder.Valid) sums[i] += recorders[i].recorder.LastValue;
                        }

                        double ms = frameSum / sampleFrames * 1000.0;
                        csv.Append($"\"{vp.name}\",{(occlusion ? "on" : "off")},{(lod ? "on" : "off")},{ms:0.00},{1000.0 / ms:0}");
                        for (int i = 0; i < recorders.Count; i++)
                            if (recorders[i].recorder.Valid) csv.Append(',').Append((sums[i] / sampleFrames).ToString("0"));
                        csv.Append($",{realtime},{baked}\n");
                    }

            foreach (var (_, recorder) in recorders) recorder.Dispose();

            cam.useOcclusionCulling = savedOcclusion;
            QualitySettings.lodBias = savedLodBias;
            camTransform.localPosition = savedLocalPos;
            camTransform.localRotation = savedLocalRot;
            Time.timeScale = savedTimeScale;
            status = "";
            running = false;

            string path = OutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, csv.ToString());
            Debug.Log($"[Benchmark] Done. Realtime lights now: {realtime} (+{beacons} alarm beacons at a Peak), baked: {baked}.\n" +
                      $"Saved to {path}\n{csv}", this);
        }

        private void LateUpdate()
        {
            if (running && camTransform != null) camTransform.SetPositionAndRotation(viewPosition, viewRotation);
        }

        /// <summary>Active, enabled lights: Realtime (not baked) and Baked.</summary>
        private static (int realtime, int baked) CountLights()
        {
            int realtime = 0, baked = 0;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!light.isActiveAndEnabled) continue;
                var output = light.bakingOutput;
                if (output.isBaked && output.lightmapBakeType == LightmapBakeType.Baked) baked++;
                else realtime++;
            }
            return (realtime, baked);
        }

        private static string OutputPath()
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "..", "Docs", "Performance", "benchmark.csv");
#else
            return Path.Combine(Application.persistentDataPath, "benchmark.csv");
#endif
        }

        private void OnGUI()
        {
            if (!running) return;
            GUI.Label(new Rect(12f, Screen.height - 32f, Screen.width, 24f), status);
        }
    }
}

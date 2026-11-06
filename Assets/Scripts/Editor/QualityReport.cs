using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: checks the lighting and texturing of Main with numbers, so quality can be shown and fixed.
    /// Tools > World Builder > Quality Report writes CSV files to Docs/Quality and a summary to the Console.
    ///
    ///   1. Lightmap UVs: every lightmapped static renderer must have a second UV channel (lightmap UVs). Missing
    ///      ones would fall back to the texture UVs, which overlap on a trim sheet and bake as blotches. Also lists
    ///      each renderer's Scale In Lightmap, so any object with uneven lightmap resolution stands out.
    ///   2. Texel density: for each renderer, texture pixels per metre on its surface
    ///      (sqrt(UV area / world area) × texture width), grouped by Structure / Props. A renderer far from its
    ///      group's median (under half or over double) looks blurrier or sharper than its neighbours.
    ///   3. Light probes: walkable points on the NavMesh (triangle corners, centres and edge midpoints, at 1 m
    ///      height) are checked for the distance to the nearest baked probe, and every probe is compared with its
    ///      neighbours: a probe far darker than all neighbours (under 15 %) is probably inside a wall or prop and
    ///      would darken robots passing by.
    ///   4. Lightmap UV overlap: each lightmapped renderer's lightmap UVs are laid onto its real lightmap texels
    ///      (lightmap size × Scale/Offset from the bake) and every texel is marked with the UV chart (island) that
    ///      touches it. A texel touched by two charts bleeds light from one surface onto another (dark or bright
    ///      blotches). This is the same check as the Scene view's UV Overlap mode, as a list.
    ///      Tools > World Builder > Light Overlapping Props With Probes fixes props with at least 1 % shared texels: they keep
    ///      Contribute GI (they still block and bounce light) but receive light from the light probes instead of
    ///      the lightmap. The probe check above shows the probes cover every walkable point. Bake again afterwards.
    /// Reading mesh data works on non-readable meshes in Edit mode only, so run it outside Play mode.
    /// </summary>
    public static class QualityReport
    {
        private const float ProbeGapLimit = 3f;    // m: a walkable point further than this from any probe is a gap
        private const float DarkRatio = 0.15f;     // a probe under 15 % of its neighbours' median brightness
        private const float FixOverlapPercent = 1f; // the fix switches props with at least 1 % of their texels shared

        [MenuItem("Tools/World Builder/Quality Report")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Quality Report", "Run it outside Play mode.", "OK");
                return;
            }
            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                EditorUtility.DisplayDialog("Quality Report", "Open Main.unity first: no 'Environment' object found.", "OK");
                return;
            }

            string folder = Path.Combine(Application.dataPath, "..", "Docs", "Quality");
            Directory.CreateDirectory(folder);
            var summary = new StringBuilder("[QualityReport]\n");
            LightmapUVs(environment, folder, summary);
            TexelDensity(environment, folder, summary);
            Probes(folder, summary);
            Overlaps(environment, folder, summary);
            summary.Append($"CSV files in {folder}");
            Debug.Log(summary.ToString());
        }

        // ---------- 1. lightmap UVs ----------

        private static void LightmapUVs(GameObject environment, string folder, StringBuilder summary)
        {
            var csv = new StringBuilder("object,mesh,has_lightmap_uv,scale_in_lightmap\n");
            int lightmapped = 0, missing = 0;
            var scales = new List<float>();
            foreach (var r in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.ContributeGI)) continue;
                if (r.receiveGI != ReceiveGI.Lightmaps || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                lightmapped++;
                bool hasUv2 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                if (!hasUv2) missing++;
                float scale = new SerializedObject(r).FindProperty("m_ScaleInLightmap").floatValue;
                scales.Add(scale);
                csv.Append($"\"{PathOf(r.transform)}\",{mesh.name},{(hasUv2 ? "yes" : "NO")},{scale:0.##}\n");
            }
            File.WriteAllText(Path.Combine(folder, "lightmap_uvs.csv"), csv.ToString());
            var distinct = scales.Distinct().OrderBy(s => s).ToList();
            summary.Append($"1. Lightmap UVs: {lightmapped} lightmapped renderers, {missing} without lightmap UVs. " +
                           $"Scale In Lightmap values used: {string.Join(", ", distinct.Select(s => s.ToString("0.##")))}.\n");
        }

        // ---------- 2. texel density ----------

        private static void TexelDensity(GameObject environment, string folder, StringBuilder summary)
        {
            var rows = new List<(string path, string group, string texture, float density)>();
            foreach (var r in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                var material = r.sharedMaterial;
                var texture = material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material?.mainTexture;
                if (mesh == null || texture == null) continue;

                double uvArea = 0, worldArea = 0;
                try
                {
                    var vertices = mesh.vertices;
                    var uvs = mesh.uv;
                    var triangles = mesh.triangles;
                    if (uvs.Length != vertices.Length) continue;
                    var m = r.transform.localToWorldMatrix;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(vertices[triangles[i]]), b = m.MultiplyPoint3x4(vertices[triangles[i + 1]]), c = m.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                        worldArea += Vector3.Cross(b - a, c - a).magnitude * 0.5;
                        Vector2 ua = uvs[triangles[i]], ub = uvs[triangles[i + 1]], uc = uvs[triangles[i + 2]];
                        uvArea += Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * 0.5;
                    }
                }
                catch { continue; }   // mesh data not readable

                if (worldArea < 1e-4 || uvArea < 1e-8) continue;
                float density = (float)(System.Math.Sqrt(uvArea / worldArea) * texture.width);
                string path = PathOf(r.transform);
                string group = GroupOf(path);
                rows.Add((path, group, texture.name, density));
            }

            var csv = new StringBuilder("object,group,texture,px_per_m,vs_group_median\n");
            summary.Append("2. Texel density (px per metre):");
            foreach (var g in rows.GroupBy(x => x.group))
            {
                var sorted = g.Select(x => x.density).OrderBy(x => x).ToList();
                float median = sorted[sorted.Count / 2];
                int low = 0, high = 0;
                foreach (var row in g)
                {
                    float ratio = row.density / median;
                    if (ratio < 0.5f) low++;
                    if (ratio > 2f) high++;
                    csv.Append($"\"{row.path}\",{row.group},{row.texture},{row.density:0},{ratio:0.00}\n");
                }
                summary.Append($" {g.Key}: {g.Count()} renderers, median {median:0}, range {sorted[0]:0}–{sorted[^1]:0}, " +
                               $"{low} under half and {high} over double the median.");
            }
            summary.Append('\n');
            File.WriteAllText(Path.Combine(folder, "texel_density.csv"), csv.ToString());
        }

        // ---------- 3. light probes ----------

        private static void Probes(string folder, StringBuilder summary)
        {
            var probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0)
            {
                summary.Append("3. Light probes: no baked probes found (bake lighting first).\n");
                return;
            }

            var positions = probes.positions;
            var sh = probes.bakedProbes;
            var brightness = new float[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                brightness[i] = (sh[i][0, 0] + sh[i][1, 0] + sh[i][2, 0]) / 3f;   // L0 (ambient) term

            var probeCsv = new StringBuilder("x,y,z,brightness,neighbour_median,suspect_inside_geometry\n");
            int dark = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                var neighbours = new List<float>();
                for (int j = 0; j < positions.Length; j++)
                    if (j != i && (positions[j] - positions[i]).sqrMagnitude < 9f) neighbours.Add(brightness[j]);
                float median = neighbours.Count > 0 ? neighbours.OrderBy(x => x).ElementAt(neighbours.Count / 2) : brightness[i];
                bool suspect = neighbours.Count >= 3 && median > 0.01f && brightness[i] < median * DarkRatio;
                if (suspect) dark++;
                var p = positions[i];
                probeCsv.Append($"{p.x:0.00},{p.y:0.00},{p.z:0.00},{brightness[i]:0.0000},{median:0.0000},{(suspect ? "YES" : "")}\n");
            }
            File.WriteAllText(Path.Combine(folder, "light_probes.csv"), probeCsv.ToString());

            // Walkable sample points from the NavMesh, 1 m above the floor.
            var nav = NavMesh.CalculateTriangulation();
            var samples = new List<Vector3>();
            for (int t = 0; t < nav.indices.Length; t += 3)
            {
                Vector3 a = nav.vertices[nav.indices[t]], b = nav.vertices[nav.indices[t + 1]], c = nav.vertices[nav.indices[t + 2]];
                foreach (var s in new[] { a, b, c, (a + b + c) / 3f, (a + b) * 0.5f, (b + c) * 0.5f, (c + a) * 0.5f })
                    samples.Add(s + Vector3.up);
            }

            var gapCsv = new StringBuilder("x,y,z,nearest_probe_m\n");
            float worst = 0f;
            int gaps = 0;
            foreach (var s in samples)
            {
                float nearest = float.MaxValue;
                foreach (var p in positions) nearest = Mathf.Min(nearest, (p - s).sqrMagnitude);
                nearest = Mathf.Sqrt(nearest);
                worst = Mathf.Max(worst, nearest);
                if (nearest > ProbeGapLimit)
                {
                    gaps++;
                    gapCsv.Append($"{s.x:0.0},{s.y:0.0},{s.z:0.0},{nearest:0.00}\n");
                }
            }
            File.WriteAllText(Path.Combine(folder, "probe_gaps.csv"), gapCsv.ToString());

            summary.Append($"3. Light probes: {positions.Length} probes, {dark} suspected inside geometry (much darker than " +
                           $"all neighbours). {samples.Count} walkable points checked: {gaps} further than {ProbeGapLimit} m " +
                           $"from any probe, worst {worst:0.0} m.\n");
        }

        // ---------- 4. lightmap UV overlap ----------

        private struct Overlap { public MeshRenderer renderer; public string path; public int texels, shared; public string note; }

        private static void Overlaps(GameObject environment, string folder, StringBuilder summary)
        {
            var found = FindOverlaps(environment);
            if (found == null)
            {
                summary.Append("4. Lightmap UV overlap: no baked lightmaps found (bake lighting first).\n");
                return;
            }
            var csv = new StringBuilder("object,group,lightmap_texels,shared_texels,shared_percent,note\n");
            var flagged = new List<Overlap>();
            foreach (var o in found.OrderByDescending(x => x.shared).ThenBy(x => x.note))
            {
                csv.Append($"\"{o.path}\",{GroupOf(o.path)},{o.texels},{o.shared},{100f * o.shared / Mathf.Max(1, o.texels):0.0},{o.note}\n");
                if (o.shared > 0) flagged.Add(o);
            }
            int unchecked_ = found.Count(o => o.note != "");
            found = found.Where(o => o.note == "").ToList();
            File.WriteAllText(Path.Combine(folder, "lightmap_overlap.csv"), csv.ToString());
            int props = flagged.Count(o => GroupOf(o.path) == "Props");
            var names = flagged.Select(o => o.path.Substring(o.path.LastIndexOf('/') + 1)).Distinct().Take(10);
            summary.Append($"4. Lightmap UV overlap: {found.Count} lightmapped renderers checked, {flagged.Count} with UV charts " +
                           $"sharing lightmap texels ({props} props, {flagged.Count - props} other)" +
                           (flagged.Count > 0 ? $": {string.Join(", ", names)}" : "") +
                           (unchecked_ > 0 ? $". {unchecked_} not checked (reason in the CSV note column)" : "") + ".\n");
        }

        [MenuItem("Tools/World Builder/Light Overlapping Props With Probes")]
        public static void ProbeLightOverlappingProps()
        {
            var environment = GameObject.Find("Environment");
            var found = environment != null ? FindOverlaps(environment) : null;
            if (found == null)
            {
                EditorUtility.DisplayDialog("Quality Report", "Open Main.unity and bake lighting first.", "OK");
                return;
            }
            // Under 1 % (a few texels on a large prop, e.g. the specimen tanks) is invisible; those keep the lightmap.
            var props = found.Where(o => GroupOf(o.path) == "Props" && 100f * o.shared / Mathf.Max(1, o.texels) >= FixOverlapPercent).ToList();
            if (props.Count == 0)
            {
                EditorUtility.DisplayDialog("Quality Report", "No props with overlapping lightmap UVs.", "OK");
                return;
            }
            foreach (var o in props)
            {
                Undo.RecordObject(o.renderer, "Light Overlapping Props With Probes");
                o.renderer.receiveGI = ReceiveGI.LightProbes;
                o.renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(environment.scene);
            var list = string.Join("\n", props.Select(o => o.path.Replace("Environment/", "")));
            Debug.Log($"[QualityReport] {props.Count} props now receive light from probes:\n{list}");
            EditorUtility.DisplayDialog("Quality Report",
                $"{props.Count} props now receive light from the light probes (listed in the Console).\n\n" +
                "Save the scene and bake lighting again (Window > Rendering > Lighting > Generate Lighting). " +
                "Ctrl+Z undoes it before saving.", "OK");
        }

        private static List<Overlap> FindOverlaps(GameObject environment)
        {
            var maps = LightmapSettings.lightmaps;
            if (maps == null || maps.Length == 0) return null;
            var results = new List<Overlap>();
            foreach (var r in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.ContributeGI)) continue;
                if (r.receiveGI != ReceiveGI.Lightmaps || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                string path = PathOf(r.transform);
                if (r.lightmapIndex < 0 || r.lightmapIndex >= maps.Length)
                {
                    results.Add(new Overlap { renderer = r, path = path, note = $"not in a baked lightmap (index {r.lightmapIndex})" });
                    continue;
                }
                var map = maps[r.lightmapIndex].lightmapColor;
                if (map == null) { results.Add(new Overlap { renderer = r, path = path, note = "lightmap texture missing" }); continue; }

                Vector2[] uv;
                int[] triangles;
                try
                {
                    uv = mesh.uv2;
                    if (uv == null || uv.Length == 0) uv = mesh.uv;   // Unity falls back to the texture UVs
                    triangles = mesh.triangles;
                }
                catch { results.Add(new Overlap { renderer = r, path = path, note = "mesh data not readable" }); continue; }
                if (uv.Length == 0) { results.Add(new Overlap { renderer = r, path = path, note = "no UVs" }); continue; }

                // Lightmap UVs -> texel coordinates in this renderer's lightmap.
                var so = r.lightmapScaleOffset;
                var p = new Vector2[uv.Length];
                for (int i = 0; i < uv.Length; i++)
                    p[i] = new Vector2((uv[i].x * so.x + so.z) * map.width, (uv[i].y * so.y + so.w) * map.height);

                // UV charts: triangles joined by shared vertices or identical lightmap UVs.
                var parent = new int[uv.Length];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;
                var byUv = new Dictionary<(long, long), int>();
                for (int i = 0; i < uv.Length; i++)
                {
                    var key = ((long)System.Math.Round(uv[i].x * 1e5), (long)System.Math.Round(uv[i].y * 1e5));
                    if (byUv.TryGetValue(key, out int first)) Union(parent, i, first);
                    else byUv[key] = i;
                }
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Union(parent, triangles[t], triangles[t + 1]);
                    Union(parent, triangles[t + 1], triangles[t + 2]);
                }

                // Mark every texel each triangle touches with its chart; a texel touched by two charts is shared.
                var owner = new Dictionary<long, int>();
                var shared = new HashSet<long>();
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector2 a = p[triangles[t]], b = p[triangles[t + 1]], c = p[triangles[t + 2]];
                    if (Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) < 1e-6f) continue;   // degenerate
                    int chart = Find(parent, triangles[t]);
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                    int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), y1 = Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!TouchesTexel(a, b, c, x, y)) continue;
                        long texel = (long)y * map.width + x;
                        if (owner.TryGetValue(texel, out int other)) { if (other != chart) shared.Add(texel); }
                        else owner[texel] = chart;
                    }
                }
                results.Add(new Overlap { renderer = r, path = path, texels = owner.Count, shared = shared.Count, note = "" });
            }
            return results;
        }

        // Separating-axis test: does triangle abc overlap the inside of texel square (x, y)–(x+1, y+1)?
        // The loop over the triangle's bounding box covers the square's own axes; this checks the three edge normals.
        private static bool TouchesTexel(Vector2 a, Vector2 b, Vector2 c, int x, int y)
        {
            return EdgeAllows(a, b, c, x, y) && EdgeAllows(b, c, a, x, y) && EdgeAllows(c, a, b, x, y);
        }

        private static bool EdgeAllows(Vector2 p, Vector2 q, Vector2 other, int x, int y)
        {
            var n = new Vector2(p.y - q.y, q.x - p.x);
            float t0 = n.x * p.x + n.y * p.y, t1 = n.x * other.x + n.y * other.y;
            float s0 = n.x * x + n.y * y;
            float sMin = s0 + Mathf.Min(0f, n.x) + Mathf.Min(0f, n.y), sMax = s0 + Mathf.Max(0f, n.x) + Mathf.Max(0f, n.y);
            float eps = 1e-4f * (Mathf.Abs(n.x) + Mathf.Abs(n.y));   // touching an edge or corner only does not count
            return sMax > Mathf.Min(t0, t1) + eps && sMin < Mathf.Max(t0, t1) - eps;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a != b) parent[a] = b;
        }

        private static string GroupOf(string path) =>
            path.Contains("/Props/") ? "Props" : path.Contains("/Art/") || path.Contains("/Ceiling/") ? "Structure" : "Other";

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}

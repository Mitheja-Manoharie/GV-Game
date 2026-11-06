using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: sets the import size of the Essentials prop textures from the texel-density check
    /// (Tools > World Builder > Quality Report) and records the memory before and after.
    ///
    /// The walls and floors sit at about 490 texture pixels per metre in every room. The small pickups and the crates
    /// were far below that, so the budget gives colour detail where it is seen and saves memory where it is not:
    ///   - T_Props_Batch2 (ammo boxes, health packs): BaseColor back to 2048 px (≈ 320 px/m; at 1024 px it was
    ///     ≈ 160 px/m, a third of the walls, and blurry when the player picks them up). ORM and Emissive stay at
    ///     1024 px: roughness/metal and glow at half resolution are hard to see on props this small.
    ///   - T_Props_Crates (cover crates, 1–1.5 m, seen up close): BaseColor from 2048 to its full 4096 px
    ///     (≈ 210–250 → ≈ 420–500 px/m, matching the walls). Its normal and ORM maps stay at 2048 px.
    /// Memory is the compressed size Unity reports for each imported texture (with mipmaps).
    /// The table goes to the Console and to Docs/Performance/textures.csv.
    /// "Restore" returns every texture here to 2048 px, the kit's import default.
    /// </summary>
    public static class TextureBudgetTool
    {
        private const string Folder = "Assets/ThirdParty/Quaternius/Essentials/Textures/";

        private static readonly (string file, int size)[] Budget =
        {
            ("T_Props_Batch2_BaseColor.png", 2048),
            ("T_Props_Batch2_Emissive.png", 1024),
            ("T_Props_Batch2_ORM.png", 1024),
            ("T_Props_Batch2_Normal.png", 1024),
            ("T_Props_Crates_BaseColor.png", 4096),
            ("T_Props_Crates_Normal.png", 2048),
            ("T_Props_Crates_ORM.png", 2048),
        };

        [MenuItem("Tools/World Builder/Apply Prop Texture Budget")]
        public static void ApplyBudget() => Apply(false);

        [MenuItem("Tools/World Builder/Restore Prop Textures To 2048")]
        public static void Restore() => Apply(true);

        private static void Apply(bool restore)
        {
            var csv = new StringBuilder("texture,source_px,before_px,before_kb,after_px,after_kb\n");
            long totalBefore = 0, totalAfter = 0;

            foreach (var (file, size) in Budget)
            {
                string path = Folder + file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var before = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (importer == null || before == null)
                {
                    Debug.LogWarning($"[TextureBudgetTool] Not found: {path}");
                    continue;
                }

                importer.GetSourceTextureWidthAndHeight(out int sourceW, out _);
                int beforePx = before.width;
                long beforeBytes = StorageSize(before);

                int target = restore ? 2048 : size;
                if (importer.maxTextureSize != target)
                {
                    importer.maxTextureSize = target;
                    foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                    {
                        var settings = importer.GetPlatformTextureSettings(platform);
                        if (!settings.overridden) continue;
                        settings.maxTextureSize = target;
                        importer.SetPlatformTextureSettings(settings);
                    }
                    importer.SaveAndReimport();
                }

                var after = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                long afterBytes = StorageSize(after);
                totalBefore += beforeBytes;
                totalAfter += afterBytes;
                csv.Append($"{Path.GetFileNameWithoutExtension(path)},{sourceW},{beforePx},{beforeBytes / 1024},{after.width},{afterBytes / 1024}\n");
            }

            csv.Append($"TOTAL,,,{totalBefore / 1024},,{totalAfter / 1024}\n");
            string output = Path.Combine(Application.dataPath, "..", "Docs", "Performance", "textures.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, csv.ToString());
            Debug.Log($"[TextureBudgetTool] {(restore ? "Restored to 2048" : "Budget applied")}: {totalBefore / 1024} KB -> " +
                      $"{totalAfter / 1024} KB. Saved to {output}\n{csv}");
        }

        /// <summary>The compressed size shown in the texture Inspector (UnityEditor.TextureUtil is internal).</summary>
        private static long StorageSize(Texture texture)
        {
            var util = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil");
            var method = util?.GetMethod("GetStorageMemorySizeLong", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null) return (long)method.Invoke(null, new object[] { texture });
            return UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
        }
    }
}

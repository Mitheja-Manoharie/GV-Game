using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: caps the textures of the small Essentials props at 1024 px and records the memory saved.
    ///
    /// T_Props_Batch2 (ammo boxes, health packs, chest, grenade, mine) is authored at 2048 px, but these props are
    /// under 0.6 m and never fill more than a small part of the screen, so 1024 px gives the same on-screen detail.
    /// The MegaKit wall and trim sheets and the crates stay at 2048 px because the player sees them up close.
    /// Memory is the compressed size Unity reports for the imported texture (with mipmaps), before and after.
    /// The table goes to the Console and to Docs/Performance/textures.csv. "Restore" puts them back to 2048.
    /// </summary>
    public static class TextureBudgetTool
    {
        private static readonly string[] Textures =
        {
            "Assets/ThirdParty/Quaternius/Essentials/Textures/T_Props_Batch2_BaseColor.png",
            "Assets/ThirdParty/Quaternius/Essentials/Textures/T_Props_Batch2_Emissive.png",
            "Assets/ThirdParty/Quaternius/Essentials/Textures/T_Props_Batch2_ORM.png",
            "Assets/ThirdParty/Quaternius/Essentials/Textures/T_Props_Batch2_Normal.png",
        };

        [MenuItem("Tools/World Builder/Cap Small-Prop Textures At 1024")]
        public static void Cap() => Apply(1024);

        [MenuItem("Tools/World Builder/Restore Small-Prop Textures To 2048")]
        public static void Restore() => Apply(2048);

        private static void Apply(int size)
        {
            var csv = new StringBuilder("texture,source_px,before_px,before_kb,after_px,after_kb\n");
            long totalBefore = 0, totalAfter = 0;

            foreach (string path in Textures)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var before = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (importer == null || before == null)
                {
                    Debug.LogWarning($"[TextureBudgetTool] Not found: {path}");
                    continue;
                }

                importer.GetSourceTextureWidthAndHeight(out int sourceW, out int sourceH);
                int beforePx = before.width;
                long beforeBytes = StorageSize(before);

                int target = size;
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
            string file = Path.Combine(Application.dataPath, "..", "Docs", "Performance", "textures.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, csv.ToString());
            Debug.Log($"[TextureBudgetTool] Max size {size}: {totalBefore / 1024} KB -> {totalAfter / 1024} KB " +
                      $"(saved {(totalBefore - totalAfter) / 1024} KB). Saved to {file}\n{csv}");
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

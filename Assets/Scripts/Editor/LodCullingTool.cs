using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: distance culling for small props with LOD Groups.
    ///
    /// Every prop under Environment/*/Props whose largest dimension is under 0.8 m (mugs, syringes, ammo boxes, health
    /// packs and similar) gets a LODGroup with a single level that is culled when the prop's screen height falls below
    /// the value that matches 25 m away with the player camera's field of view:
    ///     cull height = size / (2 · 25 m · tan(FOV / 2))
    /// At that distance such a prop covers only a few pixels, so removing it is invisible but saves its draw call and
    /// triangles. The keycard is skipped (it must stay findable). "Remove LOD Culling" takes the groups off again.
    /// Both are undoable.
    /// </summary>
    public static class LodCullingTool
    {
        private const float MaxSize = 0.8f;        // metres: only small props
        private const float CullDistance = 25f;    // metres
        private const float FallbackFov = 75f;     // the player camera's vertical field of view

        [MenuItem("Tools/World Builder/Add LOD Culling To Small Props")]
        public static void Add()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                EditorUtility.DisplayDialog("LOD Culling", "Open Main.unity first: no 'Environment' object found.", "OK");
                return;
            }

            float fov = Camera.main != null ? Camera.main.fieldOfView : FallbackFov;
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Undo.SetCurrentGroupName("Add LOD Culling");

            int added = 0, skipped = 0;
            foreach (var prop in SmallProps(environment))
            {
                if (prop.GetComponent<LODGroup>() != null || prop.GetComponent("KeycardPickup") != null) { skipped++; continue; }

                var renderers = prop.GetComponentsInChildren<Renderer>(true);
                var group = Undo.AddComponent<LODGroup>(prop);
                group.fadeMode = LODFadeMode.None;
                group.SetLODs(new[] { new LOD(1f, renderers) });
                group.RecalculateBounds();
                float cull = Mathf.Clamp(group.size / (2f * CullDistance * tanHalf), 0.001f, 0.5f);
                group.SetLODs(new[] { new LOD(cull, renderers) });
                added++;
            }

            EditorSceneManager.MarkSceneDirty(environment.scene);
            Debug.Log($"[LodCullingTool] Added LOD culling (cull beyond about {CullDistance} m) to {added} small props; {skipped} skipped. FOV {fov:0}°.");
        }

        [MenuItem("Tools/World Builder/Remove LOD Culling From Small Props")]
        public static void Remove()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null) return;
            Undo.SetCurrentGroupName("Remove LOD Culling");
            int removed = 0;
            foreach (var prop in SmallProps(environment))
            {
                var group = prop.GetComponent<LODGroup>();
                if (group != null) { Undo.DestroyObjectImmediate(group); removed++; }
            }
            EditorSceneManager.MarkSceneDirty(environment.scene);
            Debug.Log($"[LodCullingTool] Removed LOD culling from {removed} props.");
        }

        /// <summary>Outermost objects under any Environment/*/Props group whose renderers fit in a MaxSize cube.</summary>
        private static IEnumerable<GameObject> SmallProps(GameObject environment)
        {
            foreach (Transform room in environment.transform)
            {
                var props = room.Find("Props");
                if (props == null) continue;
                foreach (Transform category in props)
                    foreach (Transform prop in category)
                    {
                        var renderers = prop.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length == 0) continue;
                        var bounds = renderers[0].bounds;
                        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                        if (Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) < MaxSize) yield return prop.gameObject;
                    }
            }
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: environmental storytelling in the research lab — the scientists left in a hurry.
    /// A chair knocked onto its side in front of the specimen tanks, syringes dropped on the floor and two left
    /// lying on the desk.
    ///
    /// New objects go under Environment/ResearchLab/Props/Story. They are not static, cast no shadows and use light
    /// probes, so no rebake is needed. "Remove Lab Story" deletes the group (both undoable).
    /// </summary>
    public static class LabStoryPlacer
    {
        private const string Essentials = "Assets/ThirdParty/Quaternius/Essentials/FBX (Unity)/";
        private const float DeskTop = 0.94f;

        [MenuItem("Tools/World Builder/Place Lab Story")]
        public static void Place()
        {
            var lab = GameObject.Find("Environment/ResearchLab/Props");
            if (lab == null)
            {
                EditorUtility.DisplayDialog("Place Lab Story", "Open Main.unity first: Environment/ResearchLab/Props not found.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Place Lab Story");
            RemoveGroup(lab.transform);

            var root = new GameObject("Story");
            Undo.RegisterCreatedObjectUndo(root, "Place Lab Story");
            root.transform.SetParent(lab.transform, false);

            var mess = new GameObject("Mess");
            mess.transform.SetParent(root.transform, false);

            var chair = Model(mess.transform, "Prop_Chair", new Vector3(16.1f, 0f, 29.0f), Quaternion.Euler(0f, 35f, 90f));   // on its side, in front of the tanks
            if (chair != null) AddBoxCollider(chair);
            Model(mess.transform, "Prop_Syringe", new Vector3(15.3f, 0f, 26.3f), Quaternion.Euler(0f, 20f, 90f));             // lying flat
            Model(mess.transform, "Prop_Syringe", new Vector3(17.4f, 0f, 27.6f), Quaternion.Euler(0f, 110f, 90f));
            Model(mess.transform, "Prop_Syringe", new Vector3(13.9f, 0f, 25.5f), Quaternion.Euler(0f, 250f, 90f));

            // On the desk (Prop_Desk_Medium at (16, 27.2): top at 0.94 m, 2.84 x 1.03 m), clear of the two mugs.
            Model(mess.transform, "Prop_Syringe", new Vector3(15.95f, 0f, 27.35f), Quaternion.Euler(0f, 10f, 90f), DeskTop);
            Model(mess.transform, "Prop_Syringe", new Vector3(16.3f, 0f, 26.9f), Quaternion.Euler(0f, 165f, 90f), DeskTop);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[LabStoryPlacer] Research lab story placed under Environment/ResearchLab/Props/Story.");
        }

        [MenuItem("Tools/World Builder/Remove Lab Story")]
        public static void Remove()
        {
            var lab = GameObject.Find("Environment/ResearchLab/Props");
            if (lab == null) return;
            Undo.SetCurrentGroupName("Remove Lab Story");
            RemoveGroup(lab.transform);
            EditorSceneManager.MarkSceneDirty(lab.scene);
            Debug.Log("[LabStoryPlacer] Research lab story removed.");
        }

        private static void RemoveGroup(Transform lab)
        {
            var old = lab.Find("Story");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        /// <summary>Places a model, then lowers or raises it so its lowest point rests on a surface (the floor by default).</summary>
        private static GameObject Model(Transform parent, string model, Vector3 position, Quaternion rotation, float surfaceY = 0f)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Essentials + model + ".fbx");
            if (asset == null)
            {
                Debug.LogWarning($"[LabStoryPlacer] Missing model {model}.");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation * asset.transform.rotation);

            bool any = false;
            var bounds = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (any) go.transform.position += Vector3.up * (surfaceY + 0.002f - bounds.min.y);

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            return go;
        }

        /// <summary>A box collider fitted to the model's meshes in its own local space, so rotation does not inflate it.</summary>
        private static void AddBoxCollider(GameObject go)
        {
            bool any = false;
            var local = new Bounds();
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = go.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any) return;
            var box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
        }
    }
}

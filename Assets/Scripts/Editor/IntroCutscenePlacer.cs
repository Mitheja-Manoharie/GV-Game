using GVGame.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: adds the intro cutscene (IntroCutscene) to the open scene as a root object
    /// "IntroCutscene", and selects it so its camera paths show in the Scene view. "Remove Intro Cutscene" deletes it.
    /// Both are undoable. The cutscene needs no rebake: it only moves the player's camera and draws on screen.
    /// </summary>
    public static class IntroCutscenePlacer
    {
        private const string Name = "IntroCutscene";

        [MenuItem("Tools/World Builder/Add Intro Cutscene")]
        public static void Add()
        {
            var existing = Object.FindAnyObjectByType<IntroCutscene>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog("Intro Cutscene", "The scene already has an intro cutscene (selected).", "OK");
                return;
            }

            var go = new GameObject(Name);
            Undo.RegisterCreatedObjectUndo(go, "Add Intro Cutscene");
            Undo.AddComponent<IntroCutscene>(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[IntroCutscenePlacer] Added IntroCutscene. Press Play to watch it; Space skips it.");
        }

        [MenuItem("Tools/World Builder/Remove Intro Cutscene")]
        public static void Remove()
        {
            var existing = Object.FindAnyObjectByType<IntroCutscene>();
            if (existing == null)
            {
                EditorUtility.DisplayDialog("Intro Cutscene", "No intro cutscene in the open scene.", "OK");
                return;
            }

            var scene = existing.gameObject.scene;
            if (existing.gameObject.name == Name && existing.gameObject.GetComponents<Component>().Length == 2)
                Undo.DestroyObjectImmediate(existing.gameObject);
            else
                Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}

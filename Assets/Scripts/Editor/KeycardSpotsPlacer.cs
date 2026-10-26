using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GVGame.EditorTools
{
    /// <summary>
    /// World Builder tool: creates the KeycardSpot_* markers under Environment/KeycardSpots. Each run the
    /// KeycardPickup moves to one of them at random. The keycard model stands upright with its pivot about 0.15 m
    /// above its base, so each marker sits 0.17 m above the surface it rests on.
    ///
    /// Every spot is on the way from the start before any lockable door (service corridor, lab corridor before
    /// Door_LabSouth, maintenance bay, infirmary before Door_InfirmaryNorth), so the Facility AI can never lock the
    /// player away from the keycard. Running the tool again rebuilds the markers (undoable).
    /// </summary>
    public static class KeycardSpotsPlacer
    {
        private static readonly (string name, Vector3 position)[] Spots =
        {
            ("KeycardSpot_Infirmary",        new Vector3(-14.1f, 0.94f + 0.17f, 13.35f)),   // on the desk
            ("KeycardSpot_Service_Corridor", new Vector3(13.06f, 1.0f + 0.17f, 7.0f)),      // on a 1 m crate
            ("KeycardSpot_Lab_Corridor",     new Vector3(13.06f, 1.0f + 0.17f, 19.0f)),     // on a 1 m crate
            ("KeycardSpot_Maintenance_Bay",  new Vector3(-1.6f, 1.5f + 0.17f, 9.4f)),       // on the large crate
        };

        [MenuItem("Tools/World Builder/Place Keycard Spots")]
        public static void Place()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                EditorUtility.DisplayDialog("Place Keycard Spots", "Open Main.unity first: no 'Environment' object found.", "OK");
                return;
            }

            var old = environment.transform.Find("KeycardSpots");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("KeycardSpots");
            Undo.RegisterCreatedObjectUndo(root, "Place Keycard Spots");
            root.transform.SetParent(environment.transform, false);

            foreach (var (name, position) in Spots)
            {
                var spot = new GameObject(name);
                spot.transform.SetParent(root.transform, false);
                spot.transform.position = position;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[KeycardSpotsPlacer] Placed {Spots.Length} keycard spots under Environment/KeycardSpots.");
        }
    }
}

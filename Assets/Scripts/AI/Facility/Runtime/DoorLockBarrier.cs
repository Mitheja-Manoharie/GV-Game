using UnityEngine;
using UnityEngine.AI;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// Creates the object that represents a locked door: a carving NavMeshObstacle so enemies reroute,
    /// and optionally a visible, solid red barrier (used when there is no real door to lock).
    /// The barrier spans the door marker's local X axis, like the Systems Engineer's doors.
    /// </summary>
    public static class DoorLockBarrier
    {
        public const string ObjectName = "FacilityLockBarrier";
        private static readonly Vector3 DoorwaySize = new Vector3(4f, 4f, 0.4f);
        private static Material defaultMaterial;

        public static GameObject Create(Transform doorMarker, bool solid, Material material = null)
        {
            var root = new GameObject(ObjectName);
            root.transform.SetParent(doorMarker, false);
            root.transform.localPosition = new Vector3(0f, DoorwaySize.y * 0.5f, 0f);

            int doorsLayer = LayerMask.NameToLayer("Doors");
            if (doorsLayer >= 0) root.layer = doorsLayer;

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = DoorwaySize;
            obstacle.carving = true;

            if (solid)
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Barrier";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = new Vector3(DoorwaySize.x, DoorwaySize.y, 0.3f);
                if (doorsLayer >= 0) visual.layer = doorsLayer;
                visual.GetComponent<Renderer>().sharedMaterial = material != null ? material : DefaultMaterial();
            }

            root.SetActive(false);
            return root;
        }

        private static Material DefaultMaterial()
        {
            if (defaultMaterial != null) return defaultMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            defaultMaterial = new Material(shader) { name = "FacilityLockBarrier", color = new Color(0.85f, 0.1f, 0.1f) };
            return defaultMaterial;
        }
    }
}

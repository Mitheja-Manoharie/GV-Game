using UnityEngine;

namespace GVGame.Agents
{
    /// <summary>
    /// Tints a robot's body so the robot types are told apart in play: the Security Hunter and the Tactical Flanker
    /// share the Android model. Multiplies the material's base colour through a property block, so the shared
    /// Android material is not changed. Added to Robot_Hunter by GV Game > Agent Setup (step 4, or 9 on its own).
    /// </summary>
    public class RobotTint : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("The body's renderers. Every SkinnedMeshRenderer under this object if empty (the rifle is a MeshRenderer).")]
        public Renderer[] renderers = new Renderer[0];
        public Color tint = new Color(0.45f, 0.62f, 1f);

        void Start()
        {
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var block = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                for (int i = 0; i < r.sharedMaterials.Length; i++)
                {
                    var material = r.sharedMaterials[i];
                    if (material == null || !material.HasProperty(BaseColorId)) continue;
                    r.GetPropertyBlock(block, i);
                    block.SetColor(BaseColorId, material.GetColor(BaseColorId) * tint);
                    r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}

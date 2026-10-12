using System.Collections.Generic;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>
    /// What the Android Stalker senses at a decision (the "S" in PEAS). Filled in by the Unity runner
    /// from the scene; built by hand in unit tests, so the brain never touches a GameObject.
    /// Every optional input has a safe default, so a missing sensor can never cause a null error.
    /// </summary>
    public sealed class StalkerPercepts
    {
        /// <summary>Game time in seconds.</summary>
        public double Time;

        /// <summary>Graph node the Stalker is standing on.</summary>
        public int SelfNode;

        /// <summary>Graph node the player is standing on.</summary>
        public int PlayerNode;

        /// <summary>Straight-line distance to the player in metres. Used for the strike range check.</summary>
        public float DistanceToPlayerMetres = float.PositiveInfinity;

        /// <summary>Player health, 0 (dead) to 1 (full).</summary>
        public float PlayerHealth01 = 1f;

        /// <summary>True when the player has a clear line of sight to the Stalker right now.</summary>
        public bool PlayerSeesMe;

        /// <summary>
        /// Light level of each graph node, indexed by node id: 0 = pitch dark, 1 = fully lit.
        /// Left null when no light data is available; <see cref="LightAt"/> then returns a neutral value.
        /// </summary>
        public float[] LightByNode;

        /// <summary>Graph nodes the player currently has line of sight to.</summary>
        public ICollection<int> PlayerVisibleNodes = new HashSet<int>();

        /// <summary>Doors that are locked right now (read from the door system, the source of truth).</summary>
        public ICollection<string> LockedDoors = new HashSet<string>();

        /// <summary>The node the player is heading for when healthy, or -1 if unknown.</summary>
        public int ExitNode = -1;

        /// <summary>Nodes where the player can heal (e.g. the infirmary). The player heads here when hurt.</summary>
        public ICollection<int> HealNodes = new HashSet<int>();

        /// <summary>Light level used when no light data exists for a node: neither dark nor lit.</summary>
        public const float NeutralLight = 0.5f;

        /// <summary>Light level of a node, clamped to 0..1. Returns <see cref="NeutralLight"/> if the node has no data.</summary>
        public float LightAt(int nodeId)
        {
            if (LightByNode == null || nodeId < 0 || nodeId >= LightByNode.Length) return NeutralLight;

            float light = LightByNode[nodeId];
            if (light < 0f) return 0f;
            if (light > 1f) return 1f;
            return light;
        }

        /// <summary>True if the player can see the given node right now.</summary>
        public bool IsSeenByPlayer(int nodeId) => PlayerVisibleNodes != null && PlayerVisibleNodes.Contains(nodeId);
    }
}

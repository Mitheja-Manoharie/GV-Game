using System.Collections.Generic;

namespace LabLockdown.AI.Flanker
{
    /// <summary>
    /// What one Tactical Flanker senses when it plans (the "S" in PEAS). Filled in by the Unity agent from
    /// the scene; built by hand in unit tests.
    /// </summary>
    public sealed class FlankerPercepts
    {
        /// <summary>Graph node the flanker is standing on.</summary>
        public int AgentNode;

        /// <summary>Flanker health, 0 (dead) to 1 (full).</summary>
        public float AgentHealth01 = 1f;

        /// <summary>Graph node the player is standing on.</summary>
        public int PlayerNode;

        /// <summary>Player position on the ground plane (metres). The view cone is measured from here.</summary>
        public float PlayerX, PlayerZ;

        /// <summary>
        /// Direction the player is looking, on the ground plane (need not be normalised).
        /// Zero means unknown: every visible tile is then treated as watched, the cautious assumption.
        /// </summary>
        public float PlayerForwardX, PlayerForwardZ;

        /// <summary>Nodes the player has line of sight to (eyes to tile centre), whichever way the player faces.</summary>
        public ICollection<int> VisibleNodes = new HashSet<int>();

        /// <summary>Doors that are locked right now (from the Facility AI).</summary>
        public ICollection<string> LockedDoors = new HashSet<string>();

        /// <summary>A copy, so a decision keeps the percepts it was made from even if the caller reuses this object.</summary>
        public FlankerPercepts Clone() => new FlankerPercepts
        {
            AgentNode = AgentNode,
            AgentHealth01 = AgentHealth01,
            PlayerNode = PlayerNode,
            PlayerX = PlayerX,
            PlayerZ = PlayerZ,
            PlayerForwardX = PlayerForwardX,
            PlayerForwardZ = PlayerForwardZ,
            VisibleNodes = new HashSet<int>(VisibleNodes ?? new int[0]),
            LockedDoors = new HashSet<string>(LockedDoors ?? new string[0]),
        };
    }
}

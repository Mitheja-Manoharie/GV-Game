namespace LabLockdown.AI.Hunter
{
    public enum HunterState { Patrol, Investigate, Chase, Attack, Dead }

    /// <summary>How fast to walk. The Unity side maps these to the shared follower's speeds.</summary>
    public enum HunterGait { Patrol, Investigate, Chase }

    /// <summary>What the Hunter's sensors report this tick. The player's position is only valid when HasLineOfSight.</summary>
    public struct HunterPercepts
    {
        public float Now;
        public int HunterNode;
        public bool IsAlive;
        public bool HasLineOfSight;
        public int PlayerNode;
        public float PlayerDistance;
    }

    /// <summary>What the Hunter wants to do this tick.</summary>
    public struct HunterCommand
    {
        public HunterState State;
        public HunterGait Gait;
        /// <summary>Node to walk to, or -1 for none.</summary>
        public int TargetNode;
        /// <summary>True on the tick the target changed: the Unity side plans a new path.</summary>
        public bool TargetChanged;
        /// <summary>True when the Hunter should stand still, face the player and shoot (or is dead).</summary>
        public bool HoldPosition;
        public AttackResult Attack;
    }
}

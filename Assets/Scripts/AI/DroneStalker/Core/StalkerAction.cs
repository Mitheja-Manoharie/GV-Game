namespace LabLockdown.AI.DroneStalker
{
    /// <summary>
    /// The tactics the Android Stalker chooses between (the "A" in PEAS).
    /// The utility scorer rates every action each decision and the brain commits to the best one.
    /// </summary>
    public enum StalkerAction
    {
        /// <summary>Follow the player at a distance, keeping to dark routes the player cannot see.</summary>
        Stalk,

        /// <summary>Break the player's line of sight and settle in the nearest dark, unseen node.</summary>
        Hide,

        /// <summary>Move to a dark, unseen node on the player's likely route and wait there.</summary>
        Ambush,

        /// <summary>Close in and attack: the player is close, has not noticed the Stalker, and is within range.</summary>
        Strike,

        /// <summary>Pull back out of sight after being spotted or after a strike.</summary>
        Retreat,
    }
}

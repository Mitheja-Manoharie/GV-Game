using System;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>
    /// Every tunable number of the Drone Stalker in one place. [Serializable] with public fields so Unity shows
    /// it in the Inspector, while the class itself stays free of UnityEngine (pure C#, unit testable).
    /// Each weight is documented with the behaviour it controls, so it can be justified in the viva.
    /// </summary>
    [Serializable]
    public sealed class StalkerSettings
    {
        // ---------- Path cost: how the A* search sees the level ----------
        // The cost of entering a node = walking distance + a light penalty + a visibility penalty.
        // Penalties are never negative, so the straight-line heuristic stays admissible.

        /// <summary>Extra cost, in metres, for entering a fully lit node. A dark node adds nothing.</summary>
        public float LightCostWeight = 8f;

        /// <summary>Extra cost, in metres, for entering a node the player can currently see.</summary>
        public float SeenCostWeight = 16f;

        /// <summary>
        /// Multiplier on the A* heuristic. 1 keeps the search optimal; above 1 it is faster but may
        /// return a slightly longer route.
        /// </summary>
        public float HeuristicWeight = 1f;

        /// <summary>Node budget per search, so a decision can never stall a frame.</summary>
        public int SearchNodeBudget = 2000;

        // ---------- Distances (walking distance in metres along the level graph) ----------

        /// <summary>At or below this distance the player counts as "near" (full proximity score).</summary>
        public float NearDistanceMetres = 8f;

        /// <summary>At or above this distance the player counts as "far" (zero proximity score).</summary>
        public float FarDistanceMetres = 28f;

        /// <summary>Straight-line distance within which a strike can land.</summary>
        public float StrikeRangeMetres = 2.5f;

        // ---------- Memory (how long events keep influencing decisions) ----------

        /// <summary>How long "I was spotted" keeps pushing the Stalker towards hiding and retreating.</summary>
        public float SpottedMemorySeconds = 5f;

        /// <summary>How long "I just struck" keeps pushing the Stalker to retreat.</summary>
        public float StrikeRetreatSeconds = 6f;

        /// <summary>Minimum time between two strikes.</summary>
        public float StrikeCooldownSeconds = 12f;

        // ---------- Utility weights: Stalk ----------

        /// <summary>Baseline appeal of simply following the player.</summary>
        public float StalkBase = 0.35f;

        /// <summary>Added when the player is far away, so the Stalker closes the gap.</summary>
        public float StalkDistanceWeight = 0.25f;

        /// <summary>Subtracted while the player can see the Stalker (stalking in plain view is pointless).</summary>
        public float StalkSeenPenalty = 0.4f;

        // ---------- Utility weights: Hide ----------

        /// <summary>Added while the player can see the Stalker.</summary>
        public float HideSeenWeight = 0.55f;

        /// <summary>Added in proportion to how lit the Stalker's current node is.</summary>
        public float HideLitWeight = 0.25f;

        /// <summary>Added right after the Stalker was spotted, fading over the spotted memory.</summary>
        public float HideSpottedWeight = 0.2f;

        // ---------- Utility weights: Ambush ----------

        /// <summary>Added in proportion to how weak the player is (low health makes an ambush worthwhile).</summary>
        public float AmbushWeakWeight = 0.4f;

        /// <summary>Added in proportion to the quality of the best ambush spot (dark, unseen, on the player's route).</summary>
        public float AmbushSpotWeight = 0.35f;

        /// <summary>Added when the player is near, since an ambush only pays off close to the player.</summary>
        public float AmbushNearWeight = 0.15f;

        // ---------- Utility weights: Strike ----------
        // A strike is only possible when the player is within range, has not seen the Stalker, and the cooldown is over.

        /// <summary>Appeal of a strike once it is possible.</summary>
        public float StrikeBase = 0.45f;

        /// <summary>Added in proportion to how weak the player is.</summary>
        public float StrikeWeakWeight = 0.5f;

        // ---------- Utility weights: Retreat ----------

        /// <summary>Added right after a strike, fading over the strike retreat time.</summary>
        public float RetreatStrikeWeight = 0.9f;

        /// <summary>Added when recently spotted and the player is near, so a close call turns into a getaway.</summary>
        public float RetreatSpottedNearWeight = 0.6f;

        // ---------- Spot search (where to hide, ambush and retreat to) ----------

        /// <summary>A node at or below this light level counts as dark enough to hide or ambush in.</summary>
        public float DarkLightLevel = 0.35f;

        /// <summary>How far, in metres of walking, the Stalker looks for a place to hide.</summary>
        public float HideSearchMetres = 16f;

        /// <summary>How many nodes along the player's predicted route are considered for an ambush.</summary>
        public int AmbushLookaheadNodes = 4;

        /// <summary>A retreat must end at least this far from the player.</summary>
        public float RetreatMinDistanceMetres = 20f;

        // ---------- Sight (used by the sensor) and losing track ----------

        /// <summary>How far the drone can see the player, in metres. Night vision: light does not change it.</summary>
        public float SightRangeMetres = 20f;

        /// <summary>Width of the drone's field of view, in degrees.</summary>
        public float FieldOfViewDegrees = 120f;

        /// <summary>
        /// After losing sight of the player, keep acting on the last place it was seen for this long before patrolling.
        /// Stops the drone giving up every time a pillar or door frame blocks its view for a moment.
        /// </summary>
        public float LostSightGraceSeconds = 1.5f;

        // ---------- Patrol (no player in sight) ----------

        /// <summary>
        /// How strongly patrol prefers dark tiles. A tile's chance of being picked is proportional to
        /// 1 + PatrolDarkWeight * (1 - light), so with 3 a pitch-dark tile is four times as likely as a fully lit one.
        /// </summary>
        public float PatrolDarkWeight = 3f;

        /// <summary>Shortest patrol leg, in metres of walking, so the drone does not dither between neighbouring tiles.</summary>
        public float PatrolMinLegMetres = 8f;

        /// <summary>Longest patrol leg, in metres of walking.</summary>
        public float PatrolMaxLegMetres = 24f;

        /// <summary>How many recently visited tiles patrol avoids picking again.</summary>
        public int PatrolRecentTiles = 8;

        /// <summary>
        /// After losing the player, tiles that do not lead away from where the player was last seen keep only this
        /// fraction of their chance, so the drone patrols away from the closed door instead of waiting at it.
        /// </summary>
        public float PatrolTowardLastSeenFactor = 0.15f;

        /// <summary>Seed for the patrol's random choices. Give each drone its own seed so they spread out.</summary>
        public int PatrolSeed = 1;

        // ---------- Decision stability ----------

        /// <summary>How often the brain re-scores its actions.</summary>
        public float DecisionIntervalSeconds = 0.25f;

        /// <summary>How often the route is recomputed while following a path.</summary>
        public float ReplanIntervalSeconds = 0.5f;

        /// <summary>Minimum time before the brain may change its mind (stops flip-flopping between actions).</summary>
        public float ActionCommitSeconds = 1.5f;

        /// <summary>A new action must beat the current one by this much to replace it before the commit time is over.</summary>
        public float SwitchMargin = 0.1f;

        /// <summary>Clamps every value into a sensible range. Called by the brain before each decision.</summary>
        public void Validate()
        {
            LightCostWeight = Math.Max(0f, LightCostWeight);
            SeenCostWeight = Math.Max(0f, SeenCostWeight);
            HeuristicWeight = Math.Max(0f, HeuristicWeight);
            SearchNodeBudget = Math.Max(100, SearchNodeBudget);

            NearDistanceMetres = Math.Max(0f, NearDistanceMetres);
            FarDistanceMetres = Math.Max(NearDistanceMetres + 0.01f, FarDistanceMetres);
            StrikeRangeMetres = Math.Max(0f, StrikeRangeMetres);

            SpottedMemorySeconds = Math.Max(0.01f, SpottedMemorySeconds);
            StrikeRetreatSeconds = Math.Max(0.01f, StrikeRetreatSeconds);
            StrikeCooldownSeconds = Math.Max(0f, StrikeCooldownSeconds);

            DarkLightLevel = Math.Min(1f, Math.Max(0f, DarkLightLevel));
            HideSearchMetres = Math.Max(0f, HideSearchMetres);
            AmbushLookaheadNodes = Math.Max(1, AmbushLookaheadNodes);
            RetreatMinDistanceMetres = Math.Max(0f, RetreatMinDistanceMetres);

            SightRangeMetres = Math.Max(0f, SightRangeMetres);
            FieldOfViewDegrees = Math.Min(360f, Math.Max(1f, FieldOfViewDegrees));
            LostSightGraceSeconds = Math.Max(0f, LostSightGraceSeconds);
            PatrolDarkWeight = Math.Max(0f, PatrolDarkWeight);
            PatrolMinLegMetres = Math.Max(0f, PatrolMinLegMetres);
            PatrolMaxLegMetres = Math.Max(PatrolMinLegMetres, PatrolMaxLegMetres);
            PatrolRecentTiles = Math.Max(0, PatrolRecentTiles);
            PatrolTowardLastSeenFactor = Math.Min(1f, Math.Max(0f, PatrolTowardLastSeenFactor));

            DecisionIntervalSeconds = Math.Max(0.02f, DecisionIntervalSeconds);
            ReplanIntervalSeconds = Math.Max(0.02f, ReplanIntervalSeconds);
            ActionCommitSeconds = Math.Max(0f, ActionCommitSeconds);
            SwitchMargin = Math.Max(0f, SwitchMargin);
        }
    }
}

using System;
using System.Collections.Generic;

namespace LabLockdown.AI.DroneStalker
{
    /// <summary>
    /// The situation the scorer rates, prepared by the brain from the percepts plus its own memory
    /// (walking distance, the best ambush spot it found, how long ago it was spotted or struck).
    /// </summary>
    public sealed class StalkerSituation
    {
        /// <summary>Walking distance to the player along the level graph, in metres.</summary>
        public float WalkDistanceToPlayer = float.PositiveInfinity;

        /// <summary>Straight-line distance to the player, in metres (for the strike range).</summary>
        public float StraightDistanceToPlayer = float.PositiveInfinity;

        /// <summary>Player health, 0 to 1.</summary>
        public float PlayerHealth01 = 1f;

        /// <summary>True if the player can see the Stalker right now.</summary>
        public bool SeenNow;

        /// <summary>True if the Stalker can see the player right now (needed to strike: never through a wall or door).</summary>
        public bool PlayerInSight = true;

        /// <summary>Light level of the node the Stalker is in, 0 to 1.</summary>
        public float LightHere = StalkerPercepts.NeutralLight;

        /// <summary>Seconds since the player last saw the Stalker (infinity if never).</summary>
        public float SecondsSinceSpotted = float.PositiveInfinity;

        /// <summary>Seconds since the Stalker last struck (infinity if never).</summary>
        public float SecondsSinceStrike = float.PositiveInfinity;

        /// <summary>Quality of the best ambush spot found, 0 (none) to 1 (dark, unseen, right on the player's route).</summary>
        public float AmbushSpotQuality;
    }

    /// <summary>The score of every action, plus the terms each score was built from (for the debug overlay and the viva).</summary>
    public sealed class ActionScores
    {
        public static readonly StalkerAction[] All =
            { StalkerAction.Stalk, StalkerAction.Hide, StalkerAction.Ambush, StalkerAction.Strike, StalkerAction.Retreat };

        readonly float[] scores = new float[All.Length];
        readonly List<(StalkerAction action, string term, float value)> terms = new List<(StalkerAction, string, float)>();

        /// <summary>Score of an action; 0 for actions that are not scored (Patrol).</summary>
        public float this[StalkerAction action] => (int)action < scores.Length ? scores[(int)action] : 0f;

        /// <summary>Every contribution, e.g. (Hide, "seen now", 0.55). Lets you explain exactly why an action won.</summary>
        public IReadOnlyList<(StalkerAction action, string term, float value)> Terms => terms;

        internal void Add(StalkerAction action, string term, float value)
        {
            if (value == 0f) return;
            scores[(int)action] += value;
            terms.Add((action, term, value));
        }

        internal void ClampToZero()
        {
            for (int i = 0; i < scores.Length; i++) if (scores[i] < 0f) scores[i] = 0f;
        }

        /// <summary>The highest-scoring action. Ties go to the earlier action in <see cref="All"/>.</summary>
        public StalkerAction Best
        {
            get
            {
                StalkerAction best = All[0];
                foreach (StalkerAction a in All) if (this[a] > this[best]) best = a;
                return best;
            }
        }
    }

    /// <summary>
    /// Utility-based decision making: rates each tactic from 0 upwards using weighted, normalised factors,
    /// and the brain commits to the best. Factors (all 0 to 1):
    ///   proximity = 1 when the player is near, 0 when far      weak  = 1 - player health
    ///   seen      = 1 while the player sees the Stalker          lit   = light level where the Stalker is
    ///   spotted   = fades from 1 to 0 after being spotted        struck = fades from 1 to 0 after a strike
    ///   spot      = quality of the best ambush spot (ambush appeal is also scaled by 1 - struck)
    /// </summary>
    public sealed class UtilityScorer
    {
        readonly StalkerSettings s;

        public UtilityScorer(StalkerSettings settings)
        {
            s = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>True if a strike can land right now: player in sight and in range, the Stalker unseen, and off cooldown.</summary>
        public bool CanStrike(StalkerSituation x) =>
            x.PlayerInSight &&
            !x.SeenNow &&
            x.StraightDistanceToPlayer <= s.StrikeRangeMetres &&
            x.SecondsSinceStrike >= s.StrikeCooldownSeconds;

        public ActionScores Score(StalkerSituation x)
        {
            if (x == null) throw new ArgumentNullException(nameof(x));

            float proximity = Proximity(x.WalkDistanceToPlayer);
            float far = 1f - proximity;
            float weak = 1f - Clamp01(x.PlayerHealth01);
            float seen = x.SeenNow ? 1f : 0f;
            float lit = Clamp01(x.LightHere);
            float spotted = x.SeenNow ? 1f : Fade(x.SecondsSinceSpotted, s.SpottedMemorySeconds);
            float struck = Fade(x.SecondsSinceStrike, s.StrikeRetreatSeconds);
            float spot = Clamp01(x.AmbushSpotQuality);

            var r = new ActionScores();

            // Stalk: the default behaviour, stronger when the gap to the player is large, pointless while seen.
            r.Add(StalkerAction.Stalk, "base", s.StalkBase);
            r.Add(StalkerAction.Stalk, "player far", s.StalkDistanceWeight * far);
            r.Add(StalkerAction.Stalk, "seen now", -s.StalkSeenPenalty * seen);

            // Hide: get out of view and out of the light.
            r.Add(StalkerAction.Hide, "seen now", s.HideSeenWeight * seen);
            r.Add(StalkerAction.Hide, "standing in light", s.HideLitWeight * lit);
            r.Add(StalkerAction.Hide, "recently spotted", s.HideSpottedWeight * spotted);

            // Ambush: worth it when the player is weak and a good dark spot lies on their route.
            // Right after a strike the Stalker has given its position away, so the whole ambush appeal is
            // scaled by (1 - struck): it fades back in over the strike retreat time.
            if (spot > 0f)
            {
                float notJustStruck = 1f - struck;
                r.Add(StalkerAction.Ambush, "player weak", s.AmbushWeakWeight * weak * notJustStruck);
                r.Add(StalkerAction.Ambush, "good ambush spot", s.AmbushSpotWeight * spot * notJustStruck);
                r.Add(StalkerAction.Ambush, "player near", s.AmbushNearWeight * proximity * notJustStruck);
            }

            // Strike: only possible when close, unseen and off cooldown; more attractive against a weak player.
            if (CanStrike(x))
            {
                r.Add(StalkerAction.Strike, "in range, unseen", s.StrikeBase);
                r.Add(StalkerAction.Strike, "player weak", s.StrikeWeakWeight * weak);
            }

            // Retreat: break away right after a strike, or after a close call.
            r.Add(StalkerAction.Retreat, "just struck", s.RetreatStrikeWeight * struck);
            r.Add(StalkerAction.Retreat, "spotted up close", s.RetreatSpottedNearWeight * spotted * proximity);

            r.ClampToZero();
            return r;
        }

        /// <summary>1 at or below the near distance, 0 at or beyond the far distance, linear in between.</summary>
        public float Proximity(float walkDistance)
        {
            if (float.IsNaN(walkDistance) || float.IsPositiveInfinity(walkDistance)) return 0f;
            float span = s.FarDistanceMetres - s.NearDistanceMetres;
            if (span <= 0f) return walkDistance <= s.NearDistanceMetres ? 1f : 0f;
            return 1f - Clamp01((walkDistance - s.NearDistanceMetres) / span);
        }

        /// <summary>1 when the event just happened, falling linearly to 0 after <paramref name="memory"/> seconds.</summary>
        static float Fade(float secondsSince, float memory)
        {
            if (float.IsNaN(secondsSince) || float.IsPositiveInfinity(secondsSince) || memory <= 0f) return 0f;
            return 1f - Clamp01(secondsSince / memory);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

using System.Collections.Generic;
using System.Linq;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.DroneStalker.Tests
{
    public class StalkerBrainTests
    {
        // A 5 x 5 grid of 4 m tiles. Node id = row * 5 + column, so node 0 is a corner and node 12 the centre.
        //
        //    20 21 22 23 24
        //    15 16 17 18 19
        //    10 11 12 13 14
        //     5  6  7  8  9
        //     0  1  2  3  4
        //
        static LevelGraph Grid()
        {
            var g = new LevelGraph();
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    g.AddNode(c * 4f, r * 4f, $"T{r}{c}");
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    int id = r * 5 + c;
                    if (c < 4) g.AddEdge(id, id + 1, 4f);
                    if (r < 4) g.AddEdge(id, id + 5, 4f);
                }
            return g;
        }

        static StalkerPercepts Percepts(int self, int player, double time = 0, float health = 1f, bool inSight = true)
        {
            return new StalkerPercepts
            {
                Time = time,
                SelfNode = self,
                PlayerNode = player,
                PlayerInSight = inSight,
                PlayerHealth01 = health,
                LightByNode = new float[25],               // all dark unless a test lights something
                PlayerVisibleNodes = new HashSet<int>(),
                LockedDoors = new HashSet<string>(),
            };
        }

        [Test]
        public void StalksAHealthyPlayerFromTheDark()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            var p = Percepts(self: 0, player: 24);
            p.DistanceToPlayerMetres = 23f;

            var d = brain.Think(p);

            Assert.AreEqual(StalkerAction.Stalk, d.Action);
            Assert.IsTrue(d.HasRoute);
            Assert.AreNotEqual(24, d.TargetNode, "stalking means following at a distance, not walking onto the player");
        }

        [Test]
        public void HidesTheMomentItIsSeenEvenMidCommit()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            var p = Percepts(self: 12, player: 2);
            Assert.AreEqual(StalkerAction.Stalk, brain.Think(p).Action);

            var seen = Percepts(self: 12, player: 2, time: 0.3);    // well inside the 1.5 s commit time
            seen.PlayerSeesMe = true;
            seen.PlayerVisibleNodes = new HashSet<int> { 2, 7, 12, 17, 22 };   // the player sees the whole column
            var d = brain.Think(seen);

            Assert.AreEqual(StalkerAction.Hide, d.Action);
            Assert.IsFalse(seen.PlayerVisibleNodes.Contains(d.TargetNode), "the hiding place must be out of the player's view");
        }

        [Test]
        public void AmbushesAWeakPlayerOnTheWayToTheInfirmary()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            var p = Percepts(self: 12, player: 20, health: 0.3f);
            p.HealNodes = new HashSet<int> { 24 };      // the infirmary is at the far end of the top row
            p.DistanceToPlayerMetres = 11f;

            var d = brain.Think(p);

            Assert.AreEqual(StalkerAction.Ambush, d.Action);
            int[] route = Grid().ShortestPath(20, 24);
            var nearRoute = new HashSet<int>(route.Skip(1).Take(4));
            foreach (int n in route.Skip(1).Take(4))
                foreach (var e in Grid().EdgesOf(n)) nearRoute.Add(e.Other(n));
            Assert.IsTrue(nearRoute.Contains(d.TargetNode), "the ambush spot must be on or beside the player's next steps");
        }

        [Test]
        public void StrikesWhenCloseAndUnseenThenRetreats()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            var p = Percepts(self: 12, player: 13, health: 0.4f);
            p.DistanceToPlayerMetres = 2f;

            var strike = brain.Think(p);
            Assert.AreEqual(StalkerAction.Strike, strike.Action);
            Assert.IsTrue(strike.StrikeNow);

            var after = Percepts(self: 12, player: 13, time: 0.3, health: 0.2f);
            after.DistanceToPlayerMetres = 2f;
            var d = brain.Think(after);

            Assert.AreEqual(StalkerAction.Retreat, d.Action);
            Assert.IsFalse(d.StrikeNow, "no second strike during the cooldown");
            float[] fromPlayer = Grid().ShortestDistances(13);
            Assert.GreaterOrEqual(fromPlayer[d.TargetNode], new StalkerSettings().RetreatMinDistanceMetres);
        }

        [Test]
        public void DoesNotFlipFlopInsideTheCommitTime()
        {
            var settings = new StalkerSettings { HideLitWeight = 0.4f };   // makes Hide only slightly better than Stalk below
            var brain = new StalkerBrain(Grid(), settings);
            Assert.AreEqual(StalkerAction.Stalk, brain.Think(Percepts(self: 12, player: 13)).Action);

            var lit = Percepts(self: 12, player: 13, time: 0.3);
            lit.LightByNode[12] = 1f;                           // Hide 0.40 vs Stalk 0.35: better, but by less than the margin
            Assert.AreEqual(StalkerAction.Stalk, brain.Think(lit).Action);

            var later = Percepts(self: 12, player: 13, time: 2.0);
            later.LightByNode[12] = 1f;
            Assert.AreEqual(StalkerAction.Hide, brain.Think(later).Action);
        }

        [Test]
        public void ReplansAtOnceWhenADoorOnItsRouteIsLocked()
        {
            var graph = Grid();
            Assert.IsTrue(graph.TagDoor("Door_Mid", 6f, 8f, 0.6f));    // the edge between nodes 11 and 12
            var brain = new StalkerBrain(graph, new StalkerSettings());

            // Strike range and unseen: it heads straight for the player along row 2, through the door.
            var p = Percepts(self: 10, player: 12, health: 0.5f);
            p.DistanceToPlayerMetres = 2f;
            var first = brain.Think(p);
            Assert.IsTrue(UsesEdge(first.Path, 11, 12), "the first route should go through the door");

            // The door is locked before the next decision tick: the route must be replanned at once.
            var locked = Percepts(self: 10, player: 12, time: 0.05, health: 0.5f);
            locked.DistanceToPlayerMetres = 2f;
            locked.LockedDoors = new HashSet<string> { "Door_Mid" };
            var d = brain.Think(locked);

            Assert.IsTrue(d.HasRoute, "there is still a way round");
            Assert.IsFalse(UsesEdge(d.Path, 11, 12), "the new route must not go through the locked door");
        }

        static bool UsesEdge(int[] path, int a, int b)
        {
            for (int k = 0; k + 1 < path.Length; k++)
                if ((path[k] == a && path[k + 1] == b) || (path[k] == b && path[k + 1] == a)) return true;
            return false;
        }

        [Test]
        public void SealedInByLockedDoorsNeverThrows()
        {
            var g = new LevelGraph();
            g.AddNode(0, 0, "A");
            g.AddNode(4, 0, "B");
            g.AddEdge(0, 1, 4f);
            g.TagDoor("Door_AB", 2f, 0f, 0.6f);
            var brain = new StalkerBrain(g, new StalkerSettings());
            var p = new StalkerPercepts { SelfNode = 0, PlayerNode = 1, PlayerInSight = false, LockedDoors = new HashSet<string> { "Door_AB" } };

            StalkerDecision d = null;
            Assert.DoesNotThrow(() => d = brain.Think(p));
            CollectionAssert.AreEqual(new[] { 0 }, d.Path, "with no way out it stays put");
        }

        [Test]
        public void UnknownPositionsHoldInsteadOfThrowing()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            Assert.DoesNotThrow(() => brain.Think(Percepts(self: -1, player: 12)));
            Assert.DoesNotThrow(() => brain.Think(Percepts(self: 12, player: 99)));
            Assert.DoesNotThrow(() => brain.Think(null));
        }

        [Test]
        public void MissingSensorDataStillGivesADecision()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            var p = new StalkerPercepts { SelfNode = 0, PlayerNode = 24, PlayerInSight = true, LightByNode = null, PlayerVisibleNodes = null, LockedDoors = null, ClosedDoors = null, HealNodes = null };
            var d = brain.Think(p);
            Assert.IsTrue(d.HasRoute);
            Assert.IsNotNull(d.Scores);
        }

        [Test]
        public void NeverStrikesWhileSeen()
        {
            var scorer = new UtilityScorer(new StalkerSettings());
            var x = new StalkerSituation { StraightDistanceToPlayer = 1f, WalkDistanceToPlayer = 4f, SeenNow = true };
            Assert.IsFalse(scorer.CanStrike(x));
            Assert.AreEqual(0f, scorer.Score(x)[StalkerAction.Strike]);
        }
        // ---------------- sight-based awareness and patrol ----------------

        [Test]
        public void PatrolsFromTheStartWhenItHasNeverSeenThePlayer()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            var d = brain.Think(Percepts(self: 12, player: 0, inSight: false));

            Assert.AreEqual(StalkerAction.Patrol, d.Action);
            Assert.IsFalse(brain.IsAwareOfPlayer, "it must not know where an unseen player is");
            Assert.IsTrue(d.HasRoute, "patrol means moving, not hovering at the spawn point");
            Assert.AreEqual(-1, brain.LastSeenPlayerNode);
        }

        [Test]
        public void SwitchesFromPatrolToTacticsTheMomentItSpotsThePlayer()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            Assert.AreEqual(StalkerAction.Patrol, brain.Think(Percepts(self: 12, player: 2, inSight: false)).Action);

            var spotted = Percepts(self: 12, player: 2, time: 0.05);   // before the next decision tick
            Assert.AreNotEqual(StalkerAction.Patrol, brain.Think(spotted).Action);
            Assert.AreEqual(2, brain.LastSeenPlayerNode);
        }

        [Test]
        public void KeepsHuntingDuringTheGracePeriodThenPatrols()
        {
            var settings = new StalkerSettings();
            var brain = new StalkerBrain(Grid(), settings);
            brain.Think(Percepts(self: 12, player: 2));                                   // sees the player

            var brieflyLost = Percepts(self: 12, player: 2, time: 1.0, inSight: false);   // a pillar blocks the view
            Assert.AreNotEqual(StalkerAction.Patrol, brain.Think(brieflyLost).Action);
            Assert.IsTrue(brain.IsAwareOfPlayer);

            var gone = Percepts(self: 12, player: 2, time: 1.0 + settings.LostSightGraceSeconds + 0.1, inSight: false);
            Assert.AreEqual(StalkerAction.Patrol, brain.Think(gone).Action);
            Assert.IsFalse(brain.IsAwareOfPlayer);
        }

        [Test]
        public void LosesThePlayerBehindAClosedDoorAndPatrolsAwayFromIt()
        {
            var graph = Grid();
            Assert.IsTrue(graph.TagDoor("Door_Mid", 6f, 8f, 0.6f));     // the door between tiles 11 and 12
            // Tiles that do not lead away from the vanishing point get no chance at all, so the rule is tested exactly.
            var brain = new StalkerBrain(graph, new StalkerSettings { PatrolTowardLastSeenFactor = 0f });

            // The drone (tile 11) sees the player (tile 12) through the open doorway.
            brain.Think(Percepts(self: 11, player: 12));

            // The player steps through and shuts the door: the drone can no longer see them.
            var shut = Percepts(self: 11, player: 13, time: 3.0, inSight: false);
            shut.ClosedDoors = new HashSet<string> { "Door_Mid" };
            var d = brain.Think(shut);

            Assert.AreEqual(StalkerAction.Patrol, d.Action);
            Assert.IsFalse(UsesEdge(d.Path, 11, 12), "drones cannot pass a closed door");
            float[] fromLastSeen = Grid().ShortestDistances(12);
            Assert.Greater(fromLastSeen[d.TargetNode], fromLastSeen[11], "the first patrol leg leads away from where the player vanished");
        }

        [Test]
        public void NeverStrikesWithoutSeeingThePlayer()
        {
            var brain = new StalkerBrain(Grid(), new StalkerSettings());
            brain.Think(Percepts(self: 12, player: 13));                     // seen once, so it is aware for the grace period

            var p = Percepts(self: 12, player: 13, time: 0.5, health: 0.2f, inSight: false);
            p.DistanceToPlayerMetres = 1f;                                   // right behind a wall or door
            var d = brain.Think(p);

            Assert.IsFalse(d.StrikeNow);
            Assert.AreNotEqual(StalkerAction.Strike, d.Action);
        }

        [Test]
        public void PatrolPrefersDarkTiles()
        {
            var graph = Grid();
            var settings = new StalkerSettings { PatrolRecentTiles = 0 };
            var light = new float[25];
            for (int i = 0; i < 25; i++) light[i] = 1f;
            int[] dark = { 0, 4, 20, 24, 2, 22 };                            // 6 of the 20 tiles 8-24 m from the centre are dark
            foreach (int i in dark) light[i] = 0f;
            var percepts = new StalkerPercepts { LightByNode = light };

            var planner = new PatrolPlanner(graph, settings);
            int darkPicks = 0, picks = 400;
            for (int k = 0; k < picks; k++)
            {
                int next = planner.ChooseNext(12, percepts, new HashSet<string>());
                if (light[next] == 0f) darkPicks++;
            }
            // By count a dark tile would be picked 30% of the time; with weight 4 versus 1 it should be about 63%.
            Assert.Greater(darkPicks / (float)picks, 0.45f);
        }

        [Test]
        public void PatrolAvoidsRecentlyVisitedTiles()
        {
            var planner = new PatrolPlanner(Grid(), new StalkerSettings { PatrolMinLegMetres = 0f, PatrolMaxLegMetres = 4f });
            planner.MarkVisited(11);
            planner.MarkVisited(13);
            planner.MarkVisited(7);
            for (int k = 0; k < 50; k++)
                Assert.AreEqual(17, planner.ChooseNext(12, null, new HashSet<string>()), "the only fresh neighbour is 17");
        }
    }
}

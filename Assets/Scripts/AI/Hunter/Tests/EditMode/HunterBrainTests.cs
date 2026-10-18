using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class HunterBrainTests
    {
        private const int HunterStart = 20;   // a central tile of the real level

        private static HunterBrain CreateBrain(LevelGraph graph, BeliefMap belief = null, float roll = 0f)
        {
            return new HunterBrain(
                graph,
                belief ?? new BeliefMap(graph.NodeCount),
                new SoundModel(),
                new EdgeCostModel(),
                new AttackModel(new AttackParameters(), () => roll),
                new HunterBrainParameters());
        }

        private static HunterPercepts Percepts(float now, int hunterNode, bool sees = false, int playerNode = -1, float distance = 0f, bool alive = true)
        {
            return new HunterPercepts
            {
                Now = now,
                HunterNode = hunterNode,
                IsAlive = alive,
                HasLineOfSight = sees,
                PlayerNode = playerNode,
                PlayerDistance = distance
            };
        }

        // Node whose walking distance from 'from' is closest to the wanted distance (all doors open).
        private static int NodeAtDistance(LevelGraph graph, int from, float wanted)
        {
            float[] d = CostField.From(graph, new EdgeCostModel(), from, new DoorStateTable(DoorState.Open));
            int best = -1;
            float bestError = float.MaxValue;
            for (int n = 0; n < graph.NodeCount; n++)
            {
                if (float.IsInfinity(d[n])) continue;
                float error = Math.Abs(d[n] - wanted);
                if (error < bestError) { bestError = error; best = n; }
            }
            return best;
        }

        private static float WalkDistance(LevelGraph graph, int a, int b)
        {
            return CostField.From(graph, new EdgeCostModel(), a, new DoorStateTable(DoorState.Open))[b];
        }

        private static readonly DoorStateTable Open = new DoorStateTable(DoorState.Open);

        [Test]
        public void FirstTick_PatrolsAndPicksATarget()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);

            HunterCommand cmd = brain.Think(Percepts(0f, HunterStart), Open);

            Assert.AreEqual(HunterState.Patrol, cmd.State);
            Assert.AreEqual(HunterGait.Patrol, cmd.Gait);
            Assert.IsTrue(cmd.TargetNode >= 0 && cmd.TargetNode != HunterStart);
            Assert.IsTrue(cmd.TargetChanged);
            Assert.IsFalse(cmd.HoldPosition);
        }

        [Test]
        public void Patrol_SweepsTheWholeLevel_WithoutAnyScriptedRoute()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            int reachable = graph.CountReachable(HunterStart, new HashSet<string>());

            var visited = new HashSet<int> { HunterStart };
            int node = HunterStart;
            float now = 0f;
            for (int step = 0; step < 800 && visited.Count < reachable; step++)
            {
                HunterCommand cmd = brain.Think(Percepts(now, node), Open);
                if (cmd.TargetNode >= 0) node = cmd.TargetNode;   // arrive instantly: this tests the choice, not the walking
                visited.Add(node);
                now += 1f;
            }

            Assert.AreEqual(reachable, visited.Count);
        }

        [Test]
        public void ALoudNoiseNearby_StartsAnInvestigation_TowardTheSource()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);

            int source = NodeAtDistance(graph, HunterStart, 8f);
            brain.OnNoise(source, 30f, Open);   // a gunshot two tiles away
            HunterCommand cmd = brain.Think(Percepts(0.1f, HunterStart), Open);

            Assert.AreEqual(HunterState.Investigate, cmd.State);
            Assert.AreEqual(HunterGait.Investigate, cmd.Gait);
            Assert.AreEqual(source, brain.Belief.MostLikelyNode());
            Assert.Less(WalkDistance(graph, cmd.TargetNode, source), WalkDistance(graph, HunterStart, source));
        }

        [Test]
        public void ANoiseFromAnotherDirection_ChangesTheTarget()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            HunterCommand first = brain.Think(Percepts(0f, HunterStart), Open);

            // A source about 8 m from the Hunter whose route does NOT pass the current patrol target.
            float[] fromHunter = CostField.From(graph, new EdgeCostModel(), HunterStart, Open);
            float[] fromTarget = CostField.From(graph, new EdgeCostModel(), first.TargetNode, Open);
            int source = -1;
            for (int n = 0; n < graph.NodeCount && source < 0; n++)
                if (fromHunter[n] >= 8f && fromHunter[n] <= 12f && fromTarget[n] > fromHunter[n] + 2f) source = n;
            Assert.IsTrue(source >= 0);

            brain.OnNoise(source, 30f, Open);
            HunterCommand cmd = brain.Think(Percepts(0.1f, HunterStart), Open);

            Assert.IsTrue(cmd.TargetChanged);
            Assert.AreNotEqual(first.TargetNode, cmd.TargetNode);
            Assert.Less(WalkDistance(graph, cmd.TargetNode, source), WalkDistance(graph, HunterStart, source));
        }

        [Test]
        public void ANoiseTooQuietToHear_ChangesNothing()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);

            float[] before = brain.Belief.Snapshot();
            brain.OnNoise(NodeAtDistance(graph, HunterStart, 28f), 8f, Open);   // a thrown mug far away
            float[] after = brain.Belief.Snapshot();

            for (int i = 0; i < before.Length; i++) Assert.AreEqual(before[i], after[i], 1e-9);
        }

        [Test]
        public void AClosedDoor_CanMuffleANoiseIntoSilence()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);

            // A throw of loudness 8, three tiles away. With every door open the Hunter hears it.
            int source = NodeAtDistance(graph, HunterStart, 4f);
            float[] before = brain.Belief.Snapshot();
            brain.OnNoise(source, 8f, Open);
            float[] heardOpen = brain.Belief.Snapshot();
            bool changedOpen = false;
            for (int i = 0; i < before.Length; i++) if (Math.Abs(before[i] - heardOpen[i]) > 1e-9) changedOpen = true;
            Assert.IsTrue(changedOpen);

            // The same throw heard through a closed door loses 3 more and is inaudible when it was already marginal.
            HunterBrain muffledBrain = CreateBrain(graph);
            muffledBrain.Think(Percepts(0f, HunterStart), Open);
            var closed = new DoorStateTable(DoorState.Closed);
            float[] levelOpen = new SoundModel().Propagate(graph, source, 8f, Open);
            float[] levelClosed = new SoundModel().Propagate(graph, source, 8f, closed);
            Assert.IsTrue(levelClosed[HunterStart] <= levelOpen[HunterStart]);
        }

        [Test]
        public void SeeingThePlayerOutOfRange_ChasesAtChaseSpeed()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);

            int playerNode = NodeAtDistance(graph, HunterStart, 24f);
            HunterCommand cmd = brain.Think(Percepts(0.1f, HunterStart, sees: true, playerNode: playerNode, distance: 24f), Open);

            Assert.AreEqual(HunterState.Chase, cmd.State);
            Assert.AreEqual(HunterGait.Chase, cmd.Gait);
            Assert.IsFalse(cmd.HoldPosition);
            Assert.Less(WalkDistance(graph, cmd.TargetNode, playerNode), WalkDistance(graph, HunterStart, playerNode));
        }

        [Test]
        public void SeeingThePlayerInRange_StandsStillAndShoots()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph, roll: 0f);
            brain.Think(Percepts(0f, HunterStart), Open);
            int playerNode = NodeAtDistance(graph, HunterStart, 8f);

            HunterCommand start = brain.Think(Percepts(1.0f, HunterStart, true, playerNode, 8f), Open);
            Assert.AreEqual(HunterState.Attack, start.State);
            Assert.IsTrue(start.HoldPosition);
            Assert.AreEqual(AttackEvent.WindUpStarted, start.Attack.Event);

            HunterCommand fired = brain.Think(Percepts(1.6f, HunterStart, true, playerNode, 8f), Open);
            Assert.AreEqual(AttackEvent.Fired, fired.Attack.Event);
            Assert.IsTrue(fired.Attack.Hit);   // the injected roll of 0 always hits
        }

        [Test]
        public void DuckingBehindCoverDuringTheWindUp_CancelsTheShot()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);
            int playerNode = NodeAtDistance(graph, HunterStart, 8f);

            brain.Think(Percepts(1.0f, HunterStart, true, playerNode, 8f), Open);
            HunterCommand cmd = brain.Think(Percepts(1.3f, HunterStart), Open);   // line of sight lost

            Assert.AreEqual(AttackEvent.WindUpCancelled, cmd.Attack.Event);
            Assert.AreEqual(HunterState.Chase, cmd.State);   // it still remembers where the player was
        }

        [Test]
        public void AfterLosingSight_KeepsChasingBriefly_ThenStopsChasing()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);
            int playerNode = NodeAtDistance(graph, HunterStart, 24f);

            brain.Think(Percepts(1.0f, HunterStart, true, playerNode, 24f), Open);
            int target = brain.TargetNode;

            HunterCommand soon = brain.Think(Percepts(4.0f, HunterStart), Open);   // 3 s after the last sighting
            Assert.AreEqual(HunterState.Chase, soon.State);
            Assert.AreEqual(target, soon.TargetNode);

            HunterCommand later = brain.Think(Percepts(10.0f, HunterStart), Open);   // 9 s after: memory expired
            Assert.IsTrue(later.State != HunterState.Chase);
        }

        [Test]
        public void ADeadHunter_StopsAndStaysStopped()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);

            HunterCommand cmd = brain.Think(Percepts(1f, HunterStart, alive: false), Open);

            Assert.AreEqual(HunterState.Dead, cmd.State);
            Assert.AreEqual(-1, cmd.TargetNode);
            Assert.IsTrue(cmd.HoldPosition);
            Assert.AreEqual(AttackEvent.None, cmd.Attack.Event);
        }

        [Test]
        public void BeingShot_TurnsTheHunterTowardTheShooter()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            brain.Think(Percepts(0f, HunterStart), Open);

            int shooter = NodeAtDistance(graph, HunterStart, 16f);
            brain.OnShot(shooter);
            HunterCommand cmd = brain.Think(Percepts(0.1f, HunterStart), Open);

            Assert.AreEqual(HunterState.Investigate, cmd.State);
            Assert.AreEqual(shooter, brain.Belief.MostLikelyNode());
            Assert.Less(WalkDistance(graph, cmd.TargetNode, shooter), WalkDistance(graph, HunterStart, shooter));
        }

        [Test]
        public void WithEveryDoorLocked_TheTargetIsAlwaysReachable()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            var locked = new DoorStateTable(DoorState.Locked);
            var costs = new EdgeCostModel();

            int node = HunterStart;
            float now = 0f;
            for (int step = 0; step < 300; step++)
            {
                HunterCommand cmd = brain.Think(Percepts(now, node), locked);
                if (cmd.TargetNode >= 0)
                {
                    Assert.IsFalse(float.IsPositiveInfinity(CostField.From(graph, costs, node, locked)[cmd.TargetNode]));
                    node = cmd.TargetNode;
                }
                now += 1f;
            }
        }

        [Test]
        public void TheTarget_DoesNotFlipFlop_WhenNothingNewIsPerceived()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);

            HunterCommand first = brain.Think(Percepts(0f, HunterStart), Open);
            HunterCommand second = brain.Think(Percepts(0.1f, HunterStart), Open);
            HunterCommand third = brain.Think(Percepts(0.7f, HunterStart), Open);   // past the reselect interval

            Assert.IsFalse(second.TargetChanged);
            Assert.IsFalse(third.TargetChanged);
            Assert.AreEqual(first.TargetNode, second.TargetNode);
            Assert.AreEqual(first.TargetNode, third.TargetNode);
        }

        [Test]
        public void ArrivingAtTheTarget_MarksItSearched_AndChoosesANewOne()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);
            HunterCommand first = brain.Think(Percepts(0f, HunterStart), Open);

            HunterCommand next = brain.Think(Percepts(1f, first.TargetNode), Open);

            Assert.IsTrue(next.TargetNode >= 0 && next.TargetNode != first.TargetNode);
            Assert.IsTrue(next.TargetChanged);
            Assert.Less(brain.Belief[first.TargetNode], 1f / graph.NodeCount);   // searched: below the flat level
        }

        [Test]
        public void Investigate_UsesHysteresis_SoItDoesNotFlapAtTheBoundary()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            int n = graph.NodeCount;
            int peak = NodeAtDistance(graph, HunterStart, 24f);

            // One node weighted so the peak is 3x the flat level, then decayed to about 1.75x: between exit (1.5) and enter (2.0).
            float[] weights = new float[n];
            for (int i = 0; i < n; i++) weights[i] = 1f;
            weights[peak] = 3f * (n - 1) / (n - 3f);

            HunterBrain investigating = CreateBrain(graph, BeliefMap.FromProbabilities(weights));
            Assert.AreEqual(HunterState.Investigate, investigating.Think(Percepts(0f, HunterStart), Open).State);
            investigating.Belief.Decay(12.5f);
            float ratio = investigating.Belief.MaxProbability * n;
            Assert.IsTrue(ratio > 1.55f && ratio < 1.95f);
            Assert.AreEqual(HunterState.Investigate, investigating.Think(Percepts(0f, HunterStart), Open).State);   // still above exit

            // A brain that starts at the same level never entered Investigate, so it stays on Patrol.
            HunterBrain patrolling = CreateBrain(graph, BeliefMap.FromProbabilities(weights));
            patrolling.Belief.Decay(12.5f);
            Assert.AreEqual(HunterState.Patrol, patrolling.Think(Percepts(0f, HunterStart), Open).State);
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            LevelGraph graph = LabLockdownLayout.CreateGraph();
            HunterBrain brain = CreateBrain(graph);

            Assert.Throws<ArgumentNullException>(() => brain.Think(Percepts(0f, HunterStart), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => brain.Think(Percepts(0f, 999), Open));
            Assert.Throws<ArgumentException>(() => CreateBrain(graph, new BeliefMap(3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HunterBrainParameters(investigateEnterRatio: 1.2f, investigateExitRatio: 1.5f));
        }
    }
}

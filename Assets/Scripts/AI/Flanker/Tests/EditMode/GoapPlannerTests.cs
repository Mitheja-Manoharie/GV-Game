using System;
using System.Collections.Generic;
using System.Linq;
using LabLockdown.AI.Flanker.Goap;
using NUnit.Framework;

namespace LabLockdown.AI.Flanker.Tests
{
    public class GoapPlannerTests
    {
        private readonly GoapPlanner planner = new GoapPlanner();

        private static IEnumerable<PlannerOptions> BothSearches()
        {
            yield return new PlannerOptions();
            yield return PlannerOptions.UniformCost();
        }

        /// <summary>Checks a plan step by step: each action applicable, the costs add up, the goal holds at the end.</summary>
        private static void AssertValid(PlanResult plan, WorldState start, Goal goal)
        {
            Assert.IsTrue(plan.IsFound, $"Expected a plan, got {plan.Status}");
            Assert.AreEqual(plan.Actions.Count + 1, plan.States.Count);

            var state = start;
            float cost = 0f;
            for (int i = 0; i < plan.Actions.Count; i++)
            {
                Assert.AreEqual(state, plan.States[i]);
                Assert.IsTrue(plan.Actions[i].IsApplicable(state), $"{plan.Actions[i]} is not applicable at step {i}");
                state = plan.Actions[i].Apply(state);
                cost += plan.Actions[i].Cost;
            }

            Assert.IsTrue(goal.IsSatisfiedBy(state), "The plan does not reach the goal");
            Assert.AreEqual(cost, plan.Cost, 1e-4f);
        }

        // ---------- Lecture example: Blocks World ----------

        /// <summary>
        /// The classic STRIPS Blocks World, grounded for the given blocks:
        /// PickUp(x), PutDown(x), Stack(x, y) and Unstack(x, y), each costing 1.
        /// </summary>
        private sealed class BlocksWorld
        {
            private readonly Dictionary<string, int> facts = new Dictionary<string, int>();
            public readonly List<PlanAction> Actions = new List<PlanAction>();

            public BlocksWorld(params string[] blocks)
            {
                Fact("HandEmpty");
                foreach (var x in blocks)
                {
                    Fact($"OnTable({x})");
                    Fact($"Clear({x})");
                    Fact($"Holding({x})");
                    foreach (var y in blocks)
                        if (x != y) Fact($"On({x},{y})");
                }

                foreach (var x in blocks)
                {
                    Actions.Add(new PlanAction($"PickUp({x})", 1f,
                        requireTrue: M($"OnTable({x})", $"Clear({x})", "HandEmpty"),
                        add: M($"Holding({x})"),
                        delete: M($"OnTable({x})", $"Clear({x})", "HandEmpty")));
                    Actions.Add(new PlanAction($"PutDown({x})", 1f,
                        requireTrue: M($"Holding({x})"),
                        add: M($"OnTable({x})", $"Clear({x})", "HandEmpty"),
                        delete: M($"Holding({x})")));

                    foreach (var y in blocks)
                    {
                        if (x == y) continue;
                        Actions.Add(new PlanAction($"Stack({x},{y})", 1f,
                            requireTrue: M($"Holding({x})", $"Clear({y})"),
                            add: M($"On({x},{y})", $"Clear({x})", "HandEmpty"),
                            delete: M($"Holding({x})", $"Clear({y})")));
                        Actions.Add(new PlanAction($"Unstack({x},{y})", 1f,
                            requireTrue: M($"On({x},{y})", $"Clear({x})", "HandEmpty"),
                            add: M($"Holding({x})", $"Clear({y})"),
                            delete: M($"On({x},{y})", $"Clear({x})", "HandEmpty")));
                    }
                }
            }

            private void Fact(string name) => facts[name] = facts.Count;

            public ulong M(params string[] names)
            {
                ulong mask = 0;
                foreach (var name in names) mask |= Facts.Bit(facts[name]);
                return mask;
            }

            public WorldState State(params string[] names) => new WorldState(M(names));
        }

        [Test]
        public void SussmanAnomaly_FindsTheOptimalSixStepPlan()
        {
            // C is on A; A and B are on the table. Goal: A on B on C.
            // Solving the sub-goals one at a time fails here, which is why a full state-space search is needed.
            var world = new BlocksWorld("A", "B", "C");
            var start = world.State("On(C,A)", "OnTable(A)", "OnTable(B)", "Clear(C)", "Clear(B)", "HandEmpty");
            var goal = new Goal(world.M("On(A,B)", "On(B,C)"));

            foreach (var options in BothSearches())
            {
                var plan = planner.Plan(start, goal, world.Actions, options);
                AssertValid(plan, start, goal);
                Assert.AreEqual(6f, plan.Cost, 1e-4f, $"heuristic={options.UseHeuristic}");
                Assert.AreEqual("Unstack(C,A) → PutDown(C) → PickUp(B) → Stack(B,C) → PickUp(A) → Stack(A,B)", plan.ToString());
            }
        }

        [Test]
        public void ReverseTowerOfFour_HeuristicExpandsFewerStatesThanUniformCost()
        {
            var world = new BlocksWorld("A", "B", "C", "D");
            var start = world.State("On(A,B)", "On(B,C)", "On(C,D)", "OnTable(D)", "Clear(A)", "HandEmpty");
            var goal = new Goal(world.M("On(D,C)", "On(C,B)", "On(B,A)"));

            var astar = planner.Plan(start, goal, world.Actions);
            var uniform = planner.Plan(start, goal, world.Actions, PlannerOptions.UniformCost());

            AssertValid(astar, start, goal);
            AssertValid(uniform, start, goal);
            Assert.AreEqual(uniform.Cost, astar.Cost, 1e-4f, "A* must stay optimal");
            Assert.Less(astar.Stats.NodesExpanded, uniform.Stats.NodesExpanded);
        }

        // ---------- Edge cases ----------

        [Test]
        public void GoalAlreadyHolds_ReturnsEmptyPlan()
        {
            var start = WorldState.Of(0, 1);
            var plan = planner.Plan(start, new Goal(Facts.Mask(1)), new[] { new PlanAction("Noise", 1f, add: Facts.Mask(2)) });

            Assert.IsTrue(plan.IsFound);
            CollectionAssert.IsEmpty(plan.Actions);
            Assert.AreEqual(0f, plan.Cost);
        }

        [Test]
        public void UnreachableGoal_ReturnsNoPlan()
        {
            var actions = new[]
            {
                new PlanAction("A", 1f, add: Facts.Mask(1)),
                new PlanAction("B", 1f, requireTrue: Facts.Mask(1), add: Facts.Mask(2)),
            };

            foreach (var options in BothSearches())
            {
                var plan = planner.Plan(new WorldState(), new Goal(Facts.Mask(3)), actions, options);
                Assert.AreEqual(PlanStatus.NoPlan, plan.Status);
                Assert.IsTrue(float.IsPositiveInfinity(plan.Cost));
            }
        }

        [Test]
        public void NegativePreconditions_AreRespected()
        {
            // "Sneak" needs fact 0 (Seen) to be false, so the plan must Hide first even though Sneak is cheaper.
            var actions = new[]
            {
                new PlanAction("Hide", 2f, delete: Facts.Mask(0)),
                new PlanAction("Sneak", 1f, requireFalse: Facts.Mask(0), add: Facts.Mask(1)),
                new PlanAction("Charge", 10f, add: Facts.Mask(1)),
            };

            var start = WorldState.Of(0);
            var plan = planner.Plan(start, new Goal(Facts.Mask(1)), actions);
            AssertValid(plan, start, new Goal(Facts.Mask(1)));
            Assert.AreEqual("Hide → Sneak", plan.ToString());
        }

        [Test]
        public void NegativeGoal_CanRequireAFactToBeFalse()
        {
            var actions = new[] { new PlanAction("Holster", 1f, delete: Facts.Mask(4)) };
            var start = WorldState.Of(4);
            var goal = new Goal(0, Facts.Mask(4));

            var plan = planner.Plan(start, goal, actions);
            AssertValid(plan, start, goal);
            Assert.AreEqual(1, plan.Actions.Count);
        }

        [Test]
        public void NodeBudget_StopsTheSearch()
        {
            var world = new BlocksWorld("A", "B", "C");
            var start = world.State("On(C,A)", "OnTable(A)", "OnTable(B)", "Clear(C)", "Clear(B)", "HandEmpty");
            var plan = planner.Plan(start, new Goal(world.M("On(A,B)", "On(B,C)")), world.Actions, new PlannerOptions { MaxNodes = 3 });

            Assert.AreEqual(PlanStatus.NodeLimitReached, plan.Status);
            Assert.AreEqual(3, plan.Stats.NodesExpanded);
        }

        [Test]
        public void InvalidInput_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlanAction("Free", 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlanAction("Nan", float.NaN));
            Assert.Throws<ArgumentException>(() => new PlanAction("Both", 1f, requireTrue: 1, requireFalse: 1));
            Assert.Throws<ArgumentException>(() => new PlanAction("Flip", 1f, add: 2, delete: 2));
            Assert.Throws<ArgumentException>(() => new Goal(1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Facts.Bit(64));
            Assert.Throws<ArgumentNullException>(() => planner.Plan(new WorldState(), new Goal(1), null));
        }

        // ---------- Cross-check against brute force ----------

        /// <summary>
        /// Optimal plan cost by exhaustive search: Bellman-Ford over all 2^facts states, independent of A*.
        /// </summary>
        private static float BruteForceCost(int factCount, WorldState start, Goal goal, IReadOnlyList<PlanAction> actions)
        {
            int states = 1 << factCount;
            var cost = new float[states];
            for (int i = 0; i < states; i++) cost[i] = float.PositiveInfinity;
            cost[start.Bits] = 0f;

            for (int round = 0; round < states; round++)
            {
                bool changed = false;
                for (int s = 0; s < states; s++)
                {
                    if (float.IsPositiveInfinity(cost[s])) continue;
                    var state = new WorldState((ulong)s);
                    foreach (var action in actions)
                    {
                        if (!action.IsApplicable(state)) continue;
                        int next = (int)action.Apply(state).Bits;
                        if (cost[s] + action.Cost < cost[next] - 1e-6f)
                        {
                            cost[next] = cost[s] + action.Cost;
                            changed = true;
                        }
                    }
                }

                if (!changed) break;
            }

            float best = float.PositiveInfinity;
            for (int s = 0; s < states; s++)
                if (goal.IsSatisfiedBy(new WorldState((ulong)s))) best = Math.Min(best, cost[s]);
            return best;
        }

        /// <summary>
        /// 400 random problems over 8 facts, each solved by A* and by uniform-cost search and compared with brute force.
        /// Three in four goals are taken from the end of a random walk from the start, so they are solvable and
        /// usually need several actions; the rest are random, so "no plan" is checked too.
        /// </summary>
        [Test]
        public void RandomProblems_MatchBruteForce()
        {
            const int factCount = 8;
            var random = new Random(3062);
            ulong all = (1UL << factCount) - 1;
            int solvable = 0, multiStep = 0;

            for (int problem = 0; problem < 400; problem++)
            {
                var actions = new List<PlanAction>();
                int actionCount = random.Next(6, 16);
                for (int a = 0; a < actionCount; a++)
                {
                    ulong requireTrue = RandomMask(random, all, 0.12);
                    ulong requireFalse = RandomMask(random, all, 0.08) & ~requireTrue;
                    ulong add = RandomMask(random, all, 0.2);
                    ulong delete = RandomMask(random, all, 0.15) & ~add;
                    actions.Add(new PlanAction($"a{a}", 1 + random.Next(9) + (float)random.NextDouble(),
                        requireTrue, requireFalse, add, delete));
                }

                var start = new WorldState(RandomMask(random, all, 0.4));
                Goal goal;
                if (random.NextDouble() < 0.75)
                {
                    var end = start;
                    for (int step = random.Next(3, 9); step > 0; step--)
                    {
                        var applicable = actions.Where(a => a.IsApplicable(end)).ToList();
                        if (applicable.Count == 0) break;
                        end = applicable[random.Next(applicable.Count)].Apply(end);
                    }

                    ulong goalTrue = end.Bits & (RandomMask(random, all, 0.6) | (end.Bits & ~start.Bits));
                    goal = new Goal(goalTrue, ~end.Bits & all & RandomMask(random, all, 0.3));
                }
                else
                {
                    ulong goalTrue = RandomMask(random, all, 0.3);
                    goal = new Goal(goalTrue, RandomMask(random, all, 0.15) & ~goalTrue);
                }

                float expected = BruteForceCost(factCount, start, goal, actions);
                foreach (var options in BothSearches())
                {
                    var plan = planner.Plan(start, goal, actions, options);
                    if (float.IsPositiveInfinity(expected))
                    {
                        Assert.AreEqual(PlanStatus.NoPlan, plan.Status, $"problem {problem}");
                        continue;
                    }

                    AssertValid(plan, start, goal);
                    Assert.AreEqual(expected, plan.Cost, 1e-3f, $"problem {problem}, heuristic={options.UseHeuristic}");
                    if (options.UseHeuristic && plan.Actions.Count >= 3) multiStep++;
                }

                if (!float.IsPositiveInfinity(expected)) solvable++;
            }

            Assert.Greater(solvable, 250, "Most problems should be solvable");
            Assert.Greater(multiStep, 40, "Many problems should need a plan of three or more actions");
        }

        private static ulong RandomMask(Random random, ulong all, double probability)
        {
            ulong mask = 0;
            for (int bit = 0; (1UL << bit) <= all; bit++)
                if (random.NextDouble() < probability) mask |= 1UL << bit;
            return mask;
        }
    }
}

using System;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class AttackModelTests
    {
        private static AttackModel Create(float roll = 0f)
        {
            return new AttackModel(new AttackParameters(), () => roll);
        }

        [Test]
        public void WithoutLineOfSight_NothingHappens()
        {
            var attack = Create();
            AttackResult r = attack.Update(0f, false, 5f);

            Assert.AreEqual(AttackEvent.None, r.Event);
            Assert.AreEqual(AttackState.Idle, attack.State);
        }

        [Test]
        public void SeeingTheTargetInRange_StartsTheWindUp()
        {
            var attack = Create();
            AttackResult r = attack.Update(0f, true, 5f);

            Assert.AreEqual(AttackEvent.WindUpStarted, r.Event);
            Assert.IsTrue(attack.IsWindingUp);
        }

        [Test]
        public void OutOfRange_DoesNotStartTheWindUp()
        {
            var attack = Create();
            AttackResult r = attack.Update(0f, true, 12.5f);

            Assert.AreEqual(AttackEvent.None, r.Event);
            Assert.IsFalse(attack.IsWindingUp);
        }

        [Test]
        public void FiresOnlyOnceTheWindUpHasFinished()
        {
            var attack = Create();
            attack.Update(0f, true, 5f);

            Assert.AreEqual(AttackEvent.None, attack.Update(0.3f, true, 5f).Event);
            Assert.AreEqual(AttackEvent.None, attack.Update(0.59f, true, 5f).Event);
            Assert.AreEqual(AttackEvent.Fired, attack.Update(0.6f, true, 5f).Event);
            Assert.AreEqual(AttackState.Cooldown, attack.State);
        }

        [Test]
        public void LosingSightDuringTheWindUp_CancelsTheShot()
        {
            var attack = Create();
            attack.Update(0f, true, 5f);

            AttackResult r = attack.Update(0.4f, false, 5f);   // the player ducked behind a door
            Assert.AreEqual(AttackEvent.WindUpCancelled, r.Event);
            Assert.AreEqual(AttackState.Idle, attack.State);

            // No shot is fired later from the old wind-up.
            Assert.AreEqual(AttackEvent.None, attack.Update(0.7f, false, 5f).Event);
        }

        [Test]
        public void LeavingRangeDuringTheWindUp_CancelsTheShot()
        {
            var attack = Create();
            attack.Update(0f, true, 5f);

            Assert.AreEqual(AttackEvent.WindUpCancelled, attack.Update(0.3f, true, 20f).Event);
        }

        [Test]
        public void AfterFiring_TheCooldownBlocksTheNextWindUp()
        {
            var attack = Create();
            attack.Update(0f, true, 5f);
            attack.Update(0.6f, true, 5f);   // fires; cooldown lasts until 0.6 + 1.9 = 2.5

            Assert.AreEqual(AttackEvent.None, attack.Update(1.0f, true, 5f).Event);
            Assert.AreEqual(AttackState.Cooldown, attack.State);

            attack.Update(2.55f, true, 5f);   // cooldown over (a hair past 2.5 to stay clear of float rounding)
            Assert.AreEqual(AttackState.Idle, attack.State);
            Assert.AreEqual(AttackEvent.WindUpStarted, attack.Update(2.6f, true, 5f).Event);
        }

        [Test]
        public void ShotsAreTwoAndAHalfSecondsApart()
        {
            var attack = Create();
            var fireTimes = new System.Collections.Generic.List<float>();

            for (int i = 0; i <= 1000 && fireTimes.Count < 2; i++)
            {
                float now = i * 0.01f;
                if (attack.Update(now, true, 5f).Event == AttackEvent.Fired) fireTimes.Add(now);
            }

            Assert.AreEqual(2, fireTimes.Count);
            Assert.AreEqual(2.5, fireTimes[1] - fireTimes[0], 0.03);   // wind-up 0.6 + cooldown 1.9
        }

        [Test]
        public void HitChance_FallsLinearlyWithDistance()
        {
            AttackModel attack = Create();

            Assert.AreEqual(0.8f, attack.HitChance(0f), 1e-6);
            Assert.AreEqual(0.5f, attack.HitChance(6f), 1e-6);    // 50% at mid-range, as the group agreed
            Assert.AreEqual(0.2f, attack.HitChance(12f), 1e-6);
            Assert.AreEqual(0f, attack.HitChance(13f), 1e-6);
            Assert.AreEqual(0.8f, attack.HitChance(-3f), 1e-6);   // negative distance is treated as zero
        }

        [Test]
        public void AtMidRange_TheDamageRateMatchesTheGroupBalance()
        {
            // Group balance: 6 damage, one shot per 2.5 s, 50% to hit = 1.2 damage per second.
            AttackModel attack = Create();
            AttackParameters p = attack.Parameters;

            float dps = p.Damage * attack.HitChance(p.Range / 2f) / (p.WindUp + p.Cooldown);
            Assert.AreEqual(1.2f, dps, 1e-5);
        }

        [Test]
        public void TheRollDecidesHitOrMiss()
        {
            // At 6 m the chance is 0.5: a roll below it hits, a roll above it misses.
            var hitting = Create(0.10f);
            hitting.Update(0f, true, 6f);
            Assert.IsTrue(hitting.Update(0.6f, true, 6f).Hit);

            var missing = Create(0.90f);
            missing.Update(0f, true, 6f);
            Assert.IsFalse(missing.Update(0.6f, true, 6f).Hit);
        }

        [Test]
        public void OverManyShots_TheHitRateMatchesTheChance()
        {
            var random = new Random(7);
            var attack = new AttackModel(new AttackParameters(), () => (float)random.NextDouble());

            int fired = 0, hits = 0;
            float now = 0f;
            while (fired < 5000)
            {
                AttackResult r = attack.Update(now, true, 6f);
                if (r.Event == AttackEvent.Fired)
                {
                    fired++;
                    if (r.Hit) hits++;
                }
                now += 0.1f;
            }

            Assert.AreEqual(0.5, hits / (double)fired, 0.03);
        }

        [Test]
        public void Reset_ReturnsToIdle()
        {
            var attack = Create();
            attack.Update(0f, true, 5f);
            attack.Reset();

            Assert.AreEqual(AttackState.Idle, attack.State);
        }

        [Test]
        public void InvalidParameters_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackParameters(range: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackParameters(maxAccuracy: 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackParameters(maxAccuracy: 0.5f, minAccuracy: 0.8f));
            Assert.Throws<ArgumentNullException>(() => new AttackModel(null, () => 0f));
            Assert.Throws<ArgumentNullException>(() => new AttackModel(new AttackParameters(), null));
        }
    }
}

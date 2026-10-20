using System;
using NUnit.Framework;

namespace LabLockdown.AI.Hunter.Tests
{
    public class BlockedDoorMemoryTests
    {
        private static BlockedDoorMemory Create(DoorState real = DoorState.Closed, float forget = 20f)
        {
            return new BlockedDoorMemory(new DoorStateTable(real), forget);
        }

        [Test]
        public void UnknownDoors_ReportTheRealState()
        {
            Assert.AreEqual(DoorState.Closed, Create(DoorState.Closed).GetState("Door_A"));
            Assert.AreEqual(DoorState.Open, Create(DoorState.Open).GetState("Door_A"));
            Assert.AreEqual(DoorState.Locked, Create(DoorState.Locked).GetState("Door_A"));
        }

        [Test]
        public void ADoorFoundJammed_ReportsBlockedUntilItIsForgotten()
        {
            BlockedDoorMemory memory = Create(DoorState.Closed, 20f);
            memory.Now = 5f;
            memory.MarkBlocked("Door_A");

            memory.Now = 5f;
            Assert.AreEqual(DoorState.Blocked, memory.GetState("Door_A"));
            memory.Now = 24.9f;
            Assert.AreEqual(DoorState.Blocked, memory.GetState("Door_A"));
            memory.Now = 25.1f;   // 5 + 20: forgotten, trust the real door again
            Assert.AreEqual(DoorState.Closed, memory.GetState("Door_A"));
        }

        [Test]
        public void OtherDoors_AreUnaffected()
        {
            BlockedDoorMemory memory = Create(DoorState.Open);
            memory.MarkBlocked("Door_A");

            Assert.AreEqual(DoorState.Blocked, memory.GetState("Door_A"));
            Assert.AreEqual(DoorState.Open, memory.GetState("Door_B"));
        }

        [Test]
        public void MarkingAgain_ExtendsTheMemory()
        {
            BlockedDoorMemory memory = Create(DoorState.Closed, 20f);
            memory.Now = 0f;
            memory.MarkBlocked("Door_A");
            memory.Now = 15f;
            memory.MarkBlocked("Door_A");   // jammed again: remembered until 35

            memory.Now = 30f;
            Assert.AreEqual(DoorState.Blocked, memory.GetState("Door_A"));
        }

        [Test]
        public void ActiveCount_OnlyCountsDoorsStillRemembered()
        {
            BlockedDoorMemory memory = Create(DoorState.Closed, 10f);
            memory.Now = 0f;
            memory.MarkBlocked("Door_A");
            memory.Now = 6f;
            memory.MarkBlocked("Door_B");

            memory.Now = 8f;
            Assert.AreEqual(2, memory.ActiveCount);
            memory.Now = 12f;   // Door_A expired at 10, Door_B lasts until 16
            Assert.AreEqual(1, memory.ActiveCount);
        }

        [Test]
        public void Clear_ForgetsEverything()
        {
            BlockedDoorMemory memory = Create(DoorState.Closed);
            memory.MarkBlocked("Door_A");
            memory.Clear();

            Assert.AreEqual(DoorState.Closed, memory.GetState("Door_A"));
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new BlockedDoorMemory(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockedDoorMemory(new DoorStateTable(), 0f));
            Assert.Throws<ArgumentException>(() => Create().MarkBlocked(""));
        }
    }
}

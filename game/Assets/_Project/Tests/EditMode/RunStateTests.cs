using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>Covers ownership/equip/slot rules and circuit progression flags.</summary>
    public class RunStateTests : TestBase
    {
        private static PartDef NewPart() => ScriptableObject.CreateInstance<PartDef>();

        [Test]
        public void Equip_RequiresOwnership()
        {
            var run = new RunState();
            var part = NewPart();
            Assert.IsFalse(run.Equip(part)); // unowned
            run.OwnedParts.Add(part);
            Assert.IsTrue(run.Equip(part));
            Assert.IsTrue(run.IsEquipped(part));
        }

        [Test]
        public void Equip_RejectsDuplicates()
        {
            var run = new RunState();
            var part = NewPart();
            run.OwnedParts.Add(part);
            Assert.IsTrue(run.Equip(part));
            Assert.IsFalse(run.Equip(part));
            Assert.AreEqual(1, run.EquippedParts.Count);
        }

        [Test]
        public void Equip_RespectsSlotLimit()
        {
            var run = new RunState { MaxEquipSlots = 2 };
            for (int i = 0; i < 3; i++)
            {
                var p = NewPart();
                run.OwnedParts.Add(p);
                Assert.AreEqual(i < 2, run.Equip(p));
            }
            Assert.AreEqual(2, run.EquippedParts.Count);
            Assert.IsFalse(run.HasFreeSlot);
        }

        [Test]
        public void Unequip_FreesSlot()
        {
            var run = new RunState { MaxEquipSlots = 1 };
            var part = NewPart();
            run.OwnedParts.Add(part);
            run.Equip(part);
            Assert.IsFalse(run.HasFreeSlot);
            Assert.IsTrue(run.Unequip(part));
            Assert.IsTrue(run.HasFreeSlot);
        }

        [Test]
        public void BossRace_IsFinalRaceOfCircuit()
        {
            var run = new RunState { RacesPerCircuit = 3 };
            run.RaceIndex = 0;
            Assert.IsFalse(run.IsBossRace);
            run.RaceIndex = 2;
            Assert.IsTrue(run.IsBossRace);
        }

        [Test]
        public void RunComplete_OnlyAfterFinalRaceCleared()
        {
            var run = new RunState { RacesPerCircuit = 3 };
            run.RaceIndex = 2;
            Assert.IsFalse(run.RunComplete);
            run.RaceIndex = 3;
            Assert.IsTrue(run.RunComplete);
        }
    }
}

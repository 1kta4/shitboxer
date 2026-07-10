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
            // On the final circuit, clearing the last race completes the run.
            var run = new RunState { RacesPerCircuit = 3, TotalCircuits = 3, CircuitIndex = 2 };
            run.RaceIndex = 2;
            Assert.IsFalse(run.RunComplete);
            run.RaceIndex = 3;
            Assert.IsTrue(run.RunComplete);
        }

        [Test]
        public void RunComplete_RequiresFinalCircuit()
        {
            // Clearing a boss race short of the final circuit must NOT complete the run.
            var run = new RunState { RacesPerCircuit = 3, TotalCircuits = 3, CircuitIndex = 0 };
            run.RaceIndex = 3; // first circuit's boss cleared
            Assert.IsFalse(run.IsFinalCircuit);
            Assert.IsFalse(run.RunComplete);

            run.CircuitIndex = 1; // still a middle circuit
            Assert.IsFalse(run.RunComplete);
        }

        [Test]
        public void MidCircuitRace_NeverCompletesRun()
        {
            var run = new RunState { RacesPerCircuit = 3, TotalCircuits = 3, CircuitIndex = 2 };
            for (int race = 0; race < run.RacesPerCircuit - 1; race++)
            {
                run.RaceIndex = race;
                Assert.IsFalse(run.RunComplete, $"race {race} of the final circuit should not end the run");
            }
        }

        [Test]
        public void BossWin_AdvancesToNextCircuit()
        {
            // Mirrors RunDirector's boss-win advance on a non-final circuit.
            var run = new RunState { RacesPerCircuit = 3, TotalCircuits = 3, CircuitIndex = 0 };
            run.RaceIndex = run.RacesPerCircuit - 1;
            Assert.IsTrue(run.IsBossRace);

            run.RaceIndex += 1;            // cleared the boss
            Assert.IsFalse(run.RunComplete); // not the final circuit — advance instead of end
            run.CircuitIndex += 1;
            run.RaceIndex = 0;

            Assert.AreEqual(1, run.CircuitIndex);
            Assert.AreEqual(0, run.RaceIndex);
            Assert.IsFalse(run.IsBossRace);
        }

        [Test]
        public void FullSeason_CompletesOnlyAfterFinalCircuitBoss()
        {
            var run = new RunState { RacesPerCircuit = 3, TotalCircuits = 3 };

            for (int circuit = 0; circuit < run.TotalCircuits; circuit++)
            {
                Assert.AreEqual(circuit, run.CircuitIndex);

                // Regular races of this circuit never complete the run.
                for (int race = 0; race < run.RacesPerCircuit - 1; race++)
                {
                    run.RaceIndex = race;
                    Assert.IsFalse(run.IsBossRace);
                    Assert.IsFalse(run.RunComplete);
                }

                // Boss race, then the win (RaceIndex advances past the last race).
                run.RaceIndex = run.RacesPerCircuit - 1;
                Assert.IsTrue(run.IsBossRace);
                run.RaceIndex += 1;

                bool lastCircuit = circuit == run.TotalCircuits - 1;
                Assert.AreEqual(lastCircuit, run.RunComplete);
                if (!run.RunComplete)
                {
                    run.CircuitIndex += 1;
                    run.RaceIndex = 0;
                }
            }

            Assert.IsTrue(run.RunComplete);
            Assert.AreEqual(run.TotalCircuits - 1, run.CircuitIndex);
        }

        [Test]
        public void IsFinalCircuit_TrueOnlyOnLastCircuit()
        {
            var run = new RunState { TotalCircuits = 3 };
            run.CircuitIndex = 0;
            Assert.IsFalse(run.IsFinalCircuit);
            run.CircuitIndex = 1;
            Assert.IsFalse(run.IsFinalCircuit);
            run.CircuitIndex = 2;
            Assert.IsTrue(run.IsFinalCircuit);
        }

        [Test]
        public void Seed_DefaultsToZero_AndIsSettable()
        {
            var run = new RunState();
            Assert.AreEqual(0, run.Seed); // an unseeded state reads 0; a fresh run rolls a real one
            run.Seed = 4242;
            Assert.AreEqual(4242, run.Seed);
        }

        [Test]
        public void CarDurability_DefaultsToFull_AndIsSettable()
        {
            var run = new RunState();
            Assert.AreEqual(1f, run.CarDurability, 1e-6f); // a fresh run rolls a pristine car
            run.CarDurability = 0.55f;                     // wear carried across races within a run
            Assert.AreEqual(0.55f, run.CarDurability, 1e-6f);
        }

        [Test]
        public void DifficultyMult_StartsAtOneAndRampsUp()
        {
            var run = new RunState();
            run.CircuitIndex = 0;
            Assert.AreEqual(1f, run.DifficultyMult, 1e-4f); // first circuit is the baseline

            run.CircuitIndex = 1;
            float second = run.DifficultyMult;
            run.CircuitIndex = 2;
            float third = run.DifficultyMult;

            Assert.Greater(second, 1f);
            Assert.Greater(third, second); // later circuits are strictly harder
        }

        // ---- Part conditions: Cashout refund + Fragile break (doc 03 modifiers) ---------------

        private static PartDef NewPart(PartCondition condition, int price)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Condition = condition;
            p.Price = price;
            return p;
        }

        [Test]
        public void Condition_DefaultsToPassive()
        {
            // Every existing/newly-created PartDef is Passive so it stays a plain part.
            Assert.AreEqual(PartCondition.Passive, NewPart().Condition);
        }

        [Test]
        public void CashoutRefundTotal_SumsOwnedCashoutPrices()
        {
            var run = new RunState();
            run.OwnedParts.Add(NewPart(PartCondition.Cashout, 8));
            run.OwnedParts.Add(NewPart(PartCondition.Cashout, 10));
            run.OwnedParts.Add(NewPart(PartCondition.Passive, 5));   // ignored
            run.OwnedParts.Add(NewPart(PartCondition.Fragile, 99));  // ignored

            // Only the two Cashout parts' Prices count toward the refund.
            Assert.AreEqual(18, run.CashoutRefundTotal());
        }

        [Test]
        public void CashoutRefundTotal_IsZeroWithNoCashoutParts()
        {
            var run = new RunState();
            run.OwnedParts.Add(NewPart(PartCondition.Passive, 7));
            run.OwnedParts.Add(NewPart(PartCondition.Fragile, 12));
            Assert.AreEqual(0, run.CashoutRefundTotal());
        }

        [Test]
        public void RemovePart_DropsPartFromOwnedAndEquipped()
        {
            var run = new RunState();
            var a = NewPart();
            var b = NewPart();
            run.OwnedParts.Add(a);
            run.OwnedParts.Add(b);
            run.Equip(a);
            run.Equip(b);

            Assert.IsTrue(run.RemovePart(a));
            Assert.IsFalse(run.Owns(a));
            Assert.IsFalse(run.IsEquipped(a));
            // Exactly one removed — the other part is untouched in both lists.
            Assert.AreEqual(1, run.OwnedParts.Count);
            Assert.AreEqual(1, run.EquippedParts.Count);
            Assert.IsTrue(run.Owns(b));
            Assert.IsTrue(run.IsEquipped(b));
        }

        [Test]
        public void RemovePart_ReturnsFalseForNullOrUnowned()
        {
            var run = new RunState();
            Assert.IsFalse(run.RemovePart(null));
            Assert.IsFalse(run.RemovePart(NewPart())); // never owned
        }

        [Test]
        public void FragileBreak_RemovesExactlyOneEquippedFragilePart()
        {
            // Mirrors RunDirector.BreakOneFragilePartOnHeavyDamage: find the first equipped Fragile
            // part and RemovePart it. Exactly one leaves owned+equipped; the rest are untouched.
            var run = new RunState { MaxEquipSlots = 4 };
            var passive = NewPart(PartCondition.Passive, 5);
            var fragile1 = NewPart(PartCondition.Fragile, 10);
            var fragile2 = NewPart(PartCondition.Fragile, 11);
            foreach (var p in new[] { passive, fragile1, fragile2 })
            {
                run.OwnedParts.Add(p);
                run.Equip(p);
            }

            PartDef toBreak = null;
            foreach (PartDef p in run.EquippedParts)
                if (p.Condition == PartCondition.Fragile) { toBreak = p; break; }
            Assert.AreSame(fragile1, toBreak); // the first equipped Fragile part
            run.RemovePart(toBreak);

            Assert.AreEqual(2, run.OwnedParts.Count);    // exactly one of three removed
            Assert.AreEqual(2, run.EquippedParts.Count);
            Assert.IsFalse(run.Owns(fragile1));
            Assert.IsFalse(run.IsEquipped(fragile1));
            // The second Fragile part and the Passive part both survive.
            Assert.IsTrue(run.Owns(fragile2) && run.IsEquipped(fragile2));
            Assert.IsTrue(run.Owns(passive) && run.IsEquipped(passive));
        }
    }
}

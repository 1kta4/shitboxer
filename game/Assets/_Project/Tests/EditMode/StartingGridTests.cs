using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The seeded starting-grid deal. The load-bearing property is that it's a TRUE permutation: a merely
    /// "random" assignment that dropped or repeated a slot would spawn cars inside each other and open the
    /// race with a pile-up. Determinism is the second: a resumed (or later, shared) run must reproduce the
    /// grid it had, the same contract the shop's seeded stock carries.
    /// </summary>
    public class StartingGridTests : TestBase
    {
        private static void AssertIsPermutation(int[] order, int count)
        {
            Assert.AreEqual(count, order.Length, "every car must be dealt a slot");
            var seen = new HashSet<int>(order);
            Assert.AreEqual(count, seen.Count, "no slot may be handed out twice");
            foreach (int slot in order)
                Assert.IsTrue(slot >= 0 && slot < count, $"slot {slot} is off the grid");
        }

        [Test]
        public void Permutation_IsATruePermutation_AtEveryFieldSize()
        {
            // 8 is the shipped field; sweep the rest so a smaller/odd field can't stack cars.
            for (int count = 1; count <= 8; count++)
                AssertIsPermutation(StartingGrid.Permutation(count, seed: 1234 + count), count);
        }

        [Test]
        public void Permutation_IsDeterministicForASeed()
        {
            // A resumed run must line up on the same grid it had before the quit.
            CollectionAssert.AreEqual(
                StartingGrid.Permutation(8, seed: 99),
                StartingGrid.Permutation(8, seed: 99));
        }

        [Test]
        public void Permutation_DiffersAcrossSeeds()
        {
            // Not a guarantee for any particular pair, but across a spread of seeds the deal must move —
            // if it never did, every race of the run would reuse one grid and this whole wave would be a
            // no-op that still looked green.
            int[] baseline = StartingGrid.Permutation(8, seed: 1);
            bool anyDifferent = false;
            for (int seed = 2; seed <= 40 && !anyDifferent; seed++)
            {
                int[] other = StartingGrid.Permutation(8, seed);
                for (int i = 0; i < baseline.Length; i++)
                    if (baseline[i] != other[i]) { anyDifferent = true; break; }
            }
            Assert.IsTrue(anyDifferent, "the grid must actually vary across seeds");
        }

        [Test]
        public void Permutation_DoesNotPinTheFirstCarToPole()
        {
            // The whole point of the wave. The player is cars[0] and the scene authored them onto pole; if
            // slot 0 kept landing on car 0, winning would stay the default and the push-to-win vs
            // hang-back-to-farm decision would still be settled before the lights go out.
            int poleCount = 0;
            const int Samples = 200;
            for (int seed = 0; seed < Samples; seed++)
                if (StartingGrid.Permutation(8, seed)[0] == 0) poleCount++;

            // Expect ~1/8 (25 of 200). Bounds are wide — this pins "not pinned to pole", not the RNG's
            // distribution, so it can't turn flaky on a seed change.
            Assert.Less(poleCount, Samples / 2, "car 0 must not keep pole");
            Assert.Greater(poleCount, 0, "...but must still be able to draw it");
        }

        [Test]
        public void Permutation_SingleCar_IsTheIdentity()
        {
            CollectionAssert.AreEqual(new[] { 0 }, StartingGrid.Permutation(1, seed: 7));
        }

        [Test]
        public void Permutation_NonPositiveCount_IsEmptyNotAThrow()
        {
            // RaceManager guards this already, but the helper is public and must not throw on a degenerate
            // field — a grid deal is never worth taking the race down.
            Assert.AreEqual(0, StartingGrid.Permutation(0, seed: 7).Length);
            Assert.AreEqual(0, StartingGrid.Permutation(-3, seed: 7).Length);
        }
    }
}

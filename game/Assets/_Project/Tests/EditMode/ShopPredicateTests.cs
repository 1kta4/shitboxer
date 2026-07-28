using NUnit.Framework;
using Shitboxer.Meta;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The shared pack-eligibility predicates behind the garage's blocked-pack messages (playtest
    /// finding 2: every refusal must SAY its rule). Pure over RunState — no ScriptableObjects — so the
    /// standalone harness runs these even though the full pack-flow fixtures are editor-only.
    /// </summary>
    public class ShopPredicateTests : TestBase
    {
        [Test]
        public void AFreshRun_HasLevellableComponents()
        {
            Assert.IsTrue(ShopLogic.AnyComponentLevellable(new RunState()));
        }

        [Test]
        public void AllComponentsMaxed_NothingLevellable()
        {
            var run = new RunState();
            for (int i = 0; i < run.ComponentLevels.Length; i++)
                run.ComponentLevels[i] = CarComponentCatalog.MaxLevel;
            Assert.IsFalse(ShopLogic.AnyComponentLevellable(run));
        }

        [Test]
        public void NoFittedParts_NoSpectralTarget()
        {
            Assert.IsFalse(ShopLogic.AnySpectralTarget(new RunState()));
        }

        [Test]
        public void NullRun_IsSafelyIneligible()
        {
            Assert.IsFalse(ShopLogic.AnyComponentLevellable(null));
            Assert.IsFalse(ShopLogic.AnySpectralTarget(null));
            Assert.IsFalse(ShopLogic.IsSpectralTarget(null, null));
        }
    }
}

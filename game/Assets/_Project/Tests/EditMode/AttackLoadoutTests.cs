using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>Covers flattening equipped parts into a race AttackProfile.</summary>
    public class AttackLoadoutTests : TestBase
    {
        private static PartDef Part(PartCategory cat, System.Action<PartDef> cfg = null)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Category = cat;
            cfg?.Invoke(p);
            return p;
        }

        [Test]
        public void Build_Null_ReturnsInertProfile()
        {
            AttackProfile p = AttackLoadout.Build(null);
            Assert.IsFalse(p.IsActive);
        }

        [Test]
        public void Build_IgnoresNonAttackAndNullParts()
        {
            var parts = new[] { Part(PartCategory.Stat, x => x.ContactGripSap = 0.9f), null };
            AttackProfile p = AttackLoadout.Build(parts);
            Assert.That(p.ContactGripSap, Is.EqualTo(0f).Within(1e-5f));
            Assert.IsFalse(p.HasContact);
        }

        [Test]
        public void Build_SumsContactSaps()
        {
            var parts = new[]
            {
                Part(PartCategory.Attack, x => x.ContactGripSap = 0.2f),
                Part(PartCategory.Attack, x => { x.ContactGripSap = 0.1f; x.ContactPowerSap = 0.3f; }),
            };
            AttackProfile p = AttackLoadout.Build(parts);
            Assert.That(p.ContactGripSap, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(p.ContactPowerSap, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.IsTrue(p.HasContact);
        }

        [Test]
        public void Build_TakesWidestAuraRadius_AndSumsAuraSap()
        {
            var parts = new[]
            {
                Part(PartCategory.Attack, x => { x.AuraRadiusM = 6f; x.AuraGripSap = 0.10f; }),
                Part(PartCategory.Attack, x => { x.AuraRadiusM = 10f; x.AuraGripSap = 0.05f; }),
            };
            AttackProfile p = AttackLoadout.Build(parts);
            Assert.That(p.AuraRadiusM, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(p.AuraGripSap, Is.EqualTo(0.15f).Within(1e-4f));
            Assert.IsTrue(p.HasAura);
        }

        [Test]
        public void Build_KeepsInertThresholds()
        {
            AttackProfile p = AttackLoadout.Build(new[] { Part(PartCategory.Attack, x => x.ContactGripSap = 0.2f) });
            AttackProfile none = AttackProfile.None;
            Assert.That(p.MinImpactImpulse, Is.EqualTo(none.MinImpactImpulse).Within(1e-3f));
            Assert.That(p.ContactRecoverPerS, Is.EqualTo(none.ContactRecoverPerS).Within(1e-3f));
        }
    }
}

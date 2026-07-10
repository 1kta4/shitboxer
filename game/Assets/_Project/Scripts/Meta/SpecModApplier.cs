using System.Collections.Generic;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Bakes equipped stat parts into a VehicleSpec. Pure data-in/data-out: the base spec is
    /// deep-copied via a JsonUtility round-trip (VehicleSpec is [Serializable]) so the authored
    /// asset is never mutated. The caller wraps the returned copy in a fresh runtime
    /// VehicleSpecAsset and hands it to VehicleController.SetSpec.
    /// Mods resolve IN EQUIP ORDER into one running factor per target (doc 03's slot-order
    /// depth): SpecModOp.Multiply scales the factor, SpecModOp.Add adds to it, so an Add mod
    /// slotted ahead of a Multiply mod outperforms the reverse. A loadout of pure-Multiply mods
    /// still commutes, so it bakes identically to the old order-independent behaviour.
    /// </summary>
    public static class SpecModApplier
    {
        /// <summary>Deep copy of a spec — JsonUtility round-trip, no Vehicle-assembly changes needed.</summary>
        public static VehicleSpec Clone(VehicleSpec source) =>
            JsonUtility.FromJson<VehicleSpec>(JsonUtility.ToJson(source));

        /// <summary>
        /// Returns a modified deep copy of <paramref name="baseSpec"/> with every equipped
        /// Stat part's SpecMods applied. Non-stat parts and nulls are ignored.
        /// </summary>
        public static VehicleSpec Apply(VehicleSpec baseSpec, IEnumerable<PartDef> equippedParts)
        {
            VehicleSpec spec = Clone(baseSpec);
            if (equippedParts == null) return spec;

            // One running factor per target, walked in equip order (Add adds, Multiply scales),
            // then baked once at the end. Untouched targets never get an entry, so they're left
            // exactly as authored.
            var factor = new Dictionary<SpecModTarget, float>();
            foreach (PartDef part in equippedParts)
            {
                if (part == null || part.Category != PartCategory.Stat) continue;
                foreach (SpecMod mod in part.SpecMods)
                {
                    float current = factor.TryGetValue(mod.Target, out float f) ? f : 1f;
                    factor[mod.Target] = mod.Op == SpecModOp.Add
                        ? current + mod.Multiplier
                        : current * mod.Multiplier;
                }
            }

            foreach (KeyValuePair<SpecModTarget, float> entry in factor)
                BakeTarget(spec, entry.Key, entry.Value);
            return spec;
        }

        private static void BakeTarget(VehicleSpec spec, SpecModTarget target, float m)
        {
            switch (target)
            {
                case SpecModTarget.GripFront:
                    spec.FrontTyre.PeakMu *= m;
                    spec.FrontTyre.SlideMu *= m;
                    break;
                case SpecModTarget.GripRear:
                    spec.RearTyre.PeakMu *= m;
                    spec.RearTyre.SlideMu *= m;
                    break;
                case SpecModTarget.Power:
                    spec.Engine.PeakTorqueNm *= m;
                    break;
                case SpecModTarget.Weight:
                    spec.MassKg *= m;
                    break;
                case SpecModTarget.Downforce:
                    spec.DownforceCoeff *= m;
                    break;
            }
        }
    }
}

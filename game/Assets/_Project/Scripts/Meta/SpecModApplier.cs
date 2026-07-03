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
    /// All mods are multiplicative and order-independent for now — slot-order depth (doc 03)
    /// is a later, optional spice.
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

            foreach (PartDef part in equippedParts)
            {
                if (part == null || part.Category != PartCategory.Stat) continue;
                foreach (SpecMod mod in part.SpecMods)
                    ApplyMod(spec, mod);
            }
            return spec;
        }

        private static void ApplyMod(VehicleSpec spec, SpecMod mod)
        {
            float m = mod.Multiplier;
            switch (mod.Target)
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

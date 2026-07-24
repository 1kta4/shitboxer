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
        /// <summary>
        /// Deep copy of a spec. Delegates to <see cref="VehicleSpec.Clone"/>, which replaced the old
        /// JsonUtility round-trip: that allocated an intermediate string, silently dropped any
        /// non-serialized field, and — the reason it changed — only worked inside the Unity player
        /// loop, so every test of the spec-baking layer skipped instead of running. Null in, null out.
        /// </summary>
        public static VehicleSpec Clone(VehicleSpec source) => source?.Clone();

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
                // Edition amplifies the MAGNITUDE of this part's effect (its deviation from
                // identity), never its sign. Edition.None → edition == 1f → the branch below takes
                // the exact original expression, so un-editioned parts bake byte-for-byte as before.
                float edition = PartEditionInfo.StatMult(part.Edition);
                foreach (SpecMod mod in part.SpecMods)
                {
                    float current = factor.TryGetValue(mod.Target, out float f) ? f : 1f;
                    float amount = edition == 1f
                        ? mod.Multiplier
                        : (mod.Op == SpecModOp.Add
                            ? mod.Multiplier * edition            // Add: Multiplier IS the +fraction effect
                            : 1f + (mod.Multiplier - 1f) * edition);  // Multiply: scale deviation from 1
                    factor[mod.Target] = mod.Op == SpecModOp.Add
                        ? current + amount
                        : current * amount;
                }
            }

            foreach (KeyValuePair<SpecModTarget, float> entry in factor)
                BakeTarget(spec, entry.Key, entry.Value);

            // Final physics clamp (doc 08 decision 1). Stat parts stack multiplicatively, so a loadout
            // CAN already exceed what the car can physically do — six +10% grip parts put GripBox at
            // µ2.34, past the µ2.2 rollover ceiling. The clamp is a no-op for any loadout that stays
            // inside the bands, so ordinary builds bake exactly as before.
            PhysicsCeilings.Clamp(spec);
            return spec;
        }

        /// <summary>
        /// Returns a deep copy of <paramref name="baseSpec"/> with grip and power scaled — the
        /// bot field's stand-in for a shopping trip.
        ///
        /// Bots never equip parts (<see cref="Apply"/> is only ever handed the player's car), so a
        /// stock field falls further behind at every garage: by the last race the player is on
        /// slicks and a turbo while the rivals are still showroom. This scales the same targets a
        /// Stat part would, so a bot at gripMult 1.4 is exactly a bot on better tyres — no separate
        /// cheat path, and BotLimits reads the new grip straight off the spec so the brain actually
        /// drives to it. 1/1 returns an unmodified copy.
        /// </summary>
        public static VehicleSpec Scaled(VehicleSpec baseSpec, float gripMult, float powerMult)
        {
            VehicleSpec spec = Clone(baseSpec);
            BakeTarget(spec, SpecModTarget.GripFront, Mathf.Max(0.01f, gripMult));
            BakeTarget(spec, SpecModTarget.GripRear, Mathf.Max(0.01f, gripMult));
            BakeTarget(spec, SpecModTarget.Power, Mathf.Max(0.01f, powerMult));
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

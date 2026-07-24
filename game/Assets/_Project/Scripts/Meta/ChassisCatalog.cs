using System.Collections.Generic;

namespace Shitboxer.Meta
{
    /// <summary>One selectable chassis, as the car-select screen needs it.</summary>
    public readonly struct ChassisInfo
    {
        public readonly int Id;
        public readonly string Name;
        public readonly string Blurb;
        public readonly string UnlockFlag; // null = always available

        public ChassisInfo(int id, string name, string blurb, string unlockFlag)
        {
            Id = id;
            Name = name;
            Blurb = blurb;
            UnlockFlag = unlockFlag;
        }
    }

    /// <summary>
    /// The selectable chassis. Ids map straight into <c>RunDirector.chassisSpecs</c>
    /// (0 GripBox, 1 PowerBox, 2 Brute). Locked entries are shown greyed until their MetaProgress
    /// flag is earned. The Brute is real since doc 08 slice 11 — the first of the 15-car list, and
    /// the first chassis whose character lives in the decision-15 damage model: 1600 kg of RWD lug
    /// with WearExponent 0.4 (76% pace at half durability — barely notices a beating) and authored
    /// DamageResistance 0.25, paid for with the field's worst grip and gentle steering.
    /// </summary>
    public static class ChassisCatalog
    {
        /// <summary>The Brute's MetaProgress unlock flag — granted by RunDirector on a season clear.</summary>
        public const string BruteUnlockFlag = "chassis_brute";

        public static readonly IReadOnlyList<ChassisInfo> All = new[]
        {
            new ChassisInfo(0, "GRIP BOX",  "Cornering and bite. The all-rounder starter.", null),
            new ChassisInfo(1, "POWER BOX", "Straight-line muscle, looser rear. Starter.",  null),
            new ChassisInfo(2, "THE BRUTE", "Heavy, hits hard, shrugs damage. Clear a season to unlock.", BruteUnlockFlag),
        };

        public static bool IsUnlocked(ChassisInfo c, MetaProgress meta) =>
            c.UnlockFlag == null || (meta != null && meta.IsUnlocked(c.UnlockFlag));
    }
}

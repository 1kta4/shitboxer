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
    /// (0 GripBox, 1 PowerBox, 2 Brute, 3 Kart, 4 Open Wheeler). Locked entries are shown greyed
    /// until their MetaProgress flag is earned. Ids 2-4 are the 15-car list (doc 08 slices 11/15),
    /// each defined by the decision-15 damage model rather than a stat spread: the Brute shrugs
    /// wear off (WearExponent 0.4, resistance 0.25), the Kart's "very low health" is 750 kg of
    /// momentum physics with no damage field authored at all, and the Open Wheeler is all grip on
    /// an exponent-2 wear curve — crippled hard the moment it gets hit.
    /// </summary>
    public static class ChassisCatalog
    {
        /// <summary>The Brute's catalog id — RecordRunEndToMeta reads it for the Kart's chained unlock.</summary>
        public const int BruteId = 2;

        /// <summary>The Brute's MetaProgress unlock flag — granted by RunDirector on a season clear.</summary>
        public const string BruteUnlockFlag = "chassis_brute";

        /// <summary>The Kart's flag — granted for clearing a season IN The Brute (the list chains).</summary>
        public const string KartUnlockFlag = "chassis_kart";

        /// <summary>The Open Wheeler's flag — granted for clearing a season with the car barely scratched.</summary>
        public const string OpenWheelerUnlockFlag = "chassis_openwheeler";

        public static readonly IReadOnlyList<ChassisInfo> All = new[]
        {
            new ChassisInfo(0, "GRIP BOX",  "Cornering and bite. The all-rounder starter.", null),
            new ChassisInfo(1, "POWER BOX", "Straight-line muscle, looser rear. Starter.",  null),
            new ChassisInfo(2, "THE BRUTE", "Heavy, hits hard, shrugs damage. Clear a season to unlock.", BruteUnlockFlag),
            new ChassisInfo(3, "THE KART", "Featherweight scalpel — every shove hurts. Clear a season in The Brute.", KartUnlockFlag),
            new ChassisInfo(4, "THE OPEN WHEELER", "All grip, no armour. Crippled fast when hit. Clear a season barely scratched.", OpenWheelerUnlockFlag),
        };

        public static bool IsUnlocked(ChassisInfo c, MetaProgress meta) =>
            c.UnlockFlag == null || (meta != null && meta.IsUnlocked(c.UnlockFlag));
    }
}

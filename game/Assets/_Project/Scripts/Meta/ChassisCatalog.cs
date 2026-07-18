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
    /// The selectable chassis. Ids 0/1 map to <c>RunDirector.chassisSpecs</c> (Grip / Power boxes).
    /// Locked entries have no spec yet — they are the meta unlock hooks (see the roadmap's "unlock a
    /// second chassis / The Brute"): shown greyed until their flag is earned.
    /// </summary>
    public static class ChassisCatalog
    {
        public static readonly IReadOnlyList<ChassisInfo> All = new[]
        {
            new ChassisInfo(0, "GRIP BOX",  "Cornering and bite. The all-rounder starter.", null),
            new ChassisInfo(1, "POWER BOX", "Straight-line muscle, looser rear. Starter.",  null),
            new ChassisInfo(2, "THE BRUTE", "Heavy, hits hard. Clear a season to unlock.",  "chassis_brute"),
        };

        public static bool IsUnlocked(ChassisInfo c, MetaProgress meta) =>
            c.UnlockFlag == null || (meta != null && meta.IsUnlocked(c.UnlockFlag));
    }
}

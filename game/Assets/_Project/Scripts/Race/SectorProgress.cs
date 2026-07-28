using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Pure sector-counting math, the exact structural twin of <see cref="LapProgress"/> with a smaller
    /// divisor. A car's guarded net forward distance is measured in metres from the start/finish line
    /// (arc-length 0), so every whole multiple of a sector length is one completed sector. No engine,
    /// scene or clock state, so a headless server steps it identically.
    ///
    /// <b>Sectors are a READOUT, never a gate.</b> They are derived entirely from the same
    /// <c>RaceCarStatus.TotalDistanceM</c> the lap gate already trusts — which means they inherit its
    /// teleport/mis-projection guard for free and can never strand a driver. The ordered-checkpoint ring
    /// that preceded the distance gate was removed precisely because it hard-reset a human's progress
    /// every time that guard fired; re-expressing sectors as trigger volumes or an ordered ring would
    /// bring that bug straight back. Nothing here validates anything.
    /// </summary>
    public static class SectorProgress
    {
        /// <summary>Sectors a lap is divided into. Three, F1-style, split by equal DISTANCE not equal time.</summary>
        public const int DefaultSectorsPerLap = 3;

        /// <summary>
        /// Length of one sector on a loop of <paramref name="lapLengthM"/> metres — an equal split by
        /// distance, so a sector boundary can land anywhere on the track including mid-corner. Zero for a
        /// non-positive loop or sector count, which every consumer below reads as "no sectors".
        /// </summary>
        public static float SectorLength(float lapLengthM, int sectorsPerLap = DefaultSectorsPerLap) =>
            lapLengthM <= 0f || sectorsPerLap <= 0 ? 0f : lapLengthM / sectorsPerLap;

        /// <summary>
        /// Whole sectors completed for a guarded forward distance, counting continuously across laps —
        /// so on a 3-sector track the 4th completed sector is sector 1 of lap 2. A non-positive sector
        /// length or negative distance yields 0 (grid cars start slightly behind the line, which must
        /// not read as a completed sector).
        /// </summary>
        public static int CompletedSectors(float totalDistanceM, float sectorLengthM) =>
            sectorLengthM <= 0f ? 0 : Mathf.Max(0, Mathf.FloorToInt(totalDistanceM / sectorLengthM));

        /// <summary>
        /// The 0-based sector-within-a-lap a car is currently DRIVING, given how many it has completed.
        /// Having completed 4 sectors on a 3-sector track puts you in sector index 1 (the second sector
        /// of lap 2). Guards a non-positive sector count and negative input.
        /// </summary>
        public static int SectorIndex(int completedSectors, int sectorsPerLap = DefaultSectorsPerLap)
        {
            if (sectorsPerLap <= 0) return 0;
            int index = completedSectors % sectorsPerLap;
            return index < 0 ? index + sectorsPerLap : index;
        }

        /// <summary>
        /// Total sectors in a whole race — the bound sector crediting stops at, mirroring how lap
        /// crediting stops at the race's lap count. Zero for a non-positive lap or sector count.
        /// </summary>
        public static int TotalSectors(int totalLaps, int sectorsPerLap = DefaultSectorsPerLap) =>
            totalLaps <= 0 || sectorsPerLap <= 0 ? 0 : totalLaps * sectorsPerLap;
    }
}

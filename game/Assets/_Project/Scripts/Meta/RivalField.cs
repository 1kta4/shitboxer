using Shitboxer.Race;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Draws which rivals from the career roster show up in THIS run, and gives each one a stable identity
    /// seed. Pure and static so it is unit-testable with no scene and reproduces bit-for-bit on a headless
    /// server — same discipline as <see cref="StartingGrid"/> and <c>BotDriver.ResolveRivalConfig</c>.
    /// </summary>
    public static class RivalField
    {
        /// <summary>
        /// The rivals contesting this run: entry i is the roster index racing in grid slot i.
        ///
        /// Depends ONLY on its three arguments, which is the load-bearing property. <c>RunDirector</c> calls
        /// this at every <c>BindToScene</c> — once per race, and again on a resume — so the field must be
        /// reproducible from the run seed alone. That is what lets the drawn field persist across the scene
        /// reload, the track rotation, and a quit-and-resume without <c>RunSave</c> storing a single byte of it.
        ///
        /// A roster smaller than <paramref name="slots"/> wraps, so a rival may appear twice in one race. That
        /// is documented and harmless (both cars share one memory — they are the same driver), but the shipped
        /// 24-rival roster comfortably exceeds the 7-car grid so it should never happen in practice.
        /// </summary>
        public static int[] Draw(int runSeed, int rosterCount, int slots)
        {
            if (slots <= 0 || rosterCount <= 0) return new int[0];

            // One shuffle implementation in the codebase. Fisher-Yates over a seeded System.Random, so the
            // draw is a true permutation — no rival is handed out twice while another goes unused.
            int[] order = StartingGrid.Permutation(rosterCount, runSeed);

            var field = new int[slots];
            for (int i = 0; i < slots; i++) field[i] = order[i % rosterCount];
            return field;
        }

        /// <summary>
        /// The stable per-rival seed that drives their on-track variety draw (archetype tier, skill band,
        /// mistake pattern).
        ///
        /// WHY IT HASHES THE ID. Previously a bot's character came from
        /// <c>rivalVarietySeed * SeedStride + transform.GetSiblingIndex()</c> — its position in the scene
        /// hierarchy. That means character followed the GRID SLOT: the car starting third was always the same
        /// flavour of driver, and a rival was a different driver on every track. Hashing the id instead makes
        /// character follow the NAME, so Vera Kestrel drives like Vera Kestrel in every race, on every track,
        /// in every run of the career. That consistency is the whole point — a memory is worthless if the
        /// thing it is attached to behaves differently each time you meet it.
        ///
        /// Uses FNV-1a rather than <c>string.GetHashCode()</c>, which is DELIBERATELY randomised per process
        /// on modern .NET runtimes. Using it here would give a rival a different personality on every launch
        /// and diverge between a client and a headless server — silently, and only in ways a player would
        /// feel rather than see in a log.
        ///
        /// Never returns 0, which <c>BotDriver</c> reserves to mean "no identity pushed, use the legacy
        /// sibling-index seed".
        /// </summary>
        public static int IdentitySeed(string rivalId)
        {
            if (string.IsNullOrEmpty(rivalId)) return 0;

            unchecked
            {
                const uint FnvOffsetBasis = 2166136261u;
                const uint FnvPrime = 16777619u;

                uint hash = FnvOffsetBasis;
                for (int i = 0; i < rivalId.Length; i++)
                {
                    hash ^= rivalId[i];
                    hash *= FnvPrime;
                }

                int seed = (int)hash;
                return seed != 0 ? seed : 1; // 0 is reserved for "unidentified"
            }
        }

        /// <summary>
        /// The opaque per-race key the observation layer tags a rival's events with. Derived from the grid
        /// slot rather than the roster id because it only has to be unique WITHIN one race — the Race layer
        /// has no business knowing roster ids, and <c>RunDirector</c> maps the key back to an id at race end.
        /// 0 is reserved for the player, so slots start at 1.
        /// </summary>
        public static int KeyForSlot(int slot) => slot + 1;
    }
}

namespace Shitboxer.Race
{
    /// <summary>
    /// Assigns cars to starting-grid slots.
    ///
    /// WHY THIS EXISTS. The race scene authored the player onto pole and left them there — the scene
    /// builder says as much: "Player on pole so the first race is easy to observe." That was a Phase-1
    /// debugging convenience, and because every race reloads the same scene it silently became the shipped
    /// rule: the player started P1 in every race of every circuit, forever.
    ///
    /// It quietly settles the question the whole run is built around. doc 03's signature tension is "push to
    /// win, or hang back to farm?" — and that is only a decision if BOTH options cost something. Starting
    /// ahead of the entire field makes winning the DEFAULT: you'd have to deliberately throw a race to farm,
    /// and the survival cutoff is nearly free. Starting dead last would break it the other way. A grid that
    /// averages to mid-pack is what makes both branches live.
    ///
    /// Pure and static so it's unit-testable with no scene, matching BotBrain/BotDifficulty.
    /// </summary>
    public static class StartingGrid
    {
        /// <summary>
        /// A deterministic permutation of <paramref name="carCount"/> grid slots: entry i is the slot the
        /// i-th car takes. Fisher-Yates over a seeded RNG, so a resumed — or, later, a shared multiplayer —
        /// run reproduces the exact grid it had.
        ///
        /// Always a TRUE permutation: every car gets exactly one slot and no slot is handed out twice.
        /// That's the load-bearing property — a merely "random" assignment that dropped or doubled a slot
        /// would stack cars inside each other on the grid and the race would open with a pile-up.
        ///
        /// Returns an empty array for a non-positive count, and the identity for a single car.
        /// </summary>
        public static int[] Permutation(int carCount, int seed)
        {
            if (carCount <= 0) return new int[0];

            var order = new int[carCount];
            for (int i = 0; i < carCount; i++) order[i] = i;

            // System.Random (not UnityEngine.Random): seeded, self-contained, and it leaves Unity's global
            // RNG state alone — the same reason the shop draws through its own instance.
            var rng = new System.Random(seed);
            for (int i = carCount - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }
    }
}

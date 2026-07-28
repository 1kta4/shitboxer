namespace Shitboxer.Meta
{
    /// <summary>
    /// Balatro-foil-style cosmetic-plus tier stamped on a part (doc 03's "editions"). None (0) is
    /// the default so every existing PartDef asset deserializes as an unmodified part — see
    /// PartEditionInfo, where None maps to a literal 1x on both stat effect and price, so nothing
    /// about today's numbers moves. Higher tiers amplify the MAGNITUDE of the part's stat effect
    /// (SpecModApplier scales each SpecMod's deviation from identity, never its sign) and may cost
    /// more to stock.
    /// </summary>
    public enum PartEdition
    {
        None,        // 1.00x effect — today's exact numbers
        Foil,        // 1.25x effect
        Holo,        // 1.50x effect
        Polychrome,  // 2.00x effect
    }

    /// <summary>
    /// Pure-C# edition → multiplier lookups (no scene refs, no Unity loop deps — headless-safe).
    /// Both helpers return exactly 1f for <see cref="PartEdition.None"/> (and for any unmapped
    /// value), so the default edition is a true identity everywhere it is consumed.
    /// </summary>
    public static class PartEditionInfo
    {
        /// <summary>
        /// How much an edition amplifies a part's stat EFFECT (its deviation from identity).
        /// None == 1f, so an un-editioned part bakes to exactly today's numbers.
        /// </summary>
        public static float StatMult(PartEdition edition)
        {
            switch (edition)
            {
                case PartEdition.Foil: return 1.25f;
                case PartEdition.Holo: return 1.5f;
                case PartEdition.Polychrome: return 2f;
                default: return 1f;   // None (and any future/unknown value) → identity
            }
        }

        /// <summary>
        /// Price multiplier for stocking an edition part. None == 1f (today's price). Provided as
        /// the runtime mechanism only — no existing pricing path calls it yet, so prices are
        /// unchanged until a later shop-side step opts in.
        /// </summary>
        public static float PriceMult(PartEdition edition)
        {
            switch (edition)
            {
                case PartEdition.Foil: return 1.5f;
                case PartEdition.Holo: return 2f;
                case PartEdition.Polychrome: return 3f;
                default: return 1f;   // None (and any future/unknown value) → identity
            }
        }
    }
}

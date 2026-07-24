namespace Shitboxer.Meta
{
    /// <summary>
    /// What a booster pack contains. Two are offered every visit; buying one opens a pick-one draw.
    ///
    /// <see cref="Spectral"/> is DECLARED BUT NOT YET ROLLED — see
    /// <see cref="ShopPackCatalog.Weight"/>. Its content (the source document's high-risk transform
    /// consumables) does not exist, and stocking a pack that opens onto nothing would be worse than
    /// not offering it. The member exists so the save format, the UI and the draw table are already
    /// shaped for it.
    /// </summary>
    public enum ShopPackKind
    {
        /// <summary>Draws parts; pick one, the rest are gone.</summary>
        Parts,
        /// <summary>Draws components; pick one to level up.</summary>
        Components,
        /// <summary>Not implemented — see the type remarks.</summary>
        Spectral,
    }

    /// <summary>One pack on the shelf: what it holds, what it costs, and how many it draws.</summary>
    public readonly struct ShopPack
    {
        public readonly ShopPackKind Kind;
        public readonly int Price;
        /// <summary>How many options the pack lays out. The player keeps exactly one.</summary>
        public readonly int DrawCount;

        public ShopPack(ShopPackKind kind, int price, int drawCount)
        {
            Kind = kind;
            Price = price;
            DrawCount = drawCount;
        }

        public string DisplayName => ShopPackCatalog.DisplayName(Kind);
    }

    /// <summary>
    /// Pack pricing, draw sizes and shelf frequency — the single place a pack's economy is defined.
    /// Pure static data so a headless server rolls an identical shelf.
    /// </summary>
    public static class ShopPackCatalog
    {
        /// <summary>Every kind, in the order the draw table walks them.</summary>
        public static readonly ShopPackKind[] All =
        {
            ShopPackKind.Parts,
            ShopPackKind.Components,
            ShopPackKind.Spectral,
        };

        /// <summary>
        /// Relative shelf frequency. <see cref="ShopPackKind.Spectral"/> weighs ZERO because its
        /// contents are not built — a pack that opened onto an empty pick screen would take the
        /// player's money and hand back nothing. Give it a weight the day spectrals exist.
        /// </summary>
        public static int Weight(ShopPackKind kind)
        {
            switch (kind)
            {
                case ShopPackKind.Parts: return 55;
                case ShopPackKind.Components: return 45;
                default: return 0;   // Spectral — not implemented
            }
        }

        /// <summary>
        /// Sticker price. A components pack is cheaper than a parts pack because what it hands over is
        /// one level of one component — real, but far smaller than a part — and it competes with simply
        /// buying a Blueprint outright.
        /// </summary>
        public static int Price(ShopPackKind kind)
        {
            switch (kind)
            {
                case ShopPackKind.Parts: return 6;
                case ShopPackKind.Components: return 4;
                default: return 8;
            }
        }

        /// <summary>How many options the pack lays out; the player keeps one.</summary>
        public static int DrawCount(ShopPackKind kind)
        {
            switch (kind)
            {
                case ShopPackKind.Parts: return 3;
                case ShopPackKind.Components: return 3;
                default: return 3;
            }
        }

        public static string DisplayName(ShopPackKind kind)
        {
            switch (kind)
            {
                case ShopPackKind.Parts: return "PARTS PACK";
                case ShopPackKind.Components: return "COMPONENTS PACK";
                default: return "SPECTRAL PACK";
            }
        }

        /// <summary>A pack of this kind at its catalogue price and draw size.</summary>
        public static ShopPack Make(ShopPackKind kind) =>
            new ShopPack(kind, Price(kind), DrawCount(kind));
    }
}

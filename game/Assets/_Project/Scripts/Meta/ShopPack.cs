namespace Shitboxer.Meta
{
    /// <summary>
    /// What a booster pack contains. Two are offered every visit; buying one opens a pick-one draw.
    /// </summary>
    public enum ShopPackKind
    {
        /// <summary>Draws parts; pick one, the rest are gone.</summary>
        Parts,
        /// <summary>Draws components; pick one to level up.</summary>
        Components,
        /// <summary>Draws edition materials aimed at FITTED stat parts (doc 08 slice 13 — editions
        /// as materials); pick one, and that part is Foil/Holo/Polychrome for the rest of the run.</summary>
        Spectral,
    }

    /// <summary>
    /// One Spectral-pack offer — an edition aimed at a specific fitted part — and its string codec.
    /// Encoded as "Edition:partId" because the open pack lives on <see cref="RunState.PackSpectrals"/>
    /// as plain strings (serializable, save-safe); this is the ONE place the format is known.
    /// Pure static, headless-safe.
    /// </summary>
    public static class SpectralOffer
    {
        /// <summary>The applyable tiers, lowest first — the same order upgrades must walk.</summary>
        public static readonly PartEdition[] Tiers =
        {
            PartEdition.Foil,
            PartEdition.Holo,
            PartEdition.Polychrome,
        };

        /// <summary>
        /// Draw weight per tier: Foil is the common pull, Polychrome the jackpot. Zero for None (a
        /// material that does nothing is never offered) and for any unknown future member.
        /// </summary>
        public static int Weight(PartEdition edition)
        {
            switch (edition)
            {
                case PartEdition.Foil: return 60;
                case PartEdition.Holo: return 30;
                case PartEdition.Polychrome: return 10;
                default: return 0;
            }
        }

        public static string Encode(PartEdition edition, string partId) => $"{edition}:{partId}";

        /// <summary>
        /// Decode one stored offer. False (with junk-safe outs) for anything unparseable — a stale
        /// or hand-edited save line is dropped rather than thrown on.
        /// </summary>
        public static bool TryDecode(string encoded, out PartEdition edition, out string partId)
        {
            edition = PartEdition.None;
            partId = null;
            if (string.IsNullOrEmpty(encoded)) return false;
            int split = encoded.IndexOf(':');
            if (split <= 0 || split >= encoded.Length - 1) return false;
            if (!System.Enum.TryParse(encoded.Substring(0, split), out edition)) return false;
            if (!System.Enum.IsDefined(typeof(PartEdition), edition) || edition == PartEdition.None) return false;
            partId = encoded.Substring(split + 1);
            return true;
        }
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
        /// Relative shelf frequency. Spectral is the scarce one — its prize (a permanent stat
        /// amplifier on a fitted part) outclasses one component level, so it must not show up as
        /// often. Buying one is still refused when NO fitted part can take an edition (see
        /// ShopLogic.TryBuyPack) — the slice-4 rule that a pack whose prize can't be taken is
        /// never sold.
        /// </summary>
        public static int Weight(ShopPackKind kind)
        {
            switch (kind)
            {
                case ShopPackKind.Parts: return 50;
                case ShopPackKind.Components: return 40;
                case ShopPackKind.Spectral: return 15;
                default: return 0;
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

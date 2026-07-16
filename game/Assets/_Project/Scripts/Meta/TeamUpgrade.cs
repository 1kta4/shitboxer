using System.Collections.Generic;

namespace Shitboxer.Meta
{
    /// <summary>
    /// doc 03's "permanent team upgrades" — Balatro's vouchers. Bought once in the garage, they apply for
    /// the REST OF THE RUN rather than to a single race, which makes them the shop's only genuinely
    /// long-horizon purchase: every other option (parts, crates, repairs) pays off in the next race.
    ///
    /// Deliberately an enum + a code-side table rather than ScriptableObject assets like PartDef. The line
    /// the project draws (set by crates in wave 17): **content is assets, shop RULES are code.** Parts are
    /// open-ended content with per-asset balance data; upgrades are four fixed rules that reach into
    /// ShopLogic/RunState internals. Making them assets would also buy into the .asset + MetaAssetsBuilder
    /// duplication this repo already gets bitten by — wave 13 shipped its code and left its assets
    /// uncommitted, so a fresh clone silently lost the content.
    ///
    /// NOTE ON HORIZON: an upgrade bought in garage 1 of a 1-circuit (3-race) run only pays back across two
    /// more garages, so at the shipped season length these are structurally weak by construction. They are
    /// built for the full 8-circuit season; judge their pricing with RunDirector.totalCircuits raised.
    /// </summary>
    public enum TeamUpgrade
    {
        /// <summary>Overstock — the shelf shows an extra part every roll.</summary>
        Overstock,

        /// <summary>Reroll Surplus — every visit's reroll chain starts cheaper.</summary>
        RerollSurplus,

        /// <summary>Toolbox — one more part can be slotted onto the car.</summary>
        Toolbox,

        /// <summary>Bulk Buyer — crates draw an extra part to pick from.</summary>
        BulkBuyer,
    }

    /// <summary>Shop-facing display data for one team upgrade.</summary>
    public readonly struct TeamUpgradeInfo
    {
        public readonly TeamUpgrade Upgrade;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly int Price;

        public TeamUpgradeInfo(TeamUpgrade upgrade, string displayName, string description, int price)
        {
            Upgrade = upgrade;
            DisplayName = displayName;
            Description = description;
            Price = price;
        }
    }

    /// <summary>
    /// The team-upgrade catalogue and — more importantly — the single place every upgrade's EFFECT is
    /// computed. Each effect is a pure static taking the run, so the magnitudes can't scatter across
    /// ShopLogic/RunState/RunDirector and quietly disagree, and every one is unit-testable without a scene.
    ///
    /// Every effect returns 0 for a null run or an un-owned upgrade, so a run that has bought nothing
    /// reproduces the shipped shop byte-for-byte.
    /// </summary>
    public static class TeamUpgrades
    {
        /// <summary>Extra parts on the shelf per roll, with Overstock owned.</summary>
        public const int OverstockExtraOffers = 1;

        /// <summary>$ knocked off the base reroll cost, with Reroll Surplus owned. Floored at $1, never free.</summary>
        public const int RerollSurplusDiscount = 2;

        /// <summary>Extra equip slots, with Toolbox owned.</summary>
        public const int ToolboxExtraSlots = 1;

        /// <summary>Extra parts a crate draws, with Bulk Buyer owned.</summary>
        public const int BulkBuyerExtraDraws = 1;

        /// <summary>Every upgrade, in shop display order.</summary>
        public static readonly IReadOnlyList<TeamUpgrade> All = new[]
        {
            TeamUpgrade.Overstock,
            TeamUpgrade.RerollSurplus,
            TeamUpgrade.Toolbox,
            TeamUpgrade.BulkBuyer,
        };

        /// <summary>
        /// Display data for an upgrade. Prices sit above a typical part ($3–$12) because these never expire:
        /// the cost is meant to hurt now and repay over the remaining garages. A tuning target — see the
        /// horizon note on <see cref="TeamUpgrade"/>.
        /// </summary>
        public static TeamUpgradeInfo Info(TeamUpgrade upgrade)
        {
            switch (upgrade)
            {
                case TeamUpgrade.Overstock:
                    return new TeamUpgradeInfo(upgrade, "Overstock",
                        $"+{OverstockExtraOffers} part on the shelf, every roll.", 10);
                case TeamUpgrade.RerollSurplus:
                    return new TeamUpgradeInfo(upgrade, "Reroll Surplus",
                        $"Rerolls start ${RerollSurplusDiscount} cheaper each visit.", 8);
                case TeamUpgrade.Toolbox:
                    return new TeamUpgradeInfo(upgrade, "Toolbox",
                        $"+{ToolboxExtraSlots} equip slot on the car.", 12);
                case TeamUpgrade.BulkBuyer:
                    return new TeamUpgradeInfo(upgrade, "Bulk Buyer",
                        $"Crates draw +{BulkBuyerExtraDraws} part to pick from.", 8);
                default:
                    return new TeamUpgradeInfo(upgrade, upgrade.ToString(), string.Empty, 0);
            }
        }

        /// <summary>Price of an upgrade. 0 for anything unknown, so a bad value can never charge.</summary>
        public static int PriceOf(TeamUpgrade upgrade) => Info(upgrade).Price;

        private static bool Has(RunState run, TeamUpgrade upgrade) => run != null && run.HasUpgrade(upgrade);

        /// <summary>Extra shelf offers this run has earned. 0 without Overstock — i.e. the shipped shelf.</summary>
        public static int ExtraShopOffers(RunState run) =>
            Has(run, TeamUpgrade.Overstock) ? OverstockExtraOffers : 0;

        /// <summary>$ off the base reroll this run has earned. 0 without Reroll Surplus.</summary>
        public static int RerollDiscount(RunState run) =>
            Has(run, TeamUpgrade.RerollSurplus) ? RerollSurplusDiscount : 0;

        /// <summary>Extra equip slots this run has earned. 0 without Toolbox.</summary>
        public static int ExtraEquipSlots(RunState run) =>
            Has(run, TeamUpgrade.Toolbox) ? ToolboxExtraSlots : 0;

        /// <summary>Extra crate draws this run has earned. 0 without Bulk Buyer.</summary>
        public static int ExtraCrateDraws(RunState run) =>
            Has(run, TeamUpgrade.BulkBuyer) ? BulkBuyerExtraDraws : 0;
    }
}

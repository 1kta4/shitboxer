using Shitboxer.Meta;

namespace Shitboxer.UI.Model
{
    /// <summary>
    /// A BEFORE -> AFTER stat pair for one headline stat (Grip or Power) if a part were equipped.
    /// <see cref="Sign"/> keeps the shipped 0.5 deadband from GarageScreen.DrawStatDelta, so a change
    /// smaller than half a bar-point reads as no change (never a misleading "+0"/"-0").
    /// </summary>
    public readonly struct StatDelta
    {
        public readonly float Before;
        public readonly float After;

        public StatDelta(float before, float after)
        {
            Before = before;
            After = after;
        }

        public float Delta => After - Before;

        /// <summary>-1 loss / 0 negligible / +1 gain, with the shipped 0.5-point deadband.</summary>
        public int Sign => System.Math.Abs(Delta) < 0.5f ? 0 : (Delta > 0f ? 1 : -1);
    }

    /// <summary>
    /// A shop offer (or a crate item) as the view needs it: everything to render one row and gate its
    /// BUY, with no draw code and no live-object access. <see cref="Price"/> is the SHOP price
    /// (<c>ShopLogic.PriceOf</c>), never <c>part.Price</c> — TryBuy charges PriceOf, so the label and
    /// the affordability gate must read the same figure or they would disagree the moment the
    /// rarity/family pricing tables are turned on.
    /// </summary>
    public readonly struct OfferVm
    {
        public readonly PartDef Part;
        public readonly string Name;
        public readonly PartCategory Category;
        public readonly int Price;
        public readonly PartEdition Edition;
        public readonly string EditionTag;
        public readonly string Description;
        /// <summary>True only when the part can ACTUALLY be bought — money AND a free slot, since a
        /// bought part is always fitted. The screen reads <c>GarageViewModel.CarIsFull</c> to say which
        /// of the two is blocking, or "I have money and it won't let me buy" reads as a bug.</summary>
        public readonly bool Affordable;
        public readonly bool HasStatPreview;
        public readonly StatDelta Grip;
        public readonly StatDelta Power;
        public readonly StatDelta Weight;
        public readonly StatDelta Durability;

        public OfferVm(PartDef part, string name, PartCategory category, int price, PartEdition edition,
            string editionTag, string description, bool affordable, bool hasStatPreview,
            StatDelta grip, StatDelta power, StatDelta weight = default, StatDelta durability = default)
        {
            Part = part;
            Name = name;
            Category = category;
            Price = price;
            Edition = edition;
            EditionTag = editionTag;
            Description = description;
            Affordable = affordable;
            HasStatPreview = hasStatPreview;
            Grip = grip;
            Power = power;
            Weight = weight;
            Durability = durability;
        }
    }

    /// <summary>
    /// A part fitted to the car. Since a bought part is always equipped there is no benched state and
    /// no EQUIP action — the only thing you can do to a fitted part is SELL it, which frees its slot.
    /// <see cref="Equipped"/> is kept because a Fragile part can be destroyed mid-run, leaving a brief
    /// owned-but-unfitted window the list still has to render honestly.
    /// </summary>
    public readonly struct OwnedPartVm
    {
        public readonly PartDef Part;
        public readonly string Name;
        public readonly PartCategory Category;
        public readonly PartEdition Edition;
        public readonly string EditionTag;
        public readonly bool Equipped;
        /// <summary>What selling refunds — half the shop price, floored at $1.</summary>
        public readonly int SellValue;

        public OwnedPartVm(PartDef part, string name, PartCategory category, PartEdition edition,
            string editionTag, bool equipped, int sellValue)
        {
            Part = part;
            Name = name;
            Category = category;
            Edition = edition;
            EditionTag = editionTag;
            Equipped = equipped;
            SellValue = sellValue;
        }
    }

    /// <summary>One of the visit's two booster packs, as the shelf needs it.</summary>
    public readonly struct PackVm
    {
        /// <summary>Index into <c>ShopLogic.Packs</c> — what the buy command takes.</summary>
        public readonly int Index;
        public readonly ShopPackKind Kind;
        public readonly string Name;
        public readonly int Price;
        public readonly int DrawCount;
        public readonly bool Affordable;
        /// <summary>False when the pack cannot be opened right now (a full car for a parts pack).</summary>
        public readonly bool Buyable;

        public PackVm(int index, ShopPackKind kind, string name, int price, int drawCount,
            bool affordable, bool buyable)
        {
            Index = index;
            Kind = kind;
            Name = name;
            Price = price;
            DrawCount = drawCount;
            Affordable = affordable;
            Buyable = buyable;
        }
    }

    /// <summary>
    /// One pick from an open Spectral pack (doc 08 slice 13): an edition material pre-aimed at a
    /// specific fitted part. Already paid for at pack-buy time, so the row carries no price — the
    /// pick is the whole decision.
    /// </summary>
    public readonly struct SpectralVm
    {
        public readonly PartDef Part;
        public readonly PartEdition Edition;
        /// <summary>The full row text — "[FOIL x1.25] → JUNKYARD TURBO".</summary>
        public readonly string Label;

        public SpectralVm(PartDef part, PartEdition edition, string label)
        {
            Part = part;
            Edition = edition;
            Label = label;
        }
    }

    /// <summary>
    /// One car component: what it is, what level it sits at, and what the next Blueprint costs. Serves
    /// all three places a component appears, which differ only in what the row lets you DO —
    ///
    /// <list type="bullet">
    /// <item>the ten-row status list, where the row is a read-out and nothing is buyable;</item>
    /// <item>a Blueprint on the shelf, bought at <see cref="Price"/> if <see cref="Affordable"/>;</item>
    /// <item>a pick from an open components pack, where <see cref="Price"/> is 0 — already paid for.</item>
    /// </list>
    /// </summary>
    public readonly struct ComponentVm
    {
        public readonly CarComponent Component;
        public readonly string Name;
        public readonly string Description;
        /// <summary>Which stat bar it belongs to — decision 5's "families ARE the stats".</summary>
        public readonly BuildStat Family;
        public readonly int Level;
        public readonly int MaxLevel;
        public readonly int Price;
        public readonly bool Affordable;
        /// <summary>False once the component is at its ceiling.</summary>
        public readonly bool CanLevel;

        public ComponentVm(CarComponent component, string name, string description, BuildStat family,
            int level, int maxLevel, int price, bool affordable, bool canLevel)
        {
            Component = component;
            Name = name;
            Description = description;
            Family = family;
            Level = level;
            MaxLevel = maxLevel;
            Price = price;
            Affordable = affordable;
            CanLevel = canLevel;
        }

        public string LevelLabel => $"L{Level}/{MaxLevel}";
    }

    /// <summary>A permanent team upgrade offer (name/description/price + affordability).</summary>
    public readonly struct UpgradeVm
    {
        public readonly TeamUpgrade Upgrade;
        public readonly string Name;
        public readonly string Description;
        public readonly int Price;
        public readonly bool Affordable;

        public UpgradeVm(TeamUpgrade upgrade, string name, string description, int price, bool affordable)
        {
            Upgrade = upgrade;
            Name = name;
            Description = description;
            Price = price;
            Affordable = affordable;
        }
    }
}

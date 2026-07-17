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
        public readonly bool Affordable;
        public readonly bool HasStatPreview;
        public readonly StatDelta Grip;
        public readonly StatDelta Power;

        public OfferVm(PartDef part, string name, PartCategory category, int price, PartEdition edition,
            string editionTag, string description, bool affordable, bool hasStatPreview,
            StatDelta grip, StatDelta power)
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
        }
    }

    /// <summary>An owned part as the equip list needs it. <see cref="CanEquip"/> mirrors the EQUIP
    /// gate (not already equipped AND a free slot); UNEQUIP is always allowed when equipped.</summary>
    public readonly struct OwnedPartVm
    {
        public readonly PartDef Part;
        public readonly string Name;
        public readonly PartCategory Category;
        public readonly PartEdition Edition;
        public readonly string EditionTag;
        public readonly bool Equipped;
        public readonly bool CanEquip;

        public OwnedPartVm(PartDef part, string name, PartCategory category, PartEdition edition,
            string editionTag, bool equipped, bool canEquip)
        {
            Part = part;
            Name = name;
            Category = category;
            Edition = edition;
            EditionTag = editionTag;
            Equipped = equipped;
            CanEquip = canEquip;
        }
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

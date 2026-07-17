using System.Collections.Generic;
using Shitboxer.Meta;
using Shitboxer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Shitboxer.Editor
{
    /// <summary>
    /// Phase 3 content bootstrap. "Build Meta Assets" creates the placeholder part catalogue
    /// (doc 03's stat/economy/attack split) plus the PartPool listing them — idempotent: an
    /// existing PartDef asset is never overwritten so inspector tuning survives a rebuild,
    /// while the pool's list is refreshed every run. "Add Run Mode To Race Scene" drops a
    /// RunRig (RunDirector + RunBootstrap, wired to the pool) into RaceTest.unity so pressing
    /// Play runs the full circuit loop instead of a single race.
    /// </summary>
    public static class MetaAssetsBuilder
    {
        private const string SettingsDir = "Assets/_Project/Settings";
        private const string PartsDir = SettingsDir + "/Parts";
        private const string PoolPath = PartsDir + "/PartPool.asset";
        private const string RaceScenePath = "Assets/_Project/Scenes/RaceTest.unity";

        [MenuItem("Shitboxer/Build Meta Assets")]
        public static void BuildMetaAssets()
        {
            EnsureFolders();

            var parts = new List<PartDef>
            {
                // ---- Stat parts (grip / power / weight / downforce flavours) ----
                EnsurePart("Part_StickyCompound", p =>
                {
                    p.Id = "sticky_compound";
                    p.DisplayName = "Sticky Compound";
                    p.Description = "Gummy rubber all round. +10% grip front and rear.";
                    p.Category = PartCategory.Stat;
                    p.Price = 6;
                    p.SpecMods = Mods((SpecModTarget.GripFront, 1.10f), (SpecModTarget.GripRear, 1.10f));
                }),
                EnsurePart("Part_RaceRears", p =>
                {
                    p.Id = "race_rears";
                    p.DisplayName = "Race Rears";
                    p.Description = "Slicks on the back axle only. +12% rear grip.";
                    p.Category = PartCategory.Stat;
                    p.Price = 4;
                    p.SpecMods = Mods((SpecModTarget.GripRear, 1.12f));
                }),
                EnsurePart("Part_JunkyardTurbo", p =>
                {
                    p.Id = "junkyard_turbo";
                    p.DisplayName = "Junkyard Turbo";
                    p.Description = "Whistles ominously. +15% engine torque.";
                    p.Category = PartCategory.Stat;
                    p.Price = 8;
                    p.SpecMods = Mods((SpecModTarget.Power, 1.15f));
                }),
                EnsurePart("Part_ChippedEcu", p =>
                {
                    p.Id = "chipped_ecu";
                    p.DisplayName = "Chipped ECU";
                    p.Description = "Warranty voided. +6% engine torque, cheap.";
                    p.Category = PartCategory.Stat;
                    p.Price = 3;
                    p.SpecMods = Mods((SpecModTarget.Power, 1.06f));
                }),
                EnsurePart("Part_GuttedInterior", p =>
                {
                    p.Id = "gutted_interior";
                    p.DisplayName = "Gutted Interior";
                    p.Description = "Who needs seats? -8% mass.";
                    p.Category = PartCategory.Stat;
                    p.Price = 5;
                    p.SpecMods = Mods((SpecModTarget.Weight, 0.92f));
                }),
                EnsurePart("Part_ParkBenchWing", p =>
                {
                    p.Id = "park_bench_wing";
                    p.DisplayName = "Park Bench Wing";
                    p.Description = "Enormous, embarrassing, effective. +50% downforce, +2% mass.";
                    p.Category = PartCategory.Stat;
                    p.Price = 5;
                    p.SpecMods = Mods((SpecModTarget.Downforce, 1.50f), (SpecModTarget.Weight, 1.02f));
                }),

                // ---- Stat parts with real trade-offs + slot-order depth (doc 03) ----
                // Additive anchors (Op=Add) want to sit LEFT of the x-payoffs below: a +grip
                // Add resolved before a x1.20 grip Multiply beats the reverse (SpecModApplier).
                EnsurePart("Part_Coilovers", p =>
                {
                    p.Id = "coilovers";
                    p.DisplayName = "Coilovers";
                    p.Description = "Adjustable stiff setup. +14% grip front and rear (additive — slot it LEFT of a x-grip part for more), but +3% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.GripFront, 0.14f),
                        AddPct(SpecModTarget.GripRear, 0.14f),
                        Mul(SpecModTarget.Weight, 1.03f));
                }),
                EnsurePart("Part_StiffSprings", p =>
                {
                    p.Id = "stiff_springs";
                    p.DisplayName = "Stiff Springs";
                    p.Description = "Plants the nose, unsettles the tail. +10% front grip (additive), -4% rear grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 4;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.GripFront, 0.10f),
                        AddPct(SpecModTarget.GripRear, -0.04f));
                }),
                EnsurePart("Part_BigCam", p =>
                {
                    p.Id = "big_cam";
                    p.DisplayName = "Big Cam";
                    p.Description = "Lumpy idle, angry top end. +12% torque (additive — pair it before a x-power part), but the extra shove costs -3% rear grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Power, 0.12f),
                        Mul(SpecModTarget.GripRear, 0.97f));
                }),
                EnsurePart("Part_LightFlywheel", p =>
                {
                    p.Id = "light_flywheel";
                    p.DisplayName = "Lightweight Flywheel";
                    p.Description = "Revs snap up instantly. +9% torque (additive), but the snappier throttle costs -2% front grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Power, 0.09f),
                        Mul(SpecModTarget.GripFront, 0.98f));
                }),
                EnsurePart("Part_RaceSlicks", p =>
                {
                    p.Id = "race_slicks";
                    p.DisplayName = "Race Slicks";
                    p.Description = "Full soft compound. x1.20 grip front and rear — wants every additive grip part to ITS LEFT — but the extra rubber adds +3% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.GripFront, 1.20f),
                        Mul(SpecModTarget.GripRear, 1.20f),
                        Mul(SpecModTarget.Weight, 1.03f));
                }),
                EnsurePart("Part_BigTurbo", p =>
                {
                    p.Id = "big_turbo";
                    p.DisplayName = "Big Turbo";
                    p.Description = "Comically oversized snail. x1.25 torque (stack Big Cam to its left first), but the spikes overwhelm the tail (-6% rear grip) and the plumbing adds +2% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 11;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Power, 1.25f),
                        Mul(SpecModTarget.GripRear, 0.94f),
                        Mul(SpecModTarget.Weight, 1.02f));
                }),
                EnsurePart("Part_SemiSlicks", p =>
                {
                    p.Id = "semi_slicks";
                    p.DisplayName = "Semi-Slicks";
                    p.Description = "Rear-biased treadless tyres. x1.12 rear grip, but -2% front grip. Slots to the RIGHT of your additive grip parts.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 5;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.GripRear, 1.12f),
                        Mul(SpecModTarget.GripFront, 0.98f));
                }),
                EnsurePart("Part_BigWing", p =>
                {
                    p.Id = "big_wing";
                    p.DisplayName = "Big Wing";
                    p.Description = "Proper motorsport aero. x1.60 downforce for high-speed grip, but the drag and hardware cost +4% mass (and top speed).";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Downforce, 1.60f),
                        Mul(SpecModTarget.Weight, 1.04f));
                }),
                EnsurePart("Part_CarbonTub", p =>
                {
                    p.Id = "carbon_tub";
                    p.DisplayName = "Carbon Tub";
                    p.Description = "Featherweight monocoque. -15% mass, but the stripped-back body loses -5% downforce. Pricey.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Weight, 0.85f),
                        Mul(SpecModTarget.Downforce, 0.95f));
                }),
                EnsurePart("Part_StrippedPanels", p =>
                {
                    p.Id = "stripped_panels";
                    p.DisplayName = "Stripped Panels";
                    p.Description = "Binned the bumpers and glass. -5% mass, but -4% downforce with less bodywork to bite the air.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 4;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Weight, 0.95f),
                        Mul(SpecModTarget.Downforce, 0.96f));
                }),

                // ---- More stat parts: additive anchors for under-served targets (doc 03) ----
                // Rear-only grip, downforce, and power-to-weight anchors. The Op=Add entries want
                // to sit LEFT of the x-payoffs above (Race Slicks / Big Wing / Big Turbo) so equip
                // order pays off; each carries a real trade-off, never a free +stat.
                EnsurePart("Part_WeldedDiff", p =>
                {
                    p.Id = "welded_diff";
                    p.DisplayName = "Welded Diff";
                    p.Description = "Both rear wheels locked together. +12% rear grip on power (additive — slot it LEFT of Semi-Slicks or Race Slicks), but it scrubs the nose wide: -3% front grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 4;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.GripRear, 0.12f),
                        Mul(SpecModTarget.GripFront, 0.97f));
                }),
                EnsurePart("Part_PolyBushings", p =>
                {
                    p.Id = "poly_bushings";
                    p.DisplayName = "Poly Bushings";
                    p.Description = "Firm urethane mounts sharpen every input. +6% grip front and rear (additive) — cheap glue that makes your later x-grip parts hit harder.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 3;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.GripFront, 0.06f),
                        AddPct(SpecModTarget.GripRear, 0.06f));
                }),
                EnsurePart("Part_ColdAirIntake", p =>
                {
                    p.Id = "cold_air_intake";
                    p.DisplayName = "Cold Air Intake";
                    p.Description = "Feeds the engine denser air. +7% torque (additive — pair it before a x-power part), but the bonnet scoop spoils the airflow for -3% downforce.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 4;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Power, 0.07f),
                        Mul(SpecModTarget.Downforce, 0.97f));
                }),
                EnsurePart("Part_FrontSplitter", p =>
                {
                    p.Id = "front_splitter";
                    p.DisplayName = "Front Splitter";
                    p.Description = "Bolts under the nose for bite. +20% downforce (additive — slot it LEFT of Big Wing to stack), but the extra lip adds +2% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Downforce, 0.20f),
                        Mul(SpecModTarget.Weight, 1.02f));
                }),
                EnsurePart("Part_MagnesiumWheels", p =>
                {
                    p.Id = "magnesium_wheels";
                    p.DisplayName = "Magnesium Wheels";
                    p.Description = "Featherweight unsprung mass. -6% mass and +5% grip front and rear (additive), but the thin rims flex under load: -3% downforce.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Weight, 0.94f),
                        AddPct(SpecModTarget.GripFront, 0.05f),
                        AddPct(SpecModTarget.GripRear, 0.05f),
                        Mul(SpecModTarget.Downforce, 0.97f));
                }),
                EnsurePart("Part_GroundEffectFloor", p =>
                {
                    p.Id = "ground_effect_floor";
                    p.DisplayName = "Ground Effect Floor";
                    p.Description = "Venturi tunnels suck the car down. +30% downforce (additive — the biggest anchor to slot LEFT of Big Wing), but the heavy floor pan adds +3% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 11;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Downforce, 0.30f),
                        Mul(SpecModTarget.Weight, 1.03f));
                }),
                EnsurePart("Part_TitaniumInternals", p =>
                {
                    p.Id = "titanium_internals";
                    p.DisplayName = "Titanium Internals";
                    p.Description = "Exotic rods and valves chase pure power-to-weight. -10% mass and +8% torque (additive — anchor it before Big Turbo), but the stripped-out engine bay loses -3% downforce. Expensive.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 13;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Power, 0.08f),
                        Mul(SpecModTarget.Weight, 0.90f),
                        Mul(SpecModTarget.Downforce, 0.97f));
                }),

                // ---- Economy parts (payout hook only, this phase) ----
                EnsurePart("Part_SponsorLivery", p =>
                {
                    p.Id = "sponsor_livery";
                    p.DisplayName = "Sponsor Livery";
                    p.Description = "Backmarker TV time pays. +$1 per finishing position each race, capped at mid-pack (no bonus for tanking to the very back).";
                    p.Category = PartCategory.Economy;
                    p.Price = 5;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_TeamAccountant", p =>
                {
                    p.Id = "team_accountant";
                    p.DisplayName = "Team Accountant";
                    p.Description = "Squeezes the sponsors properly. +$2 per finishing position each race, capped at mid-pack.";
                    p.Category = PartCategory.Economy;
                    p.Price = 8;
                    p.MoneyPerPositionHeld = 2;
                }),
                EnsurePart("Part_ScrapDealer", p =>
                {
                    p.Id = "scrap_dealer";
                    p.DisplayName = "Scrap Dealer";
                    p.Description = "Knows a guy. +$1 per finishing position, capped at mid-pack (sell-for-cash hook comes later).";
                    p.Category = PartCategory.Economy;
                    p.Price = 4;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_UnderdogBonus", p =>
                {
                    p.Id = "underdog_bonus";
                    p.DisplayName = "Underdog Bonus";
                    p.Description = "The crowd loves a backmarker. +$3 per finishing position each race, capped at mid-pack — pays big for scrapping near the back, nothing for winning clean.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 8;
                    p.MoneyPerPositionHeld = 3;
                }),

                // ---- More economy parts (same mid-pack-capped payout hook, new price points) ----
                EnsurePart("Part_PizzaSponsor", p =>
                {
                    p.Id = "pizza_sponsor";
                    p.DisplayName = "Pizza Sponsor";
                    p.Description = "A local takeaway slaps a sticker on the door. +$1 per finishing position each race, capped at mid-pack — the cheapest way into the payout game.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Common;
                    p.Price = 3;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_MerchStand", p =>
                {
                    p.Id = "merch_stand";
                    p.DisplayName = "Merch Stand";
                    p.Description = "Fans buy caps when you put on a show. +$2 per finishing position each race, capped at mid-pack.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.MoneyPerPositionHeld = 2;
                }),
                EnsurePart("Part_BroadcastDeal", p =>
                {
                    p.Id = "tv_broadcast_deal";
                    p.DisplayName = "Prime-Time Broadcast Deal";
                    p.Description = "The network pays per second you're on screen scrapping in the pack. +$4 per finishing position each race, capped at mid-pack — the fattest payout, and it rewards mixing it up mid-field over a clean win.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.MoneyPerPositionHeld = 4;
                }),

                // ---- Attack parts (on-contact saps + proximity aura, doc 03) ----
                EnsurePart("Part_RamBars", p =>
                {
                    p.Id = "ram_bars";
                    p.DisplayName = "Ram Bars";
                    p.Description = "Weld-on battering ram. Shunt a rival hard and their tyres go greasy — -28% grip for a moment.";
                    p.Category = PartCategory.Attack;
                    p.Price = 6;
                    p.ContactGripSap = 0.28f;
                }),
                EnsurePart("Part_SpikePlates", p =>
                {
                    p.Id = "spike_plates";
                    p.DisplayName = "Spike Plates";
                    p.Description = "Contact costs THEM. A solid hit bleeds a rival's engine — -30% power and -10% grip.";
                    p.Category = PartCategory.Attack;
                    p.Price = 7;
                    p.ContactPowerSap = 0.30f;
                    p.ContactGripSap = 0.10f;
                }),
                EnsurePart("Part_DisruptorField", p =>
                {
                    p.Id = "disruptor_field";
                    p.DisplayName = "Disruptor Field";
                    p.Description = "Rivals tucked in behind you can't find grip. 6 m aura, -18% grip to cars on your gearbox.";
                    p.Category = PartCategory.Attack;
                    p.Price = 8;
                    p.AuraRadiusM = 6f;
                    p.AuraGripSap = 0.18f;
                }),
                EnsurePart("Part_EmpBumper", p =>
                {
                    p.Id = "emp_bumper";
                    p.DisplayName = "EMP Bumper";
                    p.Description = "Discharges on impact. A clean hit kills a rival's ignition — -45% power — but the capacitor is delicate and the whole rig is pricey.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Rare;
                    p.Price = 10;
                    p.ContactPowerSap = 0.45f;
                }),

                // ---- More attack parts: contact saps + auras at new radius / strength / price bands ----
                EnsurePart("Part_KerbFeelers", p =>
                {
                    p.Id = "kerb_feelers";
                    p.DisplayName = "Kerb Feelers";
                    p.Description = "Sharpened side rails that bite on a swipe. A glancing hit shaves a rival's grip — -20% for a moment. Cheap and light.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Common;
                    p.Price = 5;
                    p.ContactGripSap = 0.20f;
                }),
                EnsurePart("Part_HeavyBullbar", p =>
                {
                    p.Id = "heavy_bullbar";
                    p.DisplayName = "Heavy Bullbar";
                    p.Description = "A brute steel hoop that unsettles anything you lean on. A solid hit costs a rival -15% grip AND -20% power.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.ContactGripSap = 0.15f;
                    p.ContactPowerSap = 0.20f;
                }),
                EnsurePart("Part_OilDripper", p =>
                {
                    p.Id = "oil_dripper";
                    p.DisplayName = "Oil Dripper";
                    p.Description = "Leaves a greasy trail for whoever's close. A tight 4 m aura saps -12% grip from rivals right on your bumper — no contact needed.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.AuraRadiusM = 4f;
                    p.AuraGripSap = 0.12f;
                }),
                EnsurePart("Part_ShockwaveEmitter", p =>
                {
                    p.Id = "shockwave_emitter";
                    p.DisplayName = "Shockwave Emitter";
                    p.Description = "A wide pressure field that rattles everyone near you. A big 8 m aura strips -22% grip from surrounding rivals — the strongest, widest aura in the shop, and priced like it.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.AuraRadiusM = 8f;
                    p.AuraGripSap = 0.22f;
                }),

                // ---- Conditioned parts (doc 03 part modifiers) ----------------------------------
                // FRAGILE: outsized effect, but destroyed if the car finishes a race badly battered
                // (RunDirector breaks one equipped Fragile part on heavy damage). CASHOUT: refunds its
                // full Price into final money if still owned when the run ends (RunState.CashoutRefundTotal).
                // Condition is set ONLY on these newly-created parts; existing assets stay Passive.
                EnsurePart("Part_GrenadeTurbo", p =>
                {
                    p.Id = "grenade_turbo";
                    p.DisplayName = "Grenade Turbo";
                    p.Description = "Insane boost from a hand-grenade-spec snail. x1.35 torque, but the tail goes light (-6% rear grip) — and one hard race and it lets go. FRAGILE: destroyed if you finish a race badly battered.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 11;
                    p.Condition = PartCondition.Fragile;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Power, 1.35f),
                        Mul(SpecModTarget.GripRear, 0.94f));
                }),
                EnsurePart("Part_CheaterSlicks", p =>
                {
                    p.Id = "cheater_slicks";
                    p.DisplayName = "Cheater Slicks";
                    p.Description = "Qualifying-only rubber that grips like glue. x1.30 grip front and rear — but the soft carcass shreds the moment you get bashed about. FRAGILE: destroyed if you finish a race badly battered.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.Condition = PartCondition.Fragile;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.GripFront, 1.30f),
                        Mul(SpecModTarget.GripRear, 1.30f));
                }),
                EnsurePart("Part_GlassCannonRam", p =>
                {
                    p.Id = "glass_cannon_ram";
                    p.DisplayName = "Glass-Cannon Ram";
                    p.Description = "A vicious but brittle nose spike. Contact craters a rival — -40% grip AND -40% power — but the spike snaps clean off if YOU take a real beating. FRAGILE: destroyed if you finish a race badly battered.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Rare;
                    p.Price = 10;
                    p.Condition = PartCondition.Fragile;
                    p.ContactGripSap = 0.40f;
                    p.ContactPowerSap = 0.40f;
                }),
                EnsurePart("Part_GoldBarBallast", p =>
                {
                    p.Id = "gold_bar_ballast";
                    p.DisplayName = "Gold Bar Ballast";
                    p.Description = "A literal gold bar bolted in as ballast. +$1 per finishing position each race — and CASHOUT: refunds its full price into your winnings if you still own it when the run ends.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 8;
                    p.Condition = PartCondition.Cashout;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_VintagePlate", p =>
                {
                    p.Id = "vintage_plate";
                    p.DisplayName = "Vintage Numberplate";
                    p.Description = "A collector's plate that only appreciates. No on-track effect — but CASHOUT: refunds its full price into your final money if you hold it to the end of the run.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Rare;
                    p.Price = 10;
                    p.Condition = PartCondition.Cashout;
                }),

                // ---- Editioned variants (Balatro-foil premium opt-ins, doc 03 editions) ---------
                // Each carries the SAME SpecMods as a representative base part but is stamped with a
                // higher Edition, so SpecModApplier amplifies the effect MAGNITUDE (its deviation
                // from identity — upsides AND downsides, never the sign; PartEditionInfo.StatMult).
                // Priced UP via PartEditionInfo.PriceMult and kept rarer than their base part, so
                // they are a deliberate premium, not a free power spike. These are NEW assets (own
                // Ids/files); the plain parts they derive from are untouched and stay in the pool.
                EnsurePart("Part_StickyCompoundFoil", p =>
                {
                    p.Id = "sticky_compound_foil";
                    p.DisplayName = "Sticky Compound (Foil)";
                    p.Description = "A pristine foil-edition set. Same gummy compound, sharper bite: Foil amplifies its grip effect to ~+12.5% front and rear — a pricier, rarer take on the plain Sticky Compound.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Edition = PartEdition.Foil;
                    p.Price = Mathf.RoundToInt(6 * PartEditionInfo.PriceMult(PartEdition.Foil));   // 6 -> 9
                    p.SpecMods = Mods((SpecModTarget.GripFront, 1.10f), (SpecModTarget.GripRear, 1.10f));
                }),
                EnsurePart("Part_JunkyardTurboHolo", p =>
                {
                    p.Id = "junkyard_turbo_holo";
                    p.DisplayName = "Junkyard Turbo (Holo)";
                    p.Description = "A holographic collector's snail. Same whistle, bigger shove: Holo amplifies its torque effect to ~+22.5% — a rare, premium-priced take on the plain Junkyard Turbo.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Edition = PartEdition.Holo;
                    p.Price = Mathf.RoundToInt(8 * PartEditionInfo.PriceMult(PartEdition.Holo));    // 8 -> 16
                    p.SpecMods = Mods((SpecModTarget.Power, 1.15f));
                }),
                EnsurePart("Part_RaceSlicksFoil", p =>
                {
                    p.Id = "race_slicks_foil";
                    p.DisplayName = "Race Slicks (Foil)";
                    p.Description = "Foil-edition full softs. Foil deepens BOTH sides of the base Race Slicks: ~+25% grip front and rear, but the amplified rubber also adds ~+3.75% mass — a rare, top-dollar premium, never a free upgrade.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Edition = PartEdition.Foil;
                    p.Price = Mathf.RoundToInt(12 * PartEditionInfo.PriceMult(PartEdition.Foil));   // 12 -> 18
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.GripFront, 1.20f),
                        Mul(SpecModTarget.GripRear, 1.20f),
                        Mul(SpecModTarget.Weight, 1.03f));
                }),

                // ---- Draft-Leech economy part (doc 03; payoff resolved by the draft mechanism) --
                // Its value is ENTIRELY the draft payoff (DraftLeech = true, consumed downstream), so
                // it carries no finishing-position payout (MoneyPerPositionHeld stays 0) and no stat
                // effect — a modest-priced opt-in that changes nothing for a run that never buys it.
                EnsurePart("Part_SlipstreamSiphon", p =>
                {
                    p.Id = "slipstream_siphon";
                    p.DisplayName = "Slipstream Siphon";
                    p.Description = "Taps the low-pressure pocket behind the car ahead and turns clean air into cash. No on-track effect and no finishing-position payout — its whole value is the draft payoff, earned while you sit in a rival's slipstream.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.DraftLeech = true;
                }),
            };

            // The pool is refreshed every run so new parts always show up.
            var pool = AssetDatabase.LoadAssetAtPath<PartPool>(PoolPath);
            if (pool == null)
            {
                pool = ScriptableObject.CreateInstance<PartPool>();
                AssetDatabase.CreateAsset(pool, PoolPath);
            }
            pool.Parts = parts;
            EditorUtility.SetDirty(pool);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Shitboxer] Meta assets ready — {parts.Count} parts in {PartsDir}, pool at {PoolPath}.");
        }

        [MenuItem("Shitboxer/Add Run Mode To Race Scene")]
        public static void AddRunModeToRaceScene() => AddRunModeToRaceScene(RaceScenePath);

        /// <summary>
        /// Wires a RunRig (RunDirector + RunBootstrap, PartPool-configured) into one race scene.
        /// Every layout gets one, so any of them is play-ready on its own; RunDirector is a
        /// DontDestroyOnLoad singleton, so the duplicate that loads when the run rotates to the next
        /// track destroys itself in Awake and the original stays bound.
        /// </summary>
        public static void AddRunModeToRaceScene(string scenePath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[Shitboxer] {scenePath} not found — run 'Shitboxer/Build Race Scenes' first.");
                return;
            }

            BuildMetaAssets(); // idempotent; guarantees the PartPool exists
            var pool = AssetDatabase.LoadAssetAtPath<PartPool>(PoolPath);

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject rig = GameObject.Find("RunRig");
            if (rig == null) rig = new GameObject("RunRig");

            var director = rig.GetComponent<RunDirector>();
            if (!director) director = rig.AddComponent<RunDirector>();
            if (!rig.GetComponent<RunBootstrap>()) rig.AddComponent<RunBootstrap>();
            director.Configure(pool);

            // UI Toolkit garage overlay on the RunRig (persists via DontDestroyOnLoad, so it survives the
            // race-scene rotation). Shows during the Garage phase; replaces the IMGUI garage draw.
            AttachGarageUi(rig);
            AttachRaceHud();   // in-race HUD overlay on the RaceRig (UI Toolkit RaceHudView)

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RepairUiPanelRefs(scenePath); // UIDocument panel refs (garage + HUD) don't survive SaveScene — patch them.
            Debug.Log($"[Shitboxer] Run mode added to {System.IO.Path.GetFileName(scenePath)}.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Adds the UI Toolkit garage overlay (UIDocument + GarageUiHost) to the RunRig and wires its
        /// PanelSettings + stylesheets. The panel is assigned via SerializedObject; SaveScene still drops
        /// it, so <see cref="RepairGaragePanelRef"/> patches the saved file (the quirk UiGalleryBuilder hits).
        /// </summary>
        private static void AttachGarageUi(GameObject rig)
        {
            const string uiDir = "Assets/_Project/Scripts/UI";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(uiDir + "/ShitboxerPanel.asset");
            if (panel == null)
            {
                Debug.LogWarning("[Shitboxer] ShitboxerPanel.asset missing — run 'Shitboxer/Build UI Gallery' "
                    + "once to create it. Garage UI not wired into this scene.");
                return;
            }

            var doc = rig.GetComponent<UIDocument>();
            if (!doc) doc = rig.AddComponent<UIDocument>();
            var docSo = new SerializedObject(doc);
            docSo.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            docSo.ApplyModifiedPropertiesWithoutUndo();

            var host = rig.GetComponent<GarageUiHost>();
            if (!host) host = rig.AddComponent<GarageUiHost>();
            var sheets = new[]
            {
                AssetDatabase.LoadAssetAtPath<StyleSheet>(uiDir + "/USS/Tokens.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(uiDir + "/USS/Shitboxer.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(uiDir + "/USS/Garage.uss"),
            };
            var hostSo = new SerializedObject(host);
            SerializedProperty arr = hostSo.FindProperty("styleSheets");
            arr.arraySize = sheets.Length;
            for (int i = 0; i < sheets.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = sheets[i];
            hostSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Adds the in-race HUD overlay (UIDocument + RaceHudHost) to the RaceRig and wires its panel +
        /// stylesheets. Like the garage, the panel ref is repaired in the saved scene afterwards.
        /// </summary>
        private static void AttachRaceHud()
        {
            var raceRig = GameObject.Find("RaceRig");
            if (raceRig == null) return; // a bare run-mode scene with no race — nothing to overlay

            const string uiDir = "Assets/_Project/Scripts/UI";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(uiDir + "/ShitboxerPanel.asset");
            if (panel == null) return; // AttachGarageUi already warned about the missing panel

            var doc = raceRig.GetComponent<UIDocument>();
            if (!doc) doc = raceRig.AddComponent<UIDocument>();
            var docSo = new SerializedObject(doc);
            docSo.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            docSo.ApplyModifiedPropertiesWithoutUndo();

            var host = raceRig.GetComponent<RaceHudHost>();
            if (!host) host = raceRig.AddComponent<RaceHudHost>();
            var sheets = new[]
            {
                AssetDatabase.LoadAssetAtPath<StyleSheet>(uiDir + "/USS/Tokens.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(uiDir + "/USS/Garage.uss"),   // the .stat-bar atom
                AssetDatabase.LoadAssetAtPath<StyleSheet>(uiDir + "/USS/RaceHud.uss"),
            };
            var hostSo = new SerializedObject(host);
            SerializedProperty arr = hostSo.FindProperty("styleSheets");
            arr.arraySize = sheets.Length;
            for (int i = 0; i < sheets.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = sheets[i];
            hostSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Patches every null UIDocument PanelSettings ref (garage + HUD) in the saved scene.</summary>
        private static void RepairUiPanelRefs(string scenePath)
        {
            string guid = AssetDatabase.AssetPathToGUID("Assets/_Project/Scripts/UI/ShitboxerPanel.asset");
            if (string.IsNullOrEmpty(guid)) return;

            string text = System.IO.File.ReadAllText(scenePath);
            const string nullRef = "m_PanelSettings: {fileID: 0}";
            if (!text.Contains(nullRef)) return;

            text = text.Replace(nullRef, $"m_PanelSettings: {{fileID: 11400000, guid: {guid}, type: 2}}");
            System.IO.File.WriteAllText(scenePath, text);
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
        }

        private static void EnsureFolders()
        {
            foreach (string dir in new[] { "Assets/_Project", SettingsDir, PartsDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = dir.Substring(0, dir.LastIndexOf('/'));
                    AssetDatabase.CreateFolder(parent, dir.Substring(dir.LastIndexOf('/') + 1));
                }
            }
        }

        /// <summary>Creates a PartDef asset only if missing, so hand-tuning survives a rebuild.</summary>
        private static PartDef EnsurePart(string fileName, System.Action<PartDef> configure)
        {
            string path = $"{PartsDir}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PartDef>(path);
            if (existing != null) return existing;

            var part = ScriptableObject.CreateInstance<PartDef>();
            configure(part);
            AssetDatabase.CreateAsset(part, path);
            return part;
        }

        private static List<SpecMod> Mods(params (SpecModTarget target, float multiplier)[] entries)
        {
            var mods = new List<SpecMod>(entries.Length);
            foreach ((SpecModTarget target, float multiplier) in entries)
                mods.Add(new SpecMod { Target = target, Multiplier = multiplier });
            return mods;
        }

        // For parts that mix additive anchors with multiplicative payoffs on one target, spell
        // the ops out per-entry so slot order reads clearly (SpecModApplier resolves left-to-right).
        private static List<SpecMod> ModList(params SpecMod[] mods) => new List<SpecMod>(mods);

        /// <summary>One multiplicative mod: 1.10 = x1.10, 0.94 = x0.94.</summary>
        private static SpecMod Mul(SpecModTarget target, float multiplier) =>
            new SpecMod { Target = target, Multiplier = multiplier, Op = SpecModOp.Multiply };

        /// <summary>One additive mod (Op=Add): a +fraction folded before later x-mods, e.g. 0.14 = +14%, -0.04 = -4%.</summary>
        private static SpecMod AddPct(SpecModTarget target, float amount) =>
            new SpecMod { Target = target, Multiplier = amount, Op = SpecModOp.Add };
    }
}

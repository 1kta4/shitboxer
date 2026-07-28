using System;
using Shitboxer.Race;
using Shitboxer.Vehicle;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Everything a player-facing UI needs from the run loop, and the only surface it should touch.
    ///
    /// Lives in Meta — NOT in the UI assembly — so <see cref="RunDirector"/> can implement it without
    /// Meta ever naming a UI type. That is what lets the garage / HUD move to Shitboxer.UI with no
    /// back-reference, and what lets an EditMode fixture drive a garage with no scene at all (a fake
    /// host that mutates the same plain RunState/ShopLogic the director does — see wave 26).
    ///
    /// Almost every member already existed as a public member of RunDirector; the interface is mostly
    /// a declaration of the seam that was already there.
    /// </summary>
    public interface IRunHost
    {
        RunState Run { get; }
        ShopLogic Shop { get; }
        MetaProgress Meta { get; }
        RunPhase Phase { get; }
        string LastRaceSummary { get; }

        /// <summary>The player's part-free authored spec — the garage previews equipped sets against it.</summary>
        VehicleSpec BaseSpec { get; }

        int RepairCost { get; }
        int CratePrice { get; }
        int CrateDrawCount { get; }

        /// <summary>Cash a clean finish at this position banks right now. Replaces RaceHud's pushed closure.</summary>
        int PayoutPreviewFor(int position);

        /// <summary>The live race, or null between scenes / while paused in the garage.</summary>
        RaceManager CurrentRace { get; }

        /// <summary>The live player car, or null between scenes.</summary>
        VehicleController PlayerCar { get; }

        /// <summary>
        /// Live sector-part scoring for the current race (doc 08) — what the last sector paid and how it
        /// was driven. The HUD reads it to show in-race earnings; null on a host that doesn't run races.
        /// </summary>
        SectorPartRunner SectorParts { get; }

        /// <summary>
        /// The equipped active item's live charge meter (doc 08 decision 14), flattened for the HUD.
        /// <see cref="ActiveReadout.HasActive"/> false (the default struct) on a loadout without one —
        /// the HUD hides the element entirely.
        /// </summary>
        ActiveReadout ActiveItem { get; }

        /// <summary>Raised on every run-phase transition; a retained-mode UI subscribes instead of polling.</summary>
        event Action<RunPhase> PhaseChanged;

        bool BuyOffer(PartDef part);
        /// <summary>Sells a fitted part back for half its price, freeing its slot.</summary>
        bool SellPart(PartDef part);
        bool RerollShop();
        bool BuyCrate();
        bool TakeFromCrate(PartDef part);
        /// <summary>Buys one of this visit's two booster packs by shelf index.</summary>
        bool BuyPack(int packIndex);
        /// <summary>Takes one component from an open components pack, raising it a level.</summary>
        bool TakeComponent(CarComponent component);
        /// <summary>Takes one offer from an open Spectral pack, stamping that edition onto the
        /// targeted fitted part for the rest of the run (doc 08 slice 13).</summary>
        bool TakeSpectral(PartDef part, PartEdition edition);
        /// <summary>
        /// Buys one of the Blueprints ON THIS VISIT'S SHELF (<see cref="ShopLogic.Blueprints"/>),
        /// raising that component a level. False for anything not stocked.
        /// </summary>
        bool BuyBlueprint(CarComponent component);
        bool BuyUpgrade(TeamUpgrade upgrade);
        bool RepairCar();
        void StartNextRace();
        void StartNewRun();
    }
}

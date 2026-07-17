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

        /// <summary>Raised on every run-phase transition; a retained-mode UI subscribes instead of polling.</summary>
        event Action<RunPhase> PhaseChanged;

        bool BuyOffer(PartDef part);
        bool RerollShop();
        bool BuyCrate();
        bool TakeFromCrate(PartDef part);
        bool BuyUpgrade(TeamUpgrade upgrade);
        bool RepairCar();
        void StartNextRace();
        void StartNewRun();
    }
}

# 09 — Playtest script: slices 7–15

**Written 2026-07-28.** Slices 7–15 are built, editor-verified (723/723 batchmode), merged to
`main` — and unplayed. This is the script for the sessions that change that. The output is the
findings log at the bottom; the next work block (tuning pass, then the Phase 4 juice pass) is
planned off that log, not off vibes.

Doc 08 is the source of truth for what each slice *is*; this doc only says how to test it.

---

## Pre-flight (5 min)

- Pull `main` on the Windows side. Open the project, let it compile, check the Console is clean.
- Run **Shitboxer → Build Meta Assets** once so the part/active/edition assets are materialised
  (`PartPool` should report 69 parts).
- The dev harness lives in the **ESC menu, editor-only row** (`#if UNITY_EDITOR`):
  **EQUIP SECTOR PARTS · + $50 · FINISH RACE NOW · NEXT CIRCUIT >> · TIME ×4**, plus the live
  `sector $ / grip × / power ×` readout. Session A leans on all of it; Session B mostly bans it.
- **DEV: UNLOCK ALL** in the main menu (editor + development builds only) grants every chassis and
  stake for Session A — no JSON hand-edit needed. Session B still wants a virgin profile: delete the
  saves and don't press it.
- Keep this file open. Log findings the moment they happen — a note per incident, into the table
  at the bottom.

---

## Session A — systems check (~45–60 min, harness-assisted)

Scripted probes, one per slice. The goal is *does it work and does it read*, not balance.

### A1. Damage + retirement (slice 7)
Start a run on GripBox. Drive circuit 1 deliberately dirty — hit walls, trade paint.
- Does the slide from 100% → crippled **telegraph**? Can you feel 50% before the HUD tells you?
- Get wrecked on purpose (durability → 0). Does `RETIRED — CAR DESTROYED` + the −1 life +
  retry read as **fair consequence or cheap gotcha**?
- On the retry, the car starts at the 0.25 floor. Does it read as "limping wreck the crew
  barely patched" — or just as *confusingly slow*?
- Equip a Fragile part, take damage past half. Does the break at the 0.5 line make sense in
  the moment?

### A2. Actives (slice 9)
Use +$50 and the shop to cycle through the 8 active parts (Tow Cell, Zen Battery, Haymaker
Cell, Payback Coil, Checkpoint Charge, One Big Push, Slow Reactor, Push To Pass).
- For each: can you tell **how it charges without reading the tooltip**? (That's the whole
  decision-14 bet — the charge condition should reshape your driving.)
- Does the meter's cobalt → green → amber state change catch your eye at speed?
- Push To Pass: does the $2-per-press billing register, or is money too abstract mid-race?
- **The headline behaviour test: does equipping a different active change how you drive the
  next lap?** If two or more feel interchangeable, name them in the log.

### A3. Boss rotation (slice 12)
NEXT CIRCUIT >> through circuits 1–4, FINISH RACE NOW through the heats, play each boss.
- **THE ENFORCER** — does amplified damage change how you race traffic?
- **DIRTY AIR** — with a draft-charged active equipped, is "your tools are dead today" a
  puzzle or just a mute button?
- **THE TAXMAN** — does the per-deploy tax + DoublePayout create an actual decision?
- **THE LONG HAUL** — 7 laps on a wear model with a real zero: does it feel like an
  endurance exam or just long?
- Is the HUD announcement ("BOSS 3/3 — …") enough warning, or do bosses need a pre-race card?

### A4. Editions (slice 13)
Buy a Spectral pack when it rolls (weight 15 — reroll if needed, that's data too: note how
many rerolls it took).
- Do the pre-aimed offers ("[HOLO ×1.5] → JUNKYARD TURBO") read as a one-tap decision?
- Does the amplified stat move the garage bars the way the label promised?

### A5. The new cars (slices 11, 15)
Three laps each in Brute, Kart, Open Wheeler on any track. Then one Kart race in traffic.
- **Brute** — does shrugging off hits (exponent 0.4, resistance 0.25) come through?
- **Kart** — the design bet: fragility carried entirely by 750 kg of displacement. When the
  field hits you, do you *feel* punted (correct) or damaged (wrong)?
- **Open Wheeler** — ⚠ **the flagged risk:** run it clean against circuit-1 bots, then
  NEXT CIRCUIT to a late circuit and run it against ×1.6+ bots. If its clean pace beats the
  field with no build behind it, the back half of the season is trivialised — log lap deltas.
- Do the two unlock chains make sense as *goals* when read on the car-select cards?

### A6. The eight venues (slice 14)
NEXT CIRCUIT >> through all 8, one flying lap each.
- Any corner where the racing line and the track disagree? Any spot that strands you off-line
  (the old checkpoint-ring failure mode — sectors must never recreate it)?
- Does the season *arc* — does circuit 7 feel like a later track than circuit 2, even in greybox?

---

## Session B — the honest run (~75–90 min, harness banned)

One full 24-race season, GripBox, no dev tools (TIME ×4 allowed only if a race is decided and
dull — and note every use: each press is run-length evidence for doc 07 q3). This is the only
probe that can answer the economy and rhythm questions.

- **Sector scoring (slices 1–6, first play at full season length):** do you look at the
  timing strip unprompted? Can you name the style you just drove before the flash tells you?
  Do 9 ticks/race × 24 races stay a rhythm or become noise?
- **The build:** did you specialise 3–4 components deep (the design intent), or did Blueprint
  RNG/pricing push you wide or flat? Note every shop visit where you wanted a Blueprint that
  never appeared.
- **Decision 13's promise:** survive-and-farm first half, cash-it-in second half. Did the
  crossover happen, and did it feel *earned*? Did you ever out-pace the field before circuit 6?
- **The cutoff:** was last-but-safe ever tense (design intent) vs free (sandbagging risk 4)?
- **Run length:** at the end — would you start another season *right now*? If not, is the
  blocker length, difficulty shape, or repetition? (Feeds doc 07 q3: sprint mode or not.)
- **HUD (open q4):** what did you look for mid-race and not find? What never got looked at?

---

## The behaviour tests (doc 08's own pass/fail bar)

| # | Test | PASS/FAIL | Notes |
|---|---|---|---|
| 1 | Equipping a sector part visibly changes how you drive a sector | | |
| 2 | You check sector colours mid-race, unprompted | | |
| 3 | You can name the style you just drove without the UI | | |
| 4 | Nine ticks a race reads as rhythm, not noise | | |
| 5 | Retirement feels fair (consequence, not gotcha) | | |
| 6 | Each active's charge identity is legible and changes your driving | | |
| 7 | The season's back half holds (ramp lands by ramp, not by wall or fizzle) | | |
| 8 | A run specialises 3–4 components (economy pressure works) | | |
| 9 | Open Wheeler does NOT trivialise the late season | | |
| 10 | Every venue keeps you on the racing line naturally | | |

---

## Tuning-lever cheat sheet (complaint → lever)

> ⚠ **Two standing traps before touching anything.**
> **(1) Scenes override C# defaults.** The bot ramp and cutoff are `[SerializeField]` on
> `RunDirector`, serialized into **all eight** race scenes — editing the C# default changes
> nothing for existing scenes. Change the builder's value and re-run
> `AddRunModeToRaceScene` per scene (slice 15 verified the builder reproduces the committed
> YAML), or edit all eight scenes.
> **(2) Assets outlive the builder.** `MetaAssetsBuilder.EnsurePart` never overwrites an
> existing asset — balance numbers live in the `.asset` YAML *and* the builder. Edit both or
> the next fresh build silently regresses.

| Complaint | Lever | Where |
|---|---|---|
| Bots too strong/weak late | `botStrengthPerRace` 0.013, `botStrengthMax` 1.7 | `RunDirector.cs:445-451` + 8 scenes (trap 1) |
| Bots wrong from race 1 | `botStrengthBase` 1.4 | same |
| Open Wheeler trivialises season | `PeakTorqueNm` or µ — **never** `WearExponent` (glass is the identity, pace is the dial) | `Settings/Vehicles/OpenWheeler.asset` (trap 2) |
| Retirement too punishing | race-start floor `MinRaceStartDurability` 0.25 | `RunDirector.cs:494` |
| Crippled-at-half not felt / too brutal | per-chassis `WearExponent` (decision-15 table) | `Settings/Vehicles/*.asset` (trap 2) |
| Hits feel weightless / lethal on one car | `DamageResistance` (capped 0.9) | chassis asset (trap 2) |
| Cutoff brutal/loose late | `CutoffTightenPerCircuit` 0.02, `MinCutoffFraction` 0.08 | `RunDirector.cs:648-649` |
| Cutoff wrong everywhere | base `cutoffFraction` | `RaceManager` — scene-serialized (trap 1) |
| Season drags | `racesPerCircuit` / `totalCircuits` — but this is doc 07 q3 (sprint mode), a design call, not a dial | `RunState` + scenes (trap 1) |
| Nobody specialises components | `CarComponentCatalog.BlueprintPrice` (2 + level/4), `ShopLogic.BlueprintOfferCount` (2), pack weights 50/40/15 | `CarComponent.cs`, `ShopLogic.cs` |
| Active dull / spammy / never ready | its `ActiveSpec` (fill rate, min charge, drain, `UseCost`); global ×1.5 boost ceiling in `DraftBoostModel` | `Settings/Parts/Part_*.asset` (trap 2) |
| Sector parts pay too much/little | `SectorRules` magnitudes (sized vs 9 sectors/race, $5–13 position payout) | part assets (trap 2) |
| A boss is unfun (not just hard) | its `RaceModifier` wiring / rotation slot | `RaceRuleset.cs` (`BossForCircuit`) |
| Mid-season difficulty plateau | `ApplyDifficulty` saturation (commitment band caps ~circuit 3 — known, watch whether it *reads* flat) | `RunDirector.cs:653-680` |

**Do-not-touch list (locked, with history):** the lap distance gate (no checkpoint rings — not
even as sector boundaries), the airborne-gravity gate in `StepAssists` (the fall-through fix),
the `BotPersonality` no-ramming clamp (a rammer boss needs an explicit escape hatch, never a
wider clamp), `MinDurability = 0` + the RaceManager retire gate (they exist because of each
other, decision 15).

---

## Findings log

| # | Session | What happened | Severity (blocker / tune / polish / note) | Suspected lever |
|---|---|---|---|---|
| 1 | first play (2026-07-28) | Finished 2nd on the road, game registered P1 | blocker | **FIXED** — finish stamped when projected arc distance ticked over N laps, metres before the physical line for a car off the racing line. `FinishLineGate`: distance still validates the lap (the ring stays dead), the physical start/finish plane times the stamp, sub-tick interpolated, 30 m trust window + 2 s never-strand grace. |
| 2 | first play | Packs in the garage wouldn't open | blocker (UX) | **FIXED** — every refusal was silent. Blocked cards now say the rule (CAR FULL — SELL ONE / ALL COMPONENTS MAXED / NO PART CAN TAKE ONE / NO FUNDS); eligibility mirrors `TryBuyPack` via shared `ShopLogic` predicates. If a pack still refuses with none of those shown, that's a new bug — log it. |
| 3 | first play | No way to repair durability in the garage | blocker | **FIXED** — `RunDirector.RepairCar` existed with no button since the v3 UI rebuild. The rail now has a HULL % + `REPAIR $n` row under the stat bars. Paying-to-repair vs driving damaged stays the decision-15 tension; a repair-granting Spectral card remains open design space. |
| 4 | first play | Run-over screen offered only NEW RUN (same car) | tune (UX) | **FIXED** — MAIN MENU button added beside NEW RUN on both end screens (`IRunHost.QuitToMenu`). |
| 5 | | | | |

Findings 1–4: fixes **verified in play** by the user 2026-07-28, and by the full editor batchmode
suite — 738 Shitboxer tests passed / 0 failed / 1 pre-existing seeded skip (baseline 723 + 15 new:
the finish-gate math, the shared shop predicates, and the four pack-reason fixtures). The painted
start/finish stripe was audited against the new finish plane: every layout's waypoint 0 is
south-centre and the stripe is built at exactly that point, so the line you see IS the line that
times you, on all eight venues.

**When done:** transfer blockers and tunes into the next work block; anything that's a *design*
question (run length, HUD contents, boss presentation) goes to doc 08's open questions rather
than straight to a dial.

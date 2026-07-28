# 08 — Collection Design (components, parts, sectors)

**Status: in progress, 2026-07-27.** Built by walking `Jokers.docx` (the Balatro-mapped collection
spec) against the shipped physics. Decisions below are LOCKED unless re-litigated explicitly.
Open questions are at the bottom — they are the live work. Slices 1–6 playtested 2026-07-23;
slices 7–14 (damage rework, season/bot retune, actives, tuning harness, The Brute, the boss
rotation, editions-as-materials, the eight-track season) built 2026-07-24, slice 15 (the Kart and
the Open Wheeler) 2026-07-27 — **slices 7–15 not yet played**. Everything through slice 15 is
editor-verified headless (723/723 in batchmode).

Companion to `03-game-design.md`. Where the two disagree, this doc is newer; the one deliberate
override is noted in decision 2.

---

## What the source document actually is

`Jokers.docx` is two documents fighting each other.

- **~40% is already translated** into racing verbs — reaction time, coasting, meters left, sharp
  corners remaining, final lap, sectors, no-braking, opponents passed. These work.
- **~60% is untranslated Balatro** — it references `played cards`, `scored`, `hand`, `discard`,
  `hand size`, `suit`, `rank`, `retrigger`, `blind selected`. Doc 03 deleted poker hands and
  Chips×Mult in favour of position-based scoring, so this 60% has no substrate to stand on.

A detail worth recording: the doc's own numbers already encode Balatro's Chips/Mult split without
saying so. Grip values are big and additive (+30, +50, +100, +250); Power values are small and
additive (+3, +4, +8, +12), and nearly every *multiplier* in the doc lands on Power (×2, ×3, ×4).
That is Chips→Grip and Mult→Power. The catch: in Balatro those two multiply into one score. Here
they are independent physical channels — there is no `grip × power`. The scales were inherited; the
engine that justified them was not.

---

## Measured physics headroom

Taken from the shipped chassis assets, not from code defaults.

| | GripBox | PowerBox |
|---|---|---|
| Mass | 1050 kg | 1350 kg |
| Layout | **AWD** | RWD |
| PeakMu (F/R) | 1.32 / 1.32 | 1.12 / 1.08 |
| Peak torque | 205 Nm @ 5200 | 360 Nm @ 3800 |
| Redline | 7400 | 6400 |
| Final drive | 4.7 | 4.1 |
| Spring rate | 68000 N/m | 56000 N/m |
| Downforce | 2.4 | 0.6 |

### Ceilings before the car stops being a car

| Stat | Ceiling | Usable band | What breaks past it |
|---|---|---|---|
| Grip (`PeakMu`) | ~2.2 | **×1.7–2.0** | 2.2g lateral is hypercar-on-slicks. Rollover risk climbs (CoM −0.35, `TyreForceAppLift` 0.5), and `LateralVelocityDamping`'s 1.2g clamp becomes irrelevant so the car changes character. |
| Power (`PeakTorqueNm`) | — | **×1.8** | See traction/rev analysis below. |
| Weight (`MassKg`) | 700 kg floor | **×0.65–1.9** | Below ~700 kg the 68000 N/m springs pogo — natural frequency scales as √(k/m) and the spring rate is authored per chassis. |
| Durability | none today | **none** | `MinDurability` (0.4) and `MaxWearPerformanceLoss` (0.5) are `const` in `VehicleSim`. A "low durability car" is currently *unexpressible*. |

**Total realistic band: about ×2 on everything.** A Balatro run goes from ×4 mult to ×2000+ — a
×500 span. This game has ×2. That single fact is why the collection cannot be carried by stat items.

### Why "+Power" is a weak lever today

PowerBox, 1st gear: `360 × 3.0 × 4.1 × 0.9 / 2 = 1993 Nm/wheel` → 6227 N/wheel → **12,454 N** of
drive. Rear traction with launch transfer ≈ 7946 N × µ1.08 = **8,582 N**. It is **1.45× traction
limited** — more torque does literally nothing in 1st or 2nd.

GripBox, 1st gear (AWD, 4 driven): `205 × 3.4 × 4.7 × 0.9 / 4 = 737 Nm/wheel` → 2303 N/wheel →
**9,214 N**. Traction across four wheels = 10,300 N × µ1.32 = **13,596 N**. **Not limited** — it
uses everything.

Top gear, both: rev-limited, not drag-limited. PowerBox tops at ~209 km/h with 2429 N of drive
against 1350 N of drag; GripBox at ~190 km/h. Torque alone raises neither.

**Consequences:**
1. `SpecModTarget.Power` (which only scales `Engine.PeakTorqueNm`) is near-dead on RWD in low gears
   and dead at the top for everyone. Power items must also touch `FinalDriveRatio` / `RedlineRpm` /
   `GearRatios`, or they are a lie.
2. The same "+Power" part is worth very different amounts on AWD vs RWD. Balance landmine.
3. `MinDurability` / `MaxWearPerformanceLoss` must stop being `const` before the 15-car list works —
   nine of those cars are defined by durability or weight.

---

## Locked decisions

**1 — Stats are an abstract ledger, hard-capped at the physics ceilings.**
Items write to `GripPoints` / `PowerPoints` / etc. One mapping function turns the ledger into a
`VehicleSpec`, and it saturates at the measured ceilings above so no stack can produce an
undriveable car. Doc numbers survive roughly as written. All balance lives in one file.
*This inverts today's dependency:* `StatSummary` currently derives stats FROM the spec (and
`RaceHud` re-implements the same formula — the code already flags this as a desync risk). It becomes
a display of the ledger instead.

**2 — Four visible stats: Power, Grip, Weight, Durability.**
Shown in the **ESC menu and the shop only** — never on the race HUD, which stays position / lap /
sector. *This overrides doc 03's locked two-bar UI*, deliberately: a third of the collection
modifies weight or durability, which a two-bar UI cannot show.

**3 — Depth comes from rule-alteration, not stat stacking.**
Forced by the ×2 ceiling. Stat items can carry maybe 20–25% of the collection. The rest must change
*how you drive*, *what you're paid for*, or *your relationship with the field*.

**4 — Components ship. Ten of them, always installed, leveled.**
This is the doc's Planet-card system, formalised. They level via **Blueprints bought in the shop
with money** (the Balatro way). Separate inventory from the 6 part slots.
*Constraint:* with all ten always installed, "each POWER component gives +3" is a constant, not a
variable. Family jokers must scale off **component level** to have any variance.

**5 — Families ARE the four stats.**

| Family | Components |
|---|---|
| POWER | engine, turbo, exhaust, ecu |
| GRIP | tyres, suspension |
| WEIGHT | interior, chassis |
| DURABILITY | cooling, transmission |

Chosen for legibility: the ESC-menu stat display doubles as the family display for free, and no new
vocabulary is taught. Family jokers reward **lopsided/committed** builds, which is a different axis
from raw magnitude (cf. Balatro's Blackboard).

**6 — Sectors: three per lap, equal length in metres.**
`sectorLengthM = RacingLine.TotalLength / 3`. Derived read-only from the existing guarded
`RaceCarStatus.TotalDistanceM`; sector completion is the same integer-boundary function as
`LapProgress.CompletedLaps` with a different divisor, and sector times reuse `LapTiming.Elapsed` /
`Fold` verbatim.

> ⚠ **Sectors must NEVER become a gate a car has to hit.** The old ordered-checkpoint ring stranded
> human drivers off the racing line and was replaced by the distance gate. If sector boundaries
> become trigger volumes, that bug comes straight back.

**7 — Sector STYLE is the "poker hand" analog.**
Each sector is classified by *how it was driven*, and jokers reward types. Nine scoring events per
race instead of one. Multi-tag, not exclusive.

| Tag | Meaning |
|---|---|
| CLEAN | no contact, stayed on track |
| AGGRESSIVE | landed a hit or made a pass |
| DEFENSIVE | held position under pressure |
| SLIPSTREAM | spent it in someone's tow |
| PATIENT | coasted / lifted / conserved |
| RAGGED | went off, spun, took damage |

Every detector this needs **already exists** in `RaceObserver.cs` (661 lines: passes, pass side vs
corner, divebombs, contacts with fault attribution, defensive moves, yields, bluffs, proximity),
currently feeding only rival memory.

**8 — F1 sector timing colours ship alongside.**
Purple = fastest sector in the session. Green = your own personal best. Yellow = neither.

**9 — Sector income is OPT-IN via parts.**
Sectors pay nothing by default. Only owning a pays-by-sector part unlocks it. The base economy stays
position-only and fully inverted, so `PayoutTable` needs no rebalancing and snowballing costs a slot.
One currency (money) throughout.

**10 — Generate parts generously, prune later.**
"The hard part is making them up; the easy part is deleting them." New parts invented during
implementation get surfaced and added rather than silently dropped.

**11 — Twenty levels per component, ~1.7% of the stat band each.**
200 total levels across 10 components. A run can afford perhaps 50–80, so **you can never max
everything** — you specialise. A run levels 3–4 components deep and leaves the rest near L1.
Individual levels are nearly invisible on the bar; the *count* is what family jokers read.

**12 — Full season: 8 circuits, 24 races, ~75 minutes.** *(Built — slice 8.)*
Overrides doc 05's "start with 1 circuit, not 8" and requires `RunState.TotalCircuits = 8`.
This is what `RunState.DifficultyMult` (convex in `CircuitIndex`) was built for, and it is what
finally makes team upgrades and long-horizon jokers viable — both are structurally dead at
3 races by their own documentation.
*Settles collection size:* at ~85% of the pool seen per run, the full ~150 parts is correct, not 70.

**13 — The player can out-stat the field, but it must be EARNED, not scheduled.**
Bots ramp on a fixed curve landing near the *practical* player ceiling (~×1.7), not the theoretical
one (~×2.0). A typical build lands ~×1.45 and **never crosses**; only a genuinely good build passes
the field, and only in the last third of the season. The first half is survive-and-farm; the second
half is cash it in.

> **Required retune — DONE, slice 8.**
> `botStrengthBase 1.4` (unchanged), `botStrengthPerRace 0.40 → 0.013`, `botStrengthMax 3.0 → 1.70`.
> At the shipped values bots hit the ×3 cap at **race 4** and sit flat for the remaining 20 races,
> at µ 3.96 on GripBox — roughly 4g cornering, well past any driveable player car. Bots survive
> those numbers only because they are on rails following `RacingLine`; a player at µ 4 would have a
> scalpel that flips on kerbs.

**14 — Active items each declare their own charge condition.** *(Built — slice 9.)*
Not one model. An active is defined partly by *how it is earned*, so equipping one changes how you
want to drive. Conditions: **behaviour-charged** (drafting, clean running, contact dealt, damage
taken), **sector-charged** (+1 per sector line), **once per race**, **cooldown**, and
**cost-to-use** (money) — the last is what makes the boss's per-use tax bite.
Single bind, default **Q**, **rebindable in settings**. `VehicleInput.Boost` exists; there is no
rebind layer yet, so that is new work.
`DraftBoost` (charge-and-deploy, built, `Enabled = false`) is the reference implementation.

**15 — Damage: crippled at half, retired at zero.** *(Built — slice 7.)*
One number, not two — the Kart's *"very low health"* meant very low **weight**, not a second stat.

```
DurabilityMult = Durability ^ chassisExponent      floor 0.0

  D = 1.00   full pace
  D = 0.50   ~half pace, "crippled" — will miss the survival cutoff
  D = 0.00   RETIRED, race over, -1 life
```

`MinDurability` (const 0.4) → **0.0** universally; `MaxWearPerformanceLoss` (const 0.5) →
a **per-chassis curve exponent**, which is where chassis character now lives:

| Chassis | Exponent | Pace at D = 0.5 |
|---|---|---|
| Monster truck | 0.4 | 76% — barely notices |
| Grip / Power box | 1.0 | 50% |
| Open wheeler | 2.0 | 25% — crippled hard |

This gives contact racing a real worst case (today the worst outcome of being rammed all race is
driving at 70%), and it lands the Gold enhancement's *"removed if durability drops below 50%"* on an
already-meaningful threshold.

**16 — Build order: prove sector-style scoring before building anything else.**
Decision 7 is the load-bearing invention of this whole document. If sector-style scoring is not fun,
decisions 8 and 9 and a large fraction of the parts design are wrong. It is also the cheapest thing
here to test. So it ships as a standalone vertical slice, and **nothing else in this doc is built
until it is proven.** See *Slice 1* below.

---

## Slice 1 — the experiment

**Goal: find out whether sector-style scoring is fun.** Nothing else.

### Status (2026-07-22)

| # | Item | State |
|---|---|---|
| 1 | Sector split — `SectorProgress.cs` | ✅ built + tested |
| 2 | Timing colours — `SectorTiming.cs` | ✅ built + tested |
| 3 | Style tags — `SectorStyle.cs` | ✅ built + tested |
| — | Evidence accumulator — `SectorObserver.cs` | ✅ built |
| — | Referee integration — `RaceManager` | ✅ built |
| — | HUD — timing strip + style flash | ✅ built |
| 4 | 13 test parts + the `SectorCompleted` consumer | ✅ built + tested |

**Slice 1 is code-complete.** What remains is the thing only playing it can answer.

**HUD design.** The timing strip sits in the **top-right**, under BEST/LAST lap, because that corner
is already where the eye goes for timing. One cell per sector, filling in across the lap like a
broadcast graphic and clearing as each new lap's first sector completes. **The colour bar is the
read**; the time is the detail you only look at between corners. The cell for the sector being driven
gets a faint lit plate — deliberately a *plate*, not a colour, so it can never be misread as a fourth
timing tier.

The style readout is a **transient flash** top-centre, held at full opacity for half its window then
eased out. Transient on purpose: a permanent style readout would coach the player through the sector,
when the point of the slice is to find out whether they can already tell what they just drove.

**How parts score.** `PartDef` gained a `List<SectorRule>` — data on the asset, not code keyed by
part id, because "on style S grant N of resource R" is content and 150 of them would otherwise make
every balance pass a recompile. A rule is *trigger* (style / streak / colour / contacts taken /
places gained / final sector / consistent pace) → *effect* (money / grip / power / durability /
retrigger), with an op, a duration and a count-scaling flag.

`SectorPartRunner` (Meta) is the only layer that can see both sides — Race publishes the sector, Meta
owns the parts, and Race must never reference Meta. All the arithmetic lives in `SectorPartState`,
plain C# with no scene access, so the whole scoring pass is unit-testable.

Effects land on two new host-asserted sim fields, `BonusGripMult` / `BonusPowerMult`, mirroring
`BoostMult`'s design exactly: bare fields the sim never decays, defaulting to 1, folded
multiplicatively alongside the saps, wear and KERS. Money accrues into `RunState.InRaceEarnings` and
banks at payout **whatever the finish** — which is the point of decision 9, since it lets a build pay
off without the player having to finish well.

**Verification:** all gameplay assemblies, the UI layer and the full EditMode suite compile clean
with zero warnings; the suite runs **485 passed / 0 failed / 123 skipped**. The skips are fixtures
that construct `ScriptableObject`s and genuinely need the editor. `RaceHud.uss` was already in
`MetaAssetsBuilder.AttachRaceHud`'s stylesheet list, so the new rules need no builder change.

**Four defects found and fixed during the review pass**, recorded because each is the kind that
would have survived into playtesting as "the feature feels off" rather than as a crash:

1. **Consistency compared the wrong sectors.** `ConsistentPace` measured against the *previous sector
   driven* — but sectors are equal by DISTANCE, so a corner-heavy third takes seconds longer than a
   fast one, and no useful tolerance would ever have been met. Metronome would have been dead
   content. Now compares your last lap through *the same* sector index.
2. **A degenerate zero-second sector could corrupt the session.** Two boundaries credited in one step
   yields `time = 0`, and `Fold` would have installed it as the session best permanently, making
   every genuine sector afterwards read yellow. `SectorTiming.Fold` now rejects non-positive times.
3. **Race-scoped bonuses vanished on a sim rebuild.** `VehicleController`'s out-of-world watchdog
   calls `RebuildSim()`, resetting both bonus fields to 1; a runner that only writes them on a sector
   boundary would silently drop an earned bonus. `RunDirector` now re-asserts them each frame.
4. **Per-frame UI churn.** The timing strip rewrote text, colours and class lists every frame though
   they change three times a lap, and the earnings line allocated a string per frame. All now
   change-detected.

**One balance note, not a defect.** Purple is the fastest sector *anyone* has set, and bots start a
season at ×1.4 grip and power — so `Purple Streak` is near-dead until the player's build crosses over
late in a season (decision 13). That is consistent with the design rather than a bug, so it was
priced as aspirational Rare content and a reachable green-triggered part (`Benchmark`) was added
alongside it, so the colour axis is actually exercisable during slice testing.

**Content pipeline verified (2026-07-22).** All 13 assets materialised, their `SectorRules`
serialised with the correct enum values, all 13 registered in `PartPool` (61 parts total, up from
48), and every new script's `.meta` generated. Unity compiled the project cleanly at 15:52. So the
parts will roll in the shop and the dev EQUIP button will find them.

### Testing the slice — editor-only tools

Open question 6 (tuning iteration cost) turned out to block the slice itself: "shop for the right
part, then drive three laps" is far too slow a loop to answer *is this fun*. The ESC dev menu
therefore gained a `#if UNITY_EDITOR` row — compiled out of player builds entirely:

| Control | What it does |
|---|---|
| **EQUIP SECTOR PARTS** | Grants and slots every sector-rule part in the pool, up to free slots |
| **+ $50** | Funds a shopping trip |
| **FINISH RACE NOW** | Ends the race in the current standing so a payout can be checked immediately |
| *readout* | Live `sector $ / grip × / power ×`, so a test drive is verifiable without print statements |

Two deliberate details: `EQUIP SECTOR PARTS` does **not** re-bake the spec (sector parts carry no
SpecMods, and `ApplyEquippedParts` mid-race would rebuild the sim and wipe accumulated damage), which
means parts can be slotted mid-race and simply start scoring at the next sector line.
`RaceManager.DevFinishRaceNow` stamps finish times in current running order with a hair's separation,
because the leaderboard sorts finishers by time and a shared timestamp would scramble the standings
at the exact moment the payout reads them.

### A verification gap worth recording

The first editor open failed with no `Shitboxer` menu at all. Cause: the standalone dotnet harness
had been pointed at Vehicle / Race / Meta / UI but **not `Shitboxer.Editor`**, and `MetaAssetsBuilder`
was missing `using Shitboxer.Race;` for the new `SectorStyle` / `SectorColour` parameters. Unity drops
every `[MenuItem]` in an assembly that fails to compile, so the whole menu vanished.

The harness now compiles the Editor assembly too. The general lesson: it only ever verifies the
assemblies it is explicitly pointed at, and it cannot see Unity's own compilation — **the editor
Console is the authority, not the harness.**

> ⚠ Run through a standalone dotnet harness compiled against Unity's reference assemblies, because
> the editor was not running and `Library/ScriptAssemblies` is stale (Jul 19). It exercises the pure
> logic faithfully but **not**: the MonoBehaviour lifecycle (`SectorObserver.OnEnable`, the `GetOrAdd`
> install path), `.meta` generation for the six new files, or any of the visual layout. One editor
> run is still needed.

### Deliberately NOT touched
The stat ledger, components/families/Blueprints, the damage rework, the bot ramp retune, the season
length change, the 15 chassis, bosses, enhancements, seals, editions, consumables, actives.
All of those stay exactly as they ship today.

### 1. Sector split
Derived read-only from the existing guarded `RaceCarStatus.TotalDistanceM`:

- `sectorLengthM = trackPath.Line.TotalLength / 3`
- `CompletedSectors(totalDistanceM, sectorLengthM)` — structurally identical to the existing
  `LapProgress.CompletedLaps`, different divisor
- Per car: current sector index, per-sector elapsed, per-sector best
- Times reuse `LapTiming.Elapsed` / `LapTiming.Fold` unchanged

> ⚠ Sectors are a **readout**, never a gate. No trigger volumes, no ordered ring, no "must hit"
> checkpoint. The ordered-checkpoint ring was already removed once for stranding human drivers off
> the racing line; reintroducing it through sectors would bring that bug straight back.

### 2. Timing colours
Purple = fastest that sector by anyone this session. Green = your own personal best for that sector.
Yellow = neither. Needs a session-wide best table alongside the existing per-car bests.

### 3. Style tags
A `[Flags]` classification resolved at each sector line. Multi-tag — a sector can be both
AGGRESSIVE and RAGGED.

| Tag | Condition | Source (all already exist) |
|---|---|---|
| CLEAN | no contact, stayed on surface | `VehicleCombat.OnContact`, `SurfaceGripMult` |
| AGGRESSIVE | landed a hit or completed a pass | `VehicleCombat.OnContact`, `RaceObserver` |
| DEFENSIVE | held position while under pressure | `RaceObserver` defensive-move detection |
| SLIPSTREAM | spent most of the sector in a tow | `DraftSensor.IsDrafting` |
| PATIENT | sustained coasting / lifting | `VehicleInput.Throttle` / `.Brake` |
| RAGGED | went off, spun, or took damage | `VehicleSim.SlipAngleDeg`, `Durability` delta |

### 4. Ten to twelve test parts
See the ledger below. Deliberately spread across all six tags plus one retrigger and one
economy-inverting item, so the slice tests the whole shape rather than one archetype.

### 5. How we know it worked
- **Behaviour test (the important one):** does equipping a sector part visibly change how the player
  drives a sector? If not, the mechanic is decorative.
- **Attention test:** do they look at the sector colours mid-race, unprompted?
- **Legibility test:** can they say what style they just drove without the UI telling them?
- **Rhythm test:** do nine ticks a race read as a rhythm, or as noise?

### Known risk
This lands mid-Phase-4 UI work. The slice needs a small amount of HUD (sector times + colours +
a style readout), which either interrupts the UI pass or gets built twice.

---

## Cut list

**Cut outright:**

- **12 of the 15 reaction-time jokers.** Jolly, Zany, Mad, Crazy, Droll, Sly, Wily, Clever, Crafty,
  Half, Runner, Green, The Duo/Trio/Family/Order/Tribe. ~11% of the collection hanging on one
  300 ms twitch that happens once per race, with no decision and no counterplay. In Balatro these
  are hand-type jokers, and hand type is a choice made 4–5× per blind. **Decision 7 replaces them**
  with a repeatable skill moment. Keep three as a launch-specialist archetype.
- **All ~20 discard / hand-size jokers.** Banner, Juggler, Drunkard, Turtle Bean, Troubadour, Merry
  Andy, Burglar, Faceless Joker, Mail-in-Rebate, Trading Card, Ramen, Yorick, Hit the Road, Burnt
  Joker, Castle, Mr. Bones' hand economy, Stuntman's `−2 hand size`. No honest racing analog exists
  for "reject this and draw another," and inventing one means bolting a subsystem onto a racing game
  to justify copied jokers.
- **Tags (24).** In Balatro they exist because skipping a blind is a real decision. Skipping a
  championship round is thematically odd and adds a decision node before every race. Revisit only if
  the shop feels thin.
- **Stopwatch component.** The source doc can't remember what it was for.
- **Hand-construction jokers:** Splash, Four Fingers, Shortcut, Smeared, Pareidolia, DNA, Sixth
  Sense, Superposition, Séance, Certificate, Cartomancer, Hallucination, Space Joker.
- **"Does nothing → +1 joker slot" voucher.** The joke only lands with Balatro context.

**Keep — these port better than expected:**

- **Meta-jokers that reference other jokers:** Abstract Joker, Joker Stencil, Swashbuckler, Baseball
  Card, Blueprint, Brainstorm, Showman, Oops All 6s, Chicot, Luchador, Riff-Raff, Invisible Joker.
  Zero translation needed. Prioritise these — they are the best build-around items in the list.
- **Growth/risk jokers:** Cavendish, Gros Michel, Glass Joker, Madness, Vampire, Constellation,
  Hologram, Lucky Cat, Campfire, Obelisk. All port onto races-driven / contacts-made / parts-sold
  counters.
- **The entire boss list.** Every one is a rule modifier on the player — exactly the Balatro
  boss-blind shape — and `RaceModifier` (a `[Flags]` enum) is the right home. Best-designed section
  of the source document.

**Two boss conflicts to resolve:**

1. Two bosses "have inclination to touch you." `BotPersonality.cs` hard-clamps every archetype so
   that *"no archetype can make a bot cheat, cut the corridor, or drive into a rival."* That
   guarantee is load-bearing for the normal field. A rammer boss needs an explicitly-flagged escape
   hatch, **not a wider clamp**.
2. "Drifting removes your ability to press gas until you come to a stop" is a DNF on a 3-lap race,
   not a handicap. Retune to a ~2 s lockout or "until pointing within X° of the line."

**Naming collisions in the source doc:** "Steel" appears as both an Enhancement (on components) and
an Edition (on jokers). "Parts" is used for jokers *and* implies components. Locked usage:
**Components** = the 10 leveled, always-installed. **Parts** = the jokers, 6 slots.

---

## Physics assets built but unused

Worth knowing before inventing new mechanics — these already exist, tested, gated off:

| Asset | State | Natural use |
|---|---|---|
| `TyreWear` (thermal band + wear, per wheel) | built, `TyreWearEnabled = false` | Ice Cream is literally a tyre-degradation joker; tyre-compound parts |
| `DraftBoost` / KERS (charge + deploy) | built, `Enabled = false` | the first active item |
| `SurfaceGripMult` / `SurfaceZone` | built, no track marks zones | monster truck, tyre levels "improve driving on different surfaces" |
| `RaceObserver` (661 lines) | live, feeds only rival memory | every sector-style detector in decision 7 |
| `CornerTable` (entry/apex/exit/sign/class) | built | Mystic Summit's "sharp corners remaining" |
| `SpecModOp.Add` vs `Multiply` + slot order | **shipped** | Balatro's +Mult-before-×Mult, already working |
| `PartCondition.Fragile` / `.Cashout` | shipped | Gros Michel, Cavendish, Egg, Glass Joker |
| `InterestPerBlock` / `InterestCap` | shipped | the interest vouchers, the VIP chassis |

---

## Slice 2 — the stat ledger (decision 1)

**Gate cleared 2026-07-22: sector-style scoring confirmed fun**, so decision 16 no longer blocks the
rest of the plan. The ledger goes first because every later system — components, enhancements, seals,
editions — writes into it.

**Scoped deliberately.** Decision 1 also said "invert the dependency so `StatSummary` displays the
ledger", justified by `RaceHud` duplicating the stat formula. That duplication no longer exists — the
IMGUI `RaceHud` was deleted in Phase 4 and `StatSummary` now has exactly one consumer
(`GarageViewModel`). So the load-bearing half was built and the risky rewrite of 47 working stat
parts was not:

- `BuildLedger` — points per stat, unbounded and signed, the single target for all future content.
- `StatLedger.Curve` — saturating: `1 + span·(1 − e^(−points/60))`. Zero points is an exact identity;
  points can run to five figures while the multiplier asymptotes. Negative points decay toward a
  shallower floor so a drawback bites hard but can never invert a stat.
- Spans chosen so the strongest chassis lands **on** the measured ceiling rather than through it —
  GripBox's µ1.32 × 1.65 = 2.18, just under the µ2.2 rollover limit.
- **Power also buys taller gearing.** Half the gain goes into `FinalDriveRatio`, because PowerBox is
  already 1.45× traction-limited in first and rev-limited in top — torque alone barely moves lap
  time. A power *penalty* deliberately does not re-gear, or a drawback would hand back what it took.
- `PhysicsCeilings.Clamp` — a final hard clamp applied to **any** spec, from the ledger or from stat
  parts. Six stacked +10% grip parts already put GripBox at µ2.34, past the ceiling, so this changes
  an existing (unsafe) behaviour on purpose.
- `StatSummary` extended to the four stats of decision 2. Weight is shown as **lightness** so "up is
  good" holds on every bar; `VehicleSpec.DamageResistance` was added for the fourth, inert until the
  damage rework (decision 15) reads it.

**Verification:** 507 passed / 0 failed, all four compile checks clean.

> **A verification bug worth recording.** The first ledger run reported 496 passed / **129** skipped —
> up 6 from the previous 123. Six was exactly the number of `Bake` tests written, and they were being
> *skipped, not passed*: `SpecModApplier.Clone` used a `JsonUtility` round-trip, which does not work
> outside the Unity player loop. The most important test in the set — the ceiling safety property —
> was silently not running.
>
> `VehicleSpec.Clone()` now uses `MemberwiseClone` plus an explicit copy of the one reference-type
> field (`GearRatios`). It is faster, allocates once instead of building a string, picks up new fields
> for free, and works outside Unity. That took skips from 129 to **118** — unblocking 5 further tests
> elsewhere in the suite that had been quietly skipping on the same cause.
>
> Lesson: **a rising skip count is a failure signal.** Passing counts alone would have hidden this.

---

## Slice 3 — components (decisions 4, 5, 11)

Ten components, always installed, levelled 1→20 by Blueprints bought with money. They write into the
ledger from slice 2 rather than touching the spec, so there is still exactly one mapping and one cap.

**Level 1 is the baseline and contributes nothing.** Points scale with `level − 1`, so a fresh run
produces an empty ledger and therefore a spec byte-for-byte identical to the authored chassis. That
is what lets `ApplyEquippedParts` bake unconditionally instead of guessing whether a build exists.

| Component | Family | Per level |
|---|---|---|
| Engine | POWER | +3 power, −0.6 weight *(a bigger engine weighs more)* |
| Turbo | POWER | +3 power, −0.5 durability |
| Exhaust | POWER | +2 power |
| ECU | POWER | +2 power, +0.8 durability |
| Tyres | GRIP | +3 grip |
| Suspension | GRIP | +2 grip |
| Interior | WEIGHT | +3 weight *(lighter)* |
| Chassis | WEIGHT | +2 weight, +1 grip |
| Cooling | DURABILITY | +3 durability |
| Transmission | DURABILITY | +2 durability, +1 power |

**Bake order: components, then parts.** Components are the car's own specification — what it *is* —
so they set the baseline; parts are bolt-ons that modify whatever is underneath. The reverse would
make a part's percentage apply to the bare chassis and then be diluted by component levels, which is
backwards from how a player reads it.

**Persistence** stores levels as `Name:Level` pairs, the same "stable id, never an index" rule the
part and upgrade lists follow — inserting a new `CarComponent` member cannot reinterpret an existing
save's levels as different components. Only components *above* baseline are written, so a fresh run
adds nothing to the file, and a save predating components restores as the pre-component car.

### Two deviations from the source document

1. **Transmission is durability-primary here, not power-primary.** The source doc gives it "power, and
   every third level decreases durability", but decision 5 — which is locked and player-visible —
   puts it in the DURABILITY family, and the whole selling point of that decision was that the stat
   display doubles as the family display. A power-primary component sitting in the durability family
   would break exactly that legibility. It keeps a +1 power secondary. **Worth re-litigating** if the
   source intent mattered more than the grouping.
2. **The "every third level" quirks are flattened** into a smooth per-level penalty (turbo's −0.5
   durability). Aggregate effect is the same and the model stays testable; the stepped version creates
   nicer decision points ("stop at 5 or push to 6?") and could be added later if components feel too
   much like a spreadsheet.

### Not done yet

**Blueprint pricing is a first-pass guess:** `2 + level/4`, so maxing one component costs ~$85
against a season's ~$250 of position income. That should mean a run deepens three or four components
rather than maxing one, but it has never been played. **Slice 6 makes this harder to reach** — a
level now has to be drawn before it can be bought, so the effective cost of maxing one component is
the price plus however many rerolls it took to see it. Re-tune against slice 6, not against this.

**Verification:** 531 passed / 0 failed, all compile checks clean.

---

## Slice 4 — shop rework

Four changes, to bring the garage closer to Balatro's shop feel.

**1 — Reroll escalation is CONSECUTIVE.** The +$1 curve now keys off back-to-back rerolls rather than
a visit total: buying anything (part, pack, upgrade) or selling anything resets it to base. The
escalation therefore punishes *fishing*, not shopping — a player who engages with the shelf gets a
cheap reroll again, one who only spins pays more each time. This is a deviation from Balatro, which
escalates across the whole visit, and follows the literal "cost 1 dollar more if they are
consecutive".

**2 — Two packs every visit**, drawn from a weighted table. `ShopPackKind.Spectral` is declared but
weighted **zero**, because its contents do not exist — a pack that opened onto an empty pick screen
would take the player's money and hand back nothing. The member exists so the save format, UI and
draw table are already shaped for it.

| Pack | Price | Draws | Pick |
|---|---|---|---|
| Parts | $6 | 3 | one part, equipped immediately |
| Components | $4 | 3 | one component, +1 level |
| Spectral | — | — | **not implemented, never stocked** |

**3 — Bought parts are always equipped, and can be sold.** There is no owned-but-benched state for
anything bought. Selling refunds half the price (floored at $1) and removes the part from the run
entirely. Selling is deliberately allowed while a pack is open, or a player who filled their last
slot and then opened a pack would be stuck with nothing to do.

**4 — Buying is refused when the car is full.** Applies to shelf buys, parts packs (refused at *buy*
time, so a pack whose prize can't be taken is never sold) and pack picks (refused at take time, with
the pack left open so selling then taking works).

### Verification, and a testing problem worth recording

The reworked rules were verified behaviourally — 11/11 on reroll escalation, the streak reset, the
slot gate, auto-equip, sell value, pack rolling, the spectral exclusion, and the component pick.

**But the project's own `ShopLogicTests` cannot run outside Unity.** All 131 skips in the standalone
harness are `SecurityException` — Unity's native `ECall` methods refuse to run outside the player, so
any fixture that calls `ScriptableObject.CreateInstance` skips. `ShopLogicTests` is now 32 of them,
covering exactly the code this slice rewrote.

That was nearly a silent failure: the suite reported **531 passed / 0 failed** immediately after a
rework that changed crate behaviour, and a test asserting the OLD behaviour should have failed. It
didn't — it wasn't running. The harness now **reports skips broken down by fixture**, so a green
number can no longer hide an unverified subsystem.

> **The editor's Test Runner is the authority, not the standalone harness.** 131 tests — including
> every shop, save, garage and part-content fixture — only run there. Run
> *Window → General → Test Runner → EditMode → Run All* after any change to those areas.

---

## Slice 5 — the garage screen

The screen caught up with the rules underneath it.

**Rail:** four stat bars now (Power / Grip / **Lightness** / Durability). Weight is shown inverted so
"up is good" holds on every bar — the only way four stats stay glanceable. The owned list became
**FITTED**, and since a bought part is always fitted there is no EQUIP action left; each row carries
an explicit `SELL $n` button instead. Deliberately a button rather than a click-anywhere row: with
buying gated on a free slot, selling is the *only* way to make room, so it must never be a misclick.

**Main:** the shelf, then **PACKS** (two cards), then **COMPONENTS** (ten rows). All below one
another rather than behind tabs — everything you can spend on should be visible at once, or the shop
stops being a comparison and becomes a menu. Each component row is tagged with its family, colour-
matched to the stat bar it feeds, so decision 5's grouping needs no explaining.

> **Superseded in part by slice 6.** The ten COMPONENTS rows no longer carry a BUY — they became a
> read-out, with a **BLUEPRINTS** row of rolled stock above them. The layout and the family tags are
> unchanged.

**Pack picks reuse the same elements.** An open parts pack replaces the shelf; an open components
pack replaces the component list with its three picks. The pick reads as "these, choose one" rather
than as a modal floating over a list you can still shop from.

**Blocked buys say WHY.** `CAR FULL — SELL ONE` rather than `NO FUNDS` when that is the actual gate.
"I have money and it won't let me buy" reads as a bug unless the screen names the rule.

**Verification:** 13/13 garage behaviours confirmed behaviourally (four bars populated, ten
components, two packs, Blueprint charges and levels, the grip bar moving, the full-car gate, sell
refund and slot release, the components-pack pick flow, shelf hidden while a pack is open). Suite:
534 passed / 0 failed.

**Two test bugs found and fixed in the process** — both mine, both of the same kind:
`EveryVisitListsTwoPacks` asserted against a `FakeRunHost` that never opened a shop visit, so the
pack list was legitimately empty; and `AFullCarBlocksBuyingAndSaysSo` was iterating an *empty* offer
list and therefore passing vacuously while proving nothing. A complement test now pins that an
unopened shop shows no phantom stock.

**Answered along the way:** component levels are clearly *felt*, not invisible — nine levels of Tyres
moved the grip bar from 47 to 62.

---

## Slice 6 — Blueprints are rolled, not browsed

**Re-litigated 2026-07-22, and this one overrides slices 3 and 5.** Slice 5 shipped the ten COMPONENTS
rows each with a `+1 $price` button: every component buyable, every visit, in any order. That is a
*menu*, not a shop. It also quietly made the rest of the shop optional — with a guaranteed path to any
stat you wanted, the rolled shelf was only ever a bonus.

**A component level now has to turn up.** Two routes, both random:

| Route | How | Cost |
|---|---|---|
| **Blueprint on the shelf** | `ShopLogic.BlueprintOfferCount` = 2 rolled per visit, refreshed by the same reroll as the parts shelf | `BlueprintPrice(current level)` |
| **Components pack** | unchanged from slice 4 — $4, draws 3, pick one | paid at pack-buy time |

**The ten-row list survives as a read-out.** You must still be able to see what the car *is* — level,
family, MAX — so the list stays, minus every buy button. The split is the point: **BLUEPRINTS is
stock, COMPONENTS is status.**

**`ShopLogic.TryBuyBlueprint` is the only legal path.** `RunState.BuyBlueprint` stays as the
money-and-level primitive with no notion of what's for sale; the shop method adds the shelf check.
Every caller (director, gallery, test fake) goes through the shop — calling the primitive directly is
what would silently restore the old menu, so it's called out in both doc comments.

**Rules that fell out of it:**

- A bought Blueprint is **consumed**. The next level of that component has to turn up again.
- The row **rerolls with the shelf**, not with the packs. One reroll buys a whole new shelf.
- Buying a Blueprint **breaks the consecutive-reroll streak**, like every other purchase.
- Maxed components are **never stocked** — the draw shares the pack's candidate filter, so "what can
  turn up" means one thing in both places. Late in a run the row legitimately runs short, and the
  garage hides the header rather than showing an empty section.

**Knock-on for tuning:** slice 3's "maxing one component costs ~$85" is now a floor, not the price.
The real cost includes the rerolls spent waiting for that component to appear, which is exactly the
pressure that should make a run deepen three or four components instead of one. Unmeasured.

**Verification:** compiles clean (0 warnings) across Meta, UI, UI.Model and Tests.EditMode. Nine new
tests written — six on the shop rules, three on the view model — **not yet run**: the MCP bridge
dropped mid-run, and these fixtures only execute in the editor's Test Runner (see slice 4).

---

## Slice 7 — the damage rework (decision 15)

**Durability is real.** Open question 1's durability half, built 2026-07-24:

- `MinDurability` 0.4 → **0.0**. A car can now be wrecked outright.
- `MaxWearPerformanceLoss` (const) → **`VehicleSpec.WearExponent`**, the per-chassis curve of the
  decision-15 table: `DurabilityMult = D^exponent`, cached on write so the per-wheel force path
  never calls `Pow`. Both shipped boxes sit at the default 1.0 — existing assets need no edit,
  Unity fills the missing serialized field with the C# default.
- **`DamageResistance` went live**: folded in at `ApplyDamage` intake, so it protects against every
  wear source including boss-amplified hits. Capped at **0.9 in both `Validate` and the ledger
  bake** — the bake previously `Clamp01`ed, which would have let a deep-durability build become
  literally unhittable the moment the sim started reading the field.
- **`CarRaceState.Retired`**: RaceManager retires any car whose durability hits zero — bots
  included, and `ReleaseBot` stops a wrecked bot sawing at a wheel that no longer grips. A retired
  PLAYER fast-resolves the race via `FinishRaceNow` (the old editor-only `DevFinishRaceNow`,
  promoted to runtime because retirement now needs it): the running order is stamped final, the
  retired car sorts behind every finisher, and the failure path pays the elimination consolation,
  takes a life, and retries the race. The HUD verdict reads `RETIRED — CAR DESTROYED`.
- **Race-start durability floor, 0.25** (`RunDirector.RaceStartDurability`) — a rule the
  implementation forced. A failed race is *retried*; without the floor, a broke player with a
  wrecked car would retire at every green flag until the run bled out, an automatic game-over with
  zero player input. So overnight the crew hammers the panels straight enough to roll, free. 25% is
  still deeply crippled (25% pace at exponent 1) and the garage repair still reads the true carried
  wear, so the strategic choice of driving damaged to save money survives.
- **Fragile parts break at the crippled line** (`CarDurability <= 0.5`) instead of near the old 0.4
  floor — the same threshold the Gold enhancement will read, so "heavily damaged" means one thing
  everywhere.
- `RepairCostFor` generalised for free: the wear span is the whole 0..1 range now, so a
  durability-0.2 car pays 80% of full price rather than the floor-clamped 100%.

**Verification:** standalone harness rebuilt this session (the previous one was session-local) —
542 passed / 0 failed / 144 skipped, zero warnings, all assemblies + Editor compiled. The 144 skips
are the known `ScriptableObject` fixtures; the editor's Test Runner remains the authority for them.
Two old tests were deliberately rewritten, not patched: the "floor keeps a wreck driveable"
assertion (that design is exactly what decision 15 overturns) and the repair-cost floor pins.

---

## Slice 8 — season + bot ramp retune (decisions 12, 13)

Both flagged as required in the decisions above; built 2026-07-24. `totalCircuits 1 → 8`,
`racesPerCircuit 5 → 3` (the 5 existed to give a ONE-circuit season enough garages; 8 circuits give
24), `botStrengthPerRace 0.40 → 0.013`, `botStrengthMax 3.0 → 1.70`, base 1.4 unchanged. The curve
lands on the cap **by ramp, not by clamp**, exactly at race 24: `1.4 + 23×0.013 = 1.699`.

Three things worth knowing:

- These are **inspector fields serialized into all three race scenes** — the C# defaults alone
  would have changed nothing for the existing scenes. Scene YAML edited directly; `RunState`'s
  default and the old "season is ONE circuit" pin test both now assert the decision-12 shape.
- **Track rotation needed no code**: `SceneForRace` already cycles `raceScenes` modulo race number,
  so 8 circuits over 3 built tracks just repeats the loop. Real track variety is open question 5.
- **`ApplyDifficulty` saturates early**: `DifficultyMult` hits the bot-commitment band's ceiling
  (1.3) around circuit 3 and the survival cutoff tightens to its 0.08 floor by mid-season, so from
  there the *only* thing still ramping is the ×1.7 grip/power curve. Probably fine — commitment was
  always meant to be subtle — but it is untested at 24 races and worth watching in the first full
  playthrough.

---

## Slice 9 — active items (decision 14)

Built 2026-07-24, **not yet played**. The shape the doc asked for: **the charge condition is the
data** — `PartDef` gained an `ActiveSpec` (condition + fill/chunk rate + boost + drain + min charge
+ per-use cost), and the reservoir underneath is `DraftBoostModel` itself, the reference
implementation the doc named, extended with one `AddCharge` method for event-driven conditions. Its
hard ×1.5 boost ceiling therefore bounds every active for free.

- Conditions shipped: **Drafting, CleanRunning, ContactDealt** (only hits ≥50% your fault — getting
  rammed is DamageTaken's identity, paying both ways would blur the two), **DamageTaken** (charge
  per 10% durability lost), **SectorLine, OncePerRace** (arms full, never refills), **Cooldown,
  PaidUse** (always ready, each press billed — the hook a boss's per-use tax lands on; `UseCost`
  can ride on ANY condition).
- **One bind, one active**: the FIRST equipped active part owns the key; the runner
  (`ActivePartRunner`, the same Meta-seam shape as the sector runner) gathers world signals, steps
  the pure state, pays the use cost exactly once per deploy, and holds the boost on the sim every
  tick — the write doubling as the re-assert against the watchdog's mid-race sim rebuild.
- **Rebind shipped small**: a curated dropdown (Q/E/F/R/X/C/LeftShift/Space) in settings rather
  than a listen-for-any-key layer. `ActivateKeyBinding.Parse` falls back to Q on anything stale, so
  the bind always exists; the HUD hint shows the normalized key that actually works. A full rebind
  layer remains future work if the game ever needs more than this one bind.
- **HUD**: a charge meter under the durability bar — cobalt while charging, green when the press
  would bite (charged AND affordable), amber while live. Hidden entirely without an active.
- **8 parts, one per archetype**, so playtesting exercises every charge identity: Tow Cell,
  Zen Battery, Haymaker Cell, Payback Coil, Checkpoint Charge, One Big Push, Slow Reactor,
  Push To Pass ($2/use).
- The dormant designer-gated `DraftBoost` component stays exactly as it was — disabled it writes
  nothing, so the two can never fight over `BoostMult`.

**Verification:** 553 passed / 0 failed / 144 editor-only skips, zero warnings. The pure state
(charge gates, once-per-race no-refill, paid-use billing, the pause zero-dt no-op, the ceiling
clamp) is covered; the Unity glue (sensors, combat events, the real keyboard) is what playtesting
exercises.

---

## Slice 10 — the tuning harness grows (open question 6)

Two more editor-only ESC-menu tools, forced by the 24-race season: **NEXT CIRCUIT >>** (jump to the
next circuit's first race — no payout, no life, mirrors StartNextRace's transition exactly so
nothing downstream can tell) and a **TIME ×4** fast-forward toggle (applied on resume; the frozen
menu itself stays frozen). With FINISH RACE NOW these make "what does circuit 6 feel like"
a two-click question.

---

## Slice 11 — The Brute (the 15-car list begins)

Built 2026-07-24. The chassis-select stub from Wave 27 (id 2, greyed) is a real car, and the first
chassis whose character lives in the decision-15 damage model: 1600 kg of RWD lug — 430 Nm at
3200 rpm, the field's worst grip (µ 1.02–1.04), slow gentle steering, a brick's drag — with
**WearExponent 0.4** (76% pace at half durability; a beating barely registers) and authored
**DamageResistance 0.25**. Wired as `chassisSpecs[2]` in all three race scenes and the builder.
Found and fixed along the way: the `chassis_brute` unlock flag existed only as a gate — nothing
ever GRANTED it. Any season clear now unlocks it (`RecordRunEndToMeta`).

---

## Slice 12 — the boss rotation

Built 2026-07-24. The keep-list called the boss list the best-designed section of the source doc;
meanwhile the 8-circuit season was running the same unannounced top-3 gate eight times.
`RaceRuleset.BossForCircuit` now rotates four bosses — each met twice a season, each on a lever
that is actually **wired** (`ReverseGrid` is declared-but-unwired and pinned off live bosses):

| Circuit %4 | Boss | Rule |
|---|---|---|
| 0 | **THE ENFORCER** | DamageAmplified — the shipped headline boss |
| 1 | **DIRTY AIR** | new `RaceModifier.DirtyAir`: every `DraftSensor` disabled at bind — drafting, draft-charged actives, draft-leech and SLIPSTREAM tags all read dead air |
| 2 | **THE TAXMAN** | new `RaceModifier.ActiveTaxed`: +$2 per active deploy (decision 14's promised per-use tax, on the same UseCost path PaidUse bills through), sweetened with DoublePayout |
| 3 | **THE LONG HAUL** | 7 laps — the endurance exam decision 15 makes real |

All four keep `NoRepairAfter` (boss damage riding into the garage is the shared identity). Rulesets
carry a `Title`; the HUD status line announces the boss ("BOSS 3/3 — THE TAXMAN") and the
race summaries name who beat you. **`bossRacesEnabled` is ON by default now** — the 24-race season
is what boss variety was for; the old OFF default was a compatibility guard, not a design stance.

~~**Deferred with a reason — editions-as-materials (the Spectral pack).**~~ **Built — slice 13.**

---

## Slice 13 — editions as materials (the Spectral pack)

Built 2026-07-24, from the deferral note above. The trap it was deferred over is the design's
centre: **editions live on the RUN, never on the `PartDef` asset** — `RunState.PartEditions`
(id-keyed), read through `EditionOf`, which lets a run-applied material beat the authored edition.
Materials only ever climb (`TryUpgradeEdition` refuses same-or-lower), and a sold or broken part
forfeits its edition.

- **The bake**: `SpecModApplier.Apply` gained an edition-resolver parameter; the director and the
  garage preview both pass `Run.EditionOf`, so the amplified car and its preview cannot disagree.
  Null resolver = the asset's authored edition = every pre-slice-13 caller unchanged.
- **The pack**: stocked at weight 15 (vs 50 parts / 40 components) — the scarce pull, since its
  prize outclasses one component level. Draws 3 offers, each **pre-aimed** ("[HOLO ×1.5] →
  JUNKYARD TURBO") at a distinct fitted stat part, tier rolled strictly above the part's current
  one (Foil 60 / Holo 30 / Polychrome 10 — from Holo the only pull is the jackpot). Pre-aiming
  keeps the pick a one-tap decision — no second target-choosing step. Refused at buy when no
  fitted part can take an edition; only stat parts qualify (editions amplify SpecMods — a material
  on a stat-less part would be a lie).
- **The economy**: sell value prices the applied edition in (×1.5/×2/×3 on the half-refund), so a
  material is value banked, not burned. Selling an open offer's target purges that offer; if that
  empties the pack, the pack resolves.
- **The save**: `partEditions` + open-pack offers persist. Found and closed along the way: an open
  COMPONENTS pack was never saved at all — a quit mid-pack silently ate its price. All three pack
  kinds now survive a save.

**Verification:** 566 passed / 0 failed / 150 editor-only skips in the standalone harness, zero
warnings. The codec and weight table are pure-tested; the pack/eligibility/sell/save behaviours
are editor-fixture tests, same as the rest of the shop.

---

## Slice 15 — the car list grows (The Kart, The Open Wheeler)

Built 2026-07-27, **not yet played**. (Slice 14 — the eight tracks — is recorded under open
question 5.) Two more of the 15-car list, and precisely the two this plan already specced in hard
numbers: open question 2's Kart and decision 15's exponent-2.0 open wheeler. Both are pure
`VehicleSpec` content — zero new plumbing, the slice-11 recipe verbatim (asset + catalog entry +
builder line + one guid line in each of the eight scenes). The car-select UI needed nothing: it
iterates the catalog, and five 168 px cards wrap 3+2 inside the 560 px menu body.

- **THE KART (id 3)** — *"very low health… standard durability"*, taken at decision 15's word:
  **750 kg** (the 700 kg spring floor is the design wall; springs re-authored at 48 kN/m to keep
  GripBox's ~1.3 Hz ride frequency), RWD, 128 Nm at 7,600 rpm screaming to a 9,200 redline, the
  field's fastest steering (260°/s, 34° lock) — and **no damage field authored at all**
  (WearExponent 1.0, resistance 0, both by omission). Open question 2 explains why that omission
  is the whole design. Weight-reduction parts are near-dead on it (already at the floor) —
  deliberate anti-synergy, the deck that doesn't want the "good" items.
- **THE OPEN WHEELER (id 4)** — decision 15's table row made real: **WearExponent 2.0** (25% pace
  at half durability — a crippled Open Wheeler will miss the cutoff), µ 1.62/1.58 slicks,
  downforce 3.4, 880 kg on formula-stiff 82 kN/m springs, 240 Nm at the top of an 8,400 redline.
  Best power-to-weight in the field (~1.6× the boxes) — priced by glass: every hit is a crisis on
  an exponent-2 curve. Fifth gear is deliberately long-ratioed to ~200 km/h so PowerBox keeps the
  straight-line crown. **Base-pace flag for the first playtest:** its clean-lap pace may simply
  beat the ×1.7 bot ceiling — if it trivialises the back half of the season, pull PeakTorqueNm or
  µ before touching the wear curve; the glass is the identity, the pace is the dial.
- **The unlocks chain, Balatro-style, zero new telemetry** — both rules read state
  `RecordRunEndToMeta` already holds. The Kart: clear a season **in The Brute**
  (`Run.ChassisId == ChassisCatalog.BruteId` — drive the anvil, earn the feather). The Open
  Wheeler: a **barely-scratched clear** — end the season's final race (THE LONG HAUL's seven laps)
  at ≥ `RunDirector.PristineClearDurability` (0.75, comfortably above the 0.5 crippled line),
  proof of the clean driving its wear curve demands.
- **Found and closed along the way:** `RunSave` never persisted `ChassisId`. RunState's own
  doc-comment promised "a resumed run keeps its car"; every resume actually reverted to the
  GripBox (id 0), which quietly nullified the Brute unlock slice 11 shipped. The save carries it
  now; absent from an older save it defaults to 0 — exactly the reversion those saves already had.

**Verification:** standalone harness 572 / 0 / 152, zero warnings — five new pure tests (both
lock chains, the BruteId pin, the pristine threshold sitting above the crippled line, flag
uniqueness); the two new save round-trips are editor fixtures like the rest of RunSaveTests.
Full editor suite re-run in batchmode: **723 Shitboxer tests passed / 0 failed** (slice 14's 716
plus the 7 new; the one skip is ShopLogicTests' own seeded `Assert.Ignore`, pre-existing; the 10
McpUnity failures are the usual bridge-package noise). The hand-edited scene wiring was
self-audited by re-running the builder's `AddRunModeToRaceScene` on RaceTest headless and
diffing — the builder reproduces the committed YAML.

---

## The editor verification gap — CLOSED (2026-07-24)

Unity batchmode runs headless from WSL (`-batchmode -nographics -runTests`, editor closed on the
Windows side first). That retires the standing "one editor run is still needed" caveat that every
slice since 7 carried:

- **Build Meta Assets ran**: the 8 active-part assets materialised with their `ActiveSpec` blocks
  serialised correctly; `PartPool` registers 69 parts.
- **The FULL EditMode suite ran in the real editor** — including the ~150 ScriptableObject
  fixtures (shop, save, garage, part content, spectral) the standalone harness always skipped.
  First pass: 759/771, and the only real failure among them was a stale pin the harness could
  never catch (`ApplySeasonShape_ReStampsAfterSaveResume` asserting the pre-decision-12 season
  default of 1). Fixed; re-run green. The other 10 failures are the MCP bridge package's own tests
  wanting a live WebSocket server — third-party noise.
- **761 Shitboxer tests, all passing in the editor.** One caution for the next person: Unity's
  `-runTests` exit code reads 0 even on failures — parse the results XML, never trust the code.

What batchmode still cannot answer is play: slices 7–15 remain unplayed, and the doc's behaviour
tests (does retirement feel fair, does an active change how you drive, does the season's back
half hold) are still the gating step.

---

## Open questions

1. ~~**Durability and weight need to become real.**~~ **Durability done — slice 7.** Weight was
   always expressible (`MassKg` + the ledger); the remaining half of this question is content, not
   plumbing: the nine durability/weight-defined cars, Seals, and Enhancements can now be authored.
   **Slice 15 authored the two cars the plan docs themselves spec in hard numbers** (the Kart and
   the Open Wheeler; the Brute was slice 11). The rest of the 15-car list needs identities pulled
   from `Jokers.docx` (not in the repo) before it needs any code. Seals and Enhancements remain.
2. ~~**"Health" vs "durability."**~~ **Closed — slice 15, by authoring the Kart.** One number
   stands. "Very low health" is carried entirely by 750 kg of momentum physics: contact damage is
   shared-impulse (`collision.impulse` reads the same for both cars), so the Kart wears no faster
   than what hits it — it gets *displaced* (Δv ∝ 1/m; the Brute outweighs it 2.13:1), and in a
   position-scored, survival-cutoff economy, being punted off line IS the injury. The Kart ships
   with **no damage field authored at all**. "Fragile" turned out to be the *other* axis
   (WearExponent — the Open Wheeler's 2.0). A second health pool was never needed.
3. **v1 scope and sequencing.** Components, Enhancements, Seals, Editions-as-materials, Spectrals,
   Tarots, Actives, Booster tiers — eight subsystems, on top of Phase 4 UI already mid-flight.
4. **What the race HUD shows**, now that the four stat bars are excluded from it.
5. ~~**Eight circuits needs eight tracks.**~~ **Done — slice 14.** Eight greybox venues, one per
   circuit (`RunDirector.DefaultRaceScenes` orders the season arc; scene selection is keyed by
   CircuitIndex so a circuit's races and boss run at home). The five new layouts carry their doc-06
   theme in the builder's Character strings for the future art pass; what greyboxing can't express
   (weather, ice, hazards) stays doc 06's art/mechanics work.
6. ~~**Tuning iteration cost.**~~ **Done — slice 1's dev row + slice 10.** FINISH RACE NOW,
   NEXT CIRCUIT jump and TIME ×4 cover skip-race / jump-to-circuit / fast-forward; grow it only if
   a tuning session finds a missing lever.
7. **Blueprint pricing and shop presence.** How many per shop, at what cost, and do they compete
   with parts for shelf slots or get their own row?

---

## Running parts ledger

New parts invented during design or implementation get logged here before they become assets.
Per decision 10: generate generously, prune later.

### Slice 1 test set — sector-style parts

Spread deliberately across all six style tags, plus one retrigger and one economy-inverting item,
so the slice tests the whole shape rather than a single archetype.

**Shipped in `MetaAssetsBuilder`** (assets materialise on the first *Build Meta Assets* run).
Magnitudes are sized against 9 sectors a race and a $5–13 position payout, so a part that fires most
sectors pays ~$1–2 and one that fires rarely pays more.

| Part | Trigger | Effect | $ | Rarity |
|---|---|---|---|---|
| **Bruiser's Ledger** | AGGRESSIVE | +$2 | 6 | Common |
| **Coward's Purse** | PATIENT | +$3 | 7 | Common |
| **Parasite** | SLIPSTREAM | +$2 | 6 | Common |
| **Benchmark** | green sector | +$1 | 5 | Common |
| **Tithe Collector** | per contact TAKEN | +$1 each | 7 | Uncommon |
| **Metronome** | within 0.25 s of your last lap through this sector | +$2 | 8 | Uncommon |
| **Fair Weather Friend** | took a place | +6% grip, 2 sectors | 7 | Common |
| **Rear Guard** | DEFENSIVE | +3% grip, rest of race, stacks | 8 | Uncommon |
| **Panel Beater** | RAGGED | *repairs* 4% durability | 8 | Uncommon |
| **Purple Streak** | purple sector | +12% grip, 1 sector | 10 | Rare |
| **Clean Sweep** | 3 consecutive CLEAN | ×1.25 power, rest of race | 11 | Rare |
| **Sector Nine** | final sector of final lap | ×1.6 power | 10 | Rare |
| **Consistency Bonus** | CLEAN | **retrigger** every other sector rule | 12 | Rare |

Why this set, beyond covering the six tags:
- **Coward's Purse** is the sharpest test of decision 9 — it pays for driving slowly, so it probes
  directly whether the inverted economy still has teeth once parts can pay in-race.
- **Tithe Collector** pays you for being rammed: the inverted economy applied to contact.
- **Panel Beater** turns a failure state into a resource — clever or broken, worth finding out.
- **Consistency Bonus** is the only retrigger. If retriggers don't feel good here, the whole rescued
  retrigger cluster from the source document should be cut.
- **Clean Sweep** fires on *reaching* a 3-streak, not on being at or past it, so a permanent
  multiplier can't compound every sector of a clean race. There is a test pinning that.

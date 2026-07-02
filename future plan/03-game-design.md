# 03 — Game Design

**Updated to your locked decisions (2026-07-02).** This is no longer a Balatro *clone* — the poker
scoring and the deckbuilding are **gone**. What Shitboxer keeps from Balatro is its **best three
ideas**: the between-race **shop with rerolls & escalating cost**, the **stacking-parts build depth**
(jokers-as-car-parts), and a **risk/reward economy**. It bolts those onto a **sim-cade contact racer
with position-based scoring.**

---

## What Shitboxer is, in one line

> A 3D third-person **contact racing roguelike**: race 8 cars on a track, your **finish position**
> decides your **money** (inverted — losers get more, a catch-up economy), spend it in a **garage
> shop** on **parts** that stack onto your car, and survive a **season of circuits** without getting
> knocked out. Solo vs bots; online vs real people.

---

## The core loop (single-player)

```
START a run  ──►  pick 1 of 2 cars (Grip car or Power car)
      │
      ▼
   RACE  (you + 7 bots, contact racing, sim-cade physics)
      │   ├─ Finish POSITION → MONEY  (1st = least $, last = most $ — inverted catch-up)
      │   └─ SURVIVAL GATE → finish within X% of winner's time (or top-N).
      │                       Miss it → lose a life.  0 lives → RUN OVER (roguelike death).
      ▼
   GARAGE SHOP  (spend money)
      │   ├─ Buy PARTS (boost Grip/Power, improve economy, or attack rivals)
      │   ├─ REROLL the stock (escalating cost, like Balatro)
      │   └─ Permanent team upgrades
      ▼
   next race … after N races → BOSS/FEATURE RACE (circuit finale)
      │   └─ Tougher gate: must PLACE TOP-N / WIN to advance (this is why you invest money)
      ▼
   next circuit …  clear all 8 circuits → WIN THE SEASON
```

**Single-player = you vs 7 bots.** Bot difficulty scales per circuit (and per license class, later).

---

## The signature tension (this is the whole design)

Because **money is inverted** (worse finish = more money) but **survival needs you to not be last-ish**
(finish within the cutoff), every race asks:

> **Do I push to win — safer survival but *less* money and a weaker future car — or hang back to farm
> money — richer build but I risk missing the cutoff and losing a life?**

This is a **self-balancing rubber band**: winners get starved of cash and stay beatable; strugglers
get funded to catch up. It's the racing equivalent of Balatro's interest/risk economy, and it's what
makes the run interesting instead of a snowball. **The boss/feature race breaks the farming loop** —
there you *must* place well, so you can't just cruise in last forever; you have to spend the money
you hoarded to actually win when it counts.

> ⚠️ **The one balance risk to watch: sandbagging.** If the survival cutoff is loose, optimal play
> becomes "cruise in dead-last-but-safe every race to max money." Counters to design in: (a) make the
> cutoff **tight/dynamic** so last place is genuinely at risk of elimination; (b) **boss races require
> a top finish** (already in the loop above); (c) consider **non-linear money** (diminishing returns
> at the bottom) or a small meta reward for winning. Flagged again in doc 07.

---

## The two headline stats: Grip & Power

Players see **two bars**. Hover/detail expands each into the sim-cade sub-stats parts actually tune.

| Headline stat | What it feels like | Underlying sub-stats parts modify |
|---|---|---|
| **POWER** | Top speed, acceleration, "how fast" | Engine torque curve, HP, gearing, forced induction (turbo), power-to-weight (so **weight** feeds this) |
| **GRIP** | Cornering, stability, traction, "how planted" | Tyre curve peak/stiffness, suspension (springs/dampers, load transfer), downforce, weight distribution, brakes |

Keep the **two-bar UI** as the primary read; the deeper stats are for players who want to min-max.
This maps directly onto the physics layer (doc 02b): POWER ≈ drivetrain, GRIP ≈ tyre + suspension +
aero.

---

## The two starting cars (Grip vs Power)

| Car | Grip | Power | Feel | Who it's for |
|---|---|---|---|---|
| **The Corner-Carver** (Grip) | High | Low | Nimble, planted, forgiving, wins twisty tracks | New players, technical circuits |
| **The Brute** (Power) | Low | High | Fast, twitchy, hard to tame, wins on straights & contact | Aggressive players, speed tracks |

Two is the starting set (per your spec). More chassis unlock via meta-progression later (each a
different starting rule — the "deck" analog). Grip↔Power is the axis the whole upgrade economy pushes
and pulls along.

---

## Parts — the "jokers," but equipment, not a deck

**No deckbuilder, no card-drawing.** Parts are **equipment you buy in the shop and slot onto your
car**. You choose what to buy; you don't draw a random hand. They stack and combo — that's where the
build depth (the Balatro DNA) lives. Three functional families (your spec):

### 1. Stat parts — tune Grip / Power
Boost the headline stats or their sub-stats, often with a trade-off.
- *Turbo* → +Power (maybe −reliability). *Sticky Compound* → +Grip. *Stiff Springs* → +Grip in
  corners, worse over bumps. *Weight Reduction* → +Power-to-weight & +Grip, −collision toughness.
- Trade-offs are what make choices interesting: a pure "+everything" part is a boring part.

### 2. Economy parts — tune the money loop
- *Sponsor Livery* → +$ per race. *Better Accountant* → higher interest/savings cap. *Scrap Dealer*
  → cash when you sell/replace parts. These let you lean into the catch-up economy on purpose.

### 3. Attack parts — hurt rivals (mix of on-contact + proximity, per your choice)
- **On-contact** (leans into collisions being core): *Ram Bars* → hitting a car saps their Grip
  briefly / damages them; *Spike Plates* → contact costs them Power.
- **Proximity auras** (always-on, no button — fits no-active-items): *Disruptor Field* → cars right
  behind you lose grip; *Draft Leech* → you steal slipstream benefit from cars ahead; *Oil Haze* →
  nearby rivals' handling degrades.
- In **single-player** these work against bots; in **multiplayer** they become genuinely nasty vs
  real rivals — a big part of the "play with friends" chaos.

### Optional depth: slot order (the one Balatro mechanic worth keeping)
Balatro's most-loved lever is that **joker order matters**. You can carry a *light* version: parts
resolve in **slot order**, so an additive *+Grip* part placed **before** a *×Grip* multiplier part
yields more than the reverse. This gives min-maxers a puzzle without needing the poker math. **Ship
it only if the base game is already fun** — it's a spice, not the meal, now that scoring is
position-based rather than Chips×Mult.

### Part modifiers (carried over, tied to parts not cards)
- **Quality/rarity tiers** (Common→Rare) — rarer parts, bigger effects, higher price & shop rarity.
- **Finishes** (editions): +flat / +% / a **"Negative" finish that takes no slot** (the chase item).
- **Conditions**: *Fragile* (strong but can break on a big hit — great synergy with contact racing),
  *Passive* (works while equipped), *Cashout* (refunds money if you keep it to the end).

---

## Run structure (the season)

| Layer | Balatro analog | Shitboxer |
|---|---|---|
| **Race** | Blind | One race vs 7 others. Position→money, time-cutoff→survival. |
| **Circuit** | Ante (3 blinds) | ~3 races: heats (survive the cutoff) → **Boss/Feature race** (must place top-N to advance). Each circuit is themed to one of your **8 maps** (doc 06). |
| **Season** | 8 Antes | 8 circuits. Clear circuit 8 → win the run. |
| **Lives** | (fail = run over) | Miss a survival cutoff → −1 life; 0 lives → run ends. |
| **Chassis** | Decks (15) | Starter cars, each a starting rule. Ship 2, unlock more. |
| **License class** | Stakes (8) | Cumulative difficulty (tougher bots, tighter cutoffs, perishable parts). |
| **Special cups** | Challenges (20) | Fixed-constraint events (no-parts race, one-life run, Power-car-only). |

---

## Multiplayer (you chose: quick races **and** shared championship)

Both are on the roadmap; build the simpler one first (doc 05, Phase 6).

- **Quick Races (ship first)** — standalone 8-player lobby, pick a pre-built loadout, contact race,
  done. No roguelike run; simplest netcode surface. This is your "play with friends right now" mode.
- **Shared Championship (bigger mode)** — 8 players run a **whole season together**: everyone races
  the same tracks, each **builds their own car** in the shop between races, position→money as in solo
  but now against humans. The roguelike run is the shared championship. This is the marquee mode and
  where attack parts create the most chaos — but also the most design/netcode work (persistent
  economy across a multi-race online session).

Attack parts, contact, and the inverted economy all get *more* interesting with real people. But
per doc 02, **collisions between real players are the hard netcode problem** — build/prove
single-player first.

---

## What stays "Balatro," what's gone

**Kept:** the roguelike run + permadeath, the **between-race shop** (rerolls, escalating cost,
vouchers/team-upgrades, booster-style part crates), **stacking-parts build depth & combos**, the
**risk/reward economy** (here: inverted catch-up money instead of interest), rarity/finish/condition
modifiers, chassis-as-decks, stakes-as-license-classes, challenges-as-cups.

**Gone:** poker hands & Chips×Mult scoring (→ position-based), the deck & card-drawing (→ parts are
bought equipment), Planet/Tarot/Spectral *as a card economy* (their *effects* survive as part types &
shop crates).

---

## Tuning references (still useful)

Balatro's **numbers are worth stealing as *shape*, not values** — for your shop pricing, reroll
curve, and difficulty ramp:
- **Reroll cost** escalates within a shop ($5, +$1 each, resets next shop) — copy this escalation to
  punish greed.
- **Difficulty** ramps gently then exponentially across 8 antes — mirror it across 8 circuits.
- **Economy** rewards a deliberate strategy (Balatro: banking for interest; yours: the win-vs-farm
  choice) rather than pure "win = more."
- **Shop variety is the anti-staleness lever** — *Golden Lap*'s lesson: invest in part variety &
  interactions or the loop dies. Aim for parts that *combo*, not just +stat parts.

**Racing-roguelike references worth studying** (full notes were in the prior research): *Wreckfest*
(contact-racing feel & damage — your north star for how contact should *feel*), *Carmageddon: Rogue
Shift* (dual currency: in-run cash vs meta unlocks — consider for your meta layer), *SCHROTT*
(sub-5-min runs, RNG-rolled part stats), *Star Wars: Galactic Racer* (arcade racer wrapped in a
roguelite node map), *Cursed to Golf / Golfie* (proof that "skill sport + roguelite shop" sells).
**The niche is still open — no shipped, proven contact-racing roguelike exists.** Opportunity + risk.

*Caveat: the closest racing-roguelike competitors are unreleased/unproven; treat them as concept
validation, not evidence of what ships.*

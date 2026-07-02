# 06 — Content Plan: Maps, Cars, Parts, Assets

The concrete "stuff to build/acquire" doc: the 8 maps, the two cars, the part list scaffold, and
which assets to grab (per your note that free/placeholder assets are fine).

---

## The 8 maps (one signature track per circuit)

Your season is **8 circuits**, so **8 signature tracks** is the natural home for your "8 maps"
requirement — each circuit anchored by a themed track with its own **boss-race** gimmick (the
circuit-final race where you must place **top-N** to advance — doc 03).

A theme set that gives visual variety *and* distinct mechanical hooks. The boss-race twist should make
the race harder to **win/survive** (position + cutoff), not tweak a score:

| # | Circuit theme | Track flavor | Boss-race twist |
|---|---|---|---|
| 1 | **Backstreets** (tutorial) | Tight city night streets | A single strong rubber-banding rival — teaches racing clean |
| 2 | **Coastal Highway** | Fast sweeping cliffside | Crosswinds sap Grip on the exposed sections |
| 3 | **Industrial Docks** | Cargo cranes, wet concrete | Rain — everyone's Grip drops; Grip-car advantage |
| 4 | **Desert Canyon** | Big jumps, rough terrain | Debris/hazards on the racing line; contact-heavy |
| 5 | **Mountain Pass** | Hairpins, elevation, cliffs | Tightened survival cutoff — no room to sandbag |
| 6 | **Neon Speedway** | Synthwave oval + chicanes | Aggressive AI pack that boxes you in — attack parts shine |
| 7 | **Frozen Lake** | Low-grip ice, long slides | Extreme low grip + a rival with a Disruptor aura |
| 8 | **Final Circuit** (showcase) | Everything combined | Multi-phase finisher rival + a tight cutoff |

**Curated procgen vs hand-authored:** for 8 signature tracks, **hand-author the layouts** (better
racing lines) but consider **curated procgen for variant/daily runs** — stitch hand-made track
"chunks" randomly (the Octane Remix approach) so replays vary without feeling like noise. Start
hand-authored; add procgen only if you want infinite/daily modes.

**Track budget tip:** greybox all 8 as drivable layouts *first* (Phase 5), art-pass later. A track is
"done enough" when the racing line feels good, regardless of art.

---

## Cars / chassis (the "decks")

**Start with exactly two cars** (your spec), split along the **Grip vs Power** axis (doc 03). Each is
a distinct starting rule, not just a skin:

| Car | Grip | Power | Starting bias | Who it's for |
|---|---|---|---|---|
| **The Corner-Carver** (default) | High | Low | Nimble, planted, forgiving | New players, twisty circuits |
| **The Brute** | Low | High | Fast, twitchy, wins straights & contact | Aggressive players, speed circuits |

More chassis unlock via meta-progression later (Phase 5), each a different starting rule (the "deck"
analog). Ideas to expand into:

| Later chassis | Starting rule | Playstyle it pushes |
|---|---|---|
| **The Shitbox** | Balanced, +1 starting cash | The underdog / all-rounder |
| **The Tank** | Shrugs off collisions, sluggish | Contact-heavy demolition play |
| **Glass Cannon** | High Grip+Power, parts break more often | Greedy high-risk builds |
| **The Economist** | Higher savings cap, weaker base stats | Shop/economy engine builds |
| **Wildcard** | All starting parts are random rarity | Chaos/variety runs |

---

## Part list scaffold (the "jokers" — equipment, not cards)

You don't need 150 on day one — you need a **tight set that interacts.** Build the three families
(your spec), then fill. Target ~20–30 for the vertical slice, expanding toward ~60–100 for release.

### Family 1 — Stat parts (tune Grip / Power)
- **Power:** *Turbo* (+Power, −reliability), *Big Cam* (+top-end Power), *Close-Ratio Box* (+accel),
  *Weight Reduction* (+power-to-weight & +Grip, −collision toughness).
- **Grip:** *Sticky Compound* (+Grip), *Stiff Springs* (+cornering Grip, worse over bumps), *Big Wing*
  (+downforce Grip at speed, −top speed), *Uprated Brakes* (+corner-entry Grip).
- Every stat part should carry a **trade-off** — a pure "+everything" part is a boring part.

### Family 2 — Economy parts (tune the money loop)
*Sponsor Livery* (+$ per race), *Better Accountant* (higher savings cap), *Scrap Dealer* (cash when
you sell/replace parts), *Underdog Bonus* (extra $ when you finish in the bottom half — leans into the
inverted economy on purpose).

### Family 3 — Attack parts (hurt rivals — on-contact + proximity)
- **On-contact:** *Ram Bars* (hitting a car saps their Grip briefly), *Spike Plates* (contact costs
  them Power / damages them), *Push Bar* (you shove harder, take less from hits).
- **Proximity auras:** *Disruptor Field* (cars right behind you lose grip), *Draft Leech* (steal
  slipstream from cars ahead), *Oil Haze* (nearby rivals' handling degrades).
- These shine most in **multiplayer** vs real rivals; they also work vs bots in solo.

### Cross-cutting: scaling, and the optional slot-order combo
- **Scaling parts:** *Momentum* (+Grip per consecutive clean corner), *Grudge* (+stats each race vs
  the same rival) — parts that grow across a run.
- **Copy parts (optional slot-order depth):** *Twin-Turbo / ECU Clone* copies the part ahead/behind it
  in the layout. Only meaningful if you ship the optional **slot-order** rule (additive-before-×
  multiplier) from doc 03 — spice, not the meal.

### Modifiers (stack on parts)
- **Rarity:** Common → Rare (bigger effects, higher price & shop rarity).
- **Conditions:** *Fragile* (strong but can break on a big hit — great synergy with contact racing),
  *Passive* (works while equipped), *Cashout* (refunds money if kept to the end).
- **Finishes (editions):** +flat / +% / a **Negative** finish that takes no slot (the chase item).

Design each new part to ask: *"what existing part does this combo with?"* If the answer is "none," it's
a boring part. This is the antidote to Golden Lap's "loop goes stale" warning.

---

## Assets to grab (placeholders + physics base)

Verify each license at grab time. **The vehicle-physics choice is covered in depth in doc 02b** — the
short version:

**Sim-cade vehicle physics (pick one as your controller base):**
- **NWH Vehicle Physics 2** (~$60, Asset Store) — **recommended buy.** Sim-cade, modular powertrain
  (→ Power) + Pacejka-ish WheelController (→ Grip), and a clean state struct that makes the netcode
  easier. Mirror/PUN2 hooks.
- **Randomation Vehicle Physics (RVP)** (MIT, github.com/JustInvoke/Randomation-Vehicle-Physics) —
  most complete **free** sim-cade base; old/unmaintained, and the README's MIT has no LICENSE file.
- **TLabVehiclePhysics** (MIT, github.com/TLabAltoh/TLabVehiclePhysics) — modern free base with a
  **Pacejka** tyre; tiny but active.
- Avoid: SergeyMakeev/ArcadeCarPhysics and Unity's WheelCollider — too arcade / not server-friendly.

**Learning reference:**
- **Unity Karting Microgame** — a complete ready racer to dissect (Unity Learn)
- Unity's official **"Network Racing" sample** (DOTS/Netcode for Entities) — the "proper" reference
  for prediction/interpolation/lag-comp, if/when you go deep on netcode

**Networking:**
- **FishNet** (free/open, Asset Store + fish-networking.gitbook.io)
- Ready-made **FishNet client-side-predicted car controller**:
  github.com/Roceh/RVPFishNet-Multiplayer-Car-Controller — a working reference for the hardest part

**Art/audio placeholders:**
- Unity Asset Store "Free" filter for low-poly cars, city/track kits, skyboxes, and stylized
  environments (Synty-style low-poly packs are ideal for a stylized arcade look; grab any free ones,
  and Synty's paid packs are cheap if you later want cohesion)
- Kenney.nl — free CC0 low-poly car/track kits and UI/audio (excellent for greyboxing and jam-quality
  placeholders, no attribution required)

**Rule:** placeholders are for *finding the fun*. Don't spend a day on art before Phase 4. A
low-poly box on wheels that drives great beats a beautiful car that drives badly.

---

## Content sequencing (ties back to doc 05)

1. **Phase 1–3:** 1 greybox track, **both starting cars** (Grip & Power), ~20 interacting parts, 1 circuit.
2. **Phase 4:** swap in placeholder car art + 1 themed track; add crunchy collision juice.
3. **Phase 5:** all 8 tracks (greybox → art), starting 2 cars → +unlockable chassis, part pool → ~60+,
   license classes, special cups.

*Note: asset names/licenses shift over time — confirm each is still free and license-compatible when
you actually download it.*

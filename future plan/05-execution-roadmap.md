# 05 — Execution Roadmap

The build order, in "parts of execution." Each phase has an **exit criterion** — a concrete,
testable thing that must be true before you move on. The golden rule threaded through all of it:

> **Prove the fun before you scale the content.** One track that's fun to drive and one shop loop
> that's fun to optimize beats eight tracks of mush every time.

Phases are sequential in *dependency*, not calendar time — go at hobby pace. Rough solo effort
estimates are given as t-shirt sizes, not promises.

---

## Phase 0 — Foundations (S)

Set up so you never fight tooling later.

- Install **Unity 6.3 LTS** (Windows). Create a **3D (URP)** project — URP is the right renderer for a
  stylized arcade look on modest hardware.
- Commit the empty project to `github.com/1kta4/shitboxer`. Add a Unity **`.gitignore`** (Library/,
  Temp/, etc.) and a **`.gitattributes`** for line endings (see doc 04) *before* the first big commit.
- Wire up your AI assistant: Unity MCP server + Claude Code (doc 04).
- Decide the project's folder convention and a simple assembly-definition layout so compile times
  stay sane.

**Exit criterion:** empty URP project builds and runs; git + AI tooling work end-to-end.

---

## Phase 1 — A car that's fun to drive *and crash* (M) ← the most important phase

Nothing else matters if this isn't fun. No multiplayer, no parts, no UI yet.

- Stand up a **sim-cade vehicle controller** — **DECIDED 2026-07-03: custom-built plain-C# core**
  (raycast suspension, normalized slip curve + friction circle, torque-curve drivetrain), with
  RVP/TLab/PhysX Vehicle SDK used as math references only. (Rejected: buying NWH VP2, porting RVP —
  doc 02b.) **Not WheelCollider.** The sim core (`Shitboxer.Vehicle/Core/`) is stepped explicitly and
  never touches the engine loop or physics scene itself, so the *same* physics
  code can run **headless server-side** — this is load-bearing for multiplayer collisions later (doc 02).
- Get **Grip and Power** feeling distinct and tunable (tyre slip curve + friction circle to start).
  Build **both starting cars** (Grip car vs Power car) so the axis is real from day one.
- One **grey-box test track** (no art). Cornering, braking, and a satisfying **weighty collision**
  (drive into walls/obstacles) all feeling good. Contact feel is part of your core — tune it here.
- Third-person chase camera with good follow/lookahead.

**Exit criterion:** you (and a friend, in person) genuinely enjoy just driving and bumping around the
grey box, with no scoring at all. If not, stay here.

---

## Phase 2 — Race, position & the survival gate (M)

Turn driving into a race with stakes.

- Add **7 bots** with decent racing lines (stat-scaled; upgrade AI later — doc 07 Q6).
- **Position tracking** across a race; finishing order.
- The **survival gate**: finish within X% of the winner's time (or top-N) → pass; miss → you'd lose a
  life (wire the fail state even before the full run exists).
- Minimal HUD: position, lap/timer, and the cutoff you're racing against.

**Exit criterion:** a race is a tense "can I stay inside the cutoff / can I win?" moment against the
bots, and contact/overtaking feels good.

---

## Phase 3 — The roguelike layer: money, shop, parts, run (L) ← the differentiator

This is what makes it *your* game, not just a racer.

- **Inverted economy:** finish position → money (1st = least, last = most), decoupled from time (doc 03).
- **The garage shop** between races: buy **parts** (stat / economy / attack), part crates, permanent
  team upgrades; **reroll for escalating cash** (steal Balatro's $5,+$1 curve shape).
- **Parts system** = jokers-as-equipment: **stat parts** (Grip/Power), **economy parts**, **attack
  parts** (on-contact + proximity auras — doc 03). Optional **slot-order** depth only if the base is
  already fun.
- **Run structure:** ~3 races per circuit — heats (survive the cutoff) → **Boss/Feature race (must
  place top-N to advance)**; lives system; **start with 1 circuit**, not 8.
- Build the **win-vs-farm tension** and immediately playtest for **sandbagging** (risk 4, doc 07) —
  tune the cutoff so "cruise in last" is genuinely risky.

**Exit criterion:** a full 1-circuit run is fun to *optimize* — you catch yourself weighing "push to
win vs hang back to farm money" and planning shop buys between races. This is the "one more run" test.

---

## Phase 4 — Vertical slice polish (M)

Make the 1-circuit loop feel like a real (tiny) game.

- Replace grey box with **placeholder** cars + one themed track (doc 06).
- Run-summary screen, basic meta hook (unlock a second chassis), sound, **crunchy collision/damage
  FX**, screen shake, boost FX — the juice that sells a contact racer.
- First outside playtest (hand the build to a few friends).

**Exit criterion:** a stranger can pick it up, finish a run, and want to start another — without you
explaining the rules.

---

## Phase 5 — Content expansion to the full season (L)

Only now do you scale.

- Build out to **8 circuits / 8 signature maps** (doc 06 has the map plan). Curated procgen or
  hand-authored — your call per doc 06.
- Expand the part pool toward Balatro-scale variety (it doesn't need 150 on day one — but *variety
  and interactions* are what keep the loop alive; heed Golden Lap's "stale loop" warning).
- Add **chassis/starter cars** (deck analogs), **license classes** (stakes/difficulty), and a few
  **special-event cups** (challenges).
- Meta-progression + unlocks.

**Exit criterion:** an 8-circuit season is completable, varied, and the difficulty curve feels fair.

---

## Phase 6 — Multiplayer (L) ← do this LAST, and read doc 02 first

Deliberately last: the single-player game must be fun before networking multiplies your bug surface.
This is the **hardest phase** — you've made collisions mandatory (doc 02). Read doc 02 before writing
a line of netcode.

- Add **FishNet** — the payoff of the Phase-1 "same physics code, runs headless" decision. Go
  **server-authoritative: the server runs all car physics and resolves every collision.** Each client
  predicts **only its own car**; **interpolate all other cars** from server snapshots. **Soften
  collision corrections** (~200 ms blend). (doc 02, option a.)
- Add **Unity Relay + Lobby** for **"play with friends"** (join-by-code, invites) and 8-player
  sessions. Relay's free 50-CCU tier covers launch.
- **Ship Quick Races first** — standalone 8-player lobby, pick a loadout, contact race. Simplest
  netcode surface. Prove contact feels acceptable here before touching the persistent economy.
- **Then the Shared Championship** — 8 players run a season together, each builds their own car in the
  shop between races (doc 03). Bigger design + netcode job; the persistence questions in doc 07 Q5
  land here.
- Test with **Multiplayer Play Mode** (4 instances in-editor), then real builds across machines.
- Only if softened contact feels unacceptable competitively: escalate to **Photon Quantum**
  (deterministic — rebuild vehicle physics in fixed-point). Don't attempt Rocket-League full-scene
  re-sim (doc 02 tree).

**Exit criterion:** 8 players can be invited into a lobby and finish a **contact** race together with
acceptable feel over real internet — chunky contact that hides latency, not frame-perfect ramming.

---

## Phase 7 — Polish, balance, ship (ongoing)

- Balance the economy and difficulty curve — especially the survival cutoff vs sandbagging (steal
  Balatro's *shape*, not its numbers — doc 03/07).
- Voice chat (Vivox, free tier) if wanted. Settings, accessibility, controller support.
- Steam page, wishlists, demo (a demo is huge for roguelikes). Consider Early Access given the open
  niche.

---

## Dependency map (what blocks what)

```
Phase 0 ─► Phase 1 (car feel + contact) ─► Phase 2 (race+cutoff) ─► Phase 3 (roguelike loop) ─► Phase 4 (slice)
                    │                                                                              │
                    └── "same physics code, runs headless server-side" decision feeds ──► Phase 6 (multiplayer)
                                                                                                   ▲
                                                                      Phase 5 (content) ───────────┘
```

Phase 1's "same physics code, runs headless, no WheelCollider" choice is what makes Phase 6's
server-authoritative collisions possible without a rewrite. That's why it's called out twice — it's
the one early decision with the longest reach.

---

## What NOT to do (common ways this dies)

- **Don't build multiplayer early.** It multiplies every bug — and with mandatory collisions it's the
  hardest part; you'll debug netcode instead of finding the fun.
- **Don't build 8 tracks before 1 is fun.** Content is cheap to add and expensive to fix.
- **Don't use WheelCollider** because it's the "official" wheel — it can't run identically
  server-side and it'll cost you the whole multiplayer phase.
- **Don't chase photoreal visuals.** Stylized + cheap placeholders. The roguelike loop is the product.
- **Don't over-scope the part list.** 20 well-interacting parts beat 100 that don't combo.
- **Don't let the inverted economy solve itself.** Playtest for sandbagging from Phase 3 (risk 4).

# 07 — Risks & Open Questions

What can kill this project, ranked, and the decisions still sitting on your plate.
**Updated to your locked decisions (2026-07-02).**

---

## Top risks (ranked by how likely they are to sink you)

### 1. The core drive isn't fun (HIGH likelihood, FATAL impact)
Everything is stacked on a satisfying sim-cade car. If cornering, drifting, and *contact* don't feel
great, no amount of shop cleverness saves it.
- **Mitigation:** Phase 1 exists solely to de-risk this. Don't leave it until you enjoy driving the
  grey box with *no scoring*. Buy/borrow a proven physics base (NWH, or MIT RVP/TLab — doc 02b)
  rather than inventing tyre physics from zero. Budget months for tyre/suspension tuning regardless.

### 2. Networked contact physics is the hardest thing in the project (HIGH likelihood, HIGH impact)
You've made **collisions mandatory** — the right call for fun, but it puts you in the single hardest
quadrant of multiplayer dev. Realistic physics is **non-deterministic** on PhysX, and two
player-owned cars colliding have **no natural single authority**. Even Wreckfest (netcode = their
scaling bottleneck) and Rocket League (rebuilt their whole physics engine) found this hard.
- **Mitigation:** (a) make the "same physics code, run headless server-side, **no WheelCollider**"
  decision on **day one** — it's unfixable late; (b) go **server-authoritative: the server resolves
  every collision**, clients predict only their own car and interpolate others; (c) **soften contact
  corrections** (~200 ms blend) and design contact to feel **chunky and weighty rather than
  frame-perfect** — that hides latency; (d) keep multiplayer as **Phase 6**, after solo is fun; (e)
  ship **quick races** before the harder shared-championship mode. Escape hatch: **Photon Quantum**
  (deterministic) if competitive contact must be exact — but that means rebuilding vehicle physics in
  fixed-point. Full treatment in doc 02.

### 3. The roguelike loop doesn't grip (MEDIUM likelihood, FATAL impact)
The contact-racing-roguelike niche is **open precisely because nobody has proven it works.** The
win-vs-farm economy could feel unintuitive, or the shop could feel like homework between races.
- **Mitigation:** prototype the 1-circuit loop (Phase 3) and playtest *early*. If the "one more run"
  pull isn't there at 1 circuit, 8 won't create it. Lean on the proven references (doc 03): Wreckfest
  for contact feel, Carmageddon: Rogue Shift for the meta economy, Golfie/Cursed-to-Golf as proof the
  "skill sport + roguelite shop" fusion sells.

### 4. Economy sandbagging exploits the inverted money (MEDIUM likelihood, MEDIUM impact) — NEW
Because **money is inverted** (last place earns most) and **survival only needs you inside the time
cutoff**, the degenerate strategy is "cruise in dead-last-but-safe every race to farm cash."
- **Mitigation (design these in from the start):** (a) make the survival cutoff **tight/dynamic** so
  last place is genuinely at risk of losing a life; (b) **boss/feature races require a top-N finish**
  to advance (already in the doc 03 loop) so you can't only farm; (c) consider **non-linear money**
  (diminishing returns at the very bottom) or a small meta reward for winning; (d) tune bot pace so
  "last-but-safe" is a real skill tightrope, not a free lunch. This is a *balancing* problem, not an
  architectural one — but ignore it and the game solves itself boringly.

### 5. Solo scope creep (HIGH likelihood, HIGH impact)
8 maps + 8-player online contact + two MP modes + a deep parts economy + meta-progression is a *lot*
for one person.
- **Mitigation:** the phase gates in doc 05. Ship a fun 1-circuit **single-player** slice first; treat
  everything after Phase 4 as cuttable expansions. Quick races before shared championship. A small
  finished game beats a huge unfinished one.

### 6. Art/scope black hole (MEDIUM likelihood, MEDIUM impact)
Chasing visuals instead of gameplay.
- **Mitigation:** stylized look + free/cheap placeholders (doc 06); no art before Phase 4. The
  "shitbox/junkyard" aesthetic is *forgiving* — rough is on-theme.

### 7. WSL/Windows tooling friction (LOW likelihood, LOW impact)
Slow `/mnt/c`, line-ending churn, MCP boundary issues.
- **Mitigation:** doc 04 — mirrored networking, `.gitattributes` early, or just run Claude Code on
  Windows. Annoying, not fatal.

---

## Open questions still on your plate (design decisions, not research gaps)

Several earlier questions are now **answered** (see the decisions table). What's left:

1. **The survival-cutoff formula.** "Within X% of the winner's time" or "top-N"? What's X, and does
   it tighten per circuit? This single number drives the whole sandbagging balance (risk 4). Decide
   it empirically in Phase 3 playtests.

2. **Boss/feature-race gate.** I've proposed circuit-final races require a **top-N finish** (not just
   survival) to advance — this is what makes the win-vs-farm tension work and stops pure sandbagging.
   Confirm you like that, or propose an alternative "you must actually win sometimes" mechanic.

3. **Run length.** ~3-min races × ~24 races ≈ 90+ min per season — long for a "one more run" game.
   Options: fewer circuits, fewer races per circuit, or a short "sprint season" mode. Decide before
   Phase 5 content.

4. **Dual currency or single?** In-run cash (garage) + a separate meta-unlock currency (Carmageddon
   model) adds depth but also complexity. Single currency is simpler for the slice; add the meta one
   in Phase 5 if wanted.

5. **How persistent is the shared-championship economy?** 8 humans building cars across a multi-race
   online session is the ambitious mode — do players' parts/money persist across a whole online season
   (drop-in/drop-out? disconnects?), or is it a shorter self-contained mini-season? Affects netcode &
   save design a lot. Safe answer: **ship quick races first, design the persistent championship later.**

6. **Bot AI approach for single-player.** Rubber-banding vs fixed-skill vs learned lines? Bots that
   also "build cars" between races, or bots with fixed stats scaled per circuit? Simplest: stat-scaled
   bots with decent racing lines; upgrade later.

7. **Platform target.** PC-first (Steam) is the obvious call for a roguelike. Keep desktop-only for
   now (Unity WebGL is heavy and can't do this netcode well).

8. **Tone.** "Shitboxer" reads scrappy/junkyard/underdog — "build a monster from a rustbucket." That
   fantasy could unify the parts theme, the inverted "losers get funded" economy (you're the
   underdog), and the art. Worth committing to early; it's a strong hook.

---

## Decisions already made (so you don't re-litigate them)

| Decision | Answer | Doc |
|---|---|---|
| Engine | Unity 6.3 LTS | 01, 02 |
| Physics | Sim-cade; NWH VP2 (buy) or RVP/TLab MIT (build); slip-curve+friction-circle tyres | 02b |
| Netcode | FishNet + Relay/Lobby; **server resolves all collisions**, predict own car, soften contact; Quantum as escalation | 02 |
| Vehicle controller | Runs headless server-side, **not** WheelCollider | 02, 02b |
| Collisions | **ON — core to the fun** | 02, 03 |
| Scoring | **Position-based** (not Chips×Mult) | 03 |
| Economy | **Inverted catch-up money** (last earns most), decoupled from time; survival gate = time cutoff | 03 |
| Parts | Jokers-as-**equipment** (no deckbuilder): stat / economy / attack (on-contact + proximity) | 03 |
| Headline stats | **Grip & Power** (sim-cade sub-stats underneath) | 03, 02b |
| Starting cars | **Two: Grip car vs Power car** | 03 |
| Single-player | vs **bots** | 03 |
| Multiplayer | **Quick races + shared championship** (quick first) | 03, 05 |
| Multiplayer timing | Last (Phase 6), after single-player is fun | 05 |
| Art approach | Stylized + free/cheap placeholders | 06 |
| AI tooling | Unity MCP + Claude Code (Fable = just the model) | 04 |
| Custom engine | No | 01 |

---

## The honest bottom line

Two hard problems, cleanly separated. **Technically**, the risk is concentrated in **networked
contact physics** (risk 2) — it's genuinely hard, but the server-authoritative + softened-contact
path is a known, shippable-solo recipe, and 8 players is comfortable. **On design**, the risk is
whether the **contact-racing-roguelike loop is fun** (risk 3) and whether the **inverted economy
avoids sandbagging** (risk 4) — and only a playable Phase-3 prototype answers those. Everything in
this plan is arranged to reach that prototype cheaply: build the smallest single-player thing that
could prove the fun, keep collisions/multiplayer for after it's already good, then scale only what
works.

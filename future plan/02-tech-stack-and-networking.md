# 02 — Tech Stack & Networking

This is the deepest technical doc. It covers the full stack, then the part that will actually
consume your engineering time: **networked vehicle physics for 8 players**.

---

## The recommended stack

| Layer | Choice | Why |
|---|---|---|
| **Engine** | **Unity 6.3 LTS** | Current LTS (supported to Dec 2027), shipped with multiplayer templates + "Building Blocks" for Sessions/Leaderboards. Start here; only use a newer Update release (6.5) for throwaway prototyping. |
| **Language** | **C#** | Approachable, huge community, works with every asset and MCP tool below. |
| **License** | **Personal (free)** | Free under $200k/yr revenue. You'll be on this for years. |
| **Vehicle physics** | **Sim-cade controller you can run headless server-side** (see **doc 02b**) | *Not* WheelCollider. Same code must run client + server. NWH Vehicle Physics 2 (~$60) if buying; RVP/TLab (MIT) if building. |
| **UI / UX** | **UI Toolkit** (UXML + USS + UI Builder) — **DECIDED 2026-07-16** | All player-facing UI from Phase 4 on. Unity's *default* runtime pick is uGUI; we take the documented **alternative** deliberately — see "UI stack" below. Today's IMGUI is a throwaway dev harness, not the plan. |
| **Netcode** | **FishNet** (server-authoritative + free client-side prediction) | The only *free* stack with built-in prediction + reconciliation. No CCU fees. **Server resolves all collisions** — see "core challenge." |
| **"Play with friends" / NAT** | **Unity Relay + Lobby** (UGS) | Free 50-CCU Relay tier; Lobby handles invites/join-by-code. |
| **Voice (optional, later)** | **Unity Vivox** | 5,000 PCU/month free. Skip until the game is fun. |
| **Testing** | **Multiplayer Play Mode (MPPM)** | Test up to 4 instances in one editor, no rebuilds. |
| **Deterministic fallback** | **Photon Quantum 3** (only if needed) | Free to 100 CCU. Migrate here *only* if car-to-car collision desync proves unfixable. |

---

## Unity version & licensing (the facts, mid-2026)

- Unity returned to sequential numbering: **Unity 6 (6000.0) GA Oct 17, 2024.** The model is one
  Unity 6 track with **LTS releases** (yearly, 2 yrs support) and **Update releases** (quarterly).
- **Use Unity 6.3 LTS** (released ~Dec 2025, supported to Dec 2027). It shipped specifically with
  multiplayer-oriented features. Later Update releases (6.4 made ECS core; 6.5 latest) are for
  bleeding-edge prototyping only. (endoflife.date/unity; 80.lv)
- **Licensing:** Runtime Fee fully cancelled Sept 12, 2024. **Personal is free under $200k/yr**
  revenue+funding (hard gate — can't use Personal at all above it). Splash screen optional in Unity
  6. Pro is $2,310/seat/yr (Jan 2026) — irrelevant until you clear $200k. (unity.com; cgchannel.com)

---

## UI stack — **UI Toolkit** (DECIDED 2026-07-16)

**All player-facing UI is built with UI Toolkit**, Unity's retained-mode UI system: **UXML** for
structure, **USS** for styling (CSS-like), the **UI Builder** visual authoring tool, and
`VisualElement` as the rendering primitive. This is a locked decision (doc 07 table).

### Read this part honestly: we are taking Unity's *alternative*, not its default

Unity's own comparison page recommends, for **runtime (player) UI**, **uGUI first — with UI Toolkit
listed as the alternative.** We are deliberately choosing the alternative. That's a real deviation
and it needs a real reason:

- Unity recommends UI Toolkit specifically for projects needing **"a significant amount of user
  interfaces"** and for **"multi-resolution menus and HUD in intensive UI projects."** Shitboxer is
  exactly that shape — the **garage shop is the differentiator** (doc 03), not a pause menu. Part
  offers with rarity/edition/condition tags, stat-delta previews, an equip/slot-order layout,
  standings, run summaries, lap records, stake select, plus a live race HUD. This is a UI-heavy game
  wearing a racing game's clothes.
- **USS restyles the whole shop from one stylesheet.** For a Balatro-like where part cards need
  consistent rarity/edition treatment across dozens of items, cascading styles beat per-prefab
  wiring.
- UI Toolkit also supports **world-space (3D) rendering** and **custom shaders/materials**, which the
  later juice pass (damage numbers, rival nameplates, boost cues) can use without a second system.

### The tradeoffs we're accepting (don't rediscover these later)

| Cost | Detail |
|---|---|
| **Not Unity's default runtime rec** | uGUI is "established and production-proven"; UI Toolkit is **"in active development and releases new features frequently."** We trade stability for fit. |
| **No Animation Clips / Timeline integration** | Explicitly unsupported. UI juice must come from **USS transitions** or code-driven tweens — *not* Timeline. Budget for this in the Phase 4 polish pass; it's the one place uGUI would have been easier. |
| **No easy `MonoBehaviour` referencing** | uGUI's drag-a-reference-into-the-inspector workflow doesn't apply. Expect `UIDocument` + query-by-name/class (`Q<Button>("buy")`) wiring instead. |
| **Learning curve** | UXML/USS is a web-shaped mental model. It pays off at this UI volume; it would not for three buttons. |

### What this means for the IMGUI that exists today

Every current screen — `GarageScreen`, `RaceHud`, `VehicleDebugHud` — is `OnGUI()` IMGUI. Unity's
guidance is unambiguous: **IMGUI is an editor tool with no runtime recommendation at all.** That is
precisely why the game is testable-but-not-shareable right now.

The rule: **IMGUI is a throwaway dev harness. Do not invest in it, and do not port it — replace it.**
It stays only until the Phase 4 UI pass, then it's deleted (`GarageScreen`'s own header already says
*"Dies when a real UI arrives."*). Corollary: don't let "the shop already has a UI" count as Phase 4
progress in any status check — it doesn't.

**Sequencing:** UI Toolkit work belongs in **Phase 4** (vertical-slice polish), not before. The
current IMGUI is sufficient to *take the Phase 1–3 measurements* (is it fun?), and those gates come
first. Building a beautiful shop before knowing the shop is fun is the exact trap doc 05's golden
rule warns about.

*Sources: Unity Manual — "UI Toolkit" (docs.unity3d.com/Manual/UIElements.html) and "Comparison of UI
systems in Unity" (docs.unity3d.com/Manual/UI-system-compare.html), both read 2026-07-16. Unity's
system recommendations have shifted before — re-check the comparison page if this decision is ever
revisited.*

---

## The core challenge: networked vehicle physics

**Player count is a non-issue.** Real-time games run ~40–150 kbps/player; 8 players is nowhere near
any bandwidth ceiling. The binding constraint is **server/host CPU (physics ticks)**, not the
network. So "8 players (or more)" is comfortable — the *physics agreement* between those 8 cars is
the hard part.

### Why it's hard: Unity/PhysX is not deterministic

Floating-point physics results diverge across CPUs — "one rounding difference cascades into a full
desync." This means **deterministic lockstep is impractical with stock Unity vehicle physics**
(you'd need a custom fixed-point physics engine — which is exactly what Photon Quantum provides).
(kinematicsoup.com; gafferongames.com)

### The practical model everyone actually uses

**State-sync + interpolation with client-side prediction & server reconciliation:**

1. You **predict** your own car locally from your input (feels instant).
2. You keep a **ring buffer** of unacknowledged input "moves."
3. When the authoritative server state arrives, you **rewind and replay** those moves to reconcile.

This requires the **exact same physics code on client and server** — which is *why* doc 02b insists
on a **sim-cade controller you can run headless, not WheelCollider**. WheelCollider is widely reported
as jittery under networking (non-owners can't drive the physics), and it can't be run identically
client-side. **This is the single most important architectural constraint in the whole project —
decide it on day one.**

FishNet gives you this prediction/reconciliation loop **for free**; that's why it's the pick. But
note the caveat that follows: FishNet's mechanisms predict **your own** car well — the *car-to-car
collision* between two players is the part nothing hands you for free (see below).

### What shipped racing games actually do (your two design options)

- **Rocket League (the canonical reference):** authoritative server that **predicts *all* cars and
  re-simulates the whole physics scene every frame at 120 Hz**, then replicates physics state to
  correct drift. CPU-heavy but gives real car-to-car collisions. (GDC: "It IS Rocket Science!")
- **Trackmania:** **fully deterministic physics with NO car-to-car collisions** — cars are ghosts.
  Sidesteps the hard problem entirely.

**The genuinely hard part is car-to-car collision under shared authority — and you've made it
mandatory.** You've decided **collisions are core to the fun** (no ghost cars). That's a legitimate,
great design choice, but be clear-eyed: this is the **single hardest quadrant** in multiplayer game
dev. Even the pros struggle — Bugbear publicly named netcode as *Wreckfest*'s scaling bottleneck, and
Psyonix rebuilt *Rocket League*'s entire physics engine to do it. Your realistic choices:

- **(a) Server-authoritative, server resolves ALL collisions.** ← **Recommended.** One authoritative
  sim (dedicated server, or a listen-server host) runs every car's physics and owns every contact.
  Each client **predicts only its own car** from local input; **all other cars are interpolated** from
  server snapshots (rendered ~100–200 ms in the past). You then **deliberately soften the collision
  rubber-banding** — blend corrections over ~200 ms instead of snapping. This is the only model that
  gives consistent contact on non-deterministic PhysX without a bespoke engine. It's shippable solo.
- **(b) Deterministic engine (Photon Quantum).** Fixed-point physics → all 8 cars agree on every
  collision *by construction*, with crisp predicted contact and no rubber-banding. **But** you'd
  **rebuild your vehicle physics (tyres, suspension) from scratch in fixed-point** — Quantum's Karts
  sample is explicitly arcade and doesn't confirm momentum-correct kart-to-kart collisions. A large
  undertaking; the escalation path only if softened contact proves unacceptable.
- **(c) Rocket-League-style full-scene client re-sim.** Best feel, but it leans on a custom,
  near-deterministic physics engine and full-scene re-simulation every frame. **Unrealistic solo** —
  a multi-year effort on a physics stack you fully own. Don't.

**Recommendation:** ship **(a)** — server-authoritative, server owns collisions, predict own car,
interpolate others, soften the contact corrections. Accept that a hard hit at high closing speed is
the genuinely hard thing to hide, and design collisions to *feel* good rather than be frame-perfect
(chunky, weighty *Wreckfest*-style contact hides latency better than precise ramming duels). Escalate
to **(b) Quantum** only if competitive contact fairness becomes non-negotiable. Full detail and
sources are in doc 07's risk entry and the research trail; the physics-layer side is in **doc 02b**.

> **The load-bearing prerequisite (again):** the server must run the **exact same vehicle physics
> code** as the client, headless. That's why doc 02b insists on a controller you can run server-side
> and **not** stock WheelCollider. Decide this on day one — it's unfixable late.

### Tick rates

Target **~30–60 Hz network tick** with a **higher internal physics rate** (e.g. 50–120 Hz fixed).
Bandwidth is not your problem; consistency and CPU are.

---

## Netcode options compared (for the record)

| Solution | Model | Prediction / physics fit | Free tier | Notes |
|---|---|---|---|---|
| **FishNet** ✅ | Server-auth, self-host | **Only *free* stack with built-in client-side prediction + reconciliation** | Fully free/open, no CCU fees | Lag-comp is Pro ($10 one-time / $60 lifetime). Recommended. |
| Photon Quantum 3 | Deterministic rollback ECS, zero-GC physics | **Best determinism**; ships an official Arcade Racing sample | 100 CCU free (1 app) | $95/yr for 200 CCU. The migration target if collisions desync. |
| Photon Fusion 2 | State-transfer (Shared or Host/Server) | Host mode viable; weaker than Quantum for coupled collisions | 100 CCU free | Middle option. |
| Unity NGO | First-party server/client-auth | **No built-in rigidbody prediction** — you build it | Free (uses UGS) | Unity recommends ≤64 players; fine for casual, not this. |
| Unity Distributed Authority | Ownership spread across clients | **Wrong for this** — Unity's own docs say no single physics authority; not for competitive predictive games | Relay free tier | **Avoid for colliding cars.** |
| Mirror | Server-auth, self-host | Solid, but you build prediction yourself | Free (MIT) | Viable but more work than FishNet. |
| Photon PUN2 | Relay-only | Legacy/maintenance | 20 CCU | Don't start new projects on it. |

Sources: fish-networking.gitbook.io; photonengine.com/quantum; docs.unity3d.com (NGO & Distributed
Authority); github.com/MirrorNetworking/Mirror. Cross-check claims against the independent
StinkySteak Unity netcode benchmark.

---

## Unity Gaming Services you'll actually touch

- **Relay** — connects players without a dedicated server (host is a client); handles NAT/firewall
  traversal. **Free: 50 avg monthly CCU; max 150/session.** Fine for launch. This is what powers
  "play with friends."
- **Lobby** — create/join/find sessions, invites, join-by-code. Up to 150/lobby. This is the
  friend-invite UX.
- **Matchmaker** — rule-based matchmaking for public games. Add later; note Multiplay *Hosting*
  support was deprecated Mar 31, 2026, but Matchmaker still works with Relay.
- **Vivox** — voice/text chat, 5,000 PCU/month free. Post-launch nicety.

---

## The physics decision tree (put this on a sticky note)

```
Single-player prototype
        │  sim-cade controller you can run headless server-side (NOT WheelCollider) — doc 02b
        ▼
Is the driving fun on its own?  ── no ──► fix the feel before ANYTHING else
        │ yes
        ▼
Add FishNet + Relay/Lobby.
Server-authoritative: server runs ALL car physics & resolves ALL collisions.
Client predicts OWN car; interpolates others; SOFTEN collision corrections (~200ms blend).
        │
        ▼
Does contact feel good enough (chunky > frame-perfect)?  ── yes ──► ship it
        │ no — competitive contact fairness is non-negotiable
        ▼
Migrate the simulation to Photon Quantum (deterministic, fixed-point)
   └─ cost: rebuild tyre/suspension physics in fixed-point from scratch
(Do NOT attempt Rocket-League-style full-scene client re-sim on PhysX — multi-year solo trap.)
```

---

*Carried-forward uncertainties: exact Relay egress GB and Lobby/Matchmaker free-tier data figures
aren't published by Unity (pricing page 403s automated fetch). Distributed Authority has no official
max-player cap. WheelCollider jitter is strong practitioner consensus rather than one hard citation.
Rocket League's 120 Hz / predict-all-cars details come from GDC-talk summaries corroborated across
sources.*

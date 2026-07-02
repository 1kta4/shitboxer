# Shitboxer — Future Plan

> A 3D, third-person **sim-cade contact racing roguelike** with a Balatro-style **shop + stacking
> parts** meta-system (no deckbuilder). Race for **position**; an **inverted catch-up economy** funds
> your **parts**; survive a season of circuits. Single-player vs bots **and** online multiplayer vs
> real people (up to 8), **collisions on**, 8 maps/tracks. Two headline stats: **Grip** and **Power**.

This folder is a **planning + analysis dossier**, broken into "parts of execution." It is not code.
It exists so that when you sit down to actually build, every major decision is already researched,
justified, and sequenced.

*Compiled 2026-07-02. All external facts were researched fresh and are cited inline in each doc.
Where a source was a vendor's self-description, a single studio blog, or otherwise soft, it is
flagged as such — believe the flagged-uncertain bits less.*

---

## Repo state (starting point)

`shitboxer` is a **completely empty git repository**: zero commits, zero files, empty GitHub remote
(`github.com/1kta4/shitboxer`). This is a genuine blank slate — nothing constrains the architecture,
so every recommendation here is a from-scratch "best foundation" choice, not a retrofit.

---

## The headline verdicts (TL;DR)

| Question | Verdict | Confidence |
|---|---|---|
| **Engine?** | **Unity 6.3 LTS.** You already have it, C# is approachable, best free vehicle-physics assets, best AI/MCP tooling, and — decisively — FishNet gives you *free* networked-physics prediction. | High |
| **Networking for 8 players, collisions ON?** | **FishNet + Unity Relay/Lobby, server-authoritative: the server resolves ALL collisions, clients predict only their own car and interpolate others, and you soften the inevitable contact rubber-banding.** Escalate to **Photon Quantum** (deterministic) only if competitive contact must be frame-perfect. | High |
| **Is 8 players hard?** | Player count, no (trivial). But **realistic physics + mandatory collisions + 8-player online is the single hardest quadrant in multiplayer dev** — even Wreckfest/Rocket League struggled. Shippable solo, but eyes-open. | High |
| **Physics engine?** | **Sim-cade** (Wreckfest/Horizon feel). Buy **NWH Vehicle Physics 2 (~$60)** for the fast path, or build from **RVP / TLabVehiclePhysics (MIT)**. Tyre model: start with a slip-curve + friction circle. Full detail in **doc 02b**. | High |
| **3D + third person feasible solo?** | Yes, for a *stylized* contact racer (not a photoreal sim). Keep visuals stylized to avoid an art black hole. | Medium-High |
| **Custom engine?** | **No.** Multi-year trap for a solo dev on a networked 3D game. | High |
| **How do I connect Claude/AI to Unity?** | Unity MCP server + WSL "mirrored" networking, **or** just run Claude Code natively on Windows. See doc 04. | High |
| **Can I use Fable to build this?** | Yes — but Fable is just a **model**, not a Unity integration. You'd use it as the backend model behind Claude Code + a Unity MCP server. Same setup, different model. | High |
| **Is the contact-racing-roguelike niche taken?** | **No — it's genuinely open.** Every close competitor is unreleased or niche. Opportunity *and* risk. | Medium |

---

## How to read this folder (the parts of execution)

Read them in order the first time; after that they're reference.

| # | File | What it answers |
|---|---|---|
| 00 | `00-README.md` | This file — the map and the verdicts. |
| 01 | `01-engine-decision.md` | Why Unity, with Godot/Unreal/custom compared honestly. |
| 02 | `02-tech-stack-and-networking.md` | The full tech stack: Unity 6.3, netcode, and how to network mandatory car-to-car collisions for 8 players. |
| 02b | `02b-physics-and-vehicles.md` | The sim-cade physics layer: open-source & paid vehicle physics, tyre models, how Grip/Power map to real physics. |
| 03 | `03-game-design.md` | The game itself: the core loop, position scoring, the inverted catch-up economy, parts (jokers-as-equipment), the two cars. |
| 04 | `04-connecting-ai-to-unity.md` | How to connect Claude Code / Fable to your Unity on Windows from WSL. Concrete setup. |
| 05 | `05-execution-roadmap.md` | The build order: milestones from "grey box that drives" → vertical slice → content → multiplayer → polish. |
| 06 | `06-content-plan.md` | The 8 maps, the two cars, the parts list, and which free/paid assets to grab. |
| 07 | `07-risks-and-open-questions.md` | What can kill this project and the decisions still on your plate. |

---

## The one paragraph to remember

Build it in **Unity 6.3 LTS**. Prototype **single-player first (vs bots)** with a **sim-cade vehicle
controller you can run headless server-side** (not WheelCollider) — that same physics code is the
prerequisite for multiplayer collisions later. Nail the **core loop** — race for **position** (which
sets your inverted, catch-up **money**), survive the **time cutoff** or lose a life, spend money in
the **garage shop** on **parts** (boost Grip/Power, tune economy, or attack rivals), then the next
race — before adding a single extra track. Only after the solo loop is fun, add **FishNet +
Relay/Lobby** with a **server-authoritative, server-resolves-all-collisions** model and **softened
contact rubber-banding** (start with **quick races**, then the **shared championship**). Keep the art
**stylized** and lean on **free/cheap placeholders**. Use **Claude Code (optionally with Fable as the
model) + a Unity MCP server** as your build assistant. Ship small, then expand. **Know going in that
networked contact physics is the hard part — design contact to feel *chunky and good*, not
frame-perfect.**

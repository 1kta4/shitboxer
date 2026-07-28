# Shitboxer

A 3D third-person **sim-cade contact racing roguelike**: race 8 cars for position, earn an
**inverted catch-up economy** (losers get funded), spend it in a garage shop on **stacking parts**
(Grip / Power / economy / attack), and survive a season of circuits. Solo vs bots and 8-player
online with **collisions on**.

## Repo layout

| Path | What it is |
|---|---|
| `future plan/` | The planning & analysis dossier — read `future plan/00-README.md` first |
| `game/` | The Unity project (Unity 6000.3 LTS, URP). Open **this folder** in Unity Hub |
| `CLAUDE.md` | Conventions for AI-assisted development sessions |

## Working setup (WSL + Windows)

- The repo lives on `C:\dev\shitboxer` so the **Windows** Unity Editor can open `game/`.
- Claude Code runs in **WSL** and reaches the repo at `/mnt/c/dev/shitboxer`.
- WSL **mirrored networking** is enabled, so the mcp-unity WebSocket bridge (port 8090)
  works across the boundary. See `future plan/04-connecting-ai-to-unity.md`.
- Line endings are normalized to **LF** via `.gitattributes`; keep `core.autocrlf=false`
  on both sides.

## Current phase

**Phases 1–3 are built; none of their exit gates have been walked yet.**
(See `future plan/05-execution-roadmap.md` for the phase definitions and gates.)

| Phase | State |
|---|---|
| 0 — Foundations | Done |
| 1 — Car fun to drive & crash | Built: plain-C# sim core (headless-steppable, no `WheelCollider`), both starter cars, grey-box track, chase cam, contact/damage feel |
| 2 — Race, position, survival gate | Built: 7 bots with racing lines, distance-gated laps, position tracking, 15%-of-winner cutoff wired to lives |
| 3 — Roguelike layer | Built: inverted economy, garage shop w/ escalating rerolls, 47 parts (stat/economy/attack), boss top-N gate, save/resume, meta profile |
| 4 — Vertical-slice polish | Barely started: run-summary + camera shake only. No art, no audio, no VFX, all UI is IMGUI |
| 5 — Content | One grey-box track. The part pool is the only real progress |
| 6 — Multiplayer | Not started — deliberately last. The Phase-1 "headless sim core" prerequisite is intact |

**Next milestone: actually play it.** Every Phase 1–3 exit criterion is a human judgment ("do I
enjoy driving the grey box", "is the race tense", "do I catch myself weighing push-to-win vs
hang-back-to-farm"), and none have been measured — the suite proves the machinery, never the fun.
The season now defaults to **one circuit** (`RunDirector.totalCircuits`) per the roadmap's
"start with 1 circuit, not 8", so a run is short enough to replay the "one more run" test cheaply.

Many feel/balance systems ship **dormant** behind no-op defaults by design (tyre wear, boss
rulesets, difficulty ramp, shop interest); turning them on and tuning them is a deliberate later
pass, not an oversight.

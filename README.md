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

**Phase 0 — Foundations** (see `future plan/05-execution-roadmap.md`). Next milestone:
a car that's fun to drive *and crash* on a grey-box track (Phase 1).

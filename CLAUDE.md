# Shitboxer — AI session conventions

## What this project is
A sim-cade contact racing roguelike in Unity 6000.3 LTS (URP). The full design/tech plan lives in
`future plan/` — treat those docs as the source of truth for architecture decisions. Key locked
decisions (do not re-litigate; see `future plan/07-risks-and-open-questions.md` for the full table):

- **Engine:** Unity 6.3 LTS, URP, C#. Unity project is in `game/`.
- **Vehicle physics:** sim-cade controller that can run **headless server-side**. **Never use
  Unity's WheelCollider** — this is the load-bearing constraint for future multiplayer.
- **Netcode (Phase 6, not yet):** FishNet, server-authoritative, server resolves all collisions.
- **Design:** position-based scoring, inverted catch-up economy, survival time-cutoff,
  parts-as-equipment (stat/economy/attack), two headline stats: **Grip** and **Power**.
- **UI:** **UI Toolkit** (UXML/USS/UI Builder) for all player-facing UI — decided 2026-07-16.
  Deliberately Unity's documented *alternative* to its uGUI default (this is a UI-heavy game; the
  shop is the differentiator). Build it in **Phase 4**, not before. The current `OnGUI()` IMGUI
  (`GarageScreen`, `RaceHud`, `VehicleDebugHud`) is a **throwaway dev harness** — Unity says IMGUI is
  editor-only with no runtime recommendation. **Replace it, never port it**, and never count it as
  Phase 4 progress. Don't add uGUI/Canvas/TextMeshPro.
- **Build order:** single-player first. Multiplayer is deliberately LAST.

## Environment
- Unity Editor runs on **Windows**; this repo is at `C:\dev\shitboxer` (= `/mnt/c/dev/shitboxer`
  from WSL). Claude Code runs in WSL. Mirrored networking is enabled in `.wslconfig`.
- Heavy git/file operations are slow on `/mnt/c` (9P) — prefer targeted commands over
  repo-wide scans.
- Unity MCP bridge: CoderGamester/mcp-unity — Unity-side package in `game/Packages/manifest.json`,
  Node server cloned at `~/tools/mcp-unity` (WSL). WebSocket on port 8090.

## Conventions
- Line endings: LF everywhere (`.gitattributes` enforces; `core.autocrlf=false`).
- Game code goes in `game/Assets/_Project/` with assembly definitions per subsystem
  (`Shitboxer.Vehicle`, `Shitboxer.Race`, `Shitboxer.Meta`, ...) to keep compile times sane.
- Vehicle physics code must stay engine-loop-independent: simulation stepped from
  `FixedUpdate` via a plain-C# core that a headless server could also step.
- Placeholder art only until Phase 4 — do not spend time on visuals.

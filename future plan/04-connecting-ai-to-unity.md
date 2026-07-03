# 04 — Connecting Claude / Fable to Your Unity

Your setup: **Claude Code runs in WSL (Linux); Unity Editor runs on Windows.** You want an AI
assistant to read/modify the project and drive the Editor. This doc gives you the concrete path.

*This space moves fast and several products are in beta — verify version-specific details against the
linked docs before relying on them. Compiled 2026-07-02.*

---

## First, the honest answer about Fable

**Fable has no special Unity integration.** It's an Anthropic **model**, not a tool. To "use Fable
to build this," you use it as the **backend model behind Claude Code** (or another MCP client),
pointed at a Unity MCP server. Same setup as with any other model — you just select Fable. There is
no Fable Unity plugin, no Fable-specific bridge, no Fable Editor integration.

(Beware a name collision: web results pairing "Fable" with "Unity" refer to **Databricks Unity
Catalog** — a data-governance product — *not* the Unity game engine. Ignore those.)

So the rest of this doc is "how to connect an AI to Unity," and Fable is simply one model choice
within it.

---

## The three-part architecture (all Unity MCP servers look like this)

```
[ AI client ]         [ MCP server ]              [ Unity Editor plugin ]
Claude Code    <-->   Python/Node relay    <-->   C# bridge inside Unity
(the model)           (translates MCP)            (creates GameObjects, edits
                                                   scripts, runs menu items,
                                                   reads console logs, etc.)
```

The client never talks to Unity directly. The relay in the middle is what crosses machine
boundaries — which matters a lot for your WSL/Windows split.

---

## Your real problem: the WSL ↔ Windows boundary

WSL2 is a VM behind NAT. By default, `127.0.0.1` in WSL means the **Linux VM's** loopback, while
`127.0.0.1` on Windows means **Windows'** loopback — so a server on Windows `localhost:PORT` is
**not** reachable from WSL by default. There are two clean ways to solve this, and one server type
that can't be solved from WSL at all.

### The single highest-leverage fix: WSL mirrored networking

Create/edit `%UserProfile%\.wslconfig` on Windows:
```ini
[wsl2]
networkingMode = mirrored
```
Then in an admin PowerShell: `wsl --shutdown`, and restart WSL. This **collapses the two loopbacks
into one**, so `127.0.0.1:PORT` from WSL reaches the Windows-side Unity server directly. (Make sure
the file is literally `.wslconfig`, not `.wslconfig.txt`.)

> **Caveat that decides your server choice:** these networking tricks only bridge **TCP / HTTP /
> WebSocket**. They do **not** help a server whose Windows transport is a **named pipe**. Unity's
> *official* MCP uses named-pipe IPC on Windows → not reachable from WSL. Pick a WebSocket/HTTP
> server instead, or run the client on Windows (below).

---

## Choose your server by transport, not popularity

| Server | Transport to Unity | WSL-friendly? | Notes |
|---|---|---|---|
| **CoderGamester/mcp-unity** ✅ | **WebSocket, port 8090** | **Yes — best for WSL** | Has a built-in `UNITY_HOST` env var to point the relay at the Windows host IP. Tools include `execute_menu_item`, `update_gameobject`, `get_console_logs`. Node.js. |
| **CoplayDev/unity-mcp** | Python relay (assumes client & Unity see same host) | Works either way | Most popular (~11k+ stars, MIT), richest — ~47 tools (scenes, C# edit+validate, tests, build). Has a "Configure All Detected Clients" button that writes your MCP config for you. Its own Claude Code guide: docs.coplay.dev/coplay-mcp/claude-code-guide |
| **Unity official MCP** (`com.unity.ai.assistant`) | **Named-pipe IPC on Windows** | **No (from WSL)** | Requires Unity 6.0+ **and a paid Unity subscription** (Pro/Enterprise/Industry). Only sensible if you run Claude Code on Windows. Open beta, behavior may shift. |
| IvanMurzak/Unity-MCP, AnkleBreaker/unity-mcp-server | varies | varies | Other free community options; the latter advertises 268 tools. |

**Recommendation for you:** **CoderGamester/mcp-unity** (WebSocket crosses the WSL boundary cleanly,
and its `UNITY_HOST` knob is exactly built for your case), **or** CoplayDev if you want the richer
toolset and are willing to run Claude Code on Windows. Avoid the official one from WSL (named pipe +
paid subscription).

---

## The two concrete setups

### Setup A — Stay in WSL (recommended): CoderGamester + mirrored networking

1. Enable **mirrored networking** (above).
2. In Unity (Windows): Package Manager → Add package from Git URL:
   `https://github.com/CoderGamester/mcp-unity.git` — this starts the WebSocket server on `:8090`
   inside Unity.
3. In WSL, build and register the MCP server. **Correction (2026-07-03): there is no
   `@codergamester/mcp-unity` npm package** — the Node server ships inside the repo's `Server~/`
   folder and must be built from source:
   ```bash
   git clone https://github.com/CoderGamester/mcp-unity.git ~/tools/mcp-unity
   cd ~/tools/mcp-unity/Server~ && npm install && npm run build
   claude mcp add --transport stdio unity -- node ~/tools/mcp-unity/Server~/build/index.js
   ```
   If mirrored networking isn't giving you a shared localhost, pass the Windows host IP explicitly
   (find it via the `nameserver` line in `/etc/resolv.conf`):
   ```bash
   claude mcp add --transport stdio unity -e UNITY_HOST=<windows-ip> -- node ~/tools/mcp-unity/Server~/build/index.js
   ```
   Or, instead of `claude mcp add`, commit a project-scoped `.mcp.json` at the repo root:
   ```json
   {
     "mcpServers": {
       "unity": {
         "command": "node",
         "args": ["/home/ta41k/tools/mcp-unity/Server~/build/index.js"]
       }
     }
   }
   ```
4. Keep the Unity **project on the Windows drive** (the Windows Editor must open it). WSL reaches it
   at `/mnt/c/...` — see the filesystem caveats below.

### Setup B — Simplest overall: run Claude Code natively on Windows

If you'd rather not fight the boundary at all — especially if you want Unity's *official* MCP —
install and run **Claude Code on Windows** so there's no WSL boundary. Then use the Unity package's
"Configure All Detected Clients" button (Coplay) or `claude mcp add` directly. The trade-off: native
Windows stdio MCP has its own friction (`npx` is a batch script, so wrap launches as
`cmd /c npx -y <pkg>`; set `"windowsHide": true` to stop console flashing). Community guides still
lean toward WSL2 + mirrored networking overall, but native Windows is the path of least resistance
for the official server.

**Rule of thumb:** WebSocket/HTTP server → **WSL + mirrored networking** (Setup A). Official
named-pipe server → **Claude Code on Windows** (Setup B).

---

## How Claude Code connects to any MCP server (mechanics)

```bash
# local server Claude spawns as a child process (stdio):
claude mcp add --transport stdio <name> -- <command> [args...]
#   everything after `--` is the server's launch command
#   pass env vars with -e, e.g.  -e UNITY_HOST=<ip>

# networked server (streamable HTTP; supports OAuth):
claude mcp add --transport http <name> https://host/mcp
```
`--transport sse` is deprecated → use `http`. Config is scoped (local/project/user). Many Unity
packages write this config for you, so you often won't run `claude mcp add` by hand. Docs:
code.claude.com/docs/en/mcp

---

## Filesystem: keep the project on `C:\`, mind these two gotchas

The Unity Editor is a Windows app, so the project **must** sit where Windows can open it (i.e. on
`C:\`). WSL reaches it at `/mnt/c/...`, with two costs:

1. **`/mnt/c` is slow from WSL** — it goes over the 9P protocol, ~10–100× slower than native ext4. A
   Unity project has tens of thousands of files, so `git status`, greps, and file-watching from WSL
   will feel sluggish. Do heavy file operations Windows-side when you can. You can't have both
   native-ext4 speed *and* let Windows Unity open the project — accept the compromise.
2. **Line endings (CRLF vs LF)** — editing the same repo from both Windows and Linux can make **every
   file look modified** to git, and in Unity, **flipping LF↔CRLF forces full shader recompiles.**
   Mitigate with a committed **`.gitattributes`** and consistent `core.autocrlf` on both sides. Set
   this up before the repo grows.

Other WSL gotchas: on corporate VPNs, NAT mode often breaks (mirrored fixes it); clock drift after
sleep can break OAuth (`sudo hwclock -s`); confirm you're on **WSL2 not WSL1** (`wsl -l -v`).

---

## Recommended path (summary)

1. **Fable = just the model.** Use it as the backend model in Claude Code; the Unity plumbing is
   identical regardless of model.
2. **Pick the server by transport:** CoderGamester (WebSocket) for WSL, or run Claude Code on
   Windows if you want the official/Coplay setup.
3. **Enable WSL mirrored networking** — the single biggest fix if you stay in WSL.
4. **Register with `claude mcp add`** (or let the Unity package write the config).
5. **Keep the project on `C:\`**, commit a `.gitattributes`, and fix `core.autocrlf` early.

### Key sources
- CoderGamester/mcp-unity: github.com/CoderGamester/mcp-unity
- CoplayDev/unity-mcp: github.com/CoplayDev/unity-mcp · docs.coplay.dev/coplay-mcp/claude-code-guide
- Unity official MCP: docs.unity3d.com/Packages/com.unity.ai.assistant (open beta, paid tiers)
- Claude Code MCP docs: code.claude.com/docs/en/mcp
- WSL mirrored networking & `/mnt/c` perf: microsoft/WSL issue #873; startdebugging.net & mcp.directory WSL fix guides
- Shader recompile on line-ending flip: issuetracker.unity3d.com

*Uncertainties: MCP server star counts are point-in-time; Unity's official MCP is in open beta with
licensing/behavior that may shift; Fable capability claims in press are not verified Unity-engine docs.*

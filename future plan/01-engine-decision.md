# 01 — Engine Decision

**Verdict: build in Unity 6.3 LTS.** Below is the honest comparison that gets there, so you can
disagree with the reasoning rather than just the conclusion.

---

## The decision-relevant fact

For a physics-driven multiplayer racer, the hard part is **not** "8 players" — that's trivial scale
for every modern engine. The hard part is **client-side prediction & reconciliation of vehicle
physics** (making your car feel instant locally while the server stays authoritative). That single
requirement reorders the entire engine ranking, because it's the biggest differentiator between the
options. Keep it in mind as you read.

---

## Ranking for *this* project (solo dev, 3D arcade racing roguelike, 8-player online)

| Rank | Engine | Ships realistically (solo)? | Quality ceiling | One-line verdict |
|---|---|---|---|---|
| **1** | **Unity 6.3 LTS** | ★★★★★ | ★★★★ | Best fit. You have it; the netcode-for-physics problem is solved *for free* by FishNet. |
| 2 | **Godot 4.4+** | ★★★★ | ★★★ | Great free/MIT backup, but you must hand-write networked-physics prediction yourself. |
| 3 | **Unreal Engine 5** | ★★ | ★★★★★ | Highest ceiling & best built-in replication, but heaviest learning curve; Chaos Vehicles still "experimental." Overkill for a stylized arcade racer. |
| 4 | **Custom engine** | ☆ | — | Don't. A multi-year trap. |

---

## 1. Unity — recommended

**Why it wins for you specifically:**

- **You already have it installed on Windows.** Zero switching cost. C# is widely considered
  approachable for solo/hobbyist devs, and Unity's free learning path is deep: Unity Learn's Junior
  Programmer / *Create with Code*, plus the **Karting Microgame** — a ready-made racer you can pull
  apart to learn from. (learn.unity.com)
- **Netcode-for-physics is the killer advantage.** For an 8-player physics racer, the real challenge
  is prediction/reconciliation, and **FishNet is the only *free* solution with built-in client-side
  prediction** (including rigidbody prediction + desync smoothing). There are even ready-made
  FishNet client-side-predicted car controllers to start from. Unity's own first-party netcode (NGO)
  ships *no* full built-in rigidbody prediction — you'd build it yourself. This is the tiebreaker.
  (fish-networking.gitbook.io; github.com/Roceh/RVPFishNet-Multiplayer-Car-Controller)
- **Largest asset ecosystem**, including free racing templates and free arcade vehicle physics:
  SergeyMakeev's **Arcade Car Physics** (MIT), Edy's Vehicle Physics (free tier), Vehicle Physics
  Pro Community edition (free). See doc 06 for the shopping list.
- **Best AI/MCP tooling story** of any engine (mature community servers + an official one) — directly
  relevant since you want an AI assistant to help build it. See doc 04.
- **Free tier fits you:** Unity Personal is free up to **$200k/yr** revenue, splash screen optional in
  Unity 6. You owe nothing until you're genuinely successful. (unity.com/products/pricing-updates)

**Weaknesses to know going in:**

- **WheelCollider (the legacy PhysX wheel) is quirky** — slip-based friction that ignores Physic
  Materials, poorly documented, jittery under networking. **Do not use it.** Use a raycast/scripted
  arcade controller or an asset kit instead. (docs.unity3d.com/Manual/class-WheelCollider.html) This
  is a recommendation, not a blocker — see doc 02.
- **Reputation residue** from the 2023 Runtime Fee. Note the fee was **fully cancelled on Sept 12,
  2024** before ever taking effect and applies to no version including Unity 6. Unity instead raised
  Pro pricing (~$2,310/seat/yr as of Jan 2026) — but that only matters above the $200k Personal
  ceiling, i.e. not to you for a long time. (unity.com/blog/unity-is-canceling-the-runtime-fee)

---

## 2. Godot 4.4+ — strong free runner-up

**Strengths:** Fully **free/open-source (MIT)** — no royalties, no revenue share, no thresholds, you
own the game outright. Godot 4.4 (stable ~March 2025) added **3D physics interpolation** (smooths
fast vehicles), integrated **Jolt physics** (opt-in), and has viable arcade vehicle options
(`VehicleBody3D`, or the popular "sphere car" pattern). Built-in high-level multiplayer
(MultiplayerSpawner/Synchronizer, `@rpc`, ENet) handles 8 players comfortably. Community MCP
(`Coding-Solo/godot-mcp`, ~4.4k stars) is genuinely usable and captures runtime debug output.

**The key risk — why it ranks below Unity:** Godot has **no built-in client-side prediction/rollback
and no free FishNet-equivalent** to hand it to you. For a competitive-feeling networked physics
racer you'd write prediction/reconciliation from scratch. That's the single most expensive piece of
solo-dev time in this whole project, and Godot makes you pay it. (github.com/godotengine/godot-proposals/issues/7280)

**Choose Godot instead of Unity only if** avoiding all licensing/royalty entanglement is a hard
requirement for you and you accept writing your own physics netcode.

---

## 3. Unreal Engine 5 — highest ceiling, likely overkill

**Strengths:** **Best-in-class built-in networking** — authoritative client-server, native dedicated
servers, and Movement Components that handle vehicle motion + prediction + replication out of the
box (the one area UE beats Unity without a plugin). Top visual ceiling (Nanite/Lumen — which you'd
actually *disable* for a stylized racer). Hobbyist-friendly licensing: free until **$1M lifetime
gross per product**, then 5% royalty (3.5% via Epic Games Store). (dev.epicgames.com; cgchannel.com)

**Weaknesses for a solo dev:** **Chaos Vehicles is still officially tagged "Experimental"** and
under-documented — doable for arcade but frustrating to tune without a physics background. Steepest
learning curve, heaviest hardware requirement. Blueprints ease prototyping but are slower on hot
paths. For a *stylized arcade* racer where shipping matters more than fidelity, UE5 is the
highest-ceiling but lowest-chance-of-shipping option. (portal.productboard.com/epicgames — Chaos roadmap)

**Choose Unreal only if** top-tier visuals become the actual point of the project and you'll invest
in the curve.

---

## 4. Custom engine — almost certainly a mistake

Every reputable source points one way for a solo dev on a 3D *networked* game:

- You end up **making an engine, not a game** — years on renderers/tools with no gameplay.
  (zylinski.se/posts/solodevs-and-the-trap-of-the-game-engine/)
- A full-featured 3D engine is roughly a **5-year full-time effort**. (lisyarus.github.io)
- **Networking must be architected from day one** and networked physics requires inventing a whole
  authoritative + predicting simulation. The legitimate reasons to go custom (no engine can do it /
  a resourced multi-project studio / avoiding royalties) **none apply** — Godot already gives you the
  zero-royalty escape hatch. (gamedeveloper.com)

Not worth a second thought for this project.

---

## Final call

**Unity 6.3 LTS.** It uniquely combines an approachable language, the deepest free arcade-vehicle
assets, the best free learning path, the best AI/MCP tooling, and — decisively — **FishNet's free
built-in client-side prediction for networked vehicle physics**, which is the single hardest
technical problem in this genre. That netcode advantage is the tiebreaker; everything else is
roughly a wash between Unity and Godot.

*Reliability notes: FishNet's "only free built-in prediction" is a vendor self-description
(directionally reliable; cross-check the independent StinkySteak netcode benchmark). The "Chaos is
production-ready" claims come from a promotional studio blog; the verifiable anchor is Epic still
labeling Chaos Vehicles "Experimental." Several unity.com / unrealengine.com pages 403'd automated
fetching and were corroborated via docs subdomains and secondary reporting.*

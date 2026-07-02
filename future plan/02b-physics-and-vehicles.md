# 02b — Physics & Vehicles (Sim-Cade)

You asked for **decent, realistic-ish physics** with tyres, suspension, weight, torque, hp — a
**sim-cade** target (think *Wreckfest* / *Forza Horizon*, not full-sim, not pure-arcade). This doc
covers the physics layer. **The networking of that physics with mandatory collisions is the hard
part and lives in doc 02** — read both together.

**Honest headline:** the *physics* is the easy half. Any of the options below get you a car that
feels good. The **collision netcode** (doc 02) is where the real compromises are.

---

## The Grip/Power stats map onto real physics like this

| Your stat | Physics systems behind it | Parts tune… |
|---|---|---|
| **POWER** | Engine **torque curve**, HP, **gearing**, forced induction (turbo), **power-to-weight** | torque/redline, gear ratios, turbo, weight reduction |
| **GRIP** | **Tyre model** (slip→force curve), **suspension** (springs/dampers, load transfer), **downforce/aero**, weight distribution, brakes | tyre compound, spring/damper rates, ride height, wing/downforce |

So the whole upgrade economy is literally "tune the tyre + suspension + aero (Grip) vs the drivetrain
(Power)." That's a clean, teachable model.

---

## The tyre model decision (start simple, upgrade if needed)

Three realistic-enough options, easiest → hardest:

1. **Normalized slip curve + friction circle** ← **recommended start.** One authored "grip rises to a
   peak then slides" curve per axis, clamped by a combined-slip "friction circle." This is what most
   sim-cade games ship and it's *fun*. Marco Monster's classic *Car Physics for Games* even notes the
   simplification "made the car easier to drive." Cheap, tunable, good feel.
2. **Brush model** ← upgrade if GRIP needs more depth. Physically-derived (contact patch as
   deflecting bristles), fewer "magic" coefficients, tunable & consistent. Basis of *Assetto Corsa*;
   the modern sim-cade sweet spot.
3. **Pacejka "Magic Formula"** ← the industry-standard empirical curve. Accurate and fast to compute,
   but its coefficients must be **fitted to measured tyre data** (often proprietary) and naive
   implementations misbehave at low speed.

**Universal truth from every practitioner:** there is **no plug-and-play tyre model — budget months
of tuning** whichever you pick. Ship option 1, graduate only if the driving demands it.

---

## Open-source Unity vehicle physics (verified July 2026)

| Project | License | Stars | Class | Models |
|---|---|---|---|---|
| **JustInvoke/Randomation-Vehicle-Physics (RVP)** | MIT* | ~933 | **Sim-cade** | Engine, transmission, differentials, suspension, tyre friction curves. Unity 5.6-era, unmaintained since 2022. |
| **TLabAltoh/TLabVehiclePhysics** | MIT | ~14 | Sim-leaning | **Pacejka (LUT)** tyre, torque curve, clutch, manual gearbox, downforce. Active (2025). Tiny but modern. |
| SergeyMakeev/ArcadeCarPhysics | MIT | ~453 | **Arcade** | Speed curve (no drivetrain), normalized grip, downforce. Author says it's "not a final product." Feel reference only. |
| benmcinnes/ArcadeVehiclePhysics | MIT | ~199 | Pure arcade | Raycast wheels, 0–1 grip. No drivetrain. |
| hayden-donnelly/vehicle-physics | MIT | ~28 | Arcade raycast | Raycast suspension, spring/damper per corner. |

`*` RVP states MIT in its README but has **no LICENSE file** (GitHub reports `license: null`) —
minor legal-hygiene flag; consider asking the author or treating cautiously before shipping.

**Reality check on the free repos:** none model true **load-based weight transfer feeding back into
per-tyre grip** — you'll likely build that yourself. And **none are deterministic** (all drive a
PhysX float Rigidbody) — which matters enormously for netcode (doc 02).

**Best free starting points:** **RVP** (most complete sim-cade, but old) or **TLabVehiclePhysics**
(modern + Pacejka, but tiny), then add weight transfer yourself.

---

## Non-Unity references worth reading (don't copy GPL code)

For the *math*, these are the best readable references (mostly GPL — study, re-implement, don't lift):
- **VDrift** (GPL-3) — best readable **Pacejka + drivetrain** reference; has a dedicated Pacejka
  editor repo.
- **Speed Dreams** (GPL-2+, active 2025) and its ancestor **TORCS** — Pacejka-style sim physics.
- **Rigs of Rods** (GPL-3) — the canonical open-source **soft-body/deformation** racer (BeamNG's
  ancestor); architecture reference if you ever want Wreckfest-style deformation.
- **NVIDIA PhysX 5 Vehicle SDK** (**BSD-3, permissively reusable**) — strongest *legally reusable*
  reference; componentized so you can swap in a Pacejka tyre. (BeamNG itself is **closed source**.)
- Portable Pacejka to port to C#: `JyNing04/Pacejka-tire-model` (Python), `svenlr/car-physics-pacejka`
  (C++), MATLAB **MFeval** (gold-standard, reads industry `.tir` files). **Check each repo's LICENSE —
  several small ones have none (= all rights reserved).**

---

## Paid Unity assets that bundle physics + networking (the shortcut)

If you'd rather buy a strong base than build one, these are the sim-cade + multiplayer-ready options
(prices seen 2026-07-02, several on sale — treat as temporary):

| Asset | ~Price | Physics realism | Multiplayer support | Notes |
|---|---|---|---|---|
| **NWH Vehicle Physics 2** | ~$60 | **Sim-cade** — own **WheelController 3D** (modified Pacejka), full modular powertrain, diffs, turbo | **Mirror + PUN2**; exposes a **single state struct** → easiest to net-sync | **Best fit.** Torque curve → your POWER, WheelController → your GRIP. |
| Realistic Car Controller Pro | ~$50 | Sim-cade (WheelCollider + mesh-deform damage) | **Mirror + PUN2**, Photon demos | Damage model suits contact racing. |
| Edy's Vehicle Physics | ~$60 | Arcade/gameplay | None built-in | Edy points serious users to VPP instead. |
| Vehicle Physics Pro (Community) | Free (Pro €590+) | High-fidelity **sim** | None bundled | Free CE = compiled DLL, 1 car/scene, 4 wheels, desktop only. Source only at Enterprise (€5,900+). |
| HEAVYART MP Racing Template (Fusion 2) | ~$50 | Arcade | **Photon Fusion 2 — networked player collisions actually implemented** | Closest thing to a working networked-contact starting point. |
| RVPFishNet (community) | Free | Sim-cade (RVP) | **FishNet CSP port of RVP** | The closest open demo of *predicted multiplayer cars*. |

Every mainstream asset here runs on **PhysX → none deterministic**; multiplayer is via state
replication / client-side prediction, not lockstep. **NWH's single-state-struct design makes the
netcode meaningfully easier**, which is why it's the top pick if you buy.

---

## Recommendation for the physics layer

- **If buying (fastest good result):** **NWH Vehicle Physics 2 (~$60)** — its powertrain maps to
  POWER, its Pacejka-ish WheelController to GRIP, and its clean state struct is the friendliest to
  the server-authoritative netcode you'll need (doc 02). This is the pragmatic solo-dev choice.
- **If free/own-the-code:** start from **RVP (MIT)** or **TLabVehiclePhysics (MIT, Pacejka)**, add
  proper weight transfer yourself, ship a **normalized slip curve + friction circle** tyre model, and
  upgrade to **brush** only if GRIP needs more depth.
- **Either way:** design the controller so the **exact same physics code can run headless on the
  server** — this is the non-negotiable prerequisite for the multiplayer collision model in doc 02.
  **Do not use Unity's stock WheelCollider** (quirky, jittery under networking).

---

## How this connects to the hard problem (pointer to doc 02)

All of the above runs on **non-deterministic PhysX**. That's fine for single-player and fine for
*your own* predicted car online — but it's exactly why **networked car-to-car collisions** force a
server-authoritative model with softened rubber-banding (or a full fixed-point rebuild in Photon
Quantum). The physics choice here and the netcode choice in doc 02 are one decision: **pick a
controller whose physics you can run identically on the server, and let the server resolve every
collision.** See doc 02, "The core challenge," for the full treatment.

*Uncertainties flagged: RVP's missing LICENSE file; several small Pacejka repos lack licenses; asset
prices/sales are point-in-time; Quantum's Vehicle addon internals are unverified from docs (inspect
source before committing). Verify licenses at grab time.*

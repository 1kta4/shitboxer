# Shitboxer — Garage Design Kit

Everything used in the three showcase directions: type, the element inventory, the three palettes, and copy-paste CSS recipes for every effect. Use this to design independently. Notes at the end flag what survives the port to Unity USS.

---

## 1. Type

The showcase uses system fonts as stand-ins. Roles matter more than the exact face.

| Role | Stack used | Used for |
|---|---|---|
| **Display / heavy** | `"Arial Black","Helvetica Neue",sans-serif` | `GARAGE` title, `NEXT RACE` CTA |
| **Body / sans** | `ui-sans-serif,"Segoe UI",system-ui,Roboto,Arial` | Part names, descriptions |
| **Mono / utility** | `ui-monospace,"Cascadia Mono","SF Mono",Consolas` | Labels, readouts, stats, section headers, deltas, price |

**Type scale (px / letter-spacing):** title 30 · next-race 16 · offer name 14 · readout 13 · stat value 13 · body desc 12 · stat name 12 · button 12 /.08em · section header 11 /.22em uppercase · delta 11.

**Real faces worth trying (to replace the system stand-ins):**
- Chrome / bubble display: **Eurostile / Microgramma**, **Handel Gothic**, **Bank Gothic**, or a liquid-chrome type for the Aqua/Steel titles.
- Utility mono: **any clean mono** for the readouts (keeps the "service menu / disc spec" feel).
- Pixel accent (optional, already in-repo): **Silkscreen**, **Press Start 2P** — dropped from the showcase because your references lean chrome+sans, but available if you want a retro accent.

---

## 2. Element inventory (the garage screen)

Same content in all three directions — this is what you're skinning. Real in-game data shown.

- **Titlebar** — `GARAGE` (display) + `CIRCUIT 1/1` (mono, right-aligned)
- **Status readouts** — `label · dotted leader · value`:
  - `$ …… 15`
  - `LIVES …… 3`
  - `NEXT: race 1/5`
- **Stat bars** (0–100, label + track + fill + value):
  - `GRIP  62`
  - `POWER 58`
- **Section header** — uppercase, wide tracking: `SHOP`, `OWNED PARTS · 1/3 slots`
- **Shop offer row** — name + `[STAT]`/`[ECON]` tag + `$price` + description + stat delta + `BUY`:
  - `Junkyard Turbo [STAT] $5` — "Boost from a scrapyard. More Power, maybe less reliability." — `POWER 58 → 67 (+9)`
  - `Broadcast Deal [ECON] $3` — "The network pays per second you're on screen scrapping in the pack."
  - `Race Slicks [STAT] $5` — "Grip you can feel. Wears fast." — `GRIP 62 → 71 (+9)`
- **Action buttons** — `BUY PARTS CRATE ($6)` · `REROLL ($2)`
- **Owned part row** — name + tag + `— EQUIPPED` + `EQUIP`/`UNEQUIP`:
  - `Sticky Compound [STAT] — EQUIPPED`
  - `Pizza Sponsor [ECON]`
- **Footer CTA** — `NEXT RACE ›`

**Shared class skeleton:** `.gx-titlebar` `.gx-title` `.gx-circuit` · `.gx-readout` (`> i` = dotted leader) · `.gx-statbar` `.gx-track` `.gx-fill` · `.gx-section` · `.gx-offer` `.gx-info` `.gx-delta` · `.gx-btn` · `.gx-owned` · `.gx-nextrace`.

---

## 3. Palettes

### Direction 1 — Steel Silver (chrome / liquid metal)

| Token | Value |
|---|---|
| Ground (radial) | `#ffffff → #eef2f6 → #cdd5df → #aab4c1` |
| Panel | `#f7fafc → #e4e9ef → #cfd7e0` |
| Titlebar | `#e9eef3 → #c3ccd7` |
| Chrome title gradient | `#ffffff → #dfe7ef → #8f9db0 → #f3f7fb → #b9c6d6` (stroke `rgba(70,85,105,.5)`) |
| Ink | `#2f3a48` / `#26313f` / `#28313d` |
| Sub-ink | circuit `#5a687a` · stat name `#3a4756` · section `#6a7789` |
| Accent cobalt (fill) | `#5b93ff → #1a44d8` |
| CTA cobalt | `#4f8bff → #1a3fd0 → #122b9e`, text `#123a9e` |
| Gain (green) | `#0e7a2e` |
| Fault (red) | `#c81028` |
| Button metal | `#ffffff → #dbe2ea → #c2cbd6` |

### Direction 2 — Aqua Frutiger (glass / bloom / sparkle)

| Token | Value |
|---|---|
| Field | radial `#7fe3ff` + radial `#0a2ad6` + linear `#1b6ff0 → #0a34cf → #071d86` |
| Glass panel | `rgba(230,246,255,.26) → rgba(120,180,255,.14)`, `backdrop-blur(14px) saturate(1.3)`, border `rgba(220,244,255,.7)` |
| Aqua chrome title | `#ffffff → #d4f2ff → #5bb8f5 → #eaffff → #9ad9ff` + cyan glow |
| Bone text | `#eaf6ff` / `#dff3ff` / `#c6f2ff` |
| Fill | `#c7f6ff → #37c0ff → #0a86ff`, glow `rgba(90,220,255,.9)` |
| Track | `rgba(6,26,90,.5)` |
| Section | `#bfe8ff` |
| Button (water) | `#ffffff → #cdeeff → #7fd0ff`, text `#052a55` |
| CTA | `#5fdcff → #1a86ff → #0a46e0` |
| Gain (acid) | `#a9ff8f` |
| Sparkle | `#ffffff` glow `#bfefff` · Rings `rgba(190,235,255,.5)` |

### Direction 3 — Jewel Case (PS2 disc / breakcore print)

| Token | Value |
|---|---|
| Ground | `#0d1219 → #090c12` |
| Halftone dots | `radial rgba(150,200,255,.12) 1px`, tile `4px` |
| Spine | `#10161f → #0a0e14`, border `#223047`, disc ring `#7fd4ff` |
| Panel | `#0f151d → #0a0e14`, border `#1d2836` |
| Titlebar | `#131b25 → #0c1119` |
| Title ink | `#eef4fb` (black offset shadow) |
| Sub-ink | circuit `#6f8298` · readout `#b7c6d6` · next `#7fd4ff` · stat name `#8ea0b4` |
| Fill | `#1f7fd0 → #7fd4ff` + glow |
| Section accent | `#67c6ff` |
| Offer bg | `#0d131b`, border `#1c2836` · desc `#7c8ba0` |
| Gain / counter-accent (acid) | `#b6ff3a` |
| Button | `#a9e2ff → #4aa8ea`, text `#04263f` |
| CTA (acid) | `#b6ff3a → #7ad11e` |
| Rating badge red | `#c81028` · Barcode bars `#c4d2e0` |

---

## 4. Effect recipes (CSS)

### Chrome gradient text
```css
.chrome{
  color:transparent;
  background:linear-gradient(180deg,#fff 4%,#dfe7ef 30%,#8f9db0 52%,#f3f7fb 66%,#b9c6d6 92%);
  -webkit-background-clip:text; background-clip:text;
  -webkit-text-stroke:1px rgba(70,85,105,.5);
  filter:drop-shadow(0 1px 0 rgba(255,255,255,.7)) drop-shadow(0 2px 2px rgba(50,70,110,.35));
}
/* Aqua variant: blue gradient stops + filter:drop-shadow(0 0 10px rgba(120,220,255,.8)) */
```

### Glossy metal bevel (panel / button)
```css
.metal{
  background:linear-gradient(180deg,#fff,#dbe2ea 52%,#c2cbd6);
  border:1px solid #fff;
  box-shadow:
    inset 0 1px 0 #fff,                     /* lit top edge */
    inset 0 -2px 3px rgba(120,135,155,.5),  /* shadowed bottom */
    0 2px 4px rgba(50,70,110,.25);          /* drop */
}
.metal:active{transform:translateY(1px)}
```

### Glass panel (Frutiger)
```css
.glass{
  background:linear-gradient(180deg,rgba(230,246,255,.26),rgba(120,180,255,.14));
  backdrop-filter:blur(14px) saturate(1.3);
  border:1px solid rgba(220,244,255,.7);
  box-shadow:inset 0 1px 0 rgba(255,255,255,.9), 0 20px 50px -18px rgba(4,20,90,.7);
  border-radius:16px;
}
```

### Aqua field (background)
```css
.field{
  background:
    radial-gradient(90% 70% at 26% 14%, #7fe3ff 0%, transparent 46%),
    radial-gradient(120% 120% at 78% 90%, #0a2ad6 0%, transparent 55%),
    linear-gradient(158deg,#1b6ff0 0%,#0a34cf 52%,#071d86 100%);
}
```

### Soft bloom overlay (put over any panel)
```css
.bloom::after{
  content:"";position:absolute;inset:0;pointer-events:none;
  background:radial-gradient(60% 30% at 50% -6%, rgba(255,255,255,.9), transparent 70%);
  mix-blend-mode:screen;
}
```

### Sparkle ✦ and concentric ring
```css
.spark{position:absolute;color:#fff;filter:drop-shadow(0 0 6px #bfefff)}   /* glyph: ✦ */
.ring{position:absolute;border-radius:50%;border:2px solid rgba(190,235,255,.5)}
```

### Halftone dots + scanline grain
```css
.halftone::before{
  content:"";position:absolute;inset:0;pointer-events:none;opacity:.5;
  background-image:radial-gradient(rgba(150,200,255,.12) 1px, transparent 1.4px);
  background-size:4px 4px;
}
```

### Dotted-leader readout (`LABEL …… VALUE`)
```css
.readout{display:flex;align-items:center;gap:8px;font-family:monospace}
.readout i{flex:1;height:0;border-bottom:1px dotted currentColor;opacity:.4;margin-top:6px}
.readout b{font-variant-numeric:tabular-nums}
```

### Stat bar (recessed track + glowing fill)
```css
.track{height:14px;border-radius:2px;overflow:hidden;background:rgba(6,26,90,.5);
  box-shadow:inset 0 1px 3px rgba(0,10,50,.6)}
.fill{height:100%;background:linear-gradient(180deg,#c7f6ff,#37c0ff 55%,#0a86ff);
  box-shadow:0 0 12px rgba(90,220,255,.9)}   /* width:% = value */
```

### Vertical spine label (jewel case)
```css
.spine{position:absolute;left:0;top:0;bottom:0;width:52px}
.spine .vert{writing-mode:vertical-rl;transform:rotate(180deg);letter-spacing:.34em;text-transform:uppercase}
```

### Barcode (JS generator)
```js
let seed=7;
for(let i=0;i<48;i++){
  seed=(seed*1103515245+12345)&0x7fffffff;
  const h=22+(seed%12), w=(seed>>3)%3?2:3;
  bar.insertAdjacentHTML('beforeend',`<i style="height:${h}px;width:${w}px;background:#c4d2e0"></i>`);
}
/* container: display:flex; align-items:flex-end; gap:1.5px */
```

### Rating badge
```css
.badge b{background:#c81028;color:#fff;padding:2px 6px;border-radius:2px;font-weight:800}
/* e.g.  [18] SLES-00000 · CONTACT RACING */
```

---

## 5. Porting notes (CSS → Unity USS)

If a look here is going into the game, know which effects run live in USS and which must be **baked into sprite textures**:

| Effect | In USS? |
|---|---|
| Solid colors, borders, linear/radial gradients | ✅ live |
| Bevels via border + inset color | ✅ (fake inset with border colors — no `inset` box-shadow) |
| Transitions, transform, opacity, hover/active | ✅ live |
| `box-shadow` (glow, drop, inset) | ❌ bake into a 9-slice sprite |
| `backdrop-filter` blur (glass) | ❌ bake, or use a pre-blurred texture |
| `background-clip:text` chrome type | ❌ render chrome titles as image/SDF sprites |
| Bloom / `mix-blend-mode` | ❌ bake into the background texture |
| Sparkles, rings, halftone, barcode | ✅ as sprites/tiled textures (cheap) |

Rule of thumb: **structure + layout + flat color** stay in USS; **gloss, glow, blur, chrome** become authored art. Design the flat structure in USS and drop the shiny bits on top as sprites.

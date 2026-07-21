# Vanguard Protocol — UI/UX Design Spec
Source board: `FPS UI Concept Board.dc.html`. All 12 screen mockups are exported to `mockups/` at 1920×1080 (2x of the board's 960×540 working canvas).

## Design system basis
Built on the **Modernist** design system (Archivo type, zero-radius panels, strong 2px rules, single-accent-red architecture). Gameplay screens invert Modernist's light ground to its dark ramp steps; danger red is kept exclusive to Modernist's system accent.

## Color palette
| Role | Value | Use |
|---|---|---|
| HUD Base | `oklch(19% 0.015 250)` (near-black, cool) | Fullscreen dark ground |
| Panel | `color-mix(in srgb, oklch(19% .015 250) 76%, transparent)` over `--color-neutral-800 #444141` + blur | HUD glass surfaces |
| Ink | `#f3f2f2` | Primary HUD text (Modernist's light ground, reused as ink) |
| Ink Dim | `#f3f2f2` @ 56% opacity | Secondary/tertiary text |
| Action Amber | `oklch(78% 0.14 75)` (~#e8a63d) | Player state, primary CTAs, selection, ready-up target UI |
| Ready Green | `oklch(70% 0.15 152)` (~#4cae6b) | Ready state / pickup confirmation only |
| Danger Red | `#ec3013` (Modernist `--color-accent`) | Damage / low health only |

**Rule of one:** each color owns exactly one meaning everywhere. Amber never signals danger; red never signals an available action.

## Typography
- Archivo ExtraBold (800): timer, kills/deaths, ammo, K/D, button labels, panel titles — always `font-variant-numeric: tabular-nums` on any digit.
- Archivo Regular (400): body copy, lobby/settings rows.
- Eyebrows/labels: Archivo 800, uppercase, `letter-spacing: .08–.14em`.
- Scale: callouts 28px, panel titles 18px, body 14px, labels/eyebrows 11px, micro (ping/build) 10px. Nothing below 10px in the HUD.

## Spacing & layout
| Token | Value | Use |
|---|---|---|
| Safe margin | 32px | Any HUD element to screen edge |
| Cluster gap | 16px | Between unrelated HUD clusters |
| Inner gap | 8px | Icon ↔ label ↔ value within one cluster |
| Panel padding | 12–16px | Lobby cards, settings rows, scoreboard rows |
| Corner brackets | 14–16px arms, 2px stroke | Frames screen edge + key panels — tactical stand-in for Modernist's rules |

All values shown at 960×540 working scale — double for 1920×1080 shipping resolution.

## Button & control states
- **Primary action** (amber fill, `#1a1206` text): normal → hover (lighter amber + glow) → pressed (darker amber, shifts down 1px) → disabled (40% opacity, host-only lock icon + label, e.g. "Start Match").
- **Secondary/outline**: 1px `--hud-border`, ink text, no fill.
- **Danger**: 1px red border, red text (Leave Match only).
- **Ready chip**: not-ready = outline/dim; ready = green fill @ 20% + green border + green text.

## HUD feedback states
| State | Treatment |
|---|---|
| Body hit | White flash crosshair, ~120ms |
| Critical/headshot hit | Amber crosshair + outward ring pulse |
| Kill confirm | "ELIMINATED" + killer name, fades up over 0.9s |
| Pickup prompt | World-space bracketed key + label, fades in within 2.5m |
| Pickup confirmation | Top-of-HUD green toast, check icon, 1.4s |
| Low health | Red vignette pulse + red health numeral/icon, "Critical — find cover" label |
| Death | Desaturated screen, "Eliminated by [player]" + weapon/distance |
| Respawn | Circular countdown ring (not a bare number) |

## Screens (see `mockups/`)
01 Main Menu · 02 Lobby Create/Join · 03 Lobby Room · 04 Deploy Transition · 05 In-Game HUD · 06 Crosshair & Hit Feedback · 07 Pickup Prompt & Confirmation · 08 Low Health/Damage · 09 Death & Respawn · 10 Scoreboard · 11 Match Results · 12 Pause/Settings

## Key copy
- Wordmark: "VANGUARD PROTOCOL" (placeholder — swap only this)
- Tagline: "4-PLAYER TACTICAL DEATHMATCH"
- Main menu buttons: Play / Settings / Exit
- Lobby tabs: Create Public / Create Private / Join Code / Quick Join
- Lobby actions: Ready Up, Start Match · X/4 Ready (host), locked "Start Match" + "Waiting for host" (client)
- Deploy screen: "Deploying to Arena", tip line, squad row
- Match state label: "Deathmatch · First to 30"
- Pickup: "Pick up [item]" → "[ITEM] ACQUIRED"
- Low health: "Critical — find cover"
- Death: "Eliminated by [player]" / weapon + distance / "Respawning"
- Results: "[Player] Wins" + reason ("Score Limit Reached" / "Time Expired") + Return to Menu / Rematch (host)
- Settings: Gameplay/Audio/Video tabs, Resume, Leave Match

## Implementation notes (Unity)
See the board's "Implementation Notes" section: flat-sprite panels (no 4-slice), Archivo as TMP Font Assets with tabular digits, a reusable corner-bracket prefab, a UITheme ScriptableObject for the 5 color roles, and four reusable components — PlayerSlot, StatRow, Toast, Bracket-Frame — that cover most screens above. Keep HUD motion under 300ms except deploy/respawn progress; drive hit/kill/low-health feedback via Animator triggers, not per-frame tweens.

## Assets in this folder
- `mockups/` — 12 screen PNGs (1920×1080)
- `icons/` — 14 line-icon SVGs (crosshair, hit marker, shield/health, timer, signal/ping, settings, lock, crown/host, copy, check/confirm, search/join, kill-feed swap, chevron-back, waiting/add)
- `textures/` — tileable schematic grid, scanline, and noise overlay PNGs used across HUD backgrounds
- `fonts/README.md` — Archivo sourcing + Unity TMP setup

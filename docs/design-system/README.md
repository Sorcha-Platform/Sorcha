# Claude Code Prompt — Align the Sorcha codebase to the refined visual system

> **Paste everything below into Claude Code, run from the root of the Sorcha repo**
> (`github.com/Sorcha-Platform/Sorcha`). The `reference/` and `production/` folders
> referenced here ship alongside this README in the handoff bundle — keep them next to
> the repo (or drop them in a scratch dir) so Claude Code can read them.

---

## Role & goal

You are a senior front-end/.NET engineer. Bring the **entire Sorcha UI surface** — the static
marketing landing page **and** the Blazor WebAssembly + MudBlazor app — into alignment with a
single, accessible visual system: the **Sorcha Refined Visual System**. This is a *refinement,
not a rebrand*. Keep the indigo/violet palette and the "S" icon mark. Elevate execution,
reconcile the competing palettes into one source of truth, and meet **WCAG 2.2 AA on every
text/background pairing**.

The system is fully specified below and demonstrated in `reference/Sorcha Visual System.html`
(open it in a browser — it is the living spec, with a live Light/Dark toggle and a live
contrast-ratio table for every token). Treat the **values in this README and in
`production/`** as authoritative.

**These bundled files are design references, not production code to paste.** Recreate the
system using the codebase's existing patterns (CSS custom properties for the landing; a
`MudTheme` for the app). Do not introduce a CSS framework or a build step.

---

## What is non-negotiable (keep)

- **Colour family:** indigo / violet on a near-black indigo ground. This is the brand.
- **The "S" logo** (`Sorcha.UI.Web/wwwroot/sorcha-icon.svg` + PNG renders): a bold white "S"
  on the dark ground, faint grid, drifting glow-squares, soft violet bloom. **Do not redesign
  it.** Its motifs (56px grid, glow-square "data particles", soft violet bloom) extend into the
  wider system — section dividers, the 4-step "how it works" icons, the three-door cards.
- **The marketing page section order:** hero → audience router → problem → how-it-works →
  security model → organisations → citizens → standards → quantum-safe → sectors → developers →
  maturity → CTA. You are *theming* it, not re-architecting it.

---

## The reconciliation (changes to make — and why)

The audit found **three competing palettes** with no shared source. Collapse them to one.

| # | Change | Why |
|---|---|---|
| 1 | **Retire the lighter `#667eea` / `#764ba2` drift** everywhere (landing `:root`, MudTheme). Anchor to the icon's true values: ground `#090A14→#0F1020`, primary `#6366F1`, violet step `#818CF8`, deep indigo `#4F46E5`. | The icon is the source of truth; the landing had drifted lighter. |
| 2 | **`#6366F1` is a *glow* colour; `#4F46E5` is the *action* colour.** Use `#4F46E5` for filled buttons, links, the AppBar — anywhere white text or small text sits on it. Reserve `#6366F1` for large display, icons, borders, bloom. | White on `#6366F1` = **4.47:1 — fails AA** for normal text. White on `#4F46E5` = **6.3:1 — passes**. |
| 3 | **Remove the green `#48bb78` brand accent.** Green survives **only** as the `success` semantic token (`#34D399` dark / `#047857` light). | The green read as a second brand colour and fought the indigo. |
| 4 | **Verification / "verified-proof / primary-CTA" accent = a luminous violet *step*** (`#A5B4FC` on dark, `#4338CA` as text on light), **not** a new hue. A "hallmark gold" alternate exists in the reference page as a toggle but is **not** the default — ship violet. | Restraint signals seriousness (standards-body, not consumer SaaS). Keep indigo/violet dominant. |
| 5 | **Hover state = add the violet bloom glow, do not shift fill lightness.** | Keeps every fill on the safe side of the contrast line *and* extends the icon's bloom motif. |
| 6 | **Drop Bootstrap CSS** (~233 KB) from `Sorcha.UI.Web/wwwroot/app/index.html`. Replace the handful of legacy `.form-control` usages with MudBlazor form fields (`MudTextField`, etc.). | Bootstrap is loaded only for vestigial form classes and adds a third palette (`#1b6ec2 / #006bb7`). |
| 7 | **Delete the orphan `layout.css`** (defines an `app-container/topbar/sidenav` shell no Razor file uses) and the Bootstrap-override `app.css` blues. | Dead code, conflicting palette. |
| 8 | **Extract the inline `MudTheme` out of `MainLayout.razor` into `Theme.cs`** (or `SorchaTheme.cs`) and add the typography + shape scales it currently lacks. | The theme is inline with no type/shape/elevation scale; per-tenant `BrandingConfigViewModel` should map into it later. |

---

## Files to change (repo map)

All paths relative to repo root.

**Marketing (static):**
- `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/landing.css` — replace the `:root` token block with
  the variables in `production/sorcha-tokens.css` (light theme block). Update component rules to
  consume them (`var(--primary-strong)` for buttons/links, `var(--verify)` for verified states,
  retire `--accent` green).
- The landing HTML (served at `/`) — load Inter + IBM Plex Mono (see Type), add the
  `data-theme`/grid/particle hooks as needed for the hero `<canvas>` (logic in
  `reference/system.js`).

**App (Blazor + MudBlazor):**
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` — remove the inline
  `private readonly MudTheme _theme = new() {...}` and reference the extracted theme.
- **New:** `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Theme/SorchaTheme.cs` — paste
  `production/SorchaTheme.cs` (full PaletteLight + PaletteDark + Typography + LayoutProperties).
- `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html` — drop Bootstrap CSS; keep
  `MudBlazor.min.css`; swap the Google Fonts line from Roboto to **Inter 400/500/600/700** +
  **IBM Plex Mono 400/500/600**; update the inline `font-family` to Inter.
- `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app.css` — delete the Bootstrap-blue overrides
  (`#006bb7`, `#1b6ec2`); if link/validation colours are still needed, point them at the tokens.
- `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/layout.css` — **delete** (orphan).
- Dark-mode wiring already exists via `IThemeService` / `MudThemeProvider IsDarkMode` — keep it;
  just feed it the new palette.

---

## 1 · Colour tokens

Every ratio below is **computed live** in `reference/Sorcha Visual System.html` — open it and read
the "Colour tokens" tables to confirm. AA = 4.5:1 for normal text, 3:1 for large (≥24px or bold
≥18.66px). "lg-only" means usable as large text / non-text UI but **not** small body.

### Dark theme (the brand ground — this is the primary face)

| Token | Hex | Role | Contrast (on `--bg` / on `--surface-alt`) |
|---|---|---|---|
| `--bg` | `#0A0B14` | Page ground | — |
| `--surface` | `#14162B` | Raised card / panel | — |
| `--surface-alt` | `#0E1020` | Section banding | — |
| `--surface-sunk` | `#070810` | Code wells / inputs | — |
| `--border` | `#282C46` | Hairline | — |
| `--border-strong` | `#3A3F63` | Emphasised line / outline button | — |
| `--text` | `#EDEEF8` | Body & headings — **use anywhere** | 16.98 / 16.33 ✅ |
| `--text-muted` | `#9A9FBC` | Secondary copy, captions | 7.53 / 7.24 ✅ |
| `--text-faint` | `#6B7095` | Decorative / large numerals **only — not body** | 4.09 / 3.93 ⚠ lg-only |
| `--link` / `--verify` | `#A5B4FC` | Links, verified text, inline proof | 9.84 / 9.46 ✅ |
| `--primary` | `#6366F1` | Large display, icons, borders — **NOT small body** | 4.39 / 4.22 ⚠ lg-only |
| `--primary-strong` | `#818CF8` | Large accents, hover glow, icons | 6.57 / 6.32 ✅ |
| `--btn-primary-bg` | `#4F46E5` | Primary button fill (white label) | white on it = **6.3** ✅ |
| `--success` | `#34D399` | Success text & icons | ~10.2 ✅ |
| `--warning` | `#FBBF24` | Warning text & icons | ~11.8 ✅ |
| `--error` | `#F87171` | Error text & icons | ~7.1 ✅ |

Accent / CTA fill (violet): `--accent-fill #A5B4FC`, label `--accent-text #0A0B14` → **~10:1**.
Hover `--accent-hover #B7C2FD`. (Gold alternate, not default: `#F4C04E` fill, `#1A1405` label.)

### Light theme

| Token | Hex | Role | Contrast (on `--bg` / on `--surface-alt`) |
|---|---|---|---|
| `--bg` | `#FCFCFE` | Page ground | — |
| `--surface` | `#FFFFFF` | Raised card / panel | — |
| `--surface-alt` | `#F3F4FB` | Section banding (faint indigo, replaces `#f7fafc`) | — |
| `--surface-sunk` | `#F0F1F8` | Code wells / inputs | — |
| `--border` | `#E4E5F1` | Hairline | — |
| `--border-strong` | `#CDD0E4` | Emphasised line / outline button | — |
| `--text` | `#14162B` | Body & headings — **use anywhere** | 17.38 / 16.23 ✅ |
| `--text-muted` | `#585E7C` | Secondary copy, captions | 6.20 / 5.79 ✅ |
| `--text-faint` | `#8A8FA8` | Decorative **only — not body** | 3.12 / **2.91 ❌** (do not use on banding) |
| `--link` / `--verify` | `#4338CA` | Links, verified text, inline proof | 7.71 / 7.20 ✅ |
| `--primary` | `#6366F1` | Large display, icons, borders — **NOT small body** | 4.36 / 4.07 ⚠ lg-only |
| `--primary-strong` | `#4F46E5` | Links, large accents, icons | 6.14 / 5.73 ✅ |
| `--btn-primary-bg` | `#4F46E5` | Primary button fill (white label) | white on it = **6.3** ✅ |
| `--success` | `#047857` | Success text & icons | ~5.4 ✅ |
| `--warning` | `#B45309` | Warning text & icons | ~5.0 ✅ |
| `--error` | `#C81E1E` | Error text & icons (nudged from `#DC2626` to clear AA on banding) | ~5.7 / ~5.2 ✅ |

**Usage rules to enforce in review:**
- Never put `--primary` (`#6366F1`) as small body text or as a white-text fill — it fails AA. Use
  `--primary-strong` / `--btn-primary-bg` (`#4F46E5`) for those.
- Never put `--text-faint` on `--surface-alt` in light theme (2.91:1). It is decorative-only.
- "Verified / proof / verification" text uses `--verify` (resolves `#A5B4FC` dark, `#4338CA` light).

Full, copy-paste CSS variable blocks (both themes + accent family + grid/bloom/shadow tokens) are
in **`production/sorcha-tokens.css`**.

---

## 2 · Type system

- **UI / body:** **Inter** (keep). Weights 400 / 500 / 600 / 700.
- **Verifiable / technical details** (standards names, hashes, DIDs, code chips, eyebrows):
  **IBM Plex Mono** 400 / 500 / 600. (JetBrains Mono is an acceptable drop-in alternate.)
- Load lean — one `@import` / `<link>`:
  `https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap`
- `--font-sans: "Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;`
- `--font-mono: "IBM Plex Mono", "JetBrains Mono", ui-monospace, Menlo, monospace;`

### Scale

| Role | Desktop px | Mobile px | Weight | Line-height | Tracking | Family |
|---|---|---|---|---|---|---|
| H1 / hero | 56 | 38 | 700 | 1.05 | −0.022em | Inter |
| H2 / section | 40 | 30 | 700 | 1.12 | −0.018em | Inter |
| H3 | 28 | 24 | 600 | 1.2 | −0.012em | Inter |
| H4 | 22 | 20 | 600 | 1.3 | — | Inter |
| H5 | 18 | 17 | 600 | 1.4 | — | Inter |
| Body-L | 19 | 17 | 400 | 1.6 | — | Inter |
| Body | 16 | 16 | 400 | 1.6 | — | Inter |
| Caption | 13 | 13 | 500 | 1.45 | 0.005em | Inter |
| Eyebrow | 12 | 12 | 600 | 1.4 | 0.14em, UPPERCASE | **IBM Plex Mono** |
| Mono | 14 | 14 | 400 | 1.5 | — | **IBM Plex Mono** |

Mirror this into MudBlazor `Typography` (Default = Inter; H1–H6 sizes/weights as above;
`Subtitle`/`Caption` from Body-L/Caption). See `production/SorchaTheme.cs`.

---

## 3 · Component kit

All components respond to theme + accent. **Focus rings are always visible** — never remove
outlines. Hover extends the bloom, not lightness. Radii: `--radius-sm 8px`, `--radius 12px`,
`--radius-lg 16px`, pill `999px`.

- **Focus ring (global):** `outline: 2px solid var(--focus-ring); outline-offset: 2px;`
  (`--focus-ring` = `#A5B4FC` dark / `#4F46E5` light). Applies to all interactive elements via
  `:focus-visible`.
- **Buttons** (`padding 11px 20px`, 15px/600, radius 8px):
  - *Primary* — fill `--btn-primary-bg` (`#4F46E5`), label white; hover adds `--bloom-shadow`.
  - *Accent / CTA* — fill `--accent-fill` (`#A5B4FC`), label `--accent-text` (`#0A0B14`); hover bloom.
  - *Outline* — transparent, 1px `--border-strong`; hover → border `--primary`, inset ring, text `--primary-strong`.
  - *Text* — transparent, label `--link`; hover underline.
  - *Disabled* — `opacity .45`, no pointer events, no shadow.
  - **Important:** drive `background-color` (not the `background` shorthand) and only transition
    `background-color` — and **suppress transitions for one frame on theme/accent swap**
    (`html.swapping *{transition:none!important}`). A Chromium quirk freezes a `var()`-driven
    shorthand mid-transition otherwise. See `reference/system.js` (`swap()`).
- **Cards** — `--surface`, 1px `--border`, radius 12px, `--shadow-sm`; hover variant → border
  `--primary`, `--bloom-shadow`, `translateY(-3px)`.
- **Audience router (3 doors)** — card with a faint corner grid (28px, masked to the top-right
  corner), a line icon in `--primary-strong`, a mono uppercase role label in `--verify`, H4 title,
  muted body, and a "go" affordance whose arrow nudges on hover.
- **Standards / credential chips** (mono, 12.5px, pill) — **honesty is the point:**
  - *Implemented* (`.chip--impl`): solid border tinted with `--primary`, **filled** dot in
    `--verify` with a soft ring, a `LIVE` tag in `--verify`.
  - *Roadmap* (`.chip--roadmap`): **dashed** border, **hollow** dot, muted text, a `ROADMAP` tag.
- **Sticky nav** — `position: sticky`, translucent `--bg` with `backdrop-filter: blur(14px)`,
  1px `--border` bottom; brand = CSS "S" mark + "Sorcha"; muted links → `--text` on hover.
- **Hero block** — see §5.

Exact CSS for every component is in `reference/page.css` (consumes `reference/tokens.css`).

---

## 4 · Imagery & motion direction

One visual language, carried everywhere. **Line-based, geometric, monochrome with a single
accent.** No photography, no faces, no 3D coins, no handshakes, no glowing-blue-blockchain
clichés, no emoji.

- **The grid:** a faint **56px** grid, `rgba(99,102,241,0.10)` dark / `0.08` light. Use it as
  texture under the hero, in door-card corners, and as a fading baseline in **section dividers**.
- **Data particles:** small glow-squares (`#6366F1` / `#818CF8`) snapped to the 56px grid, soft
  blur. Used in the hero `<canvas>` and as the single bloom dot at the centre of a divider.
- **Soft violet bloom:** radial `rgba(99,102,241,0.12–0.18)`, behind the hero "S" and on hover.
- **4-step "how it works" icons** (Issue → Sign → Distribute → Verify): 48px line icons, 1.6
  stroke, `--primary-strong`, one accent touch (e.g. a `--verify` check). See the SVGs in the
  reference HTML.
- **Three-door cards:** the corner-grid + line-icon + accent-role-label treatment above.

**Motion (calm by mandate):**
- Hero particle `<canvas>` drifts at ~0.12px/frame, twinkling 18–48% opacity, soft 8px blur.
- **Pause off-screen** with an `IntersectionObserver` (don't burn frames when scrolled away).
- **`prefers-reduced-motion: reduce` → render a single static frame**, full contrast, content
  always visible. Never gate content visibility on animation.
- No infinite attention-grabbing loops on content. Transitions ~0.18–0.25s, ease
  `cubic-bezier(0.2,0.6,0.2,1)`.
- Full implementation: `reference/system.js` (`initParticles`).

---

## 5 · Example hero (dark theme)

Compose: the **"S" identity mark** (white S, deep-indigo offset shadow `#4F46E5`, violet bloom,
on the `#090A14→#0F1020` ground with the masked 56px grid + drifting particles), a mono eyebrow,
the headline **"Stop asking who to trust. Start verifying."** (with "who" and "verifying" in the
lilac accent), a Body-L subhead, a primary + outline CTA pair, and a **three-card audience
router** beneath (Organisations · Verifiers & citizens · Developers). Markup + CSS:
`reference/Sorcha Visual System.html` (`.hero`) + `reference/page.css`.

---

## Accessibility (hard constraints)

- **WCAG 2.2 AA on every text/background pairing.** Re-verify with the live table in the reference
  page or any contrast checker after wiring tokens. The flagged exceptions are intentional and
  documented: `--text-faint` is decorative-only; `--primary` is large/non-text only.
- **Never remove focus outlines.** Keep the `:focus-visible` ring on every interactive element.
- **Respect `prefers-reduced-motion`.**
- App dark mode stays at `#EDEEF8`-on-`#0A0B14` (~15.9:1). The light AppBar must not put small
  body text on `#6366F1` — use `#4F46E5` (white = 6.3:1) for AppBar background/text pairings.

## Banned tone (copy & visuals)

No "revolutionary / seamless / cutting-edge / next-generation / world-class / game-changing /
robust / powerful / military-grade" energy. No full-bleed gradients (large flat fields + one
deliberate accent beat them). Precision and proof, not hype. No web3 / token aesthetics.

---

## Acceptance criteria

1. One palette: the `#667eea/#764ba2` pair, the green `#48bb78` brand accent, and the Bootstrap
   blues no longer appear anywhere (grep the repo). Green appears only as `--success`.
2. Landing `:root` and the MudTheme derive from the **same hex values** (this README / `production/`).
3. Inter (body) + IBM Plex Mono (technical) load on both surfaces; Roboto and Bootstrap CSS removed.
4. `MudTheme` lives in `SorchaTheme.cs` with Palette(Light/Dark) + Typography + LayoutProperties.
5. Buttons, chips (impl vs roadmap), cards, router, nav, hero match the specs and themes.
6. Every text/bg pairing passes AA (documented exceptions aside); focus rings visible; reduced-motion honoured.
7. Hero particle canvas: calm, pauses off-screen, static under reduced-motion.

## Files in this bundle

- `README.md` — this prompt.
- `reference/Sorcha Visual System.html` — the living spec (open in a browser; live themes + ratios).
- `reference/tokens.css` — the token system (both themes, accent family, grid/bloom/shadow).
- `reference/page.css` — every component's CSS (buttons, chips, router, nav, hero, tables, code wells).
- `reference/system.js` — contrast computation, particle canvas, theme/accent toggles.
- `reference/sorcha-icon.svg` — the "S" mark (unchanged; source of truth for colours).
- `production/sorcha-tokens.css` — clean, demo-free CSS variables to drop into `landing.css`.
- `production/SorchaTheme.cs` — MudBlazor theme (PaletteLight + PaletteDark + Typography + Layout).

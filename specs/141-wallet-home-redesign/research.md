# Phase 0 Research: Wallet Home "Bolder" Reskin

All measurements and colours are authoritative from the handoff (`docs/mockups/design_handoff_wallet_home/` — `README.md` + `design/wallet.jsx` `BoldScreen` + `design/tokens.css`). Production keeps the **Roboto** app font (handoff note: prototype used Inter for polish only).

## R-001 — Dark-mode binding (MudBlazor 9.2)

**Decision**: Bind the PWA's `<MudThemeProvider>` to the existing `IThemeService`. Render with `<MudThemeProvider Theme="SorchaMudTheme.Default" IsDarkMode="@_isDark" />` where `_isDark = ThemeService.IsDarkMode`, subscribe to `IThemeService.OnThemeChanged` in `MainLayout`, call `ThemeService.InitializeAsync()` on init, and `StateHasChanged()` on change.

**Rationale**: `IThemeService` already resolves Light/Dark/System (incl. OS detection via `matchMedia`) and persists the preference; the PWA simply never wired it to the provider (the provider is currently `Theme="SorchaMudTheme.Default"` with no `IsDarkMode`). Reusing the service avoids a second source of truth and matches how the web host is expected to work.

**Verify at implementation (low risk)**: confirm the MudBlazor 9.2 `MudThemeProvider` parameter is `IsDarkMode` (bool, with `IsDarkModeChanged`) — this has been stable across v8/v9. If a one-way bind is preferred over `@bind-IsDarkMode`, set `IsDarkMode` from the service and drive re-render via `OnThemeChanged` (no two-way needed because the toggle lives in Settings, not the provider).

**Alternatives considered**: a fresh per-component dark flag (rejected — duplicates `IThemeService`); CSS-only `prefers-color-scheme` (rejected — ignores the user's explicit Light/Dark override).

## R-002 — Theme tokens: what goes in MudTheme vs CSS variables

**Decision**: Extend `SorchaMudTheme.Default` `PaletteLight`/`PaletteDark` with the design's surface/background/text values, and publish brand + gradient + the design's bespoke values as CSS custom properties the new components consume via `var(--…)`.

- Into `PaletteLight`: `Background = #f4f5fb`, `Surface = #ffffff`, `TextPrimary = #0f1024`, `TextSecondary = #5a607a`, `LinesDefault = #e5e7ef`. (Primary/Secondary already `#667eea`/`#764ba2`.)
- Into `PaletteDark`: `Background = #0a0b14`, `Surface = #181928`, `TextPrimary = #f3f4fa`, `TextSecondary = #9a9cb3`, `LinesDefault = #252638` (overrides the current MudBlazor-default dark surfaces `#1e1e1e`/`#121212`).
- CSS variables (defined once, e.g. in PWA `app.css` `:root` and dark scope, mirrored where the web host needs them): `--sorcha-gradient: linear-gradient(135deg,#667eea 0%,#764ba2 100%)`; `--sorcha-hero-gradient` (light `160deg,#667eea,#764ba2 70%,#3d2c6b`; dark ends `#1a0d2e`); `--sorcha-accent:#48bb78`; `--sorcha-warn:#d69e2e`.

**Rationale**: MudBlazor components (cards, bands, buttons elsewhere) should follow the palette automatically; the bespoke gradient/clip treatments are not expressible as palette entries, so they belong in CSS isolation reading shared variables. The handoff explicitly asks the gradient be a CSS variable (note 1) so per-tenant branding can later override it without re-declaring colours.

**Dark-scope mechanism**: MudBlazor adds a class to the root when dark; component `.razor.css` selects dark variants via a documented hook (confirm whether `body.mud-theme-dark` or a `[data-theme]`/scoped approach is cleanest in 9.2 — see R-006). The `--sorcha-hero-gradient` light/dark switch keys off the same hook.

**Alternatives considered**: all-CSS theme (rejected — loses MudBlazor palette integration for the preserved bands); all-MudTheme (rejected — can't represent the clip-path gradient hero).

## R-003 — Hero + floating bar layout inside MudLayout

**Decision**: `WalletHero` is a self-contained block placed at the top of `Index.razor`'s content, **not** the `MudAppBar`. On the home screen the `MudAppBar` is removed; the hero paints its own header row (org switcher pill + bell + scan) white-on-gradient. The gradient layer is `position:absolute` within the hero's relative container, clipped `clip-path: polygon(0 0,100% 0,100% 78%,0 100%)`, with an 8%-opacity inline SVG grid pattern; content sits in a `position:relative` layer above it. The hero's bottom region is where the card stack / first credentials overlap (negative top margin on the body, per `wallet.jsx` `marginTop:-8`).

`FloatingTabBar` is a shell concern: it replaces the bottom `MudAppBar` in `MainLayout`, rendered as `position:fixed; left:16; right:16; bottom:14` (fixed, not absolute, so it stays on-screen while content scrolls), `height:56`, `radius:18`, `backdrop-filter:blur(20px)`. `MudMainContent` keeps bottom padding (≈`pb-16` equivalent / `padding-bottom` ≥ 84px) so content clears the floating bar (FR-014).

**Rationale**: Removing the app bar only on Home matches the design (chrome lives in the hero). A shell-level floating bar gives FR-015 (every primary screen) for free. `fixed` beats the prototype's `absolute` because the real app scrolls; `absolute` would scroll the bar away.

**Open detail for plan→tasks**: other PWA pages currently rely on the `MudAppBar` top bar for the org chip + bell + scan. Removing the bottom rail is clean; the **top** `MudAppBar` stays for non-home pages (only Home folds it into the hero). So `MainLayout` keeps its top `MudAppBar` and swaps only the bottom rail for `FloatingTabBar`; Home hides/overrides the top bar via its own hero. Confirm the cleanest way to suppress the top app bar on Home only (e.g. a layout flag or letting the hero overlay it) during implementation — see R-006.

## R-004 — Press feedback without CSS `:active`

**Decision**: `BigActionButton` (and the ghost card tap-target) attach `@onpointerdown`/`@onpointerup`/`@onpointercancel`/`@onpointerleave` handlers that toggle a `pressed` CSS class applying `transform: scale(.97)` (buttons) / `scale(.98)` (cards) with a 150ms ease. Disabled Present gets no handlers and `opacity:.72`.

**Rationale**: Handoff note 6 — iOS PWAs eat CSS `:active` inconsistently. Pointer events fire reliably in WASM. Pure-CSS `:active` is the fallback only.

**Reduced motion**: the `pressed`/stack transforms are gated by `@media (prefers-reduced-motion: reduce)` which sets `transition:none` and replaces transforms with instant state (FR-023). Card-stack rotation/translate are decorative — under reduced motion the ghost cards render in their final positions without the entrance/swap animation.

## R-005 — Component placement, naming, bundle hygiene

**Decision**: New components in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Wallet/`, namespace `Sorcha.UI.Core.Components.Wallet` (library `RootNamespace` is `Sorcha.UI.Core`). Each component ships a co-located `.razor.css` (CSS isolation). No new third-party dependencies — gradients, clip-path, blur, and the QR-less ghost cards are plain CSS + inline SVG, so `check-pwa-bundle.ps1` stays green.

**Rationale**: Feature 122 placement rule (user-facing, shared with PWA → `Components.User`). The folder already exists. Re-export to web via the existing `Core` ProjectReference is automatic.

**Alternatives considered**: the handoff suggested `Sorcha.UI.Core/Components/Wallet/` — rejected because the repo's post-F122 convention puts shared *user-facing* components in `Components.User`, and `Core` re-exports them. Documented in the spec assumptions.

## R-006 — Items to confirm during implementation (carry into tasks)

These are low-risk API/selector confirmations, not design unknowns:

1. MudBlazor 9.2 dark-mode hook class/selector for CSS-isolation dark variants (`body.mud-theme-dark` vs alternative). Drives the `.razor.css` dark rules + `--sorcha-hero-gradient` switch.
2. Cleanest way to suppress the top `MudAppBar` on Home only (layout cascading flag vs hero overlay vs a Home-specific layout). The bottom-rail→`FloatingTabBar` swap is unconditional.
3. Whether `ContextChipSwitcher` (a `MudChip` styled for the app bar) needs a white-on-gradient variant parameter to read correctly inside the hero, or whether a thin wrapper/override is cleaner.
4. The active-tab detection source for `FloatingTabBar` (compare `NavigationManager.Uri` against the four base-relative routes: ``, `devices`, `activity`, `settings`).

## R-007 — States to support (from `app.jsx` defaults + README)

Fixed (not user-toggleable): density = **comfy**, card style = **soft** (shadow), tab labels = **active-only**, palette = Sorcha default. Variable (data-driven): empty vs populated (credential count), light vs dark (theme preference), sync-warning visible vs hidden (sign-in/sync state). Reference dimensions: 375×740; hero gradient block height ≈260 with the 78% clip; ghost stack 170 tall (comfy); big buttons 104 tall; tab bar 56.

# Component Contracts: Wallet Home "Bolder" Reskin

This feature has **no HTTP/gRPC contracts** (no new endpoints). The "contracts" here are the public Razor component APIs of the four new shared components. They are the integration surface between the PWA/web hosts and the shared library, and are the basis for bUnit tests.

Namespace: `Sorcha.UI.Core.Components.Wallet` (library `RootNamespace = Sorcha.UI.Core`).
Folder: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Wallet/`.

---

## WalletHero

```csharp
public enum WalletHeroMode { Empty, Active }

// Parameters
[Parameter, EditorRequired] public WalletHeroMode Mode { get; set; }
[Parameter] public int CredentialCount { get; set; }          // shown in Active headline
[Parameter] public RenderFragment? HeaderContent { get; set; } // org switcher + bell + scan slot
// Optional copy overrides (default to handoff strings; localisable)
[Parameter] public string? Eyebrow { get; set; }
[Parameter] public string? Headline { get; set; }
[Parameter] public string? Subtitle { get; set; }
```

**Contract**:
- Renders a gradient hero (light: `160deg,#667eea,#764ba2 70%,#3d2c6b`; dark: ends `#1a0d2e`), clip-path `polygon(0 0,100% 0,100% 78%,0 100%)`, 8% grid texture.
- `Empty` → eyebrow "WELCOME", headline "Your wallet is empty", subtitle enrol prompt. `Active` → eyebrow "ACTIVE WALLET", headline "{CredentialCount} credentials", subtitle present/scan prompt. Explicit copy parameters override.
- `HeaderContent` renders white-on-gradient at the top of the hero; the component provides the styling context only — host supplies `ContextChipSwitcher`, bell, scan with their existing behaviour.
- Acceptance: FR-001, FR-002, FR-003 (host wires FR-004 via the slot).

---

## BigActionButton

```csharp
public enum BigActionKind { Primary, Ghost }

[Parameter, EditorRequired] public BigActionKind Kind { get; set; }
[Parameter, EditorRequired] public string Icon { get; set; } = "";   // MudBlazor icon path
[Parameter, EditorRequired] public string Title { get; set; } = "";
[Parameter] public string? Subtitle { get; set; }
[Parameter] public bool Disabled { get; set; }
[Parameter] public EventCallback OnActivated { get; set; }
```

**Contract**:
- 104px tall, radius 16, padding 14/16, icon-chip top + title/subtitle bottom.
- `Primary` → gradient fill, white text, primary shadow. `Ghost` → surface fill, border, subtle shadow (light/dark variants).
- `Disabled` → opacity .72, no press feedback, no `OnActivated`.
- Press: pointer-event-driven `scale(.97)` (not CSS `:active`); suppressed under `prefers-reduced-motion`.
- Accessible name = `Title` + `Subtitle`.
- Acceptance: FR-005, FR-006 (host passes `Disabled`), FR-007, FR-008, FR-023, FR-024.

---

## WalletCardStack (empty ghost fan only)

```csharp
[Parameter] public EventCallback OnAddCredential { get; set; }
```

**Contract**:
- Renders three stacked rounded rectangles (top `z` highest, two behind offset/rotated/dimmed per handoff: card2 `translateY(10) rotate(-3) scale(.96) op .65`; card3 `translateY(20) rotate(4) scale(.92) op .35`).
- Top card: gradient fill, "SORCHA" eyebrow, "Add a credential" title, plus-icon in a circle, subtitle "Tap to enrol this device — or load a demo card to look around."; it is the tap-target → `OnAddCredential`.
- **No populated mode.** When credentials exist the host renders existing cards instead; this component is not used.
- Reduced motion: render cards in final positions, no entrance/swap animation.
- Acceptance: FR-009, FR-010, FR-023, FR-024 (top card has an accessible name).

---

## FloatingTabBar

```csharp
public sealed record WalletTab(string Route, string Label, string Icon);  // Route is base-relative

[Parameter] public string ActiveRoute { get; set; } = "";   // ""|"devices"|"activity"|"settings"
[Parameter] public EventCallback<string> OnNavigate { get; set; }
// Tabs are fixed internally: Home(""), Devices("devices"), Activity("activity"), Settings("settings")
```

**Contract**:
- `position:fixed; left:16; right:16; bottom:14; height:56; radius:18; backdrop-filter:blur(20px)`, light/dark surface, shadow per handoff.
- Active tab (matched by `ActiveRoute`): gradient pill background + visible label; inactive: icon only.
- `OnNavigate` carries the base-relative route; host performs `NavigateTo(route)` (home = `NavigateTo("")`).
- Each tab exposes an accessible name (label even when visually icon-only).
- Acceptance: FR-012, FR-013, FR-014 (host reserves bottom padding), FR-015 (mounted in shell), FR-024.

---

## Host integration (not a component, but the contract the page must honour)

- `Index.razor`: compose `WalletHero` (Mode from credential count) → `WalletCardStack` (empty branch, `OnAddCredential` → `NavigateTo("enrol")`) OR existing credential cards (populated branch) → `BigActionButton`×2 (Present `Disabled=@(_credentials is null or { Count: 0 })` → `NavigateTo("present")` via existing `HandlePresentRequestedAsync`; Verify → `NavigateTo("verify")`). All F125 bands + F124 overlays + clock-skew alerts preserved below/around.
- `MainLayout.razor`: bottom `MudAppBar` rail → `FloatingTabBar` (`OnNavigate` → base-relative `NavigateTo`); `<MudThemeProvider IsDarkMode="@_isDark">` bound to `IThemeService`; keep top `MudAppBar` for non-home pages (Home folds chrome into the hero per R-003).
- Feedback: inline only (`IInlineFeedback`); never `ISnackbar`.

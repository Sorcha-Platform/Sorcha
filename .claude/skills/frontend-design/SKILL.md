---
name: frontend-design
description: |
  Styles Blazor WASM components with CSS and responsive design patterns using MudBlazor Material Design.
  Use when: Creating new components, styling existing components, implementing responsive layouts, adding animations/transitions, or working with the MudBlazor component library.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Frontend-design Skill

Sorcha uses **MudBlazor 9.2.0** for Material Design components with **CSS Isolation** as the primary styling approach. The design system follows Material Design 3 with custom extensions for blockchain/workflow visualization.

The canonical theme is `SorchaMudTheme.Default` (`Sorcha.UI.Components.User/Theme/SorchaMudTheme.cs`) — both the web host and the Citizen Wallet PWA bind their `MudThemeProvider` to it; it carries the light + dark palette tokens (page background, surface, text, lines). Bespoke non-palette values (brand gradient, accent green, warn amber) are CSS custom properties (`--sorcha-gradient`, `--sorcha-accent`, `--sorcha-warn`); isolated component CSS reads MudBlazor's own `--mud-palette-*` variables for surfaces/text so light/dark switch automatically, with literal fallbacks for cross-host rendering. The wallet "Bolder" home chrome (Feature 141) — `WalletHero`, `BigActionButton`, `WalletCardStack`, `FloatingTabBar` — lives in `Sorcha.UI.Components.User/Components/Wallet/` (namespace `Sorcha.UI.Core.Components.Wallet`).

## Quick Start

### Styling a New Component

```razor
@* MyComponent.razor *@
<MudPaper Elevation="1" Class="pa-4 mb-3">
    <MudText Typo="Typo.h6" Class="mb-2">Component Title</MudText>
    <MudText Typo="Typo.body2" Color="Color.Secondary">
        Description text
    </MudText>
</MudPaper>
```

### Custom Component with CSS Isolation

```razor
@* MyCard.razor *@
<div class="custom-card @(IsSelected ? "selected" : "")">
    <div class="card-header">@Title</div>
    <div class="card-content">@ChildContent</div>
</div>

@code {
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public bool IsSelected { get; set; }
}
```

```css
/* MyCard.razor.css */
.custom-card {
    background: white;
    border: 2px solid #1976d2;
    border-radius: 10px;
    box-shadow: 0 2px 8px rgba(0,0,0,0.12);
    transition: all 0.2s ease;
}

.custom-card:hover {
    box-shadow: 0 4px 12px rgba(0,0,0,0.18);
    transform: translateY(-2px);
}

.custom-card.selected {
    border-color: #0d47a1;
    border-width: 3px;
}
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| CSS Isolation | Component-scoped styles | `Component.razor.css` |
| MudBlazor Utility | Spacing, flex, alignment | `Class="d-flex pa-4 mb-3"` |
| Color System | Semantic colors | `Color.Primary`, `Color.Error` |
| Typography | Text hierarchy | `Typo.h6`, `Typo.body2` |
| Elevation | Shadow depth | `Elevation="1"` (0-24) |
| Breakpoint | Responsive | `Breakpoint.Sm` (641px) |

## Common Patterns

### Flex Layout with Gap

```razor
<div class="d-flex align-center gap-2 mb-3">
    <MudIcon Icon="@Icons.Material.Filled.Settings" />
    <MudText Typo="Typo.body1">Settings</MudText>
    <MudSpacer />
    <MudChip T="string" Size="Size.Small" Color="Color.Info">Active</MudChip>
</div>
```

### Panel with Header/Content Pattern

```razor
<MudPaper Elevation="1" Class="panel">
    <div class="panel-header">
        <MudText Typo="Typo.subtitle1">Panel Title</MudText>
    </div>
    <div class="panel-content">
        @* Content here *@
    </div>
</MudPaper>
```

## See Also

- [aesthetics](references/aesthetics.md) - Color system, typography, brand identity
- [components](references/components.md) - MudBlazor component patterns
- [layouts](references/layouts.md) - Page structure, responsive grids
- [motion](references/motion.md) - Transitions, hover effects
- [patterns](references/patterns.md) - DO/DON'T design decisions

## Related Skills

- See the **blazor** skill for component lifecycle and state management
- See the **signalr** skill for real-time UI updates
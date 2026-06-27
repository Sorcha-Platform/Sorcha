# Phase 1 Data Model: Social Provider Brand Icons

This feature is **visual / presentational** — it introduces **no persisted entities, no schema
changes, and no new DTOs on the wire**. The "entities" below are conceptual presentation-layer
constructs that exist only at render time, derived entirely from existing provider configuration.

## Entity: Social Provider Choice (existing — unchanged data, new presentation field)

A selectable sign-in/registration option representing one external identity provider. This already
exists on all three surfaces; the feature only adds a *derived, render-time* brand-icon to its
presentation. No stored attribute is added.

| Attribute | Source | Notes |
|-----------|--------|-------|
| `providerKey` | Existing config — web `ISocialLoginService.GetConfiguredProviderNames()` (capitalised), PWA `ISocialProvidersClient.GetConfiguredAsync()` (lowercase) | Drives both the existing flow and (now) icon selection. Matched **case-insensitively**. |
| `label` | Existing — `"Continue with {provider}"` | Unchanged. Remains the accessible name. |
| `isConfigured` | Existing — provider has non-empty ClientId/ClientSecret | Unchanged. Governs whether the choice renders at all (FR-005). |
| `brandIcon` *(new, derived)* | Computed at render from `providerKey` via the icon resolver | Not stored; never persisted; recomputed each render. |

**Validation / rules** (presentation invariants, enforced by the resolver + tests):
- Every supported `providerKey` (`google`, `microsoft`, `github`, `apple`, any casing) resolves to a
  non-empty, faithful brand mark (FR-001..FR-004, FR-011, SC-001).
- An unconfigured provider does not render (FR-005) — out of scope of the resolver, governed upstream.
- A configured provider with no defined mark resolves to the **neutral fallback** (FR-007, SC-002).
- The resolved icon is **decorative** — no accessible name of its own (FR-010, SC-005).

**State transitions**: none. Stateless, idempotent render-time derivation.

## Entity: Brand Icon (new — value, not persisted)

The recognisable visual mark for a provider. It is a pure value produced by the resolver; it has no
identity, lifecycle, or storage.

| Surface | Representation | Fallback |
|---------|----------------|----------|
| Web (Razor) | Inline `<svg aria-hidden="true">…</svg>` markup string (`HtmlString`). Google/Microsoft = official multi-colour; Apple/GitHub = `fill="currentColor"` monochrome. | Neutral globe SVG (`currentColor`). |
| PWA (MudBlazor) | `Icons.Custom.Brands.{Google\|Microsoft\|GitHub\|Apple}` string passed to `StartIcon`. | `Icons.Material.Filled.Public`. |

**Invariant**: identical provider-key vocabulary and identical fallback semantics across both surfaces,
so a provider looks consistent on web and PWA (FR-009) and neither surface can emit a broken/empty mark.

## Provider → mark mapping (single source of truth)

| Provider key (case-insensitive) | Web mark | PWA mark | Colour behaviour |
|---|---|---|---|
| `google` | Official Google "G" inline SVG | `Icons.Custom.Brands.Google` | Fixed multi-colour |
| `microsoft` | Official Microsoft 4-square inline SVG | `Icons.Custom.Brands.Microsoft` | Fixed multi-colour |
| `github` | GitHub mark inline SVG | `Icons.Custom.Brands.GitHub` | `currentColor` (theme-adaptive) |
| `apple` | Apple mark inline SVG | `Icons.Custom.Brands.Apple` | `currentColor` (theme-adaptive) |
| *(any other configured key)* | Neutral globe inline SVG | `Icons.Material.Filled.Public` | `currentColor` |

This table is the contract the resolver implementations and unit tests assert against — see
`contracts/icon-resolution.md`.

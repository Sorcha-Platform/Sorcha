# Contract: Provider → Brand-Icon Resolution

This feature exposes **no new external/network API**. The existing endpoints and config that drive the
provider list are unchanged:

- Web: `ISocialLoginService.GetConfiguredProviderNames()` (unchanged).
- PWA: `GET api/auth/social/providers` via `ISocialProvidersClient.GetConfiguredAsync()` (unchanged).

The only new contract is the **internal, render-time icon-resolution behaviour** that each surface must
satisfy. It is specified here so the two independent implementations (web SVG resolver, PWA icon switch)
stay behaviourally identical and are testable.

## Web resolver — `SocialProviderBrandIcon`

Location: `src/Services/Sorcha.Tenant.Service/Services/SocialProviderBrandIcon.cs`

```
static HtmlString For(string providerKey)
```

| Input (any casing) | Output |
|---|---|
| `"google"` | inline `<svg aria-hidden="true" …>` containing the Google "G" |
| `"microsoft"` | inline `<svg aria-hidden="true" …>` Microsoft 4-square |
| `"github"` | inline `<svg aria-hidden="true" …>` GitHub mark, `fill="currentColor"` |
| `"apple"` | inline `<svg aria-hidden="true" …>` Apple mark, `fill="currentColor"` |
| any other non-empty string | neutral globe `<svg aria-hidden="true" … fill="currentColor">` |
| `null` / empty | neutral globe (never throws) |

**Guarantees**:
- Always returns non-empty markup beginning with `<svg` and carrying `aria-hidden="true"` (decorative).
- Case-insensitive on the key.
- Pure, deterministic, side-effect free, never throws.
- Markup is author-authored constant — **no caller-supplied value is interpolated into the SVG**
  (no injection surface).

## PWA resolver — `SignIn.ProviderIcon`

Location: `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor` (private static)

```
static string ProviderIcon(string providerKey)
```

| Input (any casing) | Output |
|---|---|
| `"google"` | `Icons.Custom.Brands.Google` |
| `"microsoft"` | `Icons.Custom.Brands.Microsoft` |
| `"github"` | `Icons.Custom.Brands.GitHub` |
| `"apple"` | `Icons.Custom.Brands.Apple` |
| any other / null / empty | `Icons.Material.Filled.Public` |

**Guarantees**: case-insensitive, total (no throw), returns a valid MudBlazor icon string for every
input, used as `MudButton.StartIcon` so it renders `aria-hidden` and matches the passkey button.

## Cross-surface invariants (asserted by tests / review)

1. Both resolvers accept the **same four keys** case-insensitively.
2. Both resolvers return a **non-broken visible mark for every input**, including unknowns (fallback).
3. Neither resolver changes which providers appear, nor any auth flow (FR-005, FR-006, SC-004).
4. Both marks are **decorative** — the button text stays the accessible name (FR-010, SC-005).

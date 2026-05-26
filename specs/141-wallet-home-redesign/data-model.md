# Phase 1 Data Model: Wallet Home "Bolder" Reskin

This feature introduces **no new persisted entities and no new wire contracts**. It is a presentation change over existing client-side state. This document records the view-state the home reads and the parameter shapes the new components expose, so the plan/tasks have a precise contract.

## View-state consumed (existing, unchanged)

| State | Source (existing) | Drives |
|---|---|---|
| Credential list (active context) | `ICredentialCache.ListAsync()` → `IReadOnlyList<CachedCredential>?` | Empty vs populated layout; hero headline/eyebrow; Present enabled/disabled. `null` = loading. |
| Active context label + memberships | `IUserContext` / `IUserOrgMembershipsClient` (via `ContextChipSwitcher`) | Hero org-switcher pill; refresh-on-switch. |
| Notifications unread count | F118 `IInboxApiService.GetUnreadCountAsync()` + `TenantHubConnection.OnInboxUnreadCountUpdated` | Hero bell badge. |
| Theme preference (resolved) | `IThemeService.IsDarkMode` + `OnThemeChanged` | Light/dark palette + hero gradient variant. |
| Sync / sign-in state | `ISyncService` outcome + `HttpRequestException` 401 in `Index.razor` | Amber sync-warning visibility + copy. |
| Pending-application notice | `IPendingApplicationClient.GetAsync()` (F124) | Waiting-state vs bare empty state. |
| Per-device welcome flag | `IWalletFlagsStore` (F124) | First-credential welcome overlay (unchanged). |
| Needs-attention / recent-activity / other-context | F125 band inputs assembled in `Index.razor` | Preserved bands (rendered under new chrome). |

`CachedCredential` fields available today: `Id (Guid)`, `Vct`, `RawSdJwt`, `AvailableClaimNames`, `IssuerDid?`, `DisplayLabel?`. **Note**: no issuer display-name, type code, accent colour, or meta line — which is why the populated fanned/accented card stack is out of scope. The populated branch keeps the **existing** credential card rendering (`MudCard` per credential in `Index.razor` today / the existing `CredentialCard` family), untouched.

## New component parameter contracts

All four components are presentational — no service injection beyond what they already need; behaviour (navigation, enrolment, present, verify, context switch) is passed in by the host via `EventCallback`/parameters so the components stay host-agnostic and testable. Full signatures in `contracts/component-contracts.md`.

### WalletHero
- `Mode` (enum `WalletHeroMode { Empty, Active }`) — selects eyebrow/headline/subtitle copy.
- `CredentialCount` (int) — rendered in the Active headline.
- `HeaderContent` (`RenderFragment?`) — slot for the org switcher + bell + scan affordances so the host wires existing components/behaviour; hero only provides the white-on-gradient styling context.
- (Copy strings are localisable; defaults match the handoff: "WELCOME" / "Your wallet is empty" / "Enrol this device to load yours." and "ACTIVE WALLET" / "{n} credentials" / "Tap a card to present, or scan to verify.")

### BigActionButton
- `Kind` (enum `BigActionKind { Primary, Ghost }`).
- `Icon` (string — MudBlazor icon), `Title` (string), `Subtitle` (string).
- `Disabled` (bool) — Present-when-empty (opacity .72, no press, no click).
- `OnActivated` (`EventCallback`).
- Accessible name composed from `Title` + `Subtitle`.

### WalletCardStack (empty ghost fan only)
- `OnAddCredential` (`EventCallback`) — fired when the top card is tapped (host routes to enrolment).
- Renders exactly three ghost cards; top card carries "SORCHA" eyebrow, "Add a credential", plus-icon, subtitle. No populated mode (host renders existing cards when credentials exist).
- Honours `prefers-reduced-motion`.

### FloatingTabBar
- `ActiveRoute` (string — base-relative: ``, `devices`, `activity`, `settings`) OR derives active from `NavigationManager` internally (decision in R-006 #4).
- `OnNavigate` (`EventCallback<string>`) — host performs base-relative `NavigateTo`.
- Fixed 4 tabs (Home/Devices/Activity/Settings); active tab shows gradient pill + label; inactive icon-only.

## Theme token additions (`SorchaMudTheme.Default`)

| Palette field | Light | Dark (overrides current) |
|---|---|---|
| `Background` | `#f4f5fb` | `#0a0b14` |
| `Surface` | `#ffffff` | `#181928` |
| `TextPrimary` | `#0f1024` | `#f3f4fa` |
| `TextSecondary` | `#5a607a` | `#9a9cb3` |
| `LinesDefault` | `#e5e7ef` | `#252638` |

`Primary`/`Secondary`/`AppbarBackground` unchanged. CSS variables added separately: `--sorcha-gradient`, `--sorcha-hero-gradient` (light/dark), `--sorcha-accent`, `--sorcha-warn`.

## State transitions

None persisted. The only runtime transitions are presentational and already governed by `Index.razor`: loading → empty/populated (on credential load/sync), and light ⇄ dark (on `IThemeService.OnThemeChanged`). The first-credential welcome one-way transition (`WelcomedAt` null → timestamp) is unchanged.

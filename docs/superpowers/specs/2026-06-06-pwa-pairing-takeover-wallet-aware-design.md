# Wallet-aware PairingTakeover (companion-first P0 #1)

**Date:** 2026-06-06
**Status:** Approved — ready for implementation plan

> **Correction (2026-06-07, fix after #978):** the create-wallet CTA target below is written as
> `{origin}/wallets/create`, mirroring `GoToWebSignup`. That was wrong — on-n1 browser validation
> showed it 404s because the web Blazor client is mounted under the **`/app`** base path. The
> shipped code targets **`{origin}/app/wallets/create`**. (`/auth/signup` had no `/app` prefix
> because it is a root-level tenant-service Razor page, not an `/app` WASM route — a distinction I
> missed.) Read every `/wallets/create` below as `/app/wallets/create`.
**Feature area:** Citizen Wallet PWA (Feature 128 cold-start onboarding)
**Roadmap item:** P0 #1 in `docs/superpowers/specs/2026-06-06-citizen-wallet-companion-roadmap.md`
**Decision owner:** Stuart Fraser

---

## 1. Problem

`PairingTakeover` (`src/Apps/Sorcha.Wallet.Pwa/Components/PairingTakeover.razor`) is a
full-screen overlay shown to any signed-in citizen with **no paired device on this hardware** —
it gates on `IHasPairedDeviceProbe.HasAnyDevice == false`. Its only primary action ("Set up this
device") calls `IEnrolmentService.EnrolAsync` → `POST /api/v1/wallet/devices/enrol`.

But **device enrolment requires a wallet to already exist.** `ResolveCitizenContextAsync` returns
a null wallet when none resolves for the caller, and the enrol endpoint then returns **404**. So a
citizen who reaches the PWA *without* having created a wallet on the web (signed up on web but never
created a wallet, or landed in the PWA first) taps "Set up this device" → 404 → the generic
`"Couldn't pair this device…"` error → **dead-end with no way forward**.

The consumer-tier JWT (Feature 136) carries `org_id` but **omits wallet binding**, so "does this
citizen have a wallet?" is a **server fact** — it cannot be read from the token.

This is the companion-first direction in action: the **web app owns wallet creation**; the PWA must
*route* a walletless citizen to the web rather than dead-end them. (Same shape as the already-shipped
guided-tour suppression and the sign-in "Create an account → web signup" link.)

### Key design constraint (from brainstorm)

**Walletless is a one-shot, terminal cold-start state.** Creating a wallet is essentially the first
thing a citizen does, and once a wallet exists it is impossible to drop back to zero wallets. So the
"has wallet?" signal only ever transitions `false → true`, once, and never back. This is why the
detection does **not** need a live event-driven probe — a single check is sufficient and correct.

---

## 2. Detection mechanism (decided)

We considered piggy-backing on an existing endpoint's failure mode and **rejected it** after
verifying the actual behaviour:

- `GET /api/v1/wallet/holder-keys` returns **401** when no wallet resolves (`platformUserId is null
  || citizenWallet is null`) and **404** only when a wallet *exists* but no holder/delivery key is
  derivable yet. Its 404 therefore means the *opposite* of walletless — it fires for the
  has-wallet/no-device citizen we explicitly want to keep on the pair flow. (The endpoint's XML doc
  claiming "404 when no wallet resolves" is wrong vs. its implementation.)
- The no-wallet signal is also **inconsistent across the citizen surface**: enrol 404s on no-wallet,
  holder-keys 401s. Coupling takeover logic to any one endpoint's failure mode is fragile.

**Decision:** add a tiny, purpose-built endpoint that returns an explicit boolean and always 200,
consumed by a one-shot client check.

---

## 3. Design

### 3.1 Server — new existence endpoint

A new consumer-tier endpoint in `CitizenWalletEndpoints.cs`, mounted on the existing
`/api/v1/wallet` group (so it inherits `RequireConsumerAudience` + `RateLimitPolicies.Strict`):

```
GET /api/v1/wallet/exists  →  200 { "hasWallet": bool }
```

- Handler resolves the citizen via `ResolveCitizenContextAsync` and returns
  `Results.Ok(new WalletExistsResponse { HasWallet = walletAddress is not null })`.
- **Always 200** for an authenticated consumer — it deliberately avoids the 401/404 ambiguity that
  made the other endpoints unusable for this purpose. An unauthenticated or non-consumer caller is
  still rejected upstream by the `RequireConsumerAudience` gate (401/403); a *signed-in* citizen
  always receives a clean boolean.
- New response record `WalletExistsResponse { bool HasWallet }` in
  `Sorcha.CitizenWallet.Abstractions.Models` (sibling to `HasAnyDeviceResponse`), XML-documented.
- OpenAPI metadata: `.WithName("CitizenWalletExists")`, `.WithSummary(...)`, `.WithDescription(...)`,
  `.Produces<WalletExistsResponse>(StatusCodes.Status200OK)`,
  `.Produces(StatusCodes.Status401Unauthorized)`. License header on the new model file.

### 3.2 Client — one-shot probe (not an event probe)

New PWA-local service:

```csharp
public interface IHasWalletProbe
{
    /// One-shot: does the signed-in citizen have a wallet? Walletless is a
    /// terminal cold-start state (false → true, once, never back), so there is
    /// deliberately no Changed/EnsureLoadedAsync/Refresh contract.
    Task<bool> HasWalletAsync(CancellationToken ct = default);
}
```

- Typed-HttpClient implementation `HasWalletProbe` calling `GET /api/v1/wallet/exists`.
- Lives in `src/Apps/Sorcha.Wallet.Pwa/Services/Wallet/` — **PWA-local**; the web host does not need
  it (only `PairingTakeover` consumes it). Namespace at subject level per the F123 convention.
- Registered in `Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs` with the **same handler
  chain as the device probe**: `BearerTokenHandler` + `ServerClockHandler`, base address =
  `gatewayBaseAddress`.
- **Fail-safe direction:** on a network/transient failure (`HttpRequestException`, timeout, non-2xx,
  empty body) the probe returns **`true`** — i.e. *assume the citizen has a wallet* and fall through
  to the existing pair flow (which has its own error handling). Rationale: we must never (a) block a
  real wallet-owner behind a flaky existence check, nor (b) falsely send a wallet-owner to create a
  second wallet. The cost of a false `true` is only that a genuinely walletless citizen sees the
  enrol path's existing 404 message instead of the new create-wallet copy — strictly no worse than
  today's behaviour.

### 3.3 PairingTakeover — three explicit states

State resolution order, gated so the overlay **never dead-ends and never flashes**:

| Order | Condition | UI |
|---|---|---|
| — | device probe `HasAnyDevice == null` (initial fetch in flight) | hidden (unchanged) |
| — | `HasAnyDevice == true` | hidden — device already paired here (unchanged) |
| 1 | `HasAnyDevice == false` **and** `hasWallet == false` | **Create-wallet state** (new) |
| 2 | `HasAnyDevice == false` **and** `hasWallet == true` | **Pair state** (today's UI, unchanged) |

**Flow:** `OnInitializedAsync` subscribes + awaits the device probe exactly as today. When the device
probe resolves to `false`, the component awaits the one-shot `HasWalletAsync` **once** and stores the
result in a nullable `bool? _hasWallet` field before choosing which body to render. While that single
call is in flight, `_hasWallet == null` and the overlay stays **hidden** (the same "don't flash"
rule that already applies to the null device-probe window). Visibility predicate becomes:

```
_isVisible = Probe.HasAnyDevice == false && _hasWallet is not null;
// body chosen by _hasWallet.Value
```

The existing dismissal sources (local pair-success, remote `DeviceEnrolled` hub event,
`Probe.Changed`) are unchanged and only matter in the pair state.

**Create-wallet state** — new body rendered inside the same `sorcha-welcome-overlay` /
`sorcha-welcome-content` frame (re-uses `welcome-takeover.css`, consistent with the pair state):

- Headline: **"Create your wallet first"**
- Subhead: *"Your Sorcha wallet lives in your account on the web. Create it there, then come back
  here to pair this phone and hold your credentials."*
- Primary button **"Create your wallet"** → `GoToWebWalletCreation()`:

  ```csharp
  // Wallet creation lives on the web host at origin-root /wallets/create (the
  // PWA is mounted under /wallet/, so build the absolute origin URL rather than
  // a leading-slash path). forceLoad because it leaves the PWA for the web app.
  var origin = new Uri(Nav.BaseUri).GetLeftPart(UriPartial.Authority);
  Nav.NavigateTo($"{origin}/wallets/create", forceLoad: true);
  ```

  This is byte-for-byte the `SignIn.razor` `GoToWebSignup` precedent (verified to exist at
  `/wallets/create` on the web host: `Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor`).
- **No short-code panel** in this state — redeeming a pairing code still requires a wallet, so it
  would dead-end identically. The short-code affordance stays only in the pair state.
- Test hooks: `data-testid="pairing-takeover-create-wallet"` (container) and
  `data-testid="pairing-takeover-create-wallet-button"` (primary CTA).

**Fire-and-forget return** (decided): the citizen creates the wallet on the web, returns to the PWA,
the takeover re-initialises, the one-shot check now returns `true`, and they get the pair flow. No
cross-host return URL, no web-side changes — matching the companion-first signup-link precedent.

---

## 4. Testing (TDD)

- **`HasWalletProbeTests`** (`tests/Sorcha.Wallet.Pwa.Tests/Services/`) — mocked `HttpMessageHandler`
  in the `EnrolmentServiceTests` style:
  - `200 {hasWallet:true}` → `HasWalletAsync` returns `true`.
  - `200 {hasWallet:false}` → returns `false`.
  - transient failure (throw / 500 / empty body) → returns `true` (fail-safe).
- **`PairingTakeoverTests`** (`tests/Sorcha.Wallet.Pwa.Tests/Components/`, `ComponentTestFixture`):
  - device probe `false` + wallet probe `false` → renders the create-wallet body
    (`pairing-takeover-create-wallet` present); the enrol primary button is **absent**.
  - device probe `false` + wallet probe `true` → renders the existing pair body
    (`pairing-takeover-primary-button` present); create-wallet body absent.
  - wallet probe in flight (`_hasWallet == null`) → overlay hidden (no `pairing-takeover` node).
  - device probe `true` → hidden (regression guard, unchanged behaviour).
  - MudBlazor overlay/expansion/text content rendered via the provider-host pattern
    (`MudPopoverProvider` / `MudDialogProvider`) where the component tree needs it, per
    `tests/Sorcha.UI.Core.Tests/TestHosts/GuidedTourHost.razor`.
- **E2E** under `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/` — added only if feasible.
  Authenticated PWA state lives in IndexedDB (issue #700), so an authenticated walletless E2E may
  self-`Assert.Ignore`. The `data-testid` hooks are added regardless; the PR is **not** blocked on a
  green authenticated E2E.

---

## 5. Guardrails honoured

- **Notification routing:** no `ISnackbar` (CI gate `scripts/check-no-snackbar.ps1`); the
  create-wallet state surfaces no toast — errors, if any, stay inline `MudAlert` as in the pair
  state. Actor's-own-action feedback only.
- License header on the 3 new files (`WalletExistsResponse`, `IHasWalletProbe`, `HasWalletProbe`).
- **No hard-coded `<Version>`** anywhere (unified build-time versioning).
- All work on `feature/pwa-pairing-takeover-wallet-aware` → phased atomic commits → **one PR**.
- **Clean-break** — the endpoint is brand-new, no back-compat shim. No EF/schema change (read-only
  resolution of an existing column), so no migration.
- **Doc-sync:** update the F128 entry in `.claude/skills/sorcha-architecture/SKILL.md` (new
  `/api/v1/wallet/exists` surface + the wallet-aware takeover state machine) and tick P0 #1 in the
  companion-first roadmap.

---

## 6. Out of scope (YAGNI / separate items)

- Round-trip return URL from web `/wallets/create` back to the PWA pair flow.
- A full event-driven `IHasWalletProbe` (Changed/EnsureLoadedAsync/Refresh) — unjustified for a
  one-shot terminal state.
- Any web-side change to `/wallets/create`.
- Recovery honesty (roadmap **P0 #2**) — tracked separately.
- In-PWA wallet creation / signup / recovery — the self-contained PWA milestone (roadmap §5).

---

## 7. Definition of done

- A walletless signed-in citizen in the PWA gets a clear "create your wallet on the web" path (no
  dead-end), and after creating one can return and pair the device.
- A citizen who **has** a wallet but no device here still gets the existing pair flow, unchanged.
- New behaviour is covered by bUnit (component states) + the probe unit tests; E2E hooks present.
- Single PR, green CI (`build-and-test` + `claude-review`), code-reviewed, then validated on n1
  (pull + recreate `sorcha-wallet-pwa`, confirm new build badge, walk the walletless + has-wallet
  paths) using the `n1-deploy` skill.

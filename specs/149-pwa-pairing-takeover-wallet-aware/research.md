# Phase 0 Research: Wallet-aware PairingTakeover

All open questions were resolved during the brainstorm (see the design doc). No outstanding
NEEDS CLARIFICATION.

## Decision 1 — Wallet-existence detection mechanism

**Decision:** Add a purpose-built `GET /api/v1/wallet/exists → 200 { hasWallet }` endpoint; consume
it from a one-shot client check.

**Rationale:** "Has wallet?" is a server fact (the consumer JWT carries no wallet binding). A
dedicated boolean endpoint is unambiguous and decoupled from unrelated endpoints' failure modes.

**Alternatives considered:**
- *Reuse `holder-keys` 404* — **rejected.** Verified in `CitizenWalletEndpoints.GetHolderKeys`:
  no-wallet returns **401** (`platformUserId is null || citizenWallet is null`), and **404** fires
  only when a wallet *exists* but no holder/delivery key is derivable. Its 404 means the *opposite*
  of walletless (it matches the has-wallet/no-device citizen we must keep on the pair flow). The
  endpoint's XML doc is wrong vs. its implementation.
- *Interpret enrol 404 inline* — **rejected.** Makes the user click into a failure first; couples
  takeover logic to the enrol endpoint; the enrol 404 is documented as "indistinguishable from
  non-existence".

## Decision 2 — One-shot check vs. event-driven probe

**Decision:** `IHasWalletProbe` exposes only `Task<bool> HasWalletAsync(CancellationToken)`. No
`Changed` / `EnsureLoadedAsync` / `Refresh`.

**Rationale:** Walletless is a **terminal cold-start state** — creating a wallet is essentially the
first action a citizen takes, and once a wallet exists it can never drop back to zero. The signal
transitions `false → true` exactly once, so a live change-notification contract (as on
`IHasPairedDeviceProbe`, where devices can be revoked) would be dead weight (YAGNI).

**Alternatives considered:** Full `IHasPairedDeviceProbe`-style probe — rejected as unjustified
plumbing for a one-shot signal.

## Decision 3 — Fail-safe direction on transient existence-check failure

**Decision:** On network/timeout/non-2xx/empty body, `HasWalletAsync` returns **`true`** (assume the
citizen has a wallet → fall through to the existing pair flow).

**Rationale:** Two failure modes weighed: a false `false` would wrongly tell a wallet-owner to
create a *second* wallet (bad, confusing, companion-first violation); a false `true` only means a
genuinely walletless citizen sees the pre-existing enrol 404 message — identical to today's
behaviour, i.e. no regression. We optimise to never harm the wallet-owner.

## Decision 4 — Walletless return flow

**Decision:** Fire-and-forget: force-load `{origin}/wallets/create` (absolute origin, `forceLoad`),
byte-for-byte the `SignIn.razor` `GoToWebSignup` precedent. The citizen returns to the PWA manually;
the takeover re-runs its one-shot check on next load.

**Rationale:** Matches the established companion-first handoff; avoids cross-host return-URL wiring
and any web-side change to `/wallets/create`. Verified the route exists:
`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor` → `@page "/wallets/create"`.

**Alternatives considered:** Round-trip return URL — rejected (new cross-host work; no UX payoff that
justifies it for a one-time action).

## Decision 5 — Component placement

**Decision:** `IHasWalletProbe` + `HasWalletProbe` live PWA-local in
`src/Apps/Sorcha.Wallet.Pwa/Services/Wallet/`.

**Rationale:** Only `PairingTakeover` (PWA) consumes the signal; the web host does not need it.
Contrast `IHasPairedDeviceProbe`, which lives in shared `Sorcha.UI.Components.User` because the web
nag-banner also uses it. Keep the new surface minimal.

## Pattern references (existing code to mirror)

- Endpoint shape, auth, OpenAPI metadata, `ResolveCitizenContextAsync`:
  `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs`.
- Typed-client + handler chain registration:
  `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs` (device-probe block,
  `BearerTokenHandler` + `ServerClockHandler`).
- Web handoff: `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor` `GoToWebSignup`.
- DTO sibling: `src/Common/Sorcha.CitizenWallet.Abstractions/Models/HasAnyDeviceResponse.cs`.
- bUnit fixture: `tests/Sorcha.Wallet.Pwa.Tests/Components/OfflineBannerTests.cs`
  (`ComponentTestFixture`); provider-host pattern at
  `tests/Sorcha.UI.Core.Tests/TestHosts/GuidedTourHost.razor`.

# Tasks: Wallet-aware PairingTakeover

**Feature**: 149-pwa-pairing-takeover-wallet-aware
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Approach**: Test-driven (tests written before/with implementation per task).

All three user stories are P0 and converge on the same `PairingTakeover` component, so the shared
plumbing (DTO + endpoint + probe) is foundational. US1 delivers the new walletless routing (MVP);
US2 and US3 are guarded by regression/behaviour tests on the same component edit.

---

## Phase 1: Setup

- [ ] T001 Confirm branch `149-pwa-pairing-takeover-wallet-aware` is checked out and the solution
  builds clean: `dotnet build src/Services/Sorcha.Wallet.Service` and
  `dotnet build src/Apps/Sorcha.Wallet.Pwa` succeed before changes.

---

## Phase 2: Foundational (blocking prerequisites for all user stories)

**The DTO, endpoint, and probe are shared by every user story and must land first.**

- [ ] T002 [P] Add `WalletExistsResponse` record (`bool HasWallet`, XML docs, license header) in
  `src/Common/Sorcha.CitizenWallet.Abstractions/Models/WalletExistsResponse.cs`, modelled on
  `HasAnyDeviceResponse.cs`.
- [ ] T003 Map `GET /api/v1/wallet/exists` on the existing `/api/v1/wallet` group in
  `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs`: a `WalletExists`
  handler that calls `ResolveCitizenContextAsync` and returns
  `Results.Ok(new WalletExistsResponse { HasWallet = walletAddress is not null })` (always 200 for
  an authenticated consumer). Add `.WithName("CitizenWalletExists")` / `.WithSummary` /
  `.WithDescription` / `.Produces<WalletExistsResponse>(200)` / `.Produces(401)`. Depends on T002.
- [ ] T004 [P] Add the one-shot probe interface `IHasWalletProbe`
  (`Task<bool> HasWalletAsync(CancellationToken ct = default)`, XML doc explaining the one-shot /
  terminal-state rationale, license header) in
  `src/Apps/Sorcha.Wallet.Pwa/Services/Wallet/IHasWalletProbe.cs`.
- [ ] T005 [P] Write `HasWalletProbeTests` in
  `tests/Sorcha.Wallet.Pwa.Tests/Services/HasWalletProbeTests.cs` (mocked `HttpMessageHandler`,
  `EnrolmentServiceTests` style): `200 {hasWallet:true}` → `true`; `200 {hasWallet:false}` →
  `false`; transient failure (throw / 500 / empty body) → `true` (fail-safe). Tests fail until T006.
- [ ] T006 Implement `HasWalletProbe` (typed `HttpClient`, `GET /api/v1/wallet/exists`, fail-safe
  returns `true` on `HttpRequestException`/timeout/non-2xx/empty body, structured `ILogger`
  warnings, license header) in `src/Apps/Sorcha.Wallet.Pwa/Services/Wallet/HasWalletProbe.cs`.
  Make T005 green. Depends on T004.
- [ ] T007 Register the probe typed-client in
  `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`
  (`AddHttpClient<IHasWalletProbe, HasWalletProbe>` with `gatewayBaseAddress` +
  `BearerTokenHandler` + `ServerClockHandler`, mirroring the `IHasPairedDeviceProbe` block).
  Depends on T006.

**Checkpoint:** endpoint + probe exist and probe unit tests pass; PWA + service build clean.

---

## Phase 3: User Story 1 — Walletless citizen routed to web wallet creation (P0) — MVP

**Goal:** A walletless signed-in citizen sees a create-wallet state and is routed to web
`/wallets/create` instead of dead-ending. **Independent test:** bUnit render with device probe
`false` + wallet probe `false` shows the create-wallet body and not the enrol button.

- [ ] T008 [US1] Write `PairingTakeoverTests` walletless case in
  `tests/Sorcha.Wallet.Pwa.Tests/Components/PairingTakeoverTests.cs` (`ComponentTestFixture`,
  provider-host pattern as needed): device probe `false` + injected fake `IHasWalletProbe` →
  `false` renders `data-testid="pairing-takeover-create-wallet"` and asserts
  `pairing-takeover-primary-button` (enrol) is **absent**. Fails until T009.
- [ ] T009 [US1] Edit `src/Apps/Sorcha.Wallet.Pwa/Components/PairingTakeover.razor`: inject
  `IHasWalletProbe`; add `bool? _hasWallet`; after the device probe resolves to `false`, await
  `HasWalletAsync` once and store it; render the new create-wallet body (headline "Create your
  wallet first", the agreed subhead, a `data-testid="pairing-takeover-create-wallet-button"`
  primary button calling `GoToWebWalletCreation()`, no short-code panel) when `_hasWallet == false`.
  Make T008 green. Depends on T007.
- [ ] T010 [US1] Add `GoToWebWalletCreation()` to `PairingTakeover.razor` `@code` — inject
  `NavigationManager`, force-load `{origin}/wallets/create` (absolute origin via
  `new Uri(Nav.BaseUri).GetLeftPart(UriPartial.Authority)`, `forceLoad: true`), mirroring
  `SignIn.razor` `GoToWebSignup`. Covered by the US1 component test's button-presence assertion.

**Checkpoint:** US1 independently demonstrable — walletless render routes to web create.

---

## Phase 4: User Story 2 — Citizen with a wallet keeps the pair flow (P0)

**Goal:** No regression to the existing pair experience. **Independent test:** bUnit render with
device probe `false` + wallet probe `true` shows the existing pair body.

- [ ] T011 [US2] Add `PairingTakeoverTests` has-wallet case: device probe `false` + fake probe
  `true` renders `pairing-takeover-primary-button` and the short-code panel, and the create-wallet
  body is **absent**. (Implementation already satisfied by T009's state machine; this is the
  regression guard.) Depends on T009.
- [ ] T012 [US2] Add `PairingTakeoverTests` has-device case: device probe `true` renders nothing
  (`pairing-takeover` node absent) — guards the unchanged "already paired here" path. Depends on
  T009.

**Checkpoint:** existing pair flow proven unchanged for wallet owners.

---

## Phase 5: User Story 3 — No flashing during detection (P0)

**Goal:** The overlay never shows a wrong/partial state while checks are in flight. **Independent
test:** bUnit render with `_hasWallet` unresolved shows nothing.

- [ ] T013 [US3] Add `PairingTakeoverTests` in-flight case: device probe `false` + a fake
  `IHasWalletProbe` whose `HasWalletAsync` is held pending → before completion the `pairing-takeover`
  node is absent; after completion it appears. Confirms the visibility predicate
  `HasAnyDevice == false && _hasWallet is not null`. Depends on T009.

**Checkpoint:** no-flash behaviour verified across all detection windows.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T014 [P] Doc-sync: update the F128 entry in `.claude/skills/sorcha-architecture/SKILL.md`
  (new `GET /api/v1/wallet/exists` surface + wallet-aware takeover state machine) and tick P0 #1 in
  `docs/superpowers/specs/2026-06-06-citizen-wallet-companion-roadmap.md`.
- [ ] T015 Verify guardrails: `scripts/check-no-snackbar.ps1` passes; no hard-coded `<Version>`;
  license headers on the 3 new files; nullable/no-warning Release build.
- [ ] T016 Full verification: `dotnet build` (Wallet Service + PWA + tests) warning-free and
  `dotnet test tests/Sorcha.Wallet.Pwa.Tests` green (full project run — MTP ignores `--filter` per
  repo convention). Record results for the PR description.

---

## Dependencies & order

- Phase 1 → Phase 2 → Phase 3 (US1, MVP) → Phases 4 & 5 (US2, US3 — both depend only on T009) →
  Phase 6.
- US2 (T011, T012) and US3 (T013) are independent of each other once T009 lands; can be written in
  parallel.

## Parallel opportunities

- T002 and T004 are `[P]` (different files, no interdep).
- T005 `[P]` (test file) can be written alongside T004.
- After T009: T011, T012, T013 are independent test additions to the same file — write sequentially
  to avoid edit conflicts, but they have no logic interdependencies.
- T014 `[P]` (docs) can proceed any time after the surface is settled.

## MVP scope

**Phase 2 + Phase 3 (US1)** = the shippable MVP: the walletless dead-end is removed and citizens are
routed to web wallet creation. Phases 4–5 are regression/quality guards; Phase 6 is polish/doc-sync.

## Format validation

All tasks use `- [ ] Tnnn [P?] [USn?] description + file path`; setup/foundational/polish carry no
story label; user-story tasks carry `[US1]`/`[US2]`/`[US3]`.

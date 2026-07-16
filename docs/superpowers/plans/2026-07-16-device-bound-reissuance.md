# Device-bound Credential Re-issuance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task (fresh implementer per task + two-stage review), exactly as #1195 Phase 1 was executed in this repo. Steps use checkbox (`- [ ]`) syntax. Each task lists **Anchors** the implementer must READ before coding (paths drift — grep to confirm) and writes its own literal tests from the described cases.

**Goal:** Let a citizen who was assured on the web (holder-`cnf` root credential) mint device-bound (`cnf` = device key) copies of their Assured Identity from the wallet PWA via a "Bind to device" ID-card action, capped at 3 devices with LRU eviction, so they can present standards-cleanly in person.

**Architecture:** One assurance, two bindings. The web-issued root stays holder-`cnf` (server-custody presentable). A second AIAS blueprint, driven by an in-wallet button, presents the root (proving entitlement + supplying claims) and mints a device-`cnf` copy bound to the phone's non-extractable P-256 key. A `DeviceBoundCredentialPolicy` in the issuance service enforces max-3 with status-list eviction. The wallet selects which credential to present per surface.

**Tech Stack:** .NET 10 / C# 14, Blazor WASM (PWA), SD-JWT VC + OID4VCI/OID4VP, IETF Token Status List (F114), Sorcha blueprints.

**Design:** `docs/superpowers/specs/2026-07-16-device-bound-reissuance-design.md` — READ IT FIRST.

## Global Constraints

- License header (`// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`) on every new `.cs` file; file-scoped namespaces (except `.razor`).
- Components.User `RootNamespace` is `Sorcha.UI.Core` regardless of folder.
- NEVER `git add -A` — the tree carries untracked storyboard work (`walkthroughs/_storyboards/`, `StoryboardWalkthroughTests.cs`, a modified `.gitignore`) that MUST stay untracked. Stage explicit paths only.
- Never hardcode `<Version>`. WASM code = BCL only (no Newtonsoft). `JsonElement` (not `JsonNode`) with JsonSchema.Net.
- MTP test runner: `--filter` does NOT isolate — run the whole project, read the totals; `dotnet build` before `dotnet test`; capture baselines before changing tests. `dotnet test` takes ONE project.
- Do NOT weaken security: no self-minting tokens, no making auth-gated endpoints anonymous. Do NOT re-introduce delegation onto the presentation path.
- Branch: `feature/device-bound-reissuance` (already exists, carries the spec+plan). Stay on it; no push/merge until the whole plan is green and reviewed.

---

### Task 1: Phase-1 correction — root credential back to holder-`cnf`

**Files:**
- Modify: `demos/AIAS/blueprints/aias-assured-identity.template.json` (the `holderKeys` starting-action field)

**Interfaces:**
- Produces: the AIAS **apply** blueprint issues a holder-`cnf` root again (`format: "sorcha-holder-key"`), `holderKeySourceField: "/holderKeys/holderJwk"` unchanged.

**Anchors:** the `holderKeys` property (~line 118) currently reads `"format": "sorcha-device-key"` + `"x-device-key"` (from #1197). The issuance config `holderKeySourceField` at ~line 187 stays.

- [ ] **Step 1:** Change the `holderKeys` field `format` back to `"sorcha-holder-key"` and `x-device-key` → `x-holder-key`; restore the holder-oriented title/description (bound to your wallet holder key, web-issued). Leave `holderKeySourceField` as-is.
- [ ] **Step 2:** Validate JSON: `python3 -c "import json;json.load(open('demos/AIAS/blueprints/aias-assured-identity.template.json'));print('ok')"` → `ok`.
- [ ] **Step 3:** Confirm no other checked-in AIAS apply copy diverges: `grep -rn "sorcha-device-key" demos/AIAS/` returns nothing (the device-key format now belongs only to Task 2's blueprint).
- [ ] **Step 4: Commit**

```bash
git add demos/AIAS/blueprints/aias-assured-identity.template.json
git commit -m "fix: [#1195] AIAS apply blueprint root back to holder-cnf (web-issued); device binding moves to enrolment"
```

---

### Task 2: The device-registration blueprint

**Files:**
- Create: `demos/AIAS/blueprints/aias-device-registration.template.json`
- Test: add a publish/validate assertion where the AIAS demo blueprints are tested (grep `aias-assured-identity` under `tests/` and `demos/AIAS/` for the existing publish/validate harness; co-locate).

**Interfaces:**
- Produces: a blueprint whose starting action (a) gates on presenting an `AssuredIdentityCredential` and (b) captures a device public JWK at `/deviceKey/holderJwk` (via `format: "sorcha-device-key"`), and whose issuance action mints a device-`cnf` `AssuredIdentityCredential` with `holderKeySourceField: "/deviceKey/holderJwk"`, `recipientParticipantId: "citizen"`, claims sourced from the verified presentation (Task 3).

**Anchors:** mirror `demos/AIAS/blueprints/aias-assured-identity.template.json` for participant/action/issuance shape. Use the `blueprint-builder` skill's **credential-bootstrapped application** pattern (`credentialRequirements` on the open starting action) and **Open Participants** rules (citizen `walletAddress` OMITTED).

- [ ] **Step 1:** Author the blueprint JSON. Participants: `citizen` (walletAddress omitted, organisation Public), `aias-issuer` (walletAddress from the demo's AIAS wallet, templated like the apply blueprint). Starting action `id:1`, `isStartingAction:true`, `sender:"citizen"`:
  - `credentialRequirements`: `[{ "type": "AssuredIdentityCredential", "presentationSource": "SorchaLocalWallet", "requiredClaims": [ {"claimName":"givenName"}, {"claimName":"familyName"}, {"claimName":"dateOfBirth"} ] }]` (verify the exact `presentationSource` enum value the engine accepts for an internally-held Sorcha credential — grep `presentationSource` / `PresentationSource` in `Sorcha.Blueprint.*`).
  - `dataSchemas`: one object with a single `deviceKey` property, `"format": "sorcha-device-key"`, `"x-device-key": { "required": true }`. No other user fields.
  - `routes`: `[{ "id":"to-issue", "nextActionIds":[2], "isDefault":true }]`.
  - Issuance action `id:2`, `sender:"aias-issuer"`, `requiredPriorActions:[1]`, with `credentialIssuanceConfig`: `credentialType:"AssuredIdentityCredential"`, `vct:"https://sorcha.dev/vc/assured-identity/v1"`, `displayName:"Assured Identity"`, `targetAudience:"SorchaLocalWallet"`, `recipientParticipantId:"citizen"`, `holderKeySourceField:"/deviceKey/holderJwk"`, `issuanceCondition` always-true, and `claimMappings` sourcing from the presented credential's verified claims (Task 3 defines the source-pointer prefix; use it here).
- [ ] **Step 2:** Validate JSON parses (as Task 1 Step 2).
- [ ] **Step 3:** Write a test that the blueprint **publishes without validation errors** (mirror the existing AIAS apply-blueprint publish test; assert no `VAL_BP_*` errors, open citizen participant accepted, credentialRequirement + device-key field present). Run the project; expect pass.
- [ ] **Step 4: Commit** (stage the blueprint + the test file explicitly).

---

### Task 3: Engine — source issuance claims from the verified presentation

**Files:**
- Modify: the issuance path that binds `credentialIssuanceConfig.claimMappings` to a source document — `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` (the credential-bootstrapped gate + issuance) and/or the wallet-service direct-issue path (`src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`, `IssueCredentialChainResolver.cs`, `SdJwtClaimProjection.cs`).
- Test: `tests/Sorcha.Blueprint.Service.Tests/...` (co-locate with the existing credential-bootstrapped / issuance tests — grep `credentialRequirements` in tests).

**Interfaces:**
- Consumes: the verified presentation result from the starting action's `credentialRequirement` gate (the same `VerifiedClaims` the Phase-1 `HaipPresentationVerifier`/internal verifier produced — grep `VerifiedClaims` in `Sorcha.Verifier.Engine` / `Sorcha.Blueprint.*`).
- Produces: the issuance source document exposes the verified presentation's claims under a stable pointer prefix (propose `/presentedCredential/*`) so `claimMappings` `sourceField` like `/presentedCredential/givenName` resolves. Document the exact prefix in this task and reuse it in Task 2.

**Anchors:** READ how `claimMappings.sourceField` pointers are resolved today (the issuance source doc assembly — grep `claimMappings` / `sourceField` / `ResolveClaimSource`). The credential-bootstrapped gate already verifies the presentation; find where its result is dropped and thread the verified claims into the source doc.

- [ ] **Step 1: Failing test** — an issuance action whose blueprint has a `credentialRequirement` and `claimMappings` with `sourceField: "/presentedCredential/givenName"` mints a credential whose `givenName` claim equals the value from the **verified presented** credential (not from the submitted payload). Assert the payload cannot override it (submit a different `givenName` in the payload → issued claim still equals the presented value).
- [ ] **Step 2:** Run → FAIL (`/presentedCredential/*` not resolvable today).
- [ ] **Step 3:** Implement: capture the starting action's verified-presentation claims and expose them in the issuance source document under `/presentedCredential/*`; ensure they take precedence over client payload for identity claims. Keep it minimal and additive.
- [ ] **Step 4:** Run → PASS. Run the whole test project; confirm no regression.
- [ ] **Step 5: Commit** (explicit paths).

---

### Task 4: `DeviceBoundCredentialPolicy` — count + LRU eviction

**Files:**
- Create: `src/Services/Sorcha.Wallet.Service/Services/Implementation/DeviceBoundCredentialPolicy.cs` (+ `IDeviceBoundCredentialPolicy` interface in the service's `Services/Interfaces/`). (Confirm the wallet service is where device-`cnf` AIAS creds are minted — per project memory the live issuance path is the Wallet Service direct-issue; if the mint actually lands in Blueprint.Service, create the policy there instead and adjust Task 5.)
- Test: `tests/Sorcha.Wallet.Service.Tests/...` (co-locate with credential-issuance tests).

**Interfaces:**
- Produces:
  ```csharp
  public interface IDeviceBoundCredentialPolicy
  {
      /// Called BEFORE minting a device-bound copy. Enforces max-3 per (user, credentialType)
      /// keyed on device-key JWK thumbprint. Returns the disposition; performs eviction
      /// (status-list revoke + inbox notify) as a side-effect when a NEW device exceeds the cap.
      Task<DeviceBindDisposition> ReconcileAsync(
          Guid userId, string credentialType, string deviceKeyThumbprint, CancellationToken ct);
  }
  public enum DeviceBindKind { NewWithinCap, ReplaceExisting, NewWithEviction }
  public sealed record DeviceBindDisposition(DeviceBindKind Kind, string? EvictedCredentialId);
  ```
- Consumes: a store of live device-bound copies per user (grep the credential store — `wallet."Credentials"` / `ICredentialStore` / `ICredentialRepository`), the JWK thumbprint helper (RFC 7638 — grep `Thumbprint` in `Sorcha.Cryptography`), the status-list revoke API (grep `Revoke` in `CredentialDisplay.cs`, `CredentialCommands.cs`, and the wallet-service status-list service from F114), and the F118 inbox writer (`CitizenDeviceInboxWriter` per CLAUDE.md pattern #12).

**Anchors:** F114 status-list publisher/worker + the revoke entrypoint; `CitizenDeviceInboxWriter`; RFC 7638 thumbprint util. `MAX_DEVICES = 3`.

- [ ] **Step 1: Failing tests** (one class, cases):
  - `ReconcileAsync` with 2 existing distinct thumbprints + a new one → `NewWithinCap`, no eviction, no revoke called.
  - With 3 existing + the SAME thumbprint as one → `ReplaceExisting`, no eviction, count unchanged.
  - With 3 existing distinct + a NEW thumbprint → `NewWithEviction`, `EvictedCredentialId` == the oldest (by issued-at); status-list revoke invoked for it; inbox notify invoked for the evicted device.
  - Revoke throws → `ReconcileAsync` propagates (issuance must abort); assert no partial state.
- [ ] **Step 2:** Run → FAIL (types absent).
- [ ] **Step 3:** Implement the policy: load live device-bound copies for `(userId, credentialType)`; thumbprint match → ReplaceExisting; else if count < 3 → NewWithinCap; else evict oldest (revoke via status list, then inbox notify) → NewWithEviction. Revoke-before-return; on revoke failure, throw.
- [ ] **Step 4:** Run → PASS; whole project green.
- [ ] **Step 5: Commit** (explicit paths).

---

### Task 5: Wire the policy into the device-copy issuance path

**Files:**
- Modify: the mint entrypoint for device-bound AIAS copies (`src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs` or the resolver from Task 4's investigation).
- Register `IDeviceBoundCredentialPolicy` in the wallet service DI + `IStorageRegistrationLog` if it holds state (follow CLAUDE.md pattern #10/#13 if it introduces a repository).
- Test: `tests/Sorcha.Wallet.Service.Tests/...` integration.

**Interfaces:**
- Consumes: `IDeviceBoundCredentialPolicy.ReconcileAsync` (Task 4).
- Produces: device-`cnf` AIAS issuance calls `ReconcileAsync(userId, "AssuredIdentityCredential", thumbprint(deviceJwk))` **before** minting; a `ReplaceExisting` disposition revokes/replaces the prior copy for that thumbprint; a thrown reconcile aborts the mint (no credential issued).

**Anchors:** identify how the mint knows it is a *device-bound* copy vs the web root — distinguish by `cnf` being a device (P-256, `crv:"P-256"`) vs holder (Ed25519) key, or by the blueprint/credentialType context. Only device-bound copies go through the policy; the web root does NOT.

- [ ] **Step 1: Failing test** — minting a device-bound AIAS copy invokes `ReconcileAsync`; a 4th distinct device triggers eviction end-to-end (assert the oldest copy's status is revoked and the new one is issued); minting the holder-`cnf` web root does NOT call the policy.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement the wiring (compute thumbprint, call policy, honour disposition, abort on throw). Register DI.
- [ ] **Step 4:** Run → PASS; whole project green.
- [ ] **Step 5: Commit** (explicit paths).

---

### Task 6: PWA "Bind to device" ID-card action + flow

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/Services/DeviceBindingService.cs` (+ interface) — orchestrates capture→present→submit→cache.
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Pages/CredentialDetail.razor` (add the button on the AIAS root card) — confirm this is the ID-card surface; `CredentialDetailView.razor` in Components.User may be the shared render.
- Test: `tests/Sorcha.Wallet.Pwa.Tests/...` (bUnit for the button visibility; a service test for the flow with mocked deps).

**Interfaces:**
- Consumes: `IDeviceKeyService.GetPublicJwkAsync()` (device public JWK), `IPresentationEngine` (present the root server-custody), the device-registration blueprint submission client (the same client the PWA uses to submit blueprint actions — grep how the PWA submits actions today; if none exists in the PWA, this is the new-surface cost noted in the design — add a minimal submit call to the blueprint service), `ICredentialCache` (cache the returned copy).
- Produces:
  ```csharp
  public interface IDeviceBindingService
  {
      /// True when `credential` is an AssuredIdentityCredential root (holder-cnf) and THIS device
      /// has no live device-bound copy of it yet.
      bool CanBind(CachedCredential credential);
      /// Captures the device key, presents the root, submits the device-registration blueprint,
      /// caches the returned device-cnf copy. Throws on gate/mint failure (surface inline).
      Task<CachedCredential> BindToThisDeviceAsync(CachedCredential root, CancellationToken ct);
  }
  ```

**Anchors:** `CredentialDetail.razor` for the button host; `IDeviceKeyService` (PWA); `PresentationEngine`/`IPresentationEngine`; `ICredentialCache`; the Phase-1 `sorcha-device-key` capture pattern (reuse the JWK shape). Feedback via `IInlineFeedback` (CLAUDE.md pattern #12) — NOT `ISnackbar`.

- [ ] **Step 1: Failing tests** — `CanBind` true for an AIAS holder-`cnf` root with no device copy, false when a device copy already exists or the credential isn't an AIAS root; `BindToThisDeviceAsync` (mocked deps) calls capture→present→submit→cache in order and returns the cached device copy; a present/gate failure throws and caches nothing.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement the service + wire the button on the card (visible per `CanBind`; on click runs `BindToThisDeviceAsync`, shows inline success/error, refreshes the card). Register DI in the PWA.
- [ ] **Step 4:** Run PWA test project → PASS; whole project green.
- [ ] **Step 5: Commit** (explicit paths).

---

### Task 7: Wallet presentation selection per surface

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs` (candidate selection).
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/Presentation/...` (extend `PresentationEngineTests.cs`).

**Interfaces:**
- Consumes: the credential cache (root + device copies), a signal of the present surface (in-person/offline/device vs web/remote — determine what's available; if the present request or transport already carries this, use it; else default: prefer a device-`cnf` copy this device can sign for, fall back to the holder-`cnf` root).
- Produces: `MatchCandidates`/selection returns the device-`cnf` copy when this device holds one and can sign; otherwise the holder-`cnf` root (server-custody). When an offline/in-person present is requested and no device copy exists → a distinct "bind this device first" outcome the UI can route to Task 6.

**Anchors:** the existing candidate matching (`MatchCandidates` in `PresentationEngine.cs`), how a credential's `cnf` key type is known (device P-256 vs holder Ed25519 — read from the cached SD-JWT `cnf.jwk.crv`/`kty`), and how the current present flow decides signing (Phase-1 `Present.razor` device-signs; server-custody signing path for the root — grep the wallet-service KB-JWT signing endpoint).

- [ ] **Step 1: Failing tests** — with both a root and a this-device copy cached, selection for a device/in-person present returns the device copy; for a web/remote present returns the root; with only a root cached and an offline present requested, selection yields the "bind first" outcome.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement the selection rule (minimal, driven by `cnf` key type + surface). Do NOT reintroduce delegation.
- [ ] **Step 4:** Run → PASS; whole project green.
- [ ] **Step 5: Commit** (explicit paths).

---

### Task 8: Verifier / E2E parity

**Files:**
- Test: extend `tests/Sorcha.Haip.Service.Tests/Endpoints/VerifierEndpointTests.cs` (reuse `DeviceCnfPresentationFactory` from Phase 1) and/or a wallet-service test for server-custody root presentation.

**Interfaces:**
- Consumes: the Phase-1 device-cnf harness (`DeviceCnfPresentationFactory`, `HandleDirectPost`, `HaipPresentationVerifier`).

- [ ] **Step 1:** Add a test asserting a **device-`cnf`** AIAS copy (device-signed KB-JWT) verifies through `HandleDirectPost` (this largely exists from Phase 1 Task 4 — extend it to the AIAS `vct`/claim set to prove the real credential shape, not just a generic license cred).
- [ ] **Step 2:** Add a test that a **holder-`cnf`** root presented **server-custody** (wallet service signs the KB-JWT) verifies through the standard verifier — proving the root's web/remote path. If server-custody signing isn't unit-testable in isolation, assert at the `HaipPresentationVerifier` level that a holder-`cnf` credential with a holder-key-signed KB-JWT verifies (`HolderKeyVerified == true`).
- [ ] **Step 3:** Run the Haip (and wallet) test project(s) → PASS.
- [ ] **Step 4: Commit** (explicit paths).

---

## Self-Review (author)

- **Spec coverage:** §3→Task 1; §4 blueprint→Task 2; §4.1 claim source→Task 3; §5 cap/eviction→Tasks 4–5; §4.2 button/flow→Task 6; §6 selection→Task 7; §9 verifier→Task 8. All spec sections mapped.
- **Ordering:** 1 (correction) → 2 (blueprint) → 3 (claim piping, needed by 2's issuance) → 4 (policy) → 5 (wire policy) → 6 (PWA flow) → 7 (selection) → 8 (verifier). Tasks 3 and 4 are independent and could parallelise; keep serial under SDD for a clean checkout (one implementer at a time, per repo rule).
- **Known investigation points (not placeholders — anchored):** exact `presentationSource` enum (Task 2), the claim-source pointer prefix `/presentedCredential/*` (Task 3, defined there + reused in 2), which service mints device copies (Task 4/5), the present-surface signal (Task 7). Each names where to look; the implementer confirms by grep before coding — the proven #1195 pattern.
- **Post-plan:** after all 8 green + reviewed, this is #1195 Phase 2 — open a PR, merge on green (flaky-review-only red OK), deploy client + wallet-service images to n1, then the on-phone verify (apply on web → Bind to device in the wallet → present in person → verify).

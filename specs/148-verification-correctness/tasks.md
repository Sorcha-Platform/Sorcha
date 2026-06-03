---
description: "Task list for Verification-correctness"
---

# Tasks: Verification-correctness

**Input**: Design documents from `/specs/148-verification-correctness/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/verification-behaviour.md, quickstart.md

**Tests**: TDD is REQUIRED (per spec + design). Each story writes its behaviour tests before the behaviour implementation. (For US1, the additive carrier type is created first so the tests compile — that is a compile prerequisite, not behaviour.)

**Organization**: Grouped by user story. Stories map 1:1 to the findings (US1=H3, US2=M3a, US3=M3b) and are independent. Delivery is one PR with one commit per story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3

## Path Conventions

`Sorcha.Verifier.Engine` (shared) + `Sorcha.Wallet.Pwa` (US1), `Sorcha.Tenant.Service` (US2), `Sorcha.Wallet.Service` (US3); tests in the matching `*.Tests` projects.

---

## Phase 1: Setup

**Purpose**: Establish a green TDD baseline. No project initialization (existing solution).

- [x] T001 Establish baseline: build `tests/Sorcha.Verifier.Tests`, `tests/Sorcha.Wallet.Pwa.Tests`, `tests/Sorcha.Tenant.Service.Tests`, `tests/Sorcha.Wallet.Service.Tests` to confirm they compile and pass before changes (MTP runs whole projects; `--filter` ignored).

---

## Phase 2: Foundational (Blocking Prerequisites)

**None.** Each story is self-contained; the shared `VerificationOutcome` change is part of US1. User stories may begin immediately after Setup.

---

## Phase 3: User Story 1 - The citizen is told the truth about what was verified (Priority: P1) 🎯 MVP

**Goal**: H3 — the offline device verifier reports an explicit issuer-signature status; an accepted-but-issuer-unverified result surfaces as `Warn` (not `Pass`); server verifiers unchanged.

**Independent Test**: Validator returns `NotVerified` (still `Accepted`) on the unresolved-key/`requireIssuerSignature:false` path and `Verified` when the key resolves + JWS checks; `RealVerifierEngine` maps the former to `Warn` and the latter to `Pass`. See `contracts/verification-behaviour.md` (H3 table).

### Implementation prerequisite (additive carrier type — lets tests compile)

- [x] T002 [US1] Add `IssuerSignatureStatus` enum (`Verified` / `NotVerified`) and a non-required `VerificationOutcome.IssuerSignature` property (default `NotVerified`) in `src/Common/Sorcha.Verifier.Engine/Models/VerifierSession.cs`.

### Tests for User Story 1 (write before the behaviour impl, must FAIL) ⚠️

- [x] T003 [P] [US1] Validator status tests in `tests/Sorcha.Verifier.Tests/Services/VerifiablePresentationValidatorTests.cs`: unresolved key + `requireIssuerSignature:false` → `Accepted==true` AND `IssuerSignature==NotVerified`; resolved key + valid JWS → `IssuerSignature==Verified`; unresolved key + `requireIssuerSignature:true` → `Accepted==false` (unchanged). (Fails: validator does not yet set `Verified`.)
- [x] T004 [P] [US1] PWA mapping test in `tests/Sorcha.Wallet.Pwa.Tests/Services/Verification/RealVerifierEngineTests.cs`: `Accepted && NotVerified` → `VerifyOutcome.Warn` with an issuer-not-verified message; `Accepted && Verified` → `Pass`; `!Accepted` → `Fail`. (Fails: `Map` currently returns `Pass` for any accepted outcome.)

### Implementation for User Story 1

- [x] T005 [US1] In `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs`: track an `issuerSignatureVerified` flag (set true in the resolved-key + valid-JWS branch, ~`:181-188`) and set `IssuerSignature = Verified|NotVerified` on the success outcome (~`:275`). (Depends on T002.)
- [x] T006 [US1] In `src/Apps/Sorcha.Wallet.Pwa/Services/Verification/RealVerifierEngine.cs` `Map`: map `Accepted && IssuerSignature==NotVerified` → `VerifyOutcome.Warn` + an "issuer not verified — offline / reduced assurance" message; keep `Pass` for `Verified`. (Depends on T002.)
- [x] T007 [US1] Document the offline reduced-assurance behaviour as a deliberate scoped exception in the PWA README (`src/Apps/Sorcha.Wallet.Pwa/README.md` or nearest doc). (FR-006.)
- [x] T008 [US1] Build + run `tests/Sorcha.Verifier.Tests` and `tests/Sorcha.Wallet.Pwa.Tests` to green; commit `fix(148): H3 surface issuer-signature status — PWA verifier no longer silently accepts`.

**Checkpoint**: The device verifier is honest; server verifiers unchanged.

---

## Phase 4: User Story 2 - Social-login identity tokens are cryptographically verified (Priority: P1)

**Goal**: M3a — verify the ID token's JWS signature against the provider's JWKS before trusting claims; fail-closed; keep iss/aud/exp/nonce.

**Independent Test**: A token signed by the test JWKS key passes; tampered/wrong-key/unsigned tokens and JWKS-fetch failure are rejected; existing claim checks still enforced. See `contracts/verification-behaviour.md` (M3a table).

### Tests for User Story 2 (write first, must FAIL) ⚠️

- [x] T009 [P] [US2] Signature-validation tests in `tests/Sorcha.Tenant.Service.Tests/Services/OidcExchangeServiceTests.cs`: inject a test signing-key set; assert a validly-signed token passes; tampered/wrong-key/unsigned → reject; key-source failure → reject (fail-closed); iss/aud/exp/nonce still enforced. (Fails: signature is not validated yet.)

### Implementation for User Story 2

- [x] T010 [US2] Add an injectable signing-key resolver seam (e.g. `IOidcSigningKeyResolver` returning `IEnumerable<SecurityKey>` for an `IdentityProviderConfiguration`) with a production impl that fetches + caches the JWKS (`JsonWebKeySet` from `config.JwksUri`, discovery fallback via `MetadataUrl`/`DiscoveryDocumentJson`/`{IssuerUrl}/.well-known/openid-configuration`, refresh-once on `kid` miss) under `src/Services/Sorcha.Tenant.Service/Services/`, using the existing `IHttpClientFactory`.
- [x] T011 [US2] In `src/Services/Sorcha.Tenant.Service/Services/OidcExchangeService.cs` `ValidateIdTokenAsync`: make it genuinely async; before trusting claims, verify the ID-token JWS signature against the resolved keys (`RequireSignedTokens`, `ValidateIssuerSigningKey`; signature-only), fail-closed on invalid/unmatched/unobtainable; keep the existing iss/aud/exp/nonce checks; remove the misleading TODO. (Depends on T010.)
- [x] T012 [US2] Build + run `tests/Sorcha.Tenant.Service.Tests` to green; commit `fix(148): M3a validate OIDC ID-token signature against provider JWKS`.

**Checkpoint**: Social-login trusts only cryptographically-verified identity tokens.

---

## Phase 5: User Story 3 - Disabled recovery cannot be switched on with broken verification (Priority: P2)

**Goal**: M3b — the gated-off passkey + org recovery paths fail loudly at the unverified unwrap point.

**Independent Test**: With recovery enabled, each recovery path throws `NotSupportedException` without re-keying. See `contracts/verification-behaviour.md` (M3b table).

### Tests for User Story 3 (write first, must FAIL) ⚠️

- [ ] T013 [P] [US3] Fail-loud tests in `tests/Sorcha.Wallet.Service.Tests/Services/PasskeyRecoveryServiceTests.cs` and `OrgRecoveryServiceTests.cs`: invoking the recovery path (feature enabled) throws `NotSupportedException` and does not mutate wallet state. (Fails: services currently re-key without proof.)

### Implementation for User Story 3

- [ ] T014 [US3] Throw `NotSupportedException` (clear message naming the missing proof + the feature flag) at the unverified unwrap point in `src/Services/Sorcha.Wallet.Service/Services/Implementation/PasskeyRecoveryService.cs` (~`:83`) and `OrgRecoveryService.cs` (~`:82`), before any wallet state mutation.
- [ ] T015 [US3] Build + run `tests/Sorcha.Wallet.Service.Tests` to green; commit `fix(148): M3b fail loud on unverified passkey/org recovery`.

**Checkpoint**: All three findings closed.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T016 [P] Doc sync: `verifiable-credentials` skill (note the `VerificationOutcome.IssuerSignature` status + the PWA offline reduced-assurance exception) and `docs/guides/AUTHENTICATION-SETUP.md` (OIDC ID-token JWKS signature validation). Record the backlog items (online issuer verification; §5.1 two-stack consolidation; full WebAuthn/org-signature recovery) where the initiative tracks them.
- [ ] T017 Push branch `148-verification-correctness`; open PR (`gh pr create`) referencing the design + spec; confirm `claude-review` is green (full-solution `build-and-test`/`test` stay red on the unrelated Refit-cert / Playwright infra issues — claude-review is the gate); merge on green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: none.
- **User Stories (Phases 3-5)**: each depends only on Setup. US1 touches `Verifier.Engine` + PWA; US2 Tenant; US3 Wallet — fully independent across services and parallelizable.
- **Polish (Phase 6)**: after all stories.

### Within US1

- T002 (carrier type) before T003/T004 (tests compile) before T005/T006 (behaviour) — T005 and T006 are independent of each other once T002 lands.

### Parallel Opportunities

- US1 / US2 / US3 are independent and can proceed in parallel (different services).
- Within a story, the `[P]` test tasks run together.

---

## Implementation Strategy

### MVP (highest-impact first)

1. Phase 1 Setup.
2. Phase 3 US1 (H3) — the citizen-facing honesty fix. STOP and validate.
3. Phase 4 US2 (M3a) — live-path signature verification.
4. Phase 5 US3 (M3b) — the latent-footgun guard.
5. Phase 6 — doc sync + PR.

### Delivery

One PR (`148-verification-correctness`), one commit per story (T008, T012, T015), each built + tested against its affected test project(s) before commit. Merge on green `claude-review`.

---

## Notes

- `[P]` = different files, no dependency on an incomplete task.
- MTP runs whole test projects; do not rely on `--filter`.
- H3 is additive — server verifiers (`requireIssuerSignature:true`) keep `Accepted ⇒ IssuerSignature==Verified`, no behaviour change.
- M3a stays fail-closed; M3b must not change the disabled-by-default gate.

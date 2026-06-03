# Tasks: Re-anchor org VC-issuer DID to the operational wallet (+ fail-closed issuance)

**Feature**: `149-vc-issuer-did-reanchor` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Tests**: included (Constitution §IV + spec Success Criteria SC-005 request them; TDD per story).

## Conventions

- `[P]` = parallelizable (different files, no incomplete dependency).
- `[US1|US2|US3]` = the user story a task serves.
- Each task names exact files. License header + XML docs + `.WithSummary()/.WithDescription()` on every new endpoint (Constitution §III, §V).

---

## Phase 1: Setup

- [X] T001 Create the client folder `src/Common/Sorcha.ServiceClients.Http/OrgInfo/` and confirm the touched test projects build (`Sorcha.Wallet.Service.Tests`, `Sorcha.Tenant.Service.Tests`, `Sorcha.Blueprint.Service.Tests`) so subsequent tasks compile against a green baseline.

---

## Phase 2: Foundational (blocking prerequisites for the user stories)

- [X] T002 [P] Add `Task<OrgDidDocument?> GetByPrimaryDidAsync(string did, CancellationToken)` to `src/Services/Sorcha.Tenant.Service/Services/OrgDidDocumentService.cs` (single indexed lookup on `PrimaryDid`; the doc `id` is already the DID — opaque).
- [X] T003 [P] Add internal endpoint `GET /api/internal/orgs/{orgId:guid}/wallet-address` to `src/Services/Sorcha.Tenant.Service/Endpoints/InternalEndpoints.cs` (`RequireService`); returns `{ walletAddress }` from `Organization.WalletAddress`, **404 when org missing or `WalletAddress` is null** (per `contracts/org-wallet-address.internal.md`).
- [X] T004 Add public endpoint `GET /orgs/by-did/{did}/did.json` to `src/Services/Sorcha.Tenant.Service/Endpoints/OrgDidDocumentEndpoints.cs` (anonymous, `application/did+json`), backed by `GetByPrimaryDidAsync` (per `contracts/org-did-by-did.public.md`). Depends on T002.
- [X] T005 [P] Add `IOrgInfoClient` + `OrgInfoClient` in `src/Common/Sorcha.ServiceClients.Http/OrgInfo/` with `Task<string?> ResolveCanonicalWalletAddressAsync(Guid orgId, CancellationToken)` (GET the T003 endpoint; 200→address, 404/transport→null), mirroring `OrgDidDocumentClient`.
- [X] T006 Register `IOrgInfoClient` against the Tenant base address (`ServiceClients:TenantService:Address`) with service-principal auth in the ServiceClients DI extension that registers `IOrgDidDocumentClient`. Depends on T005.

**Checkpoint:** Tenant exposes both new routes; the Wallet Service can resolve an org's canonical wallet address and fetch a published doc by DID.

---

## Phase 3: User Story 1 — Trusted credential from a known organisation (Priority: P1) 🎯 MVP

**Goal:** A native credential's `iss`/`kid` are anchored on the org's canonical wallet A, the verifier resolves the published Tenant `did.json`, and a relying party pinning A accepts it.

**Independent test:** issue a credential for an org with a master key; verify it with trust pinned to `did:sorcha:org:{A}`; signature verifies and issuer is trusted.

### Tests (write first)

- [ ] T007 [P] [US1] Tenant test: `GET /orgs/by-did/{did}/did.json` returns the published doc for a known `PrimaryDid` and 404 for an unknown DID — `tests/Sorcha.Tenant.Service.Tests/`.
- [ ] T008 [P] [US1] Wallet test: `IssuanceKeyService.GetActiveSigningMaterialAsync` (mock `IOrgInfoClient` → A) emits `iss = did:sorcha:org:{A}`, `kid = did:sorcha:org:{A}#vc-issuance-{n}`, and the regenerate snapshot carries A while the VM JWK is the derived child C's key — `tests/Sorcha.Wallet.Service.Tests/`.
- [ ] T009 [P] [US1] Blueprint/engine test: `DidX5cIssuerKeyResolver` resolves a published doc anchored on A and verifies an **EdDSA** credential signed by C; a `did-allowlist` pinned to `did:sorcha:org:{A}` matches `iss` — `tests/Sorcha.Blueprint.Service.Tests/` (pins the OKP raw-32 key-shape, D5).

### Implementation

- [ ] T010 [US1] `src/Services/Sorcha.Wallet.Service/Services/Implementation/IssuanceKeyService.cs`: build `iss`/`kid` from A (resolved via `IOrgInfoClient`) instead of `derivedRecord.WalletAddress`; pass A as `OrgDidRegenerateRequest.WalletAddress` at every snapshot push (`GetOrDeriveAsync`, `PushDidDocumentSnapshotAsync`). C's public key stays the VM JWK. Depends on T006.
- [ ] T011 [US1] `src/Common/Sorcha.ServiceClients.Http/Did/SorchaDidResolver.cs`: `ResolveOrgDidAsync` fetches the Tenant by-DID `did.json` via the 3-arg HttpClient and parses it to `DidDocument`; **remove the hardcoded `#vc-issuance-1` local rebuild for org DIDs** (return null on 404/unreachable). Leave `did:sorcha:w:` resolution untouched. Depends on T004.
- [ ] T012 [US1] `src/Services/Sorcha.Blueprint.Service/Program.cs`: after `AddDidResolvers(...)`, override `AddScoped<SorchaDidResolver>` to the 3-arg ctor with an `HttpClient` whose base address is `ServiceClients:TenantService:Address` (reuse the `IOrgDidDocumentClient` registration pattern). Depends on T011.
- [ ] T013 [P] [US1] `src/Services/Sorcha.Haip.Service/Program.cs`: repoint the public-DID `HttpClient` base address from the Wallet Service to the Tenant Service so HAIP's resolver also reads the published `did.json` (re-anchor breaks HAIP's old by-address resolution). Depends on T011.
- [ ] T014 [US1] Confirm/adjust the engine verifier (`SdJwtVcFormatHandler` / `ISdJwtService`) consumes the OKP raw-32 `IssuerKeyResolution.PublicKey` for EdDSA issuers; fix the shape only if T009 fails. Depends on T009.

**Checkpoint:** US1 independently testable — trusted issuance + verification accept end-to-end (SC-001, SC-002).

---

## Phase 4: User Story 2 — Fail closed when no verifiable credential can be produced (Priority: P2)

**Goal:** issuance for an org with no issuance key (or no resolvable A) is refused with an actionable error; no credential is delivered.

**Independent test:** attempt issuance for a key-less org → actionable error, zero credentials.

### Tests (write first)

- [ ] T015 [P] [US2] Wallet test: mint with `issuanceMaterial == null` (no master key) returns 409/422 with an actionable message and delivers no credential — `tests/Sorcha.Wallet.Service.Tests/`.
- [ ] T016 [P] [US2] Blueprint test: a `SorchaLocalWallet` action with a key-less issuer fails the action (`[VAL_RUNTIME_CRED_002]`) and records no `IssuedCredentialId` — `tests/Sorcha.Blueprint.Service.Tests/`.

### Implementation

- [ ] T017 [US2] `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`: add a guard before the signing-material fallback (~`:598`) — `if (issuanceMaterial is null) return Results.Problem(409/422, "...provision a Feature 083 org master key (Set-SorchaOrgMasterKey)...")`; delete the `signingIssuer = … ?? walletAddress` and null-`kid` fallback (`:605-606`).
- [ ] T018 [US2] `IssuanceKeyService`: when `IOrgInfoClient` returns null A (org not provisioned), return null signing material (no derived-only fallback) so the T017 guard fires. Depends on T010.

**Checkpoint:** US2 independently testable — no unverifiable credential can be minted (SC-003).

---

## Phase 5: User Story 3 — Rotated issuance key still resolves (Priority: P3)

**Goal:** credentials signed under a rotated key verify via the published doc.

**Independent test:** issue under `#vc-issuance-1`, rotate, issue under `#vc-issuance-2`; both verify.

- [ ] T019 [P] [US3] Test: after rotation the published doc (anchored on A) lists all Active VMs and the `#vc-issuance-2` credential resolves + verifies — `tests/Sorcha.Blueprint.Service.Tests/` (+ Tenant doc assertion).
- [ ] T020 [US3] Confirm `IssuanceKeyService.RotateAsync`'s `PushDidDocumentSnapshotAsync` carries A (covered by T010's snapshot change); add the A-anchored rotation snapshot only if T019 reveals a gap. Depends on T010.

**Checkpoint:** US3 independently testable — rotation does not regress verification (SC-004).

---

## Phase 6: Polish & Cross-Cutting

- [ ] T021 [P] Observability: structured (non-interpolated) log + a counter tag for fail-closed issuance refusals and for DID-resolution source (published-doc vs unresolved) on the existing issuance/identity meter — Wallet + Blueprint services.
- [ ] T022 [P] Docs sync: update Wallet/Tenant service READMEs and `docs/reference/API-DOCUMENTATION.md` for the two new endpoints; update the `verifiable-credentials` and `sorcha-architecture` skills to record that the issuer DID is now anchored on the operational wallet A (re-anchor implemented).
- [ ] T023 Run `quickstart.md` Paths A/B/C against the local Docker stack (rebuild `tenant-service wallet-service blueprint-service haip-service`); capture evidence (issued `iss`/`kid` = A; by-DID doc; fail-closed 409; rotation).
- [ ] T024 Regression gate: `dotnet test` (Credential/Did/Issuance/Trust filters) green, `scripts/check-trust-clean-break.ps1` green, no Release warnings.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T006)** → **US1 (T007–T014)** → **US2 (T015–T018)** → **US3 (T019–T020)** → **Polish (T021–T024)**.
- Foundational blocks all stories. Within Foundational: T004 needs T002; T006 needs T005; T002/T003/T005 are parallel.
- US1 implementation order: T010 (needs T006), T011 (needs T004) → T012/T013 (need T011, parallel to each other), T014 (needs T009).
- US2 needs US1's T010 (for T018); T017 is independent of US1 but lands with the same `iss` semantics.
- US3 needs US1's T010.

## Parallel Opportunities

- Foundational: T002, T003, T005 together.
- Per story, the test tasks (T007/T008/T009; T015/T016) are `[P]` (different test projects).
- US1 host wiring: T012 (Blueprint) and T013 (HAIP) are `[P]` once T011 lands.
- Polish: T021, T022 `[P]`.

## MVP

**User Story 1 (Phase 3) + its Foundational prerequisites** is the MVP — it delivers trusted cross-org credential verification (SC-001/SC-002) and unblocks the CyberEssentialsUac scenario (with the separate walkthrough master-key PR).

## Task Count

24 tasks — Setup 1, Foundational 5, US1 8, US2 4, US3 2, Polish 4.

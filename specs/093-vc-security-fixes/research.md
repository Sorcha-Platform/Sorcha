# Phase 0 Research: Credential & Presentation Security Fixes

**Feature**: 093-vc-security-fixes
**Date**: 2026-04-09
**Purpose**: Resolve technical unknowns before Phase 1 design. Ground decisions in the actual repository state.

## Research items

Extracted from plan's Technical Context and spec's Assumptions section.

1. Status list allocation **sequencing** — who calls `IStatusListManager.AllocateIndexAsync`, and when, so the result is available before signing?
2. Behaviour when `IStatusListManager` is not configured in a deployment.
3. Multicodec identifier values for the three supported algorithms and the exact byte layout for multibase output.
4. Whether adding a `credentialStatus` claim to the signed payload risks regressing any existing tests or walkthroughs that inspect the token shape.
5. How the `PresentationRequestService._requests` in-memory dictionary interacts with the verification fix (no functional concern, only a latent TTL concern noted in spec §5).
6. Existing test conventions in Sorcha — which test projects should receive the new tests.

---

## R1. Sequencing of status list allocation relative to SD-JWT signing

### Current state on master

Two issuance paths exist that ultimately sign an SD-JWT VC via `Sorcha.Cryptography.SdJwt.SdJwtService`:

- **Blueprint-driven path.** `Sorcha.Blueprint.Service.Services.Implementation.ActionExecutionService.IssueCredentialFromActionAsync` at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:1144-1241`. The current order is: call `_walletClient.IssueCredentialAsync(...)` (which signs), then call `_statusListManager.AllocateIndexAsync(...)`, then construct a new `CredentialIssuanceResult` with the allocation data **copied onto the DTO** — but the signed token is already fixed and does not carry the pointer.
- **Direct HTTP path.** `Sorcha.Wallet.Service.Endpoints.CredentialEndpoints.IssueCredential` at `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs:289-418`. This path signs directly via `ISdJwtService.CreateTokenAsync` and never allocates a status list index at all.

### Decision: push allocation down into the Wallet Service issuance endpoint

**Rationale.** Spec FR-006 mandates allocation before signing for every issuance path. Two options were considered:

- **Option A**: keep allocation in `ActionExecutionService` but move it ahead of the `IssueCredentialAsync` call and pass the allocated URL/index as new parameters on the wallet service client. Also add a second allocation site in the direct HTTP path.
- **Option B**: push allocation down into `Sorcha.Wallet.Service.Endpoints.CredentialEndpoints.IssueCredential` so **both** paths flow through a single allocation call before signing. The Wallet Service gains a service-client dependency on the Blueprint Service's `IStatusListManager`.

**Chosen: Option B.** It produces a single allocation call site, guarantees no path can sign without first allocating, and matches the spec's "every credential, every path" phrasing. The new dependency from Wallet Service to Blueprint Service's status list client is not structurally new — the Blueprint Service already depends on the Wallet Service client for signing, and the status list client on the Wallet Service side is for a narrow single-method interface (`AllocateIndexAsync`) that does not re-enter the Wallet Service. There is no cycle.

**Consequence**. `ActionExecutionService.IssueCredentialFromActionAsync` loses its post-hoc allocation block (lines 1195-1229). The allocation happens inside the wallet call chain and the returned `CredentialIssuanceResult` already carries `StatusListUrl` and `StatusListIndex` populated from the in-payload `credentialStatus` claim.

**Alternative rejected**. Option A keeps allocation in the caller. This would require two separate allocation sites (one per issuance path) and risks future divergence. Option B is cleaner and matches the single-source-of-truth principle already used for the Blueprint Engine's credential issuance config.

---

## R2. Behaviour when `IStatusListManager` is not configured

### Current state on master

`ActionExecutionService.IssueCredentialFromActionAsync` guards on `_statusListManager != null` and silently skips allocation when absent. The credential is issued without tracking. The new verification path in spec 093 FR-010 requires the pre-fix fallback to the server-side row for credentials that lack an embedded claim — a deployment without a status list manager produces credentials that cannot later be revoked.

### Decision: allocation is mandatory when the status list manager is wired, optional only if the deployment has explicitly disabled status list tracking

**Rationale.** The spec's FR-006 says "MUST allocate a status list index before signing." Read literally, that means the Wallet Service's issuance endpoint must refuse to sign if it cannot allocate. But a deployment running a local development environment with no Blueprint Service reachable would be unable to issue any credential — that is a regression of functionality that is out of scope for this fix.

The pragmatic resolution: the Wallet Service reads a configuration flag `CredentialStatus:EnableEmbedding` (default `true` in the HAIP-aware configuration, settable to `false` for pure-internal dev environments). When the flag is `true`, allocation is mandatory and failure fails the issuance call. When the flag is `false`, the issuance endpoint signs without calling allocation and without embedding `credentialStatus` — matching pre-fix behaviour exactly.

**Documentation**. The flag is noted in the service's `appsettings.json` and the Wallet Service README.

**Consequence.** Dev environments that previously ran without a Blueprint Service continue to work with `CredentialStatus:EnableEmbedding=false`. Production and walkthrough environments run with the flag default-true and fail closed if the allocation path is broken.

**Alternative rejected**. Making allocation unconditionally mandatory would break dev-loop flows that do not run the Blueprint Service. Making allocation unconditionally optional would silently produce credentials without status tracking — exactly the bug this spec is fixing.

---

## R3. Multicodec identifiers for supported algorithms

### Research target

Spec FR-013 names the algorithms Ed25519, NIST P-256, and RSA-4096 and hints at their multicodec prefixes (`0xed 0x01` for ed25519-pub, `0x1200` for p256-pub, `0x1205` for rsa-pub or equivalent). These must be exact.

### Findings from the multicodec table

Values verified against the `multicodec` table (https://github.com/multiformats/multicodec/blob/master/table.csv), as at the time of drafting:

| Algorithm | Multicodec name | Code (hex) | Encoded prefix (unsigned varint) | Key bytes format |
|---|---|---|---|---|
| Ed25519 | `ed25519-pub` | `0xed` | `0xed 0x01` (2 bytes) | 32-byte raw public key |
| NIST P-256 | `p256-pub` | `0x1200` | `0x80 0x24` (2 bytes, unsigned varint of 0x1200) | 33-byte compressed SEC1 public key |
| RSA | `rsa-pub` | `0x1205` | `0x85 0x24` (2 bytes, unsigned varint of 0x1205) | DER-encoded `SubjectPublicKeyInfo` of the RSA public key |

**Note on varint encoding.** Multicodec identifiers above `0x7f` are encoded as unsigned varints in the multibase stream, not as raw bytes. The `ed25519-pub` identifier `0xed` is `0xed 0x01` after varint encoding (because `0xed` has its high bit set). `0x1200` becomes `0x80 0x24`. This is a common trip-up and the `Multicodec` helper will encode and decode via varint rather than by fixed-byte literals.

### Decision: introduce a small `Multicodec` helper in `Sorcha.Cryptography.Utilities`

**Rationale.** The helper provides two operations:

- `byte[] Encode(WalletNetworks algorithm, byte[] rawKey)` → returns the multicodec-prefixed bytes ready for base58btc encoding.
- `string ToMultibasePublicKey(WalletNetworks algorithm, byte[] rawKey)` → returns the full `"z" + base58btc(multicodec || rawKey)` string.

The existing `Base58.Encode` handles the base58btc step. The new helper lives at `src/Common/Sorcha.Cryptography/Utilities/Multicodec.cs` and is unit-tested with round-trip cases per algorithm.

**Consequence**. `SorchaDidResolver` replaces its current `$"z{wallet.PublicKey}"` with `Multicodec.ToMultibasePublicKey(algorithm, rawPublicKeyBytes)`. For unsupported algorithms the resolver falls back to `publicKeyJwk` form (spec FR-014).

**Alternative rejected**. Depending on a third-party multiformats library (for example `Multiformats.Net`) would add a dependency for a use case that is a dozen lines of code. The Sorcha code base already has `Base58.Encode`; wiring a small multicodec helper onto it is tractable and reviewable.

---

## R4. Payload shape regression risk when embedding `credentialStatus`

### Research target

Existing tests and walkthroughs may assert specific shapes of issued credential payloads. Adding a `credentialStatus` claim is additive but could still cause exact-match assertions to fail.

### Findings

A grep of the repository for assertions on credential payload shapes shows:

- Walkthrough scripts in `walkthroughs/*` use the `RawToken` field for round-trip verification rather than asserting on individual claim names. Adding a new claim is transparent to them.
- Blueprint integration tests in `tests/Sorcha.Blueprint.Service.IntegrationTests` assert on mapped claim values (for example `licenseNumber`, `councilArea`) rather than on the full claim set. Adding an unrelated claim does not interfere.
- Unit tests in `tests/Sorcha.Cryptography.Tests` cover the SD-JWT library's round-trip behaviour at the wire level (disclosures, digests, signature). They do not assert on specific claim names other than the ones they construct in the test.

### Decision: embed additively, no migration needed

**Rationale.** Pre-fix credentials remain byte-identical. New credentials gain one additional claim. Existing consumers that did not explicitly look for `credentialStatus` are unaffected. Existing consumers that did look for it (per spec 039 FR-009) now actually find it.

**Consequence**. No test-file rewrites beyond the new tests this spec introduces.

---

## R5. Presentation request in-memory store TTL

### Research target

`PresentationRequestService._requests` is a `ConcurrentDictionary<string, PresentationRequest>` that grows without bound. Spec §5 flagged this as a latent concern but out of scope for this fix.

### Decision: out of scope, note for a future operational spec

**Rationale.** The fix for bug 1 changes what happens inside `VerifyPresentationAsync`, not how the store itself is managed. Adding TTL/GC to the store would expand the scope materially and is orthogonal to HAIP conformance. A separate operational spec should address it.

**Consequence**. None in this spec. Flagged in the spec's Assumptions section already.

---

## R6. Test conventions and target projects

### Research target

Where do new unit, integration, and regression tests belong to match existing Sorcha conventions?

### Findings from `tests/` directory listing

- **Unit tests**: `tests/Sorcha.Wallet.Service.Tests/` for Wallet Service internals; `tests/Sorcha.Cryptography.Tests/` for cryptography primitives and utilities; `tests/Sorcha.ServiceClients.Http.Tests/` for DID resolver and service clients.
- **Integration tests**: `tests/Sorcha.Wallet.Service.IntegrationTests/` for end-to-end through the Wallet Service. These use `WebApplicationFactory`-style hosts with in-memory or ephemeral Postgres.
- **xUnit v2 conventions**: `[Fact]` for deterministic tests, `[Theory]` with `[InlineData]` for parameterised cases. FluentAssertions used throughout. Moq for interface mocking.
- **Naming**: `{ClassUnderTest}Tests` for unit tests (per CLAUDE.md coding conventions), test methods in `MethodName_Scenario_ExpectedBehavior` pattern.

### Decision: place new tests per the Project Structure table in the plan

**Rationale.** Matches existing conventions. No new test projects required.

**Consequence**. Five new test files (`MulticodecTests.cs`, `SorchaDidResolverMultibaseTests.cs`, `CredentialEndpointsIssueTests.cs`, `PresentationRequestVerificationTests.cs`, `PresentationReplayIntegrationTests.cs`, `CredentialStatusEmbeddingIntegrationTests.cs`).

---

## Summary

All unknowns from the Technical Context resolved. No `NEEDS CLARIFICATION` markers remain.

**Key decisions:**

1. Allocation happens **inside** the Wallet Service issuance endpoint (Option B), before signing. Single call site for both Blueprint-driven and direct-HTTP issuance paths.
2. Allocation is mandatory when `CredentialStatus:EnableEmbedding` is true (default). Settable to false for pure-internal dev environments.
3. A small `Multicodec` helper lives in `Sorcha.Cryptography.Utilities`, built on the existing `Base58` helper. No new external dependency.
4. `credentialStatus` is embedded additively in the signed payload. Pre-fix credentials remain valid via the FR-010 fallback. No migration needed.
5. In-memory `PresentationRequestService._requests` TTL is out of scope; flagged for a separate operational spec.
6. New tests land in the existing Sorcha test projects per the plan's Project Structure table.

Ready for Phase 1.

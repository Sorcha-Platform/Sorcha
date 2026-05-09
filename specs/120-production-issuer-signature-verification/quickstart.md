# Quickstart: Production Issuer Signature Verification

**Feature**: 120-production-issuer-signature-verification
**Phase**: 1 (operator runbook)
**Date**: 2026-05-09
**Audience**: developers verifying the feature works end-to-end on a clean local stack; operators running the post-merge smoke test on n1.

## Prerequisites

- .NET 10 SDK
- Docker Desktop (or compatible runtime)
- A working `docker-compose.yml` at repo root (the standard Sorcha dev stack)
- For walkthrough validation: PowerShell 7.5+ (`pwsh`)

## Goal

Confirm that:

1. An organisation's first credential issuance lazily derives an issuance key and publishes its DID document under both forms.
2. The published `did:web` document is reachable, resolves cleanly, and declares `alsoKnownAs` linking to the `did:sorcha:org` form.
3. A credential issued under a fresh issuance key verifies under enforce-on (`RequireIssuerSignature: true`).
4. A tampered credential (signature modified, key swapped, `iss` changed) is rejected with the correct three-way failure-mode attribution.
5. An attacker constructed `did:web` document falsely claiming `alsoKnownAs` to another org's `did:sorcha` form is rejected by cross-resolution.
6. The walkthrough suite (AssuredIdentity, TradeFinance, ConstructionPermit, SelfBuildHouse) passes end-to-end with enforce-on as the default.
7. Phase 0 cleanup verified: legacy `IDIDResolver` is gone from the codebase.

## Step 1 — Bring the stack up

```powershell
# From repo root
docker-compose up -d
```

Wait for all services healthy:

```powershell
docker-compose ps
```

Aspire dashboard at `http://localhost:18888`; gateway at `http://localhost:80`.

## Step 2 — Verify Phase 0 cleanup

```powershell
# Should return zero matches across src/
Select-String -Pattern "IDIDResolver" -Path src/**/*.cs -Recurse
```

Expected: no results. The legacy interface, its implementation, and the `DIDResolutionResult` shape have all been deleted. The single previous consumer in `Sorcha.Register.Service/Program.cs:205` now uses `IDidResolverRegistry`.

## Step 3 — Trigger first credential issuance for a fresh org

Use the existing demo flow that issues an Assured Identity credential. The walkthrough is fastest:

```powershell
cd walkthroughs/AssuredIdentity
./run.ps1 -CleanState
```

Watch the Tenant Service logs:

```powershell
docker-compose logs -f tenant-service | Select-String -Pattern "OrgDidDocument"
```

Expected log lines:

```text
[INFO] OrgDidDocumentService: lazily deriving issuance key for org {orgId} (first issuance trigger)
[INFO] OrgDidDocumentService: regenerated DID document for org {orgId} (reason=IssuanceKeyDerived, version=1)
```

## Step 4 — Resolve the published DID documents

The federated `did:web` form should be reachable at the platform domain.

```powershell
# Replace {orgId} with the org id from step 3 logs
curl http://localhost/orgs/{orgId}/did.json | ConvertFrom-Json | Format-List
```

Expected: a JSON document with `id`, `alsoKnownAs` (one entry pointing at the `did:sorcha:org:` form), `verificationMethod` (two entries — versioned + thumbprint kid styles, both with the same `publicKeyMultibase`), and `assertionMethod` (referencing both VMs).

Cross-check the `did:sorcha:org:` form via the resolver registry. From inside any service that registers `IDidResolverRegistry`:

```csharp
var doc = await _registry.ResolveAsync("did:sorcha:org:" + walletAddress);
// doc.alsoKnownAs should contain the did:web form
// doc.verificationMethod should contain the same dual-VM pair as the did:web doc
```

## Step 5 — Verify a fresh credential under enforce-on

The Assured Identity walkthrough run from step 3 already exercises issuance and presentation under enforce-on. Confirm in the verifier logs:

```powershell
docker-compose logs citizen-verifier | Select-String -Pattern "verifier.issuer-resolve"
```

Expected: spans with `verifier.issuer.outcome=success` and `verifier.issuer.kid_match_mode=exact`. No `did-unresolved`, `kid-unmatched`, or `signature-failed` spans for this happy-path run.

## Step 6 — Inject a tampered credential

This step exercises the three-way failure-mode logging.

### 6a — Tampered signature (signature-failed)

Use the verifier's test seam to submit a credential whose JWS signature has been mutated by a single byte:

```powershell
./tests/scripts/test-tampered-credential.ps1 -Mutation Signature
```

Expected: HTTP 400/401 from the presentation endpoint. Verifier metric `sorcha_verifier_issuer_signature_failed_total` increments by 1.

### 6b — Unknown kid (kid-unmatched)

```powershell
./tests/scripts/test-tampered-credential.ps1 -Mutation UnknownKid
```

Expected: rejection with `sorcha_verifier_issuer_kid_unmatched_total += 1`.

### 6c — Unresolvable iss (did-unresolved)

```powershell
./tests/scripts/test-tampered-credential.ps1 -Mutation UnresolvedIss
```

Expected: rejection with `sorcha_verifier_issuer_did_unresolved_total += 1`.

(If the helper scripts above don't yet exist in the codebase, this step is the place to write them in Phase 4. They're test-fixtures, not production code.)

## Step 7 — Cross-resolution attack scenario

This is the security-critical test. It confirms FR-008 / FR-010 / SC-002.

Use the test fixture that constructs a malicious `did:web` document claiming `alsoKnownAs` to a different organisation's `did:sorcha:org` form, while serving an attacker-controlled signing key:

```powershell
./tests/scripts/test-cross-resolution-attack.ps1
```

Expected:

- The malicious credential is rejected.
- Verifier counter `sorcha_did_resolver_cross_resolve_mismatch_total` increments by 1.
- Span `did.resolve.cross` tagged `did.alsoKnownAs.match=mismatch`.
- The credential does NOT impersonate the targeted org.

If the credential is accepted, the cross-resolution implementation is broken — block ship until fixed.

## Step 8 — Run the full walkthrough suite

The ship gate. Each walkthrough must pass end-to-end with enforce-on.

```powershell
# AssuredIdentity (already covered by step 3 if run; re-run to confirm idempotency)
cd walkthroughs/AssuredIdentity
./run.ps1

# TradeFinance
cd ../TradeFinance
./run.ps1

# ConstructionPermit
cd ../ConstructionPermit
./run.ps1

# SelfBuildHouse
cd ../SelfBuildHouse
./run.ps1
```

Each `run.ps1` should report `PASS` end-to-end. A walkthrough failure under enforce-on is a ship blocker.

## Step 9 — Confirm forward-compat slots (SC-007)

Inspect a freshly-created register's genesis control record. The `RegisterPolicy` should NOT include `requireIssuerSignature` or `permittedIssuers` fields by default (because they're nullable + JSON-null-ignored), but the schema MUST accept them when present without error.

```powershell
# Roundtrip a control record with both fields populated
$json = @"
{
  "version": 1,
  "registerPolicy": {
    "requireIssuerSignature": true,
    "permittedIssuers": ["did:sorcha:org:ws1q..."]
  },
  "validators": [...]
}
"@
$json | dotnet run --project tools/RegisterControlRecordValidator
```

Expected: `valid=true`. The reserved fields parse cleanly; v1 simply does not act on them.

## Step 10 — Compromise + revocation drill

Optional but recommended for Phase 6 sign-off (validates SC-005).

1. Identify an org's active issuance key via the Tenant admin API.
2. Initiate a `RevokeIssuanceKey` governance op (proto-rule `VAL_CRED_GOV_001`).
3. Once quorum reached, attempt to present a credential signed by the now-revoked key.
4. Confirm rejection with revocation-attributed log line.
5. Confirm the org can derive a fresh issuance key and resume normal operation.

Time from initiating revocation to first rejection: should be bounded by the governance-op duration (typically minutes).

## Failure triage

If any step fails, the three-way failure-mode logging (FR-003) tells you which layer to look at:

| Symptom | Look at |
|---|---|
| `did-unresolved` count rising | `IDidResolverRegistry`, `OrgDidDocumentService`, `WebDidResolver` (network), `SorchaDidResolver` |
| `kid-unmatched` count rising | `KidThumbprintHelper`, `IIssuanceKeyService` rotation logic, `SorchaDidResolver` dual-VM publishing |
| `signature-failed` count rising | Cryptographic verification path; possible algorithm mismatch or Multicodec encoding bug |
| `cross_resolve_mismatch` count rising | Either an actual attack or a real `alsoKnownAs` bookkeeping error in `OrgDidDocumentService` |
| Walkthrough hangs | Cross-resolution latency — check `DidResolverCache` config, especially `DidResolver:Cache:WebTtlMinutes` |

## What's NOT verified by this quickstart (deferred)

- BYO-domain `did:web` end-to-end (out of scope for v1).
- Validator-side issuer-sig verification at seal time (Future B; out of scope).
- Auto-rotation schedules (manual rotation only).
- New DID methods like `did:ethr` (additive; quickstart will need extension when added).

## Cross-references

- Spec: `specs/120-production-issuer-signature-verification/spec.md`
- Plan: `specs/120-production-issuer-signature-verification/plan.md`
- Design: `docs/superpowers/specs/2026-05-09-production-issuer-signature-verification-design.md`
- Data model: `specs/120-production-issuer-signature-verification/data-model.md`
- Resolver contract: `specs/120-production-issuer-signature-verification/contracts/did-resolver-registry-contract.md`
- Endpoint contract: `specs/120-production-issuer-signature-verification/contracts/org-did-document-endpoint.openapi.yaml`

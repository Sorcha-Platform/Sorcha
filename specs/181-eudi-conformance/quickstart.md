# Quickstart: Validating Feature 181 (EUDI Conformance)

Per-user-story validation recipes against the local Docker stack (`docker-compose up -d`).

## US1 — Dialect migration (SC-001, SC-002)

```powershell
# 1. Create a presentation request via the HAIP verifier API, fetch the request object,
#    decode the JWT payload and assert dcql_query present / presentation_definition absent.
$req = Invoke-RestMethod -Method Post http://localhost/api/v1/verifier/requests `
  -Body (@{ credentialType = "https://sorcha.dev/vc/assured-identity/v1"; requiredClaims = @("givenName") } | ConvertTo-Json) `
  -ContentType application/json -Headers @{ Authorization = "Bearer $token" }
$jwt = Invoke-RestMethod $req.requestUri
# decode middle segment; assert: dcql_query, client_id starts "x509_san_dns:", typ header x5c present

# 2. Regression oracle — the walkthroughs must pass unchanged:
walkthroughs/AssuredIdentity/run-phase1-identity.ps1   # + phase2 licence (HAIP gate)
demos/AIAS/rehearse.ps1                                 # approved + rejected paths

# 3. Legacy rejection: POST a PE-shaped direct_post → expect 400 LEGACY_DIALECT.

# 4. CI gate red-test: temporarily add "presentation_definition" to a src/ file,
#    run scripts/check-presentation-dialect.ps1 → must fail (SC-008), revert.
```

## US2 — Multi-credential + alternatives (SC-003)

Author a two-query request (`identity` + `address`) and a two-option `credential_sets` request
(PID-vct OR AssuredIdentity-vct) via the verifier API; walk the PWA wallet (holding only
AssuredIdentity) through both: first shows "address: no matching credential" and blocks submission;
second completes via the AssuredIdentity branch. Verify `GET …/result` reports per-query outcomes.

## US3 — Trusted-list snapshot (SC-004, SC-009)

```powershell
# Fixture: tests generate a signed minimal TS 119 612 XML with a test CA (fixture kept under
# tests/…/Fixtures/TrustLists/). Import it:
Invoke-RestMethod -Method Post http://localhost/api/v1/trust/trustlists/import `
  -Form @{ trustListId = "test-lotl"; document = Get-Item fixture-tl.xml } -Headers $admin
# → 201 with sequenceNumber, anchorCount, signerCertThumbprint

# Issue a credential under the fixture CA (test helper), present it with a blueprint
# trustPolicy { sources:[{kind:"trustlist", trustListId:"test-lotl"}] } → verification succeeds,
# evidence.trustListId == "test-lotl#<seq>".
# DELETE the snapshot → same presentation now fails TRUSTLIST_UNAVAILABLE.
# Tamper one byte of the XML → import fails TRUSTLIST_SIGNATURE_INVALID.
# SC-009: time an operator (admin UI → import → first vouched verification) < 10 min.
```

## US4 — External issuance identity (SC-005)

```powershell
# CSR out:
$csr = Invoke-RestMethod -Method Post "$trust/orgs/$orgWallet/csr" -Headers $admin
# Sign with a local test CA (openssl or the test fixture CA), then import:
Invoke-RestMethod -Method Post "$trust/orgs/$orgWallet/certificates/import" `
  -Body (@{ certificatePem = $leaf; chainPem = @($root) } | ConvertTo-Json) -ContentType application/json -Headers $admin
# Issue a credential with credentialIssuanceConfig.trustAnchor = "x509-lotl" → decode SD-JWT x5c:
# chain terminates at the test CA root, NOT the tenant root. Verify with only the test root trusted.
# Negative: delete the imported cert → issuance fails CERT_EXTERNAL_ANCHOR_UNAVAILABLE (not tenant-root fallback).
```

## US5 — Lifecycle + Ed25519 exclusion (SC-006)

Create a new org → `GET …/certificates` shows an Active Internal cert with zero manual steps
(ED25519-primary org: `boundKeySource == "HaipCoKey"`). Pre-existing org → one-action backfill via
`/enrol`. Simulate a no-P-256-resolvable org (mock co-key derivation failure in tests) → typed 422
`CERT_KEY_NOT_ELIGIBLE`, zero unhandled-exception log entries.

## US6 — Verifier authentication (SC-007)

Configure `Haip:VerifierCertificate` with SAN dns == `Haip:PublicHost`; PWA scans a request →
consent sheet shows verifier identity (AuthenticUntrusted without an imported list; Trusted after
importing a list containing the verifier CA). Tamper the request-object signature (test hook) →
PWA refuses with `REQUEST_OBJECT_INVALID`; mismatch the SAN host → `REQUEST_HOST_MISMATCH`.

## Cross-cutting

- `dotnet build` warning-free; `dotnet test` green; E2E suites for F155/F164 verdict flows pass.
- STANDARDS.md rows updated (OpenID4VP/VCI → 1.0/final versions, SD-JWT VC `dc+sd-jwt`, new ETSI TS
  119 612 partial row) and `scripts/check-discoverability.sh` passes.
- Metrics visible on the Aspire dashboard: `sorcha_trustlist_*`, `sorcha_org_cert_issuance_total`,
  `sorcha_dialect_rejection_total`, `sorcha_request_auth_total`.

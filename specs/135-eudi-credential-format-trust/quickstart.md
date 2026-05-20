# Quickstart: EUDI Credential Format & Unified Trust

**Feature**: 135-eudi-credential-format-trust

This is the developer/operator walkthrough proving the three capabilities. Each maps to one user story and is independently demonstrable.

## Prerequisites

- .NET 10 SDK, Docker Desktop. `docker-compose up -d` (or `dotnet run --project src/Apps/Sorcha.AppHost`).
- Two new central packages added to `Directory.Packages.props`: `System.Formats.Cbor` (10.0.0) and `System.Security.Cryptography.Cose` (**10.0.8** — patched).
- A conformance mdoc test vector (EUDI PID DeviceResponse) committed under `tests/.../Fixtures/Mdoc/`.

## US1 — One trust decision for every path (P1)

1. Author a blueprint action whose requirement declares a `trustPolicy` (see `contracts/trust-policy.schema.md`) accepting the `x509-tenant` source at `minAssuranceLevel: substantial`.
2. Present an SD-JWT VC signed by a tenant org leaf through the **internal engine** path (workflow action execution).
   - ✅ Accepted; `TrustDecision.SignatureValid == true`; receipt carries `TrustEvidence` with the vouching source + CRL version.
3. Present the same credential with a tampered signature → ❌ rejected `SignatureInvalid` **on both** the engine path and the HAIP path (run the parity test).
4. Drop the policy to `register`/`low`, present a `did:sorcha:org` credential whose key is in `assertionMethod` → ✅ accepted; rotate the key out of `assertionMethod` → ❌ rejected.
5. Make the CRL endpoint unreachable under fail-closed → ❌ `RevocationUnavailable`.
6. Re-evaluate an accepted credential **offline** from pinned `TrustEvidence` → same decision (SC-005).

**Verifies**: FR-007/008/009/010/011/012/013/014/015/026, SC-001/002/005/006/007.

## US2 — Accept an mdoc from an EUDI wallet (P2)

1. Register a `trustlist` snapshot: `PUT /api/v1/trust/trustlists/eu-pid-test` with the test issuer root (see `contracts/trustlist-admin.openapi.md`).
2. Author a requirement with `format: "mso_mdoc"` and a `trustPolicy` naming that `trustlist` source.
3. Run an OpenID4VP request with a `dcql_query` for `format: "mso_mdoc"`, `doctype_value: "eu.europa.ec.eudi.pid.1"`.
4. Submit the `vp_token` (base64url DeviceResponse) to `response_uri`.
   - ✅ issuer signature over the MSO verifies; `valueDigests` match disclosed items; `DeviceAuth` verifies against the reconstructed `SessionTranscript`; disclosed PID elements surface as claims.
5. Negative cases → ❌ untrusted issuer (`UntrustedIssuer`), bad device-binding (`HolderBindingInvalid`), tampered element (`IntegrityFailure`), revoked via MSO status list (`Revoked`).
6. Re-run an SD-JWT VC presentation against an equivalent requirement → still ✅ (no regression).

**Verifies**: FR-001/002/003/004/016, SC-003.

## US3 — Issue an mdoc with a chosen trust anchor (P3)

1. Configure a `credentialIssuance` with `format: "mso_mdoc"`, `trustAnchor: "x509-tenant"`, `targetAudience: "HaipExternalWallet"`.
2. Run the OID4VCI issuance flow to an external wallet.
   - ✅ issued credential is a valid mdoc; `issuerAuth` signed by the org leaf; COSE `x5chain` (label 33) carries the leaf→root chain.
3. Present it back through US2 against a policy trusting `x509-tenant` → ✅ round-trips.
4. Switch to `format: "sd-jwt-vc"`, `trustAnchor: "x509-tenant"` → issued SD-JWT VC now carries the `x5c` JWS header (closes the chainless-issuance gap, FR-020).
5. Switch to `trustAnchor: "register"` → no chain; DID-verifiable.
6. Configure `x509-tenant` with no provisioned org cert → ❌ minting fails closed with a config error (FR-022).

**Verifies**: FR-018/019/020/021/022, SC-004.

## Regression / cross-cutting

- `Sorcha.Trust` meter emits decision counts by outcome/source/format/assurance; logs carry no subject data (FR-024).
- `dotnet build --force` then `dotnet test` — coverage ≥85% on new trust + format logic (SC-008).
- Confirm PQC signing options elsewhere are unchanged; mdoc is ES256/P-256-only and additive (FR-006, SC-009).

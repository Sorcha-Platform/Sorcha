---
title: Sorcha and the OpenID4VC / HAIP Wallet Ecosystem
description: How Sorcha issues to and verifies from HAIP-conformant holder wallets — including GOV.UK Wallet and the EU Digital Identity Wallet — via OpenID4VCI, OpenID4VP, and the HAIP 1.0 profile.
standards:
  - OpenID4VCI
  - OpenID4VP
  - HAIP 1.0
  - W3C Verifiable Credentials Data Model 2.0
  - IETF Token Status List 2024 (RFC 9972)
  - ML-DSA (FIPS 204)
last_updated: 2026-05-04
---

# Sorcha and the OpenID4VC / HAIP Wallet Ecosystem

Sorcha is the workflow infrastructure above HAIP-conformant holder wallets. It does not replace GOV.UK Wallet, it does not replace the EU Digital Identity Wallet, and it does not control the citizen experience. What it does is *issue* verifiable credentials *to* those wallets, *verify* presentations *from* those wallets, and operate the multi-party workflows that issue and verify in the first place.

This document is how an integrator working on the wallet ecosystem reads what Sorcha actually implements, where the implementation lives, and where it stops.

## What HAIP Is and Why Sorcha Implements It

The High Assurance Interoperability Profile (HAIP 1.0, OpenID Foundation, finalised December 2025) is the cross-jurisdiction profile that the EU Digital Identity Wallet, the GOV.UK Wallet, and an expanding set of national wallets are converging on. It pins down the credential format (SD-JWT VC), the issuance flow (OpenID4VCI pre-authorised flow with PKCE), the presentation flow (OpenID4VP cross-device QR), and the cryptographic suites at the wallet wire boundary (classical: ES256, EdDSA).

Sorcha implements HAIP 1.0 as the boundary protocol because that is the standard the holder ecosystem speaks. Inside the platform, Sorcha uses post-quantum cryptography (ML-DSA, FIPS 204) for its own signing operations — but at the HAIP boundary every signature is classical, because that is what the wallets accept.

## What Sorcha Does Not Do

- **Sorcha is not a wallet.** The citizen wallet PWA shipped in Feature 114 is a reference holder for development and demos. Production deployments expect citizens to use GOV.UK Wallet or EUDIW.
- **Sorcha does not control the citizen UX.** The wallet decides how a credential is rendered, how consent is gathered, and how disclosures are surfaced. Sorcha provides the data contract.
- **Sorcha is not a credential schema authority.** Schemas are referenced by URI; the issuing authority is the source of truth for what each field means.
- **Sorcha does not arbitrate trust.** Trust anchors come from the surrounding ecosystem — government registers, the EU trust list, or a Sorcha system register seeded from a published genesis ceremony (spec 099).

## The Issuer Path — OpenID4VCI

The HAIP Service exposes the OpenID4VCI issuer endpoints under the gateway. The flow Sorcha supports is the **pre-authorised code flow** with PKCE, which is the HAIP-mandated profile for high-assurance issuance.

| Phase | What happens | Where in code |
|---|---|---|
| Offer | Issuing authority builds a credential offer (SD-JWT VC type) and hands it to the holder out-of-band | `src/Services/Sorcha.Haip.Service` |
| Pre-authorised token | Holder wallet exchanges the pre-auth code (plus PIN, where required) for an access token | HAIP Service token endpoint |
| Credential endpoint | Holder wallet POSTs proof-of-possession of its key; HAIP Service mints the SD-JWT VC | HAIP Service credential endpoint |
| Status list | Issued credentials reference an IETF Token Status List 2024 (RFC 9972) URL the holder can poll | `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs` |

The credential format is **SD-JWT VC** with selective disclosure encoded as JSON Pointer paths plus per-disclosure salts. Inside the SD-JWT VC the disclosures are bound to the holder's key — only the holder can present them. The W3C Verifiable Credentials Data Model 2.0 envelope wraps the SD-JWT VC.

## The Verifier Path — OpenID4VP

The HAIP Service also runs the OpenID4VP verifier endpoint. The supported flow is the **cross-device same-origin profile** that HAIP mandates: a verifier presents a QR encoding an `openid4vp://` URL; the holder wallet on a separate device authenticates, gathers consent, and POSTs a signed presentation back to the verifier.

| Phase | What happens |
|---|---|
| Authorization request | Verifier builds the OpenID4VP request with a presentation definition (which disclosures it needs) and a nonce |
| QR handover | The request is encoded as a QR; the holder wallet scans or deep-links from another device |
| Presentation | The holder wallet returns a Verifiable Presentation containing the SD-JWT VC and a Key-Bound JWT signing the nonce + verifier audience |
| Verification | Verifier checks the issuer signature, the holder key-binding, the nonce match, and the IETF status list — every check is independent |

The verifier reference UI lives in `src/Apps/Sorcha.Verifier.Web` (Blazor Server). It is an integration-test surface and a demo — production verifiers in customer deployments are expected to embed the verification logic into their own application.

## The Wallet Backend — Citizen Wallet PWA (Feature 114)

The citizen wallet PWA is a Blazor WASM holder wallet that Sorcha ships for development, demos, and the cases where a customer wants a Sorcha-branded wallet rather than relying on a government-issued one. It is server-anchored: holder keys are derived from BIP44 slot 108 (`sorcha:citizen-holder`) on the server, then a delegation to a per-device WebCrypto P-256 key is signed and shipped to the device. This is aligned with the EUDIW WSCA (Wallet Secure Cryptographic Application) model.

The citizen wallet ships with:

- **Device enrolment** — POST `/devices/enrol` registers a per-device key and receives the holder→device delegation
- **Credential sync** — incremental sync via signed cursor JWTs (30-day TTL)
- **Consent UX** — deep-link paste of OpenID4VP requests, KB-JWT verification before display
- **Self-renewal of delegation** — 30 days before expiry the wallet rotates its device key
- **Activity log** — server-forwarded log of presentations made by the wallet

See `.claude/skills/sorcha-architecture/SKILL.md` § "Citizen Wallet PWA (Feature 114)" for the server-side surface in detail.

## Status Lists — IETF Token Status List 2024 (RFC 9972)

Every issued credential carries an `iss` claim and a `status_list` claim pointing at an RFC 9972 status list URL. The list itself is a compact bitstring; each issued credential corresponds to one bit. Revocation is a single-bit flip plus a re-signed list.

Sorcha implements two status list backends:

- **Per-org status list** — one status list per issuing org, signed by the org's issuer key. Citizen wallet credentials go here.
- **Per-blueprint status list** — internal-path status list signed by the platform, used by Sorcha's own internal credential issuance. Lives in `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs`.

The W3C Bitstring Status List envelope is supported as an alternative surface for the same underlying bitstring — same data, different envelope, both indexed by the same status list index.

## Trust Anchors and DID Resolution

Issuer keys must be resolvable by every verifier. Sorcha uses two key resolution paths:

- **Tenant register** — production verifiers resolve `did:sorcha:org:` issuer DIDs to the issuer's signing key by reading verification methods from the Tenant Service register. The seam is `IIssuerKeyResolver` in `src/Common/Sorcha.Cryptography`.
- **JWK registry (development)** — for development and demos, a `JwkRegistryIssuerKeyResolver` allows issuers to register their key directly. The citizen wallet's verifier-side demo flow uses this.

The system register genesis (spec 099) defines how the very first set of trust anchors is seeded — the public ceremony, the validator key import, the publication of the genesis docket. A production deployment cannot bypass this; trust must be anchored in something a third party can audit.

## Where the Implementation Stops

Honest gaps named explicitly:

- **mdoc / ISO 18013-5** — planned, not implemented. `STANDARDS.md` rows the mdoc credential format as `planned`. The codebase has no mdoc encoder or verifier today.
- **mTLS at internal hops** — not yet enforced. The constitutional principle is in place; the wire enforcement is on the roadmap.
- **DID method registry** — `did:sorcha:org:` and `did:sorcha:holder:` are implemented but not registered with the W3C DID method registry. Inter-platform DID resolution requires bilateral agreement today.

## Pointers

| Source | Purpose |
|---|---|
| [`STANDARDS.md`](../STANDARDS.md) | Authoritative status of every standard cited above |
| [`docs/security-model.md`](security-model.md) | Cryptographic posture, including the classical-at-HAIP-boundary discussion |
| [`docs/architecture.md`](architecture.md) | Where the HAIP Service sits in the overall service topology |
| `specs/094-haip-issuer/`, `specs/097-haip-credential-issuance/`, `specs/098-haip-credential-presentation/` | Feature specs behind the implementation |
| `.claude/skills/verifiable-credentials/SKILL.md` | Implementation patterns, common gotchas, and selective-disclosure mechanics |

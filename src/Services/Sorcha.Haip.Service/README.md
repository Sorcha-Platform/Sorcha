# Sorcha HAIP Service

The HAIP Service is Sorcha's boundary protocol surface for HAIP-conformant holder wallets
(EU Digital Identity Wallet, GOV.UK Wallet, and the Sorcha citizen wallet PWA). It implements the
OpenID4VCI issuer endpoints, the OpenID4VP verifier endpoints, and the High Assurance Interoperability
Profile (HAIP 1.0) at the wire boundary. Inside the platform Sorcha signs with post-quantum
cryptography; at the HAIP boundary every signature is classical (ES256 / EdDSA), because that is what
the wallets accept.

Ecosystem context and the honest scope statement live in
[`docs/openid4vc-haip-integration.md`](../../../docs/openid4vc-haip-integration.md). Standards status is
authoritative in [`STANDARDS.md`](../../../STANDARDS.md).

## Overview

| Concern | Detail |
|---------|--------|
| Issuer | OpenID4VCI pre-authorised code flow with PKCE (HAIP-mandated) |
| Verifier | OpenID4VP cross-device same-origin flow (QR handover) |
| Credential formats | SD-JWT VC (`dc+sd-jwt` final media type) and ISO `mso_mdoc` (Feature 135) |
| Presentation dialect | OpenID4VP 1.0 **DCQL** (`dcql_query`; Presentation Exchange retired — Feature 181) |
| Trust | routed through the shared `ITrustEvaluator` (register / `x509-tenant` / `x509-lotl` / trustlist) |

## API Endpoints

### Issuer (OpenID4VCI)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/.well-known/openid-credential-issuer` | Issuer metadata |
| GET | `/.well-known/oauth-authorization-server` | Authorization-server metadata |
| GET | `/{offerId}` | Retrieve a credential offer |
| POST | `/nonce` | Issue a `c_nonce` for proof-of-possession |
| POST | `/token` | Exchange the pre-authorised code (+ PIN where required) for an access token |
| POST | `/credential` | Mint the SD-JWT VC or `mso_mdoc` after proof-of-possession, per the offer's `format` + `trustAnchor` |

### Verifier (OpenID4VP)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/verifier/requests` | Create a presentation request (carries the `dcql_query`) |
| GET | `/api/v1/verifier/requests/{requestId}/request-object` | Signed request object (JWS, `x5c` header) fetched by the wallet |
| POST | `/api/v1/verifier/requests/{requestId}/direct-post` | Wallet posts the `vp_token`; per-query verification loop |
| GET | `/api/v1/verifier/requests/{requestId}/result` | Verifier-side outcome |

Wallet-facing GET request-object and `direct-post` are correctly `AllowAnonymous`; the internal
endpoints use the `RequireService` policy (SEC-013).

## Verifier Certificate & Signed Request Objects (Feature 181 US6)

The verifier authenticates itself to the wallet by signing its OpenID4VP request object with an X.509
**verifier certificate** and identifying itself with a prefixed **`x509_san_dns:{host}`** `client_id`.

- **`VerifierCertificate`** (`Services/VerifierCertificate.cs`) — resolved once at startup via
  `VerifierCertificate.Resolve(configuration, environment)`. Exposes `PublicHost`, `ClientId`
  (`x509_san_dns:{PublicHost}`), the ES256 signing key, and the `x5c` chain.
- **`RequestObjectSigner`** (`Services/RequestObjectSigner.cs`) — signs the request object (ES256) and
  embeds the `x5c` leaf→root chain in the JWS header, so the wallet can verify the signature, bind the
  leaf SAN dNSName to the `client_id` host, and chain to a trusted anchor.

### Configuration (`Haip:`)

| Key | Purpose |
|-----|---------|
| `Haip:PublicHost` | The installation's public host; the certificate SAN dNSName must equal this, and it backs the `x509_san_dns:{host}` client_id |
| `Haip:VerifierCertificate` | PFX file path or base64 PKCS#12 blob carrying a P-256 (ES256) private key |
| `Haip:VerifierCertificatePassword` | Optional password for the PFX |

Startup fails fast in **Production/Staging** when `Haip:VerifierCertificate` is unconfigured — the
verifier must present an x5c-bound request object. In Development a self-signed ES256 certificate with
SAN dNSName = `Haip:PublicHost` is generated in memory so self-contained demos keep working (the wallet
then renders the request as `AuthenticUntrusted`). A configured certificate whose SAN does not match
`Haip:PublicHost` is rejected at startup.

The wallet side of this exchange (`RequestObjectValidator` → three-state `VerifierAuthState`) lives in
`Sorcha.Verifier.Engine`; see the `sorcha-architecture` skill, "EUDI conformance — DCQL dialect, trust
rail, verifier auth (Feature 181)", US6.

### Other `Haip:` settings

| Key | Default | Purpose |
|-----|---------|---------|
| `Haip:IssuerUrl` | `https://sorcha.example/haip` | Issuer identifier in metadata + credentials |
| `Haip:TokenLifetimeSeconds` | 300 | Access-token lifetime |
| `Haip:NonceLifetimeSeconds` | 300 | `c_nonce` lifetime |
| `Haip:PreAuthCodeLifetimeSeconds` | 300 | Pre-authorised-code lifetime |
| `Haip:OfferLifetimeSeconds` | 600 | Credential-offer lifetime |

## Related

- [`docs/openid4vc-haip-integration.md`](../../../docs/openid4vc-haip-integration.md) — ecosystem role and scope
- [`docs/reference/API-DOCUMENTATION.md`](../../../docs/reference/API-DOCUMENTATION.md) — "EUDI Conformance API (Feature 181)" and "EUDI Credential Format & Unified Trust API (Feature 135)"
- `.claude/skills/verifiable-credentials/SKILL.md` — SD-JWT mechanics and issuer-signing model
- `specs/181-eudi-conformance/`, `specs/135-eudi-credential-format-trust/`, `specs/094-haip-issuer/`, `specs/097-haip-credential-issuance/`, `specs/098-haip-credential-presentation/`

# `llms.txt` template

**Feature**: 117-ai-discoverability

This is the template `llms.txt` (at the repo root) MUST follow. The shape is from [llmstxt.org](https://llmstxt.org).

## Structure

```text
# <Project Name>
> <one-paragraph factual summary, single sentence preferred, ≤ 280 chars>

## Capabilities
- <Capability>: <one-line factual description>
- <Capability>: <one-line factual description>
- ...

## Standards
- <Standard Name>: <stable canonical spec URL>
- <Standard Name>: <stable canonical spec URL>
- ...

## Links
- OpenAPI: <full URL of /.well-known/openapi.json>
- MCP manifest: <full URL of /.well-known/mcp.json>
- Quickstart: <repo URL>/blob/master/docs/quickstart.md
- Architecture: <repo URL>/blob/master/docs/architecture.md
- STANDARDS.md: <repo URL>/blob/master/STANDARDS.md
```

## Constraints

- **File size** ≤ 8192 bytes.
- **Plain text** with `Content-Type: text/plain; charset=utf-8` when served over HTTP.
- **Exactly one** H1 line (the project name).
- **Exactly one** blockquote summary paragraph (`>` line) immediately under the H1.
- **Sections required**: `## Capabilities`, `## Standards`, `## Links`. Section order is preferred but not enforced.
- Every `## Standards` entry name MUST match a row in `STANDARDS.md` with status `full` or `partial`. Cross-reference enforced by `scripts/check-discoverability.sh`.
- Every `## Links` URL MUST resolve to HTTP 200 against the running instance (for instance-served URLs) or against the public repo (for repo-relative URLs).
- **No marketing adjectives**. Deny-list (case-insensitive): `revolutionary`, `best-in-class`, `industry-leading`, `cutting-edge`, `world-class`, `seamless`.

## Initial Sorcha content (illustrative, not verbatim)

```text
# Sorcha
> Open proof infrastructure for the multi-party world — verifiable credentials, distributed registers, and standards-aligned credential exchange (OpenID4VCI / OpenID4VP / HAIP) on a post-quantum-internal foundation.

## Capabilities
- Verifiable credentials: SD-JWT VC issuance, presentation, and revocation per W3C VCDM 2.0
- HAIP-aligned exchange: OpenID4VCI issuer, OpenID4VP verifier, IETF Token Status List 2024
- Distributed registers: append-only transaction logs with peer replication and validator consensus
- HD wallets: BIP32 / BIP39 / BIP44 with Sorcha-purpose derivation paths
- Post-quantum cryptography: ML-DSA (FIPS 204) signing internally; classical wire boundary at HAIP
- MCP server: 36 tools across admin, designer, and participant slices for AI-driven workflow automation

## Standards
- BIP32: https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki
- BIP39: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
- BIP44: https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki
- ML-DSA (FIPS 204): https://csrc.nist.gov/pubs/fips/204/final
- OpenID4VCI: https://openid.net/specs/openid-4-verifiable-credential-issuance-1_0.html
- OpenID4VP: https://openid.net/specs/openid-4-verifiable-presentations-1_0.html
- HAIP 1.0: https://openid.net/specs/openid4vc-high-assurance-interoperability-profile-1_0.html
- W3C VC Data Model 2.0: https://www.w3.org/TR/vc-data-model-2.0/
- IETF Token Status List 2024 (RFC 9972): https://datatracker.ietf.org/doc/html/rfc9972
- W3C Bitstring Status List: https://www.w3.org/TR/vc-bitstring-status-list/
- DID (W3C): https://www.w3.org/TR/did-core/
- OAuth 2.0: https://datatracker.ietf.org/doc/html/rfc6749

## Links
- OpenAPI: https://sorcha.example/.well-known/openapi.json
- MCP manifest: https://sorcha.example/.well-known/mcp.json
- Quickstart: https://github.com/Sorcha-Platform/Sorcha/blob/master/docs/quickstart.md
- Architecture: https://github.com/Sorcha-Platform/Sorcha/blob/master/docs/architecture.md
- STANDARDS.md: https://github.com/Sorcha-Platform/Sorcha/blob/master/STANDARDS.md
```

The illustrative content above is reference material — the production `llms.txt` is hand-written during T044 with up-to-date URLs and capability statements verified against the implemented platform.

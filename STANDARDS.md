# Sorcha Standards Compliance

Sorcha is cryptographic proof infrastructure for multi-party workflows. The standards below underpin that posture: BIP32/39/44 for hierarchical-deterministic wallet keying, ML-DSA (NIST FIPS 204) for post-quantum signatures on the internal path, ML-KEM (FIPS 203) for post-quantum key encapsulation, JSON Pointer–based selective disclosure inside SD-JWT VC envelopes, Merkle dockets for ledger integrity, and the OpenID4VC + HAIP profile for wallet interoperability at the boundary. This file is the single source of truth for those claims; every other surface that names a standard (`llms.txt`, OpenAPI `info.x-standards`, `docs/` frontmatter) cross-references a row here.

Honest gaps named explicitly: HAIP 1.0 mandates classical signatures at the wire boundary, so the post-quantum posture is internal only; SLH-DSA (FIPS 205) and BBS+ are not yet implemented; mTLS is not yet enforced on inter-service hops outside the gateway.

Status values:

- `full` — the standard is fully implemented for the component(s) listed.
- `partial` — the standard is partially implemented; the Notes column names the specific gap.
- `planned` — the standard is on the roadmap but not yet implemented; the Notes column names the spec or roadmap item that will deliver it.

| Standard | Version | Body | Spec URL | Components | Status | Notes |
|---|---|---|---|---|---|---|
| BIP32 | 2017 | Bitcoin Improvement Proposals | [BIP-0032](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki) | `src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs` | full | NBitcoin-backed; HD path-style derivation across all wallets |
| BIP39 | 2013 | Bitcoin Improvement Proposals | [BIP-0039](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki) | `src/Core/Sorcha.Wallet.Portable/Domain/ValueObjects/Mnemonic.cs` | full | English wordlist only |
| BIP44 | 2014 | Bitcoin Improvement Proposals | [BIP-0044](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki) | `src/Core/Sorcha.Wallet.Portable/Constants/SorchaDerivationPaths.cs` | full | Sorcha-specific purpose namespace per derivation slot |
| ML-DSA (FIPS 204) | 2024 | NIST | [FIPS 204](https://csrc.nist.gov/pubs/fips/204/final) | `src/Common/Sorcha.Cryptography/Core/PqcSignatureProvider.cs`, `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs` | full | Internal signing path; HAIP wire boundary remains classical per HAIP 1.0 |
| ML-KEM (FIPS 203) | 2024 | NIST | [FIPS 203](https://csrc.nist.gov/pubs/fips/203/final) | `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs` | full | Internal key-encapsulation path |
| OpenID4VCI | Draft 14 | OpenID Foundation | [OpenID4VCI](https://openid.net/specs/openid-4-verifiable-credential-issuance-1_0.html) | `src/Services/Sorcha.Haip.Service` | partial | Issuer endpoint per spec 097; ongoing hardening |
| OpenID4VP | Draft 21 | OpenID Foundation | [OpenID4VP](https://openid.net/specs/openid-4-verifiable-presentations-1_0.html) | `src/Services/Sorcha.Haip.Service` | partial | Verifier endpoint per spec 098 |
| HAIP 1.0 | 2025-12 | OpenID Foundation | [HAIP 1.0](https://openid.net/specs/openid4vc-high-assurance-interoperability-profile-1_0.html) | `src/Services/Sorcha.Haip.Service` | partial | Wire boundary classical-only per spec; PQC is internal |
| W3C Verifiable Credentials Data Model 2.0 | 2025 | W3C | [VC Data Model 2.0](https://www.w3.org/TR/vc-data-model-2.0/) | `src/Common/Sorcha.Cryptography/SdJwt`, `src/Services/Sorcha.Wallet.Service` | full | SD-JWT VC profile; classical embedding for wire compatibility |
| IETF Token Status List 2024 (RFC 9972) | 2024 | IETF | [RFC 9972](https://datatracker.ietf.org/doc/html/rfc9972) | `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs`, `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs` | full | Per spec 095; W3C and IETF envelopes back the same bitstring |
| W3C Bitstring Status List | 2024 | W3C | [Bitstring Status List](https://www.w3.org/TR/vc-bitstring-status-list/) | `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs` | full | Internal-path issuance default |
| ISO 18013-5 (mdoc) | 2021 | ISO/IEC | [ISO 18013-5](https://www.iso.org/standard/69084.html) | n/a | planned | Roadmap; not yet implemented |
| DID (W3C) | 1.0 | W3C | [DID 1.0](https://www.w3.org/TR/did-core/) | `src/Common/Sorcha.Cryptography`, `src/Services/Sorcha.Wallet.Service` | partial | `did:sorcha:org:` and `did:sorcha:holder:` types; no DID resolution registry adoption |
| OAuth 2.0 | 2012 | IETF | [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749) | `src/Services/Sorcha.Tenant.Service` | full | JWT issuer for service-to-service and user authentication |
| SLH-DSA (FIPS 205) | 2024 | NIST | [FIPS 205](https://csrc.nist.gov/pubs/fips/205/final) | n/a | planned | Stateless hash-based signature; complementary PQC algorithm to ML-DSA |
| BBS+ Signatures | Draft | IETF | [BBS Signatures](https://datatracker.ietf.org/doc/draft-irtf-cfrg-bbs-signatures/) | n/a | planned | Selective-disclosure signature scheme; SD-JWT VC currently covers the disclosure use case |

## Sorcha-implemented capabilities

Internal cross-service capabilities that aren't external standards but are referenced from agent-facing surfaces (`llms.txt`, `docs/`, OpenAPI). Each links to the spec that defines the contract.

- **Notifications & Inbox** — `full` — [specs/118-notifications-architecture/spec.md](specs/118-notifications-architecture/spec.md). Five-hub topology (Blueprint, Wallet, Register, Tenant, Chat) with Redis backplane, thin-signal contract (opaque IDs only), and durable per-user inbox with category filtering. ChatHub is the documented streaming exception (FR-019).

## Maintenance

- **PR checklist requirement.** Every PR that touches a path listed in any Components cell MUST review this file and update it if the change affects compliance status. The PR template at `.github/pull_request_template.md` carries a "STANDARDS.md reviewed" checkbox.
- **Structural CI check.** A CI step in `.github/workflows/ai-discoverability-check.yml` (via `scripts/check-discoverability.sh`) verifies on every PR to master that this file is present, parses as a Markdown table with the seven required columns, every Status is one of `full`/`partial`/`planned`, and every Components path resolves to a real path in the repository.
- **Cross-reference contract.** A second CI check verifies every standard named in `llms.txt`, `docs/llms-full.txt`, the served OpenAPI document's `info.x-standards`, and the `standards[]` frontmatter of published `docs/` files matches a row here with status `full` or `partial`. A `planned` row cannot be cited externally.
- **Drift policy.** A stale row is treated as a defect, not a documentation issue. If a Components path is renamed or a standard's adoption status changes, the same PR that makes the change updates this file.

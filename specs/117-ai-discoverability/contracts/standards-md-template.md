# `STANDARDS.md` template

**Feature**: 117-ai-discoverability

This is the template `STANDARDS.md` (at the repo root) MUST follow.

## Structure

```markdown
# Sorcha Standards Compliance

Sorcha implements the following standards. Every claim is backed by a component path that resolves to real code in this repository. Status values:

- `full` — the standard is fully implemented for the component(s) listed.
- `partial` — the standard is partially implemented; the Notes column names the specific gap.
- `planned` — the standard is on the roadmap but not yet implemented; the Notes column names the spec or roadmap item that will deliver it.

| Standard | Version | Body | Spec URL | Components | Status | Notes |
|---|---|---|---|---|---|---|
| <Name> | <Version or year> | <Body acronym> | [<Spec ID>](<URL>) | `<repo path>` | `full`/`partial`/`planned` | <Notes if partial/planned> |
| ... | ... | ... | ... | ... | ... | ... |

## Maintenance

- Every PR that touches a path listed in any Components cell MUST review this file and update it if the change affects compliance status.
- A CI check on every PR to master verifies this file is present and parses as a Markdown table without structural errors.
- A CI cross-reference check verifies every standard named in `llms.txt`, `docs/llms-full.txt`, OpenAPI `info.x-standards`, and the `standards[]` frontmatter of published `docs/` files matches a row here with status `full` or `partial`.
- The PR template carries a "STANDARDS.md reviewed" checkbox.
```

## Constraints

- **Single table** with the seven required columns.
- **Status** column: exactly one of `full`, `partial`, `planned`.
- **Components** column: every path in the cell (comma-separated if multiple) MUST resolve to a real path in the repo. CI-enforced.
- **Notes** column: required for any row whose Status is `partial` or `planned`. Empty for `full` rows is acceptable.
- **Spec URL** column: a stable canonical URL for the standard (RFC, NIST publication, ISO standard page, W3C TR, BIP wiki page, OpenID Foundation specification page).
- **Maintenance section**: present at the bottom, describes the cross-reference contract.
- **No marketing adjectives**. Same deny-list as `llms.txt`.

## Initial Sorcha content (illustrative, not verbatim — final list assembled during T049)

```markdown
| Standard | Version | Body | Spec URL | Components | Status | Notes |
|---|---|---|---|---|---|---|
| BIP32 | 2017 | Bitcoin Improvement Proposals | [BIP-0032](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki) | `src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs` | full | NBitcoin-backed; BIP32 path-style derivation across all wallets |
| BIP39 | 2013 | Bitcoin Improvement Proposals | [BIP-0039](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki) | `src/Core/Sorcha.Wallet.Portable/Domain/ValueObjects/Mnemonic.cs` | full | English wordlist only |
| BIP44 | 2014 | Bitcoin Improvement Proposals | [BIP-0044](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki) | `src/Core/Sorcha.Wallet.Portable/Constants/SorchaDerivationPaths.cs` | full | Sorcha-specific purpose namespace per slot |
| ML-DSA (FIPS 204) | 2024 | NIST | [FIPS 204](https://csrc.nist.gov/pubs/fips/204/final) | `src/Common/Sorcha.Cryptography/Pqc/` | full | Internal signing path; HAIP wire boundary remains classical per HAIP 1.0 |
| OpenID4VCI | Draft 14 | OpenID Foundation | [OpenID4VCI](https://openid.net/specs/openid-4-verifiable-credential-issuance-1_0.html) | `src/Services/Sorcha.Haip.Service/` | partial | Issuer endpoint per spec 097; ongoing hardening |
| OpenID4VP | Draft 21 | OpenID Foundation | [OpenID4VP](https://openid.net/specs/openid-4-verifiable-presentations-1_0.html) | `src/Services/Sorcha.Haip.Service/` | partial | Verifier endpoint per spec 098 |
| HAIP 1.0 | 2025-12 | OpenID Foundation | [HAIP 1.0](https://openid.net/specs/openid4vc-high-assurance-interoperability-profile-1_0.html) | `src/Services/Sorcha.Haip.Service/` | partial | Wire boundary classical-only per spec; PQC is internal |
| W3C VC Data Model 2.0 | 2025 | W3C | [VC Data Model 2.0](https://www.w3.org/TR/vc-data-model-2.0/) | `src/Common/Sorcha.Cryptography/SdJwt/`, `src/Services/Sorcha.Wallet.Service/` | full | SD-JWT VC profile; classical embedding for wire compatibility |
| IETF Token Status List 2024 (RFC 9972) | 2024 | IETF | [RFC 9972](https://datatracker.ietf.org/doc/html/rfc9972) | `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs`, `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs` | full | Per spec 095; W3C and IETF envelopes back the same bitstring |
| W3C Bitstring Status List | 2024 | W3C | [Bitstring Status List](https://www.w3.org/TR/vc-bitstring-status-list/) | `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs` | full | Internal-path issuance default |
| ISO 18013-5 (mdoc) | 2021 | ISO/IEC | [ISO 18013-5](https://www.iso.org/standard/69084.html) | n/a | planned | Roadmap; not yet implemented |
| DID (W3C) | 1.0 | W3C | [DID 1.0](https://www.w3.org/TR/did-core/) | `src/Common/Sorcha.Cryptography/`, `src/Services/Sorcha.Wallet.Service/` | partial | `did:sorcha:org:` and `did:sorcha:holder:` types; no DID resolution registry adoption |
| OAuth 2.0 | 2012 | IETF (RFC 6749) | [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749) | `src/Services/Sorcha.Tenant.Service/` | full | Used as the JWT issuer |
```

The illustrative table above is reference material — the production `STANDARDS.md` is committed during T049 with paths verified against the current state of the codebase.

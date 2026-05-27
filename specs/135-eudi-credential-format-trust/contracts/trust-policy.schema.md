# Contract: TrustPolicy on CredentialRequirement

JSON shape carried on a blueprint action's `credentialRequirements[]`. Replaces the removed `acceptedIssuers`.

## Schema (informal)

```jsonc
{
  "type": "IdentityAttestation",          // unchanged
  "format": "sd-jwt-vc",                   // NEW: "sd-jwt-vc" | "mso_mdoc" (default sd-jwt-vc)
  "revocationCheckPolicy": "FailClosed",   // unchanged (FailClosed default)
  "requiredClaims": [ /* unchanged */ ],
  "presentationSource": "HaipExternalWallet", // unchanged discriminator
  "trustPolicy": {                          // NEW (omit → default policy, FR-026)
    "combinator": "anyOf",                  // "anyOf" (default) | "allOf"
    "minAssuranceLevel": "substantial",     // "low" (default) | "substantial" | "high"
    "sources": [
      { "kind": "x509-tenant", "confersAssurance": "substantial" },
      { "kind": "trustlist", "trustListId": "eu-lotl-2026q2", "confersAssurance": "high" },
      { "kind": "did-allowlist", "allowedIssuers": ["did:sorcha:org:ws1q..."], "confersAssurance": "low" },
      { "kind": "register", "confersAssurance": "low" }
    ]
  }
}
```

## Rules

- `format` selects the `ICredentialFormatHandler`; a presentation of a different format than `format` is rejected (`FormatUnsupported`).
- `trustPolicy.sources[].kind` ∈ `register | x509-tenant | trustlist | did-allowlist`.
- `combinator=anyOf` → accept if **any** source vouches at/above `minAssuranceLevel`; `allOf` → **every** source must vouch (an unreachable required source ⇒ fail closed, FR-013).
- Omitting `trustPolicy` ⇒ evaluator synthesises the default (legacy issuers→`did-allowlist`, else single `register` at `low`).
- Established assurance = source-conferred level, raised only by an explicit credential assurance claim the source supports; absent ⇒ `low`.

## Acceptance mapping

- US1 scenarios 1–6, FR-009/010/011/012/013/026.

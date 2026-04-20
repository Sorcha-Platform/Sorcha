# Contract: AssuredIdentityCredential

**Feature**: 107-assured-identity-v1
**Format**: SD-JWT VC (W3C Verifiable Credentials 2.0, SD-JWT VC profile)

## Identity

| Property | Value |
|---|---|
| `vct` (verifiable credential type) | `AssuredIdentityCredential` |
| Issuer DID | `did:sorcha:org:<wallet-of-issuing-org>` |
| Holder binding | `cnf` claim with the holder's wallet public key (per HAIP) |
| Expiry (`exp`) | None — identity credentials do not expire in v1; revocation via Feature 079 |
| Issued at (`iat`) | Issuance timestamp |

## Claim schema

All claims are selectively disclosable.

```jsonc
{
  "vct": "AssuredIdentityCredential",
  "iss": "did:sorcha:org:wsXXX...",
  "iat": 1740000000,
  "cnf": { "jwk": { /* holder pubkey */ } },

  "givenName": "Aisling",
  "middleName": "Marie",          // optional
  "familyName": "O'Brien",
  "fullName": "Aisling Marie O'Brien",
  "dateOfBirth": "1986-08-12",
  "email": "aisling@example.com",
  "address": {
    "line1": "14 Princes Street",
    "line2": null,
    "town": "Edinburgh",
    "region": null,
    "postcode": "EH1 2NG",
    "country": "GB"
  },
  "portrait": "/9j/4AAQSkZJ..."   // optional, base64 JPEG ≤27KB
}
```

### Required vs optional claims

| Claim | Required at issuance? | Notes |
|---|---|---|
| `givenName` | Yes | From submission `/name/givenName` |
| `middleName` | No | Omitted if blank |
| `familyName` | Yes | From submission `/name/familyName` |
| `fullName` | Yes | Renderer-derived |
| `dateOfBirth` | Yes | Server-side `formatMaximum: today` enforced |
| `email` | Yes | RFC 5322 format |
| `address` | Yes | Whole object disclosed/withheld as a unit in v1 |
| `portrait` | No | Included only if citizen provided a photo and the client-side token is within size bounds |

## Selective disclosure

Each claim is its own SD claim (per SD-JWT VC convention). The holder may withhold any claim during a presentation. The `address` object is a single SD claim — sub-field selective disclosure (e.g. disclose town but withhold street) is out of scope in v1.

## Issuance flows

### Register-native (Feature 106)

1. Citizen submits Page 5 of the wizard
2. Assessor (agent or human) approves
3. `ActionExecutionService` mints the credential, encrypts it via `EncryptionPipelineService` for the holder's wallet, seals it as a recipient-addressed disclosure on the register
4. Holder's peer's `InboundCredentialDetector` picks it up via the bloom-filter notification path
5. Credential lands in the holder's MyCredentials PENDING tab on the holder's peer
6. Holder accepts → transitions to Active

### HAIP external (Feature 097 + 104)

1. Citizen submits Page 5 of the wizard
2. Assessor approves
3. `ActionExecutionService` mints the credential offer via the HAIP issuer, exposes the OpenID4VCI pre-authorised code
4. The next action is a Wave 14b claim card (per `x-credential-offer` extension), surfaced to the citizen in My Actions
5. Citizen clicks Claim (in-platform redemption via `HaipLocalReceiveService` into the local Sorcha wallet) OR Scan (QR for external HAIP wallet)
6. Credential lands in the chosen wallet

The blueprint declares both paths via the existing dual-path Wave 14b card; the holder picks at claim time.

## Revocation

- Mechanism: existing Feature 079 revocation transactions
- Reasons supported: Superseded, Erroneous, Compromised, Expired (n/a here), Withdrawn, Regulatory
- Verifier check: per credentialRequirements `revocationCheckPolicy: FailClosed`

## Replaces

- `VerifiedCitizenCredential` (from `walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json`)
- `AssuredPersonCredential` (from `walkthroughs/HaipVerifiedCitizen/blueprints/assured-person.json`)

Both are removed in Phase 7. No back-compat shim or aliasing.

# Contract: DrivingLicenceCredential

**Feature**: 107-assured-identity-v1
**Format**: SD-JWT VC

## Identity

| Property | Value |
|---|---|
| `vct` | `DrivingLicenceCredential` |
| Issuer DID | `did:sorcha:org:<wallet-of-dla-org>` |
| Holder binding | `cnf` (same wallet that holds the AssuredIdentityCredential — verified at presentation time) |
| Expiry | 10 years (`exp = iat + P10Y`) |

## Claim schema

```jsonc
{
  "vct": "DrivingLicenceCredential",
  "iss": "did:sorcha:org:wsYYY...",
  "iat": 1740000000,
  "exp": 2055000000,
  "cnf": { "jwk": { /* holder pubkey */ } },

  "licenceNumber": "DLA-2026-A7K3-001",
  "vehicleClass": "Car (B)",
  "issuedDate": "2026-04-20",
  "expiryDate": "2036-04-20",
  "holderName": "Aisling O'Brien",
  "holderDateOfBirth": "1986-08-12",
  "holderPortrait": "/9j/4AAQSkZJ..."   // optional, carried forward from presented identity if disclosed
}
```

### Required vs optional claims

| Claim | Required | Source |
|---|---|---|
| `licenceNumber` | Yes | DLA-side generated at approval (format: `DLA-{year}-{instance-suffix}-{class}`) |
| `vehicleClass` | Yes | Submission `/vehicleClass` (enum) |
| `issuedDate` | Yes | Today at approval |
| `expiryDate` | Yes | `issuedDate + P10Y` |
| `holderName` | Yes | Concatenated from presented `givenName + " " + familyName` |
| `holderDateOfBirth` | Yes | Presented `dateOfBirth` |
| `holderPortrait` | No | Presented `portrait` (only if the citizen elected to include their portrait in the AssuredIdentityCredential AND elected to disclose it during presentation) |

## Phase 2 verification step (presentation requirement)

The Driving Licence blueprint's verification action declares:

```jsonc
{
  "credentialRequirements": [
    {
      "type": "AssuredIdentityCredential",
      "presentationSource": "HaipExternalWallet",
      "requiredClaims": [
        { "claimName": "givenName" },
        { "claimName": "familyName" },
        { "claimName": "dateOfBirth" }
      ],
      "optionalClaims": [
        { "claimName": "portrait" }
      ],
      "revocationCheckPolicy": "FailClosed",
      "description": "Government-issued AssuredIdentityCredential — name, DoB, and optional portrait carried forward to the licence"
    }
  ]
}
```

**Not requested**: `email`, `middleName`, `fullName`, `address`. These remain in the citizen's wallet and never reach the DLA.

The presentation uses OpenID4VP `direct_post` with KB-JWT key binding (existing Feature 098 pipeline).

## Issuance + claim flow

1. Citizen submits Phase 2 starting action (open, late-bound to same wallet that holds AssuredIdentity)
2. DLA presentation request issued; citizen consents and presents (selectively disclosing the requested claims + optional portrait)
3. DLA officer (agent or human) reviews the **two stacked id-cards** (presented identity above, licence-to-be below) and approves
4. Licence credential minted via HAIP OpenID4VCI; offer routed to citizen's My Actions via `outputMapping`
5. Citizen claims via Wave 14b claim card (same dual-path as Phase 1)

## Replaces

`DrivingLicenceCredential` (from `walkthroughs/HaipDrivingLicence/blueprints/driving-licence.json`). The blueprint file moves to `walkthroughs/AssuredIdentity/blueprints/driving-licence.json` with the consolidated claim shape (adds `holderDateOfBirth`, `holderPortrait`).

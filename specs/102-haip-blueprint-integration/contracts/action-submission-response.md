# Contract: Action Submission Response (Extended)

## Endpoint

`POST /api/instances/{instanceId}/actions/{actionId}/execute`

## Response Body (extended fields only)

```json
{
  "transactionId": "abc123...",
  "instanceId": "def456...",
  "isComplete": false,
  "nextActions": [...],
  
  "credentialOffer": {
    "offerId": "d55a418e-09f1-4441-9f40-61cde235e24b",
    "credentialOfferUri": "openid-credential-offer://?credential_offer=%7B%22credential_issuer%22...",
    "credentialType": "VerifiedIdentityCredential",
    "issuerName": "Government Identity Authority",
    "expiresAt": "2026-04-11T21:05:00Z"
  },
  
  "presentationRequest": null
}
```

### When credentialOffer is present

Action has `credentialIssuanceConfig.targetAudience: HaipExternalWallet`. The offer was created via the HAIP Service and the URI can be rendered as a QR code for an external wallet to scan.

### When presentationRequest is present

Action has `credentialRequirements` with `presentationSource: HaipExternalWallet` and no credential presentations were submitted. A presentation request was created via the HAIP Service.

```json
{
  "transactionId": "abc123...",
  "instanceId": "def456...",
  "isComplete": false,
  "nextActions": [...],
  
  "credentialOffer": null,
  
  "presentationRequest": {
    "requestId": "95ec7ae6-8efb-43ce-b9b9-4603ccd045b4",
    "presentationRequestUri": "openid4vp://authorize?client_id=...",
    "credentialType": "VerifiedIdentityCredential",
    "requestedClaims": ["givenName", "familyName", "dateOfBirth"],
    "expiresAt": "2026-04-11T21:15:00Z"
  }
}
```

### Both null (default)

Standard action execution with no HAIP interaction. This is the existing behaviour.

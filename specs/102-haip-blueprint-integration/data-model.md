# Data Model: HAIP Blueprint Integration

## Modified Entities

### ActionSubmissionResponse (Blueprint Service)

Existing response model extended with HAIP interaction data.

**New Fields:**

| Field | Type | Description |
|-------|------|-------------|
| CredentialOffer | HaipCredentialOfferResponse? | Present when action issues a credential to HaipExternalWallet |
| PresentationRequest | HaipPresentationRequestResponse? | Present when action creates a presentation request for HaipExternalWallet |

### HaipCredentialOfferResponse (new, Blueprint Service)

| Field | Type | Description |
|-------|------|-------------|
| OfferId | Guid | Unique offer identifier for status polling |
| CredentialOfferUri | string | openid-credential-offer:// URI for QR rendering |
| CredentialType | string | Type of credential being offered |
| IssuerName | string? | Display name of the issuing organisation |
| ExpiresAt | DateTimeOffset | When the offer expires |

### HaipPresentationRequestResponse (new, Blueprint Service)

| Field | Type | Description |
|-------|------|-------------|
| RequestId | Guid | Unique request identifier for result polling |
| PresentationRequestUri | string | openid4vp://authorize URI for QR rendering |
| CredentialType | string | Type of credential being requested |
| RequestedClaims | List&lt;string&gt;? | Claims requested for selective disclosure |
| ExpiresAt | DateTimeOffset | When the request expires |

## Blueprint Templates

### Identity Attestation Blueprint

| Element | Value |
|---------|-------|
| Participants | 1: government-admin |
| Actions | 1: Issue Identity Credential (starting) |
| Credential Issuance | VerifiedIdentityCredential, HaipExternalWallet |
| Schema Fields | givenName, familyName, fullName, dateOfBirth, email, address (nested) |
| Disclosable | All top-level fields + address sub-fields |

### Driving Licence Blueprint (updated)

| Element | Value |
|---------|-------|
| Participants | 2: council, applicant |
| Actions | 2: Verify Identity (starting, council), Issue Licence (council) |
| Action 1 | credentialRequirements: VerifiedIdentityCredential, HaipExternalWallet |
| Action 2 | credentialIssuance: DrivingLicenceCredential, HaipExternalWallet |
| Routing | verify-identity -> issue-licence -> complete |

## Walkthrough State Extensions

### state.json (both walkthroughs)

Existing fields preserved. New fields added:

| Field | Type | Description |
|-------|------|-------------|
| registerId | string | Register created for this walkthrough |
| blueprintId | string | Published blueprint ID |
| instanceId | string? | Current workflow instance ID (set during run) |

## Existing Entities (unchanged, referenced)

- **ActionSubmissionResultViewModel** (UI): Already has `CredentialOffer` (HaipCredentialOfferInfo) and `PresentationRequest` (HaipPresentationRequestInfo) properties. JSON property names align with the new Blueprint Service response properties via camelCase policy.
- **CreateOfferResult** (IHaipServiceClient): Returns OfferId, CredentialOfferUri, PreAuthorizedCode, ExpiresAt.
- **CreatePresentationRequestResult** (IHaipServiceClient): Returns RequestId, AuthorizationRequestUri, RequestUri, Nonce, ExpiresAt.

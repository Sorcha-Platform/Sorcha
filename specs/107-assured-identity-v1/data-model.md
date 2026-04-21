# Data Model: Assured Identity v1

**Feature**: 107-assured-identity-v1
**Date**: 2026-04-20

## Scope

This document defines the entities, fields, relationships, validation rules, and state transitions introduced or modified by this feature. Entities that already exist in the platform (Blueprint, Action, Participant, Instance, Wallet, Persona) are referenced rather than redefined.

## Credential types

### AssuredIdentityCredential

The canonical person-identity credential issued by a government (or equivalent) organisation to a late-bound citizen applicant.

| Claim | Type | Required | Selectively disclosable | Source on issue |
|---|---|---|---|---|
| `givenName` | string | Yes | Yes | Submission payload `/name/givenName` |
| `middleName` | string | No | Yes | Submission payload `/name/middleName` |
| `familyName` | string | Yes | Yes | Submission payload `/name/familyName` |
| `fullName` | string | Yes (derived) | Yes | Renderer-derived from given + middle + family; included in payload |
| `dateOfBirth` | string (ISO 8601 date) | Yes | Yes | Submission payload `/dateOfBirth` |
| `email` | string (RFC 5322) | Yes | Yes | Submission payload `/email` |
| `address` | object (see below) | Yes | Yes (whole object) | Submission payload `/address` |
| `portrait` | string (base64 JPEG) | No | Yes | Submission payload `/portrait/tokenImageBase64` (only if citizen provided a photo and token size ≤ 20KB) |

**Address sub-object shape**:

| Field | Type | Required | Selectively disclosable within address |
|---|---|---|---|
| `line1` | string | Yes | — |
| `line2` | string | No | — |
| `town` | string | Yes | — |
| `region` | string | No | — |
| `postcode` | string | Yes | — |
| `country` | string (ISO 3166-1 alpha-2) | Yes | — |

The address is disclosed or withheld as a unit in v1. Sub-field selective disclosure is out of scope (noted in spec).

**Issuer**: `did:sorcha:org:<wallet-of-issuing-org>` (existing DID scheme).
**Expiry**: None. Identity credentials do not expire in v1; revocation uses the existing Feature 079 revocation-transaction pipeline.
**Format**: SD-JWT VC.
**Key binding**: Holder's wallet key (`cnf` claim), per standard HAIP pattern.

**State transitions** (per existing credential lifecycle; this feature adds no new states):
- Issued → Active (on holder accept, per Feature 106 for register-native or standard OpenID4VCI for HAIP external)
- Active → Revoked (via Feature 079 revocation transaction)

**Replaces**: `VerifiedCitizenCredential` (schema-first type from `walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json`) and `AssuredPersonCredential` (from `walkthroughs/HaipVerifiedCitizen/blueprints/assured-person.json`). Both are removed in Phase 7.

### DrivingLicenceCredential

Issued by Acme Licensing Co. after verification of a presented `AssuredIdentityCredential`.

| Claim | Type | Required | Selectively disclosable | Source on issue |
|---|---|---|---|---|
| `licenceNumber` | string | Yes | Yes | Issuing-side generated at approval time |
| `vehicleClass` | string (enum: `Car (B)`, `Motorcycle (A)`, `Lorry (C)`, `Bus (D)`) | Yes | Yes | Submission payload `/vehicleClass` |
| `issuedDate` | string (ISO 8601 date) | Yes | Yes | Issuing-side generated (today at approval) |
| `expiryDate` | string (ISO 8601 date) | Yes | Yes | `issuedDate` + 10 years |
| `holderName` | string | Yes | Yes | Carried forward from presented Assured Identity claim `givenName + familyName` |
| `holderDateOfBirth` | string | Yes | Yes | Carried forward from presented Assured Identity claim `dateOfBirth` |
| `holderPortrait` | string (base64 JPEG, ≤20KB) | No | Yes | Carried forward from presented Assured Identity `portrait` claim (if the citizen elected to disclose it) |

**Issuer**: `did:sorcha:org:<wallet-of-licensing-org>`.
**Expiry**: 10 years (`P10Y` on the credential).
**Format**: SD-JWT VC.
**Key binding**: Holder's wallet key (same wallet that presented the Assured Identity).

**Presentation requirement on Phase 2's verification action**:
- Credential type: `AssuredIdentityCredential`
- Required claims: `givenName`, `familyName`, `dateOfBirth`, `portrait` (if the citizen chose to include it in their Assured Identity)
- Not requested: `email`, `address`, `middleName`, `fullName`
- Revocation check policy: `FailClosed`

## Schema extensions

### XReviewExtension

A new blueprint schema extension marking a wizard page as a read-only review summary of prior pages' values.

| Field | Type | Required | Notes |
|---|---|---|---|
| `layout` | enum: `id-card` \| `passport-page` \| `tabular` \| `receipt` | Yes | v1 implements only `id-card`; other values are reserved enum placeholders. Unknown values surface as a publish-time warning, renderer falls back to tabular minimal rendering. |
| `editable` | bool | No (default `true`) | When `true`, the renderer generates Edit-X buttons per section. When `false`, the review page is pure display (useful for issued-credential detail views). |
| `header` | object | Yes | Card header config |
| `header.issuerName` | string | Yes | e.g. "Acme Verification Co." |
| `header.credentialName` | string | Yes | e.g. "Assured Identity" |
| `header.colourTheme` | enum: `identity-navy` \| `licence-pink` \| custom | No (default `identity-navy`) | Drives the CSS custom-property set on the card root |

**Placement**: on a `type: object` page within a blueprint's `x-pages` list, alongside `x-sections` / `x-introduction` / `x-width`.

**Parsed by**: `Sorcha.Blueprint.Models.SchemaLayoutParser` (existing parser, extended to recognise `x-review`).

**Rendered by**: `Sorcha.UI.Core.Components.Forms.ReviewSummaryRenderer` (new) which dispatches by `layout` to the matching layout component (`IdCardLayout.razor` in v1).

**Semantics**:
- Page is read-only — no form state mutation
- Field values sourced from the bound `FormContext.FormData` keyed by pointer
- When `editable: true`, clicking an Edit-X button navigates the wizard back to the originating page with all prior data preserved (bound model intact)
- Rendered identically for citizen-side draft review and issuer-side pending review; action set (Submit/Edit vs Approve/Reject) derived from the hosting action's routes, not from the extension

### XFileCaptureConfig (extension to existing XFileExtension)

Adds camera-capture and token-resize fields to the existing Feature 085 `x-file` schema extension.

| Field | Type | Required | Notes |
|---|---|---|---|
| `capture` | enum: `user` \| `environment` \| null | No | Advises the renderer to default the device camera on mobile. `user` = front-facing (selfie), `environment` = rear. null = legacy (plain file picker). |
| `embedAs` | enum: `image-token-jpeg-240x320` \| null | No | Advises the renderer to produce a resized token JPEG client-side alongside the full original. null = no resize; legacy behaviour. |

**Existing fields unchanged**: `accept`, `maxSizePerFile`, `maxChunks`.

## Runtime entities (UI-side)

### IdCardLayoutConfig

Runtime record passed to `IdCardLayout.razor`. Built by `ReviewSummaryRenderer` from the parsed `XReviewExtension` plus the form context.

| Field | Type | Source |
|---|---|---|
| `IssuerName` | string | `XReviewExtension.Header.IssuerName` |
| `CredentialName` | string | `XReviewExtension.Header.CredentialName` |
| `ColourTheme` | enum | `XReviewExtension.Header.ColourTheme` (default `identity-navy`) |
| `Watermark` | enum: `Draft` \| `Pending` \| `Issued` \| null | Derived from action state (Draft on citizen's pre-submit review; Pending on assessor's review; Issued or null on wallet detail view) |
| `FieldValues` | `IDictionary<string, object?>` | Pulled from `FormContext.FormData` for fields referenced across prior pages |
| `Editable` | bool | `XReviewExtension.Editable` |
| `EditJumpTargets` | `IDictionary<string, int>` | Map of section label → originating page index (for Edit-X button navigation) |

### Portrait (payload field value)

The runtime representation of a captured photo in the action payload.

| Field | Type | Required | Notes |
|---|---|---|---|
| `FullOriginalChunkIds` | `IReadOnlyList<string>` | Yes (if photo provided) | Transaction IDs from the existing Feature 085 chunked-file upload pipeline; carries the original-resolution JPEG |
| `TokenImageBase64` | string | Yes (if photo provided) | Client-side-resized 240×320 JPEG, base64-encoded. Must be ≤ ~20KB. |
| `ContentType` | string | Yes (if photo provided) | `image/jpeg` in v1 |
| `Hash` | string (hex SHA-256) | Yes (if photo provided) | Computed client-side over the full original; propagated with chunk metadata for integrity |

**Validation rules**:
- `TokenImageBase64` length MUST be ≤ ~27KB (20KB raw × 1.37 base64 overhead)
- If `TokenImageBase64` exceeds the bound, the issuance-time claim builder refuses to include `portrait` in the credential and surfaces a warning; the credential is still issued without the portrait claim
- The full original MUST pass the existing Feature 085 file-chunks pipeline validations (accept list, size, encryption)

## Cross-peer smoke-test artefact

### CrossPeerFindings

The markdown document produced by `run-multi-peer.ps1` on every run.

| Section | Content |
|---|---|
| Frontmatter (YAML) | `run_timestamp`, `peer_a_version`, `peer_b_version`, `outcome` (enum: `pass` \| `degraded-pass` \| `fail` \| `env-failure`) |
| Topology | Description of the two-peer configuration used, register id, org DIDs |
| Timings | Per-step milestone timings: issue on peer A → docket seal → peer-B inbound detection → peer-B MyCredentials PENDING → holder Accept |
| Anomalies | Any unexpected behaviour observed (slow detection, partial replication, UI inconsistencies) |
| Reproduction notes | Commands used, environment variables, any manual intervention needed |

**Storage**: one committed baseline document `walkthroughs/AssuredIdentity/multi-peer-findings.md` representing the latest known-good run; a rolling per-run set gitignored in the same directory.

## Persistent data changes

**None at the persistence layer.** This feature adds no new database entities, no new tables, no new collections, no new Redis keys. Every piece of data flows through existing persistence (Feature 085 file chunks, Feature 106 sealed disclosures, Feature 103 schema index, Feature 092 persona profile).

## Deletions

Deleted in Phase 7:

- `walkthroughs/HaipVerifiedCitizen/` — entire directory
- `walkthroughs/HaipDrivingLicence/` — entire directory
- `VerifiedCitizenCredential` (credential type name) — no live references in code; schema-first type, removed by deleting the blueprints that declared it
- `AssuredPersonCredential` (credential type name) — same as above

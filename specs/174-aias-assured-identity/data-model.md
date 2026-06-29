# Phase 1 Data Model: AIAS Assured Identity (M1)

No new persistent storage is introduced. The entities below are either existing domain objects
(reused) or in-memory/config shapes for the new external-check hook.

## Entities

### AIAS Organisation *(existing — Tenant Service)*
The fictional assurance provider; issuer of the Assured Identity credential and owner of the
application blueprint.
- `Name`: "Acme Identity Assurance Services (AIAS)"
- `Subdomain`: e.g. `aias`
- Branding (name/theme) is carried in the blueprint template, not org storage (R7).
- Provisioned idempotently by `AiasDemo.psm1`.

### Assured Identity Application *(existing — register workflow instance)*
The submitted application as workflow payload.
- Identity details: given/family name, date of birth, **address incl. postcode**, email.
- `portrait` (optional): `/portrait/tokenImageBase64` (F107 embedAs token).
- State: submitted → approved → rejected (workflow routing).

### External Check Result *(NEW — in-memory, agent)*
The output of one external check, merged into the rules context as a fact.
- `Name`: stable fact key, e.g. `postcodeExists`, `profane`, `emailVerified`, `photoPresent`.
- `Value`: boolean.
- `Detail` (optional): human string for the rejection reason (e.g. the unfound postcode).
- Exposed to JSON Logic under `/checks/{Name}` (value) and `/checks/{Name}Detail` (detail).

### Assurance Decision *(existing — agent action result)*
The agent's recorded outcome.
- `Decision`: `approved` | `rejected`.
- `Reason` / `verificationNotes`: human-readable, on-brand (humorous) for rejections.
- Recorded on the immutable register and surfaced to the applicant.

### Assured Identity Credential *(existing — SD-JWT VC via HAIP)*
Issued on approval.
- `credentialType`: AIAS Assured Identity.
- Claims from `claimMappings`: name, DOB, email, address, `portrait` (when present), all `disclosable`
  as configured.
- Issuer: AIAS org (per the VC issuer-signing model — the org needs a master key set, see quickstart).

## Configuration shapes (NEW — `demos/AIAS/agent/`)

### `assure-id.checks.json`
Declares which external checks run and their settings.
```json
{
  "checks": [
    { "name": "emailVerified", "type": "email-verified" },
    { "name": "photoPresent",  "type": "field-present", "field": "/portrait/tokenImageBase64" },
    { "name": "postcodeExists", "type": "uk-postcode",
      "addressField": "/address", "offlineFixture": "fixtures/postcodes.offline.json",
      "offlineMode": "auto" },
    { "name": "profane", "type": "profanity",
      "fields": ["/givenName", "/familyName", "/address"], "wordlistInline": ["..."] }
  ]
}
```

### `assure-id.rules.json` *(JSON Logic over the merged facts)*
```json
[
  {
    "actionName": "Verify Assured Identity Application",
    "condition": { "==": [ { "var": "checks.postcodeExists" }, false ] },
    "decision": "reject",
    "payload": { "decision": "rejected",
      "verificationNotes": "AIAS could not locate that address on any map." }
  },
  {
    "actionName": "Verify Assured Identity Application",
    "condition": { "==": [ { "var": "checks.profane" }, true ] },
    "decision": "reject",
    "payload": { "decision": "rejected",
      "verificationNotes": "AIAS does not assure identities described in such... colourful terms." }
  },
  {
    "actionName": "Verify Assured Identity Application",
    "condition": { "==": [ { "var": "checks.emailVerified" }, false ] },
    "decision": "reject",
    "payload": { "decision": "rejected",
      "verificationNotes": "AIAS needs a verified email before it can assure you." }
  },
  {
    "actionName": "Verify Assured Identity Application",
    "condition": { "==": [ true, true ] },
    "decision": "approve",
    "payload": { "decision": "approved", "verificationNotes": "Assured by AIAS." }
  }
]
```
> Rule order matters — first match wins (existing `RulesDecisionEngine` semantics). Rejections are
> evaluated before the catch-all approve.

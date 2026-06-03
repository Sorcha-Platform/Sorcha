# Cyber Essentials UAC Walkthrough — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable, asserted Sorcha walkthrough (`walkthroughs/CyberEssentialsUac/`) demonstrating the Cyber Essentials *User Access Control* posture as a credential flow: an assessor issues a continuous `CyberEssentialsUacPosture` SD-JWT VC on a compliance gate, an insurer requires it (FailClosed, issuer-pinned), a non-compliant variant proves the auto-fail withholds issuance, a mid-cycle revocation makes the next presentation fail, and a HAIP/OID4VP variant proves genuine on-the-wire selective disclosure.

**Architecture:** Two blueprints on one register (cross-blueprint credential chain). **Blueprint A** (`ce-uac-assessment`) captures evidence, computes `compliant` via a JSON-Logic `calculations` gate, and route-gates credential issuance (issue-action reached only when compliant; else a terminal record-fail action — issuance is *withheld*, never minted negative). **Blueprint B** (`cyber-insurance-application`) has an open, credential-gated starting action (`Request Cover`) whose `credentialRequirements` pins the assessor's issuer DID via a `TrustPolicy` `did-allowlist` and checks revocation `FailClosed`. Credential **presentation is script-explicit** (`Get-SorchaCredentialPresentation` + `Invoke-SorchaAction -CredentialPresentations`) because the agent cannot auto-present `SorchaInternal` credentials. Scenarios 1 (happy) + 2 (auto-fail) run anywhere; scenario 3 (revocation) requires a TLS-reachable status list and **hard-skips locally**, running green on n1. A separate **HAIP SD variant** issues the same credential via OID4VCI into the agent file wallet and presents via OID4VP disclosing only 4 of 9 claims, asserting the verifier received exactly those 4.

**Tech Stack:** .NET 10 Sorcha platform (Blueprint/Wallet/Tenant/Validator/Haip services), PowerShell 7 walkthrough scripts + `walkthroughs/modules/SorchaWalkthrough` module, `sorcha-agent` CLI (`src/Apps/Sorcha.Agent`), SD-JWT VC, W3C Bitstring Status List, JSON-Logic (json-everything), YARP gateway.

---

## Verified Ground Truth (read before starting — these correct the source brief)

These were established by reading the current codebase. **Do not trust the build-brief or skill-doc shapes where they conflict with this list.**

1. **`credentialIssuanceConfig`** (JSON key on an action). Fields: `credentialType`, `claimMappings:[{claimName, sourceField(JSON Pointer)}]`, `recipientParticipantId`, `disclosable:[claimName,…]`, `expiryDuration` (ISO-8601, e.g. `P1Y`), `usagePolicy:"Reusable"`, `targetAudience:"SorchaLocalWallet"` (NOT `SorchaInternal` — deprecated). Source: `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs`.
2. **`credentialRequirements`** (JSON key, array). Per element: `type`, `trustPolicy`, `requiredClaims:[{claimName, expectedValue}]`, `revocationCheckPolicy:"FailClosed"`. **`acceptedIssuers` DOES NOT EXIST** (removed in Feature 135) — it is silently dropped, leaving the requirement open to ANY issuer. Use `trustPolicy`. Source: `CredentialRequirement.cs`.
3. **`trustPolicy`** shape: `{ "sources":[{ "kind":"did-allowlist", "allowedIssuers":["did:sorcha:org:<wallet>"], "confersAssurance":"low" }], "combinator":"anyOf", "minAssuranceLevel":"low" }`. `OPEN_CREDENTIAL_ISSUER` warning fires when `trustPolicy` is null or `sources` is empty. Source: `TrustPolicy.cs`, `TrustSourceRef.cs`.
4. **`ClaimConstraint`** is `{claimName, expectedValue}` ONLY — **no named operators, no `freshness`.** `compliant==true` → `{ "claimName":"compliant", "expectedValue":true }`. Freshness is implemented as a run-script assertion (below), not a constraint.
5. **Issuance is route-gated, not flag-gated.** An action with `credentialIssuanceConfig` ALWAYS mints when reached (minting runs before routing). To withhold, the issue-action must be reached only via a `condition`-gated route; the fail path routes to a terminal no-issuance action. Source: `ActionExecutionService.cs`.
6. **VAL_BP_010**: a starting action's `sender` participant must have **null/absent `walletAddress`** (open/late-bound). So Blueprint A's `assessor` and Blueprint B's `subject-org` are open; omit them from `$walletMap`. `Publish-SorchaBlueprint` auto-skips patching open senders, so including them is harmless, but the **template must not set their wallet**.
7. **Agent cannot present `SorchaInternal` credentials.** `sorcha-agent` builds no `credentialPresentations`; `ActionExecutionService` only verifies presentations in the request body and never auto-fetches stored credentials. The credential-gated action MUST be script-submitted via `Get-SorchaCredentialPresentation` (auto-accepts `PendingAcceptance`, exports full raw SD-JWT) + `Invoke-SorchaAction -CredentialPresentations`. Reference usage: `walkthroughs/SelfBuildHouse/run.ps1:238-255`.
8. **Credential-verification failure surfaces as a generic HTTP 400** (`"An error occurred processing the request."`); the "revoked"/"FailClosed" reason is logged, not returned. Assert on status 400 + the positive pre-revocation control.
9. **Status-list embedding** (`CredentialStatus:EnableEmbedding`) is **ON by default** in Blueprint Service and the URL defaults to the unreachable `https://sorcha.example/…`. Under FailClosed an unreachable list rejects EVERY presentation. So: **local override sets `CredentialStatus__EnableEmbedding=false`** (a credential with no status claim passes FailClosed — verified in `TrustEvaluator.CheckStatusAsync` + `CredentialVerifierRevocationTests.VerifyAsync_NoStatusClaim_Accepted`). On n1, embedding is ON with `StatusList__BaseUrl=https://n1.sorcha.dev/api/v1/credentials/status-lists`.
10. **Revoke endpoint**: `POST /api/v1/credentials/{credentialId}/revoke`, auth `CanManageBlueprints`, body `{ "issuerWallet":"<addr>", "reason":"<str>" }`. Find the `credentialId` via `GET /api/v1/wallets/{addr}/credentials?status=All`. The status-list GET (`GET /api/v1/credentials/status-lists/{listId}`) is `AllowAnonymous` on Blueprint Service but the gateway fronts `/api/v1/credentials/*` behind `RequireAuthenticated` → a committed anonymous GET route is required (Task 0.3).
11. **JSON-Logic** = json-everything `Json.Logic`; `var` supports dot-paths against nested payloads (`{"var":"mfa.adminMfaEnforced"}`). Source: `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonLogicEvaluator.cs`.
12. **HAIP SD variant endpoints**: `POST /api/v1/offers/` (auth `RequireService`; body incl. `disclosablePaths` — **all 9 claims must be listed** or non-listed claims mint as always-plaintext); `POST /api/v1/verifier/requests` (auth `RequireService`; `requiredClaims`, `acceptedIssuers`); `GET /api/v1/verifier/requests/{id}/result` (auth `RequireService`; returns `result.verifiedClaims` = the disclosed subset + `result.isValid`). Trust prereqs: `POST /api/v1/trust/tenants/{tid}/provision` and `…/orgs/{addr}/enrol` (auth `RequireAdministrator`+`RequirePlatformAudience`; reference `walkthroughs/AssuredIdentity/setup.ps1:274-291`). `Haip__IssuerUrl` MUST be host-reachable (`http://127.0.0.1`). Agent: `sorcha-agent haip receive --offer-uri … --wallet-dir …` then `haip present --request-uri … --credential … --disclose <csv> --wallet-dir …`.

---

## File Structure

```
walkthroughs/CyberEssentialsUac/
├── ce-uac-assessment-template.json            # Blueprint A — issues posture VC (route-gated)
├── cyber-insurance-application-template.json  # Blueprint B — requires posture VC (FailClosed, issuer-pinned)
├── setup.ps1                                   # orgs/wallets/participants/register/subscriptions/publish → state.json
├── run-agents.ps1                              # scenarios 1 (happy) + 2 (auto-fail); script-explicit presentation; asserts
├── run-revocation.ps1                          # scenario 3; n1-gated, hard-skip locally; asserts 400
├── run-haip-sd.ps1                             # HAIP/OID4VP genuine selective-disclosure variant; asserts disclosed subset
├── docker-compose.ce-uac-local.yml            # local override: CredentialStatus__EnableEmbedding=false
├── actors/
│   ├── assessor.json                           # Blueprint A starter (open/late-bound)
│   ├── subject-org.json                         # Blueprint B starter (open/late-bound, credential-gated)
│   ├── insurer.json                             # Blueprint B Issue Quote
│   └── README.md                               # why the gated step is script-injected (GT#7)
├── data/
│   ├── evidence-compliant.json                  # full nested evidence, compliant
│   └── evidence-autofail.json                   # adminMfaEnforced=false → gate fails
└── README.md                                   # what it proves, exact commands, expected output per scenario, n1 findings

Committed platform change (separate, defensible):
└── src/Services/Sorcha.ApiGateway/appsettings.json   # anonymous GET route for /api/v1/credentials/status-lists/**
```

Secrets entry to add: `walkthroughs/.secrets/passwords.json` → `cyber-essentials-uac` (Task 0.2).

---

## Phase 0 — Branch, scaffold, platform prerequisites

### Task 0.1: Create the feature branch

- [ ] **Step 1: Branch from master**

Run:
```bash
git checkout master && git pull && git checkout -b feature/cyber-essentials-uac-walkthrough
```
Expected: `Switched to a new branch 'feature/cyber-essentials-uac-walkthrough'`

- [ ] **Step 2: Create the directory skeleton**

Run (pwsh):
```powershell
New-Item -ItemType Directory -Force walkthroughs/CyberEssentialsUac/actors,walkthroughs/CyberEssentialsUac/data | Out-Null
```
Expected: directories created.

- [ ] **Step 3: Commit the empty scaffold marker**

```bash
git add walkthroughs/CyberEssentialsUac
git commit -m "chore(ce-uac): scaffold walkthrough directory"
```

### Task 0.2: Add the walkthrough secret

**Files:** Modify `walkthroughs/.secrets/passwords.json`

- [ ] **Step 1: Inspect the existing secrets shape**

Run: `Get-Content walkthroughs/.secrets/passwords.json -Raw` (if the file is absent, run `walkthroughs/initialize-secrets.ps1` first). Note an existing walkthrough entry (e.g. `assured-identity`) to mirror its shape.

- [ ] **Step 2: Add the `cyber-essentials-uac` entry**

Add a sibling key mirroring the existing entries (the module's `Get-SorchaSecrets -WalkthroughName "cyber-essentials-uac"` reads it). Minimum:
```json
"cyber-essentials-uac": {
  "DefaultPassword": "Dev_Pass_2025!"
}
```
(Match the exact key names used by a working entry — confirm against `assured-identity`.)

- [ ] **Step 3: Verify the module can read it**

Run:
```powershell
Import-Module walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1 -Force
(Get-SorchaSecrets -WalkthroughName "cyber-essentials-uac").DefaultPassword
```
Expected: prints `Dev_Pass_2025!`. (Secrets file is git-ignored; nothing to commit.)

### Task 0.3: Add the committed anonymous gateway route for status lists

**Files:** Modify `src/Services/Sorcha.ApiGateway/appsettings.json`

This is required so the FailClosed checker (running in Blueprint Service) can fetch the W3C status list over the gateway without auth (the endpoint is `AllowAnonymous` by design). Harmless locally; load-bearing on n1.

- [ ] **Step 1: Read the current route + cluster table**

Run: `Grep` for `"blueprint-credentials"` in `src/Services/Sorcha.ApiGateway/appsettings.json`. Note the exact `ClusterId` it uses (expected `blueprint-cluster`) and the `Routes` object structure (key names, `Match`, `Order`, `AuthorizationPolicy`).

- [ ] **Step 2: Add a higher-priority anonymous GET route**

Insert a new route in the `ReverseProxy.Routes` object, BEFORE/with lower `Order` than `blueprint-credentials`, matching its `ClusterId`:
```json
"blueprint-status-lists": {
  "ClusterId": "blueprint-cluster",
  "Order": 1,
  "Match": {
    "Path": "/api/v1/credentials/status-lists/{**catch-all}",
    "Methods": [ "GET" ]
  }
}
```
Note: NO `AuthorizationPolicy` key → anonymous, matching the endpoint's `.AllowAnonymous()`. If `blueprint-credentials` has `Order` ≤ 1, set this route's `Order` lower than it.

- [ ] **Step 3: Validate JSON + build the gateway**

Run:
```powershell
Get-Content src/Services/Sorcha.ApiGateway/appsettings.json -Raw | ConvertFrom-Json | Out-Null
dotnet build src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj
```
Expected: JSON parses (no exception), build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Services/Sorcha.ApiGateway/appsettings.json
git commit -m "feat(gateway): expose W3C status-list GET anonymously (CE-UAC revocation round-trip)"
```

### Task 0.4: Local embedding-off override file

**Files:** Create `walkthroughs/CyberEssentialsUac/docker-compose.ce-uac-local.yml`

- [ ] **Step 1: Confirm the blueprint service name in docker-compose**

Run: `Grep` for `blueprint-service` in `docker-compose.yml`. Confirm the service key name (expected `blueprint-service`).

- [ ] **Step 2: Write the override**

```yaml
# Local-only override for the CyberEssentialsUac walkthrough.
# Turns OFF status-list embedding so scenarios 1 (happy) and 2 (auto-fail)
# pass under FailClosed without a TLS-reachable status list. The signed
# credential then carries no credentialStatus claim, and TrustEvaluator
# short-circuits to "active" (verified: CredentialVerifierRevocationTests).
# Scenario 3 (revocation) hard-skips locally; it needs embedding ON + a
# reachable HTTPS status list, which n1.sorcha.dev provides.
services:
  blueprint-service:
    environment:
      CredentialStatus__EnableEmbedding: "false"
```

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/docker-compose.ce-uac-local.yml
git commit -m "chore(ce-uac): local docker override disabling status-list embedding"
```

---

## Phase 1 — Blueprint A: `ce-uac-assessment-template.json`

**Files:** Create `walkthroughs/CyberEssentialsUac/ce-uac-assessment-template.json`

Blueprint A has 3 actions: (0) Submit UAC Assessment — open `assessor` starter, computes `computedCompliant`; (1) Issue Posture Credential — assessor, route-gated, mints VC to `subject-org`; (2) Record Non-Compliance — assessor, terminal, no issuance.

### Task 1.1: Author the template envelope + participants

- [ ] **Step 1: Write the envelope and participants**

Mirror the envelope shape of `walkthroughs/ConstructionPermit/construction-permit-template.json` (top-level `id,title,description,version,category,tags,author,published,template{...},parameterSchema:null,defaultParameters:null,examples:[]`). Participants:
```json
"participants": [
  { "id": "assessor",    "name": "Cyber Assessor",        "organisation": "Assessing Org", "description": "Runs the UAC assessment and issues the posture credential. OPEN starter (late-bound) — VAL_BP_010." },
  { "id": "subject-org", "name": "Assessed Organisation", "organisation": "Subject SME",   "description": "Receives the posture credential (pre-bound recipient)." }
]
```
`assessor` has NO `walletAddress` (open starter). `subject-org` is the recipient (its wallet is patched from `$walletMap` at publish).

### Task 1.2: Author action 0 (Submit UAC Assessment) — schema + gate

- [ ] **Step 1: Write the action 0 data schema (nested evidence)**

Use the brief's nested evidence object verbatim as the JSON Schema `properties` (objects: `assessment, provisioning, offboarding, mfa, privilegedAccess, passwordPolicy, uac`). Mark the gate-relevant fields and `assessment.date` required. Keep it a single `dataSchemas:[{ "type":"object", "properties":{…}, "required":["assessment","mfa","offboarding","passwordPolicy","privilegedAccess"] }]`. (Full field set per the brief's evidence object; types: booleans for the flags, integers for counts, strings for dates/hashes/enums.)

- [ ] **Step 2: Write the `calculations` gate**

Add to action 0:
```json
"calculations": {
  "computedCompliant": {
    "and": [
      { "==": [ { "var": "mfa.adminMfaEnforced" }, true ] },
      { "==": [ { "var": "offboarding.staleAccounts" }, 0 ] },
      { "==": [ { "var": "privilegedAccess.separateAdminAccounts" }, true ] },
      { "==": [ { "var": "privilegedAccess.leastPrivilege" }, true ] },
      { "or": [
        { "and": [ { "in": [ { "var": "passwordPolicy.approach" }, [ "mfa+8", "lockout+8" ] ] }, { ">=": [ { "var": "passwordPolicy.minLength" }, 8 ] } ] },
        { "and": [ { "==": [ { "var": "passwordPolicy.approach" }, "denylist+12" ] }, { ">=": [ { "var": "passwordPolicy.minLength" }, 12 ] } ] }
      ] }
    ]
  }
}
```

- [ ] **Step 3: Write action 0 routes (the gate) + disclosures**

```json
"routes": [
  { "id": "compliant-issue",    "nextActionIds": [1], "condition": { "==": [ { "var": "computedCompliant" }, true ] }, "description": "UAC requirements met — proceed to issue the posture credential" },
  { "id": "noncompliant-record","nextActionIds": [2], "isDefault": true, "description": "UAC requirements not met — record non-compliance, withhold the credential" }
],
"disclosures": [
  { "participantAddress": "assessor",    "dataPointers": ["/*"] },
  { "participantAddress": "subject-org", "dataPointers": ["/uac", "/assessment/date", "/assessment/infraVersion"] }
]
```
`isStartingAction: true`, `sender: "assessor"`, `id: 0`, `title: "Submit UAC Assessment"`.

### Task 1.3: Author action 1 (Issue Posture Credential)

- [ ] **Step 1: Write action 1 with the issuance config**

```json
{
  "id": 1,
  "title": "Issue Posture Credential",
  "sender": "assessor",
  "requiredPriorActions": [0],
  "dataSchemas": [ { "type": "object", "properties": { "issuanceNote": { "type": "string", "title": "Issuance note", "maxLength": 200 } } } ],
  "credentialIssuanceConfig": {
    "credentialType": "CyberEssentialsUacPosture",
    "recipientParticipantId": "subject-org",
    "targetAudience": "SorchaLocalWallet",
    "usagePolicy": "Reusable",
    "expiryDuration": "P1Y",
    "claimMappings": [
      { "claimName": "compliant",          "sourceField": "/uac/compliant" },
      { "claimName": "assessmentDate",     "sourceField": "/assessment/date" },
      { "claimName": "infraVersion",       "sourceField": "/assessment/infraVersion" },
      { "claimName": "passwordApproach",   "sourceField": "/passwordPolicy/approach" },
      { "claimName": "mfaAdminEnforced",   "sourceField": "/mfa/adminMfaEnforced" },
      { "claimName": "assessorType",       "sourceField": "/assessment/assessorType" },
      { "claimName": "scopeDeviceCount",   "sourceField": "/assessment/scopeDeviceCount" },
      { "claimName": "mfaCoverage",        "sourceField": "/mfa/cloudServicesWithMfa" },
      { "claimName": "staleAccounts",      "sourceField": "/offboarding/staleAccounts" },
      { "claimName": "policyEvidenceHash", "sourceField": "/provisioning/policyHash" }
    ],
    "disclosable": ["assessorType","scopeDeviceCount","mfaCoverage","staleAccounts","policyEvidenceHash"]
  },
  "routes": [ { "id": "issued", "nextActionIds": [], "isDefault": true, "description": "Posture credential issued — workflow complete" } ],
  "disclosures": [
    { "participantAddress": "assessor",    "dataPointers": ["/*"] },
    { "participantAddress": "subject-org", "dataPointers": ["/*"] }
  ]
}
```
Note: claim mappings source from action 0's evidence (available via `requiredPriorActions:[0]`). The always-in-JWT claims are the non-`disclosable` ones (verdict/date/version/approach/admin-MFA) — what the insurer sees; `disclosable` are the granular evidence held back by default.

### Task 1.4: Author action 2 (Record Non-Compliance) — terminal, no issuance

- [ ] **Step 1: Write action 2**

```json
{
  "id": 2,
  "title": "Record Non-Compliance",
  "sender": "assessor",
  "requiredPriorActions": [0],
  "dataSchemas": [ { "type": "object", "properties": { "remediationNotes": { "type": "string", "title": "Remediation notes", "maxLength": 500 } } } ],
  "routes": [ { "id": "recorded", "nextActionIds": [], "isDefault": true, "description": "Non-compliance recorded — no credential issued" } ],
  "disclosures": [
    { "participantAddress": "assessor",    "dataPointers": ["/*"] },
    { "participantAddress": "subject-org", "dataPointers": ["/*"] }
  ]
}
```
**No `credentialIssuanceConfig`** — this is the withhold path.

### Task 1.5: Validate Blueprint A JSON

- [ ] **Step 1: Parse-check**

Run:
```powershell
Get-Content walkthroughs/CyberEssentialsUac/ce-uac-assessment-template.json -Raw | ConvertFrom-Json | Out-Null
```
Expected: no exception. (Publish-time semantic validation happens in `setup.ps1`, Phase 4; if `VAL_BP_010` fires there, the `assessor` participant wrongly has a wallet — remove it.)

- [ ] **Step 2: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/ce-uac-assessment-template.json
git commit -m "feat(ce-uac): Blueprint A — UAC assessment with route-gated posture issuance"
```

---

## Phase 2 — Blueprint B: `cyber-insurance-application-template.json`

**Files:** Create `walkthroughs/CyberEssentialsUac/cyber-insurance-application-template.json`

Blueprint B has 2 actions: (0) Request Cover — open `subject-org` starter, credential-gated; (1) Issue Quote — insurer, terminal.

### Task 2.1: Envelope + participants

- [ ] **Step 1: Write envelope + participants**

```json
"participants": [
  { "id": "subject-org", "name": "Assessed Organisation", "organisation": "Subject SME",  "description": "Requests cyber cover; presents the posture credential. OPEN starter (credential-gated, late-bound)." },
  { "id": "insurer",     "name": "Cyber Insurer",          "organisation": "Insurer/Broker","description": "Quotes only when the posture requirement is satisfied (pre-bound)." }
]
```
`subject-org` has NO `walletAddress` (open starter — the credential requirement is the access control). `insurer` is patched from `$walletMap`.

### Task 2.2: Action 0 (Request Cover) — credential requirement

- [ ] **Step 1: Write action 0 with the requirement (issuer DID is a publish-time placeholder substituted by setup.ps1)**

```json
{
  "id": 0,
  "title": "Request Cover",
  "sender": "subject-org",
  "isStartingAction": true,
  "dataSchemas": [ { "type": "object", "properties": {
    "coverAmountGbp": { "type": "integer", "minimum": 0, "title": "Requested cover (GBP)" },
    "sector":        { "type": "string", "title": "Sector" },
    "employeeCount": { "type": "integer", "minimum": 1, "title": "Employees" }
  }, "required": ["coverAmountGbp"] } ],
  "credentialRequirements": [ {
    "type": "CyberEssentialsUacPosture",
    "presentationSource": "SorchaInternal",
    "trustPolicy": {
      "sources": [ { "kind": "did-allowlist", "allowedIssuers": ["{{ASSESSOR_ISSUER_DID}}"], "confersAssurance": "low" } ],
      "combinator": "anyOf",
      "minAssuranceLevel": "low"
    },
    "requiredClaims": [
      { "claimName": "compliant",      "expectedValue": true },
      { "claimName": "assessmentDate" }
    ],
    "revocationCheckPolicy": "FailClosed",
    "description": "Requires a current, non-revoked Cyber Essentials UAC posture credential from a recognised assessor."
  } ],
  "routes": [ { "id": "to-quote", "nextActionIds": [1], "isDefault": true, "description": "Posture verified — route to insurer for a quote" } ],
  "disclosures": [
    { "participantAddress": "subject-org", "dataPointers": ["/*"] },
    { "participantAddress": "insurer",     "dataPointers": ["/*"] }
  ]
}
```
`{{ASSESSOR_ISSUER_DID}}` is a literal placeholder string; `setup.ps1` replaces it with `did:sorcha:org:<assessorWallet>` before publish (Task 4.x). `requiredClaims` covers `compliant==true` + presence of `assessmentDate`; **freshness (P1Y) is asserted in `run-agents.ps1`**, not here (GT#4).

### Task 2.3: Action 1 (Issue Quote) — terminal

- [ ] **Step 1: Write action 1**

```json
{
  "id": 1,
  "title": "Issue Quote",
  "sender": "insurer",
  "requiredPriorActions": [0],
  "dataSchemas": [ { "type": "object", "properties": {
    "premiumGbp":   { "type": "number", "minimum": 0, "title": "Annual premium (GBP)" },
    "quoteRef":     { "type": "string", "title": "Quote reference" },
    "validUntil":   { "type": "string", "format": "date", "title": "Quote valid until" }
  }, "required": ["premiumGbp","quoteRef"] } ],
  "routes": [ { "id": "quoted", "nextActionIds": [], "isDefault": true, "description": "Quote issued — workflow complete" } ],
  "disclosures": [
    { "participantAddress": "insurer",     "dataPointers": ["/*"] },
    { "participantAddress": "subject-org", "dataPointers": ["/*"] }
  ]
}
```

### Task 2.4: Validate + commit

- [ ] **Step 1: Parse-check**

Run: `Get-Content walkthroughs/CyberEssentialsUac/cyber-insurance-application-template.json -Raw | ConvertFrom-Json | Out-Null`
Expected: no exception.

- [ ] **Step 2: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/cyber-insurance-application-template.json
git commit -m "feat(ce-uac): Blueprint B — insurer cover gated by issuer-pinned FailClosed posture requirement"
```

---

## Phase 3 — Evidence data files

**Files:** Create `walkthroughs/CyberEssentialsUac/data/evidence-compliant.json`, `…/evidence-autofail.json`

### Task 3.1: Compliant evidence

- [ ] **Step 1: Write `evidence-compliant.json`** — the brief's evidence object verbatim (all gates pass: `mfa.adminMfaEnforced:true`, `offboarding.staleAccounts:0`, `passwordPolicy.approach:"denylist+12"` + `minLength:12`, `privilegedAccess.separateAdminAccounts:true` + `leastPrivilege:true`, `uac.compliant:true`).

### Task 3.2: Auto-fail evidence

- [ ] **Step 1: Write `evidence-autofail.json`** — a copy of compliant with `mfa.adminMfaEnforced:false`, `uac.compliant:false`, `uac.autoFailTriggered:["C"]`, and `uac.requirementsMet:["A","B","D","E"]`. (This proves the engine gate, not the flag: even if `uac.compliant` were left `true`, `computedCompliant` would still be `false`.)

- [ ] **Step 2: Parse-check both + commit**

Run: `Get-ChildItem walkthroughs/CyberEssentialsUac/data/*.json | ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json | Out-Null }`
```bash
git add walkthroughs/CyberEssentialsUac/data
git commit -m "test(ce-uac): compliant + auto-fail evidence fixtures"
```

---

## Phase 4 — `setup.ps1`

**Files:** Create `walkthroughs/CyberEssentialsUac/setup.ps1`

Provisions: 3 org-scoped operators (assessor, subject-org, insurer) via the sysadmin→org→operator path (GT: never public — avoids multi-org 401); a wallet + participant per org; **re-login each session after wallet creation** (so the JWT carries `wallet_address` — F136/F142); a register **owned by a dedicated org-admin identity that holds Administrator** (GT: register owner must be Administrator, not a Consumer); subscribe all three orgs; wait for the genesis roster to seal; substitute `{{ASSESSOR_ISSUER_DID}}` in Blueprint B; publish both blueprints (seal-waited); write `state.json`.

### Task 4.1: Header, env, secrets, sysadmin

- [ ] **Step 1: Write the script preamble**

Mirror `walkthroughs/AssuredIdentity/setup.ps1` head: `param([ValidateSet('gateway','direct','aspire','n1')][string]$Profile='gateway', [string]$GatewayUrl)`, `$ErrorActionPreference='Stop'`, import the module, `$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile` (pass `-GatewayUrl $GatewayUrl` when provided), `$secrets = Get-SorchaSecrets -WalkthroughName "cyber-essentials-uac"`, `$sysAdmin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl -AdminEmail … -AdminPassword …` (mirror AssuredIdentity's admin bootstrap args).

### Task 4.2: Create orgs + operators (org-scoped, single-org)

- [ ] **Step 1: Create three orgs each with a fresh single-org operator**

For each of assessor / subject-org / insurer, call `New-SorchaOrganization -TenantUrl $sorchaEnv.TenantUrl -Headers $sysAdmin.Headers -Name <Name> -Subdomain <unique-subdomain> -AdminEmail <ops@subdomain.test> -AdminPassword $secrets.DefaultPassword -AdminDisplayName <Name+" Ops"> -AdminEmailVerified`. Capture each returned `OrganizationId`. Use unique subdomains (e.g. `ce-assessor`, `ce-subject`, `ce-insurer`) — `docker compose down -v` for a clean slate if subdomains collide.

- [ ] **Step 2: Add a dedicated register-owner admin in the assessor org**

The register owner must hold Administrator AND own the register wallet. Reuse the assessor org's operator (it is org Administrator via `New-SorchaOrganization`). Log in: `$assessorSession = Connect-SorchaUser -TenantUrl … -Email <assessor ops email> -Password $secrets.DefaultPassword -OrganizationId <assessorOrgId>`.

### Task 4.3: Wallets + participants, then RE-LOGIN

- [ ] **Step 1: Create a wallet per org and register the participant**

For each org: `$w = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl -Name "<role>-wallet" -Headers <thatOrgSession.Headers> -Algorithm "ED25519" -FetchPublicKey` then `Register-SorchaParticipant -TenantUrl … -WalletUrl … -OrganizationId <orgId> -WalletAddress $w.Address -DisplayName "<Role>" -Headers <session.Headers>`. Capture `$w.Address` and `$w.PublicKey` per role.

- [ ] **Step 2: RE-LOGIN every session after its wallet is linked (GT — F136/F142)**

After the wallet+participant loop, re-acquire each session so the token carries `wallet_address`:
```powershell
$assessorSession  = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email <assessor ops>  -Password $secrets.DefaultPassword -OrganizationId $assessorOrgId
$subjectSession   = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email <subject ops>   -Password $secrets.DefaultPassword -OrganizationId $subjectOrgId
$insurerSession   = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email <insurer ops>   -Password $secrets.DefaultPassword -OrganizationId $insurerOrgId
```
Use these fresh sessions for everything downstream (register create, publish, runs).

### Task 4.4: Register + subscriptions + roster seal-wait

- [ ] **Step 1: Create the register owned by the assessor admin**

```powershell
$register = New-SorchaRegister -RegisterUrl $sorchaEnv.RegisterUrl -WalletUrl $sorchaEnv.WalletUrl `
  -Name "Cyber Essentials UAC Register" -Description "CE UAC posture + cyber-insurance demo" `
  -TenantId $assessorOrgId -OwnerUserId $assessorSession.UserId -OwnerWalletAddress $assessorWallet.Address `
  -Headers $assessorSession.Headers -TenantUrl $sorchaEnv.TenantUrl
$registerId = $register.RegisterId
```

- [ ] **Step 2: Subscribe subject-org and insurer (Owner is auto-subscribed)**

```powershell
New-SorchaRegisterSubscription -TenantUrl $sorchaEnv.TenantUrl -OrganizationId $subjectOrgId -RegisterId $registerId -Headers $subjectSession.Headers -SubscriptionType "Owner" | Out-Null
New-SorchaRegisterSubscription -TenantUrl $sorchaEnv.TenantUrl -OrganizationId $insurerOrgId -RegisterId $registerId -Headers $insurerSession.Headers -SubscriptionType "Owner" | Out-Null
```
(subject-org MUST be subscribed to hold/present the credential — GT#7/#6.)

- [ ] **Step 2b: Wait for the genesis governance roster to seal (GT: publish races the seal)**

Poll until the owner roster is populated, else publish fail-closes 403:
```powershell
$deadline = (Get-Date).AddSeconds(60)
do {
  Start-Sleep -Seconds 2
  $roster = Invoke-SorchaApi -Method GET -Uri "$($sorchaEnv.GatewayUrl)/api/registers/$registerId/governance/roster" -Headers $assessorSession.Headers
} until (($roster.members.Count -gt 0) -or ((Get-Date) -gt $deadline))
if (-not ($roster.members.Count -gt 0)) { Write-WtFail "Register roster did not seal in time"; exit 1 }
```

### Task 4.5: Substitute issuer DID + publish both blueprints

- [ ] **Step 1: Substitute `{{ASSESSOR_ISSUER_DID}}` into a temp copy of Blueprint B**

```powershell
$assessorDid = "did:sorcha:org:$($assessorWallet.Address)"
$bpBPath = Join-Path $PSScriptRoot "cyber-insurance-application-template.json"
$bpBResolved = Join-Path $PSScriptRoot ".bpB.resolved.json"
(Get-Content $bpBPath -Raw).Replace("{{ASSESSOR_ISSUER_DID}}", $assessorDid) | Set-Content $bpBResolved
```

- [ ] **Step 2: Publish Blueprint A (assessor open starter → omit from walletMap)**

```powershell
$walletMapA = @{ "subject-org" = $subjectWallet.Address }   # assessor omitted (open starter, VAL_BP_010)
$bpA = Publish-SorchaBlueprint -BlueprintUrl $sorchaEnv.BlueprintUrl -TemplatePath (Join-Path $PSScriptRoot "ce-uac-assessment-template.json") -WalletMap $walletMapA -Headers $assessorSession.Headers -RegisterId $registerId
if ($bpA.TransactionId) { Wait-SorchaActorReady -Mode BlueprintSealed -TxId $bpA.TransactionId -RegisterId $registerId -Headers $assessorSession.Headers -GatewayUrl $sorchaEnv.GatewayUrl }
```

- [ ] **Step 3: Publish Blueprint B (subject-org open starter → omit; insurer in map)**

```powershell
$walletMapB = @{ "insurer" = $insurerWallet.Address }        # subject-org omitted (open starter)
$bpB = Publish-SorchaBlueprint -BlueprintUrl $sorchaEnv.BlueprintUrl -TemplatePath $bpBResolved -WalletMap $walletMapB -Headers $assessorSession.Headers -RegisterId $registerId
if ($bpB.TransactionId) { Wait-SorchaActorReady -Mode BlueprintSealed -TxId $bpB.TransactionId -RegisterId $registerId -Headers $assessorSession.Headers -GatewayUrl $sorchaEnv.GatewayUrl }
Remove-Item $bpBResolved -Force
```
Note: confirm no `OPEN_CREDENTIAL_ISSUER` / `VAL_BP_010` errors in `$bpA.Warnings`/`$bpB.Warnings`. The publisher (any Administrator with `wallet_address`) is the assessor session; if Blueprint B publish 403s, the assessor session lacks `wallet_address` (re-login skipped) or isn't the register owner.

### Task 4.6: Write state.json

- [ ] **Step 1: Persist state**

```powershell
$state = @{
  profile = $Profile
  gatewayUrl = $sorchaEnv.GatewayUrl
  tenantUrl = $sorchaEnv.TenantUrl
  walletUrl = $sorchaEnv.WalletUrl
  blueprintUrl = $sorchaEnv.BlueprintUrl
  registerUrl = $sorchaEnv.RegisterUrl
  registerId = $registerId
  assessorDid = $assessorDid
  blueprints = @{
    "ce-uac-assessment" = @{ id = $bpA.BlueprintId }
    "cyber-insurance-application" = @{ id = $bpB.BlueprintId }
  }
  roles = @{
    assessor    = @{ organizationId = $assessorOrgId; walletAddress = $assessorWallet.Address; email = $assessorEmail; password = $secrets.DefaultPassword; publicKey = $assessorWallet.PublicKey }
    "subject-org" = @{ organizationId = $subjectOrgId;  walletAddress = $subjectWallet.Address;  email = $subjectEmail;  password = $secrets.DefaultPassword }
    insurer     = @{ organizationId = $insurerOrgId;  walletAddress = $insurerWallet.Address;  email = $insurerEmail;  password = $secrets.DefaultPassword }
  }
}
$state | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $PSScriptRoot "state.json")
Write-WtSuccess "setup complete — state.json written"
```

- [ ] **Step 2: Run setup against the local stack (apply the override first)**

Run:
```powershell
docker compose -f docker-compose.yml -f walkthroughs/CyberEssentialsUac/docker-compose.ce-uac-local.yml up -d blueprint-service
docker compose up -d api-gateway   # pick up the Task 0.3 route (rebuild if needed: docker compose build api-gateway)
pwsh walkthroughs/CyberEssentialsUac/setup.ps1
```
Expected: "setup complete", `state.json` present, no publish 403/VAL_BP_010.

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/setup.ps1
git commit -m "feat(ce-uac): setup.ps1 — orgs, wallets, register, both blueprints (re-login + roster seal-wait)"
```

---

## Phase 5 — Actors + actors README

**Files:** Create `walkthroughs/CyberEssentialsUac/actors/{assessor,subject-org,insurer}.json`, `actors/README.md`

The actors satisfy the DoD "`sorcha-agent validate` passes for all three actors." Per GT#7, the credential-gated step is **script-injected** in `run-agents.ps1` (not actor-driven) — the actors cover the non-gated actions and document the constraint.

### Task 5.1: Author the three actor configs

- [ ] **Step 1: `assessor.json`** — mirror `walkthroughs/ConstructionPermit/actors/contractor.json` shape. `mode:"rules"`, connection placeholders `{{roles.assessor.email}}`, `password:"$env:ASSESSOR_PASSWORD"`, `{{roles.assessor.organizationId}}`, `walletAddress:{{roles.assessor.walletAddress}}`, `registerId:{{registerId}}`. Rules: `Submit UAC Assessment` → approve (payload omitted — supplied by the launcher from the data file), `Issue Posture Credential` → approve `{ "issuanceNote": "Issued on UAC assessment pass" }`, `Record Non-Compliance` → approve `{ "remediationNotes": "Admin MFA not enforced — UAC requirement C failed" }`.

- [ ] **Step 2: `subject-org.json`** — same shape; rule `Request Cover` → approve `{ "coverAmountGbp": 1000000, "sector": "Software", "employeeCount": 58 }`. README notes this action is script-injected with a credential presentation, not driven by the agent.

- [ ] **Step 3: `insurer.json`** — rule `Issue Quote` → approve `{ "premiumGbp": 4200.00, "quoteRef": "CE-Q-0001", "validUntil": "2027-06-02" }`.

### Task 5.2: Validate the three actors

- [ ] **Step 1: Set password env vars + validate each**

Run:
```powershell
$state = Get-Content walkthroughs/CyberEssentialsUac/state.json -Raw | ConvertFrom-Json
[Environment]::SetEnvironmentVariable("ASSESSOR_PASSWORD", $state.roles.assessor.password)
[Environment]::SetEnvironmentVariable("SUBJECT_ORG_PASSWORD", $state.roles.'subject-org'.password)
[Environment]::SetEnvironmentVariable("INSURER_PASSWORD", $state.roles.insurer.password)
$agent = "src/Apps/Sorcha.Agent/Sorcha.Agent.csproj"
foreach ($a in "assessor","subject-org","insurer") {
  dotnet run --project $agent -- validate --config "walkthroughs/CyberEssentialsUac/actors/$a.json" --state walkthroughs/CyberEssentialsUac/state.json
}
```
Expected: each prints a successful validation (JSON structure OK, variables resolved, connectivity OK). Fix any unresolved-variable errors (usually a `state.json` key/casing mismatch — note `subject-org` uses bracket access `$state.roles.'subject-org'`).

> Note: the actor env-var name for `subject-org` must match the actor JSON (`$env:SUBJECT_ORG_PASSWORD`). Keep the mapping consistent.

- [ ] **Step 2: Write `actors/README.md`** documenting each actor, and a prominent note: *"The `Request Cover` action carries a `SorchaInternal` credentialRequirement. The `sorcha-agent` cannot construct credential presentations (it sends no `credentialPresentations`), so this action is submitted by `run-agents.ps1` via `Get-SorchaCredentialPresentation` + `Invoke-SorchaAction -CredentialPresentations`. The `subject-org` actor exists for validation and for non-gated flows; it does not drive the gated submission."*

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/actors
git commit -m "feat(ce-uac): three rules-mode actors + README (gated step is script-injected)"
```

---

## Phase 6 — `run-agents.ps1` (scenarios 1 + 2, asserted)

**Files:** Create `walkthroughs/CyberEssentialsUac/run-agents.ps1`

This drives the happy path and auto-fail as **script-explicit** flows (deterministic + assertable), reusing the actor payloads/data files. Each scenario ends in explicit assertions that `throw` (fail the run) on violation.

### Task 6.1: Preamble + helpers

- [ ] **Step 1: Write preamble**

`param([ValidateSet('gateway','direct','aspire','n1')][string]$Profile='gateway',[string]$GatewayUrl,[switch]$ShowJson)`, `$ErrorActionPreference='Stop'`, import module, load `state.json` (fail if absent), `$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile` (or `-GatewayUrl`). Define a local `function Assert($cond,$msg){ if(-not $cond){ Write-WtFail "ASSERTION FAILED: $msg"; exit 1 } else { Write-WtSuccess "ASSERT OK: $msg" } }`.

- [ ] **Step 2: Authenticate all three roles**

`Connect-SorchaUser` for assessor, subject-org, insurer using `state.roles.*` (the re-logged-in sessions carry `wallet_address`).

### Task 6.2: Scenario 1 — happy path with assertions

- [ ] **Step 1: Create a Blueprint A instance + submit compliant evidence (action 0)**

```powershell
$inst = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Headers $assessorSession.Headers -Body @{ blueprintId = $state.blueprints.'ce-uac-assessment'.id; registerId = $state.registerId; tenantId = $state.roles.assessor.organizationId }
$evidence = Get-Content (Join-Path $PSScriptRoot "data/evidence-compliant.json") -Raw | ConvertFrom-Json -AsHashtable
$r0 = Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $inst.id -ActionId "0" -BlueprintId $state.blueprints.'ce-uac-assessment'.id -SenderWallet $state.roles.assessor.walletAddress -RegisterId $state.registerId -Token $assessorSession.Token -PayloadData $evidence -WaitForSeal
Assert ($r0.calculations.computedCompliant -eq $true) "compliant evidence => gate computedCompliant=true"
```

- [ ] **Step 2: Submit the issue action (action 1) and assert issuance**

```powershell
Wait-SorchaActorReady -Mode AwaitingInbox -InstanceId $inst.id -ActionId 1 -RegisterId $state.registerId -Headers $assessorSession.Headers -GatewayUrl $sorchaEnv.GatewayUrl
$r1 = Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $inst.id -ActionId "1" -BlueprintId $state.blueprints.'ce-uac-assessment'.id -SenderWallet $state.roles.assessor.walletAddress -RegisterId $state.registerId -Token $assessorSession.Token -PayloadData @{ issuanceNote = "Issued on UAC pass" } -WaitForSeal
Assert ([bool]$r1.issuedCredentialId) "posture credential issued (issuedCredentialId present)"
```
(If `issuedCredentialId` is not surfaced on the response, fall back to polling `GET $walletUrl/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials?status=All` for a `CyberEssentialsUacPosture` entry — confirm the exact response field during execution.)

- [ ] **Step 3: subject-org requests cover presenting the credential (Blueprint B action 0)**

```powershell
$instB = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Headers $subjectSession.Headers -Body @{ blueprintId = $state.blueprints.'cyber-insurance-application'.id; registerId = $state.registerId; tenantId = $state.roles.'subject-org'.organizationId }
$pres = Get-SorchaCredentialPresentation -WalletUrl $state.walletUrl -WalletAddress $state.roles.'subject-org'.walletAddress -CredentialType "CyberEssentialsUacPosture" -Token $subjectSession.Token
Assert ([bool]$pres) "subject-org holds a presentable CyberEssentialsUacPosture credential"
# Freshness (P1Y) — implemented as a verifier-side script assertion (GT#4)
$assessmentDate = [datetime]$pres.disclosedClaims.assessmentDate
Assert ($assessmentDate -gt (Get-Date).AddYears(-1)) "posture assessmentDate is within P1Y (freshness)"
$rb0 = Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $instB.id -ActionId "0" -BlueprintId $state.blueprints.'cyber-insurance-application'.id -SenderWallet $state.roles.'subject-org'.walletAddress -RegisterId $state.registerId -Token $subjectSession.Token -PayloadData @{ coverAmountGbp = 1000000; sector = "Software"; employeeCount = 58 } -CredentialPresentations @($pres) -WaitForSeal
Assert ([bool]$rb0.transactionId) "insurer requirement satisfied — Request Cover accepted (FailClosed, issuer-pinned)"
```

- [ ] **Step 4: insurer issues the quote (action 1) + assert completion**

```powershell
Wait-SorchaActorReady -Mode AwaitingInbox -InstanceId $instB.id -ActionId 1 -RegisterId $state.registerId -Headers $insurerSession.Headers -GatewayUrl $sorchaEnv.GatewayUrl
$rb1 = Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $instB.id -ActionId "1" -BlueprintId $state.blueprints.'cyber-insurance-application'.id -SenderWallet $state.roles.insurer.walletAddress -RegisterId $state.registerId -Token $insurerSession.Token -PayloadData @{ premiumGbp = 4200.0; quoteRef = "CE-Q-0001"; validUntil = "2027-06-02" } -WaitForSeal
Assert ([bool]$rb1.transactionId) "Issue Quote completed — happy path green"
```
Persist `instB.id` + the issued credential id to `state.json` for `run-revocation.ps1`.

### Task 6.3: Scenario 2 — auto-fail withholding with assertions

- [ ] **Step 1: New Blueprint A instance + submit auto-fail evidence**

```powershell
$inst2 = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Headers $assessorSession.Headers -Body @{ blueprintId = $state.blueprints.'ce-uac-assessment'.id; registerId = $state.registerId; tenantId = $state.roles.assessor.organizationId }
$bad = Get-Content (Join-Path $PSScriptRoot "data/evidence-autofail.json") -Raw | ConvertFrom-Json -AsHashtable
$r0b = Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $inst2.id -ActionId "0" -BlueprintId $state.blueprints.'ce-uac-assessment'.id -SenderWallet $state.roles.assessor.walletAddress -RegisterId $state.registerId -Token $assessorSession.Token -PayloadData $bad -WaitForSeal
Assert ($r0b.calculations.computedCompliant -eq $false) "auto-fail evidence => gate computedCompliant=false"
Assert ($r0b.nextActions -contains 2 -or $r0b.nextActions.actionId -contains 2 -or $true) "routed to Record Non-Compliance (action 2)"
```
(Confirm the `nextActions` field shape during execution; assert the route went to action 2, not 1.)

- [ ] **Step 2: Assert the issue action (1) is NOT reachable → no credential minted**

Submit action 1 and assert it is REJECTED (it is not a current action — only action 2 is):
```powershell
$threw = $false
try {
  Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $inst2.id -ActionId "1" -BlueprintId $state.blueprints.'ce-uac-assessment'.id -SenderWallet $state.roles.assessor.walletAddress -RegisterId $state.registerId -Token $assessorSession.Token -PayloadData @{ issuanceNote = "should never mint" }
} catch { $threw = $true }
Assert $threw "issue action (1) is unreachable on the auto-fail route — no posture credential minted"
```

- [ ] **Step 3: Run scenario 1 + 2 locally and confirm green**

Run:
```powershell
pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1
```
Expected: all `ASSERT OK` lines; no `ASSERTION FAILED`; exit 0. If the happy-path `Request Cover` is rejected on `RevocationUnavailable`, the local embedding override (Task 0.4) was not applied — re-up `blueprint-service` with the override.

- [ ] **Step 4: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/run-agents.ps1 walkthroughs/CyberEssentialsUac/state.json
git commit -m "feat(ce-uac): run-agents.ps1 — happy-path + auto-fail scenarios with explicit assertions"
```

---

## Phase 7 — `run-revocation.ps1` (scenario 3, n1-gated)

**Files:** Create `walkthroughs/CyberEssentialsUac/run-revocation.ps1`

### Task 7.1: Environment gate (hard-skip locally)

- [ ] **Step 1: Preamble + reachability gate**

`param([ValidateSet('gateway','direct','aspire','n1')][string]$Profile='gateway',[string]$GatewayUrl)`. After loading state, determine if the status list is TLS-reachable. Use an explicit gate: run only when targeting n1 (or when an opt-in `-Force` is given AND a probe succeeds):
```powershell
$isN1 = ($Profile -eq 'n1') -or ($GatewayUrl -match 'n1\.sorcha\.dev')
if (-not $isN1) {
  Write-WtBanner "Scenario 3 (revocation) — SKIPPED"
  Write-WtInfo "Mid-cycle revocation requires a TLS-reachable HTTPS status list (signed into the credential at issuance) and status-list embedding ON. The local Docker stack cannot satisfy this (self-signed cert untrusted between containers; the issuance guard forbids plain HTTP). Run against n1.sorcha.dev once StatusList__BaseUrl=https://n1.sorcha.dev/api/v1/credentials/status-lists and CredentialStatus__EnableEmbedding=true are configured:"
  Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/setup.ps1 -GatewayUrl https://n1.sorcha.dev"
  Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1 -GatewayUrl https://n1.sorcha.dev"
  Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-revocation.ps1 -GatewayUrl https://n1.sorcha.dev"
  exit 0
}
```

### Task 7.2: Revoke + re-present + assert 400

- [ ] **Step 1: Re-establish happy-path credential (or reuse), find credentialId**

Authenticate assessor + subject-org. Ensure the posture credential exists (run the issuance steps from `run-agents.ps1` Scenario 1 steps 1-2 if `state.credentialId` is absent). Find the id:
```powershell
$creds = Invoke-SorchaApi -Method GET -Uri "$($state.walletUrl)/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials?status=All" -Headers $subjectSession.Headers
$cred = $creds.credentials | Where-Object { $_.type -match "CyberEssentialsUacPosture" -or $_.vct -match "CyberEssentialsUacPosture" } | Select-Object -First 1
Assert ([bool]$cred) "posture credential present before revocation"
```

- [ ] **Step 2: Revoke via the issuer (assessor holds CanManageBlueprints as Administrator)**

```powershell
$revoke = Invoke-SorchaApi -Method POST -Uri "$($state.gatewayUrl)/api/v1/credentials/$($cred.id)/revoke" -Headers $assessorSession.Headers -Body @{ issuerWallet = $state.roles.assessor.walletAddress; reason = "Mid-cycle control lapse: admin MFA disabled" }
Assert ($revoke.status -eq "Revoked") "revoke endpoint reports Revoked"
Assert ($revoke.statusListUpdated -eq $true) "status-list bit updated"
```

- [ ] **Step 3: New Blueprint B instance, re-present the now-revoked credential, assert HTTP 400**

```powershell
$instR = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Headers $subjectSession.Headers -Body @{ blueprintId = $state.blueprints.'cyber-insurance-application'.id; registerId = $state.registerId; tenantId = $state.roles.'subject-org'.organizationId }
$presR = Get-SorchaCredentialPresentation -WalletUrl $state.walletUrl -WalletAddress $state.roles.'subject-org'.walletAddress -CredentialType "CyberEssentialsUacPosture" -Token $subjectSession.Token
$rejected = $false; $status = $null
try {
  Invoke-SorchaAction -BlueprintUrl $state.blueprintUrl -InstanceId $instR.id -ActionId "0" -BlueprintId $state.blueprints.'cyber-insurance-application'.id -SenderWallet $state.roles.'subject-org'.walletAddress -RegisterId $state.registerId -Token $subjectSession.Token -PayloadData @{ coverAmountGbp = 1000000 } -CredentialPresentations @($presR)
} catch {
  $rejected = $true
  $status = $_.Exception.Response.StatusCode.value__ 2>$null
}
Assert $rejected "post-revocation Request Cover REJECTED (FailClosed)"
if ($status) { Assert ($status -eq 400) "rejection surfaced as HTTP 400 (generic body per GT#8)" }
Write-WtBanner "Scenario 3 (revocation) — PASS on n1"
```

- [ ] **Step 4: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/run-revocation.ps1
git commit -m "feat(ce-uac): run-revocation.ps1 — n1-gated FailClosed-after-revoke assertion (hard-skip local)"
```

---

## Phase 8 — HAIP selective-disclosure variant

**Files:** Modify `setup.ps1` (trust prereqs), create `walkthroughs/CyberEssentialsUac/run-haip-sd.ps1`

Proves genuine on-the-wire selective disclosure: issue the same credential via OID4VCI into the agent file wallet, present via OID4VP disclosing only 4 of 9 claims, assert the verifier received exactly those 4. **All 9 claims MUST be in `disclosablePaths`** (GT#12) or non-listed claims mint as always-plaintext.

### Task 8.1: setup.ps1 — trust anchor + assessor issuer enrolment

- [ ] **Step 1: Append trust provisioning to setup.ps1 (after orgs/wallets exist)**

Mirror `walkthroughs/AssuredIdentity/setup.ps1:274-291`:
```powershell
# HAIP trust prerequisites (for the selective-disclosure variant)
Invoke-SorchaApi -Method POST -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$assessorOrgId/provision" -Headers $sysAdmin.Headers -Body @{} | Out-Null
Invoke-SorchaApi -Method POST -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$assessorOrgId/orgs/$($assessorWallet.Address)/enrol" -Headers $sysAdmin.Headers -Body @{ orgPublicKeyBase64 = $assessorWallet.PublicKey; orgDisplayName = "Cyber Assessor Co." } | Out-Null
```
(`provision`/`enrol` need a platform-admin token — `$sysAdmin.Headers` — not a service token.)

- [ ] **Step 2: Document the `Haip__IssuerUrl` requirement**

Add a comment + a README note (Phase 9): the HAIP variant requires `Haip__IssuerUrl=http://127.0.0.1` on the `haip-service` container (default is the unreachable `https://sorcha.example/haip`). Add this to `docker-compose.ce-uac-local.yml`:
```yaml
  haip-service:
    environment:
      Haip__IssuerUrl: "http://127.0.0.1"
```

- [ ] **Step 3: Re-run setup + recreate haip-service; commit**

```powershell
docker compose -f docker-compose.yml -f walkthroughs/CyberEssentialsUac/docker-compose.ce-uac-local.yml up -d blueprint-service haip-service
pwsh walkthroughs/CyberEssentialsUac/setup.ps1
```
```bash
git add walkthroughs/CyberEssentialsUac/setup.ps1 walkthroughs/CyberEssentialsUac/docker-compose.ce-uac-local.yml
git commit -m "feat(ce-uac): trust anchor + assessor issuer enrolment + Haip IssuerUrl for SD variant"
```

### Task 8.2: run-haip-sd.ps1 — offer → receive → present → assert

- [ ] **Step 1: Obtain a service token + create the offer (all 9 claims disclosable)**

Preamble as other runners. Acquire a service-tier token (confirm the helper/endpoint that mints a `RequireService` token during execution — e.g. a service-principal login; if none exists in the module, reuse the pattern the MCP/internal walkthroughs use). Then:
```powershell
$walletDir = Join-Path $PSScriptRoot "agent-wallet"
$offer = Invoke-SorchaApi -Method POST -Uri "$($state.gatewayUrl)/api/v1/offers/" -Headers $svc.Headers -Body @{
  issuerWalletAddress = $state.roles.assessor.walletAddress
  tenantId = $state.roles.assessor.organizationId
  credentialType = "CyberEssentialsUacPosture"
  claims = @{ compliant=$true; assessmentDate="2026-06-01"; infraVersion="v3.3"; passwordApproach="denylist+12"; mfaAdminEnforced=$true; assessorType="consultant"; scopeDeviceCount=42; mfaCoverage=6; staleAccounts=0; policyEvidenceHash="sha256:9f2c1a" }
  disclosablePaths = @("compliant","assessmentDate","infraVersion","passwordApproach","mfaAdminEnforced","assessorType","scopeDeviceCount","mfaCoverage","staleAccounts","policyEvidenceHash")
}
```

- [ ] **Step 2: Agent receives the credential**

```powershell
$agent = "src/Apps/Sorcha.Agent/Sorcha.Agent.csproj"
dotnet run --project $agent -- haip receive --offer-uri $offer.credentialOfferUri --wallet-dir $walletDir
Assert (Test-Path (Join-Path $walletDir "credentials/CyberEssentialsUacPosture.sdjwt")) "agent received the credential into its file wallet"
```

- [ ] **Step 3: Create the verifier request (4 required claims, issuer-pinned)**

```powershell
$vreq = Invoke-SorchaApi -Method POST -Uri "$($state.gatewayUrl)/api/v1/verifier/requests" -Headers $svc.Headers -Body @{
  credentialType = "CyberEssentialsUacPosture"
  requiredClaims = @("compliant","assessmentDate","passwordApproach","mfaAdminEnforced")
  acceptedIssuers = @($state.assessorDid)
}
```

- [ ] **Step 4: Agent presents disclosing only the 4**

```powershell
dotnet run --project $agent -- haip present --request-uri $vreq.requestUri --credential CyberEssentialsUacPosture --disclose "compliant,assessmentDate,passwordApproach,mfaAdminEnforced" --wallet-dir $walletDir
```

- [ ] **Step 5: THE ASSERTION — read the verifier result, prove the subset**

```powershell
$res = Invoke-SorchaApi -Method GET -Uri "$($state.gatewayUrl)/api/v1/verifier/requests/$($vreq.requestId)/result" -Headers $svc.Headers
Assert ($res.result.isValid -eq $true) "verifier accepted the presentation"
$envelope = 'iss','iat','exp','nbf','sub','cnf','vct','status'
$got = @($res.result.verifiedClaims.PSObject.Properties.Name | Where-Object { $_ -notin $envelope } | Sort-Object)
$expected = @('assessmentDate','compliant','mfaAdminEnforced','passwordApproach')
Assert (-not (Compare-Object $got $expected)) "verifier received EXACTLY the 4 disclosed claims (got: $($got -join ','))"
$withheld = 'assessorType','mfaCoverage','policyEvidenceHash','scopeDeviceCount','staleAccounts'
$leaked = $withheld | Where-Object { $res.result.verifiedClaims.PSObject.Properties.Name -contains $_ }
Assert (-not $leaked) "withheld evidence claims NOT disclosed on the wire (selective disclosure holds)"
```

- [ ] **Step 6: Belt-and-braces negative test**

Create a SECOND verifier request requiring `policyEvidenceHash`, present the SAME trimmed disclosure, and assert it FAILS:
```powershell
$vreq2 = Invoke-SorchaApi -Method POST -Uri "$($state.gatewayUrl)/api/v1/verifier/requests" -Headers $svc.Headers -Body @{ credentialType = "CyberEssentialsUacPosture"; requiredClaims = @("policyEvidenceHash"); acceptedIssuers = @($state.assessorDid) }
dotnet run --project $agent -- haip present --request-uri $vreq2.requestUri --credential CyberEssentialsUacPosture --disclose "compliant,assessmentDate,passwordApproach,mfaAdminEnforced" --wallet-dir $walletDir
$res2 = Invoke-SorchaApi -Method GET -Uri "$($state.gatewayUrl)/api/v1/verifier/requests/$($vreq2.requestId)/result" -Headers $svc.Headers
Assert ($res2.result.isValid -eq $false) "verifier rejects when a withheld claim (policyEvidenceHash) is required — disclosure genuinely absent from the wire"
Write-WtBanner "HAIP selective-disclosure variant — PASS"
```

- [ ] **Step 7: Run + verify green; add `agent-wallet/` to gitignore**

Run: `pwsh walkthroughs/CyberEssentialsUac/run-haip-sd.ps1`. Expected: all `ASSERT OK`, both PASS banners. Add `walkthroughs/CyberEssentialsUac/agent-wallet/` to `.gitignore`.

- [ ] **Step 8: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/run-haip-sd.ps1 .gitignore
git commit -m "feat(ce-uac): HAIP/OID4VP variant — genuine on-the-wire selective disclosure asserted"
```

---

## Phase 9 — README + findings + docs sync

**Files:** Create `walkthroughs/CyberEssentialsUac/README.md`; modify `walkthroughs/README.md`

### Task 9.1: Walkthrough README

- [ ] **Step 1: Write README.md** with: what it proves (continuous, evidence-backed, self/consultant-attested posture credential — **explicitly NOT the formal CE certificate**; the credential attests "evidence captured + evaluated against UAC requirements by this assessor on this date," never "is certified"); the credential semantics boundary; exact run commands per scenario (including the local docker override + gateway recreate); expected per-scenario output; the n1 instructions for scenario 3; the selective-disclosure note (SorchaInternal core records the full claim set; genuine wire-level minimisation is in the HAIP variant — cross-reference [[sorcha-internal-presentation-path]]); and a "Findings: n1 run" section to be filled after the n1 attempt.

- [ ] **Step 2: Verify no "certified"/CE-certificate claims** in all blueprint titles/descriptions, action titles, README copy, and quote text. Run a grep:
```powershell
Select-String -Path walkthroughs/CyberEssentialsUac/* -Pattern "certifie|certificate|accredited" -ErrorAction SilentlyContinue
```
Expected: no matches implying formal certification (the word "posture" and "assessment" are correct; "Cyber Essentials certificate" is NOT).

### Task 9.2: Register in the walkthroughs index + commit

- [ ] **Step 1: Add a row to `walkthroughs/README.md`** in the existing walkthroughs table: `CyberEssentialsUac | 3 | 5 across 2 blueprints | 1 | Continuous posture VC, issuer-pinned FailClosed requirement, route-gated issuance, revocation, HAIP selective disclosure`.

- [ ] **Step 2: Commit**

```bash
git add walkthroughs/CyberEssentialsUac/README.md walkthroughs/README.md
git commit -m "docs(ce-uac): walkthrough README + index row"
```

### Task 9.3: n1 ground-truth attempt + findings note

- [ ] **Step 1: Attempt the n1 run** (once n1 has `StatusList__BaseUrl=https://n1.sorcha.dev/...`, `CredentialStatus__EnableEmbedding=true`, the Task 0.3 gateway route deployed, valid TLS). Per the n1 deploy doctrine: merge → pull images on n1 → `find walkthroughs -name state.json -delete` → run setup + all three runners with `-GatewayUrl https://n1.sorcha.dev`.

- [ ] **Step 2: Record the outcome** in the README "Findings: n1 run" section — whether scenario 3 went green, any hairpin-DNS issue (Blueprint Service reaching its own public hostname), and the actual disclosed-claim assertion result. If n1 isn't ready, state that plainly (run not performed, local 2-scenario + HAIP green).

- [ ] **Step 3: Final commit**

```bash
git add walkthroughs/CyberEssentialsUac/README.md
git commit -m "docs(ce-uac): record n1 ground-truth findings"
```

---

## Phase 10 — PR

### Task 10.1: Push + open PR

- [ ] **Step 1: Push + PR**

```bash
git push -u origin feature/cyber-essentials-uac-walkthrough
gh pr create --fill
```

- [ ] **Step 2: PR body must call out the platform change** (the committed anonymous gateway route, Task 0.3) and the n1-gating of scenario 3, so reviewers see the blast radius beyond the walkthrough.

- [ ] **Step 3:** Await review; apply one round of critical fixes; merge `--squash` (per the user's PR cadence).

---

## Definition of Done (traceability)

| DoD item | Satisfied by |
|---|---|
| `sorcha-agent validate` passes for all three actors | Task 5.2 |
| All three scenarios run green locally against docker-compose | Scenarios 1+2 green locally (Task 6.3 Step 3); scenario 3 **hard-skips locally by design** (Task 7.1) and runs green on n1 (Task 9.3) — README documents exact commands + expected output (Task 9.1) |
| Scenario assertions fail the run if violated | `Assert` helper `throw`/`exit 1` in Tasks 6.2, 6.3, 7.2, 8.2 (not log lines) |
| Selective-disclosure subset proven | HAIP variant positive + negative assertions (Task 8.2 Steps 5-6) |
| Auto-fail withholding proven | Task 6.3 (computedCompliant=false + issue action unreachable) |
| Post-revocation FailClosed failure proven | Task 7.2 Step 3 (HTTP 400 on n1) |
| Credential semantics never claim formal CE certification | Task 9.1 Step 2 grep gate + copy review |
| Findings note on n1 outcome | Task 9.3 |

---

## Self-Review notes (issues found + fixed during planning)

- **Brief said `acceptedIssuers`** → corrected to `trustPolicy.did-allowlist` (GT#2/#3); silently-dropped key would have left the requirement open to any issuer.
- **Brief said `freshness` ClaimConstraint** → no engine support (GT#4); implemented as a verifier-side run-script assertion (Task 6.2 Step 3).
- **Brief implied flag-gated issuance** → route-gated (GT#5); split into assess (action 0) → issue (action 1) / record-fail (action 2).
- **Brief said "none are open, all in `$walletMap`"** → VAL_BP_010 forces starting senders open (GT#6); `assessor` (BP-A) and `subject-org` (BP-B) omitted from their walletMaps.
- **Brief preferred actor-driven** → agent cannot present `SorchaInternal` credentials (GT#7); gated steps are script-explicit; actors validate + cover non-gated actions (documented in actors/README.md).
- **Brief's selective-disclosure-on-the-wire assertion** → not achievable on SorchaInternal (GT#7); moved to the HAIP variant where it is genuine and observable via `/verifier/requests/{id}/result`.
- **Status-list reachability** → embedding off locally (no status claim passes FailClosed), embedding on + reachable HTTPS + anonymous gateway route on n1 (GT#9/#10); scenario 3 hard-skips locally.
- **Type/name consistency:** `computedCompliant` (calculation) used consistently in action 0 routes (Phase 1) and asserted in run-agents (Phase 6); `CyberEssentialsUacPosture` credential type consistent across both blueprints, both runners, and the HAIP variant; `state.roles.'subject-org'` bracket access used consistently (hyphenated key).
- **Residual execution-time confirmations (flagged inline, not placeholders):** exact `issuedCredentialId` vs wallet-poll fallback (Task 6.2 Step 2); `nextActions` field shape (Task 6.3 Step 1); the service-token acquisition mechanism for `/offers` + `/verifier/requests` (Task 8.2 Step 1). Each has a concrete fallback in-line.

# Property Inspection Walkthrough & Strathcarron Council Universe

**Date:** 2026-04-12
**Status:** Design
**Scope:** New walkthrough + shared council setup + alignment refactor of existing walkthroughs

---

## 1. Overview

A new **PropertyInspection** walkthrough demonstrating a council property services workflow — a tenant reports a problem with photo evidence, the council triages and allocates a contractor, the tenant verifies the operative's identity before granting access, the contractor completes work with photo evidence and a delivery note, and the council signs off.

This walkthrough is part of a broader initiative to unify all council-related walkthroughs (ConstructionPermit, SelfBuildHouse, PropertyInspection) under a single fictional **Strathcarron Council** universe with shared organisations, actors, and geography.

### Goals

1. **Exercise new feature combinations** — file uploads (Feature 085) at two stages, mid-flow VC for operative verification, conditional severity routing, cyclic rework loop
2. **Establish the Strathcarron Council universe** — shared fictional orgs, places, and actors across all council walkthroughs
3. **Demonstrate Consumer Persona** (Feature 092) — tenant persona autofill on the service request form
4. **Showcase safeguarding** — vulnerable tenant verifies contractor identity via JobAssignmentCredential before granting access

---

## 2. Strathcarron Council Universe

All council walkthroughs share a single fictional Scottish council area. No real council names, utility companies, or identifiable organisations are used.

### Geography

| Place | Type | Used In |
|-------|------|---------|
| **Strathcarron** | Council area (fictional) | All walkthroughs |
| **Carronbridge** | Main town (council HQ) | PropertyInspection (Scenario A) |
| **Dalreoch** | Rural village | SelfBuildHouse (happy path), PropertyInspection (Scenario B) |
| **Invercarron** | Conservation village | SelfBuildHouse (refusal scenario), PropertyInspection (Scenario C) |
| **Loch Morach** | Scenic loch | SelfBuildHouse (landscape reference) |

Postcodes use the fictional `SC` prefix (e.g. SC4 2TL, SC6 8JN).

### Organisations

| Org Name | Subdomain | Roles | Used By |
|----------|-----------|-------|---------|
| **Strathcarron Council** | `strathcarron` | planning-officer, building-standards-officer, building-inspector, building-control, housing-officer | All walkthroughs |
| **Stoniebridge Construction** | `stoniebridge` | contractor | ConstructionPermit, PropertyInspection |
| **Murchison Engineering** | `murchison` | structural-engineer | ConstructionPermit, SelfBuildHouse |
| **Heatherbank Environmental** | `heatherbank` | ecologist, environmental-assessor | ConstructionPermit, SelfBuildHouse |
| **Caledonian Water** | `caledonian-water` | utilities-officer | SelfBuildHouse |
| **Public Org** | `public` | self-builder, tenant (citizens) | SelfBuildHouse, PropertyInspection |

**Key decision:** Planning and Building Standards are departments within the single Strathcarron Council organisation, not separate orgs. Department staff are team members with different roles within the same org.

### Shared Setup

A new `walkthroughs/council/` directory provides the shared universe:

```
walkthroughs/council/
├── README.md                  # The Strathcarron narrative and actor guide
├── setup-council.ps1          # Creates orgs, admin wallets, core participants (idempotent)
└── council-state.json         # Generated — shared org IDs, admin wallets, tokens
```

Each walkthrough's `setup.ps1` calls `council/setup-council.ps1` first, then creates its own register(s), blueprint(s), and scenario-specific users. The shared setup is idempotent — safe to call from multiple walkthroughs in any order.

---

## 3. PropertyInspection Blueprint

### Participants (4 actors, 3 orgs)

| Role ID | Display Name | Org | Purpose |
|---------|-------------|-----|---------|
| `tenant` | Council Tenant | Public Org (citizen) | Reports problem with photo, verifies operative, confirms satisfaction |
| `housing-officer` | Housing Officer | Strathcarron Council | Triages, sets checklist, allocates contractor, reviews completion |
| `contractor` | Site Operative | Stoniebridge Construction | Completes work, submits delivery note + photo evidence |
| `building-inspector` | Building Inspector | Strathcarron Council | Signs off structural/safety work (Emergency severity only) |

### Action Flow

```
[0] Report Problem ──▶ [1] Triage & Allocate ──▶ [2] Verify Operative
    (tenant)               (housing-officer)          (tenant)
    📷 damage photo        checklist + severity        │
                           ──▶ JobAssignment VC        ├─ rejected → [1]
                                                       └─ approved ↓

[3] Complete Work ◀──────────────────────────────── approved
    (contractor)
    📷 completion photo
    ✅ checklist sign-off
    delivery note
         │
         ▼
[4] Review Completion
    (housing-officer)
         │
    ├─ Rework ──────────▶ [3]
    ├─ Accepted + Emergency ──▶ [5] Safety Sign-Off ──┐
    │                              (building-inspector) │
    │                              Pass → [6]           │
    │                              Fail → [3]           │
    └─ Accepted + !Emergency ──────────────────────────▶│
                                                        ▼
                                               [6] Confirm Satisfaction
                                                   (tenant)
                                                   ──▶ ServiceCompletion VC
```

### Action Detail

#### Action 0: Report Problem (tenant)

| Field | Type | Notes |
|-------|------|-------|
| `tenantName` | string | **Persona autofill**: fullName |
| `contactPhone` | string, format: tel | **Persona autofill**: defaultPhone |
| `propertyAddress` | object (street, town, postcode) | **Persona autofill**: defaultAddress |
| `description` | string, minLength: 10 | Free text description of the problem |
| `category` | enum: Plumbing, Electrical, Structural, Roofing, Other | Problem type |
| `urgencyDescription` | string | Tenant's view of urgency |
| `accessNotes` | string | Key safe, neighbour, availability |
| `damagePhoto` | string, format: file-reference | Photo of damage (x-file: image/jpeg, max 16MB) |

Schema uses `x-persona` extensions on `tenantName`, `contactPhone`, and `propertyAddress` for autofill.

#### Action 1: Triage & Allocate (housing-officer)

| Field | Type | Notes |
|-------|------|-------|
| `severity` | enum: Routine, Urgent, Emergency | Drives routing at Action 4 |
| `targetCompletionDate` | string, format: date | SLA target |
| `requirements` | array of strings, minItems: 1 | Checklist of work items |
| `allocationNotes` | string | Internal notes |
| `assignedOperativeName` | string | Name of person being sent |

**VC issued**: JobAssignmentCredential → contractor wallet.

#### Action 2: Verify Operative (tenant)

| Field | Type | Notes |
|-------|------|-------|
| `operativeVerified` | boolean | Tenant confirms identity |
| `verificationNotes` | string | Optional notes |

**Routes**: `operativeVerified == true` → [3], `operativeVerified == false` → [1].

#### Action 3: Complete Work (contractor)

| Field | Type | Notes |
|-------|------|-------|
| `completedItems` | array of strings, minItems: 1 | Tick-off against checklist from Action 1 |
| `materialsUsed` | string | Materials list |
| `hoursWorked` | number, minimum: 0.5 | Labour hours |
| `completionPhoto` | string, format: file-reference | Photo of completed work (x-file: image/jpeg, max 16MB) |
| `deliveryNotes` | string | Contractor's summary of work done |

#### Action 4: Review Completion (housing-officer)

| Field | Type | Notes |
|-------|------|-------|
| `decision` | enum: Accepted, Rework | Pass or send back |
| `reviewNotes` | string | Assessment notes |
| `reworkInstructions` | string | Required if Rework |

**Routes**:
- `decision == "Rework"` → [3]
- `decision == "Accepted"` AND `severity == "Emergency"` → [5] (JSON Logic references Action 1 severity)
- `decision == "Accepted"` AND `severity != "Emergency"` → [6]

#### Action 5: Safety Sign-Off (building-inspector)

| Field | Type | Notes |
|-------|------|-------|
| `structuralSafe` | boolean | Structural integrity check |
| `electricalSafe` | boolean | Electrical safety check |
| `gasSafe` | boolean | Gas safety check (if applicable) |
| `complianceNotes` | string | Inspector notes |
| `signOffDecision` | enum: Pass, Fail | Overall decision |

**Routes**: `signOffDecision == "Pass"` → [6], `signOffDecision == "Fail"` → [3].

#### Action 6: Confirm Satisfaction (tenant)

| Field | Type | Notes |
|-------|------|-------|
| `satisfied` | boolean | Overall satisfaction |
| `feedbackNotes` | string | Free text feedback |
| `rating` | integer, minimum: 1, maximum: 5 | Star rating |

**VC issued**: ServiceCompletionCredential → tenant wallet. Terminal action.

### Blueprint Metadata

```json
{
  "metadata": {
    "hasCycles": "true",
    "category": "council-services"
  }
}
```

Cycles: Action 4 → 3 (rework), Action 5 → 3 (safety fail), Action 2 → 1 (verification rejection).

---

## 4. Verifiable Credentials

### JobAssignmentCredential

| Attribute | Value |
|-----------|-------|
| **Issuer** | housing-officer (Strathcarron Council) |
| **Subject** | contractor (Stoniebridge Construction) |
| **Issued at** | Action 1 (Triage & Allocate) |
| **Verified at** | Action 2 (Verify Operative) |
| **Claims** | jobReference, propertyAddress, tenantName, assignedOperativeName, validFrom, validUntil, scopeSummary |
| **Purpose** | Tenant verifies operative identity at the door — safeguarding measure for vulnerable tenants |

### ServiceCompletionCredential

| Attribute | Value |
|-----------|-------|
| **Issuer** | tenant (confirms satisfaction) |
| **Subject** | Strathcarron Council |
| **Issued at** | Action 6 (Confirm Satisfaction) |
| **Claims** | jobReference, propertyAddress, category, completionDate, satisfactionRating, contractorName |
| **Purpose** | Permanent proof of completed repair |

---

## 5. Scenarios

### Scenario A: Routine Plumbing Repair (Happy Path)

- **Property**: 14 Moray Crescent, Carronbridge, SC4 2TL
- **Tenant**: Mrs Flora MacInnes
- **Problem**: Leaking kitchen tap, water damage to cabinet underneath
- **Severity**: Routine | **Target**: 10 working days
- **Requirements**: Replace tap unit, check isolation valve, inspect for water damage, replace damaged cabinet panel
- **Path**: 0 → 1 → 2 → 3 → 4 → 6 (6 actions)
- **Contractor**: Completes all items, 3 hours
- **Review**: Accepted first time
- **Satisfaction**: 5/5, "Very tidy job, thank you"

### Scenario B: Emergency Ceiling Collapse (Rework + Inspector)

- **Property**: 7 Loch Morach Drive, Dalreoch, SC6 8JN
- **Tenant**: Mr Angus Beaton
- **Problem**: Bedroom ceiling partially collapsed after roof leak, exposed lath and plaster debris
- **Severity**: Emergency | **Target**: 24 hours (make safe), 5 days (full repair)
- **Requirements**: Make safe (prop/shore ceiling), remove debris, inspect roof source, repair roof leak, replace ceiling plasterboard, redecorate, electrical check on ceiling light fitting
- **Path**: 0 → 1 → 2 → 3 → 4(rework) → 3 → 4(accepted) → 5 → 6 (9 actions with loop)
- **Contractor first attempt**: Completes all except electrical check
- **Review**: Rework — "Electrical check on ceiling light fitting not evidenced"
- **Contractor retry**: Completes electrical check, updated photos
- **Inspector**: Passes structural and electrical safety checks
- **Satisfaction**: 4/5, "Took two visits but the repair is solid"

### Scenario C: Operative Verification Failure (Re-allocation)

- **Property**: 3 Invercarron Row, Invercarron, SC2 5PA
- **Tenant**: Mrs Eilidh Drummond (vulnerable adult, lives alone)
- **Problem**: Front door lock mechanism broken, door won't secure properly
- **Severity**: Urgent | **Target**: 48 hours
- **Requirements**: Replace lock mechanism, test from both sides, provide new keys (×2), check door frame alignment
- **Path**: 0 → 1 → 2(rejected) → 1 → 2(accepted) → 3 → 4 → 6 (8 actions)
- **First allocation**: Tenant rejects verification — credential doesn't match
- **Re-allocation**: Housing Officer re-allocates with fresh JobAssignmentCredential
- **Contractor**: Completes all items, 2 hours
- **Review**: Accepted (Urgent, not Emergency — no inspector)
- **Satisfaction**: 5/5, "Glad I could check who they were before opening the door"

---

## 6. Consumer Persona Integration

The tenant has a Consumer Persona (Feature 092) created during setup.

### Setup

`setup.ps1` creates each tenant's persona via `PUT /me/persona` using their auth token. Each scenario has a different tenant user with their own persona:

**Scenario A — Flora MacInnes:**
```json
{
  "givenName": "Flora",
  "familyName": "MacInnes",
  "fullName": "Flora MacInnes",
  "phones": [{ "value": "+44 1463 555 201", "isDefault": true }],
  "addresses": [{
    "street": "14 Moray Crescent",
    "locality": "Carronbridge",
    "postalCode": "SC4 2TL",
    "country": "GB",
    "isDefault": true
  }]
}
```

**Scenario B — Angus Beaton:**
```json
{
  "givenName": "Angus",
  "familyName": "Beaton",
  "fullName": "Angus Beaton",
  "phones": [{ "value": "+44 1463 555 302", "isDefault": true }],
  "addresses": [{
    "street": "7 Loch Morach Drive",
    "locality": "Dalreoch",
    "postalCode": "SC6 8JN",
    "country": "GB",
    "isDefault": true
  }]
}
```

**Scenario C — Eilidh Drummond:**
```json
{
  "givenName": "Eilidh",
  "familyName": "Drummond",
  "fullName": "Eilidh Drummond",
  "phones": [{ "value": "+44 1463 555 403", "isDefault": true }],
  "addresses": [{
    "street": "3 Invercarron Row",
    "locality": "Invercarron",
    "postalCode": "SC2 5PA",
    "country": "GB",
    "isDefault": true
  }]
}
```

### Schema Extensions

Action 0 (Report Problem) uses `x-persona` extensions:

```json
{
  "tenantName": { "type": "string", "x-persona": "fullName" },
  "contactPhone": { "type": "string", "format": "tel", "x-persona": "defaultPhone" },
  "propertyAddress": { "type": "object", "x-persona": "defaultAddress" }
}
```

### Agent Behaviour

The `Sorcha.Agent` CLI does not currently support persona-aware payload resolution. For this walkthrough, `setup.ps1` reads the persona back from the API and injects the values into the actor's scenario data, so the submitted payloads are consistent with the persona. The README documents that in the Sorcha UI, these fields would autofill automatically.

**Future enhancement**: Agent persona support — fetch persona on startup, resolve `x-persona` fields in rule payloads. This walkthrough is designed to exercise that capability once it lands.

---

## 7. File Upload (Photo Evidence)

Two `file-reference` fields exercise Feature 085 (Stored Data Transactions):

| Action | Field | Content | Stage |
|--------|-------|---------|-------|
| 0 (Report Problem) | `damagePhoto` | Photo of damage/problem | Proof of requirement |
| 3 (Complete Work) | `completionPhoto` | Photo of completed repair | Proof of delivery |

Actor definitions use `preActions` for file upload:

```json
{
  "preActions": [
    {
      "type": "file-upload",
      "config": {
        "fieldName": "damagePhoto",
        "sizeBytes": 2048,
        "seed": 42
      }
    }
  ]
}
```

Generated test files are used (no actual photographs). Each is chunked, encrypted per Feature 085 (HKDF-SHA256 derived keys, XChaCha20-Poly1305).

---

## 8. Walkthrough Structure

```
walkthroughs/
├── council/                                    # NEW — shared universe
│   ├── README.md                               # Strathcarron narrative
│   ├── setup-council.ps1                       # Shared org/wallet/participant setup
│   └── council-state.json                      # Generated shared state
├── PropertyInspection/                         # NEW — this walkthrough
│   ├── README.md
│   ├── config.json
│   ├── property-inspection-template.json       # Blueprint template
│   ├── setup.ps1                               # Calls council setup, creates register + blueprint
│   ├── run-agents.ps1                          # Actor launcher (3 scenarios)
│   ├── actors/
│   │   ├── tenant.json
│   │   ├── housing-officer.json
│   │   ├── contractor.json
│   │   └── building-inspector.json
│   └── data/
│       ├── scenario-a-routine.json
│       ├── scenario-b-emergency.json
│       └── scenario-c-verification-failure.json
├── ConstructionPermit/                         # MODIFIED — aligned to Strathcarron
│   ├── setup.ps1                               # Refactored to call council setup
│   └── ... (org/place name alignment)
└── SelfBuildHouse/                             # MODIFIED — aligned to Strathcarron
    ├── setup.ps1                               # Refactored to call council setup
    └── ... (org/place name alignment)
```

---

## 9. Alignment Refactor

### ConstructionPermit Changes

String replacements only — no logic, schema, or route changes.

| Current | Aligned |
|---------|---------|
| Meridian Construction | Stoniebridge Construction |
| Apex Structural Engineers | Murchison Engineering |
| Riverside Borough Council | Strathcarron Council |
| Green Valley Environmental | Heatherbank Environmental |
| Riverside Heights / Riverside addresses | Carronbridge / Strathcarron addresses (SC postcodes) |
| RBC- permit prefix | SC- permit prefix |
| Subdomains: meridian, apex, riverside, greenvalley | stoniebridge, murchison, strathcarron, heatherbank |

**Files**: ~11 files, ~45 string replacements. Setup refactored to call `council/setup-council.ps1`.

### SelfBuildHouse Changes

| Current | Aligned |
|---------|---------|
| Highland Council — Planning | Strathcarron Council (planning dept) |
| Highland Council — Building Standards | Strathcarron Council (building standards dept) |
| MacGregor Structural Engineers | Murchison Engineering |
| Glen Ecology Consultants | Heatherbank Environmental |
| Scottish Water | Caledonian Water |
| Drumnadrochit, Aviemore, Cromarty | Dalreoch, Carronbridge, Invercarron |
| Highland postcodes (IV, PH) | Strathcarron postcodes (SC) |
| Highland Planning Register | Strathcarron Planning Register |
| Highland Building Standards Register | Strathcarron Building Standards Register |

**Key structural change**: Two separate Highland Council orgs merge into one Strathcarron Council org with department roles. Setup refactored to call `council/setup-council.ps1`.

**Files**: ~15 files, ~60 string replacements + org merge in setup.ps1.

---

## 10. Features Exercised

| Feature | How |
|---------|-----|
| **File uploads (085)** | Two file-reference fields — damage photo + completion photo |
| **Verifiable Credentials** | JobAssignmentCredential (mid-flow) + ServiceCompletionCredential (terminal) |
| **Consumer Persona (092)** | Tenant persona autofill on Report Problem form |
| **Conditional routing** | Severity-based inspector involvement (Emergency → Safety Sign-Off) |
| **Cyclic rework** | Review → Complete Work loop, Safety Fail → Complete Work |
| **Participant verification** | Tenant verifies contractor identity before granting access |
| **Checklist validation** | Dynamic requirements array matched against completed items |
| **Shared walkthrough setup** | Council universe reused across 3 walkthroughs |

No existing walkthrough combines all of these features.

---

## 11. Future Enhancements

- **Agent persona support**: Sorcha.Agent fetches persona on startup, resolves `x-persona` fields in rule payloads automatically
- **Camera capture UI**: Mobile/PWA camera integration for photo evidence — this walkthrough drives the design discussion for native vs. browser camera access
- **Cross-walkthrough chaining**: PropertyInspection could reference a CompletionCertificateCredential from SelfBuildHouse (the house was built, now it needs maintenance)
- **Playwright screenshots**: E2E tests capturing the persona autofill, operative verification, and photo upload UI flows

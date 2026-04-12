# Council Property Inspection

**Purpose:** Council property services workflow demonstrating file uploads, operative identity verification (safeguarding), cyclic rework, and verifiable credentials across three organisations
**Date Created:** 2026-04-12
**Status:** ✅ Complete
**Prerequisites:** Docker Desktop, PowerShell 7+, Sorcha services running

---

## Overview

This walkthrough demonstrates a realistic council property repair workflow set in the fictional **Strathcarron Council** universe. A council tenant reports a maintenance problem with photo evidence. A housing officer triages the case, sets a work checklist, allocates a contractor, and issues a **JobAssignment Verifiable Credential**. The tenant uses that credential to verify the operative's identity at the door before granting access — a safeguarding measure for vulnerable residents. The contractor completes the repair with photo evidence and a delivery note. The housing officer reviews the completion and can send work back for rework. Emergency-severity jobs additionally require a **Building Inspector sign-off** before the tenant confirms satisfaction and receives a **ServiceCompletion Verifiable Credential**.

The walkthrough exercises:
- **File uploads** (Feature 085) — photo evidence at two stages (damage report, completion)
- **Mid-flow verifiable credential** — JobAssignmentCredential issued at Action 1, verified by tenant at Action 2
- **Consumer Persona autofill** (Feature 092) — tenant's name, phone, and address pre-filled from profile
- **Conditional routing** — Emergency severity triggers inspector sign-off branch
- **Cyclic rework** — Contractor can be sent back from review (Action 4 → 3) or inspector fail (Action 5 → 3)
- **Operative verification rejection** — Tenant can reject an operative, forcing re-allocation (Action 2 → 1)

---

## Strathcarron Council Universe

This walkthrough is part of the shared **Strathcarron Council** demo universe alongside ConstructionPermit and SelfBuildHouse. All organisations, places, and actors are fictional.

| Place | Type | Used In |
|-------|------|---------|
| Carronbridge, SC4 | Main town (council HQ) | Scenario A |
| Dalreoch, SC6 | Rural village | Scenario B |
| Invercarron, SC2 | Conservation village | Scenario C |

See `walkthroughs/council/README.md` for the full universe narrative.

---

## Organisations & Participants

| Participant ID | Display Name | Organisation | Role in Workflow |
|---|---|---|---|
| `tenant` | Council Tenant | Public Org (citizen) | Reports problem (0), verifies operative (2), confirms satisfaction (6) |
| `housing-officer` | Housing Officer | Strathcarron Council | Triages and allocates (1), reviews completion (4) |
| `contractor` | Site Operative | Stoniebridge Construction | Completes repair with photo evidence (3) |
| `building-inspector` | Building Inspector | Strathcarron Council | Safety sign-off for Emergency jobs only (5) |

---

## Prerequisites

1. Docker Desktop running
2. Sorcha services started: `docker-compose up -d`
3. Shared council universe provisioned: `pwsh -File walkthroughs/council/setup-council.ps1`

---

## Quick Start

```powershell
# 1. Provision register, blueprint, and tenant personas
pwsh -File walkthroughs/PropertyInspection/setup.ps1 -Profile gateway -Scenario a

# 2. Run the actors
pwsh -File walkthroughs/PropertyInspection/run-agents.ps1

# Run a specific scenario
pwsh -File walkthroughs/PropertyInspection/setup.ps1 -Profile gateway -Scenario b
pwsh -File walkthroughs/PropertyInspection/run-agents.ps1 -Scenario b
```

---

## Workflow

### Flow Diagram

```
[0] Report Problem ──▶ [1] Triage & Allocate ──▶ [2] Verify Operative
    (tenant)               (housing-officer)          (tenant)
    📷 damage photo        checklist + severity        │
    persona autofill       ──▶ JobAssignment VC        ├─ rejected → [1] (re-allocate)
                                                       └─ verified ↓

[3] Complete Work ◀──────────────────────────────── verified
    (contractor)
    📷 completion photo
    ✅ checklist sign-off
    delivery note
         │
         ▼
[4] Review Completion
    (housing-officer)
         │
    ├─ Rework ──────────────────────────────────▶ [3]
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

### Action Details

#### Action 0: Report Problem
**Participant:** `tenant` (Public Org)
**Purpose:** Submit maintenance request with damage photo

| Field | Type | Notes |
|---|---|---|
| `tenantName` | string | Persona autofill: `fullName` |
| `contactPhone` | string (tel) | Persona autofill: `defaultPhone` |
| `propertyAddress` | object | Persona autofill: `defaultAddress` |
| `description` | string, minLength: 10 | Description of the problem |
| `category` | enum: Plumbing, Electrical, Structural, Roofing, Other | Problem type |
| `urgencyDescription` | string | Tenant's view of urgency |
| `accessNotes` | string | Key safe, neighbour, availability |
| `damagePhoto` | file-reference | Photo of damage (JPEG, max 16MB) |

---

#### Action 1: Triage & Allocate
**Participant:** `housing-officer` (Strathcarron Council)
**Purpose:** Set severity, work checklist, and issue JobAssignment credential

| Field | Type | Notes |
|---|---|---|
| `severity` | enum: Routine, Urgent, Emergency | Drives routing at Action 4 |
| `targetCompletionDate` | date | SLA target |
| `requirements` | array of strings | Checklist of work items |
| `allocationNotes` | string | Internal notes |
| `assignedOperativeName` | string | Name of person being sent |

**Credential issued:** `JobAssignmentCredential` → contractor wallet
Claims: jobReference, propertyAddress, tenantName, assignedOperativeName, validFrom, validUntil, scopeSummary

---

#### Action 2: Verify Operative
**Participant:** `tenant` (Public Org)
**Purpose:** Confirm operative identity via JobAssignmentCredential before granting door access

| Field | Type | Notes |
|---|---|---|
| `operativeVerified` | boolean | Tenant confirms identity |
| `verificationNotes` | string | Optional notes |

**Routing:** `operativeVerified == true` → [3] | `operativeVerified == false` → [1] (re-allocate)

---

#### Action 3: Complete Work
**Participant:** `contractor` (Stoniebridge Construction)
**Purpose:** Submit completed work with photo evidence and delivery note

| Field | Type | Notes |
|---|---|---|
| `completedItems` | array of strings | Tick-off against checklist from Action 1 |
| `materialsUsed` | string | Materials list |
| `hoursWorked` | number, min: 0.5 | Labour hours |
| `completionPhoto` | file-reference | Photo of completed work (JPEG, max 16MB) |
| `deliveryNotes` | string | Contractor's summary of work done |

---

#### Action 4: Review Completion
**Participant:** `housing-officer` (Strathcarron Council)
**Purpose:** Accept completion or send back for rework

| Field | Type | Notes |
|---|---|---|
| `decision` | enum: Accepted, Rework | Pass or send back |
| `reviewNotes` | string | Assessment notes |
| `reworkInstructions` | string | Required if Rework |

**Routing:**
- `decision == "Rework"` → [3]
- `decision == "Accepted"` AND `severity == "Emergency"` → [5]
- `decision == "Accepted"` AND `severity != "Emergency"` → [6]

---

#### Action 5: Safety Sign-Off (Emergency only)
**Participant:** `building-inspector` (Strathcarron Council)
**Purpose:** Confirm structural and electrical safety for emergency repairs

| Field | Type | Notes |
|---|---|---|
| `structuralSafe` | boolean | Structural integrity check |
| `electricalSafe` | boolean | Electrical safety check |
| `gasSafe` | boolean | Gas safety check (if applicable) |
| `complianceNotes` | string | Inspector notes |
| `signOffDecision` | enum: Pass, Fail | Overall decision |

**Routing:** `signOffDecision == "Pass"` → [6] | `signOffDecision == "Fail"` → [3]

---

#### Action 6: Confirm Satisfaction
**Participant:** `tenant` (Public Org)
**Purpose:** Record tenant satisfaction and close the job

| Field | Type | Notes |
|---|---|---|
| `satisfied` | boolean | Overall satisfaction |
| `feedbackNotes` | string | Free text feedback |
| `rating` | integer 1–5 | Star rating |

**Credential issued:** `ServiceCompletionCredential` → tenant wallet. Terminal action.

---

## Test Scenarios

### Scenario A: Routine Plumbing Repair (Happy Path)

- **Property:** 14 Moray Crescent, Carronbridge, SC4 2TL
- **Tenant:** Mrs Flora MacInnes
- **Problem:** Leaking kitchen tap, water damage to cabinet underneath
- **Severity:** Routine | **Target:** 10 working days
- **Path:** 0 → 1 → 2 → 3 → 4 → 6 (6 actions)

| Step | Action | Participant | Key Input |
|---|---|---|---|
| 1 | Report Problem | tenant (Flora) | Persona autofill: name, phone, address; damage photo |
| 2 | Triage & Allocate | housing-officer | Routine, 4 checklist items, issues JobAssignment VC |
| 3 | Verify Operative | tenant (Flora) | operativeVerified: true |
| 4 | Complete Work | contractor | All 4 items, 3 hours, completion photo |
| 5 | Review Completion | housing-officer | Accepted (Routine → no inspector) |
| 6 | Confirm Satisfaction | tenant (Flora) | rating: 5, "Very tidy job, thank you" |

**Expected:** 6 actions executed, JobAssignment VC issued at step 2, ServiceCompletion VC issued at step 6, no rework or inspector.

---

### Scenario B: Emergency Ceiling Collapse (Rework + Inspector)

- **Property:** 7 Loch Morach Drive, Dalreoch, SC6 8JN
- **Tenant:** Mr Angus Beaton
- **Problem:** Bedroom ceiling partially collapsed after roof leak
- **Severity:** Emergency | **Target:** 24h (make safe), 5 days (full repair)
- **Path:** 0 → 1 → 2 → 3 → 4(rework) → 3 → 4(accepted) → 5 → 6 (9 actions with loop)

| Step | Action | Participant | Key Input |
|---|---|---|---|
| 1 | Report Problem | tenant (Angus) | Damage photo of collapsed ceiling |
| 2 | Triage & Allocate | housing-officer | Emergency, 7 checklist items including electrical check |
| 3 | Verify Operative | tenant (Angus) | operativeVerified: true |
| 4 | Complete Work (attempt 1) | contractor | 6 of 7 items — misses electrical check |
| 5 | Review Completion | housing-officer | Rework: "Electrical check not evidenced" |
| 6 | Complete Work (attempt 2) | contractor | All 7 items, updated photos |
| 7 | Review Completion | housing-officer | Accepted (Emergency → triggers inspector) |
| 8 | Safety Sign-Off | building-inspector | Structural + electrical safe: Pass |
| 9 | Confirm Satisfaction | tenant (Angus) | rating: 4, "Took two visits but the repair is solid" |

**Expected:** 9 actions with one rework loop, Safety Sign-Off reached, both VCs issued.

---

### Scenario C: Operative Verification Failure (Re-allocation)

- **Property:** 3 Invercarron Row, Invercarron, SC2 5PA
- **Tenant:** Mrs Eilidh Drummond (vulnerable adult, lives alone)
- **Problem:** Front door lock mechanism broken, door won't secure
- **Severity:** Urgent | **Target:** 48 hours
- **Path:** 0 → 1 → 2(rejected) → 1 → 2(accepted) → 3 → 4 → 6 (8 actions)

| Step | Action | Participant | Key Input |
|---|---|---|---|
| 1 | Report Problem | tenant (Eilidh) | Persona autofill; urgency described |
| 2 | Triage & Allocate (first) | housing-officer | Urgent, first contractor allocated, JobAssignment VC issued |
| 3 | Verify Operative (reject) | tenant (Eilidh) | operativeVerified: false — "credential doesn't match" |
| 4 | Triage & Allocate (re-allocate) | housing-officer | Fresh JobAssignment VC for replacement operative |
| 5 | Verify Operative (accept) | tenant (Eilidh) | operativeVerified: true |
| 6 | Complete Work | contractor | All 4 items, 2 hours |
| 7 | Review Completion | housing-officer | Accepted (Urgent → no inspector) |
| 8 | Confirm Satisfaction | tenant (Eilidh) | rating: 5, "Glad I could check who they were before opening the door" |

**Expected:** 8 actions with one re-allocation loop, safeguarding path exercised, ServiceCompletion VC issued.

---

## Verifiable Credentials

### JobAssignmentCredential

| Attribute | Value |
|---|---|
| **Issuer** | housing-officer (Strathcarron Council) |
| **Subject** | contractor (Stoniebridge Construction) |
| **Issued at** | Action 1 (Triage & Allocate) |
| **Verified at** | Action 2 (Verify Operative) |
| **Claims** | jobReference, propertyAddress, tenantName, assignedOperativeName, validFrom, validUntil, scopeSummary |
| **Purpose** | Tenant verifies operative identity at the door — safeguarding for vulnerable residents |

### ServiceCompletionCredential

| Attribute | Value |
|---|---|
| **Issuer** | tenant (confirms satisfaction) |
| **Subject** | Strathcarron Council |
| **Issued at** | Action 6 (Confirm Satisfaction) |
| **Claims** | jobReference, propertyAddress, category, completionDate, satisfactionRating, contractorName |
| **Purpose** | Permanent proof of completed repair |

---

## Consumer Persona Integration

The tenant's Report Problem form (Action 0) uses **Consumer Persona** (Feature 092) to autofill:

| Field | Persona Source |
|---|---|
| `tenantName` | `fullName` |
| `contactPhone` | `defaultPhone` |
| `propertyAddress` | `defaultAddress` (street, locality, postalCode) |

The `setup.ps1` creates a persona for each tenant via `PUT /me/persona` before running the scenario. Fields autofilled from the persona appear with a cream tint and a `self` provenance indicator. The tenant can edit any autofilled field before submitting.

---

## Features Exercised

| Feature | Where |
|---|---|
| File uploads (Feature 085) | Action 0 (damage photo), Action 3 (completion photo) |
| Mid-flow verifiable credential | JobAssignmentCredential issued at Action 1 |
| Terminal verifiable credential | ServiceCompletionCredential issued at Action 6 |
| Consumer Persona autofill (Feature 092) | Action 0 — tenant name, phone, address |
| Conditional routing | Action 4 branches on severity (Emergency → inspector, otherwise → satisfaction) |
| Cyclic rework | Action 4 → 3 (incomplete work), Action 5 → 3 (safety fail) |
| Operative verification rejection | Action 2 → 1 (re-allocation loop) |
| Multi-user same org | housing-officer and building-inspector are both Strathcarron Council |
| Selective disclosure | Each participant sees only their relevant fields |

---

## File Structure

```
walkthroughs/PropertyInspection/
├── README.md                          # This file
├── config.json                        # Walkthrough metadata
├── property-inspection-template.json  # Blueprint template (7 actions)
├── setup.ps1                          # Register + blueprint + persona setup
├── run-agents.ps1                     # Actor launcher
├── actors/
│   ├── tenant.json                    # Council tenant (citizen)
│   ├── housing-officer.json           # Strathcarron Council
│   ├── contractor.json                # Stoniebridge Construction
│   └── building-inspector.json        # Strathcarron Council
└── data/
    ├── scenario-a-routine.json        # Happy path — plumbing repair
    ├── scenario-b-emergency.json      # Emergency — rework + inspector
    └── scenario-c-verification-failure.json  # Re-allocation — safeguarding
```

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Council setup fails | Run `pwsh -File walkthroughs/council/setup-council.ps1 -Force` to recreate |
| Tenant persona not autofilling | Check that `setup.ps1` completed the `PUT /me/persona` step |
| File upload rejected | Verify Docker has sufficient memory (≥4GB); check chunk size ≤4MB |
| JobAssignment VC not visible to tenant | Ensure Action 1 completed and VC was written to contractor wallet |
| Building inspector step skipped | Expected for Routine and Urgent scenarios — inspector only runs for Emergency severity |
| `council-state.json` stale | Delete `walkthroughs/council/council-state.json` and re-run council setup |

---

## Related Documentation

- [Strathcarron Council Universe](../council/README.md) — shared orgs, geography, and actors
- [ConstructionPermit Walkthrough](../ConstructionPermit/) — 4-org construction permit (same universe)
- [SelfBuildHouse Walkthrough](../SelfBuildHouse/) — 6-org self-build with planning and building standards
- [Consumer Persona](../../docs/) — Feature 092 overview
- [Stored Data Transactions](../../CLAUDE.md#stored-data-transactions-api-feature-085) — File upload feature
- [CLAUDE.md](../../CLAUDE.md) — project conventions

# Construction Permit Approval

**Purpose:** Multi-org, multi-user workflow demonstrating conditional routing, calculations, and verifiable credential issuance across four organizations
**Date Created:** 2026-02-17
**Status:** ✅ Complete
**Prerequisites:** Docker Desktop, PowerShell 7+, Sorcha services running

---

## Overview

This walkthrough demonstrates a realistic construction permit approval process involving four organizations and five participants. A contractor submits building plans, which pass through structural assessment, planning review, optional environmental review (conditional on risk score), building control inspection, and final council approval — producing a **Building Permit Verifiable Credential** at the end.

The scenario exercises every major Sorcha capability:
- **Multi-organization participation** — 4 separate orgs with independent wallets
- **Multi-user within one org** — Council has both a Planning Officer and Building Control Inspector
- **Conditional routing** — high-risk projects go through environmental review, low-risk skip it
- **Calculations** — risk score computed from building parameters, permit fee from project value
- **Rejection paths** — Planning Officer can reject back to contractor
- **Verifiable credential issuance** — approved permits produce a signed VC

---

## Scenario

### The Story

**Stoniebridge Construction** wants to build a new development on a brownfield site. They submit their plans through Sorcha, which routes the application through structural assessment, council planning, an optional environmental review (for high-risk builds), building control sign-off, and finally back to the council planning officer for permit issuance.

Two test runs demonstrate the branching:
1. **Low risk** — 3-storey residential (risk score 5.5) skips environmental review
2. **High risk** — 8-storey commercial (risk score 14.5) triggers environmental review

### Organizations

| Organization | Role | Description |
|---|---|---|
| **Stoniebridge Construction** | Contractor | Submits building application with plans and site data |
| **Murchison Engineering** | Structural Assessor | Independent review of structural integrity, calculates risk score |
| **Strathcarron Council** | Local Authority | Two participants: Planning Officer (zoning + final approval) and Building Control Inspector (technical sign-off) |
| **Heatherbank Environmental** | Environmental Assessor | Conducts impact assessment when risk score is high (conditional) |

### Participants

| Participant ID | Display Name | Organization | Role in Workflow |
|---|---|---|---|
| `contractor` | Site Manager | Stoniebridge Construction | Submits application (Action 1) |
| `structural-engineer` | Lead Engineer | Murchison Engineering | Structural assessment + risk calculation (Action 2) |
| `planning-officer` | Planning Officer | Strathcarron Council | Zoning review + final approval (Actions 3, 6) |
| `environmental-assessor` | Environmental Consultant | Heatherbank Environmental | Environmental impact assessment (Action 4, conditional) |
| `building-control` | Building Control Inspector | Strathcarron Council | Technical inspection + fee calculation (Action 5) |

---

## Workflow

### Flow Diagram

```
                              ┌─────────────────────┐
                              │  [1] Submit          │
                              │  Application         │
                              │  (contractor)        │
                              └─────────┬────────────┘
                                        │
                              ┌─────────▼────────────┐
                              │  [2] Structural       │
                              │  Assessment           │
                              │  (structural-engineer)│
                              │                       │
                              │  Calculates:          │
                              │  - riskScore          │
                              │  - structuralGrade    │
                              └─────────┬────────────┘
                                        │
                              ┌─────────▼────────────┐
                              │  [3] Planning Review  │
                         ┌────│  (planning-officer)   │────┐
                         │    │                       │    │
                         │    │  Can REJECT → [1]     │    │
                         │    └───────────────────────┘    │
                         │                                  │
                    riskScore >= 7                     riskScore < 7
                         │                                  │
               ┌─────────▼────────────┐                    │
               │  [4] Environmental   │                    │
               │  Assessment          │                    │
               │  (env-assessor)      │                    │
               │                      │                    │
               │  Can REJECT → [1]    │                    │
               └─────────┬────────────┘                    │
                         │                                  │
                         └──────────┬───────────────────────┘
                                    │
                          ┌─────────▼────────────┐
                          │  [5] Building Control │
                          │  Inspection           │
                          │  (building-control)   │
                          │                       │
                          │  Calculates:          │
                          │  - permitFee          │
                          └─────────┬────────────┘
                                    │
                          ┌─────────▼────────────┐
                          │  [6] Final Approval   │
                          │  (planning-officer)   │
                          │                       │
                          │  Issues:              │
                          │  Building Permit VC   │
                          │                       │
                          │  Can REJECT → [1]     │
                          └───────────────────────┘
```

### Action Details

#### Action 1: Submit Application
**Participant:** `contractor` (Stoniebridge Construction)
**Purpose:** Submit building plans and project details for assessment

| Field | Type | Validation | Description |
|---|---|---|---|
| `projectName` | string | required, min 3 chars | Name of the development |
| `siteAddress` | string | required | Full postal address of the site |
| `buildingType` | enum | residential, commercial, industrial | Category of construction |
| `estimatedValue` | number | required, min 10000 | Project value in GBP |
| `grossFloorArea` | number | required, min 10 | Total floor area in square metres |
| `storeys` | integer | required, min 1, max 100 | Number of storeys |
| `existingStructure` | boolean | required | Whether there is an existing building on site |

**Disclosures:**
- `contractor` sees all submitted data
- `structural-engineer` sees all submitted data (needs full picture for assessment)

---

#### Action 2: Structural Assessment
**Participant:** `structural-engineer` (Murchison Engineering)
**Purpose:** Review structural plans and calculate risk score

| Field | Type | Description |
|---|---|---|
| `loadRating` | number | Calculated structural load rating (kN/m2) |
| `foundationType` | enum: piled, raft, strip, pad | Recommended foundation type |
| `structuralGrade` | enum: A, B, C, D | Overall structural grade (A=excellent, D=poor) |
| `structuralNotes` | string | Engineer's assessment notes |

**Calculations (JSON Logic):**
```json
{
  "riskScore": {
    "*": [
      { "+": [
        { "*": [{ "var": "storeys" }, 1.5] },
        { "/": [{ "var": "grossFloorArea" }, 500] }
      ]},
      { "if": [
        { "==": [{ "var": "buildingType" }, "industrial"] }, 1.5,
        { "==": [{ "var": "buildingType" }, "commercial"] }, 1.2,
        1.0
      ]}
    ]
  }
}
```

**Risk score examples:**
| Scenario | Storeys | Floor Area | Type | Formula | Risk Score |
|---|---|---|---|---|---|
| Small house | 2 | 150 m2 | residential | (2*1.5 + 150/500) * 1.0 | 3.3 |
| Low-rise residential | 3 | 800 m2 | residential | (3*1.5 + 800/500) * 1.0 | 6.1 |
| Mid-rise office | 6 | 2000 m2 | commercial | (6*1.5 + 2000/500) * 1.2 | 15.6 |
| Large warehouse | 2 | 5000 m2 | industrial | (2*1.5 + 5000/500) * 1.5 | 19.5 |

**Disclosures:**
- `structural-engineer` sees all data (own + previous)
- `planning-officer` sees: projectName, siteAddress, buildingType, estimatedValue, riskScore, structuralGrade, structuralNotes

---

#### Action 3: Planning Review
**Participant:** `planning-officer` (Strathcarron Council)
**Purpose:** Evaluate zoning compliance and route based on risk

| Field | Type | Description |
|---|---|---|
| `zoningCompliant` | boolean | Whether the project complies with local zoning regulations |
| `planningNotes` | string | Planning officer's assessment notes |

**Routing (conditional):**
- If `riskScore >= 7` → Route to **Action 4** (Environmental Assessment)
- If `riskScore < 7` → Route to **Action 5** (Building Control Inspection)

**Rejection:** Can reject back to Action 1 (contractor must resubmit)

**Disclosures:**
- `planning-officer` sees all accumulated data
- `environmental-assessor` sees: projectName, siteAddress, buildingType, grossFloorArea, riskScore, structuralGrade (if routed to action 4)
- `building-control` sees: projectName, siteAddress, buildingType, estimatedValue, grossFloorArea, storeys, structuralGrade, loadRating (if routed to action 5)

---

#### Action 4: Environmental Impact Assessment (Conditional)
**Participant:** `environmental-assessor` (Heatherbank Environmental)
**Purpose:** Assess environmental impact for high-risk projects (riskScore >= 7)

*This action is only reached when the planning officer routes high-risk projects here.*

| Field | Type | Description |
|---|---|---|
| `environmentalImpact` | enum: low, medium, high | Assessed level of environmental impact |
| `mitigationRequired` | boolean | Whether mitigation measures are needed |
| `environmentalConditions` | string | Conditions that must be met (e.g., noise limits, drainage) |

**Rejection:** Can reject back to Action 1 if environmental concerns are too severe

**Disclosures:**
- `environmental-assessor` sees: projectName, siteAddress, buildingType, grossFloorArea, riskScore, structuralGrade
- `building-control` sees: projectName, siteAddress, buildingType, estimatedValue, grossFloorArea, storeys, structuralGrade, loadRating, environmentalImpact, mitigationRequired

---

#### Action 5: Building Control Inspection
**Participant:** `building-control` (Strathcarron Council)
**Purpose:** Technical review and compliance check, calculate permit fee

| Field | Type | Description |
|---|---|---|
| `structuralApproved` | boolean | Structural plans meet building regulations |
| `fireCompliant` | boolean | Fire safety requirements satisfied |
| `accessCompliant` | boolean | Accessibility requirements satisfied |
| `inspectionNotes` | string | Inspector's technical notes |

**Calculations (JSON Logic):**
```json
{
  "permitFee": {
    "max": [
      250,
      { "+": [
        { "*": [{ "var": "estimatedValue" }, 0.002] },
        { "*": [{ "var": "grossFloorArea" }, 1.50] }
      ]}
    ]
  }
}
```

**Fee examples:**
| Project Value | Floor Area | Fee Formula | Permit Fee |
|---|---|---|---|
| £150,000 | 120 m2 | max(250, 150000*0.002 + 120*1.50) | £480 |
| £500,000 | 800 m2 | max(250, 500000*0.002 + 800*1.50) | £2,200 |
| £5,000,000 | 3000 m2 | max(250, 5000000*0.002 + 3000*1.50) | £14,500 |
| £30,000 | 50 m2 | max(250, 30000*0.002 + 50*1.50) | £250 (minimum) |

**Disclosures:**
- `building-control` sees all accumulated data
- `planning-officer` sees all accumulated data (needs full picture for final decision)

---

#### Action 6: Final Approval
**Participant:** `planning-officer` (Strathcarron Council)
**Purpose:** Issue or reject the building permit and mint the verifiable credential

| Field | Type | Description |
|---|---|---|
| `approved` | boolean | Final approval decision |
| `permitNumber` | string | Unique permit reference (e.g., SC-2026-00142) |
| `validUntil` | string (date) | Permit expiry date (typically 3 years) |
| `conditions` | string | Any conditions attached to the permit |

**Rejection:** Can reject back to Action 1 (contractor receives rejection with reasons)

**Credential Issuance:**
On approval, a **Building Permit Verifiable Credential** is minted:

```json
{
  "type": ["VerifiableCredential", "BuildingPermitCredential"],
  "issuer": "did:sorcha:w:{planning-officer-wallet}",
  "credentialSubject": {
    "permitNumber": "SC-2026-00142",
    "projectName": "Carronbridge Heights",
    "siteAddress": "14 Waterfront Lane, Carronbridge, SC1 4AB",
    "buildingType": "residential",
    "storeys": 3,
    "riskScore": 6.1,
    "structuralGrade": "B",
    "permitFee": 2200,
    "conditions": "Standard drainage conditions apply",
    "validUntil": "2029-02-17"
  },
  "expirationDate": "2029-02-17"
}
```

**Disclosures:**
- `planning-officer` sees all accumulated data
- `contractor` sees: permitNumber, approved, validUntil, conditions, permitFee (the information they need to proceed)

---

## Test Scenarios

### Scenario A: Low-Risk Residential (Happy Path)

A straightforward 3-storey residential development that skips environmental review.

| Step | Action | Participant | Key Input |
|---|---|---|---|
| 1 | Submit Application | contractor | 3 storeys, 800 m2, residential, £500,000 |
| 2 | Structural Assessment | structural-engineer | loadRating: 4.5, grade: B |
| 3 | Planning Review | planning-officer | zoningCompliant: true |
| — | *riskScore = 6.1, routes to Action 5 (skips environmental)* | | |
| 4 | Building Control | building-control | all compliant |
| 5 | Final Approval | planning-officer | approved, permit issued |

**Expected:** 5 actions executed, risk score 6.1, permit fee £2,200, Building Permit VC issued.

### Scenario B: High-Risk Commercial

An 8-storey commercial building that triggers the environmental review branch.

| Step | Action | Participant | Key Input |
|---|---|---|---|
| 1 | Submit Application | contractor | 8 storeys, 3500 m2, commercial, £5,000,000 |
| 2 | Structural Assessment | structural-engineer | loadRating: 8.2, grade: A |
| 3 | Planning Review | planning-officer | zoningCompliant: true |
| — | *riskScore = 22.8, routes to Action 4 (environmental review)* | | |
| 4 | Environmental Assessment | environmental-assessor | impact: medium, mitigation: true |
| 5 | Building Control | building-control | all compliant |
| 6 | Final Approval | planning-officer | approved, permit issued |

**Expected:** 6 actions executed, risk score 22.8, permit fee £15,250, environmental conditions attached, Building Permit VC issued.

### Scenario C: Rejection

A project that fails planning review due to zoning non-compliance.

| Step | Action | Participant | Key Input |
|---|---|---|---|
| 1 | Submit Application | contractor | 4 storeys, 1200 m2, commercial, £2,000,000 |
| 2 | Structural Assessment | structural-engineer | loadRating: 5.0, grade: B |
| 3 | Planning Review | planning-officer | zoningCompliant: false, REJECT |

**Expected:** Workflow rejected at action 3, routes back to contractor with planning officer's notes.

---

## Files in This Walkthrough

| File | Description |
|---|---|
| `README.md` | This file — scenario overview and action specifications |
| `construction-permit-template.json` | Blueprint template with parameterised participants |
| `test-construction-permit.ps1` | Main walkthrough script (all 3 scenarios) |
| `data/scenario-a-low-risk.json` | Input data for low-risk residential scenario |
| `data/scenario-b-high-risk.json` | Input data for high-risk commercial scenario |
| `data/scenario-c-rejection.json` | Input data for rejection scenario |
| `RESULTS.md` | Test results and findings |

---

## Quick Start

```powershell
# 1. Ensure Docker services are running
docker-compose up -d

# 2. Run the walkthrough
./walkthroughs/ConstructionPermit/test-construction-permit.ps1

# Or run a specific scenario
./walkthroughs/ConstructionPermit/test-construction-permit.ps1 -Scenario A
```

---

## Key Concepts Demonstrated

| Concept | Where |
|---|---|
| Template parameterisation | Participant wallet addresses injected at instance creation |
| Conditional routing | Action 3 branches on riskScore (>= 7 or < 7) |
| Rejection routing | Actions 3, 4, 6 can reject back to Action 1 |
| Chained calculations | Risk score (Action 2) feeds routing; permit fee (Action 5) feeds credential |
| Selective disclosure | Each participant sees only what they need |
| Multi-user same org | Planning Officer and Building Control are both Strathcarron Council |
| Verifiable credential | Building Permit VC issued on final approval |
| Credential downstream use | Contractor can present the permit VC to other systems/inspectors |

---

## Related Documentation

- [Blueprint Builder Skill](../../.claude/skills/blueprint-builder/)
- [Verifiable Credentials](../../CLAUDE.md#verifiable-credentials)
- [SelfBuildHouse Walkthrough](../SelfBuildHouse/) — 6-org, 2-register advanced variant with cross-register VCs
- [CLAUDE.md](../../CLAUDE.md) — project conventions

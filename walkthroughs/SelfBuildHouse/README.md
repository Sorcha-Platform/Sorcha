# Self-Build House: Planning Permission & Building Warrant

A comprehensive walkthrough demonstrating the full Scottish self-build house approval process — from planning application through to building warrant, staged inspections, and completion certificate — running across **two registers** with **verifiable credentials** bridging the gap between them.

## Overview

This walkthrough models the real-world process a member of the public goes through to build a house in Scotland. It exercises the widest set of Sorcha platform features of any walkthrough:

| Feature | How It's Used |
|---------|---------------|
| **Multi-register** | Planning Register + Building Standards Register |
| **Cross-register VCs** | Planning Permission VC (Register 1) presented as prerequisite on Register 2 |
| **Credential chain** | Planning Permission → Building Warrant → Completion Certificate |
| **6 organisations** | Member of public + 5 professional/government bodies |
| **7 participants** | 2 share the Building Standards org (officer + inspector) |
| **Document uploads** | Site plans, structural calcs, ecology surveys, inspection photos, certificates |
| **Conditional routing** | Protected species triggers mitigation plan; pass/fail inspections |
| **Rejection loops** | Request amendments, failed inspections with remediation, terminal refusal |
| **JSON Logic calculations** | Foundation risk, planning fees, warrant fees, structural adequacy, energy performance |
| **Selective disclosure** | Consultees see only relevant data; inspector doesn't see cost data |
| **Credential requirements** | Actions gated on presenting valid VCs |
| **Staged inspections** | Foundation → Structure & Weathertight → Final Completion |

---

## Organisations & Participants

| Organisation | Participant | Role |
|-------------|-------------|------|
| *(Member of Public)* | `self-builder` | Applicant — submits applications, uploads evidence, receives credentials |
| Strathcarron Council — Planning | `planning-officer` | Reviews planning application, issues Planning Permission |
| Strathcarron Council — Building Standards | `building-standards-officer` | Reviews warrant against 7 Scottish standards, issues Building Warrant |
| Strathcarron Council — Building Standards | `building-inspector` | Conducts staged site inspections, issues Completion Certificate |
| Caledonian Water | `utilities-officer` | Water supply & drainage consultation |
| Murchison Engineering | `structural-engineer` | Site investigation, structural calculations, foundation design |
| Heatherbank Environmental | `ecologist` | Habitat survey, protected species assessment, mitigation planning |

---

## Registers

| Register | Purpose | Owner |
|----------|---------|-------|
| **Strathcarron Planning Register** | Planning applications, consultations, decisions, and Planning Permission VCs | Planning Officer |
| **Strathcarron Building Standards Register** | Building warrant applications, staged inspections, and Completion Certificate VCs | Building Standards Officer |

---

## Blueprint 1: Planning Permission

**Register:** Strathcarron Planning Register
**Actions:** 7 (including conditional action 4)
**Credential Issued:** `PlanningPermissionCredential`

### Action Flow

```
┌──────────────────────────┐
│ 1. Submit Planning       │ ◄─── request amendments ──────┐
│    Application           │                                │
│    (self-builder)        │                                │
└────────┬─────────────────┘                                │
         │                                                  │
         ▼                                                  │
┌──────────────────────────┐                                │
│ 2. Site Investigation    │                                │
│    Report                │                                │
│    (structural-engineer) │                                │
└────────┬─────────────────┘                                │
         │                                                  │
         ▼                                                  │
┌──────────────────────────┐     ┌────────────────────┐     │
│ 3. Ecological Survey     │────▶│ 4. Species         │     │
│    (ecologist)           │     │    Mitigation Plan  │     │
│                          │     │    (ecologist)      │     │
└────────┬─────────────────┘     └────────┬───────────┘     │
         │ no protected species           │                 │
         ├────────────────────────────────┘                 │
         ▼                                                  │
┌──────────────────────────┐                                │
│ 5. Utilities             │──── not feasible ──────────────┘
│    Consultation          │
│    (caledonian-water)      │
└────────┬─────────────────┘
         │ feasible
         ▼
┌──────────────────────────┐
│ 6. Planning Review       │──── request amendments ────────┘
│    & Decision            │
│    (planning-officer)    │──── refuse ──▶ [TERMINAL]
└────────┬─────────────────┘
         │ approve
         ▼
┌──────────────────────────┐
│ 7. Issue Planning        │
│    Permission            │──▶ PlanningPermissionCredential
│    (planning-officer)    │
└──────────────────────────┘
```

### Action Details

| # | Action | Sender | Key Data | Uploads | Calculations |
|---|--------|--------|----------|---------|--------------|
| 1 | Submit Planning Application | self-builder | Site address, OS grid ref, dwelling type, storeys, floor area, construction method, build cost | Site location plan, floor plans, elevations | — |
| 2 | Site Investigation Report | structural-engineer | Soil classification, bearing capacity, water table depth, radon risk, slope stability | Ground investigation report, borehole logs | `foundationRiskScore` (0-15) |
| 3 | Ecological Survey | ecologist | Habitat classification, protected species (Y/N), species list, impact rating | Phase 1 habitat survey, site photographs | — |
| 4 | Species Mitigation Plan | ecologist | Mitigation type, NatureScot licence, timing restrictions, mitigation measures, cost | Species Protection Plan | — |
| 5 | Utilities Consultation | utilities-officer | Water supply feasibility, drainage solution, surface water plan, connection cost | Drainage assessment report | — |
| 6 | Planning Review & Decision | planning-officer | Local plan compliance, design quality, landscape impact, objections, decision | Planning officer report | `planningFee` |
| 7 | Issue Planning Permission | planning-officer | Permit reference, grant/expiry dates, conditions, approved plan refs | — | — |

### Calculations

**Foundation Risk Score** (Action 2):
- Soil type: rock=0, gravel=1, sand=2, clay=3, peat=4
- Water table: <1m=3, <2m=2, <3m=1, else=0
- Contamination: +3 if found
- Radon: high=2, medium=1, low=0
- Slope: unstable=3, marginal=1, stable=0
- **Range:** 0 (excellent) to 15 (severe)

**Planning Fee** (Action 6):
- ≤75m² → £300
- ≤150m² → £600
- >150m² → £600 + £2.50/m² above 150

---

## Blueprint 2: Building Warrant & Completion

**Register:** Strathcarron Building Standards Register
**Actions:** 7 (including 3 staged inspections)
**Credential Required:** `PlanningPermissionCredential` (from Blueprint 1!)
**Credentials Issued:** `BuildingWarrantCredential`, `CompletionCertificateCredential`

### Action Flow

```
┌──────────────────────────────┐
│ 1. Submit Building Warrant   │ ◄─── request amendments ──┐
│    Application               │                            │
│    (self-builder)            │                            │
│    REQUIRES: Planning        │                            │
│    Permission VC ✓           │                            │
└────────┬─────────────────────┘                            │
         │                                                  │
         ▼                                                  │
┌──────────────────────────────┐                            │
│ 2. Structural Calculations   │                            │
│    Submission                │                            │
│    (structural-engineer)     │                            │
└────────┬─────────────────────┘                            │
         │                                                  │
         ▼                                                  │
┌──────────────────────────────┐                            │
│ 3. Building Standards Review │─── amendments ─────────────┘
│    (7 Scottish Standards)    │
│    (building-standards-off)  │─── refuse ──▶ [TERMINAL]
└────────┬─────────────────────┘
         │ approve
         ▼
┌──────────────────────────────┐
│ 4. Issue Building Warrant    │──▶ BuildingWarrantCredential
│    (building-standards-off)  │
└────────┬─────────────────────┘
         │
         ▼
┌──────────────────────────────┐
│ 5. Foundation Inspection     │ ◄─── fail (remediate) ──┐
│    (building-inspector)      │                          │
│    REQUIRES: Building        │──── fail ────────────────┘
│    Warrant VC ✓              │
└────────┬─────────────────────┘
         │ pass
         ▼
┌──────────────────────────────┐
│ 6. Structure & Weathertight  │ ◄─── fail (remediate) ──┐
│    Inspection                │                          │
│    (building-inspector)      │──── fail ────────────────┘
└────────┬─────────────────────┘
         │ pass
         ▼
┌──────────────────────────────┐
│ 7. Final Inspection &        │
│    Completion Certificate    │──▶ CompletionCertificateCredential
│    (building-inspector)      │
└──────────────────────────────┘
```

### The 7 Scottish Building Standards (Action 3)

| Standard | Area | What's Checked |
|----------|------|----------------|
| 1 — Structure | Structural stability | Foundation design, structural frame, loading |
| 2 — Fire | Fire safety | Detection (LD2), escape routes, fire resistance |
| 3 — Environment | Environmental protection | Radon, DPC, ventilation, drainage |
| 4 — Safety | Personal safety | Guarding, glazing, electrical safety |
| 5 — Noise | Acoustic performance | Party wall/floor insulation (N/A for detached) |
| 6 — Energy | Energy efficiency | SAP rating, insulation, air-tightness, heating |
| 7 — Sustainability | Low-carbon heating | LZC heating target (ASHP/GSHP/biomass) |

### Calculations

**Structural Adequacy Score** (Action 2):
- `min(safetyFactor, 3.0) × eurocodeMultiplier × foundationMultiplier`
- Eurocode compliance: 1.0 if yes, 0.5 if no
- Foundation: piled=0.9, raft=0.95, others=1.0
- **Typical range:** 0.5 to 3.0

**Building Warrant Fee** (Action 3):
- ≤£50k → £300
- ≤£100k → £600
- ≤£250k → £1,200
- >£250k → £1,200 + £50 per £10k above £250k

**Energy Performance Check** (Action 6):
- ≤3.0 m³/h/m² → "excellent"
- ≤5.0 → "compliant"
- ≤7.0 → "remediation-needed"
- >7.0 → "fail"

---

## Verifiable Credential Flow

This is the key differentiator of this walkthrough — **credentials chain across registers**:

```
Register 1 (Planning)              Register 2 (Building Standards)
─────────────────────              ─────────────────────────────────
Action 7 ──ISSUES──▶               ┌──────────────────────┐
PlanningPermission   ──PRESENTED──▶│ Action 1: Submit BW  │
Credential                         │ (self-builder)       │
                                   └──────────┬───────────┘
                                              │
                                   Action 4 ──ISSUES──▶
                                   BuildingWarrant  ──PRESENTED──▶ Action 5
                                   Credential
                                              │
                                   Action 7 ──ISSUES──▶
                                   CompletionCertificate
                                   Credential
```

| Credential | Issued By | Issued To | Used At | Expiry |
|-----------|-----------|-----------|---------|--------|
| `PlanningPermissionCredential` | Planning Officer (Register 1) | Self-Builder | Building Warrant Application (Register 2, Action 1) | 3 years |
| `BuildingWarrantCredential` | Building Standards Officer (Register 2) | Self-Builder | Foundation Inspection (Register 2, Action 5) | 3 years |
| `CompletionCertificateCredential` | Building Inspector (Register 2) | Self-Builder | — (permanent record, no expiry) | None |

---

## Scenarios

### Scenario A: Happy Path — Timber-Frame Bungalow

**Setting:** Rural bungalow at Dalreoch on Loch Morach-side, garden ground plot.

| Attribute | Value |
|-----------|-------|
| Location | Lochside Road, Dalreoch, SC6 3TU |
| Dwelling | Single-storey detached, 120m², 3 bedrooms |
| Construction | Timber-frame, natural larch cladding, slate roof |
| Ground | Gravel, good bearing, deep water table |
| Ecology | Garden ground, no protected species |
| Water/drainage | Mains water, septic tank |
| Heating | Air-source heat pump + solar PV |

**Planning path:** 1→2→3→5→6→7 (skips action 4 — no mitigation needed)
**Warrant path:** 1→2→3→4→5→6→7 (all inspections pass first time)
**Credentials:** 3 issued (Planning Permission, Building Warrant, Completion Certificate)
**Expected calculations:** Risk score=2, Planning fee=£600, Warrant fee=£600, Structural adequacy=1.8, Air-tightness="excellent"

### Scenario B: Protected Species — Two-Storey Woodland Edge

**Setting:** Woodland edge plot near Carronbridge in the Cairngorms National Park. Bat roost and red squirrels discovered.

| Attribute | Value |
|-----------|-------|
| Location | Rothiemurchus, Carronbridge, SC4 1QH |
| Dwelling | Two-storey detached, 200m², 4 bedrooms |
| Construction | Timber-frame, larch cladding, zinc standing-seam roof |
| Ground | Clay, moderate bearing, water table at 1.5m — raft foundation |
| Ecology | Woodland, bats + red squirrels — NatureScot licence required |
| Water/drainage | Mains water (180m), private treatment plant |
| Heating | Ground-source heat pump + solar PV |

**Planning path:** 1→2→3→**4**→5→6→7 (action 4 triggered by protected species)
**Warrant path:** 1→2→3→4→5→6→7 (all inspections pass)
**Credentials:** 3 issued
**Expected calculations:** Risk score=7, Planning fee=£725, Warrant fee=£1,200, Structural adequacy=1.9, Air-tightness="compliant"

### Scenario C: Refused — Modern Design in Conservation Area

**Setting:** Gap site in Invercarron village conservation area. Contemporary flat-roof design clashes with traditional character.

| Attribute | Value |
|-----------|-------|
| Location | Church Street, Invercarron, SC2 8XA |
| Dwelling | Two-storey detached, 150m², 3 bedrooms |
| Construction | ICF (Insulated Concrete Form), white render |
| Ground | Rock, excellent bearing — no issues |
| Ecology | Vacant plot, no concerns |
| Water/drainage | Mains water, public sewer — all fine |
| Problem | Flat-roof contemporary design in 18th-century conservation area |

**Planning path:** 1→2→3→5→6 (refused at planning review — terminal rejection)
**Building warrant:** Never starts (no Planning Permission VC)
**Credentials:** 0 issued
**Expected:** 7 objections, refusal citing Policy 57 and Section 64

---

## Running the Walkthrough

### Prerequisites

- Docker Desktop running with `docker-compose up -d`
- PowerShell 7+ (`pwsh`)

### Setup (Run Once)

```bash
# Generate walkthrough secrets (if not already done)
pwsh walkthroughs/initialize-secrets.ps1

# Bootstrap: create org, 7 wallets, 2 registers, 2 blueprints
pwsh walkthroughs/SelfBuildHouse/setup.ps1 [-Profile gateway|direct|aspire]
```

### Run Scenarios

```bash
# Run all 3 scenarios
pwsh walkthroughs/SelfBuildHouse/run.ps1

# Run specific scenario
pwsh walkthroughs/SelfBuildHouse/run.ps1 -Scenario A

# Show request/response JSON
pwsh walkthroughs/SelfBuildHouse/run.ps1 -ShowJson
```

### Expected Output

```
╔══════════════════════════════════════╗
║   SelfBuildHouse — Results           ║
╚══════════════════════════════════════╝
  [OK] Scenario A: Happy Path — Timber-Frame Bungalow
     Planning: APPROVED (6/6), Warrant: COMPLETED (7/7), 4.2s
  [OK] Scenario B: Protected Species — Two-Storey Woodland Edge
     Planning: APPROVED (7/7), Warrant: COMPLETED (7/7), 5.1s
  [OK] Scenario C: Refused — Modern Design in Conservation Area
     Planning: REFUSED (5/5), Warrant: N/A (-), 1.8s

  Duration: 11.1s

  RESULT: PASS
```

---

## Document Uploads Summary

Each action that accepts document evidence is listed below with the upload fields:

| Action | Blueprint | Uploads |
|--------|-----------|---------|
| 1 — Submit Planning | Planning | Site location plan, floor plans, elevation drawings |
| 2 — Site Investigation | Planning | Ground investigation report, borehole logs |
| 3 — Ecological Survey | Planning | Habitat survey report, site photographs |
| 4 — Species Mitigation | Planning | Species Protection Plan |
| 5 — Utilities Consultation | Planning | Drainage assessment report |
| 6 — Planning Review | Planning | Planning officer report |
| 1 — Submit Warrant | Warrant | Approved drawings, building spec, SAP calcs |
| 2 — Structural Calcs | Warrant | Structural calculations package, foundation design |
| 3 — Standards Review | Warrant | Technical assessment report |
| 5 — Foundation Inspection | Warrant | Inspection photographs |
| 6 — Structure Inspection | Warrant | Inspection photographs, air-tightness certificate |
| 7 — Final Inspection | Warrant | Inspection photos, electrical cert, EPC certificate |

---

## Disclosure Rules

### Planning Blueprint

| Participant | Can See |
|-------------|---------|
| self-builder | Everything they submitted + planning decision/conditions/fees |
| structural-engineer | Site data (address, grid ref, plot size, construction method) |
| ecologist | Site data + contamination status |
| utilities-officer | Everything they submitted |
| planning-officer | Everything (full access as decision maker) |

### Building Warrant Blueprint

| Participant | Can See |
|-------------|---------|
| self-builder | Everything they submitted + review results + inspection outcomes |
| structural-engineer | Site address + construction method + build cost |
| building-standards-officer | Everything (full access as warrant issuer) |
| building-inspector | Everything (full access as inspector) |

---

## File Structure

```
walkthroughs/SelfBuildHouse/
├── README.md                              # This file
├── config.json                            # Walkthrough metadata
├── setup.ps1                              # Bootstrap: org, wallets, registers, blueprints
├── run.ps1                                # Execute scenarios
├── planning-permission-template.json      # Blueprint 1: Planning Permission (7 actions)
├── building-warrant-template.json         # Blueprint 2: Building Warrant (7 actions)
└── data/
    ├── scenario-a-happy-path.json         # Bungalow, no issues, 3 VCs
    ├── scenario-b-protected-species.json  # Woodland, bats + squirrels, 3 VCs
    └── scenario-c-refused.json            # Conservation area, refused, 0 VCs
```

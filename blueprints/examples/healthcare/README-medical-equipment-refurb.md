# Medical Equipment Refurbishment Blueprint

## Overview

A multi-organisation healthcare workflow for medical device refurbishment involving a hospital, a third-party refurbishment company, and a health authority. Demonstrates conditional routing based on device risk classification, JSON Logic calculations, rejection paths, and Refurbishment Certificate Verifiable Credential issuance.

## Organisations

| Organisation | Participants | Role |
|---|---|---|
| City General Hospital | Biomedical Engineer, Department Head | Submits device and approves budget |
| MedTech Refurbishment Ltd | Lead Technician | Quotes, repairs, and certifies |
| Regional Health Authority | Compliance Officer | Regulatory review (safety-critical only) |

## Workflow (5 Actions)

```
[1] Submit Request (biomedical-engineer)
  -> [2] Departmental Approval (department-head) [can reject -> 1]
    -> [3] Quote & Accept (lead-technician) [can reject -> 1]
      -> IF safety-critical: [4] Regulatory Hold (compliance-officer) [can reject -> 1]
      -> [5] Complete & Certify (lead-technician) -> issues Refurbishment Certificate VC
```

## Key Features

- **Conditional Routing:** Device risk category (derived from class + failure type) determines whether regulatory review is required
- **Calculations:** Risk category and estimated cost computed via JSON Logic
- **Rejection Paths:** Three actions can reject back to the originator
- **Verifiable Credential:** Refurbishment Certificate VC issued to the biomedical engineer on completion
- **Selective Disclosure:** Compliance officer sees technical data but not budget; department head sees summary but not technical details
- **Participant Publishing:** All 4 participants are published to the register before blueprint execution (exercises spec 001)

## Risk Category Logic

| Device Class | Failure Type | Risk Category |
|---|---|---|
| Class III | any | safety-critical |
| Class IIa or IIb | safety | safety-critical |
| Class IIa | electrical/mechanical/etc. | routine |
| Class I | any | routine |

## Estimated Cost Formula

```
estimatedCost = max(500, classMultiplier * complexityFactor * 250)

Where classMultiplier:
  Class III  = 4
  Class IIb  = 3
  Class IIa  = 2
  Class I    = 1
```

## Test Scenarios

| Scenario | Device | Path | Actions |
|---|---|---|---|
| A: Routine | Class IIa patient monitor, electrical fault | 1 -> 2 -> 3 -> 5 | 4 |
| B: Safety-Critical | Class III defibrillator, safety failure | 1 -> 2 -> 3 -> 4 -> 5 | 5 |
| C: Rejection | Class IIb monitor, water damage (BER) | 1 -> 2 -> 3 (rejected) | 3 |

## Usage

This blueprint is used in the [MedicalEquipmentRefurb walkthrough](../../walkthroughs/MedicalEquipmentRefurb/). To run:

```powershell
./walkthroughs/MedicalEquipmentRefurb/test-medical-equipment-refurb.ps1
```

## Related

- [simple-patient-referral.json](simple-patient-referral.json) — Simpler healthcare workflow
- [moderate-medical-records-sharing.json](moderate-medical-records-sharing.json) — Medical records with HIPAA compliance

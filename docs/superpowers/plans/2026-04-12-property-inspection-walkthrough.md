# PropertyInspection Walkthrough & Strathcarron Council Universe — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the PropertyInspection walkthrough (7 actions, 4 actors, 3 scenarios) within a shared Strathcarron Council universe, then align ConstructionPermit and SelfBuildHouse to the same universe.

**Architecture:** Shared `walkthroughs/council/setup-council.ps1` creates the common orgs/wallets; each walkthrough's `setup.ps1` calls it then creates its own register and blueprint. Actor-based execution via `sorcha-agent` with rules mode. Blueprint uses file-reference fields for photo evidence and credentialIssuanceConfig for VCs.

**Tech Stack:** PowerShell 7+, Sorcha.Agent CLI, JSON blueprint templates, SorchaWalkthrough shared module

**Design Spec:** `docs/superpowers/specs/2026-04-12-property-inspection-walkthrough-design.md`

---

## File Structure

### New Files

```
walkthroughs/council/
├── README.md                                    # Strathcarron universe narrative
├── setup-council.ps1                            # Shared org/wallet setup (idempotent)

walkthroughs/PropertyInspection/
├── README.md                                    # Walkthrough docs
├── config.json                                  # Metadata
├── property-inspection-template.json            # Blueprint template (7 actions)
├── setup.ps1                                    # Register + blueprint + persona setup
├── run-agents.ps1                               # Actor launcher
├── actors/
│   ├── tenant.json                              # Council tenant (citizen)
│   ├── housing-officer.json                     # Strathcarron Council
│   ├── contractor.json                          # Stoniebridge Construction
│   └── building-inspector.json                  # Strathcarron Council
└── data/
    ├── scenario-a-routine.json                  # Happy path
    ├── scenario-b-emergency.json                # Rework + inspector
    └── scenario-c-verification-failure.json     # Operative rejection
```

### Modified Files

```
walkthroughs/.secrets/passwords.json             # Add property-inspection + council entries
walkthroughs/ConstructionPermit/                 # ~11 files: org/place name alignment
walkthroughs/SelfBuildHouse/                     # ~15 files: org/place name alignment + org merge
```

---

## Task 1: Add Secrets Entries

**Files:**
- Modify: `walkthroughs/.secrets/passwords.json`

- [ ] **Step 1: Read current passwords.json structure**

```bash
cat walkthroughs/.secrets/passwords.json | head -30
```

Understand the existing key naming pattern.

- [ ] **Step 2: Add council and property-inspection entries**

Add to `passwords.json`:

```json
"council": {
  "sysAdminEmail": "admin@sorcha.local",
  "sysAdminPassword": "Dev_Pass_2025!",
  "sysAdminName": "System Administrator",
  "housingOfficerEmail": "housing@strathcarron.local",
  "housingOfficerPassword": "Dev_Pass_2025!",
  "housingOfficerName": "Housing Officer",
  "planningOfficerEmail": "planning@strathcarron.local",
  "planningOfficerPassword": "Dev_Pass_2025!",
  "planningOfficerName": "Planning Officer",
  "buildingStandardsEmail": "building-standards@strathcarron.local",
  "buildingStandardsPassword": "Dev_Pass_2025!",
  "buildingStandardsName": "Building Standards Officer",
  "buildingInspectorEmail": "inspector@strathcarron.local",
  "buildingInspectorPassword": "Dev_Pass_2025!",
  "buildingInspectorName": "Building Inspector",
  "buildingControlEmail": "building-control@strathcarron.local",
  "buildingControlPassword": "Dev_Pass_2025!",
  "buildingControlName": "Building Control Inspector",
  "contractorEmail": "contractor@stoniebridge.local",
  "contractorPassword": "Dev_Pass_2025!",
  "contractorName": "Site Operative",
  "structuralEmail": "engineer@murchison.local",
  "structuralPassword": "Dev_Pass_2025!",
  "structuralName": "Lead Engineer",
  "ecologistEmail": "ecologist@heatherbank.local",
  "ecologistPassword": "Dev_Pass_2025!",
  "ecologistName": "Senior Ecologist",
  "utilitiesEmail": "utilities@caledonian-water.local",
  "utilitiesPassword": "Dev_Pass_2025!",
  "utilitiesName": "Connections Officer"
},
"property-inspection": {
  "tenantAEmail": "flora.macinnes@public.local",
  "tenantAPassword": "Dev_Pass_2025!",
  "tenantAName": "Flora MacInnes",
  "tenantBEmail": "angus.beaton@public.local",
  "tenantBPassword": "Dev_Pass_2025!",
  "tenantBName": "Angus Beaton",
  "tenantCEmail": "eilidh.drummond@public.local",
  "tenantCPassword": "Dev_Pass_2025!",
  "tenantCName": "Eilidh Drummond"
}
```

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/.secrets/passwords.json
git commit -m "chore: add council + property-inspection secrets entries"
```

---

## Task 2: Create Shared Council Setup

**Files:**
- Create: `walkthroughs/council/setup-council.ps1`
- Create: `walkthroughs/council/README.md`

- [ ] **Step 1: Create council/README.md**

```markdown
# Strathcarron Council Demo Universe

A shared fictional Scottish council area used across all council-related walkthroughs.
No real council names, utility companies, or identifiable organisations are used.

## Geography

| Place | Type | Postcode Prefix |
|-------|------|-----------------|
| Strathcarron | Council area | SC |
| Carronbridge | Main town (council HQ) | SC4 |
| Dalreoch | Rural village | SC6 |
| Invercarron | Conservation village | SC2 |
| Loch Morach | Scenic loch | — |

## Organisations

| Org | Subdomain | Roles |
|-----|-----------|-------|
| Strathcarron Council | strathcarron | planning-officer, building-standards-officer, building-inspector, building-control, housing-officer |
| Stoniebridge Construction | stoniebridge | contractor |
| Murchison Engineering | murchison | structural-engineer |
| Heatherbank Environmental | heatherbank | ecologist, environmental-assessor |
| Caledonian Water | caledonian-water | utilities-officer |

## Usage

Each walkthrough calls `setup-council.ps1` before its own setup:

```powershell
$councilState = & (Join-Path $PSScriptRoot ".." "council" "setup-council.ps1") -Profile $Profile
```

The script is idempotent — safe to call multiple times or from different walkthroughs.

## Walkthroughs Using This Universe

- **ConstructionPermit** — 4-org construction permit approval
- **SelfBuildHouse** — 6-org self-build with planning + building standards
- **PropertyInspection** — Council property services with photo evidence
```

- [ ] **Step 2: Create council/setup-council.ps1**

This script creates the shared orgs (Strathcarron Council, Stoniebridge Construction, Murchison Engineering, Heatherbank Environmental, Caledonian Water), their admin users, wallets, and participants. It outputs a state object that walkthrough-specific setup scripts consume.

```powershell
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Shared council universe setup — creates orgs, admin users, and wallets
# for the Strathcarron Council demo universe. Idempotent.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$Force,
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$stateFile = Join-Path $scriptDir "council-state.json"

# ── Module ────────────────────────────────────────────────────────
$modulePath = Join-Path $scriptDir ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

# ── Environment ───────────────────────────────────────────────────
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck
$secrets = Get-SorchaSecrets -WalkthroughName "council"

Write-WtBanner "Strathcarron Council Universe Setup"

# ── Idempotency: return cached state if valid ─────────────────────
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "Council state file exists, validating..."
    $existing = Get-Content $stateFile -Raw | ConvertFrom-Json
    try {
        $sysAdmin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
            -AdminEmail $secrets.sysAdminEmail -AdminPassword $secrets.sysAdminPassword
        Write-WtSuccess "Council state valid — reusing"
        return $existing
    } catch {
        Write-WtWarn "Council state invalid, recreating..."
    }
}

# ── System admin login ────────────────────────────────────────────
$sysAdmin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.sysAdminEmail -AdminPassword $secrets.sysAdminPassword

# ── Enable public org ─────────────────────────────────────────────
Invoke-SorchaApi -Method PUT `
    -Uri "$($sorchaEnv.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } -Headers $sysAdmin.Headers | Out-Null

# ── Organisation definitions ──────────────────────────────────────
$orgDefs = @(
    @{ name = "Strathcarron Council"; subdomain = "strathcarron"; desc = "Local authority — planning, building standards, housing" }
    @{ name = "Stoniebridge Construction"; subdomain = "stoniebridge"; desc = "General contractor" }
    @{ name = "Murchison Engineering"; subdomain = "murchison"; desc = "Structural engineering consultancy" }
    @{ name = "Heatherbank Environmental"; subdomain = "heatherbank"; desc = "Ecology and environmental consultancy" }
    @{ name = "Caledonian Water"; subdomain = "caledonian-water"; desc = "Water and drainage utility" }
)

# ── User definitions (org admins) ─────────────────────────────────
# Each org admin is first registered on the public org, then assigned as admin of their private org.
$orgAdminDefs = @(
    @{ role = "planning-officer"; orgSubdomain = "strathcarron"; email = $secrets.planningOfficerEmail; password = $secrets.planningOfficerPassword; name = $secrets.planningOfficerName }
    @{ role = "contractor"; orgSubdomain = "stoniebridge"; email = $secrets.contractorEmail; password = $secrets.contractorPassword; name = $secrets.contractorName }
    @{ role = "structural-engineer"; orgSubdomain = "murchison"; email = $secrets.structuralEmail; password = $secrets.structuralPassword; name = $secrets.structuralName }
    @{ role = "ecologist"; orgSubdomain = "heatherbank"; email = $secrets.ecologistEmail; password = $secrets.ecologistPassword; name = $secrets.ecologistName }
    @{ role = "utilities-officer"; orgSubdomain = "caledonian-water"; email = $secrets.utilitiesEmail; password = $secrets.utilitiesPassword; name = $secrets.utilitiesName }
)

# Team members (non-admin users added to existing orgs)
$teamMemberDefs = @(
    @{ role = "building-standards-officer"; orgSubdomain = "strathcarron"; email = $secrets.buildingStandardsEmail; password = $secrets.buildingStandardsPassword; name = $secrets.buildingStandardsName }
    @{ role = "building-inspector"; orgSubdomain = "strathcarron"; email = $secrets.buildingInspectorEmail; password = $secrets.buildingInspectorPassword; name = $secrets.buildingInspectorName }
    @{ role = "building-control"; orgSubdomain = "strathcarron"; email = $secrets.buildingControlEmail; password = $secrets.buildingControlPassword; name = $secrets.buildingControlName }
    @{ role = "housing-officer"; orgSubdomain = "strathcarron"; email = $secrets.housingOfficerEmail; password = $secrets.housingOfficerPassword; name = $secrets.housingOfficerName }
)

$publicOrgId = "00000000-0000-0000-0000-000000000002"

# ── Step 1: Register all users on public org ──────────────────────
Write-WtStep "Registering users on public org"
$allUsers = $orgAdminDefs + $teamMemberDefs
foreach ($u in $allUsers) {
    Register-SorchaPublicUser -TenantUrl $sorchaEnv.TenantUrl `
        -Email $u.email -Password $u.password -DisplayName $u.name | Out-Null
}

# ── Step 2: Verify emails ────────────────────────────────────────
Write-WtStep "Verifying user emails"
$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true&pageSize=100" `
    -Headers $sysAdmin.Headers
foreach ($u in $allUsers) {
    $pu = $publicUsers.users | Where-Object { $_.email -eq $u.email } | Select-Object -First 1
    if ($pu) {
        Confirm-SorchaUserEmail -TenantUrl $sorchaEnv.TenantUrl `
            -OrganizationId $publicOrgId -UserId $pu.id -Headers $sysAdmin.Headers | Out-Null
    }
}

# ── Step 3: Create private orgs ──────────────────────────────────
Write-WtStep "Creating organisations"
$orgs = @{}
foreach ($def in $orgDefs) {
    $adminUser = $orgAdminDefs | Where-Object { $_.orgSubdomain -eq $def.subdomain } | Select-Object -First 1
    $result = New-SorchaOrganization -TenantUrl $sorchaEnv.TenantUrl `
        -Name $def.name -Subdomain $def.subdomain `
        -AdminEmail $adminUser.email -Headers $sysAdmin.Headers -Description $def.desc
    $orgs[$def.subdomain] = $result.OrganizationId
    Write-WtInfo "  $($def.name) → $($result.OrganizationId)"
}

# ── Step 4: Add team members to Strathcarron Council ──────────────
Write-WtStep "Adding team members to Strathcarron Council"
$strathcarronId = $orgs["strathcarron"]
foreach ($u in $teamMemberDefs) {
    Get-OrCreateUser -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $strathcarronId `
        -Email $u.email -DisplayName $u.name `
        -Headers $sysAdmin.Headers -Roles @("Administrator", "Consumer") | Out-Null
    Write-WtInfo "  $($u.name) added to Strathcarron Council"
}

# ── Step 5: Login as each user, create wallets, register participants ─
Write-WtStep "Creating wallets and registering participants"
$sessionCache = @{}
$roles = @{}

foreach ($u in $allUsers) {
    $orgId = $orgs[$u.orgSubdomain]
    $cacheKey = "$($u.email)|$orgId"

    # Login
    if (-not $sessionCache.ContainsKey($cacheKey)) {
        $session = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
            -Email $u.email -Password $u.password -OrganizationId $orgId
        $sessionCache[$cacheKey] = $session
    }
    $session = $sessionCache[$cacheKey]

    # Wallet
    $wallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl `
        -Name "$($u.name) Wallet" -Headers $session.Headers -FetchPublicKey

    # Participant
    $participant = Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl `
        -WalletUrl $sorchaEnv.WalletUrl -OrganizationId $orgId `
        -WalletAddress $wallet.Address -DisplayName $u.name -Headers $session.Headers

    $roles[$u.role] = @{
        email          = $u.email
        password       = $u.password
        name           = $u.name
        organizationId = $orgId
        walletAddress  = $wallet.Address
        publicKey      = $participant.PublicKey ?? $wallet.PublicKey
        participantId  = $participant.ParticipantId
        orgSubdomain   = $u.orgSubdomain
    }
    Write-WtInfo "  $($u.role) → $($wallet.Address)"
}

# ── Build state ───────────────────────────────────────────────────
$councilState = @{
    profile       = $Profile
    organizations = $orgs
    roles         = $roles
    sysAdmin      = @{
        email    = $secrets.sysAdminEmail
        password = $secrets.sysAdminPassword
    }
    environment   = @{
        gatewayUrl   = $sorchaEnv.GatewayUrl
        tenantUrl    = $sorchaEnv.TenantUrl
        blueprintUrl = $sorchaEnv.BlueprintUrl
        registerUrl  = $sorchaEnv.RegisterUrl
        walletUrl    = $sorchaEnv.WalletUrl
    }
}

$councilState | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8
Write-WtSuccess "Council state saved to $stateFile"

return $councilState
```

- [ ] **Step 3: Verify script syntax**

```bash
pwsh -NoExecute -File walkthroughs/council/setup-council.ps1
```

Expected: No syntax errors.

- [ ] **Step 4: Commit**

```bash
git add walkthroughs/council/
git commit -m "feat: add shared Strathcarron Council universe setup"
```

---

## Task 3: Create Blueprint Template

**Files:**
- Create: `walkthroughs/PropertyInspection/property-inspection-template.json`

- [ ] **Step 1: Create the blueprint template**

This is the core blueprint with 7 actions, 4 participants, conditional routing, cyclic rework, 2 file-reference fields, and 2 VCs. The template follows the exact pattern from ConstructionPermit/SelfBuildHouse templates.

```json
{
  "id": "property-inspection-v1",
  "title": "Council Property Inspection",
  "description": "Council property services workflow — tenant reports problem with photo evidence, council triages and allocates contractor, tenant verifies operative identity, contractor completes work with photo evidence, council signs off.",
  "version": 1,
  "category": "government",
  "tags": ["council", "property", "inspection", "photo-evidence", "safeguarding", "strathcarron"],
  "author": "Sorcha Team",
  "published": true,
  "template": {
    "id": "property-inspection-generated",
    "title": "Council Property Inspection",
    "version": 1,
    "metadata": {
      "category": "Council Services",
      "complexity": "Complex",
      "actions": "7",
      "features": "Multi-Org, File Upload, Conditional Routing, Cycles, Verifiable Credentials, Persona Autofill",
      "sector": "Local Government",
      "hasCycles": "true"
    },
    "instanceReference": {
      "prefix": "PI",
      "components": [
        { "field": "/category", "transform": "Truncate", "chars": 4 },
        { "field": "/propertyAddress/town", "transform": "FirstWord", "chars": 3 }
      ]
    },
    "participants": [
      {
        "id": "tenant",
        "name": "Council Tenant",
        "organisation": "Public",
        "description": "Reports problem with photo evidence, verifies operative identity, confirms satisfaction",
        "walletAddress": "addr_patched_by_publish_blueprint"
      },
      {
        "id": "housing-officer",
        "name": "Housing Officer",
        "organisation": "Strathcarron Council",
        "description": "Triages requests, sets requirements checklist, allocates contractor, reviews completion",
        "walletAddress": "addr_patched_by_publish_blueprint"
      },
      {
        "id": "contractor",
        "name": "Site Operative",
        "organisation": "Stoniebridge Construction",
        "description": "Completes repair work, submits delivery note with photo evidence of completed work",
        "walletAddress": "addr_patched_by_publish_blueprint"
      },
      {
        "id": "building-inspector",
        "name": "Building Inspector",
        "organisation": "Strathcarron Council",
        "description": "Signs off structural/safety work for Emergency severity jobs",
        "walletAddress": "addr_patched_by_publish_blueprint"
      }
    ],
    "actions": [
      {
        "id": 0,
        "title": "Report Problem",
        "description": "Tenant reports a property issue with description, category, and photo evidence of damage",
        "sender": "tenant",
        "isStartingAction": true,
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "tenantName": {
                "type": "string",
                "title": "Full Name",
                "minLength": 2,
                "maxLength": 100,
                "x-persona": "fullName"
              },
              "contactPhone": {
                "type": "string",
                "format": "tel",
                "title": "Contact Phone",
                "x-persona": "defaultPhone"
              },
              "propertyAddress": {
                "type": "object",
                "title": "Property Address",
                "x-persona": "defaultAddress",
                "properties": {
                  "street": { "type": "string", "title": "Street", "minLength": 3 },
                  "town": { "type": "string", "title": "Town", "minLength": 2 },
                  "postcode": { "type": "string", "title": "Postcode", "pattern": "^SC[0-9] [0-9][A-Z]{2}$" }
                },
                "required": ["street", "town", "postcode"]
              },
              "description": {
                "type": "string",
                "title": "Problem Description",
                "minLength": 10,
                "maxLength": 2000
              },
              "category": {
                "type": "string",
                "title": "Category",
                "enum": ["Plumbing", "Electrical", "Structural", "Roofing", "Other"]
              },
              "urgencyDescription": {
                "type": "string",
                "title": "Urgency Notes",
                "maxLength": 500
              },
              "accessNotes": {
                "type": "string",
                "title": "Access Notes",
                "description": "Key safe location, neighbour contact, availability",
                "maxLength": 500
              },
              "damagePhoto": {
                "type": "string",
                "format": "file-reference",
                "title": "Photo of Damage",
                "x-file": {
                  "accept": ["image/jpeg", "image/png"],
                  "maxSizePerFile": "16MB",
                  "maxChunks": 10
                }
              }
            },
            "required": ["tenantName", "contactPhone", "propertyAddress", "description", "category", "damagePhoto"]
          }
        ],
        "disclosures": [
          { "participantAddress": "tenant", "dataPointers": ["/*"] },
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] }
        ],
        "routes": [
          {
            "id": "to-triage",
            "nextActionIds": [1],
            "isDefault": true,
            "description": "Route to Housing Officer for triage"
          }
        ]
      },
      {
        "id": 1,
        "title": "Triage & Allocate",
        "description": "Housing Officer assesses severity, creates requirements checklist, and allocates contractor",
        "sender": "housing-officer",
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "severity": {
                "type": "string",
                "title": "Severity",
                "enum": ["Routine", "Urgent", "Emergency"]
              },
              "targetCompletionDate": {
                "type": "string",
                "format": "date",
                "title": "Target Completion Date"
              },
              "requirements": {
                "type": "array",
                "title": "Requirements Checklist",
                "items": { "type": "string", "minLength": 3 },
                "minItems": 1,
                "maxItems": 20
              },
              "allocationNotes": {
                "type": "string",
                "title": "Allocation Notes",
                "maxLength": 1000
              },
              "assignedOperativeName": {
                "type": "string",
                "title": "Assigned Operative",
                "minLength": 2
              }
            },
            "required": ["severity", "targetCompletionDate", "requirements", "assignedOperativeName"]
          }
        ],
        "credentialIssuanceConfig": {
          "credentialType": "JobAssignmentCredential",
          "recipientParticipantId": "contractor",
          "expiryDuration": "P30D",
          "claimMappings": [
            { "claimName": "jobReference", "sourceField": "/instanceReference" },
            { "claimName": "propertyAddress", "sourceField": "/propertyAddress" },
            { "claimName": "tenantName", "sourceField": "/tenantName" },
            { "claimName": "assignedOperativeName", "sourceField": "/assignedOperativeName" },
            { "claimName": "severity", "sourceField": "/severity" },
            { "claimName": "scopeSummary", "sourceField": "/requirements" }
          ],
          "disclosable": [
            "jobReference",
            "assignedOperativeName",
            "severity"
          ]
        },
        "disclosures": [
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] },
          { "participantAddress": "tenant", "dataPointers": ["/severity", "/targetCompletionDate", "/assignedOperativeName"] },
          { "participantAddress": "contractor", "dataPointers": ["/*"] }
        ],
        "routes": [
          {
            "id": "to-verify",
            "nextActionIds": [2],
            "isDefault": true,
            "description": "Route to tenant for operative verification"
          }
        ]
      },
      {
        "id": 2,
        "title": "Verify Operative",
        "description": "Tenant verifies the contractor's identity using the JobAssignmentCredential before granting access",
        "sender": "tenant",
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "operativeVerified": {
                "type": "boolean",
                "title": "Operative Identity Verified"
              },
              "verificationNotes": {
                "type": "string",
                "title": "Verification Notes",
                "maxLength": 500
              }
            },
            "required": ["operativeVerified"]
          }
        ],
        "disclosures": [
          { "participantAddress": "tenant", "dataPointers": ["/*"] },
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] },
          { "participantAddress": "contractor", "dataPointers": ["/operativeVerified"] }
        ],
        "routes": [
          {
            "id": "verified-proceed",
            "nextActionIds": [3],
            "condition": { "==": [{ "var": "operativeVerified" }, true] },
            "description": "Operative verified — proceed to work"
          },
          {
            "id": "rejected-reallocate",
            "nextActionIds": [1],
            "condition": { "==": [{ "var": "operativeVerified" }, false] },
            "description": "Verification failed — return to Housing Officer for re-allocation"
          }
        ]
      },
      {
        "id": 3,
        "title": "Complete Work",
        "description": "Contractor completes repair, ticks off checklist items, and submits photo evidence of completed work",
        "sender": "contractor",
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "completedItems": {
                "type": "array",
                "title": "Completed Checklist Items",
                "items": { "type": "string", "minLength": 3 },
                "minItems": 1
              },
              "materialsUsed": {
                "type": "string",
                "title": "Materials Used",
                "maxLength": 1000
              },
              "hoursWorked": {
                "type": "number",
                "title": "Hours Worked",
                "minimum": 0.5,
                "maximum": 100
              },
              "completionPhoto": {
                "type": "string",
                "format": "file-reference",
                "title": "Photo of Completed Work",
                "x-file": {
                  "accept": ["image/jpeg", "image/png"],
                  "maxSizePerFile": "16MB",
                  "maxChunks": 10
                }
              },
              "deliveryNotes": {
                "type": "string",
                "title": "Delivery Notes",
                "minLength": 5,
                "maxLength": 2000
              }
            },
            "required": ["completedItems", "hoursWorked", "completionPhoto", "deliveryNotes"]
          }
        ],
        "disclosures": [
          { "participantAddress": "contractor", "dataPointers": ["/*"] },
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] },
          { "participantAddress": "building-inspector", "dataPointers": ["/*"] }
        ],
        "routes": [
          {
            "id": "to-review",
            "nextActionIds": [4],
            "isDefault": true,
            "description": "Route to Housing Officer for completion review"
          }
        ]
      },
      {
        "id": 4,
        "title": "Review Completion",
        "description": "Housing Officer reviews the contractor's work, photos, and checklist completion",
        "sender": "housing-officer",
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "decision": {
                "type": "string",
                "title": "Decision",
                "enum": ["Accepted", "Rework"]
              },
              "reviewNotes": {
                "type": "string",
                "title": "Review Notes",
                "maxLength": 2000
              },
              "reworkInstructions": {
                "type": "string",
                "title": "Rework Instructions",
                "maxLength": 2000
              }
            },
            "required": ["decision", "reviewNotes"]
          }
        ],
        "disclosures": [
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] },
          { "participantAddress": "contractor", "dataPointers": ["/decision", "/reworkInstructions"] },
          { "participantAddress": "tenant", "dataPointers": ["/decision"] },
          { "participantAddress": "building-inspector", "dataPointers": ["/*"] }
        ],
        "routes": [
          {
            "id": "rework",
            "nextActionIds": [3],
            "condition": { "==": [{ "var": "decision" }, "Rework"] },
            "description": "Work incomplete — return to contractor"
          },
          {
            "id": "accepted-emergency",
            "nextActionIds": [5],
            "condition": {
              "and": [
                { "==": [{ "var": "decision" }, "Accepted"] },
                { "==": [{ "var": "severity" }, "Emergency"] }
              ]
            },
            "description": "Emergency job accepted — route to Building Inspector for safety sign-off"
          },
          {
            "id": "accepted-standard",
            "nextActionIds": [6],
            "isDefault": true,
            "description": "Non-emergency job accepted — route to tenant for satisfaction"
          }
        ]
      },
      {
        "id": 5,
        "title": "Safety Sign-Off",
        "description": "Building Inspector verifies structural, electrical, and gas safety for Emergency severity jobs",
        "sender": "building-inspector",
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "structuralSafe": {
                "type": "boolean",
                "title": "Structural Integrity Safe"
              },
              "electricalSafe": {
                "type": "boolean",
                "title": "Electrical Safety Confirmed"
              },
              "gasSafe": {
                "type": "boolean",
                "title": "Gas Safety Confirmed"
              },
              "complianceNotes": {
                "type": "string",
                "title": "Compliance Notes",
                "maxLength": 2000
              },
              "signOffDecision": {
                "type": "string",
                "title": "Sign-Off Decision",
                "enum": ["Pass", "Fail"]
              }
            },
            "required": ["structuralSafe", "electricalSafe", "signOffDecision"]
          }
        ],
        "disclosures": [
          { "participantAddress": "building-inspector", "dataPointers": ["/*"] },
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] },
          { "participantAddress": "tenant", "dataPointers": ["/signOffDecision"] },
          { "participantAddress": "contractor", "dataPointers": ["/signOffDecision", "/complianceNotes"] }
        ],
        "routes": [
          {
            "id": "safety-pass",
            "nextActionIds": [6],
            "condition": { "==": [{ "var": "signOffDecision" }, "Pass"] },
            "description": "Safety checks passed — proceed to tenant satisfaction"
          },
          {
            "id": "safety-fail",
            "nextActionIds": [3],
            "condition": { "==": [{ "var": "signOffDecision" }, "Fail"] },
            "description": "Safety checks failed — return to contractor"
          }
        ]
      },
      {
        "id": 6,
        "title": "Confirm Satisfaction",
        "description": "Tenant confirms satisfaction with the completed repair work",
        "sender": "tenant",
        "dataSchemas": [
          {
            "type": "object",
            "properties": {
              "satisfied": {
                "type": "boolean",
                "title": "Satisfied with Repair"
              },
              "feedbackNotes": {
                "type": "string",
                "title": "Feedback",
                "maxLength": 1000
              },
              "rating": {
                "type": "integer",
                "title": "Rating (1-5)",
                "minimum": 1,
                "maximum": 5
              }
            },
            "required": ["satisfied", "rating"]
          }
        ],
        "credentialIssuanceConfig": {
          "credentialType": "ServiceCompletionCredential",
          "recipientParticipantId": "tenant",
          "expiryDuration": "P3650D",
          "claimMappings": [
            { "claimName": "jobReference", "sourceField": "/instanceReference" },
            { "claimName": "propertyAddress", "sourceField": "/propertyAddress" },
            { "claimName": "category", "sourceField": "/category" },
            { "claimName": "completionDate", "sourceField": "/completionDate" },
            { "claimName": "satisfactionRating", "sourceField": "/rating" },
            { "claimName": "contractorName", "sourceField": "/assignedOperativeName" }
          ],
          "disclosable": [
            "jobReference",
            "propertyAddress",
            "completionDate",
            "satisfactionRating"
          ]
        },
        "disclosures": [
          { "participantAddress": "tenant", "dataPointers": ["/*"] },
          { "participantAddress": "housing-officer", "dataPointers": ["/*"] },
          { "participantAddress": "contractor", "dataPointers": ["/satisfied", "/rating", "/feedbackNotes"] }
        ],
        "routes": []
      }
    ]
  },
  "parameterSchema": null,
  "defaultParameters": null,
  "examples": []
}
```

- [ ] **Step 2: Validate JSON syntax**

```bash
pwsh -Command "Get-Content walkthroughs/PropertyInspection/property-inspection-template.json | ConvertFrom-Json | Out-Null; Write-Host 'Valid JSON'"
```

Expected: "Valid JSON"

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/PropertyInspection/property-inspection-template.json
git commit -m "feat: add PropertyInspection blueprint template (7 actions, 4 participants)"
```

---

## Task 4: Create Scenario Data Files

**Files:**
- Create: `walkthroughs/PropertyInspection/data/scenario-a-routine.json`
- Create: `walkthroughs/PropertyInspection/data/scenario-b-emergency.json`
- Create: `walkthroughs/PropertyInspection/data/scenario-c-verification-failure.json`

- [ ] **Step 1: Create scenario-a-routine.json**

```json
{
  "name": "Scenario A: Routine Plumbing Repair",
  "description": "Happy path — leaking tap, routine severity, accepted first time",
  "path": "0 → 1 → 2 → 3 → 4 → 6",
  "actions": {
    "reportProblem": {
      "tenantName": "Flora MacInnes",
      "contactPhone": "+44 1463 555 201",
      "propertyAddress": {
        "street": "14 Moray Crescent",
        "town": "Carronbridge",
        "postcode": "SC4 2TL"
      },
      "description": "Kitchen tap is leaking continuously. Water has caused damage to the chipboard cabinet underneath the sink. The cabinet door is swollen and won't close properly.",
      "category": "Plumbing",
      "urgencyDescription": "Not an emergency but getting worse — bucket filling up twice a day",
      "accessNotes": "Key safe at side gate, code shared with housing office. Available weekdays 9am-5pm."
    },
    "triageAllocate": {
      "severity": "Routine",
      "targetCompletionDate": "2026-04-25",
      "requirements": [
        "Replace tap unit (mixer tap, kitchen spec)",
        "Check isolation valve under sink",
        "Inspect for further water damage to adjacent units",
        "Replace damaged cabinet panel if beyond repair"
      ],
      "allocationNotes": "Standard plumbing job. Tenant has key safe access so no need for appointment.",
      "assignedOperativeName": "Jamie Crawford"
    },
    "verifyOperative": {
      "operativeVerified": true,
      "verificationNotes": "Credential matches — Jamie Crawford, Stoniebridge Construction"
    },
    "completeWork": {
      "completedItems": [
        "Replaced tap unit — fitted Bristan Quest mono mixer",
        "Checked isolation valve — functioning correctly, no leak",
        "Inspected adjacent cabinets — no further water damage",
        "Replaced damaged cabinet panel with matching unit"
      ],
      "materialsUsed": "Bristan Quest mono mixer tap, 15mm flexi connectors x2, cabinet panel (600mm white melamine)",
      "hoursWorked": 3.0,
      "deliveryNotes": "Tap replaced and tested with no leaks. Cabinet panel replaced. All waste removed from property."
    },
    "reviewCompletion": {
      "decision": "Accepted",
      "reviewNotes": "All checklist items completed. Photos show clean installation. No further action required."
    },
    "confirmSatisfaction": {
      "satisfied": true,
      "feedbackNotes": "Very tidy job, thank you. Kitchen is back to normal.",
      "rating": 5
    }
  }
}
```

- [ ] **Step 2: Create scenario-b-emergency.json**

```json
{
  "name": "Scenario B: Emergency Ceiling Collapse (Rework + Inspector)",
  "description": "Emergency severity — ceiling collapse, first attempt missing electrical check, rework, then inspector sign-off",
  "path": "0 → 1 → 2 → 3 → 4(rework) → 3 → 4(accepted) → 5 → 6",
  "actions": {
    "reportProblem": {
      "tenantName": "Angus Beaton",
      "contactPhone": "+44 1463 555 302",
      "propertyAddress": {
        "street": "7 Loch Morach Drive",
        "town": "Dalreoch",
        "postcode": "SC6 8JN"
      },
      "description": "Bedroom ceiling has partially collapsed following heavy rain. Large section of plaster has fallen onto the bed. Water staining visible on exposed joists. Concerned about structural integrity of remaining ceiling and possible electrical risk from ceiling light fitting.",
      "category": "Structural",
      "urgencyDescription": "Emergency — ceiling still dropping plaster, cannot use bedroom, worried about further collapse",
      "accessNotes": "Will be home all day. Bedroom door is closed to contain debris. Please call on arrival."
    },
    "triageAllocate": {
      "severity": "Emergency",
      "targetCompletionDate": "2026-04-13",
      "requirements": [
        "Make safe — prop and shore remaining ceiling",
        "Remove fallen debris safely",
        "Inspect roof above for source of water ingress",
        "Repair roof leak",
        "Replace ceiling plasterboard and skim",
        "Redecorate ceiling and affected walls",
        "Electrical check on ceiling light fitting and wiring"
      ],
      "allocationNotes": "Emergency response. Ceiling collapse with potential structural and electrical risk. Send experienced team.",
      "assignedOperativeName": "Rory MacPherson"
    },
    "verifyOperative": {
      "operativeVerified": true,
      "verificationNotes": "Credential confirmed — Rory MacPherson, Stoniebridge Construction, Emergency job"
    },
    "completeWorkFirstAttempt": {
      "completedItems": [
        "Made safe — Acrow props installed under remaining ceiling section",
        "Removed all fallen plaster and debris, bagged and removed from property",
        "Inspected roof — found cracked ridge tile allowing water ingress",
        "Replaced cracked ridge tile and repointed surrounding tiles",
        "Replaced ceiling plasterboard (2 sheets 2400x1200) and skimmed",
        "Redecorated ceiling with mist coat and two coats white emulsion"
      ],
      "materialsUsed": "Acrow props x2, plasterboard 2400x1200 x2, multi-finish plaster, ridge tile, roofing cement, white emulsion 5L",
      "hoursWorked": 14.0,
      "deliveryNotes": "Roof repaired and ceiling replaced. Room cleaned and usable. Note: electrical check of ceiling fitting still to be completed — waiting for qualified electrician availability."
    },
    "reviewCompletionRework": {
      "decision": "Rework",
      "reviewNotes": "Six of seven checklist items completed. Roof and ceiling repair looks good in photos.",
      "reworkInstructions": "Electrical check on ceiling light fitting not evidenced. This is a safety requirement for Emergency jobs. Please arrange qualified electrician and submit certificate."
    },
    "completeWorkRetry": {
      "completedItems": [
        "Made safe — completed (previous visit)",
        "Removed debris — completed (previous visit)",
        "Inspected roof — completed (previous visit)",
        "Repaired roof leak — completed (previous visit)",
        "Replaced ceiling plasterboard — completed (previous visit)",
        "Redecorated — completed (previous visit)",
        "Electrical check — NICEIC-registered electrician tested ceiling rose, pendant, and cable run. All satisfactory. Certificate ref: NICEIC-2026-SC-008734"
      ],
      "materialsUsed": "No additional materials (electrical inspection only)",
      "hoursWorked": 1.5,
      "deliveryNotes": "Electrical inspection completed by qualified NICEIC electrician. Ceiling light fitting, wiring, and connection all tested satisfactory. Certificate provided."
    },
    "reviewCompletionAccepted": {
      "decision": "Accepted",
      "reviewNotes": "All seven checklist items now completed including electrical certificate. Forwarding to Building Inspector for Emergency safety sign-off."
    },
    "safetySignOff": {
      "structuralSafe": true,
      "electricalSafe": true,
      "gasSafe": true,
      "complianceNotes": "Ceiling repair structurally sound — plasterboard properly fixed to joists. Roof repair adequate. NICEIC electrical certificate confirms safe installation. No gas appliances affected. Property safe for habitation.",
      "signOffDecision": "Pass"
    },
    "confirmSatisfaction": {
      "satisfied": true,
      "feedbackNotes": "Took two visits but the repair is solid. Bedroom ceiling looks like new. Glad the electrical was checked properly.",
      "rating": 4
    }
  }
}
```

- [ ] **Step 3: Create scenario-c-verification-failure.json**

```json
{
  "name": "Scenario C: Operative Verification Failure (Re-allocation)",
  "description": "Urgent lock repair — tenant rejects first operative (credential mismatch), re-allocated, then completed",
  "path": "0 → 1 → 2(rejected) → 1 → 2(accepted) → 3 → 4 → 6",
  "actions": {
    "reportProblem": {
      "tenantName": "Eilidh Drummond",
      "contactPhone": "+44 1463 555 403",
      "propertyAddress": {
        "street": "3 Invercarron Row",
        "town": "Invercarron",
        "postcode": "SC2 5PA"
      },
      "description": "Front door lock mechanism has broken. The handle is hanging loose and the lock cylinder is visible. Door will not secure properly — can be pushed open from outside. I live alone and feel very unsafe.",
      "category": "Other",
      "urgencyDescription": "Urgent — door cannot be locked, I am a vulnerable adult living alone",
      "accessNotes": "I will not open the door unless I can verify who is visiting. Please ensure operative has proper identification. Neighbour at number 5 (Mrs Campbell) has my spare key for emergencies."
    },
    "triageAllocateFirst": {
      "severity": "Urgent",
      "targetCompletionDate": "2026-04-14",
      "requirements": [
        "Replace lock mechanism (5-lever mortice deadlock BS3621)",
        "Test lock operation from both sides",
        "Provide tenant with new keys (x2)",
        "Check door frame alignment — repair if needed"
      ],
      "allocationNotes": "Vulnerable adult, lives alone. Must verify operative identity before entry. Priority job.",
      "assignedOperativeName": "Craig Dunbar"
    },
    "verifyOperativeRejected": {
      "operativeVerified": false,
      "verificationNotes": "Person at door does not match credential. Name on credential says Craig Dunbar but person gave different name. Refusing access."
    },
    "triageAllocateSecond": {
      "severity": "Urgent",
      "targetCompletionDate": "2026-04-14",
      "requirements": [
        "Replace lock mechanism (5-lever mortice deadlock BS3621)",
        "Test lock operation from both sides",
        "Provide tenant with new keys (x2)",
        "Check door frame alignment — repair if needed"
      ],
      "allocationNotes": "RE-ALLOCATION: Previous operative could not be verified by tenant. Sending Jamie Crawford who has confirmed availability.",
      "assignedOperativeName": "Jamie Crawford"
    },
    "verifyOperativeAccepted": {
      "operativeVerified": true,
      "verificationNotes": "Credential matches — Jamie Crawford, Stoniebridge Construction. Glad I could check."
    },
    "completeWork": {
      "completedItems": [
        "Replaced lock mechanism — fitted ERA 5-lever BS3621 mortice deadlock",
        "Tested lock from inside and outside — smooth operation both sides",
        "Provided tenant with 2 new keys, demonstrated operation",
        "Checked door frame — slight misalignment corrected with hinge adjustment"
      ],
      "materialsUsed": "ERA 5-lever mortice deadlock BS3621, 3\" brass hinges x2 (frame adjustment)",
      "hoursWorked": 2.0,
      "deliveryNotes": "New BS3621 lock fitted and tested. Door frame realigned. Tenant has both keys and demonstrated she can operate the lock comfortably. Property is now secure."
    },
    "reviewCompletion": {
      "decision": "Accepted",
      "reviewNotes": "All four checklist items completed. BS3621 lock fitted as specified. Tenant confirmed she can operate the lock. Good job given the re-allocation complication."
    },
    "confirmSatisfaction": {
      "satisfied": true,
      "feedbackNotes": "Glad I could check who they were before opening the door. The new lock is much better than the old one. Feel much safer now.",
      "rating": 5
    }
  }
}
```

- [ ] **Step 4: Commit**

```bash
git add walkthroughs/PropertyInspection/data/
git commit -m "feat: add PropertyInspection scenario data (routine, emergency, verification failure)"
```

---

## Task 5: Create Actor Definitions

**Files:**
- Create: `walkthroughs/PropertyInspection/actors/tenant.json`
- Create: `walkthroughs/PropertyInspection/actors/housing-officer.json`
- Create: `walkthroughs/PropertyInspection/actors/contractor.json`
- Create: `walkthroughs/PropertyInspection/actors/building-inspector.json`

- [ ] **Step 1: Create actors/tenant.json**

```json
{
  "actor": {
    "name": "tenant",
    "description": "Council tenant — reports problem with photo, verifies operative, confirms satisfaction"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "{{roles.tenant.email}}",
      "password": "$env:TENANT_PASSWORD",
      "organizationId": "{{roles.tenant.organizationId}}"
    },
    "walletAddress": "{{roles.tenant.walletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 15 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "Report Problem",
      "decision": "approve",
      "preActions": [
        {
          "type": "file-upload",
          "config": {
            "fieldName": "damagePhoto",
            "sizeBytes": 4096,
            "seed": 42,
            "fileName": "damage-photo.jpg",
            "contentType": "image/jpeg"
          }
        }
      ],
      "payload": {
        "tenantName": "Flora MacInnes",
        "contactPhone": "+44 1463 555 201",
        "propertyAddress": {
          "street": "14 Moray Crescent",
          "town": "Carronbridge",
          "postcode": "SC4 2TL"
        },
        "description": "Kitchen tap is leaking continuously. Water has caused damage to the chipboard cabinet underneath the sink. The cabinet door is swollen and won't close properly.",
        "category": "Plumbing",
        "urgencyDescription": "Not an emergency but getting worse — bucket filling up twice a day",
        "accessNotes": "Key safe at side gate, code shared with housing office. Available weekdays 9am-5pm."
      }
    },
    {
      "actionName": "Verify Operative",
      "decision": "approve",
      "payload": {
        "operativeVerified": true,
        "verificationNotes": "Credential matches — Jamie Crawford, Stoniebridge Construction"
      }
    },
    {
      "actionName": "Confirm Satisfaction",
      "decision": "approve",
      "payload": {
        "satisfied": true,
        "feedbackNotes": "Very tidy job, thank you. Kitchen is back to normal.",
        "rating": 5
      }
    }
  ],
  "resilience": {
    "retryCount": 3,
    "retryDelaySeconds": 2,
    "circuitBreakerThreshold": 5,
    "circuitBreakerDurationSeconds": 30
  },
  "logging": {
    "level": "Information",
    "actionLog": "./logs/tenant-actions.jsonl"
  }
}
```

- [ ] **Step 2: Create actors/housing-officer.json**

```json
{
  "actor": {
    "name": "housing-officer",
    "description": "Housing Officer — triages requests, allocates contractor, reviews completion"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "{{roles.housing-officer.email}}",
      "password": "$env:HOUSING_OFFICER_PASSWORD",
      "organizationId": "{{roles.housing-officer.organizationId}}"
    },
    "walletAddress": "{{roles.housing-officer.walletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 15 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "Triage & Allocate",
      "decision": "approve",
      "payload": {
        "severity": "Routine",
        "targetCompletionDate": "2026-04-25",
        "requirements": [
          "Replace tap unit (mixer tap, kitchen spec)",
          "Check isolation valve under sink",
          "Inspect for further water damage to adjacent units",
          "Replace damaged cabinet panel if beyond repair"
        ],
        "allocationNotes": "Standard plumbing job. Tenant has key safe access so no need for appointment.",
        "assignedOperativeName": "Jamie Crawford"
      }
    },
    {
      "actionName": "Review Completion",
      "decision": "approve",
      "payload": {
        "decision": "Accepted",
        "reviewNotes": "All checklist items completed. Photos show clean installation. No further action required."
      }
    }
  ],
  "resilience": {
    "retryCount": 3,
    "retryDelaySeconds": 2,
    "circuitBreakerThreshold": 5,
    "circuitBreakerDurationSeconds": 30
  },
  "logging": {
    "level": "Information",
    "actionLog": "./logs/housing-officer-actions.jsonl"
  }
}
```

- [ ] **Step 3: Create actors/contractor.json**

```json
{
  "actor": {
    "name": "contractor",
    "description": "Site Operative — completes repair work with photo evidence and delivery note"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "{{roles.contractor.email}}",
      "password": "$env:CONTRACTOR_PASSWORD",
      "organizationId": "{{roles.contractor.organizationId}}"
    },
    "walletAddress": "{{roles.contractor.walletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 15 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "Complete Work",
      "decision": "approve",
      "preActions": [
        {
          "type": "file-upload",
          "config": {
            "fieldName": "completionPhoto",
            "sizeBytes": 4096,
            "seed": 99,
            "fileName": "completion-photo.jpg",
            "contentType": "image/jpeg"
          }
        }
      ],
      "payload": {
        "completedItems": [
          "Replaced tap unit — fitted Bristan Quest mono mixer",
          "Checked isolation valve — functioning correctly, no leak",
          "Inspected adjacent cabinets — no further water damage",
          "Replaced damaged cabinet panel with matching unit"
        ],
        "materialsUsed": "Bristan Quest mono mixer tap, 15mm flexi connectors x2, cabinet panel (600mm white melamine)",
        "hoursWorked": 3.0,
        "deliveryNotes": "Tap replaced and tested with no leaks. Cabinet panel replaced. All waste removed from property."
      }
    }
  ],
  "resilience": {
    "retryCount": 3,
    "retryDelaySeconds": 2,
    "circuitBreakerThreshold": 5,
    "circuitBreakerDurationSeconds": 30
  },
  "logging": {
    "level": "Information",
    "actionLog": "./logs/contractor-actions.jsonl"
  }
}
```

- [ ] **Step 4: Create actors/building-inspector.json**

```json
{
  "actor": {
    "name": "building-inspector",
    "description": "Building Inspector — safety sign-off for Emergency severity jobs"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "{{roles.building-inspector.email}}",
      "password": "$env:BUILDING_INSPECTOR_PASSWORD",
      "organizationId": "{{roles.building-inspector.organizationId}}"
    },
    "walletAddress": "{{roles.building-inspector.walletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 20 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "Safety Sign-Off",
      "decision": "approve",
      "payload": {
        "structuralSafe": true,
        "electricalSafe": true,
        "gasSafe": true,
        "complianceNotes": "Ceiling repair structurally sound. NICEIC electrical certificate confirms safe installation. No gas appliances affected. Property safe for habitation.",
        "signOffDecision": "Pass"
      }
    }
  ],
  "resilience": {
    "retryCount": 3,
    "retryDelaySeconds": 2,
    "circuitBreakerThreshold": 5,
    "circuitBreakerDurationSeconds": 30
  },
  "logging": {
    "level": "Information",
    "actionLog": "./logs/building-inspector-actions.jsonl"
  }
}
```

- [ ] **Step 5: Commit**

```bash
git add walkthroughs/PropertyInspection/actors/
git commit -m "feat: add PropertyInspection actor definitions (4 actors)"
```

---

## Task 6: Create PropertyInspection setup.ps1

**Files:**
- Create: `walkthroughs/PropertyInspection/setup.ps1`

- [ ] **Step 1: Create setup.ps1**

This script calls the shared council setup, creates a tenant user (public org citizen), creates the Property Inspection register, subscribes orgs, publishes participants, publishes the blueprint, and creates the tenant's persona.

```powershell
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PropertyInspection walkthrough setup — calls shared council setup,
# then creates register, blueprint, tenant users, and persona.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$Scenario = 'a',
    [switch]$Force,
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$stateFile = Join-Path $scriptDir "state.json"

# ── Module ────────────────────────────────────────────────────────
$modulePath = Join-Path $scriptDir ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

# ── Environment ───────────────────────────────────────────────────
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck
$piSecrets = Get-SorchaSecrets -WalkthroughName "property-inspection"

Write-WtBanner "PropertyInspection Walkthrough Setup"

# ── Shared council setup ──────────────────────────────────────────
Write-WtStep "Running shared council setup"
$councilScript = Join-Path $scriptDir ".." "council" "setup-council.ps1"
$councilState = & $councilScript -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# ── System admin session ──────────────────────────────────────────
$sysAdmin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $councilState.sysAdmin.email -AdminPassword $councilState.sysAdmin.password

# ── Tenant user setup (scenario-specific) ─────────────────────────
Write-WtStep "Creating tenant user"

$tenantDefs = @{
    a = @{ email = $piSecrets.tenantAEmail; password = $piSecrets.tenantAPassword; name = $piSecrets.tenantAName }
    b = @{ email = $piSecrets.tenantBEmail; password = $piSecrets.tenantBPassword; name = $piSecrets.tenantBName }
    c = @{ email = $piSecrets.tenantCEmail; password = $piSecrets.tenantCPassword; name = $piSecrets.tenantCName }
}
$tenantDef = $tenantDefs[$Scenario.ToLower()]

$publicOrgId = "00000000-0000-0000-0000-000000000002"

# Register tenant on public org
Register-SorchaPublicUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $tenantDef.email -Password $tenantDef.password -DisplayName $tenantDef.name | Out-Null

# Verify email
$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true&pageSize=100" `
    -Headers $sysAdmin.Headers
$tenantUser = $publicUsers.users | Where-Object { $_.email -eq $tenantDef.email } | Select-Object -First 1
if ($tenantUser) {
    Confirm-SorchaUserEmail -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId -UserId $tenantUser.id -Headers $sysAdmin.Headers | Out-Null
}

# Login as tenant
$tenantSession = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $tenantDef.email -Password $tenantDef.password -OrganizationId $publicOrgId

# Create wallet
$tenantWallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl `
    -Name "$($tenantDef.name) Wallet" -Headers $tenantSession.Headers -FetchPublicKey

# Register participant
$tenantParticipant = Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl -OrganizationId $publicOrgId `
    -WalletAddress $tenantWallet.Address -DisplayName $tenantDef.name -Headers $tenantSession.Headers

Write-WtInfo "Tenant $($tenantDef.name) → $($tenantWallet.Address)"

# ── Create register ───────────────────────────────────────────────
Write-WtStep "Creating Property Inspection register"

# Register owner = Strathcarron Council (housing-officer creates it)
$housingRole = $councilState.roles."housing-officer"
$housingSession = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $housingRole.email -Password $housingRole.password `
    -OrganizationId $housingRole.organizationId

$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Name "Strathcarron Property Services Register" `
    -Description "Council property inspection and repair workflow register" `
    -TenantId $housingRole.organizationId `
    -OwnerUserId $housingSession.UserId `
    -OwnerWalletAddress $housingRole.walletAddress `
    -Headers $housingSession.Headers `
    -Metadata @{ createdBy = "PropertyInspection walkthrough" }

Write-WtInfo "Register → $($register.RegisterId)"

# ── Subscribe orgs ────────────────────────────────────────────────
Write-WtStep "Subscribing organisations to register"

# Strathcarron Council (owner — auto-subscribed by New-SorchaRegister)
# Stoniebridge Construction
$contractorRole = $councilState.roles."contractor"
$contractorSession = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $contractorRole.email -Password $contractorRole.password `
    -OrganizationId $contractorRole.organizationId
New-SorchaRegisterSubscription -TenantUrl $sorchaEnv.TenantUrl `
    -OrganizationId $contractorRole.organizationId `
    -RegisterId $register.RegisterId `
    -RegisterName "Strathcarron Property Services Register" `
    -SubscriptionType "Public" -Headers $contractorSession.Headers | Out-Null

# Public org subscription (for tenant)
Add-SorchaPublicOrgSubscription -TenantUrl $sorchaEnv.TenantUrl `
    -RegisterId $register.RegisterId `
    -RegisterName "Strathcarron Property Services Register" `
    -SysAdminHeaders $sysAdmin.Headers `
    -SysAdminEmail $councilState.sysAdmin.email | Out-Null

# ── Publish participants to register ──────────────────────────────
Write-WtStep "Publishing participants to register"

$participantPublishDefs = @(
    @{ role = "tenant"; name = $tenantDef.name; org = "Public"; address = $tenantWallet.Address; publicKey = $tenantParticipant.PublicKey ?? $tenantWallet.PublicKey; orgId = $publicOrgId; session = $tenantSession }
    @{ role = "housing-officer"; name = $housingRole.name; org = "Strathcarron Council"; address = $housingRole.walletAddress; publicKey = $housingRole.publicKey; orgId = $housingRole.organizationId; session = $housingSession }
    @{ role = "contractor"; name = $contractorRole.name; org = "Stoniebridge Construction"; address = $contractorRole.walletAddress; publicKey = $contractorRole.publicKey; orgId = $contractorRole.organizationId; session = $contractorSession }
    @{ role = "building-inspector"; name = $councilState.roles."building-inspector".name; org = "Strathcarron Council"; address = $councilState.roles."building-inspector".walletAddress; publicKey = $councilState.roles."building-inspector".publicKey; orgId = $councilState.roles."building-inspector".organizationId; session = $null }
)

# Building inspector session
$biRole = $councilState.roles."building-inspector"
$biSession = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $biRole.email -Password $biRole.password -OrganizationId $biRole.organizationId
$participantPublishDefs[3].session = $biSession

foreach ($p in $participantPublishDefs) {
    Publish-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $p.orgId `
        -RegisterId $register.RegisterId `
        -ParticipantName $p.name `
        -OrganizationName $p.org `
        -WalletAddress $p.address `
        -PublicKey $p.publicKey `
        -Headers $p.session.Headers | Out-Null
    Write-WtInfo "  Published $($p.role)"
}

# ── Publish blueprint ─────────────────────────────────────────────
Write-WtStep "Publishing blueprint"

$walletMap = @{
    "tenant"            = $tenantWallet.Address
    "housing-officer"   = $housingRole.walletAddress
    "contractor"        = $contractorRole.walletAddress
    "building-inspector" = $biRole.walletAddress
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "property-inspection-template.json") `
    -WalletMap $walletMap `
    -Headers $housingSession.Headers `
    -IdPrefix "pi" `
    -RegisterId $register.RegisterId

Write-WtInfo "Blueprint → $($blueprint.BlueprintId)"
if ($blueprint.Warnings) {
    foreach ($w in $blueprint.Warnings) { Write-WtWarn "  $w" }
}

# ── Create tenant persona ────────────────────────────────────────
Write-WtStep "Creating tenant persona"

$personaDefs = @{
    a = @{
        givenName = "Flora"; familyName = "MacInnes"; fullName = "Flora MacInnes"
        phones = @(@{ value = "+44 1463 555 201"; isDefault = $true })
        addresses = @(@{ street = "14 Moray Crescent"; locality = "Carronbridge"; postalCode = "SC4 2TL"; country = "GB"; isDefault = $true })
    }
    b = @{
        givenName = "Angus"; familyName = "Beaton"; fullName = "Angus Beaton"
        phones = @(@{ value = "+44 1463 555 302"; isDefault = $true })
        addresses = @(@{ street = "7 Loch Morach Drive"; locality = "Dalreoch"; postalCode = "SC6 8JN"; country = "GB"; isDefault = $true })
    }
    c = @{
        givenName = "Eilidh"; familyName = "Drummond"; fullName = "Eilidh Drummond"
        phones = @(@{ value = "+44 1463 555 403"; isDefault = $true })
        addresses = @(@{ street = "3 Invercarron Row"; locality = "Invercarron"; postalCode = "SC2 5PA"; country = "GB"; isDefault = $true })
    }
}

$persona = $personaDefs[$Scenario.ToLower()]
Invoke-SorchaApi -Method PUT `
    -Uri "$($sorchaEnv.GatewayUrl)/api/me/persona" `
    -Body $persona -Headers $tenantSession.Headers | Out-Null
Write-WtSuccess "Persona created for $($persona.fullName)"

# ── Save state ────────────────────────────────────────────────────
$state = @{
    profile      = $Profile
    scenario     = $Scenario
    registerId   = $register.RegisterId
    blueprintId  = $blueprint.BlueprintId
    blueprintUrl = $sorchaEnv.BlueprintUrl
    tenantUrl    = $sorchaEnv.TenantUrl
    gatewayUrl   = $sorchaEnv.GatewayUrl
    walletUrl    = $sorchaEnv.WalletUrl
    organizations = $councilState.organizations
    roles        = @{
        "tenant"            = @{ email = $tenantDef.email; password = $tenantDef.password; organizationId = $publicOrgId; walletAddress = $tenantWallet.Address }
        "housing-officer"   = @{ email = $housingRole.email; password = $housingRole.password; organizationId = $housingRole.organizationId; walletAddress = $housingRole.walletAddress }
        "contractor"        = @{ email = $contractorRole.email; password = $contractorRole.password; organizationId = $contractorRole.organizationId; walletAddress = $contractorRole.walletAddress }
        "building-inspector" = @{ email = $biRole.email; password = $biRole.password; organizationId = $biRole.organizationId; walletAddress = $biRole.walletAddress }
    }
}

$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "Setup Complete — run: ./run-agents.ps1 -StatePath $stateFile"
```

- [ ] **Step 2: Verify script syntax**

```bash
pwsh -NoExecute -File walkthroughs/PropertyInspection/setup.ps1
```

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/PropertyInspection/setup.ps1
git commit -m "feat: add PropertyInspection setup.ps1 with council setup, persona, and register"
```

---

## Task 7: Create run-agents.ps1

**Files:**
- Create: `walkthroughs/PropertyInspection/run-agents.ps1`

- [ ] **Step 1: Create run-agents.ps1**

```powershell
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PropertyInspection actor launcher — creates blueprint instance and runs agents.

param(
    [string]$Profile = "gateway",
    [string]$StatePath,
    [int]$TimeoutMinutes = 5,
    [string]$AgentBinary
)

$ErrorActionPreference = 'Stop'
$walkthroughDir = $PSScriptRoot
$actorsDir = Join-Path $walkthroughDir "actors"

# ── Module ────────────────────────────────────────────────────────
$modulePath = Join-Path $walkthroughDir ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PropertyInspection — Actor Launcher"

# ── Load state ────────────────────────────────────────────────────
if (-not $StatePath) { $StatePath = Join-Path $walkthroughDir "state.json" }
if (-not (Test-Path $StatePath)) {
    Write-Error "State file not found: $StatePath. Run setup.ps1 first."
    exit 1
}
$state = Get-Content $StatePath -Raw | ConvertFrom-Json

# ── Environment ───────────────────────────────────────────────────
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck

# ── Create blueprint instance ─────────────────────────────────────
Write-WtStep "Creating blueprint instance"

$housingRole = $state.roles."housing-officer"
$housingSession = Connect-SorchaUser -TenantUrl $state.tenantUrl `
    -Email $housingRole.email -Password $housingRole.password `
    -OrganizationId $housingRole.organizationId

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Headers $housingSession.Headers `
    -Body @{
        blueprintId = $state.blueprintId
        registerId  = $state.registerId
        tenantId    = $housingRole.organizationId
    }

if (-not $instance?.id) {
    Write-Error "Failed to create blueprint instance."
    exit 1
}
Write-WtSuccess "Instance created: $($instance.id)"

# ── Resolve agent command ─────────────────────────────────────────
if (-not $AgentBinary) {
    $agentProject = Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj"
    $agentCmd = "dotnet"
    $agentBaseArgs = @("run", "--project", (Resolve-Path $agentProject).Path, "--")
} else {
    $agentCmd = $AgentBinary
    $agentBaseArgs = @()
}

# ── Actor configurations ──────────────────────────────────────────
$actors = @(
    @{ File = "tenant.json"; EnvVar = "TENANT_PASSWORD"; PasswordKey = "tenant" }
    @{ File = "housing-officer.json"; EnvVar = "HOUSING_OFFICER_PASSWORD"; PasswordKey = "housing-officer" }
    @{ File = "contractor.json"; EnvVar = "CONTRACTOR_PASSWORD"; PasswordKey = "contractor" }
    @{ File = "building-inspector.json"; EnvVar = "BUILDING_INSPECTOR_PASSWORD"; PasswordKey = "building-inspector" }
)

# ── Set password env vars ─────────────────────────────────────────
foreach ($actor in $actors) {
    $role = $state.roles.($actor.PasswordKey)
    if ($role -and $role.password) {
        [Environment]::SetEnvironmentVariable($actor.EnvVar, $role.password)
    }
}

# ── Launch actors ─────────────────────────────────────────────────
$logsDir = Join-Path $walkthroughDir "logs"
New-Item -Path $logsDir -ItemType Directory -ErrorAction SilentlyContinue | Out-Null

$processes = @()
foreach ($actor in $actors) {
    $configPath = Join-Path $actorsDir $actor.File
    if (-not (Test-Path $configPath)) {
        Write-WtWarn "Actor config not found: $configPath"
        continue
    }

    $logFile = Join-Path $logsDir ($actor.File -replace '\.json$', '.log')
    $agentArgs = $agentBaseArgs + @("run", "--config", $configPath, "--state", $StatePath)

    Write-WtInfo "Launching $($actor.File)..."
    $proc = Start-Process -FilePath $agentCmd -ArgumentList $agentArgs `
        -RedirectStandardOutput $logFile `
        -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow

    $processes += @{ Process = $proc; Name = $actor.File; LogFile = $logFile }
}

Write-WtSuccess "All $($processes.Count) actors launched"

# ── Wait for completion ───────────────────────────────────────────
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$allExited = $false

while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) { $allExited = $true; break }

    $runningNames = ($running | ForEach-Object { $_.Name }) -join ", "
    Write-Host "`r  Waiting... ($($running.Count) running: $runningNames)" -NoNewline
    Start-Sleep -Seconds 5
}
Write-Host ""

# ── Shutdown & summary ────────────────────────────────────────────
if (-not $allExited) {
    Write-WtWarn "Timeout after $TimeoutMinutes minutes — killing remaining actors"
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    foreach ($p in $running) {
        try { $p.Process.Kill() } catch { }
    }
}

# Clean up env vars
foreach ($actor in $actors) {
    [Environment]::SetEnvironmentVariable($actor.EnvVar, $null)
}

# Summary
Write-WtBanner "Results"
foreach ($p in $processes) {
    $exitCode = $p.Process.ExitCode
    $status = if ($exitCode -eq 0) { "[PASS]" } else { "[FAIL] (exit $exitCode)" }
    Write-Host "  $status $($p.Name)"
    if ($exitCode -ne 0 -and (Test-Path $p.LogFile)) {
        Write-Host "    Log: $($p.LogFile)"
        Get-Content $p.LogFile | Select-Object -Last 5 | ForEach-Object { Write-Host "    $_" }
    }
}

$failed = $processes | Where-Object { $_.Process.ExitCode -ne 0 }
if ($failed.Count -gt 0) {
    Write-WtWarn "$($failed.Count) actor(s) failed"
    exit 1
} else {
    Write-WtSuccess "All actors completed successfully"
}
```

- [ ] **Step 2: Verify syntax**

```bash
pwsh -NoExecute -File walkthroughs/PropertyInspection/run-agents.ps1
```

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/PropertyInspection/run-agents.ps1
git commit -m "feat: add PropertyInspection run-agents.ps1 actor launcher"
```

---

## Task 8: Create config.json and README.md

**Files:**
- Create: `walkthroughs/PropertyInspection/config.json`
- Create: `walkthroughs/PropertyInspection/README.md`

- [ ] **Step 1: Create config.json**

```json
{
  "name": "PropertyInspection",
  "description": "Council property services — tenant reports problem with photo evidence, council triages and allocates contractor, tenant verifies operative identity, contractor completes work with photo evidence, council signs off. 4 actors, 7 actions, 3 scenarios.",
  "category": "multi-org",
  "universe": "strathcarron-council",
  "organizations": [
    { "name": "Public Org", "subdomain": "public", "role": "tenant (citizen)" },
    { "name": "Strathcarron Council", "subdomain": "strathcarron", "role": "housing-officer, building-inspector" },
    { "name": "Stoniebridge Construction", "subdomain": "stoniebridge", "role": "contractor" }
  ],
  "secretsKey": "property-inspection",
  "councilSecretsKey": "council",
  "requiresRegister": true,
  "requiresParticipants": true,
  "template": "property-inspection-template.json",
  "scenarios": [
    "data/scenario-a-routine.json",
    "data/scenario-b-emergency.json",
    "data/scenario-c-verification-failure.json"
  ],
  "features": [
    "file-upload",
    "verifiable-credentials",
    "consumer-persona",
    "conditional-routing",
    "cyclic-rework",
    "participant-verification"
  ]
}
```

- [ ] **Step 2: Create README.md**

Create a comprehensive README following the pattern of existing walkthrough READMEs. Cover: overview, prerequisites, quick start, scenario descriptions, actor details, features exercised, and troubleshooting. Reference the design spec for full details.

The README should be substantial (150-250 lines) covering the narrative (Strathcarron Council, safeguarding angle), the three scenarios with their action paths, and how to run each scenario. Follow the pattern from `walkthroughs/ConstructionPermit/README.md`.

- [ ] **Step 3: Commit**

```bash
git add walkthroughs/PropertyInspection/config.json walkthroughs/PropertyInspection/README.md
git commit -m "feat: add PropertyInspection config.json and README.md"
```

---

## Task 9: Align ConstructionPermit to Strathcarron Universe

**Files:**
- Modify: `walkthroughs/ConstructionPermit/setup.ps1`
- Modify: `walkthroughs/ConstructionPermit/construction-permit-template.json`
- Modify: `walkthroughs/ConstructionPermit/README.md`
- Modify: `walkthroughs/ConstructionPermit/config.json`
- Modify: `walkthroughs/ConstructionPermit/actors/*.json` (5 files)
- Modify: `walkthroughs/ConstructionPermit/data/*.json` (3 files)

This task is string replacements only — no logic changes.

- [ ] **Step 1: Replace organisation names across all files**

Apply these replacements across all ConstructionPermit files:

| Find | Replace |
|------|---------|
| `Meridian Construction` | `Stoniebridge Construction` |
| `Apex Structural Engineers` | `Murchison Engineering` |
| `Riverside Borough Council` | `Strathcarron Council` |
| `Green Valley Environmental` | `Heatherbank Environmental` |
| `meridian` (subdomain) | `stoniebridge` |
| `apex` (subdomain) | `murchison` |
| `riverside` (subdomain) | `strathcarron` |
| `greenvalley` (subdomain) | `heatherbank` |

- [ ] **Step 2: Replace place names and addresses**

| Find | Replace |
|------|---------|
| `Riverside Heights` | `Carronbridge Heights` |
| `14 Waterfront Lane, Riverside, RS1 4AB` | `14 Waterfront Lane, Carronbridge, SC1 4AB` |
| `42 Commerce Street, Riverside, RS2 7GH` | `42 Commerce Street, Carronbridge, SC2 7GH` |
| `8 Industrial Way, Riverside, RS3 1KL` | `8 Industrial Way, Carronbridge, SC3 1KL` |
| `Central Business Tower` | `Carronbridge Business Tower` |
| `Eastside Commercial Centre` | `Eastside Commercial Centre` (keep — generic enough) |
| `RBC-` (permit prefix) | `SC-` |
| `Riverside Borough` | `Strathcarron` |

- [ ] **Step 3: Replace email domains in secrets references**

Update `passwords.json` to use council secrets key, or update the setup.ps1 to reference the shared council secrets. The simplest approach: update `setup.ps1` to call `council/setup-council.ps1` and use its state for org/wallet/participant setup, keeping only the ConstructionPermit-specific register and blueprint creation.

- [ ] **Step 4: Refactor setup.ps1 to use shared council setup**

Replace the org creation, user registration, and wallet/participant setup sections with a call to `council/setup-council.ps1`. Keep only:
1. Call to council setup
2. Register creation (Construction Permit Register)
3. Org subscriptions to register
4. Participant publishing to register
5. Blueprint publishing
6. State file output

- [ ] **Step 5: Run walkthrough to verify no regressions**

```bash
cd walkthroughs/ConstructionPermit
pwsh -File setup.ps1 -Profile gateway
pwsh -File run-agents.ps1
```

Expected: All 5 actors complete without errors.

- [ ] **Step 6: Commit**

```bash
git add walkthroughs/ConstructionPermit/
git commit -m "refactor: align ConstructionPermit to Strathcarron Council universe"
```

---

## Task 10: Align SelfBuildHouse to Strathcarron Universe

**Files:**
- Modify: `walkthroughs/SelfBuildHouse/setup.ps1`
- Modify: `walkthroughs/SelfBuildHouse/planning-permission-template.json`
- Modify: `walkthroughs/SelfBuildHouse/building-warrant-template.json`
- Modify: `walkthroughs/SelfBuildHouse/README.md`
- Modify: `walkthroughs/SelfBuildHouse/config.json`
- Modify: `walkthroughs/SelfBuildHouse/actors/*.json` (7 files)
- Modify: `walkthroughs/SelfBuildHouse/data/*.json` (3 files)

- [ ] **Step 1: Replace organisation names**

| Find | Replace |
|------|---------|
| `Highland Council — Planning` | `Strathcarron Council` |
| `Highland Council — Building Standards` | `Strathcarron Council` |
| `MacGregor Structural Engineers` | `Murchison Engineering` |
| `Glen Ecology Consultants` | `Heatherbank Environmental` |
| `Scottish Water` | `Caledonian Water` |
| `highland-planning` (subdomain) | `strathcarron` |
| `highland-bs` (subdomain) | `strathcarron` |
| `macgregor` (subdomain) | `murchison` |
| `glen-ecology` (subdomain) | `heatherbank` |
| `scottish-water` (subdomain) | `caledonian-water` |

- [ ] **Step 2: Replace place names**

| Find | Replace |
|------|---------|
| `Drumnadrochit` | `Dalreoch` |
| `Aviemore` | `Carronbridge` |
| `Cromarty` | `Invercarron` |
| `Loch Ness` | `Loch Morach` |
| `Inverness-shire` | `Strathcarron` |
| `Lochside Road` | `Lochside Road` (keep — generic) |
| `Highland` (in register names, policy refs) | `Strathcarron` |
| `IV63 6TU` / `PH22 1QH` / `IV11 8XA` | `SC6 3TU` / `SC4 1QH` / `SC2 8XA` |
| `NH5130` / `NH9010` / `NH7867` (grid refs) | `SC5130` / `SC9010` / `SC7867` |

- [ ] **Step 3: Merge two Highland Council orgs into one Strathcarron Council**

This is the structural change. SelfBuildHouse currently creates `highland-planning` and `highland-bs` as separate orgs. Both must become the single `strathcarron` org from the shared council setup. In `setup.ps1`:

- Remove creation of two separate Highland Council orgs
- Call `council/setup-council.ps1` instead
- Use `councilState.organizations.strathcarron` for both planning and building standards roles
- Building Standards users become team members of the same Strathcarron Council org

- [ ] **Step 4: Update register ownership**

The Planning Register and Building Standards Register both become owned by Strathcarron Council. Different department users (planning-officer, building-standards-officer) create/own their respective registers within the same org.

- [ ] **Step 5: Run walkthrough to verify**

```bash
cd walkthroughs/SelfBuildHouse
pwsh -File setup.ps1 -Profile gateway
pwsh -File run-agents.ps1 -Scenario a
```

Expected: All 7 actors complete scenario A without errors.

- [ ] **Step 6: Commit**

```bash
git add walkthroughs/SelfBuildHouse/
git commit -m "refactor: align SelfBuildHouse to Strathcarron Council universe (merge orgs)"
```

---

## Task 11: End-to-End Test PropertyInspection

**Files:** None (testing only)

- [ ] **Step 1: Start Docker environment**

```bash
docker-compose up -d
```

Wait for services to be healthy.

- [ ] **Step 2: Run PropertyInspection setup (Scenario A)**

```bash
cd walkthroughs/PropertyInspection
pwsh -File setup.ps1 -Profile gateway -Scenario a
```

Expected: State file created with registerId, blueprintId, and all role details.

- [ ] **Step 3: Run agents (Scenario A)**

```bash
pwsh -File run-agents.ps1 -Profile gateway
```

Expected: All 4 actors complete. Path: Report → Triage → Verify → Complete → Review → Satisfy.

- [ ] **Step 4: Verify persona was created**

```bash
pwsh -Command "
  Import-Module ./modules/SorchaWalkthrough/SorchaWalkthrough.psm1 -Force
  `$env = Initialize-SorchaEnvironment -Profile gateway
  `$state = Get-Content walkthroughs/PropertyInspection/state.json | ConvertFrom-Json
  `$session = Connect-SorchaUser -TenantUrl `$env.TenantUrl -Email `$state.roles.tenant.email -Password `$state.roles.tenant.password -OrganizationId `$state.roles.tenant.organizationId
  Invoke-SorchaApi -Method GET -Uri \"`$(`$env.GatewayUrl)/api/me/persona\" -Headers `$session.Headers
"
```

Expected: Returns Flora MacInnes persona with address and phone.

- [ ] **Step 5: Run Scenario B (if time allows)**

```bash
pwsh -File setup.ps1 -Profile gateway -Scenario b -Force
pwsh -File run-agents.ps1 -Profile gateway -TimeoutMinutes 8
```

Note: Scenario B exercises the rework loop and inspector, so actors need scenario-specific payloads. The default actor configs use Scenario A data. For B and C, either swap actor payloads or create scenario-specific actor variants. This is a known limitation — document in README that actor configs default to Scenario A, and scenarios B/C require manual actor payload updates or a `-Scenario` flag in `run-agents.ps1`.

- [ ] **Step 6: Commit any fixes**

```bash
git add -A
git commit -m "fix: PropertyInspection E2E adjustments from testing"
```

---

## Task 12: Create PR

- [ ] **Step 1: Push and create PR**

```bash
git push origin docs/property-inspection-design
gh pr create --title "feat: PropertyInspection walkthrough + Strathcarron Council universe" --body "$(cat <<'EOF'
## Summary

- New **PropertyInspection** walkthrough: 7 actions, 4 actors, 3 scenarios
- Shared **Strathcarron Council** universe (`walkthroughs/council/`)
- **ConstructionPermit** aligned to Strathcarron (string replacements)
- **SelfBuildHouse** aligned to Strathcarron (org merge + string replacements)

### Features Exercised
- File uploads (Feature 085) — damage photo + completion photo
- Verifiable Credentials — JobAssignmentCredential + ServiceCompletionCredential
- Consumer Persona (Feature 092) — tenant autofill
- Conditional routing — severity-based inspector involvement
- Cyclic rework — contractor retry loop
- Participant verification — safeguarding for vulnerable tenants

## Test plan
- [ ] `setup.ps1 -Scenario a` creates all resources
- [ ] `run-agents.ps1` completes Scenario A (happy path)
- [ ] Persona API returns tenant data
- [ ] ConstructionPermit still passes after alignment
- [ ] SelfBuildHouse still passes after alignment

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

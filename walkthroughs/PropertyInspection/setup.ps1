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
$piSecrets = Get-SorchaSecrets -WalkthroughName "property-inspection" -Profile $Profile

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
    -Name "Strathcarron Property Register" `
    -Description "Council property inspection and repair workflow register" `
    -TenantId $housingRole.organizationId `
    -OwnerUserId $housingSession.UserId `
    -OwnerWalletAddress $housingRole.walletAddress `
    -Headers $housingSession.Headers `
    -DevMode `
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
    -RegisterName "Strathcarron Property Register" `
    -SubscriptionType "Public" -Headers $contractorSession.Headers | Out-Null

# Public org subscription (for tenant)
Add-SorchaPublicOrgSubscription -TenantUrl $sorchaEnv.TenantUrl `
    -RegisterId $register.RegisterId `
    -RegisterName "Strathcarron Property Register" `
    -SysAdminHeaders $sysAdmin.Headers `
    -SysAdminEmail $councilState.sysAdmin.email | Out-Null

# ── Publish participants to register ──────────────────────────────
Write-WtStep "Publishing participants to register"

# The tenant is deliberately ABSENT. They are the sender of action 0, the starting action, which
# makes them the Feature 103 OPEN participant: late-bound to whoever actually submits, not declared
# up front. Every other walkthrough with a citizen starting-participant does the same (AssuredIdentity
# publishes only the analyst and licensing officer; ConstructionPermit only the four org roles).
#
# Publishing them was not merely redundant, it could not work: the call publishes a participant record
# FOR AN ORGANISATION, and the tenant's organisation is the shared Public org, authorised with the
# citizen's own consumer-tier session. n1 answers
# POST /api/organizations/00000000-0000-0000-0000-000000000002/participants/publish with 403, and
# setup died there before reaching the blueprint publish (#1427).
$participantPublishDefs = @(
    @{ role = "housing-officer"; name = $housingRole.name; org = "Strathcarron Council"; address = $housingRole.walletAddress; publicKey = $housingRole.publicKey; orgId = $housingRole.organizationId; session = $housingSession }
    @{ role = "contractor"; name = $contractorRole.name; org = "Stoniebridge Construction"; address = $contractorRole.walletAddress; publicKey = $contractorRole.publicKey; orgId = $contractorRole.organizationId; session = $contractorSession }
    @{ role = "building-inspector"; name = $councilState.roles."building-inspector".name; org = "Strathcarron Council"; address = $councilState.roles."building-inspector".walletAddress; publicKey = $councilState.roles."building-inspector".publicKey; orgId = $councilState.roles."building-inspector".organizationId; session = $null }
)

# Building inspector session (last entry — index tracks the list above)
$biRole = $councilState.roles."building-inspector"
$biSession = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $biRole.email -Password $biRole.password -OrganizationId $biRole.organizationId
$participantPublishDefs[-1].session = $biSession

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

# ── Provision the Feature-083 org master key for the credential ISSUER org ─
# Action 1 (housing-officer, sender) issues a JobAssignmentCredential to the
# contractor. credentialIssuanceConfig omits targetAudience, which defaults to
# SorchaLocalWallet (see CredentialIssuanceConfig.TargetAudience) — so this is
# the same native-wallet issuance path as ConstructionPermit / CyberEssentialsUac
# / Strathcarron, not the HAIP OpenID4VCI path. Without a master key,
# IssuanceKeyService.GetActiveSigningMaterialAsync returns null and the mint
# FAILS (400 "Failed to issue credential"). $housingSession was obtained above
# (line ~84) via Connect-SorchaUser AFTER the housing-officer's wallet was
# already created during the shared council setup, so its JWT already carries
# wallet_address — no extra re-login needed here. Idempotent (409 on re-run
# is fine). See the walkthrough-builder skill.
#
# NB: action 6 (sender "tenant") also carries a credentialIssuanceConfig
# (ServiceCompletionCredential, also defaulting to SorchaLocalWallet) but is
# NOT provisioned here — the tenant is a Public-org citizen/consumer, and
# POST /api/wallets/org/{orgId}/master-key requires RequireAdministrator +
# RequirePlatformAudience, which a consumer-tier token can never satisfy.
# Provisioning a master key for the shared Public org (if that's even the
# right fix) is a platform-wide decision, not a per-walkthrough setup step.
# Flagged for follow-up; left unedited.
Write-WtStep "Provisioning credential-issuer org master key (housing-officer / Strathcarron Council)"

Set-SorchaOrgMasterKey `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $housingRole.organizationId `
    -Headers $housingSession.Headers

# ── Publish blueprint ─────────────────────────────────────────────
Write-WtStep "Publishing blueprint"

# "tenant" is intentionally absent — open participant, late-bound at runtime (see the
# participant-publish block above). Publish-SorchaBlueprint would skip it anyway, but leaving it out
# keeps the map honest about who is actually pre-bound.
$walletMap = @{
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

# Field names and formats are the SERVER's, not this script's invention:
#   PersonaAddress is (Line1, Line2?, City, Region?, PostalCode, Country, IsDefault, Label?)
#   — Sorcha.Tenant.Models.Persona.PersonaAddress. The older "street"/"locality" spellings bound to
#   nothing, so PUT /api/me/persona answered 400 addresses[0].line1=required, addresses[0].city=required.
#   PersonaService validates phones against E.164 (PersonaService.E164Regex), so the number must carry
#   no spaces: "+441463555201", not "+44 1463 555 201" — the spaced form returned invalid_phone.
$personaDefs = @{
    a = @{
        givenName = "Flora"; familyName = "MacInnes"; fullName = "Flora MacInnes"
        phones = @(@{ value = "+441463555201"; isDefault = $true })
        addresses = @(@{ line1 = "14 Moray Crescent"; city = "Carronbridge"; postalCode = "SC4 2TL"; country = "GB"; isDefault = $true })
    }
    b = @{
        givenName = "Angus"; familyName = "Beaton"; fullName = "Angus Beaton"
        phones = @(@{ value = "+441463555302"; isDefault = $true })
        addresses = @(@{ line1 = "7 Loch Morach Drive"; city = "Dalreoch"; postalCode = "SC6 8JN"; country = "GB"; isDefault = $true })
    }
    c = @{
        givenName = "Eilidh"; familyName = "Drummond"; fullName = "Eilidh Drummond"
        phones = @(@{ value = "+441463555403"; isDefault = $true })
        addresses = @(@{ line1 = "3 Invercarron Row"; city = "Invercarron"; postalCode = "SC2 5PA"; country = "GB"; isDefault = $true })
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

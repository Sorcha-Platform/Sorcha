# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS Assured Identity demo toolkit (Feature 174 / M1). Idempotent, reboot-proof
# provisioning over the proven demos/AssuredIdentity loop, plus the autonomous
# Assure-ID agent (rules mode + external checks). Exports:
#   New-AiasOrg, Publish-AiasBlueprint, Start-AiasAgent, Get-AiasDemoStatus,
#   Reset-AiasDemo, and the one-shot Initialize-AiasDemo orchestrator.
#
# This module orchestrates EXISTING Sorcha HTTP endpoints (via the shared
# SorchaWalkthrough module) and the EXISTING sorcha-agent CLI; it adds no
# services. It mirrors demos/AssuredIdentity/AssuredIdentityDemo.psm1 and reuses
# the same generic lib units (Common/DemoState/Auth/AgentLaunch).
#
# Topology note: unlike the two-node AssuredIdentity demo (issuer + subscriber),
# the AIAS M1 demo is single-target (Docker-first, or n1) per
# specs/174-aias-assured-identity/quickstart.md — one installation owns the
# register and runs the agent. The provisioning steps are otherwise identical.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- dependencies -----------------------------------------------------------
$script:DemoRoot = $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force -DisableNameChecking

# dot-source the generic lib units we REUSE verbatim from the AssuredIdentity demo
# (Common first — the others depend on its token helpers + Get-DemoApiBase).
$script:AssuredLib = Join-Path $PSScriptRoot "../AssuredIdentity/lib"
. (Join-Path $script:AssuredLib "Common.ps1")       # Expand-DemoTokens, Get-DemoUnresolvedTokens, Get-DemoApiBase
. (Join-Path $script:AssuredLib "AgencyNaming.ps1")  # Set-BlueprintIssuerName, Test-AgencyNameCoherence
. (Join-Path $script:AssuredLib "Idempotency.ps1")   # Resolve-AuthorityAction
. (Join-Path $script:AssuredLib "DemoState.ps1")     # Read/Write/Merge-DemoState
. (Join-Path $script:AssuredLib "Auth.ps1")          # Import-DemoSecrets, Get-DemoAdminPassword, Connect-DemoNodeAdmin
. (Join-Path $script:AssuredLib "AgentLaunch.ps1")    # New-AgentActorConfig (token render helper)

# AIAS-local lib units
. (Join-Path $PSScriptRoot "lib/BlueprintPublish.ps1")  # Resolve-BlueprintPublishAction (#1269 follow-up)

# --- well-known ids / constants ---------------------------------------------
$script:PublicOrgId  = "00000000-0000-0000-0000-000000000002"
$script:AiasName     = "Acme Identity Assurance Services"
$script:AiasSubdomain = "aias"
# M2: the cyber questionnaire lives on its OWN register (see New-AiasOrg step 6a). 20 chars — the
# hard cap enforced deep inside register finalize is 38; exceeding it fails in a way that has
# historically surfaced as an unexplained 90-second seal timeout rather than a clean error.
$script:AiasCyberName = "Acme Cyber Assurance"

# --- target table (single-installation; Docker-first, or n1) ----------------
# Mirrors the AssuredIdentity node shape (id/role/gateway/adminEmail) so the
# reused Auth.ps1 helpers (Connect-DemoNodeAdmin / Get-DemoAdminPassword) work
# unchanged. The AIAS demo owns the register AND runs the agent on one node.
$script:AiasTargets = @{
    docker = [pscustomobject]@{ id = 'docker'; role = 'issuer'; gateway = 'http://localhost:80';     adminEmail = 'admin@sorcha.local' }
    n1     = [pscustomobject]@{ id = 'n1';     role = 'issuer'; gateway = 'https://n1.sorcha.dev'; adminEmail = 'admin@sorcha.local' }
}

<#
.SYNOPSIS
    Resolve a -Target name ('docker'|'n1') to a node descriptor (gateway + admin).
#>
function Get-AiasTarget {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('docker', 'n1')][string]$Target)
    return $script:AiasTargets[$Target]
}

# ============================================================================
# internal HTTP probes (thin IO — mirrors AssuredIdentityDemo's probes)
# ============================================================================

<#
.SYNOPSIS
    True if the named register reads back on the node (idempotency probe).
#>
function Test-AiasRegisterReadable {
    param([string]$Api, [string]$RegisterId, [hashtable]$Headers)
    if ([string]::IsNullOrWhiteSpace($RegisterId)) { return $false }
    try {
        $r = Invoke-SorchaApi -Method GET -Uri "$Api/registers/$RegisterId" -Headers $Headers
        return ($null -ne $r)
    } catch { return $false }
}

<#
.SYNOPSIS
    Return the blueprint ids currently published on a register (or @()).
#>
function Get-AiasPublishedBlueprintIds {
    # The register-scoped published-blueprints endpoint requires auth (401 anonymous), so callers MUST
    # pass an authenticated session's headers. Omitting them makes the request 401 and the catch swallow
    # it to @(), which surfaces as a false 'blueprint-not-published' / NotReady even when it IS published.
    param([string]$Api, [string]$RegisterId, [hashtable]$Headers)
    try {
        $r = Invoke-SorchaApi -Method GET -Uri "$Api/registers/$RegisterId/blueprints/published" -Headers $Headers
        return @($r.blueprints | ForEach-Object { $_.blueprintId })
    } catch { return @() }
}

<#
.SYNOPSIS
    True if the node's gateway answers a health probe (status verdict).
#>
function Test-AiasGatewayHealthy {
    param([string]$Gateway)
    foreach ($path in @('/health', '/alive', '/')) {
        try {
            Invoke-WebRequest -Uri ($Gateway.TrimEnd('/') + $path) -UseBasicParsing -TimeoutSec 8 | Out-Null
            return $true
        } catch {
            try { if ($_.Exception.Response) { return $true } } catch {}
        }
    }
    return $false
}

# ============================================================================
# New-AiasOrg — create the AIAS org + issuer/agent identities (idempotent)
# ============================================================================

<#
.SYNOPSIS
    Provision the AIAS organisation, its issuer wallet, the Assure-ID agent
    identity, and an advertised register — idempotent (skip-if-present).
.DESCRIPTION
    Mirrors New-IssuingAuthority (demos/AssuredIdentity). On a re-run against an
    already-provisioned, readable register it RETURNS the recorded state without
    recreating anything (FR-010, SC-001). It does NOT publish the blueprint or
    launch the agent — Publish-AiasBlueprint / Start-AiasAgent do that, and the
    Initialize-AiasDemo orchestrator chains all three.
.PARAMETER Target
    'docker' (default) or 'n1' — selects the gateway + admin.
.PARAMETER StateFile
    Per-run state record. Default demos/AIAS/state.json.
.PARAMETER Force
    Recreate even when a readable register + org already exist.
#>
function New-AiasOrg {
    [CmdletBinding()]
    param(
        [ValidateSet('docker', 'n1')][string]$Target = 'docker',
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [switch]$Force
    )

    Write-WtBanner "AIAS demo — provision org '$script:AiasName' ($Target)"

    $node    = Get-AiasTarget -Target $Target
    $api     = Get-DemoApiBase -Gateway $node.gateway
    $secrets = Import-DemoSecrets
    $slug    = $script:AiasSubdomain

    Write-WtStep "1: sysadmin login ($($node.id))"
    $sysAdmin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
    Write-WtSuccess "admin org: $($sysAdmin.OrganizationId)"

    # --- idempotency probe (skip-if-present) -------------------------------
    $existing = Read-DemoState -Path $StateFile
    $registerReadable = $false
    if ($existing -and ($existing.PSObject.Properties.Name -contains 'registerId')) {
        $registerReadable = Test-AiasRegisterReadable -Api $api -RegisterId $existing.registerId -Headers $sysAdmin.Headers
    }
    $blueprintPublished = $false
    if ($existing -and $registerReadable -and ($existing.PSObject.Properties.Name -contains 'blueprintId')) {
        $blueprintPublished = (@(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $existing.registerId -Headers $sysAdmin.Headers) -contains $existing.blueprintId)
    }
    $action = Resolve-AuthorityAction `
        -HasOrg ([bool]($existing -and $existing.organizationId)) `
        -HasRegisterId ([bool]($existing -and ($existing.PSObject.Properties.Name -contains 'registerId') -and $existing.registerId)) `
        -RegisterReadable $registerReadable -BlueprintPublished $blueprintPublished -Force:$Force.IsPresent

    if ($action -eq 'Reuse') {
        Write-WtSuccess "AIAS already provisioned (reuse): org=$($existing.organizationId) register=$($existing.registerId)"

        # M2 self-heal: Resolve-AuthorityAction (demos/AssuredIdentity/lib/Idempotency.ps1) only
        # reasons about the identity register + blueprint — it is SHARED with the AssuredIdentity
        # demo, which has no concept of a cyber register, so widening its contract to know about
        # one would risk changing that demo's behaviour for no benefit to it. A node that was
        # provisioned before the cyber register existed (or whose recorded cyberRegisterId no
        # longer reads back) therefore lands here with a 'Reuse' verdict and no usable
        # cyberRegisterId. Check for it independently, every run, and self-heal if missing — this
        # is the ONLY way such a node can ever acquire the cyber register without -Force
        # (destructive full recreate).
        $existingCyberIdProp = $existing.PSObject.Properties['cyberRegisterId']
        $existingCyberId = if ($existingCyberIdProp) { $existingCyberIdProp.Value } else { $null }
        $cyberReadable = if ($existingCyberId) {
            Test-AiasRegisterReadable -Api $api -RegisterId $existingCyberId -Headers $sysAdmin.Headers
        } else { $false }

        if ($existingCyberId -and $cyberReadable) {
            return $existing
        }

        if ($existingCyberId) {
            Write-WtWarn "Recorded cyber register '$existingCyberId' is not readable on $($node.id) — re-provisioning."
        } else {
            Write-WtInfo "No cyber register recorded (pre-M2 state) — provisioning it now."
        }

        $newCyberRegisterId = Repair-AiasCyberRegister -Api $api -Node $node -Secrets $secrets -Slug $slug `
            -OrganizationId $existing.organizationId -IssuerWalletAddress $existing.issuerWalletAddress `
            -AgentEmail $existing.agentEmail

        $merged = Merge-DemoState -Existing $existing -Updates @{ cyberRegisterId = $newCyberRegisterId }
        Write-DemoState -State $merged -Path $StateFile | Out-Null
        Write-WtSuccess "cyber register provisioned on reuse path — state updated"
        return (Read-DemoState -Path $StateFile)
    }
    if ($action -eq 'ReconcileStale') {
        Write-WtWarn "Recorded register '$($existing.registerId)' is not readable on $($node.id) — reconciling (re-provisioning fresh)."
    }

    Write-WtStep "2: enable public org + register verification-admin"
    Invoke-SorchaApi -Method PUT -Uri "$api/platform/settings/public-org" -Body @{ enabled = $true } -Headers $sysAdmin.Headers | Out-Null
    $pw = Get-DemoAdminPassword -Node $node -Secrets $secrets
    $vAdminEmail = "verification-admin@$slug.local"
    Register-SorchaPublicUser -TenantUrl $api -Email $vAdminEmail -Password $pw -DisplayName "AIAS Verification Admin" | Out-Null

    Write-WtStep "3: verify verification-admin email"
    $pu = Invoke-SorchaApi -Method GET -Uri "$api/organizations/$script:PublicOrgId/users?includeInactive=true" -Headers $sysAdmin.Headers
    $u = $pu.users | Where-Object { $_.email -eq $vAdminEmail } | Select-Object -First 1
    if ($u) { Confirm-SorchaUserEmail -TenantUrl $api -OrganizationId $script:PublicOrgId -UserId $u.id -Headers $sysAdmin.Headers; Write-WtInfo "verified $vAdminEmail" }

    Write-WtStep "4: create org '$script:AiasName'"
    $vOrg = New-SorchaOrganization -TenantUrl $api -Name $script:AiasName -Subdomain $slug -AdminEmail $vAdminEmail -Headers $sysAdmin.Headers -Description "Acme Identity Assurance Services — issues photo-bearing Assured Identity credentials"
    $vOrgId = $vOrg.OrganizationId
    Write-WtSuccess "AIAS org: $vOrgId"

    Write-WtStep "5: verification-admin (Tier 2) login + issuer wallet + participant"
    $vAdmin = Connect-SorchaUser -TenantUrl $api -Email $vAdminEmail -Password $pw -OrganizationId $vOrgId
    # The ORGANISATION's own wallet, created by its admin (#1525). Distinct from the issuer
    # wallet below: this is what the org's issuer DID anchors on, so credential issuance has
    # nothing to anchor to without it. The platform will not create it — the recovery phrase is
    # shown once and belongs to the org admin.
    $null = New-SorchaOrgWallet -TenantUrl $api -WalletUrl $api `
        -OrganizationId $vOrgId -Headers $vAdmin.Headers

    $vWallet = New-SorchaWallet -WalletUrl $api -Name "$script:AiasName Issuer" -Headers $vAdmin.Headers -FetchPublicKey
    Write-WtSuccess "issuer wallet: $($vWallet.Address)"
    $null = Register-SorchaParticipant -TenantUrl $api -WalletUrl $api -OrganizationId $vOrgId -WalletAddress $vWallet.Address -DisplayName "AIAS Verification Admin" -Headers $vAdmin.Headers
    $vAdmin = Connect-SorchaUser -TenantUrl $api -Email $vAdminEmail -Password $pw -OrganizationId $vOrgId

    # --- VC-issuance master key (FR-007, quickstart step 2) -----------------
    # REQUIRED for AIAS: without it the org signs native SorchaLocalWallet VCs
    # with the bare wallet key (unresolvable iss, no did:sorcha kid), so later
    # verification fails closed. The AssuredIdentity demo historically skipped
    # this (HAIP-enrolment only); AIAS MUST do it. Idempotent (no-op on 409).
    Write-WtStep "5b: set AIAS org VC-issuance master key"
    Set-AiasOrgMasterKey -WalletUrl $api -OrganizationId $vOrgId -Headers $vAdmin.Headers

    Write-WtStep "5c: Assure-ID agent (Tier 3) + wallet + participant"
    $agentEmail = "assure-id-agent@$slug.local"
    $null = New-SorchaOrgUser -TenantUrl $api -OrganizationId $vOrgId -Email $agentEmail -Password $pw -DisplayName "Assure-ID Agent" -Headers $sysAdmin.Headers -Roles @("Consumer") -EmailVerified
    $agent = Connect-SorchaUser -TenantUrl $api -Email $agentEmail -Password $pw -OrganizationId $vOrgId
    $agentWallet = New-SorchaWallet -WalletUrl $api -Name "Assure-ID Agent Wallet" -Headers $agent.Headers -FetchPublicKey
    Write-WtSuccess "agent wallet: $($agentWallet.Address)"
    $null = Register-SorchaParticipant -TenantUrl $api -WalletUrl $api -OrganizationId $vOrgId -WalletAddress $agentWallet.Address -DisplayName "Assure-ID Agent" -Headers $agent.Headers

    Write-WtStep "6: create advertised DevMode register (owned by $($node.id))"
    $register = New-SorchaRegister -RegisterUrl $api -WalletUrl $api -Name $script:AiasName -Description "AIAS Assured Identity register — owned by $($node.id)" -TenantId $vOrgId -OwnerUserId $vAdmin.UserId -OwnerWalletAddress $vWallet.Address -Headers $vAdmin.Headers -TenantUrl $api -DevMode:$true
    Write-WtSuccess "register: $($register.RegisterId)"

    # M2: the cyber questionnaire lives on its OWN register, not the Identity one. Keeps the two
    # agents cleanly separated — each agent config is register-scoped, so neither can pick up the
    # other's pending actions. Name is 20 chars; the hard cap is 38 and exceeding it fails deep in
    # register finalize, which previously surfaced as an unexplained 90-second seal timeout.
    Write-WtStep "6a: create advertised DevMode Cyber register (owned by $($node.id))"
    $cyberRegister = New-SorchaRegister -RegisterUrl $api -WalletUrl $api `
        -Name $script:AiasCyberName `
        -Description "AIAS Cyber Level register — owned by $($node.id)" `
        -TenantId $vOrgId -OwnerUserId $vAdmin.UserId -OwnerWalletAddress $vWallet.Address `
        -Headers $vAdmin.Headers -TenantUrl $api -DevMode:$true
    Write-WtSuccess "cyber register: $($cyberRegister.RegisterId)"

    Write-WtStep "6b: publish Assure-ID agent participant on register"
    try {
        $null = Publish-SorchaParticipant -TenantUrl $api -OrganizationId $vOrgId -RegisterId $register.RegisterId -ParticipantName "Assure-ID Agent" -OrganizationName $script:AiasName -WalletAddress $agentWallet.Address -PublicKey $agentWallet.PublicKey -Headers $vAdmin.Headers
        Write-WtSuccess "agent participant published"
    } catch { Write-WtWarn "participant publish: $($_.Exception.Message)" }

    # Same wallet, published as a participant on the CYBER register too — publish governance for a
    # register comes from owner-wallet + participant publishing on THAT register (F142); owning the
    # identity register does not carry over. Reuses the Assure-ID agent's own wallet/identity rather
    # than minting a second one — the two are separate agent PROCESSES driven by separate configs
    # (Build-AiasAgentConfig -Mode cyber), not separate on-chain identities.
    Write-WtStep "6c: publish Assure-ID agent participant on cyber register"
    try {
        $null = Publish-SorchaParticipant -TenantUrl $api -OrganizationId $vOrgId -RegisterId $cyberRegister.RegisterId -ParticipantName "Assure-ID Agent" -OrganizationName $script:AiasName -WalletAddress $agentWallet.Address -PublicKey $agentWallet.PublicKey -Headers $vAdmin.Headers
        Write-WtSuccess "agent participant published (cyber)"
    } catch { Write-WtWarn "cyber participant publish: $($_.Exception.Message)" }

    $state = @{
        target              = $Target
        gateway             = $node.gateway
        agencyName          = $script:AiasName
        organizationId      = $vOrgId
        issuerWalletAddress = $vWallet.Address
        agentEmail          = $agentEmail
        agentWallet         = $agentWallet.Address
        registerId          = $register.RegisterId
        cyberRegisterId     = $cyberRegister.RegisterId
        blueprintId         = $null
        agentMode           = 'rules'
    }
    Write-DemoState -State $state -Path $StateFile | Out-Null
    Write-WtSuccess "AIAS org provisioned — state recorded"
    return (Read-DemoState -Path $StateFile)
}

<#
.SYNOPSIS
    M2 self-heal: create (or recreate) the AIAS Cyber register + its Assure-ID agent participant
    for a node that was provisioned before the cyber register existed, or whose recorded
    cyberRegisterId no longer reads back.
.DESCRIPTION
    Called only from New-AiasOrg's 'Reuse' path — the fresh-provisioning path (steps 6a/6c in the
    body of New-AiasOrg) already does this inline for a brand-new org and is left untouched.
    Neither the verification-admin nor the Assure-ID agent session survives across PowerShell
    invocations, so this reconnects both from recorded state before creating anything.
    New-SorchaRegister is itself idempotent by name (reuses via the caller's subscriptions), and
    Publish-SorchaParticipant tolerates an already-published (409) participant — so this function
    is safe to call repeatedly and will never produce a second cyber register.
.PARAMETER Api
    The node's API base.
.PARAMETER Node
    Node descriptor (id/gateway) — for register description + Get-DemoAdminPassword.
.PARAMETER Secrets
    Loaded demo secrets (Import-DemoSecrets) — for the shared demo-user password.
.PARAMETER Slug
    The AIAS subdomain slug — to reconstruct the verification-admin email.
.PARAMETER OrganizationId
    The already-provisioned AIAS org id.
.PARAMETER IssuerWalletAddress
    The org's issuer wallet address (recorded state.issuerWalletAddress) — register owner wallet.
.PARAMETER AgentEmail
    The Assure-ID agent's login email (recorded state.agentEmail).
#>
function Repair-AiasCyberRegister {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Api,
        [Parameter(Mandatory)][object]$Node,
        [Parameter(Mandatory)][object]$Secrets,
        [Parameter(Mandatory)][string]$Slug,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$IssuerWalletAddress,
        [Parameter(Mandatory)][string]$AgentEmail
    )

    $pw = Get-DemoAdminPassword -Node $Node -Secrets $Secrets
    $vAdminEmail = "verification-admin@$Slug.local"
    $vAdmin = Connect-SorchaUser -TenantUrl $Api -Email $vAdminEmail -Password $pw -OrganizationId $OrganizationId

    # Same wallet the Assure-ID agent already uses on the identity register — reused by name
    # (New-SorchaWallet is idempotent), not minted fresh. Mirrors the "one wallet, two register
    # participations" design of the fresh-provisioning path (New-AiasOrg step 6c comment).
    $agent = Connect-SorchaUser -TenantUrl $Api -Email $AgentEmail -Password $pw -OrganizationId $OrganizationId
    $agentWallet = New-SorchaWallet -WalletUrl $Api -Name "Assure-ID Agent Wallet" -Headers $agent.Headers -FetchPublicKey

    Write-WtStep "6a (repair): create advertised DevMode Cyber register (owned by $($Node.id))"
    $cyberRegister = New-SorchaRegister -RegisterUrl $Api -WalletUrl $Api `
        -Name $script:AiasCyberName `
        -Description "AIAS Cyber Level register — owned by $($Node.id)" `
        -TenantId $OrganizationId -OwnerUserId $vAdmin.UserId -OwnerWalletAddress $IssuerWalletAddress `
        -Headers $vAdmin.Headers -TenantUrl $Api -DevMode:$true
    Write-WtSuccess "cyber register: $($cyberRegister.RegisterId)"

    Write-WtStep "6c (repair): publish Assure-ID agent participant on cyber register"
    try {
        $null = Publish-SorchaParticipant -TenantUrl $Api -OrganizationId $OrganizationId -RegisterId $cyberRegister.RegisterId -ParticipantName "Assure-ID Agent" -OrganizationName $script:AiasName -WalletAddress $agentWallet.Address -PublicKey $agentWallet.PublicKey -Headers $vAdmin.Headers
        Write-WtSuccess "agent participant published (cyber, repair)"
    } catch { Write-WtWarn "cyber participant publish (repair): $($_.Exception.Message)" }

    return $cyberRegister.RegisterId
}

<#
.SYNOPSIS
    Provision the AIAS org's Feature 083 HD master key (idempotent).
.DESCRIPTION
    Thin pass-through to the discovered SorchaWalkthrough helper
    Set-SorchaOrgMasterKey (POST /api/wallets/org/{orgId}/master-key). Kept as a
    clearly-named AIAS function so the quickstart's "Set-SorchaOrgMasterKey" step
    has a single, greppable call site. Idempotent: the underlying helper swallows
    the 409 (already-provisioned) and re-throws anything else. The one-time
    recovery mnemonic in the response is intentionally discarded (demo/dev only).
#>
function Set-AiasOrgMasterKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][hashtable]$Headers
    )
    Set-SorchaOrgMasterKey -WalletUrl $WalletUrl -OrganizationId $OrganizationId -Headers $Headers
}

# ============================================================================
# Publish-AiasBlueprint — render {{issuerName}} + publish (idempotent)
# ============================================================================

<#
.SYNOPSIS
    Render and publish the AIAS Assured Identity blueprint onto the register.
.DESCRIPTION
    Mirrors step 8 of New-IssuingAuthority. Renders
    blueprints/aias-assured-identity.template.json with {{issuerName}} =
    "Acme Identity Assurance Services (AIAS)" (single source — Set-BlueprintIssuerName),
    binds the Assure-ID agent participant via WalletMap, and publishes to the
    register recorded in state. Skips when the target blueprint is already
    published and readable (idempotent). Updates state.blueprintId.
.PARAMETER StateFile
    The state record written by New-AiasOrg.
.PARAMETER Force
    Re-publish even when an already-published blueprint id is recorded.
#>
<#
.SYNOPSIS
    The set of published blueprint ids a provisioned AIAS demo must have.
.DESCRIPTION
    Issue #1269: state used to record a SINGLE scalar blueprintId, so Get-AiasDemoStatus could only
    ever validate one blueprint and was structurally unable to notice a second one missing — it
    reported blueprint=True while the device-registration workflow was absent from the register
    entirely. This reads the recorded SET, falling back to the legacy scalar so a state.json written
    before this change still validates rather than erroring.
#>
function Get-AiasExpectedBlueprintIds {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$State)

    $ids = [System.Collections.ArrayList]@()
    if (-not $State) { return @() }

    # Indexed property access, NOT `.PSObject.Properties.Name -contains`: on a sparse or empty
    # pscustomobject the latter throws "The property 'Name' cannot be found on this object", which
    # would turn a partially-written state.json into a crash instead of a NotReady verdict.
    $idsProp = $State.PSObject.Properties['blueprintIds']
    if ($idsProp -and $idsProp.Value) {
        foreach ($prop in $idsProp.Value.PSObject.Properties) {
            if ($prop.Value) { $ids.Add([string]$prop.Value) | Out-Null }
        }
    }

    # Legacy state (pre-#1269) recorded only the scalar. Include it so an older state.json is still
    # checked, and so the application workflow is covered even if blueprintIds is somehow partial.
    $scalarProp = $State.PSObject.Properties['blueprintId']
    if ($scalarProp -and $scalarProp.Value) {
        if (-not $ids.Contains([string]$scalarProp.Value)) { $ids.Add([string]$scalarProp.Value) | Out-Null }
    }

    return @($ids)
}

function Publish-AiasBlueprint {
    [CmdletBinding()]
    param(
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [switch]$Force
    )

    $state = Read-DemoState -Path $StateFile
    if (-not $state) { throw "No state.json — run New-AiasOrg first." }

    $node = Get-AiasTarget -Target ([string]$state.target)
    $api  = Get-DemoApiBase -Gateway $node.gateway
    $secrets = Import-DemoSecrets
    $pw = Get-DemoAdminPassword -Node $node -Secrets $secrets
    $vAdminEmail = "verification-admin@$script:AiasSubdomain.local"

    Write-WtBanner "AIAS demo — publish blueprint (issuerName='$script:AiasName')"

    # Connect the verification-admin up front so the idempotency probe below can authenticate the
    # register-scoped published-blueprints read (it 401s anonymously). Needed for the publish call anyway.
    $vAdmin = Connect-SorchaUser -TenantUrl $api -Email $vAdminEmail -Password $pw -OrganizationId $state.organizationId

    $recordedAppId = $state.PSObject.Properties['blueprintId'] ? [string]$state.blueprintId : $null

    # Issue #1269: the demo carries multiple templates and only ever published one. The
    # device-registration workflow (F174 / #1195 Phase 2 "Bind to device") existed as a complete,
    # publishable template that nothing in the provisioning path published — so the register reported
    # "1 workflow(s)" and the PWA's Bind-to-device button had no workflow behind it. All are
    # published here, and state records the SET (see $publishedIds below) so the readiness check can
    # no longer be structurally blind to one of them going missing.
    #
    # M2: each template now publishes to its OWN register — the identity + device-binding workflows
    # share the Assured Identity register, but the cyber questionnaire has its own (New-AiasOrg step
    # 6a). RegisterId is therefore per-spec, not a single shared $state.registerId.
    $cyberRegisterIdProp = $state.PSObject.Properties['cyberRegisterId']
    $cyberRegisterId = if ($cyberRegisterIdProp) { $cyberRegisterIdProp.Value } else { $null }

    $blueprintSpecs = @(
        @{ Key = 'assuredIdentity'
           File = 'aias-assured-identity.template.json'
           IdPrefix = 'aias-assured-identity'
           RegisterId = $state.registerId
           # The analyst is the agent; the citizen is an OPEN participant, late-bound on first
           # submission, so it must NOT appear here (VAL_BP_010).
           WalletMap = @{ "verification-analyst" = $state.agentWallet } }
        @{ Key = 'deviceRegistration'
           File = 'aias-device-registration.template.json'
           IdPrefix = 'aias-device-registration'
           RegisterId = $state.registerId
           WalletMap = @{ "aias-issuer" = $state.issuerWalletAddress } }
        @{ Key = 'cyberLevel'
           File = 'aias-cyber-level.template.json'
           IdPrefix = 'aias-cyber-level'
           RegisterId = $cyberRegisterId
           WalletMap = @{ "aias-analyst" = $state.agentWallet } }
    )

    foreach ($spec in $blueprintSpecs) {
        if (-not $spec.RegisterId) {
            throw "No register id for template '$($spec.File)' — re-run New-AiasOrg to provision the missing register."
        }
    }

    # Idempotency is decided PER SPEC below (Resolve-BlueprintPublishAction), not all-or-nothing
    # here. The old guard early-returned as soon as the SCALAR state.blueprintId was published —
    # so on any already-provisioned node it returned before the both-templates loop and the
    # device-registration blueprint stayed unpublished, which is the very blindness #1269 removed
    # from the body. Forcing past it was not a fix either: ids are timestamped, so -Force
    # republishes the application workflow under a new id and leaves a duplicate behind.
    #
    # M2: the "already published" list must be fetched PER REGISTER now — the cyber blueprint lives
    # on a different register, so a lookup against $state.registerId alone would never see it and
    # would republish it (with a new id) on every run.
    $alreadyPublishedByRegister = @{}
    if (-not $Force) {
        $distinctRegisterIds = @($blueprintSpecs | ForEach-Object { $_.RegisterId } | Select-Object -Unique)
        foreach ($rid in $distinctRegisterIds) {
            $alreadyPublishedByRegister[$rid] = @(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $rid -Headers $vAdmin.Headers)
        }
    }

    $publishedIds = @{}
    $appBlueprintId = $null
    $rendered_ai = $null
    foreach ($spec in $blueprintSpecs) {
        Write-WtStep "render $($spec.File) -> {{issuerName}} = '$script:AiasName'"
        $templateRaw = Get-Content -LiteralPath (Join-Path $script:DemoRoot "blueprints/$($spec.File)") -Raw
        # Rendered even when reusing — the agency-name coherence assertion below reads it.
        $rendered = Set-BlueprintIssuerName -BlueprintJson $templateRaw -AgencyName $script:AiasName

        $preferred = ($spec.Key -eq 'assuredIdentity') ? $recordedAppId : $null
        $alreadyPublished = if ($alreadyPublishedByRegister.ContainsKey($spec.RegisterId)) { $alreadyPublishedByRegister[$spec.RegisterId] } else { @() }
        $decision = Resolve-BlueprintPublishAction -IdPrefix $spec.IdPrefix `
            -PublishedIds $alreadyPublished -PreferredId $preferred -Force ([bool]$Force)

        if ($decision.Action -eq 'Reuse') {
            Write-WtSuccess "blueprint ($($spec.Key)): '$($decision.ExistingId)' already published — reuse"
            $publishedIds[$spec.Key] = $decision.ExistingId
        }
        else {
            $tempBp = Join-Path ([System.IO.Path]::GetTempPath()) ("{0}-{1}.json" -f $spec.IdPrefix, $script:AiasSubdomain)
            $rendered | Set-Content -LiteralPath $tempBp -Encoding UTF8

            $pubResult = Publish-SorchaBlueprint -BlueprintUrl $api -TemplatePath $tempBp -WalletMap $spec.WalletMap -Headers $vAdmin.Headers -IdPrefix $spec.IdPrefix -RegisterId $spec.RegisterId
            Remove-Item -LiteralPath $tempBp -ErrorAction SilentlyContinue
            Write-WtSuccess "blueprint ($($spec.Key)): $($pubResult.BlueprintId)"
            $publishedIds[$spec.Key] = $pubResult.BlueprintId
        }

        # The assured-identity blueprint stays the one recorded as state.blueprintId — rehearse.ps1
        # and run-demo.ps1 read that scalar as "the application workflow". Taken from the decision
        # so a REUSED id is preserved verbatim rather than nulled out.
        if ($spec.Key -eq 'assuredIdentity') { $appBlueprintId = $publishedIds[$spec.Key]; $rendered_ai = $rendered }
    }
    $rendered = $rendered_ai

    # Publishing is async: the blueprint's publish transaction must SEAL into a docket (a few seconds)
    # before it appears in the register's /blueprints/published list. Wait for it here — otherwise the
    # immediately-following Get-AiasDemoStatus reads before the seal and reports a false-negative
    # 'blueprint-not-published' (NotReady) even though the publish succeeded. Mirrors the participant
    # seal-wait; bounded so a genuinely-stuck seal still surfaces rather than hanging.
    # Issue #1269: wait for EVERY published id, not just the application workflow — otherwise a
    # second blueprint that never seals passes unnoticed, which is the blindness being removed.
    # M2: sealing is tracked PER REGISTER — each id is only ever checked against the register it was
    # actually published to, since the cyber blueprint seals on the cyber register, not the identity one.
    $sealDeadline = (Get-Date).AddSeconds(90)
    $awaitingByRegister = @{}
    foreach ($spec in $blueprintSpecs) {
        $id = $publishedIds[$spec.Key]
        if (-not $id) { continue }
        if (-not $awaitingByRegister.ContainsKey($spec.RegisterId)) {
            $awaitingByRegister[$spec.RegisterId] = [System.Collections.ArrayList]@()
        }
        $awaitingByRegister[$spec.RegisterId].Add($id) | Out-Null
    }
    $countAwaiting = { ($awaitingByRegister.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum }
    while ((Get-Date) -lt $sealDeadline -and (& $countAwaiting) -gt 0) {
        foreach ($rid in @($awaitingByRegister.Keys)) {
            $pending = $awaitingByRegister[$rid]
            if ($pending.Count -eq 0) { continue }
            $pubNow = @(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $rid -Headers $vAdmin.Headers)
            foreach ($id in @($pending)) {
                if ($pubNow -contains $id) { $pending.Remove($id) | Out-Null }
            }
        }
        if ((& $countAwaiting) -gt 0) { Start-Sleep -Seconds 3 }
    }
    if ((& $countAwaiting) -eq 0) {
        Write-WtSuccess "all $($publishedIds.Count) blueprint(s) sealed + visible in their register's published list"
    }
    else {
        $stillAwaiting = @($awaitingByRegister.Values | ForEach-Object { $_ })
        Write-WtWarn "blueprint(s) not visible in /blueprints/published after 90s (may still be sealing): $($stillAwaiting -join ', ')"
    }

    # coherence assertion (single-source name across org/register/participant/blueprint)
    $coherence = Test-AgencyNameCoherence -AgencyName $script:AiasName -OrgName $script:AiasName -RegisterName $script:AiasName -ParticipantOrg $script:AiasName -BlueprintJson $rendered
    if (-not $coherence.Coherent) { Write-WtWarn "agency-name coherence issues: $($coherence.Mismatches -join '; ')" }

    # blueprintId stays for back-compat (rehearse.ps1 / run-demo.ps1 read it as the application
    # workflow); blueprintIds is the authoritative SET the readiness check verifies (#1269).
    $merged = Merge-DemoState -Existing $state -Updates @{
        blueprintId  = $appBlueprintId
        blueprintIds = $publishedIds
    }
    Write-DemoState -State $merged -Path $StateFile | Out-Null
    return (Read-DemoState -Path $StateFile)
}

# ============================================================================
# Start-AiasAgent — generate runtime config + launch the Assure-ID agent
# ============================================================================

<#
.SYNOPSIS
    Generate demos/AIAS/agent/{assure-id|cyber}.config.json and launch that agent.
.DESCRIPTION
    The sorcha-agent ActorDefinition loader requires the rules INLINE under a
    "rules" property (plus actor/connection/inbox/mode="rules"), and supports an
    optional "checksFile" (relative to the config) for the external-check hook.
    This function:
      1. Reads the BARE rule array from agent/{mode}.rules.json,
      2. Embeds it under "rules" in an ActorDefinition whose connection tokens are
         filled from the provisioned state (gateway/registerId/orgId/agentWallet)
         with credentials drawn from $env:AGENT_EMAIL / $env:AGENT_PASSWORD,
      3. Sets "checksFile" to the mode's checks file (relative — checks + ../fixtures
         sit next to the generated config so relative resolution works),
      4. Writes agent/{mode}.config.json,
      5. Launches: sorcha-agent run --config <config> --state <state.json>.
    Mirrors Start-DemoAgent + Start-ApprovalAgent (demos/AssuredIdentity). When
    sorcha-agent is not on PATH it writes the config and prints the manual command
    (non-fatal) so provisioning still succeeds.
.PARAMETER StateFile
    The state record (also passed to sorcha-agent for placeholder resolution).
.PARAMETER Mode
    'assure-id' (default) or 'cyber' (M2). Selects which agent to launch; process
    identity is recorded under mode-prefixed state keys (agentPid vs
    cyberAgentPid, etc.) so the two tracked agents never clobber each other.
#>
function Start-AiasAgent {
    [CmdletBinding()]
    param(
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [ValidateSet('assure-id', 'cyber')]
        [string]$Mode = 'assure-id'
    )

    $state = Read-DemoState -Path $StateFile
    if (-not $state) { throw "No state.json — run New-AiasOrg first." }

    $agentLabel = if ($Mode -eq 'cyber') { 'Cyber' } else { 'Assure-ID' }

    # Idempotent: Initialize-AiasDemo may call this for the same mode more than once (re-run after
    # a partial provisioning retry, or simply re-running the one-shot orchestrator) and must never
    # spawn a second copy of an agent that is already running. Test-AiasAgentAlive confirms process
    # IDENTITY (name + start time), not just a recorded PID — PIDs recycle, so a bare "is there a
    # number" check would treat an unrelated process that inherited the old PID as the live agent.
    if (Test-AiasAgentAlive -State $state -Mode $Mode) {
        $pidKey        = if ($Mode -eq 'cyber') { 'cyberAgentPid' }        else { 'agentPid' }
        $configPathKey = if ($Mode -eq 'cyber') { 'cyberAgentConfigPath' } else { 'agentConfigPath' }
        $logPathKey    = if ($Mode -eq 'cyber') { 'cyberAgentLogPath' }    else { 'agentLogPath' }
        $existingPid = $state.PSObject.Properties[$pidKey].Value
        Write-WtSuccess "$agentLabel agent already running (pid $existingPid) — not relaunching"
        $configPathProp = $state.PSObject.Properties[$configPathKey]
        $logPathProp    = $state.PSObject.Properties[$logPathKey]
        return [pscustomobject]@{
            Process    = (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)
            ConfigPath = if ($configPathProp) { $configPathProp.Value } else { $null }
            LogPath    = if ($logPathProp) { $logPathProp.Value } else { $null }
        }
    }

    # M2: each mode expects its own published blueprint. Indexed PSObject.Properties['x'] access
    # throughout — 'blueprintIds' (and the cyberLevel key within it) may not exist on an older
    # state.json, and direct property access would throw under Set-StrictMode rather than degrading
    # to a clean warning.
    $expectedBlueprintKey = if ($Mode -eq 'cyber') { 'cyberLevel' } else { 'assuredIdentity' }
    $blueprintProp = $state.PSObject.Properties['blueprintIds']
    $hasBlueprint = $blueprintProp -and $blueprintProp.Value -and
                    $blueprintProp.Value.PSObject.Properties[$expectedBlueprintKey]
    if (-not $hasBlueprint) {
        Write-WtWarn "state has no '$expectedBlueprintKey' blueprint id — publish first (Publish-AiasBlueprint)."
    }

    $node = Get-AiasTarget -Target ([string]$state.target)
    $secrets = Import-DemoSecrets

    Write-WtBanner "AIAS demo — launch $agentLabel agent (rules)"

    # analyst (agent) credentials are threaded via env, exactly like AssuredIdentity
    $env:AGENT_EMAIL = $state.agentEmail
    $env:AGENT_PASSWORD = (Get-DemoAdminPassword -Node $node -Secrets $secrets)

    $configPath = Build-AiasAgentConfig -State $state -Node $node -Mode $Mode
    $checksName = if ($Mode -eq 'cyber') { 'cyber.checks.json' } else { 'assure-id.checks.json' }
    Write-WtSuccess "agent config: $configPath (checksFile=$checksName)"

    Write-WtStep "launching sorcha-agent (rules mode)"
    $agentCmd = Get-Command 'sorcha-agent' -ErrorAction SilentlyContinue
    if (-not $agentCmd) {
        Write-WtWarn "sorcha-agent not found on PATH. Config written; start it manually:"
        Write-WtInfo  "  sorcha-agent run --config `"$configPath`" --state `"$StateFile`""
        return [pscustomobject]@{ Process = $null; ConfigPath = $configPath }
    }

    # Detached, with durable logs.
    #
    # Context: the tracked agent was found dead on 2026-07-25 having been started 07-23. The machine
    # had not rebooted, no .NET fault was logged, and Reset-AiasDemo (the only code path that stops
    # it) had not run — its state.json and rendered config were both still present. The cause could
    # not be established, because this function previously launched with -NoNewWindow and NO stream
    # redirection: everything the agent said went to the caller's console and was gone with it.
    #
    # That absence of evidence is the defect being fixed here. A component that sits watching for
    # citizen applications and decides approve/reject must leave a trail.
    #
    # -NoNewWindow also attaches the child to the caller's console, so a real console window being
    # closed would deliver CTRL_CLOSE_EVENT to the agent and take it down. That is the leading
    # explanation for the disappearance but it is NOT proven: an A/B of the old and new patterns
    # across separate non-interactive shell sessions saw BOTH survive, so the console-close path was
    # never reproduced. Redirecting both streams removes the console entirely, which closes that
    # route regardless — and next time the log will say what actually happened.
    #
    # Logs are per-launch so an earlier run's trail is never overwritten. Per-mode prefix so the
    # Assure-ID and Cyber agents' logs are never interleaved under the same filename stem.
    $logDir = Join-Path $script:DemoRoot 'logs'
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $logPrefix = if ($Mode -eq 'cyber') { 'cyber-agent' } else { 'agent' }
    $outLog = Join-Path $logDir "$logPrefix-$stamp.log"
    $errLog = Join-Path $logDir "$logPrefix-$stamp.err.log"

    $proc = Start-Process -FilePath $agentCmd.Source `
        -ArgumentList @('run', '--config', $configPath, '--state', $StateFile) `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    Write-WtSuccess "sorcha-agent started (pid $($proc.Id)) — detached"
    Write-WtInfo    "  log: $outLog"
    Write-WtInfo    "  err: $errLog"

    # Record process IDENTITY, not just the number. A PID alone cannot answer "is the agent still
    # running?" — PIDs recycle, so an unrelated process inheriting the number reads as a live agent.
    $startedAt = $null
    try { $startedAt = $proc.StartTime.ToUniversalTime().ToString('o') } catch { }

    # M2: key the tracked-process bookkeeping off the mode so the Assure-ID and Cyber agents are
    # tracked independently in state — 'agentPid'/'agentConfigPath'/etc for assure-id (unchanged,
    # back-compat with existing state.json / rehearse.ps1 reads), 'cyberAgentPid'/'cyberAgentConfigPath'/etc
    # for cyber.
    $pidKey          = if ($Mode -eq 'cyber') { 'cyberAgentPid' }          else { 'agentPid' }
    $configPathKey   = if ($Mode -eq 'cyber') { 'cyberAgentConfigPath' }   else { 'agentConfigPath' }
    $processNameKey  = if ($Mode -eq 'cyber') { 'cyberAgentProcessName' }  else { 'agentProcessName' }
    $startedAtKey    = if ($Mode -eq 'cyber') { 'cyberAgentStartedAt' }    else { 'agentStartedAt' }
    $logPathKey      = if ($Mode -eq 'cyber') { 'cyberAgentLogPath' }      else { 'agentLogPath' }
    $errLogPathKey   = if ($Mode -eq 'cyber') { 'cyberAgentErrorLogPath' } else { 'agentErrorLogPath' }

    $updates = @{}
    $updates[$pidKey]         = $proc.Id
    $updates[$configPathKey]  = $configPath
    $updates[$processNameKey] = $proc.ProcessName
    $updates[$startedAtKey]   = $startedAt
    $updates[$logPathKey]     = $outLog
    $updates[$errLogPathKey]  = $errLog

    $merged = Merge-DemoState -Existing $state -Updates $updates
    Write-DemoState -State $merged -Path $StateFile | Out-Null
    return [pscustomobject]@{ Process = $proc; ConfigPath = $configPath; LogPath = $outLog }
}

<#
.SYNOPSIS
    Is the agent recorded in state actually running?
.DESCRIPTION
    Confirms process identity rather than trusting the recorded PID. PIDs recycle: after the
    tracked agent dies, any later process can be handed the same number, and a bare
    `Get-Process -Id` then reports a dead agent as healthy. So the name must match, and the start
    time must match the one captured at launch (within a small tolerance for clock/format rounding).

    Returns $false — never throws — when state is missing, incomplete, or the process is gone, so
    callers can use it directly in a boolean context.
.PARAMETER Mode
    'assure-id' (default) or 'cyber' (M2). Selects which agent's mode-prefixed state keys to read
    (agentPid/agentProcessName/agentStartedAt vs cyberAgentPid/cyberAgentProcessName/
    cyberAgentStartedAt — see Start-AiasAgent) so the two tracked agents are never conflated.
#>
function Test-AiasAgentAlive {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowNull()]$State,
        [ValidateSet('assure-id', 'cyber')]
        [string]$Mode = 'assure-id'
    )

    if (-not $State) { return $false }

    # Indexed PSObject.Properties['x'] access throughout — a state.json written before M2 (or
    # before either agent has ever been started in this mode) simply lacks these keys, and direct
    # property access would throw under Set-StrictMode rather than degrading to "not alive".
    $pidKey         = if ($Mode -eq 'cyber') { 'cyberAgentPid' }          else { 'agentPid' }
    $processNameKey = if ($Mode -eq 'cyber') { 'cyberAgentProcessName' }  else { 'agentProcessName' }
    $startedAtKey   = if ($Mode -eq 'cyber') { 'cyberAgentStartedAt' }    else { 'agentStartedAt' }

    $pidProp = $State.PSObject.Properties[$pidKey]
    if (-not $pidProp -or -not $pidProp.Value) { return $false }

    $proc = Get-Process -Id $pidProp.Value -ErrorAction SilentlyContinue
    if (-not $proc) { return $false }

    $processNameProp = $State.PSObject.Properties[$processNameKey]
    if ($processNameProp -and $processNameProp.Value `
        -and $proc.ProcessName -ne $processNameProp.Value) {
        return $false
    }

    $startedAtProp = $State.PSObject.Properties[$startedAtKey]
    if ($startedAtProp -and $startedAtProp.Value) {
        # ConvertFrom-Json coerces an ISO-8601 value into a [DateTime] (Kind=Utc), so the recorded
        # value arrives here as EITHER a DateTime or a string depending on how the caller loaded
        # state. Handle both explicitly.
        #
        # Do not collapse this to [DateTime]::Parse($startedAtProp.Value): when the value is
        # already a DateTime, PowerShell stringifies it as MM/dd/yyyy before parsing, which THROWS
        # under a non-US culture (en-GB reads month 25). The catch below then swallowed it and this
        # whole check silently degraded to the name match — present in the source, inert at runtime.
        $recorded = $null
        try {
            $recorded = if ($startedAtProp.Value -is [DateTime]) {
                ([DateTime]$startedAtProp.Value).ToUniversalTime()
            }
            else {
                [DateTime]::Parse(
                    [string]$startedAtProp.Value,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
            }
        }
        catch {
            # Unreadable timestamp: fall open to the name match already made above rather than
            # declaring a live agent dead.
            $recorded = $null
        }

        if ($null -ne $recorded `
            -and [Math]::Abs((($proc.StartTime.ToUniversalTime()) - $recorded).TotalSeconds) -gt 5) {
            return $false
        }
    }

    return $true
}

<#
.SYNOPSIS
    Build the runtime ActorDefinition config (rules inline + checksFile) for an
    AIAS agent and write it to agent/{assure-id|cyber}.config.json. Returns the path.
.DESCRIPTION
    Pure assembly + IO. Reads the bare rule array from the mode's rules file and
    embeds it under "rules"; connection tokens are filled from the provisioned
    state; credentials reference $env:AGENT_EMAIL / $env:AGENT_PASSWORD (resolved
    by the agent at runtime, never written to disk — mirrors
    analyst.rules.template.json). The config is written into demos/AIAS/agent/ so
    the relative "checksFile" and "../fixtures/postcodes.offline.json" resolve.
.PARAMETER Mode
    'assure-id' (default) or 'cyber'. M2: the two modes are FULLY independent —
    actor identity, description, rules file, checks file, register id, and output
    config path are all selected off this. Sharing any one of those between modes
    is a silent-misbehaviour risk, and sharing the config PATH is a live-demo
    footgun: a running agent holds its config in memory, so overwriting the
    other mode's file on disk only surfaces on that agent's next restart.
#>
function Build-AiasAgentConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][object]$Node,
        [ValidateSet('assure-id', 'cyber')]
        [string]$Mode = 'assure-id'
    )
    $agentDir = Join-Path $script:DemoRoot "agent"

    # Indexed PSObject.Properties['x'] access for cyberRegisterId — it is a NEW state field (M2), so
    # a state.json written before this change (or before New-AiasOrg's cyber-register step ran) won't
    # have it. Direct `$State.cyberRegisterId` would throw under Set-StrictMode on such a state.
    $cyberRegisterIdProp = $State.PSObject.Properties['cyberRegisterId']

    $agentFiles = if ($Mode -eq 'cyber') {
        @{
            ActorName        = 'cyber-agent'
            ActorDescription = "Autonomous AIAS Cyber agent — scores a cyber-hygiene questionnaire (password habits, 2FA, update discipline, phishing awareness, password sharing) into a Bronze/Silver/Gold/Platinum level band, and rejects outright when the presented credential carries no portrait (rules mode)."
            Rules            = 'cyber.rules.json'
            Checks           = 'cyber.checks.json'
            Config           = 'cyber.config.json'
            RegisterId       = if ($cyberRegisterIdProp) { $cyberRegisterIdProp.Value } else { $null }
        }
    }
    else {
        @{
            ActorName        = 'assure-id-agent'
            ActorDescription = "Autonomous AIAS Assure-ID agent — evaluates external checks (email verified, postcode exists, no profanity) and approves or rejects with an on-brand reason (rules mode)."
            Rules            = 'assure-id.rules.json'
            Checks           = 'assure-id.checks.json'
            Config           = 'assure-id.config.json'
            RegisterId       = $State.registerId
        }
    }

    if (-not $agentFiles.RegisterId) {
        throw "State has no register id for mode '$Mode' — run New-AiasOrg first (cyber mode needs state.cyberRegisterId)."
    }

    $rulesPath = Join-Path $agentDir $agentFiles.Rules
    if (-not (Test-Path -LiteralPath $rulesPath)) { throw "Bare rules not found: $rulesPath" }

    # Depth 30 so nested JSON-Logic conditions survive the round-trip.
    $rules = Get-Content -LiteralPath $rulesPath -Raw | ConvertFrom-Json -Depth 30

    $config = [ordered]@{
        actor = [ordered]@{
            name        = $agentFiles.ActorName
            description = $agentFiles.ActorDescription
        }
        connection = [ordered]@{
            gatewayUrl = $Node.gateway
            registerId = $agentFiles.RegisterId
            credentials = [ordered]@{
                email          = '$env:AGENT_EMAIL'
                password       = '$env:AGENT_PASSWORD'
                organizationId = $State.organizationId
            }
            walletAddress = $State.agentWallet
        }
        inbox = [ordered]@{
            signalR = [ordered]@{ enabled = $true }
            polling = [ordered]@{ enabled = $true; intervalSeconds = 15 }
        }
        mode       = "rules"
        checksFile = $agentFiles.Checks
        rules      = @($rules)
    }

    $outPath = Join-Path $agentDir $agentFiles.Config
    ($config | ConvertTo-Json -Depth 30) | Set-Content -LiteralPath $outPath -Encoding UTF8
    return $outPath
}

# ============================================================================
# Initialize-AiasDemo — one-shot idempotent provisioning + agent launch
# ============================================================================

<#
.SYNOPSIS
    Run the full idempotent AIAS provisioning + agent launch in one call.
.DESCRIPTION
    org -> master key -> blueprint -> agent config + launch. Safe to re-run after
    a network wipe (FR-010, SC-001). This is what run-demo.ps1 calls.
.PARAMETER Target
    'docker' (default) or 'n1'.
.PARAMETER Force
    Force recreate org + republish blueprint.
#>
function Initialize-AiasDemo {
    [CmdletBinding()]
    param(
        [ValidateSet('docker', 'n1')][string]$Target = 'docker',
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [switch]$Force
    )

    Write-WtBanner "AIAS Assured Identity demo (Feature 174 / M1) — provision ($Target)"
    $node = Get-AiasTarget -Target $Target
    if (-not (Test-AiasGatewayHealthy -Gateway $node.gateway)) {
        Write-WtWarn "Gateway $($node.gateway) did not answer a health probe — is the stack up (docker-compose up -d)?"
    }

    $null = New-AiasOrg -Target $Target -StateFile $StateFile -Force:$Force.IsPresent
    $null = Publish-AiasBlueprint -StateFile $StateFile -Force:$Force.IsPresent

    # M2: launch BOTH agents. Shipped without this, the cyber register + blueprint are provisioned
    # with nothing to service them — a citizen's questionnaire submission sits unscored forever.
    # Start-AiasAgent is idempotent per mode (Test-AiasAgentAlive gate), so re-running
    # Initialize-AiasDemo never spawns a duplicate of either agent.
    $agent = Start-AiasAgent -StateFile $StateFile -Mode 'assure-id'
    $cyberAgent = Start-AiasAgent -StateFile $StateFile -Mode 'cyber'

    $state = Read-DemoState -Path $StateFile
    $cyberRegisterIdProp = $state.PSObject.Properties['cyberRegisterId']
    Write-WtBanner "AIAS READY — org=$($state.organizationId) register=$($state.registerId) blueprint=$($state.blueprintId)"
    return [pscustomobject]@{
        target            = $Target
        organizationId    = $state.organizationId
        registerId        = $state.registerId
        cyberRegisterId   = if ($cyberRegisterIdProp) { $cyberRegisterIdProp.Value } else { $null }
        blueprintId       = $state.blueprintId
        agentRunning      = [bool]($agent -and $agent.Process)
        agentConfig       = $agent.ConfigPath
        cyberAgentRunning = [bool]($cyberAgent -and $cyberAgent.Process)
        cyberAgentConfig  = $cyberAgent.ConfigPath
    }
}

# ============================================================================
# Get-AiasDemoStatus — at-a-glance readiness
# ============================================================================

<#
.SYNOPSIS
    Report AIAS demo readiness: gateway reachable, register readable, blueprint
    published, agent process alive. Returns { Verdict; Reasons; Detail }.
#>
function Get-AiasDemoStatus {
    [CmdletBinding()]
    param([string]$StateFile = (Join-Path $script:DemoRoot "state.json"))

    $state = Read-DemoState -Path $StateFile
    if (-not $state) {
        Write-WtWarn "No state.json — AIAS not provisioned."
        return [pscustomobject]@{ Verdict = 'NotReady'; Reasons = @('not-provisioned') }
    }

    $node    = Get-AiasTarget -Target ([string]$state.target)
    $api     = Get-DemoApiBase -Gateway $node.gateway
    $secrets = Import-DemoSecrets

    $reasons = @()
    $gatewayHealthy = Test-AiasGatewayHealthy -Gateway $node.gateway
    if (-not $gatewayHealthy) { $reasons += 'gateway-unreachable' }

    $registerReadable = $false
    $blueprintPublished = $false
    $adminOk = $false
    try {
        $admin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
        $adminOk = $true
        $registerReadable = Test-AiasRegisterReadable -Api $api -RegisterId $state.registerId -Headers $admin.Headers
    } catch { }
    if (-not $registerReadable) { $reasons += 'register-not-readable' }

    # M2: the cyber questionnaire lives on its own register (New-AiasOrg step 6a) — probe it
    # independently so a missing or unsealed cyber register reports NotReady with a named cause
    # rather than surfacing later as a publish failure. Indexed PSObject.Properties['x'] access, NOT
    # `.Properties.Name -contains` — the latter throws on a sparse/empty pscustomobject, turning a
    # partially-written state.json into a crash instead of a clean NotReady verdict (the trap #1269
    # already fixed once).
    $cyberRegisterReadable = $false
    $cyberProp = $state.PSObject.Properties['cyberRegisterId']
    if (-not $cyberProp -or -not $cyberProp.Value) {
        $reasons += 'cyber-register-missing'
    }
    elseif ($adminOk) {
        $cyberRegisterReadable = Test-AiasRegisterReadable -Api $api -RegisterId $cyberProp.Value -Headers $admin.Headers
        if (-not $cyberRegisterReadable) { $reasons += 'cyber-register-not-readable' }
    }
    else {
        # Admin login itself failed above (already surfaced via 'register-not-readable' / gateway
        # reasons) — the cyber register can't be probed either without a session.
        $reasons += 'cyber-register-not-readable'
    }

    # Issue #1269: verify EVERY expected blueprint, not just the application workflow. The previous
    # single-id check reported blueprint=True while the device-registration workflow was missing from
    # the register altogether.
    # M2: the cyber-level blueprint lives on the SEPARATE cyber register, so it must be looked up
    # there too — otherwise it always reads as missing even once published (it never appears in the
    # identity register's published list).
    $expectedBlueprintIds = @(Get-AiasExpectedBlueprintIds -State $state)
    $missingBlueprintIds = @()
    if ($registerReadable -and $expectedBlueprintIds.Count -gt 0) {
        $publishedNow = @(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $state.registerId -Headers $admin.Headers)
        if ($cyberProp -and $cyberProp.Value) {
            $publishedNow += @(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $cyberProp.Value -Headers $admin.Headers)
        }
        $missingBlueprintIds = @($expectedBlueprintIds | Where-Object { $publishedNow -notcontains $_ })
        $blueprintPublished = ($missingBlueprintIds.Count -eq 0)
    }
    if (-not $blueprintPublished) {
        $reasons += 'blueprint-not-published'
        if ($missingBlueprintIds.Count -gt 0) { Write-WtInfo "missing blueprint(s): $($missingBlueprintIds -join ', ')" }
        if ($expectedBlueprintIds.Count -eq 0) { Write-WtInfo "state records no blueprint ids — run Publish-AiasBlueprint" }
    }

    $agentRunning = Test-AiasAgentAlive -State $state
    if (-not $agentRunning) {
        $reasons += 'agent-not-running'
        if ($state.PSObject.Properties.Name -contains 'agentLogPath' -and $state.agentLogPath) {
            Write-WtInfo "last agent log: $($state.agentLogPath)"
        }
    }

    # M2: the cyber questionnaire is scored by its own agent process (Start-AiasAgent -Mode cyber) —
    # check it independently, same as the cyber register above. Without this, a cyber agent that
    # never started (e.g. Start-AiasAgent's early PATH-check return, which exits before the
    # mode-prefixed state write) went unnoticed: run-demo.ps1 gates its exit code purely on Verdict,
    # so a citizen's cyber-questionnaire submission could sit unscored forever behind a green banner.
    $cyberAgentRunning = Test-AiasAgentAlive -State $state -Mode 'cyber'
    if (-not $cyberAgentRunning) { $reasons += 'cyber-agent-not-running' }

    $verdict = if ($reasons.Count -eq 0) { 'Ready' } else { 'NotReady' }
    Write-WtBanner "AIAS status: $verdict ($($node.id))"
    Write-WtInfo "gateway=$gatewayHealthy register=$registerReadable cyberRegister=$cyberRegisterReadable blueprint=$blueprintPublished ($($expectedBlueprintIds.Count) expected) agent=$agentRunning cyberAgent=$cyberAgentRunning"
    if ($reasons.Count -gt 0) { Write-WtInfo "reasons: $($reasons -join ', ')" }

    return [pscustomobject]@{
        Verdict = $verdict
        Reasons = $reasons
        Detail  = [pscustomobject]@{
            GatewayHealthy         = $gatewayHealthy
            RegisterReadable       = $registerReadable
            CyberRegisterReadable  = $cyberRegisterReadable
            BlueprintPublished     = $blueprintPublished
            AgentRunning           = $agentRunning
            CyberAgentRunning      = $cyberAgentRunning
        }
    }
}

# ============================================================================
# Reset-AiasDemo — local cleanup (stops agent, clears state + rendered config)
# ============================================================================

<#
.SYNOPSIS
    Stop the tracked Assure-ID agent and clear local AIAS demo state.
.DESCRIPTION
    Mirrors Reset-Demo (demos/AssuredIdentity): a LOCAL reset — it stops the
    tracked agent process and removes state.json + the generated agent config. A
    full server-side DB wipe (org, register Mongo DBs, wallets) is a node-side
    operation (docker compose down -v, or the n1 reset recipe); the command
    prints a reminder. Re-run Initialize-AiasDemo afterwards to re-provision.
#>
function Reset-AiasDemo {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$StateFile = (Join-Path $script:DemoRoot "state.json"))

    Write-WtBanner "AIAS demo — reset (local)"
    $removed = @()

    $state = Read-DemoState -Path $StateFile

    # M2: stop BOTH tracked agents. A surviving cyber agent left running after a re-provision keeps
    # polling its (possibly now-stale) register — a confusing hazard on a demo that gets
    # re-provisioned repeatedly, not just untidiness. Indexed PSObject.Properties['x'] access,
    # consistent with the rest of this module's optional-field reads.
    foreach ($mode in @('assure-id', 'cyber')) {
        $pidKey = if ($mode -eq 'cyber') { 'cyberAgentPid' } else { 'agentPid' }
        $agentLabel = if ($mode -eq 'cyber') { 'cyber-agent' } else { 'agent' }

        $pidProp = if ($state) { $state.PSObject.Properties[$pidKey] } else { $null }
        if (-not $pidProp -or -not $pidProp.Value) { continue }

        # Identity-checked: without this a recycled PID means Stop-Process -Force kills an
        # unrelated process that merely inherited the number.
        $p = if (Test-AiasAgentAlive -State $state -Mode $mode) {
            Get-Process -Id $pidProp.Value -ErrorAction SilentlyContinue
        } else { $null }
        if ($p -and $PSCmdlet.ShouldProcess("sorcha-agent pid $($pidProp.Value) ($mode)", "Stop")) {
            Stop-Process -Id $pidProp.Value -Force -ErrorAction SilentlyContinue
            $removed += "$agentLabel(pid $($pidProp.Value))"
        }
    }

    if ($PSCmdlet.ShouldProcess($StateFile, "Remove demo state + rendered agent config")) {
        Remove-Item -LiteralPath $StateFile -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $script:DemoRoot "agent/assure-id.config.json") -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $script:DemoRoot "agent/cyber.config.json") -ErrorAction SilentlyContinue
        $removed += "state.json", "assure-id.config.json", "cyber.config.json"
    }

    Write-WtInfo "NOTE: a full server-side wipe (org, register Mongo DBs, demo wallets) is node-side."
    Write-WtInfo "      Docker: docker compose down -v. n1: the documented reset recipe (network-bootstrap skill)."
    Write-WtSuccess "reset removed: $($removed -join ', ')"
    return [pscustomobject]@{ removed = $removed }
}

Export-ModuleMember -Function `
    New-AiasOrg, Set-AiasOrgMasterKey, Publish-AiasBlueprint, Start-AiasAgent, `
    Build-AiasAgentConfig, Initialize-AiasDemo, Get-AiasDemoStatus, Reset-AiasDemo, `
    Test-AiasAgentAlive, Get-AiasExpectedBlueprintIds

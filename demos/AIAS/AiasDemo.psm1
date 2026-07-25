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

# --- well-known ids / constants ---------------------------------------------
$script:PublicOrgId  = "00000000-0000-0000-0000-000000000002"
$script:AiasName     = "Acme Identity Assurance Services"
$script:AiasSubdomain = "aias"

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
        return $existing
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

    Write-WtStep "6b: publish Assure-ID agent participant on register"
    try {
        $null = Publish-SorchaParticipant -TenantUrl $api -OrganizationId $vOrgId -RegisterId $register.RegisterId -ParticipantName "Assure-ID Agent" -OrganizationName $script:AiasName -WalletAddress $agentWallet.Address -PublicKey $agentWallet.PublicKey -Headers $vAdmin.Headers
        Write-WtSuccess "agent participant published"
    } catch { Write-WtWarn "participant publish: $($_.Exception.Message)" }

    $state = @{
        target              = $Target
        gateway             = $node.gateway
        agencyName          = $script:AiasName
        organizationId      = $vOrgId
        issuerWalletAddress = $vWallet.Address
        agentEmail          = $agentEmail
        agentWallet         = $agentWallet.Address
        registerId          = $register.RegisterId
        blueprintId         = $null
        agentMode           = 'rules'
    }
    Write-DemoState -State $state -Path $StateFile | Out-Null
    Write-WtSuccess "AIAS org provisioned — state recorded"
    return (Read-DemoState -Path $StateFile)
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

    # idempotency: already published + recorded?
    if (-not $Force -and $state.blueprintId) {
        $pub = @(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $state.registerId -Headers $vAdmin.Headers)
        if ($pub -contains $state.blueprintId) {
            Write-WtSuccess "blueprint '$($state.blueprintId)' already published — reuse"
            return $state
        }
    }

    Write-WtStep "render template -> {{issuerName}} = '$script:AiasName'"
    $templateRaw = Get-Content -LiteralPath (Join-Path $script:DemoRoot "blueprints/aias-assured-identity.template.json") -Raw
    $rendered = Set-BlueprintIssuerName -BlueprintJson $templateRaw -AgencyName $script:AiasName
    $tempBp = Join-Path ([System.IO.Path]::GetTempPath()) ("aias-assured-identity-{0}.json" -f $script:AiasSubdomain)
    $rendered | Set-Content -LiteralPath $tempBp -Encoding UTF8

    $walletMap = @{ "verification-analyst" = $state.agentWallet }
    $blueprint = Publish-SorchaBlueprint -BlueprintUrl $api -TemplatePath $tempBp -WalletMap $walletMap -Headers $vAdmin.Headers -IdPrefix "aias-assured-identity" -RegisterId $state.registerId
    Remove-Item -LiteralPath $tempBp -ErrorAction SilentlyContinue
    Write-WtSuccess "blueprint: $($blueprint.BlueprintId)"

    # Publishing is async: the blueprint's publish transaction must SEAL into a docket (a few seconds)
    # before it appears in the register's /blueprints/published list. Wait for it here — otherwise the
    # immediately-following Get-AiasDemoStatus reads before the seal and reports a false-negative
    # 'blueprint-not-published' (NotReady) even though the publish succeeded. Mirrors the participant
    # seal-wait; bounded so a genuinely-stuck seal still surfaces rather than hanging.
    $bpId = $blueprint.BlueprintId
    $sealDeadline = (Get-Date).AddSeconds(90)
    $bpSealed = $false
    while ((Get-Date) -lt $sealDeadline) {
        if (@(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $state.registerId -Headers $vAdmin.Headers) -contains $bpId) { $bpSealed = $true; break }
        Start-Sleep -Seconds 3
    }
    if ($bpSealed) { Write-WtSuccess "blueprint sealed + visible in register's published list" }
    else { Write-WtWarn "blueprint '$bpId' published but not visible in /blueprints/published after 90s — it may still be sealing" }

    # coherence assertion (single-source name across org/register/participant/blueprint)
    $coherence = Test-AgencyNameCoherence -AgencyName $script:AiasName -OrgName $script:AiasName -RegisterName $script:AiasName -ParticipantOrg $script:AiasName -BlueprintJson $rendered
    if (-not $coherence.Coherent) { Write-WtWarn "agency-name coherence issues: $($coherence.Mismatches -join '; ')" }

    $merged = Merge-DemoState -Existing $state -Updates @{ blueprintId = $blueprint.BlueprintId }
    Write-DemoState -State $merged -Path $StateFile | Out-Null
    return (Read-DemoState -Path $StateFile)
}

# ============================================================================
# Start-AiasAgent — generate runtime config + launch the Assure-ID agent
# ============================================================================

<#
.SYNOPSIS
    Generate demos/AIAS/agent/assure-id.config.json and launch the Assure-ID agent.
.DESCRIPTION
    The sorcha-agent ActorDefinition loader requires the rules INLINE under a
    "rules" property (plus actor/connection/inbox/mode="rules"), and supports an
    optional "checksFile" (relative to the config) for the external-check hook.
    This function:
      1. Reads the BARE rule array from agent/assure-id.rules.json,
      2. Embeds it under "rules" in an ActorDefinition whose connection tokens are
         filled from the provisioned state (gateway/registerId/orgId/agentWallet)
         with credentials drawn from $env:AGENT_EMAIL / $env:AGENT_PASSWORD,
      3. Sets "checksFile": "assure-id.checks.json" (relative — checks + ../fixtures
         sit next to the generated config so relative resolution works),
      4. Writes agent/assure-id.config.json,
      5. Launches: sorcha-agent run --config <config> --state <state.json>.
    Mirrors Start-DemoAgent + Start-ApprovalAgent (demos/AssuredIdentity). When
    sorcha-agent is not on PATH it writes the config and prints the manual command
    (non-fatal) so provisioning still succeeds.
.PARAMETER StateFile
    The state record (also passed to sorcha-agent for placeholder resolution).
#>
function Start-AiasAgent {
    [CmdletBinding()]
    param(
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json")
    )

    $state = Read-DemoState -Path $StateFile
    if (-not $state) { throw "No state.json — run New-AiasOrg first." }
    if (-not $state.blueprintId) { Write-WtWarn "state has no blueprintId — publish the blueprint first (Publish-AiasBlueprint)." }

    $node = Get-AiasTarget -Target ([string]$state.target)
    $secrets = Import-DemoSecrets

    Write-WtBanner "AIAS demo — launch Assure-ID agent (rules)"

    # analyst (agent) credentials are threaded via env, exactly like AssuredIdentity
    $env:AGENT_EMAIL = $state.agentEmail
    $env:AGENT_PASSWORD = (Get-DemoAdminPassword -Node $node -Secrets $secrets)

    $configPath = Build-AiasAgentConfig -State $state -Node $node
    Write-WtSuccess "agent config: $configPath (checksFile=assure-id.checks.json)"

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
    # Logs are per-launch so an earlier run's trail is never overwritten.
    $logDir = Join-Path $script:DemoRoot 'logs'
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $outLog = Join-Path $logDir "agent-$stamp.log"
    $errLog = Join-Path $logDir "agent-$stamp.err.log"

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

    $merged = Merge-DemoState -Existing $state -Updates @{
        agentPid          = $proc.Id
        agentConfigPath   = $configPath
        agentProcessName  = $proc.ProcessName
        agentStartedAt    = $startedAt
        agentLogPath      = $outLog
        agentErrorLogPath = $errLog
    }
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
#>
function Test-AiasAgentAlive {
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][AllowNull()]$State)

    if (-not $State) { return $false }

    $props = $State.PSObject.Properties.Name
    if ($props -notcontains 'agentPid' -or -not $State.agentPid) { return $false }

    $proc = Get-Process -Id $State.agentPid -ErrorAction SilentlyContinue
    if (-not $proc) { return $false }

    if ($props -contains 'agentProcessName' -and $State.agentProcessName `
        -and $proc.ProcessName -ne $State.agentProcessName) {
        return $false
    }

    if ($props -contains 'agentStartedAt' -and $State.agentStartedAt) {
        # ConvertFrom-Json coerces an ISO-8601 value into a [DateTime] (Kind=Utc), so the recorded
        # value arrives here as EITHER a DateTime or a string depending on how the caller loaded
        # state. Handle both explicitly.
        #
        # Do not collapse this to [DateTime]::Parse($State.agentStartedAt): when the value is
        # already a DateTime, PowerShell stringifies it as MM/dd/yyyy before parsing, which THROWS
        # under a non-US culture (en-GB reads month 25). The catch below then swallowed it and this
        # whole check silently degraded to the name match — present in the source, inert at runtime.
        $recorded = $null
        try {
            $recorded = if ($State.agentStartedAt -is [DateTime]) {
                ([DateTime]$State.agentStartedAt).ToUniversalTime()
            }
            else {
                [DateTime]::Parse(
                    [string]$State.agentStartedAt,
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
    Build the runtime ActorDefinition config (rules inline + checksFile) for the
    Assure-ID agent and write it to agent/assure-id.config.json. Returns the path.
.DESCRIPTION
    Pure assembly + IO. Reads the bare rule array from assure-id.rules.json and
    embeds it under "rules"; connection tokens are filled from the provisioned
    state; credentials reference $env:AGENT_EMAIL / $env:AGENT_PASSWORD (resolved
    by the agent at runtime, never written to disk — mirrors
    analyst.rules.template.json). The config is written into demos/AIAS/agent/ so
    the relative "checksFile" and "../fixtures/postcodes.offline.json" resolve.
#>
function Build-AiasAgentConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][object]$Node
    )
    $agentDir = Join-Path $script:DemoRoot "agent"
    $rulesPath = Join-Path $agentDir "assure-id.rules.json"
    if (-not (Test-Path -LiteralPath $rulesPath)) { throw "Bare rules not found: $rulesPath" }

    # Depth 30 so nested JSON-Logic conditions survive the round-trip.
    $rules = Get-Content -LiteralPath $rulesPath -Raw | ConvertFrom-Json -Depth 30

    $config = [ordered]@{
        actor = [ordered]@{
            name        = "assure-id-agent"
            description = "Autonomous AIAS Assure-ID agent — evaluates external checks (email verified, postcode exists, no profanity) and approves or rejects with an on-brand reason (rules mode)."
        }
        connection = [ordered]@{
            gatewayUrl = $Node.gateway
            registerId = $State.registerId
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
        checksFile = "assure-id.checks.json"
        rules      = @($rules)
    }

    $outPath = Join-Path $agentDir "assure-id.config.json"
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
    $agent = Start-AiasAgent -StateFile $StateFile

    $state = Read-DemoState -Path $StateFile
    Write-WtBanner "AIAS READY — org=$($state.organizationId) register=$($state.registerId) blueprint=$($state.blueprintId)"
    return [pscustomobject]@{
        target         = $Target
        organizationId = $state.organizationId
        registerId     = $state.registerId
        blueprintId    = $state.blueprintId
        agentRunning   = [bool]($agent -and $agent.Process)
        agentConfig    = $agent.ConfigPath
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
    try {
        $admin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
        $registerReadable = Test-AiasRegisterReadable -Api $api -RegisterId $state.registerId -Headers $admin.Headers
    } catch { }
    if (-not $registerReadable) { $reasons += 'register-not-readable' }
    if ($registerReadable -and $state.blueprintId) {
        $blueprintPublished = (@(Get-AiasPublishedBlueprintIds -Api $api -RegisterId $state.registerId -Headers $admin.Headers) -contains $state.blueprintId)
    }
    if (-not $blueprintPublished) { $reasons += 'blueprint-not-published' }

    $agentRunning = Test-AiasAgentAlive -State $state
    if (-not $agentRunning) {
        $reasons += 'agent-not-running'
        if ($state.PSObject.Properties.Name -contains 'agentLogPath' -and $state.agentLogPath) {
            Write-WtInfo "last agent log: $($state.agentLogPath)"
        }
    }

    $verdict = if ($reasons.Count -eq 0) { 'Ready' } else { 'NotReady' }
    Write-WtBanner "AIAS status: $verdict ($($node.id))"
    Write-WtInfo "gateway=$gatewayHealthy register=$registerReadable blueprint=$blueprintPublished agent=$agentRunning"
    if ($reasons.Count -gt 0) { Write-WtInfo "reasons: $($reasons -join ', ')" }

    return [pscustomobject]@{
        Verdict = $verdict
        Reasons = $reasons
        Detail  = [pscustomobject]@{
            GatewayHealthy     = $gatewayHealthy
            RegisterReadable   = $registerReadable
            BlueprintPublished = $blueprintPublished
            AgentRunning       = $agentRunning
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
    if ($state -and ($state.PSObject.Properties.Name -contains 'agentPid') -and $state.agentPid) {
        # Identity-checked: without this a recycled PID means Stop-Process -Force kills an
        # unrelated process that merely inherited the number.
        $p = if (Test-AiasAgentAlive -State $state) {
            Get-Process -Id $state.agentPid -ErrorAction SilentlyContinue
        } else { $null }
        if ($p -and $PSCmdlet.ShouldProcess("sorcha-agent pid $($state.agentPid)", "Stop")) {
            Stop-Process -Id $state.agentPid -Force -ErrorAction SilentlyContinue
            $removed += "agent(pid $($state.agentPid))"
        }
    }

    if ($PSCmdlet.ShouldProcess($StateFile, "Remove demo state + rendered agent config")) {
        Remove-Item -LiteralPath $StateFile -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $script:DemoRoot "agent/assure-id.config.json") -ErrorAction SilentlyContinue
        $removed += "state.json", "assure-id.config.json"
    }

    Write-WtInfo "NOTE: a full server-side wipe (org, register Mongo DBs, demo wallets) is node-side."
    Write-WtInfo "      Docker: docker compose down -v. n1: the documented reset recipe (network-bootstrap skill)."
    Write-WtSuccess "reset removed: $($removed -join ', ')"
    return [pscustomobject]@{ removed = $removed }
}

Export-ModuleMember -Function `
    New-AiasOrg, Set-AiasOrgMasterKey, Publish-AiasBlueprint, Start-AiasAgent, `
    Build-AiasAgentConfig, Initialize-AiasDemo, Get-AiasDemoStatus, Reset-AiasDemo, `
    Test-AiasAgentAlive

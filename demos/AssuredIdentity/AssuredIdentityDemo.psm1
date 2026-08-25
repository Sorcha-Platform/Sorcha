# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Assured Identity Demo toolkit — node-agnostic provisioning over the proven
# F143 cross-installation loop. Exports four commands:
#   New-IssuingAuthority, Connect-Subscriber, Reset-Demo, Get-DemoStatus
#
# This module orchestrates EXISTING Sorcha HTTP endpoints and the EXISTING
# sorcha-agent CLI; it adds no services. See contracts/commands.md.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- dependencies -----------------------------------------------------------
$script:DemoRoot = $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force -DisableNameChecking

# dot-source the lib units (Common first — others depend on its helpers)
. (Join-Path $PSScriptRoot "lib/Common.ps1")
. (Join-Path $PSScriptRoot "lib/NodeInventory.ps1")
. (Join-Path $PSScriptRoot "lib/AgencyNaming.ps1")
. (Join-Path $PSScriptRoot "lib/Readiness.ps1")
. (Join-Path $PSScriptRoot "lib/Idempotency.ps1")
. (Join-Path $PSScriptRoot "lib/StatusVerdict.ps1")
. (Join-Path $PSScriptRoot "lib/DemoState.ps1")
. (Join-Path $PSScriptRoot "lib/Auth.ps1")
. (Join-Path $PSScriptRoot "lib/AgentLaunch.ps1")

$script:PublicOrgId = "00000000-0000-0000-0000-000000000002"

# ============================================================================
# internal HTTP probes (thin IO — the pure predicates live in the lib units)
# ============================================================================

function Test-DemoRegisterReadable {
    param([string]$Api, [string]$RegisterId, [hashtable]$Headers)
    if ([string]::IsNullOrWhiteSpace($RegisterId)) { return $false }
    try {
        $r = Invoke-SorchaApi -Method GET -Uri "$Api/registers/$RegisterId" -Headers $Headers
        return ($null -ne $r)
    } catch { return $false }
}

function Get-DemoSubscriptionStatus {
    param([string]$Api, [string]$OrgId, [string]$RegisterId, [hashtable]$Headers)
    try {
        $r = Invoke-SorchaApi -Method GET -Uri "$Api/organizations/$OrgId/register-subscriptions/$RegisterId" -Headers $Headers
        return [string]$r.status
    } catch { return $null }
}

function Get-DemoSyncState {
    param([string]$Api, [string]$RegisterId, [hashtable]$Headers)
    try {
        $r = Invoke-SorchaApi -Method GET -Uri "$Api/registers/$RegisterId/sync-state" -Headers $Headers
        return [string]$r.state
    } catch { return 'Indeterminate' }
}

function Get-DemoPublishedBlueprintIds {
    param([string]$Api, [string]$RegisterId)
    try {
        $r = Invoke-SorchaApi -Method GET -Uri "$Api/registers/$RegisterId/blueprints/published"
        return @($r.blueprints | ForEach-Object { $_.blueprintId })
    } catch { return @() }
}

function Test-DemoGatewayHealthy {
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

function ConvertTo-DemoSlug {
    param([string]$Text)
    $slug = ($Text.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($slug)) { $slug = 'authority' }
    return $slug
}

# ============================================================================
# New-IssuingAuthority (issuer node) — FR-001/002/003/005, FR-010/011
# ============================================================================

function New-IssuingAuthority {
    [CmdletBinding()]
    param(
        [string]$NodesFile = (Join-Path $script:DemoRoot "demo-nodes.json"),
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [string]$IssuerNode,
        [string]$AgencyName = "Strathcarron Identity Authority",
        [ValidateSet('rules', 'ai', 'human')][string]$AgentMode = 'rules',
        [switch]$Force
    )

    Write-WtBanner "Assured Identity Demo — provision issuing authority"

    $inventory = Get-DemoNodeInventory -Path $NodesFile
    $node = if ($IssuerNode) { Select-DemoNode -Inventory $inventory -Id $IssuerNode } else { Get-DemoNodeByRole -Inventory $inventory -Role 'issuer' }
    $api = Get-DemoApiBase -Gateway $node.gateway
    $secrets = Import-DemoSecrets
    $slug = ConvertTo-DemoSlug -Text $AgencyName

    Write-WtStep "1: sysadmin login ($($node.id))"
    $sysAdmin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
    Write-WtSuccess "admin org: $($sysAdmin.OrganizationId)"

    # --- idempotency probe (FR-003, R5) ------------------------------------
    $existing = Read-DemoState -Path $StateFile
    $registerReadable = $false
    if ($existing -and $existing.PSObject.Properties.Name -contains 'registerId') {
        $registerReadable = Test-DemoRegisterReadable -Api $api -RegisterId $existing.registerId -Headers $sysAdmin.Headers
    }
    $blueprintPublished = $false
    if ($existing -and $registerReadable -and ($existing.PSObject.Properties.Name -contains 'blueprintId')) {
        $blueprintPublished = (@(Get-DemoPublishedBlueprintIds -Api $api -RegisterId $existing.registerId) -contains $existing.blueprintId)
    }
    $action = Resolve-AuthorityAction `
        -HasOrg ([bool]($existing -and $existing.organizationId)) `
        -HasRegisterId ([bool]($existing -and ($existing.PSObject.Properties.Name -contains 'registerId') -and $existing.registerId)) `
        -RegisterReadable $registerReadable -BlueprintPublished $blueprintPublished -Force:$Force.IsPresent

    if ($action -eq 'Reuse' -and $existing.agencyName -eq $AgencyName) {
        Write-WtSuccess "Authority already provisioned (reuse): register=$($existing.registerId) blueprint=$($existing.blueprintId)"
        Start-DemoAgent -Node $node -Api $api -State $existing -AgentMode $AgentMode -Secrets $secrets -StateFile $StateFile | Out-Null
        return $existing
    }
    if ($action -eq 'ReconcileStale') {
        Write-WtWarn "Recorded register '$($existing.registerId)' is not readable on $($node.id) — reconciling (re-provisioning a fresh authority)."
    }

    Write-WtStep "2: enable public org + register verification-admin"
    Invoke-SorchaApi -Method PUT -Uri "$api/platform/settings/public-org" -Body @{ enabled = $true } -Headers $sysAdmin.Headers | Out-Null
    $vAdminEmail = "verification-admin@$slug.local"
    Register-SorchaPublicUser -TenantUrl $api -Email $vAdminEmail -Password (Get-DemoAdminPassword -Node $node -Secrets $secrets) -DisplayName "Verification Admin" | Out-Null

    Write-WtStep "3: verify verification-admin email"
    $pu = Invoke-SorchaApi -Method GET -Uri "$api/organizations/$script:PublicOrgId/users?includeInactive=true" -Headers $sysAdmin.Headers
    $u = $pu.users | Where-Object { $_.email -eq $vAdminEmail } | Select-Object -First 1
    if ($u) { Confirm-SorchaUserEmail -TenantUrl $api -OrganizationId $script:PublicOrgId -UserId $u.id -Headers $sysAdmin.Headers; Write-WtInfo "verified $vAdminEmail" }

    Write-WtStep "4: create org '$AgencyName'"
    $pw = Get-DemoAdminPassword -Node $node -Secrets $secrets
    $vOrg = New-SorchaOrganization -TenantUrl $api -Name $AgencyName -Subdomain $slug -AdminEmail $vAdminEmail -Headers $sysAdmin.Headers -Description "Issues Assured Identity credentials to citizens"
    $vOrgId = $vOrg.OrganizationId
    Write-WtSuccess "issuing-authority org: $vOrgId"

    Write-WtStep "5: verification-admin (Tier 2) login + issuer wallet + participant"
    $vAdmin = Connect-SorchaUser -TenantUrl $api -Email $vAdminEmail -Password $pw -OrganizationId $vOrgId
    # The ORGANISATION's own wallet, created by its admin (#1525). Distinct from the issuer
    # wallet below: this is what the org's issuer DID anchors on, so credential issuance has
    # nothing to anchor to without it. The platform will not create it — the recovery phrase is
    # shown once and belongs to the org admin.
    $null = New-SorchaOrgWallet -TenantUrl $api -WalletUrl $api `
        -OrganizationId $vOrgId -Headers $vAdmin.Headers

    $vWallet = New-SorchaWallet -WalletUrl $api -Name "$AgencyName Issuer" -Headers $vAdmin.Headers -FetchPublicKey
    Write-WtSuccess "issuer wallet: $($vWallet.Address)"
    $null = Register-SorchaParticipant -TenantUrl $api -WalletUrl $api -OrganizationId $vOrgId -WalletAddress $vWallet.Address -DisplayName "Verification Admin" -Headers $vAdmin.Headers
    $vAdmin = Connect-SorchaUser -TenantUrl $api -Email $vAdminEmail -Password $pw -OrganizationId $vOrgId

    Write-WtStep "5c: verification-analyst (Tier 3) + wallet + participant"
    $vAnalystEmail = "verification-analyst@$slug.local"
    $null = New-SorchaOrgUser -TenantUrl $api -OrganizationId $vOrgId -Email $vAnalystEmail -Password $pw -DisplayName "Verification Analyst" -Headers $sysAdmin.Headers -Roles @("Consumer") -EmailVerified
    $vAnalyst = Connect-SorchaUser -TenantUrl $api -Email $vAnalystEmail -Password $pw -OrganizationId $vOrgId
    $vAnalystWallet = New-SorchaWallet -WalletUrl $api -Name "Verification Analyst Wallet" -Headers $vAnalyst.Headers -FetchPublicKey
    Write-WtSuccess "analyst wallet: $($vAnalystWallet.Address)"
    $null = Register-SorchaParticipant -TenantUrl $api -WalletUrl $api -OrganizationId $vOrgId -WalletAddress $vAnalystWallet.Address -DisplayName "Verification Analyst" -Headers $vAnalyst.Headers

    Write-WtStep "7: create ADVERTISED DevMode register ($($node.id) owns it)"
    $register = New-SorchaRegister -RegisterUrl $api -WalletUrl $api -Name $AgencyName -Description "Assured Identity register — owned by $($node.id) ($AgencyName)" -TenantId $vOrgId -OwnerUserId $vAdmin.UserId -OwnerWalletAddress $vWallet.Address -Headers $vAdmin.Headers -TenantUrl $api -DevMode:$true
    Write-WtSuccess "register: $($register.RegisterId)"

    Write-WtStep "7b: publish analyst participant on register"
    try {
        $null = Publish-SorchaParticipant -TenantUrl $api -OrganizationId $vOrgId -RegisterId $register.RegisterId -ParticipantName "Verification Analyst" -OrganizationName $AgencyName -WalletAddress $vAnalystWallet.Address -PublicKey $vAnalystWallet.PublicKey -Headers $vAdmin.Headers
        Write-WtSuccess "analyst participant published"
    } catch { Write-WtWarn "participant publish: $($_.Exception.Message)" }

    Write-WtStep "8: publish Assured Identity blueprint (issuerName='$AgencyName')"
    $templateRaw = Get-Content -LiteralPath (Join-Path $script:DemoRoot "blueprints/assured-identity.template.json") -Raw
    $rendered = Set-BlueprintIssuerName -BlueprintJson $templateRaw -AgencyName $AgencyName
    $tempBp = Join-Path ([System.IO.Path]::GetTempPath()) ("assured-identity-{0}.json" -f $slug)
    $rendered | Set-Content -LiteralPath $tempBp -Encoding UTF8
    $walletMap = @{ "verification-analyst" = $vAnalystWallet.Address }
    $blueprint = Publish-SorchaBlueprint -BlueprintUrl $api -TemplatePath $tempBp -WalletMap $walletMap -Headers $vAdmin.Headers -IdPrefix "assured-identity" -RegisterId $register.RegisterId
    Remove-Item -LiteralPath $tempBp -ErrorAction SilentlyContinue
    Write-WtSuccess "blueprint: $($blueprint.BlueprintId)"

    # coherence assertion (SC-004)
    $coherence = Test-AgencyNameCoherence -AgencyName $AgencyName -OrgName $AgencyName -RegisterName $AgencyName -ParticipantOrg $AgencyName -BlueprintJson $rendered
    if (-not $coherence.Coherent) { Write-WtWarn "agency-name coherence issues: $($coherence.Mismatches -join '; ')" }

    $state = @{
        issuerNodeId        = $node.id
        agencyName          = $AgencyName
        organizationId      = $vOrgId
        issuerWalletAddress = $vWallet.Address
        analystEmail        = $vAnalystEmail
        analystWallet       = $vAnalystWallet.Address
        registerId          = $register.RegisterId
        blueprintId         = $blueprint.BlueprintId
        agentMode           = $AgentMode
        subscribers         = @()
    }

    Write-WtStep "9: approval agent ($AgentMode)"
    $agent = Start-DemoAgent -Node $node -Api $api -State $state -AgentMode $AgentMode -Secrets $secrets -StateFile $StateFile
    if ($agent -and $agent.Process) { $state['agentPid'] = $agent.Process.Id }

    Write-DemoState -State $state -Path $StateFile | Out-Null
    Write-WtBanner "AUTHORITY READY — register=$($register.RegisterId) blueprint=$($blueprint.BlueprintId) mode=$AgentMode"

    return [pscustomobject]@{
        issuerNode     = $node.id
        agencyName     = $AgencyName
        organizationId = $vOrgId
        registerId     = $register.RegisterId
        blueprintId    = $blueprint.BlueprintId
        agentMode      = $AgentMode
        agentRunning   = [bool]($agent -and $agent.Process)
    }
}

# render + launch the approval agent, threading analyst creds via env
function Start-DemoAgent {
    param([object]$Node, [string]$Api, [object]$State, [string]$AgentMode, [hashtable]$Secrets, [string]$StateFile)
    $analystEmail = if ($State -is [hashtable]) { $State['analystEmail'] } else { $State.analystEmail }
    $analystWallet = if ($State -is [hashtable]) { $State['analystWallet'] } else { $State.analystWallet }
    $orgId = if ($State -is [hashtable]) { $State['organizationId'] } else { $State.organizationId }
    $registerId = if ($State -is [hashtable]) { $State['registerId'] } else { $State.registerId }

    $env:AGENT_EMAIL = $analystEmail
    $env:AGENT_PASSWORD = (Get-DemoAdminPassword -Node $Node -Secrets $Secrets)

    $tokens = @{
        gateway       = $Node.gateway
        registerId    = $registerId
        orgId         = $orgId
        analystWallet = $analystWallet
        analystEmail  = $analystEmail
    }
    return Start-ApprovalAgent -Mode $AgentMode -TemplateDir (Join-Path $script:DemoRoot "agent") `
        -Tokens $tokens -StatePath $StateFile -WorkDir $script:DemoRoot -IssuerGateway $Node.gateway
}

# ============================================================================
# Connect-Subscriber (subscriber node) — FR-004/008, readiness gate R4
# ============================================================================

function Connect-Subscriber {
    [CmdletBinding()]
    param(
        [string]$NodesFile = (Join-Path $script:DemoRoot "demo-nodes.json"),
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [string]$SubscriberNode,
        [string]$RegisterId,
        [int]$TimeoutSeconds = 120
    )

    Write-WtBanner "Assured Identity Demo — connect subscriber"

    $inventory = Get-DemoNodeInventory -Path $NodesFile
    $node = if ($SubscriberNode) { Select-DemoNode -Inventory $inventory -Id $SubscriberNode } else { Get-DemoNodeByRole -Inventory $inventory -Role 'subscriber' }
    $api = Get-DemoApiBase -Gateway $node.gateway
    $secrets = Import-DemoSecrets

    $state = Read-DemoState -Path $StateFile
    if (-not $RegisterId) {
        if (-not $state) { throw "No -RegisterId given and no state.json found. Run New-IssuingAuthority first or pass -RegisterId." }
        $RegisterId = $state.registerId
    }
    $targetBlueprintId = if ($state) { $state.blueprintId } else { $null }
    if (-not $targetBlueprintId) { throw "state.json has no blueprintId; cannot readiness-gate. Re-run New-IssuingAuthority." }

    Write-WtStep "1: sysadmin login ($($node.id))"
    $sysAdmin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets

    Write-WtStep "2: enable public org + subscribe to register $RegisterId"
    Invoke-SorchaApi -Method PUT -Uri "$api/platform/settings/public-org" -Body @{ enabled = $true } -Headers $sysAdmin.Headers | Out-Null

    $subStatus = Get-DemoSubscriptionStatus -Api $api -OrgId $script:PublicOrgId -RegisterId $RegisterId -Headers $sysAdmin.Headers
    $registerReadable = Test-DemoRegisterReadable -Api $api -RegisterId $RegisterId -Headers $sysAdmin.Headers
    $subAction = Resolve-SubscriptionAction -SubscriptionStatus $subStatus -RegisterReadable $registerReadable
    Write-WtInfo "subscription action: $subAction (status=$subStatus, registerReadable=$registerReadable)"

    if ($subAction -ne 'ReuseSubscription') {
        $null = New-SorchaRegisterSubscription -TenantUrl $api -OrganizationId $script:PublicOrgId -RegisterId $RegisterId -Headers $sysAdmin.Headers -SubscriptionType "Public"
    }

    Write-WtStep "3: readiness gate (subscription Active + sync CaughtUp + blueprint published) timeout=${TimeoutSeconds}s"
    # Inline poll using the module-internal probes (cross-scope scriptblocks can't see
    # module-private functions; Wait-SubscriberReady stays lib-only for unit tests).
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $rv = $null
    do {
        $subS  = Get-DemoSubscriptionStatus -Api $api -OrgId $script:PublicOrgId -RegisterId $RegisterId -Headers $sysAdmin.Headers
        $syncS = Get-DemoSyncState -Api $api -RegisterId $RegisterId -Headers $sysAdmin.Headers
        $pubIds = @(Get-DemoPublishedBlueprintIds -Api $api -RegisterId $RegisterId)
        $rv = Test-SubscriberReady -SubscriptionStatus $subS -SyncState $syncS -PublishedBlueprintIds $pubIds -TargetBlueprintId $targetBlueprintId
        if ($rv.Ready) { break }
        if ($sw.Elapsed.TotalSeconds -ge $TimeoutSeconds) { break }
        Write-WtInfo ("  waiting - sub=$subS sync=$syncS bpPublished=" + ($pubIds -contains $targetBlueprintId) + " (" + [int]$sw.Elapsed.TotalSeconds + "s)")
        Start-Sleep -Seconds 6
    } while ($true)
    $sw.Stop()
    $verdict = [pscustomobject]@{
        Status  = if ($rv.Ready) { 'Ready' } else { 'NotReady' }
        Reasons = $rv.Reasons
        Elapsed = $sw.Elapsed
    }

    # update subscribers[] in state
    if ($state) {
        $subs = @()
        if ($state.PSObject.Properties.Name -contains 'subscribers' -and $state.subscribers) {
            $subs = @($state.subscribers | Where-Object { $_.nodeId -ne $node.id })
        }
        $subs += [pscustomobject]@{
            nodeId         = $node.id
            orgId          = $script:PublicOrgId
            subscriptionId = $null
            status         = $verdict.Status
            lastReadyAt    = if ($verdict.Status -eq 'Ready') { (Get-Date).ToUniversalTime().ToString('o') } else { $null }
        }
        $merged = Merge-DemoState -Existing $state -Updates @{ subscribers = $subs }
        Write-DemoState -State $merged -Path $StateFile | Out-Null
    }

    if ($verdict.Status -eq 'Ready') {
        Write-WtBanner "SUBSCRIBER READY — $($node.id) can serve testers (elapsed $([int]$verdict.Elapsed.TotalSeconds)s)"
    } else {
        Write-WtWarn "SUBSCRIBER NOT READY — reasons: $($verdict.Reasons -join ', '). Recovery may still be in progress; retry Connect-Subscriber."
    }

    return [pscustomobject]@{
        subscriberNode = $node.id
        orgId          = $script:PublicOrgId
        registerId     = $RegisterId
        status         = $verdict.Status
        reasons        = $verdict.Reasons
    }
}

# ============================================================================
# Reset-Demo — FR-017
# ============================================================================

function Reset-Demo {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$NodesFile = (Join-Path $script:DemoRoot "demo-nodes.json"),
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [ValidateSet('issuer', 'subscriber', 'all')][string]$Scope = 'all',
        [string]$Node
    )

    Write-WtBanner "Assured Identity Demo — reset ($Scope)"
    $removed = @()

    # stop any tracked approval agent
    $state = Read-DemoState -Path $StateFile
    if ($state -and ($state.PSObject.Properties.Name -contains 'agentPid') -and $state.agentPid) {
        $p = Get-Process -Id $state.agentPid -ErrorAction SilentlyContinue
        if ($p -and $PSCmdlet.ShouldProcess("sorcha-agent pid $($state.agentPid)", "Stop")) {
            Stop-Process -Id $state.agentPid -Force -ErrorAction SilentlyContinue
            $removed += "agent(pid $($state.agentPid))"
        }
    }

    if ($Scope -in @('issuer', 'all')) {
        if ($PSCmdlet.ShouldProcess($StateFile, "Remove demo state")) {
            Remove-Item -LiteralPath $StateFile -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $script:DemoRoot "agent/analyst.rules.json") -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $script:DemoRoot "agent/analyst.ai.json") -ErrorAction SilentlyContinue
            $removed += "state.json", "rendered agent configs"
        }
        Write-WtInfo "NOTE: a full issuer DB wipe (demo wallets, non-system register Mongo DBs) is a NODE-SIDE operation."
        Write-WtInfo "      Run the documented reset recipe on the issuer host (see DEMO.md / n1-deploy skill)."
    }

    if ($Scope -in @('subscriber', 'all') -and $Node) {
        Write-WtInfo "NOTE: unsubscribe + replicated-state cleanup for subscriber '$Node' is node-side; see DEMO.md."
        if ($state -and ($state.PSObject.Properties.Name -contains 'subscribers')) {
            $subs = @($state.subscribers | Where-Object { $_.nodeId -ne $Node })
            $merged = Merge-DemoState -Existing $state -Updates @{ subscribers = $subs }
            if (Test-Path -LiteralPath $StateFile) { Write-DemoState -State $merged -Path $StateFile | Out-Null }
            $removed += "subscriber '$Node' state entry"
        }
    }

    Write-WtSuccess "reset removed: $($removed -join ', ')"
    return [pscustomobject]@{ scope = $Scope; node = $Node; removed = $removed }
}

# ============================================================================
# Get-DemoStatus — FR-018, SC-007
# ============================================================================

function Get-DemoStatus {
    [CmdletBinding()]
    param(
        [string]$NodesFile = (Join-Path $script:DemoRoot "demo-nodes.json"),
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json")
    )

    $state = Read-DemoState -Path $StateFile
    if (-not $state) { Write-WtWarn "No state.json — demo not provisioned."; return [pscustomobject]@{ Verdict = 'NotReady'; Reasons = @('not-provisioned') } }

    $inventory = Get-DemoNodeInventory -Path $NodesFile
    $secrets = Import-DemoSecrets
    $issuer = Select-DemoNode -Inventory $inventory -Id $state.issuerNodeId
    $issuerApi = Get-DemoApiBase -Gateway $issuer.gateway

    $issuerReachable = Test-DemoGatewayHealthy -Gateway $issuer.gateway

    # approver present: human mode = manual (present), else tracked pid alive
    $approverPresent = $false
    if ($state.agentMode -eq 'human') { $approverPresent = $true }
    elseif ($state.PSObject.Properties.Name -contains 'agentPid' -and $state.agentPid) {
        $approverPresent = [bool](Get-Process -Id $state.agentPid -ErrorAction SilentlyContinue)
    }

    $subSignals = @()
    if ($state.PSObject.Properties.Name -contains 'subscribers') {
        foreach ($sub in @($state.subscribers)) {
            $node = $inventory | Where-Object { $_.id -eq $sub.nodeId } | Select-Object -First 1
            if (-not $node) { continue }
            $api = Get-DemoApiBase -Gateway $node.gateway
            $admin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
            $subSignals += [pscustomobject]@{
                NodeId                = $sub.nodeId
                SubscriptionStatus    = (Get-DemoSubscriptionStatus -Api $api -OrgId $script:PublicOrgId -RegisterId $state.registerId -Headers $admin.Headers)
                SyncState             = (Get-DemoSyncState -Api $api -RegisterId $state.registerId -Headers $admin.Headers)
                PublishedBlueprintIds = (Get-DemoPublishedBlueprintIds -Api $api -RegisterId $state.registerId)
                TargetBlueprintId     = $state.blueprintId
            }
        }
    }

    $verdict = Get-ReadinessVerdict -IssuerReachable $issuerReachable -ApproverPresent $approverPresent -Subscribers $subSignals

    Write-WtBanner "Demo status: $($verdict.Verdict)"
    Write-WtInfo "issuer $($issuer.id): reachable=$issuerReachable  approver($($state.agentMode))=$approverPresent"
    foreach ($pn in $verdict.PerNode) {
        $r = if ($pn.Ready) { "READY" } else { "not ready ($($pn.Reasons -join ','))" }
        Write-WtInfo "subscriber $($pn.NodeId): $r"
    }
    if ($verdict.Reasons.Count -gt 0) { Write-WtInfo "overall reasons: $($verdict.Reasons -join ', ')" }

    return $verdict
}

Export-ModuleMember -Function New-IssuingAuthority, Connect-Subscriber, Reset-Demo, Get-DemoStatus

# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS Demo toolkit — single-node provisioning for the AI-Assisted Identity
# Assurance Service (AIAS) authority. Exports one command: Invoke-AiasDemo.
#
# Establishes the AIAS issuing authority on a local Docker stack:
#   org → verification-admin user → issuer wallet → register (owned by issuer
#   wallet) → fresh session → blueprint publish → participant publish →
#   public-org subscription → agent config written.
#
# The register is created with the verification-admin (issuer) wallet as owner
# (Pattern A, mirroring AssuredIdentity). This satisfies the F142 PublishGate
# and eliminates the 403 / participant-seal-timeout / public-org-500 symptoms.
# See specs/175-fix-aias-publish-governance/ for root-cause analysis.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- dependencies -----------------------------------------------------------
$script:DemoRoot = $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force -DisableNameChecking

$script:AiasOrgName    = "AIAS Authority"
$script:AiasSubdomain  = "aias-authority"
$script:AiasRegName    = "AIAS Authority"
$script:PublicOrgId    = "00000000-0000-0000-0000-000000000002"

# ============================================================================
# Invoke-AiasDemo — provision the AIAS issuing authority end-to-end
# ============================================================================

function Invoke-AiasDemo {
    <#
    .SYNOPSIS
        Provision the AIAS authority: org, wallet, register (issuer-owned),
        blueprint publish, participant publish, public-org subscription, agent config.
    .PARAMETER BaseUrl
        API gateway base URL (e.g. http://localhost). All service calls go
        through the gateway. Defaults to http://localhost.
    .PARAMETER SysAdminHeaders
        Authorization headers carrying the system-admin JWT. Obtained by
        Connect-SorchaAdmin in the entry script (run-demo.ps1).
    .PARAMETER AdminPassword
        Password for the verification-admin org user. Defaults to the shared
        walkthrough dev seed password.
    #>
    [CmdletBinding()]
    param(
        [string]$BaseUrl        = "http://localhost",
        [Parameter(Mandatory)][hashtable]$SysAdminHeaders,
        [string]$AdminPassword  = "Wt-aias-authority-admin-2026!"
    )

    $api = "$BaseUrl/api"

    Write-WtBanner "AIAS Demo — provision issuing authority"

    # ── Step 1: enable public org ──────────────────────────────────────────────
    Write-WtStep "1: enable public org"
    try {
        $null = Invoke-SorchaApi -Method PUT `
            -Uri "$api/platform/settings/public-org" `
            -Body @{ enabled = $true } `
            -Headers $SysAdminHeaders
    } catch { Write-WtWarn "public-org setting unchanged: $($_.Exception.Message)" }

    # ── Step 2: create AIAS org ────────────────────────────────────────────────
    Write-WtStep "2: create AIAS organisation ('$script:AiasOrgName')"
    $vAdminEmail = "verification-admin@$script:AiasSubdomain.local"
    $vOrg = New-SorchaOrganization `
        -TenantUrl $api `
        -Name $script:AiasOrgName `
        -Subdomain $script:AiasSubdomain `
        -AdminEmail $vAdminEmail `
        -AdminPassword $AdminPassword `
        -AdminDisplayName "Verification Admin" `
        -AdminEmailVerified `
        -Headers $SysAdminHeaders `
        -Description "AI-Assisted Identity Assurance Service — issues AIAS credentials to citizens"
    $vOrgId = $vOrg.OrganizationId
    Write-WtSuccess "AIAS org: $vOrgId"

    # ── Step 3: verification-admin login + issuer wallet ───────────────────────
    Write-WtStep "3: verification-admin login + issuer wallet"
    $vAdmin = Connect-SorchaUser `
        -TenantUrl $api `
        -Email $vAdminEmail `
        -Password $AdminPassword `
        -OrganizationId $vOrgId
    Write-WtSuccess "verification-admin: $($vAdmin.UserId)"

    $vWallet = New-SorchaWallet `
        -WalletUrl $api `
        -Name "AIAS Issuer Wallet" `
        -Headers $vAdmin.Headers `
        -FetchPublicKey
    Write-WtSuccess "issuer wallet: $($vWallet.Address)"

    # Link verification-admin as a platform participant (required before register creation).
    $null = Register-SorchaParticipant `
        -TenantUrl $api `
        -WalletUrl $api `
        -OrganizationId $vOrgId `
        -WalletAddress $vWallet.Address `
        -DisplayName "Verification Admin" `
        -Headers $vAdmin.Headers

    # ── Step 4: fresh verification-admin session (Decision 4 from research.md) ─
    # The token minted before wallet-link lacks wallet_address. Mint a fresh
    # session so the JWT carries wallet_address — required by the F142 PublishGate.
    Write-WtStep "4: fresh verification-admin session (JWT must carry wallet_address)"
    $vAdmin = Connect-SorchaUser `
        -TenantUrl $api `
        -Email $vAdminEmail `
        -Password $AdminPassword `
        -OrganizationId $vOrgId
    Write-WtSuccess "fresh session minted — wallet_address in JWT"

    # ── Step 5: create AIAS register owned by the issuer wallet (Pattern A) ───
    # -OwnerUserId + -OwnerWalletAddress places the issuer wallet on the register
    # roster, satisfying the F142 PublishGate for all subsequent publish calls.
    # TenantUrl triggers auto-subscribe of the Sorcha public org (FR-005).
    Write-WtStep "5: create AIAS register (issuer wallet as owner — FR-001/002)"
    $register = New-SorchaRegister `
        -RegisterUrl $api `
        -WalletUrl $api `
        -Name $script:AiasRegName `
        -Description "AIAS register — owned by the AIAS issuer wallet" `
        -TenantId $vOrgId `
        -OwnerUserId $vAdmin.UserId `
        -OwnerWalletAddress $vWallet.Address `
        -Headers $vAdmin.Headers `
        -TenantUrl $api `
        -DevMode:$true
    $registerId = $register.RegisterId
    Write-WtSuccess "register: $registerId (reused=$($register.Reused))"

    # T017 — idempotency guard: when the register is reused we cannot re-verify
    # ownership from the client side. Since the first run created it with the
    # issuer wallet as owner (see -OwnerWalletAddress above), a reused register
    # carries that ownership forward. If publish subsequently 403s, the register
    # was created by an earlier run WITHOUT the owner-wallet fix — re-provision
    # via `docker-compose down -v && docker-compose up -d`, then re-run this script.
    if ($register.Reused) {
        Write-WtInfo "Register reused — ownership assumed correct (issuer wallet from initial provision)."
        Write-WtInfo "If blueprint publish fails with 403, the register predates the governance fix."
        Write-WtInfo "Remedy: docker-compose down -v && docker-compose up -d, then re-run run-demo.ps1."
    }

    # ── Step 6: publish AIAS blueprint ────────────────────────────────────────
    # Published with the fresh verification-admin session (wallet_address in JWT).
    # The register roster match allows the F142 PublishGate to pass (no 403).
    Write-WtStep "6: publish AIAS blueprint (FR-003)"
    $templatePath = Join-Path $script:DemoRoot "blueprints/aias.template.json"
    $templateRaw  = Get-Content -LiteralPath $templatePath -Raw
    $rendered     = $templateRaw -replace '\{\{issuerName\}\}', $script:AiasOrgName

    $tempBp = Join-Path ([System.IO.Path]::GetTempPath()) "aias-$(New-Guid).json"
    $rendered | Set-Content -LiteralPath $tempBp -Encoding UTF8

    $walletMap = @{ "verification-analyst" = $vWallet.Address }
    $blueprint = Publish-SorchaBlueprint `
        -BlueprintUrl $api `
        -TemplatePath $tempBp `
        -WalletMap $walletMap `
        -Headers $vAdmin.Headers `
        -IdPrefix "aias" `
        -RegisterId $registerId

    Remove-Item -LiteralPath $tempBp -ErrorAction SilentlyContinue
    Write-WtSuccess "blueprint: $($blueprint.BlueprintId)"

    # ── Step 7: publish verification-analyst participant ───────────────────────
    # With the issuer wallet on the register roster the participant publish seals
    # within the normal window (no ~90s timeout — FR-004).
    Write-WtStep "7: publish verification-analyst participant (FR-004)"
    try {
        $null = Publish-SorchaParticipant `
            -TenantUrl $api `
            -OrganizationId $vOrgId `
            -RegisterId $registerId `
            -ParticipantName "Verification Analyst" `
            -OrganizationName $script:AiasOrgName `
            -WalletAddress $vWallet.Address `
            -PublicKey $vWallet.PublicKey `
            -Headers $vAdmin.Headers
        Write-WtSuccess "participant published"
    } catch {
        Write-WtWarn "participant publish (non-fatal): $($_.Exception.Message)"
    }

    # ── Step 8: write agent configuration (FR-006) ────────────────────────────
    Write-WtStep "8: write agent configuration"
    $agentDir = Join-Path $script:DemoRoot "agent"
    if (-not (Test-Path $agentDir)) { New-Item -ItemType Directory -Path $agentDir | Out-Null }

    $agentConfig = @{
        authority      = $script:AiasOrgName
        organizationId = $vOrgId
        registerId     = $registerId
        blueprintId    = $blueprint.BlueprintId
        walletAddress  = $vWallet.Address
        apiBase        = $api
        provisionedAt  = (Get-Date).ToUniversalTime().ToString('o')
    }

    $agentConfigPath = Join-Path $agentDir "agent-config.json"
    $agentConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $agentConfigPath -Encoding UTF8
    Write-WtSuccess "agent config: $agentConfigPath"

    Write-WtBanner "AIAS AUTHORITY READY — register=$registerId blueprint=$($blueprint.BlueprintId)"

    return [pscustomobject]@{
        organizationId = $vOrgId
        registerId     = $registerId
        blueprintId    = $blueprint.BlueprintId
        walletAddress  = $vWallet.Address
        agentConfigPath = $agentConfigPath
    }
}

Export-ModuleMember -Function Invoke-AiasDemo

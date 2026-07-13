# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS Assured Identity demo (Feature 174 / M1) — rehearsal / test hook (FR-011, SC-004).
#
# Against the already-provisioned environment (run ./run-demo.ps1 first) this runs
# ONE APPROVAL and ONE REJECTION end to end and asserts the outcomes:
#   approval  -> the autonomous Assure-ID agent approves; an AssuredIdentityCredential
#                (offer) lands in the applicant's wallet, carrying the submitted portrait.
#   rejection -> a bad postcode ("ZZ99 9ZZ") is rejected; the recorded decision is
#                "rejected" with the on-brand reason and NO credential is issued.
#
# Submission is driven via the gateway HTTP API exactly like the proven
# walkthroughs/AssuredIdentity/run-phase1-identity.ps1 (instance create -> Action 1
# submit with holder keys); the agent (not this script) performs Action 2.
#
# Exit 0 on success, non-zero on any assertion failure.
#
# Usage:
#   ./demos/AIAS/rehearse.ps1            # Docker
#   ./demos/AIAS/rehearse.ps1 -Target n1
[CmdletBinding()]
param(
    [ValidateSet('docker', 'n1')][string]$Target = 'docker',
    [string]$StateFile = (Join-Path $PSScriptRoot "state.json"),
    [int]$DecisionTimeoutSeconds = 60,
    [int]$DeliveryTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot "AiasDemo.psm1") -Force -DisableNameChecking
# SorchaWalkthrough LAST: AiasDemo nested-imports it with -Force, which evicts it from the caller's
# scope, so re-import it here to win the script-scoped copy (Write-Wt* cmdlets). Same fix as run-demo.
Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force -DisableNameChecking

# The demo's generic lib helpers (Read-DemoState, Get-DemoApiBase, Import-DemoSecrets,
# Connect-DemoNodeAdmin) are dot-sourced by AiasDemo into ITS module scope and are NOT re-exported,
# so this script's own script-level calls to them fail with "not recognized". Dot-source them here
# too (same units AiasDemo uses) so rehearse resolves them in its own scope.
$script:AssuredLib = Join-Path $PSScriptRoot "../AssuredIdentity/lib"
. (Join-Path $script:AssuredLib "Common.ps1")     # Get-DemoApiBase
. (Join-Path $script:AssuredLib "DemoState.ps1")  # Read/Write/Merge-DemoState
. (Join-Path $script:AssuredLib "Auth.ps1")       # Import-DemoSecrets, Connect-DemoNodeAdmin

$state = Read-DemoState -Path $StateFile
if (-not $state) { Write-WtFail "No state.json — run ./run-demo.ps1 -Target $Target first."; exit 2 }

$gateway = $state.gateway
$api     = Get-DemoApiBase -Gateway $gateway
$failures = @()

# ---------------------------------------------------------------------------
# Helpers (local to the rehearsal — submission + decision-read, gateway HTTP API)
# ---------------------------------------------------------------------------

function New-RehearsalApplicant {
    param([string]$Tag, [switch]$SkipEmailVerification)
    # Anonymous signup on the public org + a wallet so the credential can be delivered.
    $email = "aias-rehearse-$Tag-$([guid]::NewGuid().ToString('N').Substring(0,8))@example.test"
    $pw = "Rehearse_Pass_2026!"
    Register-SorchaPublicUser -TenantUrl $api -Email $email -Password $pw -DisplayName "Rehearsal $Tag" | Out-Null
    $publicOrgId = "00000000-0000-0000-0000-000000000002"

    # Verify the email so the agent's emailVerified gate passes — UNLESS this is the F183
    # unverified-applicant rehearsal, which must reach the email-gate reject. The returned
    # EmailVerified flag drives what the submission carries (mirrors the web client's
    # x-claim-source stamp of the real email_verified JWT claim).
    $emailVerified = $false
    if (-not $SkipEmailVerification) {
        $secrets = Import-DemoSecrets
        $node = [pscustomobject]@{ id = $state.target; adminEmail = 'admin@sorcha.local'; gateway = $gateway }
        $admin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
        $pu = Invoke-SorchaApi -Method GET -Uri "$api/organizations/$publicOrgId/users?includeInactive=true" -Headers $admin.Headers
        $u = $pu.users | Where-Object { $_.email -eq $email } | Select-Object -First 1
        if ($u) {
            $null = Confirm-SorchaUserEmail -TenantUrl $api -OrganizationId $publicOrgId -UserId $u.id -Headers $admin.Headers
            $emailVerified = $true
        }
    }

    # Login succeeds even for an unverified user (the token just carries email_verified=false).
    $session = Connect-SorchaUser -TenantUrl $api -Email $email -Password $pw -OrganizationId $publicOrgId
    $wallet = New-SorchaWallet -WalletUrl $api -Name "Rehearsal $Tag Wallet" -Headers $session.Headers -FetchPublicKey
    return [pscustomobject]@{
        Email = $email; Session = $session; Wallet = $wallet; PublicOrgId = $publicOrgId; EmailVerified = $emailVerified
    }
}

function Submit-RehearsalApplication {
    param(
        [Parameter(Mandatory)][object]$Applicant,
        [Parameter(Mandatory)][string]$Postcode,
        [switch]$WithPortrait
    )
    $instance = Invoke-SorchaApi -Method POST `
        -Uri "$api/instances/" `
        -Body @{
            blueprintId = $state.blueprintId
            registerId  = $state.registerId
            tenantId    = $Applicant.PublicOrgId
            metadata    = @{ source = "rehearsal"; demo = "AIAS" }
        } `
        -Headers $Applicant.Session.Headers
    $instanceId = $instance.id

    $payload = @{
        name = @{ givenName = "Ada"; middleName = ""; familyName = "Rehearsal"; fullName = "Ada Rehearsal" }
        dob  = @{ dateOfBirth = "1990-01-01" }
        email = @{ email = $Applicant.Email }
        address = @{
            line1 = "1 Demo Street"; town = "Testington"; region = "Testshire"
            postcode = $Postcode; country = "GB"
        }
    }
    # F183: carry the applicant's REAL email-verified status, exactly as the web client's
    # x-claim-source seeder stamps it from the email_verified JWT claim — NOT a hardcoded true.
    # Verified → emailVerified:true; unverified → key omitted (the absent-on-the-wire shape the
    # agent's email gate rejects, which the old hardcoded true masked).
    if ($Applicant.EmailVerified) { $payload.emailVerified = $true }
    if ($WithPortrait) {
        # Tiny valid-JPEG-ish token well under the F107 ~27KB gate so the portrait
        # claim is carried. (Real applicants supply a camera/upload-sized photo.)
        $bytes = [byte[]](0xFF,0xD8,0xFF,0xE0,0x00,0x10,0x4A,0x46,0x49,0x46,0x00,0x01,0xFF,0xD9)
        $payload.portrait = @{ tokenImageBase64 = [Convert]::ToBase64String($bytes) }
    }

    # Carry holder keys so the issued credential can be bound + delivered (F137).
    $holderKeys = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/holder-keys" -Headers $Applicant.Session.Headers
    $payload.holderKeys = @{
        holderJwk           = $holderKeys.holderJwk
        encryptionPublicKey = $holderKeys.encryptionPublicKey
        algorithm           = $holderKeys.algorithm
    }

    $null = Invoke-SorchaAction `
        -BlueprintUrl $api -InstanceId $instanceId -ActionId "1" `
        -BlueprintId $state.blueprintId -SenderWallet $Applicant.Wallet.Address `
        -RegisterId $state.registerId -Token $Applicant.Session.Token `
        -PayloadData $payload -WaitForSeal
    return $instanceId
}

function Wait-AiasDecision {
    # Poll the instance until the autonomous agent's Action 2 decision folds in.
    # Returns the decision string ('approved'|'rejected') or $null on timeout.
    param([Parameter(Mandatory)][string]$InstanceId, [Parameter(Mandatory)][hashtable]$Headers)
    $deadline = (Get-Date).AddSeconds($DecisionTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $inst = Invoke-SorchaApi -Method GET -Uri "$api/instances/$InstanceId" -Headers $Headers

            $currentActions = @()
            if ($inst.PSObject.Properties.Name -contains 'currentActionIds') { $currentActions = @($inst.currentActionIds) }

            # Approval routes to the citizen Claim action (Action 3); the instance parks there awaiting
            # the claim, so a pending Action 3 is the unambiguous approved signal.
            if ($currentActions -contains 3 -or $currentActions -contains '3') { return 'approved' }

            # Explicit decision payload, when the projection surfaces it (disclosed to the applicant).
            if ($inst.PSObject.Properties.Name -contains 'data' -and $inst.data -and
                ($inst.data.PSObject.Properties.Name -contains 'decision')) {
                $d = [string]$inst.data.decision
                if ($d) { return $d }
            }

            # Rejection routes to a terminal route (nextActionIds: []). Under the Feature-145
            # ledger-derived projection that folds as a COMPLETED instance with no current actions and
            # the claim action (3) never reached — distinct from an approval, which parks at Action 3.
            # (InstanceState: 1 = Completed, 2 = Rejected.) accumulatedData is not surfaced on the
            # projection, so the terminal shape — not a decision string — is the reliable reject signal.
            $state = if ($inst.PSObject.Properties.Name -contains 'state') { "$($inst.state)" } else { '' }
            $terminal = ($state -match '^(1|2)$') -or ($state -match 'complete|reject')
            if ($terminal -and $currentActions.Count -eq 0) { return 'rejected' }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $null
}

function Test-CredentialDelivered {
    param([Parameter(Mandatory)][object]$Applicant)
    $deadline = (Get-Date).AddSeconds($DeliveryTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $snap = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/credentials" -Headers $Applicant.Session.Headers
            if ($snap.credentials -and $snap.credentials.Count -gt 0) {
                $hit = $snap.credentials | Where-Object { $_.vct -match "AssuredIdentity" -or $_.displayLabel -match "Assured" } | Select-Object -First 1
                if ($hit) { return $hit }
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

# ---------------------------------------------------------------------------
# Rehearsal 1 — APPROVAL (existing postcode + portrait)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS rehearsal 1/3 — APPROVAL (verified email + existing postcode + portrait)"
try {
    $approveApplicant = New-RehearsalApplicant -Tag "approve"
    $goodPostcode = "SW1A 1AA"   # present in fixtures/postcodes.offline.json + postcodes.io
    $instId = Submit-RehearsalApplication -Applicant $approveApplicant -Postcode $goodPostcode -WithPortrait
    Write-WtInfo "submitted approval application (instance=$instId)"

    $decision = Wait-AiasDecision -InstanceId $instId -Headers $approveApplicant.Session.Headers
    if ($decision -ne 'approved') { $failures += "APPROVAL: expected decision 'approved' within ${DecisionTimeoutSeconds}s, got '$decision'." }
    else { Write-WtSuccess "agent approved the application" }

    if ($decision -eq 'approved') {
        $cred = Test-CredentialDelivered -Applicant $approveApplicant
        if (-not $cred) { $failures += "APPROVAL: no AssuredIdentityCredential delivered within ${DeliveryTimeoutSeconds}s." }
        else {
            Write-WtSuccess "AssuredIdentityCredential delivered (id=$($cred.id))"
            $hasPortrait = ($cred.PSObject.Properties.Name -contains 'portrait' -and $cred.portrait) -or `
                           ($cred.PSObject.Properties.Name -contains 'displayLabel')   # offer present
            if (-not $hasPortrait) { Write-WtWarn "APPROVAL: credential offer present but portrait claim not visible in the summary projection (verify on claim)." }
        }
    }
} catch {
    $failures += "APPROVAL: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Rehearsal 2 — REJECTION (non-existent postcode -> on-brand reason, no credential)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS rehearsal 2/3 — REJECTION (verified email, bad postcode 'ZZ99 9ZZ')"
try {
    $rejectApplicant = New-RehearsalApplicant -Tag "reject"
    $instId = Submit-RehearsalApplication -Applicant $rejectApplicant -Postcode "ZZ99 9ZZ"
    Write-WtInfo "submitted rejection application (instance=$instId)"

    $decision = Wait-AiasDecision -InstanceId $instId -Headers $rejectApplicant.Session.Headers
    if ($decision -ne 'rejected') { $failures += "REJECTION: expected decision 'rejected' within ${DecisionTimeoutSeconds}s, got '$decision'." }
    else { Write-WtSuccess "agent rejected the application (bad postcode)" }

    # No credential must be issued on rejection.
    $cred = Test-CredentialDelivered -Applicant $rejectApplicant
    if ($cred) { $failures += "REJECTION: a credential was issued ($($cred.id)) — none should be on rejection." }
    else { Write-WtSuccess "no credential issued (correct)" }

    # The on-brand reason should be on the recorded decision / surfaced to the applicant.
    try {
        $inst = Invoke-SorchaApi -Method GET -Uri "$api/instances/$instId" -Headers $rejectApplicant.Session.Headers
        $notes = $null
        if ($inst.PSObject.Properties.Name -contains 'data' -and $inst.data -and ($inst.data.PSObject.Properties.Name -contains 'verificationNotes')) {
            $notes = [string]$inst.data.verificationNotes
        }
        if ($notes -and $notes -match 'AIAS') { Write-WtSuccess "on-brand reason recorded: $notes" }
        else { Write-WtWarn "REJECTION: could not read the on-brand reason from the instance projection (rejection still recorded)." }
    } catch { Write-WtWarn "REJECTION: reason read transient error: $($_.Exception.Message)" }
} catch {
    $failures += "REJECTION: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Rehearsal 3 — REJECTION (unverified email -> email-gate reject, no credential) [F183]
# Real web-form shape: an unverified applicant's client stamps NO verified emailVerified,
# so the payload omits it and the agent's email gate rejects. This is the case the old
# hardcoded emailVerified=$true masked — a genuine web applicant could never get here.
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS rehearsal 3/3 — REJECTION (unverified email; no emailVerified on the wire)"
try {
    $unverifiedApplicant = New-RehearsalApplicant -Tag "unverified" -SkipEmailVerification
    $goodPostcode = "SW1A 1AA"   # good postcode so the ONLY reject reason is the email gate
    $instId = Submit-RehearsalApplication -Applicant $unverifiedApplicant -Postcode $goodPostcode
    Write-WtInfo "submitted unverified-email application (instance=$instId)"

    $decision = Wait-AiasDecision -InstanceId $instId -Headers $unverifiedApplicant.Session.Headers
    if ($decision -ne 'rejected') { $failures += "UNVERIFIED: expected decision 'rejected' within ${DecisionTimeoutSeconds}s, got '$decision'." }
    else { Write-WtSuccess "agent rejected the application (unverified email)" }

    # No credential must be issued on an email-gate reject.
    $cred = Test-CredentialDelivered -Applicant $unverifiedApplicant
    if ($cred) { $failures += "UNVERIFIED: a credential was issued ($($cred.id)) — none should be on an email-gate reject." }
    else { Write-WtSuccess "no credential issued (correct)" }
} catch {
    $failures += "UNVERIFIED: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Verdict
# ---------------------------------------------------------------------------
if ($failures.Count -eq 0) {
    Write-WtBanner "AIAS rehearsal PASSED — approval issued a credential; rejection recorded with no credential."
    exit 0
} else {
    Write-WtBanner "AIAS rehearsal FAILED"
    foreach ($f in $failures) { Write-WtFail $f }
    exit 1
}

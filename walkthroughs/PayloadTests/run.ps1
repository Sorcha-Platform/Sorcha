#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PayloadTests — Run
# Exercises the stored data transaction (Feature 085) file attachment flow:
#   1. Generate deterministic test file of configurable size
#   2. Chunk and upload via POST /api/file-chunks (staged submission)
#   3. Execute blueprint action with file reference in payload
#   4. Download file via Wallet Service and verify integrity
#
# Usage:
#   pwsh walkthroughs/PayloadTests/run.ps1                     # 1KB smoke test
#   pwsh walkthroughs/PayloadTests/run.ps1 -FileSize 4MB       # Single chunk boundary
#   pwsh walkthroughs/PayloadTests/run.ps1 -FileSize 10MB      # Multi-chunk (3 chunks)
#   pwsh walkthroughs/PayloadTests/run.ps1 -FileSize 40MB      # Max limit (10 chunks)
#   pwsh walkthroughs/PayloadTests/run.ps1 -Rounds 5           # Multiple rounds
#   pwsh walkthroughs/PayloadTests/run.ps1 -FileSize 4MB -Rounds 3 -ShowJson

param(
    [string]$FileSize = '1KB',
    [int]$Rounds = 1,
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PayloadTests — File Transfer ($FileSize x $Rounds rounds)"

# ============================================================================
# Load State
# ============================================================================
$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "No state.json found. Run setup.ps1 first."
    exit 1
}
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$stepsPassed = 0
$totalSteps  = 0
$start       = Get-Date

# ============================================================================
# Helper: Parse file size string to bytes
# ============================================================================
function ConvertTo-Bytes([string]$sizeStr) {
    if ($sizeStr -match '^(\d+(?:\.\d+)?)\s*(KB|MB|GB|B)?$') {
        $num    = [double]$Matches[1]
        $suffix = if ($Matches[2]) { $Matches[2].ToUpper() } else { 'B' }
        switch ($suffix) {
            'GB' { return [long]($num * 1073741824) }
            'MB' { return [long]($num * 1048576) }
            'KB' { return [long]($num * 1024) }
            default { return [long]$num }
        }
    }
    throw "Invalid file size: $sizeStr"
}

# ============================================================================
# Helper: Generate deterministic test file content
# Produces a byte array filled with a repeating pattern derived from a seed.
# The seed ensures the same size always produces the same content for validation.
# ============================================================================
function New-TestFileContent([long]$sizeBytes, [int]$seed = 42) {
    $rng = [System.Random]::new($seed)
    $buffer = [byte[]]::new($sizeBytes)
    $rng.NextBytes($buffer)
    return $buffer
}

# ============================================================================
# Helper: Compute SHA-256 hash of byte array
# ============================================================================
function Get-Sha256Hash([byte[]]$data) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash($data)
    $sha.Dispose()
    return ($hash | ForEach-Object { $_.ToString("x2") }) -join ''
}

# ============================================================================
# Helper: Upload file as chunks and return file reference
# ============================================================================
function Send-FileChunks {
    param(
        [byte[]]$FileContent,
        [string]$FileName,
        [string]$ContentType,
        [string]$FileHash,
        [string]$SenderWallet,
        [string]$RegisterAddress,
        [string]$Token,
        [string]$GatewayUrl
    )

    $chunkSize = 4 * 1024 * 1024  # 4MB
    $totalChunks = [Math]::Ceiling($FileContent.Length / $chunkSize)
    if ($totalChunks -eq 0) { $totalChunks = 1 }

    $chunkTxIds      = @()
    $uploadSessionId = $null
    $saltBase64      = $null

    $headers = @{
        Authorization = "Bearer $Token"
        "Content-Type" = "application/json"
    }

    for ($i = 0; $i -lt $totalChunks; $i++) {
        $offset = $i * $chunkSize
        $length = [Math]::Min($chunkSize, $FileContent.Length - $offset)
        $chunk  = [byte[]]::new($length)
        [Array]::Copy($FileContent, $offset, $chunk, 0, $length)

        $body = @{
            senderWallet    = $SenderWallet
            registerAddress = $RegisterAddress
            chunkIndex      = [int]$i
            totalChunks     = [int]$totalChunks
            fileHash        = "sha256:$FileHash"
            contentType     = $ContentType
            contentBase64   = [Convert]::ToBase64String($chunk)
        }

        # Include session ID for chunks after the first
        if ($uploadSessionId) {
            $body.uploadSessionId = $uploadSessionId
        }

        $chunkStart = Get-Date
        try {
            $response = Invoke-SorchaApi -Method POST `
                -Uri "$GatewayUrl/api/file-chunks/" `
                -Body $body `
                -Headers $headers
        } catch {
            Write-WtFail "  Chunk $i upload error: $($_.Exception.Message)"
            $errorBody = Get-SorchaErrorBody $_
            if ($errorBody) { Write-WtFail "  Server response: $errorBody" }
            throw
        }
        $chunkMs = ((Get-Date) - $chunkStart).TotalMilliseconds

        $chunkTxIds += $response.chunkTransactionId

        # Capture session from first chunk response
        if (-not $uploadSessionId) {
            $uploadSessionId = $response.uploadSessionId
            $saltBase64      = $response.saltBase64
        }

        Write-WtInfo ("  Chunk {0}/{1}: {2:N0} bytes -> {3} ({4:N0}ms)" -f `
            ($i + 1), $totalChunks, $length, $response.chunkTransactionId, $chunkMs)
    }

    # Build file reference for action payload
    return @{
        fileName            = $FileName
        contentType         = $ContentType
        size                = $FileContent.Length
        hash                = "sha256:$FileHash"
        salt                = $saltBase64
        chunkTransactionIds = $chunkTxIds
        masterKeyId         = "server-managed"
        uploadSessionId     = $uploadSessionId
    }
}

# ============================================================================
# Helper: Poll instance until a specific action ID becomes current
# Returns the instance object (which includes lastTransactionId)
# ============================================================================
function Wait-ForActionReady {
    param(
        [string]$BlueprintUrl,
        [string]$InstanceId,
        [int]$ExpectedActionId,
        [string]$Token,
        [int]$MaxWaitSeconds = 30
    )
    $waited = 0
    while ($waited -lt $MaxWaitSeconds) {
        $inst = Invoke-SorchaApi -Method GET `
            -Uri "$BlueprintUrl/instances/$InstanceId" `
            -Headers @{ Authorization = "Bearer $Token" }
        if ($inst.currentActionIds -contains $ExpectedActionId) {
            return $inst
        }
        Start-Sleep -Seconds 1
        $waited++
    }
    throw "Timeout waiting for action $ExpectedActionId to become current (${MaxWaitSeconds}s)"
}

# ============================================================================
# Helper: Download file via Wallet Service and verify integrity
# ============================================================================
function Receive-AndVerifyFile {
    param(
        [string]$WalletAddress,
        [string]$RegisterId,
        [string]$ActionTxId,
        [string]$FieldName,
        [string]$ExpectedHash,
        [long]$ExpectedSize,
        [string]$Token,
        [string]$GatewayUrl
    )

    $headers = @{ Authorization = "Bearer $Token" }

    $uri = "$GatewayUrl/api/v1/wallets/$WalletAddress/files/download" +
           "?registerId=$RegisterId&txId=$ActionTxId&fieldName=$FieldName&fileIndex=0"

    $downloadStart = Get-Date
    try {
        $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method GET
        $downloadMs = ((Get-Date) - $downloadStart).TotalMilliseconds

        $downloadedBytes = $response.Content
        $downloadedHash  = Get-Sha256Hash -data $downloadedBytes

        Write-WtInfo ("  Downloaded: {0:N0} bytes in {1:N0}ms" -f $downloadedBytes.Length, $downloadMs)

        if ($downloadedHash -ne $ExpectedHash) {
            Write-WtFail "  Hash mismatch! Expected: $ExpectedHash Got: $downloadedHash"
            return $false
        }

        if ($downloadedBytes.Length -ne $ExpectedSize) {
            Write-WtFail "  Size mismatch! Expected: $ExpectedSize Got: $($downloadedBytes.Length)"
            return $false
        }

        Write-WtSuccess "  Integrity verified: hash and size match"
        return $true
    } catch {
        $downloadMs = ((Get-Date) - $downloadStart).TotalMilliseconds
        Write-WtFail "  Download failed after ${downloadMs}ms: $($_.Exception.Message)"
        return $false
    }
}

# ============================================================================
# Step 1: Authenticate both participants
# ============================================================================
Write-WtStep "Step 1: Authenticate Participants"
$totalSteps++

try {
    $senderAuth = Connect-SorchaUser `
        -TenantUrl $state.tenantUrl `
        -Email $state.sender.email `
        -Password $state.sender.password `
        -OrganizationId $state.sender.organizationId
    Write-WtInfo "Sender authenticated"

    $receiverAuth = Connect-SorchaUser `
        -TenantUrl $state.tenantUrl `
        -Email $state.receiver.email `
        -Password $state.receiver.password `
        -OrganizationId $state.receiver.organizationId
    Write-WtInfo "Receiver authenticated"
    $stepsPassed++
} catch {
    Write-WtFail "Authentication failed: $($_.Exception.Message)"
    exit 1
}

# ============================================================================
# Step 2: Generate test file
# ============================================================================
Write-WtStep "Step 2: Generate Test File ($FileSize)"
$totalSteps++

try {
    $fileSizeBytes = ConvertTo-Bytes $FileSize
    $testContent   = New-TestFileContent -sizeBytes $fileSizeBytes -seed 85
    $fileHash      = Get-Sha256Hash -data $testContent
    $chunkCount    = [Math]::Ceiling($fileSizeBytes / (4 * 1024 * 1024))
    if ($chunkCount -eq 0) { $chunkCount = 1 }

    Write-WtInfo "Size: $($fileSizeBytes.ToString('N0')) bytes ($chunkCount chunk(s))"
    Write-WtInfo "SHA-256: $fileHash"
    $stepsPassed++
} catch {
    Write-WtFail "File generation failed: $($_.Exception.Message)"
    exit 1
}

# ============================================================================
# Step 3: Create workflow instance
# ============================================================================
Write-WtStep "Step 3: Create Workflow Instance"
$totalSteps++

try {
    $instanceBody = @{
        blueprintId = $state.blueprintId
        registerId  = $state.registerId
        tenantId    = $state.sender.organizationId
        metadata    = @{ source = "walkthrough"; test = "payload" }
    }

    $instance = Invoke-SorchaApi -Method POST `
        -Uri "$($state.blueprintUrl)/instances/" `
        -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $($senderAuth.Token)" } `
        -ShowJson:$ShowJson

    $instanceId = $instance.id
    if (-not $instanceId) { $instanceId = $instance.instanceId }
    Write-WtSuccess "Instance: $instanceId"
    $stepsPassed++
} catch {
    Write-WtFail "Instance creation failed: $($_.Exception.Message)"
    exit 1
}

# ============================================================================
# Rounds: Upload → Execute → Download → Verify
# ============================================================================
for ($round = 1; $round -le $Rounds; $round++) {
    Write-WtBanner "Round $round of $Rounds"

    # -- Upload file chunks --
    Write-WtStep "Round $round — Upload File Chunks"
    $totalSteps++

    try {
        # Generate unique content per round (different seed)
        if ($round -gt 1) {
            $testContent = New-TestFileContent -sizeBytes $fileSizeBytes -seed (85 + $round)
            $fileHash    = Get-Sha256Hash -data $testContent
            Write-WtInfo "Round $round SHA-256: $fileHash"
        }

        $uploadStart = Get-Date
        $fileRef = Send-FileChunks `
            -FileContent $testContent `
            -FileName "test-file-round$round.bin" `
            -ContentType "application/octet-stream" `
            -FileHash $fileHash `
            -SenderWallet $state.sender.walletAddress `
            -RegisterAddress $state.registerId `
            -Token $senderAuth.Token `
            -GatewayUrl $state.gatewayUrl
        $uploadMs = ((Get-Date) - $uploadStart).TotalMilliseconds

        Write-WtSuccess ("Uploaded {0:N0} bytes in {1:N0}ms ({2} chunks)" -f `
            $fileSizeBytes, $uploadMs, $fileRef.chunkTransactionIds.Count)
        $stepsPassed++
    } catch {
        Write-WtFail "Upload failed: $($_.Exception.Message)"
        continue
    }

    # -- Execute send action --
    Write-WtStep "Round $round — Execute Send Action"
    $totalSteps++

    try {
        $actionResult = Invoke-SorchaAction `
            -BlueprintUrl $state.blueprintUrl `
            -InstanceId $instanceId `
            -ActionId "0" `
            -BlueprintId $state.blueprintId `
            -SenderWallet $state.sender.walletAddress `
            -RegisterId $state.registerId `
            -Token $senderAuth.Token `
            -PayloadData @{
                message    = "Round $round test file ($FileSize)"
                attachment = $fileRef
            }

        # Action execution is async — the initial response may have an empty transactionId.
        # Poll the instance until action 1 becomes current (action 0 has been sealed).
        Write-WtInfo "  Polling for action 0 to seal..."
        $inst = Wait-ForActionReady `
            -BlueprintUrl $state.blueprintUrl `
            -InstanceId $instanceId `
            -ExpectedActionId 1 `
            -Token $senderAuth.Token `
            -MaxWaitSeconds 30

        # Use lastTransactionId from the polled instance as the reliable transaction ID
        $actionTxId = $inst.lastTransactionId
        if (-not $actionTxId) {
            $actionTxId = $actionResult.transactionId
        }
        Write-WtSuccess "Action sealed: $actionTxId"
        $stepsPassed++
    } catch {
        Write-WtFail "Action execution failed: $($_.Exception.Message)"
        continue
    }

    # -- Download and verify --
    Write-WtStep "Round $round — Download & Verify File"
    $totalSteps++

    try {
        # Download as sender (has the wrapped key in encrypted payload).
        # TODO: Add disclosure rules to blueprint to wrap for both sender+receiver.
        $verified = Receive-AndVerifyFile `
            -WalletAddress $state.sender.walletAddress `
            -RegisterId $state.registerId `
            -ActionTxId $actionTxId `
            -FieldName "attachment" `
            -ExpectedHash $fileHash `
            -ExpectedSize $fileSizeBytes `
            -Token $senderAuth.Token `
            -GatewayUrl $state.gatewayUrl

        if ($verified) {
            $stepsPassed++
        }
    } catch {
        Write-WtFail "Verification failed: $($_.Exception.Message)"
    }

    # -- Receiver acknowledges --
    Write-WtStep "Round $round — Receiver Acknowledges"
    $totalSteps++

    try {
        $ackResult = Invoke-SorchaAction `
            -BlueprintUrl $state.blueprintUrl `
            -InstanceId $instanceId `
            -ActionId "1" `
            -BlueprintId $state.blueprintId `
            -SenderWallet $state.receiver.walletAddress `
            -RegisterId $state.registerId `
            -Token $receiverAuth.Token `
            -PayloadData @{
                acknowledged = $true
            }

        Write-WtSuccess "Receiver acknowledged"
        $stepsPassed++

        # If more rounds remain, wait for action 0 to become available again
        if ($round -lt $Rounds) {
            Write-WtInfo "  Polling for action 0 to become available for next round..."
            $null = Wait-ForActionReady `
                -BlueprintUrl $state.blueprintUrl `
                -InstanceId $instanceId `
                -ExpectedActionId 0 `
                -Token $receiverAuth.Token `
                -MaxWaitSeconds 30
        }
    } catch {
        Write-WtFail "Acknowledge failed: $($_.Exception.Message)"
    }
}

# ============================================================================
# Summary
# ============================================================================
$elapsed = (Get-Date) - $start
Write-Host ""
Write-Host "╔══════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║           PayloadTests Results           ║" -ForegroundColor Cyan
Write-Host "╠══════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host ("║  File size:   {0,-27}║" -f $FileSize) -ForegroundColor Cyan
Write-Host ("║  Rounds:      {0,-27}║" -f $Rounds) -ForegroundColor Cyan
Write-Host ("║  Steps:       {0}/{1}{2}║" -f $stepsPassed, $totalSteps, (' ' * (25 - "$stepsPassed/$totalSteps".Length))) -ForegroundColor Cyan
Write-Host ("║  Duration:    {0,-27}║" -f $elapsed.ToString('mm\:ss\.fff')) -ForegroundColor Cyan
Write-Host ("║  Status:      {0,-27}║" -f $(if ($stepsPassed -eq $totalSteps) { "ALL PASSED" } else { "SOME FAILED" })) -ForegroundColor $(if ($stepsPassed -eq $totalSteps) { "Green" } else { "Red" })
Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan

if ($stepsPassed -lt $totalSteps) { exit 1 }

#!/usr/bin/env pwsh
# Debug payload hash computation

$payload = @{
    testData = "HELLO"
    timestamp = "2026-02-13T20:00:00.0000000+00:00"
    sequence = 1
}

# Method 1: PowerShell serialization
$json1 = $payload | ConvertTo-Json -Compress
Write-Host "Method 1 (PowerShell direct):" -ForegroundColor Yellow
Write-Host $json1

# Method 2: PowerShell -> Object -> PowerShell
$json2 = ($payload | ConvertTo-Json | ConvertFrom-Json) | ConvertTo-Json -Compress
Write-Host "`nMethod 2 (PowerShell roundtrip):" -ForegroundColor Yellow
Write-Host $json2

# Method 3: Sorted keys
$sortedPayload = [ordered]@{
    sequence = $payload.sequence
    testData = $payload.testData
    timestamp = $payload.timestamp
}
$json3 = $sortedPayload | ConvertTo-Json -Compress
Write-Host "`nMethod 3 (Sorted keys):" -ForegroundColor Yellow
Write-Host $json3

# Compute hashes
$sha256 = [System.Security.Cryptography.SHA256]::Create()

$hash1 = [BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($json1))).Replace('-', '').ToLowerInvariant()
$hash2 = [BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($json2))).Replace('-', '').ToLowerInvariant()
$hash3 = [BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($json3))).Replace('-', '').ToLowerInvariant()

Write-Host "`nHash 1: $hash1" -ForegroundColor Cyan
Write-Host "Hash 2: $hash2" -ForegroundColor Cyan
Write-Host "Hash 3: $hash3" -ForegroundColor Cyan
Write-Host "`nAll same: $($hash1 -eq $hash2 -and $hash2 -eq $hash3)" -ForegroundColor $(if ($hash1 -eq $hash2 -and $hash2 -eq $hash3) { "Green" } else { "Red" })

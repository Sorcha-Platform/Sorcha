#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# initialize-secrets.ps1 — Generate walkthrough credentials in .secrets/passwords.json.
# Run once before using any walkthrough. Safe to re-run (produces identical output).
#
# Usage:
#   pwsh walkthroughs/initialize-secrets.ps1
#   pwsh walkthroughs/initialize-secrets.ps1 -Force  # Overwrite existing

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$secretsDir = Join-Path $scriptDir ".secrets"
$passwordsFile = Join-Path $secretsDir "passwords.json"

Write-Host ""
Write-Host "Sorcha Walkthrough — Secrets Initialization" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Check if already exists
if ((Test-Path $passwordsFile) -and -not $Force) {
    Write-Host "[!] Secrets file already exists: $passwordsFile" -ForegroundColor Yellow
    Write-Host "    Use -Force to regenerate all passwords." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# Ensure directory exists
if (-not (Test-Path $secretsDir)) {
    New-Item -ItemType Directory -Path $secretsDir -Force | Out-Null
}

# All walkthroughs use the platform seed admin (created by DatabaseInitializer on startup).
# This matches the credentials in Sorcha.Tenant.Service/Data/DatabaseInitializer.cs:
#   DefaultAdminEmail    = "admin@sorcha.local"
#   DefaultAdminPassword = "Dev_Pass_2025!"
$platformEmail    = "admin@sorcha.local"
$platformPassword = "Dev_Pass_2025!"
$platformName     = "System Administrator"

# Define all walkthrough credential sets
$secrets = [ordered]@{
    "_meta" = @{
        generatedAt = (Get-Date -Format "o")
        description = "Auto-generated walkthrough credentials. Do NOT commit to source control."
        note        = "All walkthroughs use the platform seed admin (DatabaseInitializer defaults)."
    }
    "platform" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "admin-integration" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "mcp-server" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "register-demo" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "wallet-verify" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "register-mongodb" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "construction-permit" = @{
        meridianAdminEmail    = $platformEmail
        meridianAdminPassword = $platformPassword
        # Multi-org user credentials (all use same dev password for walkthrough simplicity)
        contractorEmail       = "contractor@meridian.local"
        contractorPassword    = "Dev_Pass_2025!"
        contractorName        = "Site Manager"
        engineerEmail         = "engineer@apex.local"
        engineerPassword      = "Dev_Pass_2025!"
        engineerName          = "Lead Engineer"
        planningEmail         = "planning@riverside.local"
        planningPassword      = "Dev_Pass_2025!"
        planningName          = "Planning Officer"
        environmentalEmail    = "environmental@greenvalley.local"
        environmentalPassword = "Dev_Pass_2025!"
        environmentalName     = "Environmental Consultant"
        inspectorEmail        = "inspector@riverside.local"
        inspectorPassword     = "Dev_Pass_2025!"
        inspectorName         = "Building Control Inspector"
    }
    "health-declaration" = @{
        adminEmail     = $platformEmail
        adminPassword  = $platformPassword
        adminName      = $platformName
        patientEmail   = "patient@health-demo.local"
        patientPassword = "Dev_Pass_2025!"
        patientName    = "Demo Patient"
    }
    "form-coverage" = @{
        adminEmail        = $platformEmail
        adminPassword     = $platformPassword
        adminName         = $platformName
        submitterEmail    = "submitter@form-coverage.local"
        submitterPassword = "Dev_Pass_2025!"
        submitterName     = "Demo Submitter"
        reviewerEmail     = "reviewer@form-coverage.local"
        reviewerPassword  = "Dev_Pass_2025!"
        reviewerName      = "Demo Reviewer"
    }
    "payload-test" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
        senderEmail   = "sender@payload-sender.local"
        senderPassword = "Dev_Pass_2025!"
        senderName    = "Sender"
        receiverEmail  = "receiver@payload-receiver.local"
        receiverPassword = "Dev_Pass_2025!"
        receiverName   = "Receiver"
    }
    "self-build-house" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "trade-finance" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "dist-register" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
    "perf" = @{
        adminEmail    = $platformEmail
        adminPassword = $platformPassword
        adminName     = $platformName
    }
}

# Write to file
$json = $secrets | ConvertTo-Json -Depth 5
Set-Content -Path $passwordsFile -Value $json -Encoding UTF8

Write-Host "[OK] Secrets generated: $passwordsFile" -ForegroundColor Green
Write-Host ""
Write-Host "Platform admin: $platformEmail" -ForegroundColor Yellow
Write-Host ""
Write-Host "Walkthrough credential sets:" -ForegroundColor Yellow

$walkthroughCount = 0
foreach ($key in $secrets.Keys) {
    if ($key -eq "_meta" -or $key -eq "platform") { continue }
    $walkthroughCount++
    Write-Host "  $key" -ForegroundColor White
}

Write-Host ""
Write-Host "$walkthroughCount walkthrough credential sets generated." -ForegroundColor Green
Write-Host ""
Write-Host "IMPORTANT: This file contains passwords. Do NOT commit it to source control." -ForegroundColor Red
Write-Host "           The walkthroughs/.secrets/ directory is already in .gitignore." -ForegroundColor Red
Write-Host ""

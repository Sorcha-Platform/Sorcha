# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Pester runner for the Assured Identity demo toolkit unit tests.
#   pwsh -File demos/AssuredIdentity/tests/Invoke-DemoTests.ps1
param([switch]$CI)

$ErrorActionPreference = 'Stop'
Import-Module Pester -MinimumVersion 5.0.0 -Force

$config = New-PesterConfiguration
$config.Run.Path = $PSScriptRoot
$config.Output.Verbosity = if ($CI) { 'Normal' } else { 'Detailed' }
$config.Run.Exit = $true

Invoke-Pester -Configuration $config

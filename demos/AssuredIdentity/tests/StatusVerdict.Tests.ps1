# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

BeforeAll {
    . (Join-Path $PSScriptRoot "../lib/Readiness.ps1")     # Test-SubscriberReady dependency
    . (Join-Path $PSScriptRoot "../lib/StatusVerdict.ps1")

    function New-Sub([string]$id, [string]$sub, [string]$sync, [string[]]$bps) {
        [pscustomobject]@{ NodeId = $id; SubscriptionStatus = $sub; SyncState = $sync; PublishedBlueprintIds = $bps; TargetBlueprintId = 'bp-1' }
    }
}

Describe "Get-ReadinessVerdict" {
    It "is Ready when issuer up, approver present, and a subscriber is fully ready" {
        $v = Get-ReadinessVerdict -IssuerReachable $true -ApproverPresent $true -Subscribers @(New-Sub 'n1' 'Active' 'CaughtUp' @('bp-1'))
        $v.Verdict | Should -Be 'Ready'
        $v.Reasons | Should -BeNullOrEmpty
    }

    It "is NotReady when the issuer is unreachable" {
        $v = Get-ReadinessVerdict -IssuerReachable $false -ApproverPresent $true -Subscribers @(New-Sub 'n1' 'Active' 'CaughtUp' @('bp-1'))
        $v.Verdict | Should -Be 'NotReady'
        $v.Reasons | Should -Contain 'issuer-unreachable'
    }

    It "is NotReady when the approver is absent" {
        $v = Get-ReadinessVerdict -IssuerReachable $true -ApproverPresent $false -Subscribers @(New-Sub 'n1' 'Active' 'CaughtUp' @('bp-1'))
        $v.Reasons | Should -Contain 'approver-absent'
    }

    It "is NotReady with no subscribers" {
        $v = Get-ReadinessVerdict -IssuerReachable $true -ApproverPresent $true -Subscribers @()
        $v.Reasons | Should -Contain 'no-subscribers'
    }

    It "is NotReady when no subscriber is ready, and reports per-node reasons" {
        $v = Get-ReadinessVerdict -IssuerReachable $true -ApproverPresent $true -Subscribers @(New-Sub 'n1' 'Active' 'CaughtUp' @())
        $v.Verdict | Should -Be 'NotReady'
        $v.Reasons | Should -Contain 'no-subscriber-ready'
        $v.PerNode[0].Reasons | Should -Contain 'blueprint-not-published'
    }

    It "is Ready if at least one of several subscribers is ready" {
        $v = Get-ReadinessVerdict -IssuerReachable $true -ApproverPresent $true -Subscribers @(
            (New-Sub 'n1' 'Active' 'CaughtUp' @()),
            (New-Sub 'n2' 'Active' 'CaughtUp' @('bp-1'))
        )
        $v.Verdict | Should -Be 'Ready'
    }
}

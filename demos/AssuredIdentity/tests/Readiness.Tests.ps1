# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

BeforeAll {
    . (Join-Path $PSScriptRoot "../lib/Readiness.ps1")
}

Describe "Test-SubscriberReady" {
    It "is Ready when all three signals are good" {
        $v = Test-SubscriberReady -SubscriptionStatus 'Active' -SyncState 'CaughtUp' -PublishedBlueprintIds @('bp-1') -TargetBlueprintId 'bp-1'
        $v.Ready | Should -BeTrue
        $v.Reasons | Should -BeNullOrEmpty
    }

    It "is NotReady with blueprint-not-published when recovery is in progress" {
        $v = Test-SubscriberReady -SubscriptionStatus 'Active' -SyncState 'CaughtUp' -PublishedBlueprintIds @() -TargetBlueprintId 'bp-1'
        $v.Ready | Should -BeFalse
        $v.Reasons | Should -Contain 'blueprint-not-published'
    }

    It "reports each failing signal" {
        $v = Test-SubscriberReady -SubscriptionStatus 'Pending' -SyncState 'Syncing' -PublishedBlueprintIds @() -TargetBlueprintId 'bp-1'
        $v.Reasons.Count | Should -Be 3
        ($v.Reasons -join ';') | Should -BeLike "*subscription-not-active*"
        ($v.Reasons -join ';') | Should -BeLike "*sync-state-not-caughtup*"
    }
}

Describe "Wait-SubscriberReady" {
    It "returns Ready immediately when signals are already good" {
        $v = Wait-SubscriberReady -GetSubscriptionStatus { 'Active' } -GetSyncState { 'CaughtUp' } `
            -GetPublishedBlueprintIds { @('bp-1') } -TargetBlueprintId 'bp-1' -TimeoutSeconds 5 -PollSeconds 1
        $v.Status | Should -Be 'Ready'
    }

    It "becomes Ready once the blueprint appears (recovery window)" {
        $script:calls = 0
        $published = { $script:calls++; if ($script:calls -ge 3) { @('bp-1') } else { @() } }
        $v = Wait-SubscriberReady -GetSubscriptionStatus { 'Active' } -GetSyncState { 'CaughtUp' } `
            -GetPublishedBlueprintIds $published -TargetBlueprintId 'bp-1' -TimeoutSeconds 10 -PollSeconds 1
        $v.Status | Should -Be 'Ready'
        $script:calls | Should -BeGreaterOrEqual 3
    }

    It "returns NotReady with reasons on timeout (never throws)" {
        $v = Wait-SubscriberReady -GetSubscriptionStatus { 'Active' } -GetSyncState { 'CaughtUp' } `
            -GetPublishedBlueprintIds { @() } -TargetBlueprintId 'bp-1' -TimeoutSeconds 2 -PollSeconds 1
        $v.Status | Should -Be 'NotReady'
        $v.Reasons | Should -Contain 'blueprint-not-published'
    }
}

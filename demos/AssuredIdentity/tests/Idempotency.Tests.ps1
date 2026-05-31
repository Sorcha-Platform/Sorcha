# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

BeforeAll {
    . (Join-Path $PSScriptRoot "../lib/Idempotency.ps1")
}

Describe "Resolve-AuthorityAction" {
    It "reuses a fully-present, readable authority" {
        Resolve-AuthorityAction -HasOrg $true -HasRegisterId $true -RegisterReadable $true -BlueprintPublished $true -Force $false | Should -Be 'Reuse'
    }

    It "reconciles when a recorded register is not readable (the stale footgun)" {
        Resolve-AuthorityAction -HasOrg $true -HasRegisterId $true -RegisterReadable $false -BlueprintPublished $false -Force $false | Should -Be 'ReconcileStale'
    }

    It "creates when nothing exists" {
        Resolve-AuthorityAction -HasOrg $false -HasRegisterId $false -RegisterReadable $false -BlueprintPublished $false -Force $false | Should -Be 'Create'
    }

    It "creates (recreates) under -Force even if present" {
        Resolve-AuthorityAction -HasOrg $true -HasRegisterId $true -RegisterReadable $true -BlueprintPublished $true -Force $true | Should -Be 'Create'
    }

    It "creates when register readable but blueprint not yet published" {
        Resolve-AuthorityAction -HasOrg $true -HasRegisterId $true -RegisterReadable $true -BlueprintPublished $false -Force $false | Should -Be 'Create'
    }
}

Describe "Resolve-SubscriptionAction" {
    It "reuses an Active subscription to a readable register" {
        Resolve-SubscriptionAction -SubscriptionStatus 'Active' -RegisterReadable $true | Should -Be 'ReuseSubscription'
    }

    It "reconciles a subscription whose register is not readable" {
        Resolve-SubscriptionAction -SubscriptionStatus 'Active' -RegisterReadable $false | Should -Be 'ReconcileStaleSubscription'
    }

    It "creates when there is no subscription" {
        Resolve-SubscriptionAction -SubscriptionStatus '' -RegisterReadable $true | Should -Be 'CreateSubscription'
    }

    It "creates when subscription is Pending" {
        Resolve-SubscriptionAction -SubscriptionStatus 'Pending' -RegisterReadable $true | Should -Be 'CreateSubscription'
    }
}

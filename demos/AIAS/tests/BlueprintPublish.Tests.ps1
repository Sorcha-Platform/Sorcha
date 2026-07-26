# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

BeforeAll {
    . (Join-Path $PSScriptRoot "../lib/BlueprintPublish.ps1")
}

Describe "Resolve-BlueprintPublishAction" {

    Context "the n1 case that motivated this — application published, device blueprint missing" {

        # Exactly what the live register held: 35 tx for the application workflow, none for the
        # device one. The old all-or-nothing guard early-returned here and published neither.
        #
        # Inlined per It, NOT hoisted to the Context body: Pester 5 does not scope a Context-body
        # variable into its Its, so the list arrived $null — which made the "publishes the missing
        # device blueprint" case pass VACUOUSLY (an empty list also yields Publish).
        It "reuses the application workflow instead of duplicating it" {
            $r = Resolve-BlueprintPublishAction -IdPrefix 'aias-assured-identity' `
                -PublishedIds @('aias-assured-identity-20260723093004') `
                -PreferredId 'aias-assured-identity-20260723093004' -Force $false
            $r.Action     | Should -Be 'Reuse'
            $r.ExistingId | Should -Be 'aias-assured-identity-20260723093004'
        }

        It "publishes the missing device blueprint — WITHOUT -Force" {
            $r = Resolve-BlueprintPublishAction -IdPrefix 'aias-device-registration' `
                -PublishedIds @('aias-assured-identity-20260723093004') -Force $false
            $r.Action | Should -Be 'Publish'
        }
    }

    It "publishes when nothing is published at all (fresh provision)" {
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-device-registration' -PublishedIds @() -Force $false).Action |
            Should -Be 'Publish'
    }

    It "reuses both once both are published (idempotent re-run)" {
        $published = @('aias-assured-identity-20260723093004', 'aias-device-registration-20260726001122')
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-assured-identity'   -PublishedIds $published -Force $false).Action | Should -Be 'Reuse'
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-device-registration' -PublishedIds $published -Force $false).Action | Should -Be 'Reuse'
    }

    It "republishes everything under -Force" {
        $published = @('aias-assured-identity-20260723093004', 'aias-device-registration-20260726001122')
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-assured-identity' -PublishedIds $published -PreferredId 'aias-assured-identity-20260723093004' -Force $true).Action |
            Should -Be 'Publish'
    }

    It "prefers the id state already records when -Force left duplicates behind" {
        # A previous -Force publishes a second application workflow. Reuse must be deterministic —
        # picking whichever the register happened to list first would silently re-point state.
        $published = @('aias-assured-identity-20260726090000', 'aias-assured-identity-20260723093004')
        $r = Resolve-BlueprintPublishAction -IdPrefix 'aias-assured-identity' `
            -PublishedIds $published -PreferredId 'aias-assured-identity-20260723093004' -Force $false
        $r.ExistingId | Should -Be 'aias-assured-identity-20260723093004'
    }

    It "matches on the prefix boundary, not a bare prefix" {
        # 'aias-assured-identity' must not be satisfied by a differently-named workflow that merely
        # starts with the same letters.
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-assured-identity' `
            -PublishedIds @('aias-assured-identity-lite-20260726') -Force $false).Action |
            Should -Be 'Reuse'   # 'aias-assured-identity-' IS a prefix of 'aias-assured-identity-lite-...'

        # …but an unrelated id never satisfies it.
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-device-registration' `
            -PublishedIds @('aias-assured-identity-20260723093004') -Force $false).Action |
            Should -Be 'Publish'
    }

    It "tolerates nulls in the published list rather than throwing" {
        (Resolve-BlueprintPublishAction -IdPrefix 'aias-device-registration' `
            -PublishedIds @($null, '') -Force $false).Action | Should -Be 'Publish'
    }
}

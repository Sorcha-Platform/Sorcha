# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# US3 / FR-005 / SC-004 — re-rendering under a new agency name leaves no prior-name residue.

BeforeAll {
    $lib = Join-Path $PSScriptRoot "../lib"
    . (Join-Path $lib "Common.ps1")
    . (Join-Path $lib "AgencyNaming.ps1")
    $script:Template = '{ "p": { "organisation": "{{issuerName}}" }, "x-review": { "header": { "issuerName": "{{issuerName}}" } } }'
}

Describe "Rename coherence" {
    It "rendering under a new name contains no trace of the old name" {
        $first = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName "Acme Verification Co."
        $first | Should -BeLike "*Acme Verification Co.*"

        $second = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName "Glenmara Borough Registry"
        $second | Should -BeLike "*Glenmara Borough Registry*"
        $second | Should -Not -BeLike "*Acme*"
    }

    It "coherence holds when every site is renamed together" {
        $name = "Glenmara Borough Registry"
        $rendered = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName $name
        $r = Test-AgencyNameCoherence -AgencyName $name -OrgName $name -RegisterName $name -ParticipantOrg $name -BlueprintJson $rendered
        $r.Coherent | Should -BeTrue
    }

    It "coherence fails if any site still carries the old name" {
        $rendered = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName "Glenmara Borough Registry"
        $r = Test-AgencyNameCoherence -AgencyName "Glenmara Borough Registry" -OrgName "Acme Verification Co." `
            -RegisterName "Glenmara Borough Registry" -ParticipantOrg "Glenmara Borough Registry" -BlueprintJson $rendered
        $r.Coherent | Should -BeFalse
        ($r.Mismatches -join ';') | Should -BeLike "*org name*"
    }
}

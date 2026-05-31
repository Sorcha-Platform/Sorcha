# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

BeforeAll {
    $lib = Join-Path $PSScriptRoot "../lib"
    . (Join-Path $lib "Common.ps1")
    . (Join-Path $lib "AgencyNaming.ps1")
    $script:Template = '{ "p": { "organisation": "{{issuerName}}" }, "x-review": { "header": { "issuerName": "{{issuerName}}" } } }'
}

Describe "Set-BlueprintIssuerName" {
    It "replaces every issuerName token" {
        $out = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName "Strathcarron Identity Authority"
        $out | Should -BeLike "*Strathcarron Identity Authority*"
        (Get-DemoUnresolvedTokens -Text $out) | Should -BeNullOrEmpty
    }

    It "leaves unrelated tokens intact and still renders the issuerName" {
        $mixed = '{ "header": { "issuerName": "{{issuerName}}" }, "other": "{{somethingElse}}" }'
        $out = Set-BlueprintIssuerName -BlueprintJson $mixed -AgencyName "Acme"
        $out | Should -BeLike "*Acme*"
        $out | Should -BeLike "*{{somethingElse}}*"   # not our concern, preserved
    }
}

Describe "Test-AgencyNameCoherence" {
    It "is coherent when all sites match and no token remains" {
        $rendered = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName "Acme Verification Co."
        $r = Test-AgencyNameCoherence -AgencyName "Acme Verification Co." -OrgName "Acme Verification Co." `
            -RegisterName "Acme Verification Co." -ParticipantOrg "Acme Verification Co." -BlueprintJson $rendered
        $r.Coherent | Should -BeTrue
        $r.Mismatches | Should -BeNullOrEmpty
    }

    It "flags a mismatched org name" {
        $rendered = Set-BlueprintIssuerName -BlueprintJson $script:Template -AgencyName "Acme"
        $r = Test-AgencyNameCoherence -AgencyName "Acme" -OrgName "Stale Co." `
            -RegisterName "Acme" -ParticipantOrg "Acme" -BlueprintJson $rendered
        $r.Coherent | Should -BeFalse
        ($r.Mismatches -join ';') | Should -BeLike "*org name*"
    }

    It "flags an unresolved blueprint token" {
        $r = Test-AgencyNameCoherence -AgencyName "Acme" -OrgName "Acme" -RegisterName "Acme" `
            -ParticipantOrg "Acme" -BlueprintJson '{ "issuerName": "{{issuerName}}" }'
        $r.Coherent | Should -BeFalse
        ($r.Mismatches -join ';') | Should -BeLike "*unresolved issuerName token*"
    }
}

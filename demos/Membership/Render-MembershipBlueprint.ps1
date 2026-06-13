# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Render a lint-clean membership instance blueprint (+ POS presentation) from a
# scheme config, with no blueprint hand-editing. Extends the F144 AssuredIdentity
# provisioning pattern (demos/AssuredIdentity/AssuredIdentityDemo.psm1): it emits
# artefacts only — provisioning them onto a register reuses the existing
# blueprint-publish / register-subscription endpoints (see README.md).
#
# Usage:
#   ./Render-MembershipBlueprint.ps1                                   # uses membership.config.example.json
#   ./Render-MembershipBlueprint.ps1 -ConfigPath ./my-scheme.json
#
[CmdletBinding()]
param(
    [string]$ConfigPath  = "$PSScriptRoot/membership.config.example.json",
    [string]$TemplatePath = "$PSScriptRoot/membership.blueprint.template.jsonc",
    [string]$OutDir      = "$PSScriptRoot/instances",
    [string]$PresentationOutDir = "$PSScriptRoot/presentations"
)

$ErrorActionPreference = 'Stop'

function ConvertTo-JsonArray {
    # Guarantee a JSON array literal even for 0/1 elements (ConvertTo-Json collapses singles).
    param([object[]]$Items)
    if (-not $Items) { return '[]' }
    '[' + (($Items | ForEach-Object { ConvertTo-Json $_ -Depth 20 -Compress }) -join ',') + ']'
}

Write-Host "Reading config: $ConfigPath" -ForegroundColor Cyan
$config = Get-Content -Raw $ConfigPath | ConvertFrom-Json

# --- Minimal config sanity (full validation is membership.config.schema.json) ---
foreach ($req in @('schemeName','schemeSlug','issuerOrg','tiers','defaultTier','trustPolicy')) {
    if (-not $config.PSObject.Properties.Name.Contains($req)) {
        throw "Config is missing required field '$req'. Validate against membership.config.schema.json."
    }
}
if (-not $config.trustPolicy.sources -or $config.trustPolicy.sources.Count -lt 1) {
    throw "trustPolicy.sources must have at least one entry, or OPEN_CREDENTIAL_ISSUER will fire at publish."
}

$schemeName  = $config.schemeName
$credType    = if ($config.credentialType) { $config.credentialType } else { 'MembershipCredential/v1' }
$identType   = if ($config.identityCredentialType) { $config.identityCredentialType } else { 'AssuredIdentityCredential' }
$reqClaims   = if ($config.requiredIdentityClaims) { $config.requiredIdentityClaims } else { @('givenName','familyName','dateOfBirth') }
$disclosable = if ($config.disclosable) { $config.disclosable } else { @('givenName','familyName','dateOfBirth','memberNumber','tier') }
$expiry      = if ($config.expiryDuration) { $config.expiryDuration } else { 'P2Y' }
$sector      = if ($config.sector) { $config.sector } else { 'General' }

# --- Build JSON fragments -----------------------------------------------------
$trustPolicyJson   = ConvertTo-Json $config.trustPolicy -Depth 20 -Compress
$requiredClaimsJson = ConvertTo-JsonArray (@($reqClaims | ForEach-Object { [ordered]@{ claimName = $_ } }))

$applicantFieldsJson = if ($config.applicantFields) { ConvertTo-Json $config.applicantFields -Depth 20 -Compress } else { '{}' }
$applicantRequiredJson = ConvertTo-JsonArray (@($config.applicantRequired))

# Issue action: identity fields (materialised from carried presentation) + issuer-assigned memberNumber + tier.
$issueProps = [ordered]@{}
foreach ($c in $reqClaims) {
    $issueProps[$c] = if ($c -eq 'dateOfBirth') { [ordered]@{ type='string'; format='date'; title='Date of birth' } }
                      else { [ordered]@{ type='string'; title=$c } }
}
$issueProps['memberNumber'] = [ordered]@{ type='string'; title='Member number'; description="Assigned by $schemeName." }
$issueProps['tier']         = [ordered]@{ type='string'; enum=@($config.tiers); title='Membership tier' }
$issuePropertiesJson = ConvertTo-Json $issueProps -Depth 20 -Compress
$issueRequiredJson   = ConvertTo-JsonArray (@($reqClaims + @('memberNumber','tier')))

$claimMappings = @()
foreach ($c in ($reqClaims + @('memberNumber','tier'))) {
    $claimMappings += [ordered]@{ claimName = $c; sourceField = "/$c" }
}
$claimMappingsJson = ConvertTo-JsonArray $claimMappings
$disclosableJson   = ConvertTo-JsonArray (@($disclosable))

# --- Token substitution -------------------------------------------------------
$tpl = Get-Content -Raw $TemplatePath

$replacements = [ordered]@{
    '{{SCHEME_NAME}}'             = $schemeName
    '{{SCHEME_SLUG}}'            = $config.schemeSlug
    '{{SECTOR}}'                 = $sector
    '{{ISSUER_NAME}}'           = $config.issuerOrg.name
    '{{ISSUER_WALLET_ADDRESS}}' = $config.issuerOrg.walletAddress
    '{{CREDENTIAL_TYPE}}'       = $credType
    '{{IDENTITY_CREDENTIAL_TYPE}}' = $identType
    '{{EXPIRY_DURATION}}'       = $expiry
    '{{TRUST_POLICY_JSON}}'     = $trustPolicyJson
    '{{REQUIRED_CLAIMS_JSON}}'  = $requiredClaimsJson
    '{{APPLICANT_FIELDS_JSON}}' = $applicantFieldsJson
    '{{APPLICANT_REQUIRED_JSON}}' = $applicantRequiredJson
    '{{ISSUE_PROPERTIES_JSON}}' = $issuePropertiesJson
    '{{ISSUE_REQUIRED_JSON}}'   = $issueRequiredJson
    '{{CLAIM_MAPPINGS_JSON}}'   = $claimMappingsJson
    '{{DISCLOSABLE_JSON}}'      = $disclosableJson
}
foreach ($k in $replacements.Keys) { $tpl = $tpl.Replace($k, [string]$replacements[$k]) }

# Strip comments (full-line // and block /* */) so the result is strict JSON.
# Only strips lines whose first non-space chars are // — never touches https:// inside strings.
$tpl = [System.Text.RegularExpressions.Regex]::Replace($tpl, '(?m)^\s*//.*$', '')
$tpl = [System.Text.RegularExpressions.Regex]::Replace($tpl, '/\*.*?\*/', '', 'Singleline')

# Validate it parses, then re-emit canonical JSON.
try { $parsed = $tpl | ConvertFrom-Json }
catch { throw "Rendered blueprint is not valid JSON after substitution: $($_.Exception.Message)" }

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$outFile = Join-Path $OutDir "$($config.schemeSlug).blueprint.json"
($parsed | ConvertTo-Json -Depth 40) | Set-Content -Path $outFile -Encoding utf8
Write-Host "  -> blueprint: $outFile" -ForegroundColor Green

# --- POS presentation ---------------------------------------------------------
$pos = [ordered]@{
    '$comment' = "POS presentation request for $schemeName. Verifier requests memberNumber + tier only; identity withheld via limit_disclosure=required. Discount is verifier policy (verifierProfile), never minted."
    verifierRequest = [ordered]@{ requiredVct = $credType; requiredClaims = @('memberNumber','tier'); optionalClaims = @(); purpose = "Verify membership at point of sale" }
    presentationDefinition = [ordered]@{
        id = "$($config.schemeSlug)-pos"
        input_descriptors = @(
            [ordered]@{
                id = 'primary'; name = $credType; purpose = 'Verify membership at point of sale'
                constraints = [ordered]@{
                    limit_disclosure = 'required'
                    fields = @(
                        [ordered]@{ path = @('$.vct'); filter = [ordered]@{ type='string'; const=$credType } },
                        [ordered]@{ path = @('$.memberNumber'); optional = $false },
                        [ordered]@{ path = @('$.tier'); optional = $false }
                    )
                }
            }
        )
    }
    consentSurface = [ordered]@{ disclosedFields = @('memberNumber','tier'); withheldFields = @($reqClaims) }
}
if (-not (Test-Path $PresentationOutDir)) { New-Item -ItemType Directory -Path $PresentationOutDir | Out-Null }
$posFile = Join-Path $PresentationOutDir "$($config.schemeSlug)-pos.presentation.json"
($pos | ConvertTo-Json -Depth 40) | Set-Content -Path $posFile -Encoding utf8
Write-Host "  -> POS presentation: $posFile" -ForegroundColor Green

Write-Host "Done. Publish the blueprint with the issuer wallet bound via your register's blueprint-publish flow (README.md)." -ForegroundColor Cyan

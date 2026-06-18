<#
.SYNOPSIS
  Print the version CI would derive (versionName from the latest v* tag, versionCode from a
  build number). Versioning is normally fully handled inside build.yml; this is for local /
  on-call (trigger-build.ps1) builds where you want to mirror the CI scheme.

.DESCRIPTION
  No manual bumping: versionName = latest reachable v* tag minus the leading 'v' (else
  0.0.0-dev); versionCode = -BuildNumber (CI uses the Actions run number). Emits values you
  can pass to trigger-build.ps1.

.EXAMPLE
  ./bump-version.ps1 -BuildNumber 57
  # -> versionName=1.4.0 versionCode=57
#>
[CmdletBinding()]
param(
  [string]$BuildNumber = '0',
  [string]$Mac = 'stuart@macmini',
  [string]$RepoDir = '~/projects/Sorcha'
)
$ErrorActionPreference = 'Stop'

$tag = (ssh $Mac "zsh -lc 'cd $RepoDir && git describe --tags --match `"v*`" --abbrev=0 2>/dev/null'").Trim()
if ($tag -match '^v(.+)$') { $versionName = $Matches[1] } else { $versionName = '0.0.0-dev' }

Write-Host "versionName=$versionName versionCode=$BuildNumber"
Write-Host "`nUse with:  ./trigger-build.ps1 -VersionName $versionName -VersionCode $BuildNumber" -ForegroundColor DarkGray

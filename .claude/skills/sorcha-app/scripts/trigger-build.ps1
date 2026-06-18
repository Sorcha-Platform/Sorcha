<#
.SYNOPSIS
  Run a fastlane lane on the Mac build node ON-CALL — a direct SSH build that bypasses
  GitHub Actions (for interactive / debugging builds). For the unattended path use
  dispatch-workflow.ps1 instead.

.DESCRIPTION
  Orchestrator-side script (runs on the Windows dev box). MUST use Windows OpenSSH — Git
  Bash has no agent socket and cannot authenticate to the Mac. Invoke from PowerShell.

  The build engine itself lives in the repo (mobile/wallet fastlane); this only triggers it.

.EXAMPLE
  ./trigger-build.ps1 -Lane android_adhoc
.EXAMPLE
  ./trigger-build.ps1 -Lane android_adhoc -VersionName 1.4.0 -VersionCode 57
#>
[CmdletBinding()]
param(
  [ValidateSet('android_adhoc','android_internal','ios_adhoc','ios_beta')]
  [string]$Lane = 'android_adhoc',
  [string]$Mac = 'stuart@macmini',
  [string]$RepoDir = '~/projects/Sorcha',
  [string]$VersionName = '',
  [string]$VersionCode = ''
)
$ErrorActionPreference = 'Stop'

$envPrefix = ''
if ($VersionName) { $envPrefix += "SORCHA_VERSION_NAME=$VersionName " }
if ($VersionCode) { $envPrefix += "SORCHA_VERSION_CODE=$VersionCode " }

# zsh -lc so the login shell loads ~/.zprofile (JAVA_HOME/ANDROID_HOME/dotnet/fastlane).
# BUNDLE_GEMFILE= avoids bundler until Gemfile.lock exists (blocked on Xcode 17/clang 17).
$remote = "cd $RepoDir/mobile/wallet && ${envPrefix}BUNDLE_GEMFILE= fastlane $Lane"
Write-Host "==> $Mac : fastlane $Lane" -ForegroundColor Cyan
ssh $Mac "zsh -lc '$remote'"
exit $LASTEXITCODE

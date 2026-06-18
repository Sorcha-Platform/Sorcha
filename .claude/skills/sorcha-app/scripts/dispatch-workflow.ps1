<#
.SYNOPSIS
  Fire the unattended build.yml workflow on demand (workflow_dispatch) and report the run.

.DESCRIPTION
  Orchestrator-side (Windows). Drives `gh workflow run` on the Mac. workflow_dispatch only
  works once build.yml is on the DEFAULT branch (master). Before merge, trigger by pushing
  a v* tag instead (see SKILL.md). After dispatch, pair with watch-build.ps1.

.EXAMPLE
  ./dispatch-workflow.ps1 -Lane android_adhoc
.EXAMPLE
  ./dispatch-workflow.ps1 -Lane android_adhoc -Ref master
#>
[CmdletBinding()]
param(
  [ValidateSet('android_adhoc','android_internal','ios_adhoc','ios_beta')]
  [string]$Lane = 'android_adhoc',
  [string]$Ref = 'master',
  [string]$Mac = 'stuart@macmini',
  [string]$RepoDir = '~/projects/Sorcha'
)
$ErrorActionPreference = 'Stop'

Write-Host "==> dispatch build.yml  lane=$Lane  ref=$Ref" -ForegroundColor Cyan
ssh $Mac "zsh -lc 'cd $RepoDir && gh workflow run build.yml --ref $Ref -f lane=$Lane'"
Start-Sleep -Seconds 6
Write-Host 'Latest run:'
ssh $Mac "zsh -lc 'cd $RepoDir && gh run list --workflow build.yml --limit 1'"
Write-Host "`nWatch it with:  ./watch-build.ps1" -ForegroundColor DarkGray

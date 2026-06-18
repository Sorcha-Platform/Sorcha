<#
.SYNOPSIS
  Watch a GitHub Actions run on the Mac self-hosted runner to completion, then summarise
  pass/fail and, on failure, extract the most relevant error lines.

.DESCRIPTION
  Orchestrator-side (Windows). Uses Windows OpenSSH to drive `gh` on the Mac (gh is
  authenticated there). Pass a run id, or omit to watch the latest build.yml run.

.EXAMPLE
  ./watch-build.ps1 -RunId 27789890516
.EXAMPLE
  ./watch-build.ps1            # watches the most recent Mobile Build run
#>
[CmdletBinding()]
param(
  [string]$RunId = '',
  [string]$Mac = 'stuart@macmini',
  [string]$RepoDir = '~/projects/Sorcha'
)
$ErrorActionPreference = 'Stop'

if (-not $RunId) {
  $out = ssh $Mac "zsh -lc 'cd $RepoDir && gh run list --workflow build.yml --limit 1 --json databaseId --jq .[].databaseId'"
  $RunId = ("$out").Trim()
  if (-not $RunId) { Write-Error 'No Mobile Build runs found.'; exit 1 }
  Write-Host "Latest Mobile Build run: $RunId" -ForegroundColor Cyan
}

ssh $Mac "zsh -lc 'cd $RepoDir && gh run watch $RunId --exit-status --interval 15'"
$rc = $LASTEXITCODE

if ($rc -eq 0) {
  Write-Host "`n==> PASS (run $RunId)" -ForegroundColor Green
  Write-Host 'Artifacts:'
  ssh $Mac "zsh -lc 'cd $RepoDir && gh run view $RunId --json artifacts --jq `".artifacts[]?.name`" 2>/dev/null'"
} else {
  Write-Host "`n==> FAIL (run $RunId, exit $rc) — extracted reason:" -ForegroundColor Red
  ssh $Mac "zsh -lc 'cd $RepoDir && gh run view $RunId --log-failed 2>/dev/null | grep -iE `"error|fail|exception|FAILURE|not found|invalid`" | head -30'"
}
exit $rc

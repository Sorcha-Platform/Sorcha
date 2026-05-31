# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Approval-agent rendering + launch for rules/ai/human (FR-010/011/012, R6).

Set-StrictMode -Version Latest

$script:DemoAiDecisionWaitSeconds = 90

<#
.SYNOPSIS
    Render an agent actor config from a tokenised template.
.DESCRIPTION
    Substitutes {{...}} tokens (gateway, registerId, orgId, analystWallet,
    analystEmail, blueprintId) into the template and writes the rendered config.
    Returns the output path. Throws if any token is left unresolved.
.PARAMETER TemplatePath
    Path to analyst.<mode>.template.json.
.PARAMETER Tokens
    Hashtable of substitution values.
.PARAMETER OutputPath
    Where to write the rendered actor config.
#>
function New-AgentActorConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TemplatePath,
        [Parameter(Mandatory)][hashtable]$Tokens,
        [Parameter(Mandatory)][string]$OutputPath
    )
    if (-not (Test-Path -LiteralPath $TemplatePath)) {
        throw "Agent template not found: $TemplatePath"
    }
    $raw = Get-Content -LiteralPath $TemplatePath -Raw
    $rendered = Expand-DemoTokens -Text $raw -Tokens $Tokens
    $unresolved = @(Get-DemoUnresolvedTokens -Text $rendered)
    if ($unresolved.Count -gt 0) {
        throw "Agent config still has unresolved tokens: $($unresolved -join ', ')"
    }
    $rendered | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    return $OutputPath
}

<#
.SYNOPSIS
    Launch (or instruct) the approval agent for the chosen mode.
.DESCRIPTION
    - rules: render config, launch `sorcha-agent run` as a tracked child process.
    - ai   : precheck ANTHROPIC_API_KEY, render config (carrying the decision-wait
             guardrail), launch. A slow/failed decision surfaces via Get-DemoStatus
             rather than auto-degrading the engine (R6).
    - human: no launch — print approval instructions and return $null process.
    Returns { Mode; Process; ConfigPath; Guardrail }.
.PARAMETER Mode
    rules | ai | human.
.PARAMETER TemplateDir
    Directory holding analyst.rules.template.json / analyst.ai.template.json / analyst.persona.md.
.PARAMETER Tokens
    Substitution values for the actor config.
.PARAMETER StatePath
    Path to state.json (passed to sorcha-agent for {{placeholder}} resolution).
.PARAMETER WorkDir
    Where rendered configs are written.
.PARAMETER IssuerGateway
    Issuer gateway URL (for the human-mode instructions).
#>
function Start-ApprovalAgent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('rules', 'ai', 'human')][string]$Mode,
        [Parameter(Mandatory)][string]$TemplateDir,
        [Parameter(Mandatory)][hashtable]$Tokens,
        [Parameter(Mandatory)][string]$StatePath,
        [Parameter(Mandatory)][string]$WorkDir,
        [string]$IssuerGateway = ""
    )

    if ($Mode -eq 'human') {
        Write-WtStep "Agent mode: human"
        Write-WtInfo  "No approval agent launched. To approve applications:"
        Write-WtInfo  "  1. Log into the issuer node ($IssuerGateway) as the verification analyst."
        Write-WtInfo  "  2. Open the pending 'Verify Assured Identity Application' (Action 2)."
        Write-WtInfo  "  3. Set decision = approved and submit."
        return [pscustomobject]@{ Mode = 'human'; Process = $null; ConfigPath = $null; Guardrail = $null }
    }

    if ($Mode -eq 'ai' -and [string]::IsNullOrWhiteSpace($env:ANTHROPIC_API_KEY)) {
        throw "Agent mode 'ai' requires the ANTHROPIC_API_KEY environment variable to be set."
    }

    $templatePath = Join-Path $TemplateDir "analyst.$Mode.template.json"
    $configPath = Join-Path $WorkDir "analyst.$Mode.json"
    New-AgentActorConfig -TemplatePath $templatePath -Tokens $Tokens -OutputPath $configPath | Out-Null

    $guardrail = $null
    if ($Mode -eq 'ai') {
        $guardrail = [pscustomobject]@{ DecisionWaitSeconds = $script:DemoAiDecisionWaitSeconds; OnTimeout = 'surface-status' }
    }

    Write-WtStep "Agent mode: $Mode — launching sorcha-agent"
    $agentCmd = Get-Command 'sorcha-agent' -ErrorAction SilentlyContinue
    if (-not $agentCmd) {
        Write-WtWarn "sorcha-agent not found on PATH. Rendered config at $configPath; start it manually:"
        Write-WtInfo  "  sorcha-agent run --config `"$configPath`" --state `"$StatePath`""
        return [pscustomobject]@{ Mode = $Mode; Process = $null; ConfigPath = $configPath; Guardrail = $guardrail }
    }

    $proc = Start-Process -FilePath $agentCmd.Source `
        -ArgumentList @('run', '--config', $configPath, '--state', $StatePath) `
        -PassThru -NoNewWindow
    Write-WtSuccess "sorcha-agent ($Mode) started (pid $($proc.Id))"
    return [pscustomobject]@{ Mode = $Mode; Process = $proc; ConfigPath = $configPath; Guardrail = $guardrail }
}

# CyberEssentialsUac — Actor Configs

Three `sorcha-agent` rules-mode actor configs for the Cyber Essentials UAC walkthrough.

## Actors

| File | Role | Blueprints covered |
|------|------|--------------------|
| `assessor.json` | Cyber Assessor (Blueprint A) | Submit UAC Assessment, Issue Posture Credential, Record Non-Compliance |
| `subject-org.json` | Assessed Organisation (Blueprint B, non-gated flows / validate) | Request Cover (payload only — see note below) |
| `insurer.json` | Cyber Insurer (Blueprint B) | Issue Quote |

## Actions covered

### Blueprint A — Cyber Essentials UAC Posture Assessment

| Action | Actor | Notes |
|--------|-------|-------|
| `Submit UAC Assessment` | assessor | Empty payload (`{}`); the launcher (`run-agents.ps1`) supplies the evidence object from the data file before calling the actor |
| `Issue Posture Credential` | assessor | Reached only when `computedCompliant == true` (compliant route) |
| `Record Non-Compliance` | assessor | Reached only when `computedCompliant == false` (default/non-compliant route) |

### Blueprint B — Cyber Insurance Application

| Action | Actor | Notes |
|--------|-------|-------|
| `Request Cover` | subject-org | Credential-gated — see IMPORTANT note below |
| `Issue Quote` | insurer | |

## IMPORTANT: `Request Cover` is credential-gated and NOT driven by the actor

> The `Request Cover` action (Blueprint B) carries a `SorchaInternal` credentialRequirement. The `sorcha-agent` does NOT construct credential presentations (it sends no `credentialPresentations` in its execute body — verified in `src/Apps/Sorcha.Agent/Execution/ActionExecutor.cs`), and `ActionExecutionService` only verifies presentations supplied in the request body (it never auto-fetches stored credentials). Therefore `Request Cover` is submitted by `run-agents.ps1` via `Get-SorchaCredentialPresentation` + `Invoke-SorchaAction -CredentialPresentations`, NOT by the `subject-org` actor. The `subject-org` actor exists for `sorcha-agent validate` and for any non-gated flows; it does not drive the credential-gated submission. The assessor and insurer actors cover their non-gated actions.

The `subject-org.json` config retains a `Request Cover` rule so that `sorcha-agent validate` can perform structural validation of the actor config against the blueprint. In live execution the rule is never reached because `run-agents.ps1` handles that step with credential injection before the agent processes the inbox.

## Environment variables

Each actor config references a password via `$env:*`. Set these from `state.json` before running:

```powershell
$state = Get-Content state.json | ConvertFrom-Json
$env:ASSESSOR_PASSWORD    = $state.roles.assessor.'password'
$env:SUBJECT_ORG_PASSWORD = $state.roles.'subject-org'.'password'
$env:INSURER_PASSWORD     = $state.roles.insurer.'password'
```

## Validation (once Docker is running)

Run structural validation for each actor against the published blueprint state:

```powershell
# Assessor
$env:ASSESSOR_PASSWORD = "<from state.json>"
dotnet run --project src/Apps/Sorcha.Agent/Sorcha.Agent.csproj -- `
    validate --config walkthroughs/CyberEssentialsUac/actors/assessor.json `
             --state  walkthroughs/CyberEssentialsUac/state.json

# Subject Org
$env:SUBJECT_ORG_PASSWORD = "<from state.json>"
dotnet run --project src/Apps/Sorcha.Agent/Sorcha.Agent.csproj -- `
    validate --config walkthroughs/CyberEssentialsUac/actors/subject-org.json `
             --state  walkthroughs/CyberEssentialsUac/state.json

# Insurer
$env:INSURER_PASSWORD = "<from state.json>"
dotnet run --project src/Apps/Sorcha.Agent/Sorcha.Agent.csproj -- `
    validate --config walkthroughs/CyberEssentialsUac/actors/insurer.json `
             --state  walkthroughs/CyberEssentialsUac/state.json
```

## State.json placeholder reference

The actor configs use `{{...}}` placeholders resolved at runtime against `state.json`:

| Placeholder | Resolved from |
|-------------|--------------|
| `{{registerId}}` | `state.registerId` |
| `{{roles.assessor.email}}` | `state.roles.assessor.email` |
| `{{roles.assessor.organizationId}}` | `state.roles.assessor.organizationId` |
| `{{roles.assessor.walletAddress}}` | `state.roles.assessor.walletAddress` |
| `{{roles.subject-org.email}}` | `state.roles.subject-org.email` |
| `{{roles.subject-org.organizationId}}` | `state.roles.subject-org.organizationId` |
| `{{roles.subject-org.walletAddress}}` | `state.roles.subject-org.walletAddress` |
| `{{roles.insurer.email}}` | `state.roles.insurer.email` |
| `{{roles.insurer.organizationId}}` | `state.roles.insurer.organizationId` |
| `{{roles.insurer.walletAddress}}` | `state.roles.insurer.walletAddress` |

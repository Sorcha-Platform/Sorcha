# ConstructionPermit Actor Agents

Autonomous actor definition files for the ConstructionPermit walkthrough. Each file configures one participant to run as an independent `sorcha-agent` process.

## Prerequisites

1. Run `setup.ps1` to create orgs, wallets, participants, register, and blueprint
2. Build the agent: `dotnet build src/Apps/Sorcha.Agent/`

## Actor Files

| File | Role | Actions Handled |
|------|------|----------------|
| contractor.json | Site Manager (Stoniebridge Construction) | Submit Application |
| structural-engineer.json | Lead Engineer (Murchison Engineering) | Structural Assessment |
| planning-officer.json | Planning Officer (Strathcarron Council) | Planning Review, Final Approval |
| environmental-assessor.json | Environmental Consultant (Heatherbank Environmental) | Environmental Impact Assessment |
| building-control.json | Building Control Inspector (Strathcarron Council) | Building Control Inspection |

## Running All Actors

```powershell
# From the walkthroughs/ConstructionPermit/ directory
pwsh run-agents.ps1 -Profile gateway
```

The launcher:
1. Reads `state.json` for credentials and IDs
2. Sets password environment variables
3. Launches 5 `sorcha-agent run` processes in background
4. Waits up to 5 minutes for all to complete
5. Prints summary and cleans up

## Running a Single Actor

```bash
export CONTRACTOR_PASSWORD=your-password
dotnet run --project src/Apps/Sorcha.Agent -- run \
  --config walkthroughs/ConstructionPermit/actors/contractor.json \
  --state walkthroughs/ConstructionPermit/state.json
```

## Cross-Machine Deployment

Pre-configured remote actor files (`*-remote.json`) point to `https://n1.sorcha.dev`:

| File | Role |
|------|------|
| planning-officer-remote.json | Planning Officer |
| building-control-remote.json | Building Control Inspector |
| environmental-assessor-remote.json | Environmental Consultant |

### Automated distributed run

```powershell
# Starts local actors and prints instructions for remote machine
pwsh run-agents-distributed.ps1
```

### Manual steps for remote machine

1. Copy `*-remote.json` files and `state.json` to the remote machine
2. Set password env vars (printed by the distributed launcher)
3. Run each agent:
   ```bash
   sorcha-agent run --config planning-officer-remote.json --state state.json &
   sorcha-agent run --config building-control-remote.json --state state.json &
   sorcha-agent run --config environmental-assessor-remote.json --state state.json &
   ```

The actor file + state file is the entire deployment contract.

## Validating Config

```bash
dotnet run --project src/Apps/Sorcha.Agent -- validate \
  --config walkthroughs/ConstructionPermit/actors/contractor.json \
  --state walkthroughs/ConstructionPermit/state.json
```

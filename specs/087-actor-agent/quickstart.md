# Quickstart: Sorcha Actor Agent

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for Sorcha services)
- An existing walkthrough setup completed (e.g., ConstructionPermit)

## 1. Build the Agent

```bash
dotnet build src/Apps/Sorcha.Agent/
```

## 2. Run a Walkthrough Setup

```powershell
# Start services
docker-compose up -d

# Run ConstructionPermit setup (creates orgs, wallets, participants, register, blueprint)
pwsh walkthroughs/ConstructionPermit/setup.ps1 -Profile gateway
# Output: walkthroughs/ConstructionPermit/state.json
```

## 3. Create an Actor Definition

Create `walkthroughs/ConstructionPermit/actors/contractor.json`:

```json
{
  "actor": {
    "name": "contractor",
    "description": "Submits the planning application"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "contractor@example.com",
      "password": "$env:CONTRACTOR_PASSWORD",
      "organizationId": "{{contractorOrgId}}"
    },
    "walletAddress": "{{contractorWalletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 60 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "SubmitApplication",
      "decision": "approve",
      "payload": {
        "projectName": "Riverside Development",
        "siteAddress": "123 River Lane",
        "estimatedCost": 250000,
        "description": "Two-storey residential extension"
      }
    }
  ]
}
```

## 4. Validate the Config

```bash
export CONTRACTOR_PASSWORD=your-password-here

dotnet run --project src/Apps/Sorcha.Agent -- validate \
  --config walkthroughs/ConstructionPermit/actors/contractor.json \
  --state walkthroughs/ConstructionPermit/state.json
```

## 5. Run the Actor

```bash
dotnet run --project src/Apps/Sorcha.Agent -- run \
  --config walkthroughs/ConstructionPermit/actors/contractor.json \
  --state walkthroughs/ConstructionPermit/state.json
```

## 6. Run All Actors (Full Workflow)

```powershell
pwsh walkthroughs/ConstructionPermit/run-agents.ps1 -Profile gateway
```

## 7. Run Across Machines

Copy to remote machine:
- `sorcha-agent` binary (or use `dotnet run` if SDK is installed)
- Actor JSON files for actors assigned to that machine
- `state.json` from the setup

Update `gatewayUrl` in actor files to point to the Sorcha instance (e.g., `https://n1.sorcha.dev`).

```bash
# On remote machine
sorcha-agent run --config ./planning-officer.json --state ./state.json
```

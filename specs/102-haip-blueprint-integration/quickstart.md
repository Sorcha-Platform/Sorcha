# Quickstart: HAIP Blueprint Integration

## Prerequisites

- Docker Desktop running
- .NET 10 SDK
- PowerShell 7+

## Build and Run

```bash
# 1. Build solution
dotnet build

# 2. Start Docker stack
docker compose build --parallel
docker compose up -d

# 3. Initialize secrets
pwsh walkthroughs/initialize-secrets.ps1

# 4. Run HAIP Identity Attestation walkthrough
pwsh walkthroughs/HaipIdentityAttestation/setup.ps1 -Force
pwsh walkthroughs/HaipIdentityAttestation/run.ps1

# 5. Run HAIP Driving Licence walkthrough
pwsh walkthroughs/HaipDrivingLicence/setup.ps1 -Force
pwsh walkthroughs/HaipDrivingLicence/run.ps1

# 6. Run screenshot tests
dotnet test tests/Sorcha.UI.E2E.Tests --filter "TestCategory=HaipScreenshots" -- NUnit.NumberOfTestWorkers=1
```

## Verify

After running the walkthroughs:

1. Open http://localhost/app/auth/login
2. Login as gov-admin@haip-walkthrough.local / Dev_Pass_2025! (select "Government Identity Authority")
3. Check "My Workflows" — should show the identity attestation instance
4. Login as council-admin@haip-walkthrough.local / Dev_Pass_2025! (select "Council Licensing Authority")
5. Check "My Workflows" — should show the driving licence instance
6. Check "Pending Actions" — should show any remaining actions

## Key Changes

- `ActionSubmissionResponse` now includes `CredentialOffer` and `PresentationRequest` properties
- `ActionExecutionService` maps HAIP client results to the response
- Blueprint templates drive both HAIP walkthroughs
- Walkthrough scripts create Blueprint instances instead of calling HAIP API directly

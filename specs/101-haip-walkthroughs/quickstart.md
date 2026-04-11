# Quickstart: HAIP Walkthroughs

Manual verification procedure for the HAIP walkthrough scenarios.

## Prerequisites

- Docker Desktop running
- .NET 10 SDK installed
- PowerShell 7+ (`pwsh`)
- Solution built: `dotnet build`

## Step 1 -- Start the Docker stack

```bash
docker-compose up -d
```

Wait for all services to become healthy. Confirm with:

```bash
docker-compose ps
```

All services should show `healthy` or `running` status.

## Step 2 -- Run the HaipIdentityAttestation walkthrough

```bash
pwsh walkthroughs/HaipIdentityAttestation/setup.ps1
pwsh walkthroughs/HaipIdentityAttestation/run.ps1
```

The setup script provisions the issuer organisation, enrols it as a HAIP issuer
for the `VerifiedIdentityCredential` type, and generates a holder key. The run
script executes the full OpenID4VCI pre-authorized code flow.

### Verify

Confirm the credential was received and stored:

```bash
ls wallet/credentials/VerifiedIdentityCredential.sdjwt
```

The file should exist and contain a valid SD-JWT VC (three tilde-separated segments).

## Step 3 -- Run the HaipDrivingLicence walkthrough

```bash
pwsh walkthroughs/HaipDrivingLicence/setup.ps1
pwsh walkthroughs/HaipDrivingLicence/run.ps1
```

This walkthrough issues a driving licence credential that chains from the
verified identity established in Step 2.

### Verify

Confirm the credential was received and stored:

```bash
ls wallet/credentials/DrivingLicenceCredential.sdjwt
```

## Step 4 -- Verify both credentials

Check the wallet directory contains both credentials:

```bash
ls wallet/credentials/
```

Expected output:

```
DrivingLicenceCredential.sdjwt
VerifiedIdentityCredential.sdjwt
```

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `setup.ps1` fails with connection error | Services not ready | Wait 30s, retry; check `docker-compose logs haip-service` |
| Exit code 2 from `run.ps1` | Pre-authorized code expired | Re-run `setup.ps1` to generate a fresh offer, then `run.ps1` |
| Exit code 3 from `run.ps1` | Holder key mismatch | Delete `wallet/holder-key.pem` and re-run `setup.ps1` |
| Exit code 4 from `run.ps1` | Network timeout | Check `docker-compose ps` for crashed containers |
| Credential file missing after successful run | Wrong working directory | Run scripts from the walkthrough root directory |

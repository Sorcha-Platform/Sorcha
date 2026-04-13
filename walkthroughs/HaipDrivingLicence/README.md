# HAIP Driving Licence Walkthrough

Verifies the citizen's identity credential via OID4VP `direct_post`, then issues a **DrivingLicenceCredential** via OID4VCI. This is the second HAIP walkthrough and chains from [HaipVerifiedCitizen](../HaipVerifiedCitizen/).

## What It Tests

- Full HAIP round-trip: present credential, verify, issue new credential
- OpenID4VP `direct_post` presentation with selective disclosure
- KB-JWT (Key Binding JWT) generation using the holder's ES256 key
- Presentation request creation via `/api/v1/verifier/requests`
- Selective disclosure of specific claims (`givenName`, `familyName`, `dateOfBirth`) from a larger credential
- Credential chaining: driving licence issued after identity verification
- Council organisation as a separate HAIP issuer with its own trust enrolment
- Blueprint schema with `HaipExternalWallet` as `presentationSource` and `targetAudience`

## Prerequisites

- Docker Desktop running with `docker-compose up -d`
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`
- PowerShell 7.5+
- .NET 10 SDK
- **HaipVerifiedCitizen** must have run first (or it runs inline automatically)

## How to Run

```powershell
# Setup: creates Council org, enrols as issuer
# Runs HaipVerifiedCitizen inline if not already done
pwsh walkthroughs/HaipDrivingLicence/setup.ps1

# Run: present identity -> verify -> issue driving licence
pwsh walkthroughs/HaipDrivingLicence/run.ps1
```

If `HaipVerifiedCitizen/state.json` does not exist when `setup.ps1` runs, it will automatically invoke the identity attestation setup and run scripts first.

### Parameters

| Parameter | Script | Default | Description |
|-----------|--------|---------|-------------|
| `-Profile` | setup.ps1 | `gateway` | URL profile: `gateway`, `direct`, `aspire`, `n1` |
| `-SkipHealthCheck` | setup.ps1 | off | Skip Docker container health verification |
| `-Force` | setup.ps1 | off | Re-run setup even if `state.json` exists |
| `-ShowJson` | run.ps1 | off | Print full JSON request/response for debugging |

## What Setup Creates

| Resource | Details |
|----------|---------|
| **Council Licensing Authority** | Private org (`council-licence` subdomain) |
| Council Admin | `council-admin@haip-walkthrough.local` with admin role |
| Council Wallet | ED25519 wallet for signing credentials |
| Council Participant | Linked to Council wallet |
| Org Certificate | Council org enrolled as HAIP issuer under platform trust anchor |

Setup also references state from `HaipVerifiedCitizen` (tenant ID, wallet directory, citizen persona).

## The HAIP Round-Trip

`run.ps1` executes the full present-verify-issue cycle:

1. **Authenticate** as Council Admin
2. **Create presentation request** asking for `VerifiedCitizenCredential` with claims `givenName`, `familyName`, `dateOfBirth`
3. **`sorcha-agent haip present`** loads the stored identity credential, builds a selective disclosure presentation with KB-JWT, and submits via `direct_post` to the verifier
4. **Create credential offer** for `DrivingLicenceCredential` with licence number, vehicle class, dates, and holder name
5. **`sorcha-agent haip receive`** exchanges the pre-auth code and receives the driving licence credential
6. **Verify** both credentials exist in the wallet

## Expected Output

After presentation:
```
=== Presentation Accepted ===
  Credential: VerifiedCitizenCredential
  Disclosed:  givenName,familyName,dateOfBirth
  Verifier:   <verifier-client-id>
=============================
```

After credential receipt:
```
=== Credential Received ===
  Type:      DrivingLicenceCredential
  Issuer:    http://127.0.0.1/api/v1
  Stored:    ./wallet/credentials/DrivingLicenceCredential.sdjwt
  Token len: <length> chars
  cnf:       present
===========================
```

### Final Wallet Contents

```
wallet/                                    (shared with HaipVerifiedCitizen)
├── holder-key.pem
├── holder-key.jwk.json
└── credentials/
    ├── VerifiedCitizenCredential.sdjwt   # From identity attestation
    └── DrivingLicenceCredential.sdjwt     # From this walkthrough
```

## Blueprint

The walkthrough includes a `blueprints/driving-licence.json` template defining two actions:

1. **Verify Applicant Identity** (`applicant` participant) -- requires presenting a `VerifiedCitizenCredential` from `HaipExternalWallet`
2. **Issue Driving Licence** (`council` participant) -- issues a `DrivingLicenceCredential` to `HaipExternalWallet`

The blueprint is reference material showing how HAIP credential requirements integrate with Sorcha's workflow schema. The walkthrough scripts drive the HAIP flow directly rather than through the blueprint engine.

## Troubleshooting

### HaipVerifiedCitizen not run
```
WARN: HaipVerifiedCitizen not run -- running it now
```
This is normal. Setup automatically runs the identity attestation walkthrough if `state.json` is missing. It will create the Government org, provision the trust anchor, and issue the identity credential before continuing.

### Credential not found in wallet
```
[ERROR] Credential 'VerifiedCitizenCredential' not found in wallet
```
The identity credential must exist at `../HaipVerifiedCitizen/wallet/credentials/VerifiedCitizenCredential.sdjwt`. Re-run the identity attestation walkthrough.

### Presentation rejected (403)
```
[ERROR] Presentation rejected (403): ...
```
Check that:
- The credential has not expired
- The KB-JWT nonce matches the presentation request nonce
- The holder key matches the `cnf` claim in the credential
- The disclosed claims match what the verifier requested

### Issuer metadata unreachable
Same as for HaipVerifiedCitizen -- the `IssuerUrl` must be host-resolvable (`http://127.0.0.1`), not a Docker-internal hostname.

### Token exchange or credential request fails
The HAIP service internal endpoints (offers, verifier) accept any authenticated user token. Ensure the Council admin session token has not expired. The scripts do not auto-refresh tokens.

## Key Learnings

- **Shared wallet directory**: Both HAIP walkthroughs share the same wallet directory (`HaipVerifiedCitizen/wallet/`). The driving licence walkthrough references this via `state.walletDir`.
- **Selective disclosure**: Only three claims are disclosed from the identity credential (`givenName`, `familyName`, `dateOfBirth`), even though the credential contains address, email, and other fields. The verifier only sees the disclosed claims plus the KB-JWT proof.
- **Credential chaining**: The driving licence is only issued after the identity credential is successfully presented and verified. This demonstrates the real-world pattern of requiring one credential to obtain another.
- **Two issuers, one trust anchor**: Both Government and Council are enrolled under the same platform trust anchor. Each has its own wallet and signing key.

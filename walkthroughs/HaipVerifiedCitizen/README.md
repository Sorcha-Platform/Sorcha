# HAIP Verified Citizen Walkthrough

Issues a **VerifiedCitizenCredential** to a citizen via the HAIP OpenID4VCI pre-authorized code flow. This is the first of two HAIP walkthroughs and must run before [HaipDrivingLicence](../HaipDrivingLicence/).

## What It Tests

- Trust anchor provisioning via `/api/v1/trust/tenants/{id}/provision`
- Organisation certificate enrolment via `/api/v1/trust/tenants/{id}/orgs/{address}/enrol`
- Credential offer creation via `/api/v1/offers`
- Pre-authorized code exchange at the token endpoint
- JWT proof of possession with an ES256 holder key
- SD-JWT VC issuance with `cnf` holder key binding
- Nested address disclosure (individual address fields as separate disclosable paths)
- Credential storage in a local file-based wallet

## Prerequisites

- Docker Desktop running with `docker-compose up -d`
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`
- PowerShell 7.5+
- .NET 10 SDK (to build and run `sorcha-agent`)

## How to Run

```powershell
# Setup: creates orgs, users, wallets, trust anchor
pwsh walkthroughs/HaipVerifiedCitizen/setup.ps1

# Run: creates offer, exchanges pre-auth code, receives credential
pwsh walkthroughs/HaipVerifiedCitizen/run.ps1
```

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
| **Government Identity Authority** | Private org (`gov-identity` subdomain) |
| Government Admin | `gov-admin@haip-walkthrough.local` with admin role |
| Government Wallet | ED25519 wallet for signing credentials |
| Government Participant | Linked to Government wallet |
| Trust Anchor | Platform-level trust anchor provisioned |
| Org Certificate | Government org enrolled as HAIP issuer |
| Citizen User | `alice.obrien@haip-walkthrough.local` on public org |
| Persona Data | Alice O'Brien, DOB 1990-03-15, 42 Grafton Street, Dublin |

## What the Agent Does

`run.ps1` invokes `sorcha-agent haip receive` which acts as a simulated HAIP wallet:

1. **Parses** the `openid-credential-offer://` URI to extract the credential offer JSON
2. **Fetches** issuer metadata from `/.well-known/openid-credential-issuer`
3. **Exchanges** the pre-authorized code at the token endpoint for an access token and `c_nonce`
4. **Generates** (or loads) an ES256 holder key pair (P-256) stored in `wallet/holder-key.pem`
5. **Builds** a JWT proof of possession binding the holder key to the `c_nonce` and issuer audience
6. **Requests** the credential at the credential endpoint with the JWT proof
7. **Stores** the issued SD-JWT VC in `wallet/credentials/VerifiedCitizenCredential.sdjwt`

## Expected Output

```
=== Credential Received ===
  Type:      VerifiedCitizenCredential
  Issuer:    http://127.0.0.1/api/v1
  Stored:    ./wallet/credentials/VerifiedCitizenCredential.sdjwt
  Token len: <length> chars
  cnf:       present
===========================
```

### Wallet Directory Structure

```
wallet/
├── holder-key.pem               # ES256 (P-256) private key
├── holder-key.jwk.json           # Public JWK for reference
└── credentials/
    └── VerifiedCitizenCredential.sdjwt   # Issued SD-JWT VC
```

The credential contains selectively disclosable claims: `givenName`, `familyName`, `fullName`, `dateOfBirth`, `email`, and individual address fields (`street`, `locality`, `region`, `postcode`, `country`).

## Troubleshooting

### Docker not running
```
ERROR: Health check failed
```
Run `docker-compose up -d` and wait for all containers to report healthy before re-running.

### Secrets file missing
```
ERROR: Cannot find secrets for 'haip-verified-citizen'
```
Run `pwsh walkthroughs/initialize-secrets.ps1` to generate `.secrets/passwords.json`.

### Issuer metadata fetch fails
```
[ERROR] Failed to fetch issuer metadata: ...
```
The `IssuerUrl` configured in `docker-compose.yml` must be the **host-resolvable** URL (typically `http://127.0.0.1`), not the Docker-internal hostname. Check that the HAIP issuer metadata endpoint is accessible from the host machine at the configured URL.

### Trust anchor provisioning fails
The `/api/v1/trust/*` endpoints require a YARP route from the API Gateway to the tenant service. Verify that `trust-cluster` is configured in the gateway's YARP routes.

### Token exchange fails (401/400)
The credential endpoint correlates the Bearer token to the offer ID via `AccessTokenStore`. If the offer has expired or the pre-auth code was already consumed, create a new offer.

### Multi-org login shows org selection
When a user belongs to multiple organisations, the login flow displays org selection cards within the login page (SPA, the URL does not change). This is expected. The walkthrough scripts handle org selection via `Connect-SorchaUser -OrganizationId`.

## Key Learnings

- **IssuerUrl resolution**: The issuer URL in docker-compose must be the host-resolvable URL (`http://127.0.0.1`), not the Docker-internal service hostname. The agent runs on the host and must be able to reach the issuer metadata and token/credential endpoints.
- **HAIP internal endpoints**: The offer and credential endpoints accept any authenticated user token (relaxed from `RequireService` policy to allow walkthrough access).
- **Issuer JWK in header**: In dev/walkthrough mode, the issuer's public JWK is embedded in the JWS header. Production deployments use `x5c` certificate chains instead.
- **Holder key persistence**: The ES256 key is generated once and reused across receive and present operations. Both walkthroughs share the same wallet directory and holder key.

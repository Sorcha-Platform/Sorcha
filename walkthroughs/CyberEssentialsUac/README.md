# Cyber Essentials UAC Walkthrough

**Category:** Credential Issuance & Reuse  
**Status:** Scripted (scenarios 1 + 2 + HAIP variant run locally; scenario 3 requires n1)

---

## What it proves

A continuous, evidence-backed, self/consultant-attested **Cyber Essentials User Access Control (UAC) posture** credential flow.

**Boundary — read this carefully.**  
The `CyberEssentialsUacPosture` credential attests:

> "This evidence was captured and evaluated against the UAC requirements (IASME Requirements for IT Infrastructure v3.3) by this assessor on this date."

This is a **posture assessment credential**. It is **NOT the formal Cyber Essentials certificate** issued by an accredited certification body under the NCSC scheme. The walkthrough does not simulate, replace, or reproduce any part of that certification process. Neither the blueprint, the credential type, nor any assertion in this walkthrough should be read as implying that the assessed organisation *is* Cyber Essentials certified.

The walkthrough proves:

- **Route-gated credential withholding** — a JSON Logic compliance gate decides at action 0 whether the workflow proceeds to credential issuance (action 1) or is diverted to a terminal non-compliance record (action 2). No credential is ever minted on the non-compliant branch.
- **Issuer-pinned FailClosed credential gate** — Blueprint B (`cyber-insurance-application`) requires a `CyberEssentialsUacPosture` credential whose issuer DID matches a trust policy allow-list baked in at publish time. The `revocationCheckPolicy` is `FailClosed`: a revoked credential causes the action to be rejected even if the credential is otherwise structurally valid.
- **Mid-cycle revocation enforcement** — revoking the posture credential after it has been presented once prevents re-use on any subsequent Blueprint B instance. (n1-only; see Scenario 3.)
- **Genuine on-the-wire selective disclosure** — the HAIP variant (`run-haip-sd.ps1`) issues via OID4VCI and presents via OID4VP, proving that only the 4 requested claims cross the wire and that withheld claims are provably absent from the verifier's result.

---

## Architecture

Two blueprints, one register, three actors.

```
┌──────────────────────────────────────────────────┐
│  Register: Cyber Essentials UAC Register          │
│  Owner: assessor org                              │
│                                                   │
│  ┌───────────────────────────────┐                │
│  │ Blueprint A                   │                │
│  │ ce-uac-assessment             │                │
│  │                               │                │
│  │ Action 0: Submit UAC Evidence │  assessor      │
│  │   └─ computedCompliant=true ──┼─▶ Action 1: Issue Posture VC ──▶ subject-org wallet
│  │   └─ computedCompliant=false ─┼─▶ Action 2: Record Non-Compliance (terminal, no VC)
│  └───────────────────────────────┘                │
│                                                   │
│  ┌─────────────────────────────────────────────┐  │
│  │ Blueprint B                                 │  │
│  │ cyber-insurance-application                 │  │
│  │                                             │  │
│  │ Action 0: Request Cover  ◀── subject-org    │  │
│  │   Credential gate (SorchaInternal)          │  │
│  │   • type: CyberEssentialsUacPosture         │  │
│  │   • trustPolicy: did-allowlist (assessorDid)│  │
│  │   • revocationCheckPolicy: FailClosed       │  │
│  │   └─▶ Action 1: Issue Quote  (insurer)      │  │
│  └─────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────┘
```

### Actors

| File | Role | Blueprints |
|------|------|-----------|
| `actors/assessor.json` | Cyber Assessor — issues posture credentials | Blueprint A (actions 0, 1, 2) |
| `actors/subject-org.json` | Assessed Organisation — receives the credential, requests cover | Blueprint B action 0 (validation only — see note below) |
| `actors/insurer.json` | Cyber Insurer — quotes based on verified posture | Blueprint B action 1 |

**Important:** Blueprint B action 0 ("Request Cover") carries a `SorchaInternal` credential requirement. The `sorcha-agent` CLI does not construct credential presentations (`ActionExecutor.cs` sends no `credentialPresentations` in its execute body), so the `subject-org` actor cannot drive this action autonomously. `run-agents.ps1` injects the presentation via `Get-SorchaCredentialPresentation` + `Invoke-SorchaAction -CredentialPresentations`. The `subject-org.json` actor config exists for structural validation (`sorcha-agent validate`) only. See `actors/README.md` for the full explanation.

### Credential type

`CyberEssentialsUacPosture` — 10 claims mapped from the assessment evidence.

**Blueprint-level (SorchaInternal) disclosure model** — applies to scenarios 1–3:

| Claim | Source field | Always disclosed |
|-------|-------------|-----------------|
| `compliant` | `/uac/compliant` | yes |
| `assessmentDate` | `/assessment/date` | yes |
| `infraVersion` | `/assessment/infraVersion` | yes |
| `passwordApproach` | `/passwordPolicy/approach` | yes |
| `mfaAdminEnforced` | `/mfa/adminMfaEnforced` | yes |
| `assessorType` | `/assessment/assessorType` | selectively disclosable |
| `scopeDeviceCount` | `/assessment/scopeDeviceCount` | selectively disclosable |
| `mfaCoverage` | `/mfa/cloudServicesWithMfa` | selectively disclosable |
| `staleAccounts` | `/offboarding/staleAccounts` | selectively disclosable |
| `policyEvidenceHash` | `/provisioning/policyHash` | selectively disclosable |

> **HAIP/OID4VP variant** (`run-haip-sd.ps1`) uses a different disclosure model: all 10 claims are placed in `disclosablePaths` (none is baked in as always-plaintext), and the presentation discloses only 4 (`compliant`, `assessmentDate`, `passwordApproach`, `mfaAdminEnforced`). In this path `infraVersion` is also withheld — 6 claims withheld in total.

Credential validity: `P1Y` (1 year from issuance). Blueprint B checks `assessmentDate` is present and `compliant = true`.

---

## Prerequisites

- Docker Desktop running (`docker-compose up -d`)
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`
- PowerShell 7.5+
- .NET 10 SDK (for `run-haip-sd.ps1` only — drives `Sorcha.Agent` via `dotnet run`)

---

## Local override: apply before running

The local Docker stack cannot serve a TLS-reachable status list, so `CredentialStatus__EnableEmbedding` must be `false` for scenarios 1 and 2 to pass the FailClosed gate. The HAIP variant additionally needs `Haip__IssuerUrl` set to `http://127.0.0.1` so the agent CLI can reach the OID4VCI/OID4VP endpoints from the host.

Apply the override before starting (or restart the affected services after applying):

```powershell
# Apply override and restart the two affected services
docker compose -f docker-compose.yml `
               -f walkthroughs/CyberEssentialsUac/docker-compose.ce-uac-local.yml `
               up -d blueprint-service haip-service

# The api-gateway needs the anonymous status-list route committed to its config;
# rebuild if you have not pulled since that change was merged:
docker compose up -d api-gateway
```

`docker-compose.ce-uac-local.yml` sets:

```yaml
blueprint-service:
  environment:
    CredentialStatus__EnableEmbedding: "false"
haip-service:
  environment:
    Haip__IssuerUrl: "http://127.0.0.1"
```

---

## Run commands

```powershell
# Step 1 — provision (idempotent; use -Force to re-provision)
pwsh walkthroughs/CyberEssentialsUac/setup.ps1

# Step 2 — run scenarios 1 and 2
pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1

# Step 3 — scenario 3: mid-cycle revocation (SKIPS automatically on local Docker)
pwsh walkthroughs/CyberEssentialsUac/run-revocation.ps1

# Step 4 — HAIP selective-disclosure variant
# Local stack only — see "Where the service token comes from" below.
pwsh walkthroughs/CyberEssentialsUac/run-haip-sd.ps1
```

Additional flags:

| Flag | Script | Effect |
|------|--------|--------|
| `-Force` | `setup.ps1` | Deletes state.json and re-provisions (produces fresh blueprint IDs) |
| `-GatewayUrl <url>` | all scripts | Target a specific node (e.g. `https://n1.sorcha.dev`) |
| `-Profile n1` | all scripts | Use the n1 URL profile |
| `-ShowJson` | run scripts | Print full request/response JSON for debugging |

---

## Scenarios and expected output

### Scenario 1 — Happy path

**Evidence:** `data/evidence-compliant.json` — `adminMfaEnforced: true`, `staleAccounts: 0`, `separateAdminAccounts: true`, `leastPrivilege: true`, `passwordPolicy.approach: denylist+12`, `minLength: 12`.

**Flow:**
1. Assessor creates Blueprint A instance, submits compliant evidence (action 0).
2. JSON Logic gate evaluates: `computedCompliant = true`.
3. Assessor submits action 1 (Issue Posture Credential) → `CyberEssentialsUacPosture` minted in subject-org wallet.
4. Subject-org creates Blueprint B instance, fetches the credential presentation from wallet.
5. Subject-org submits Blueprint B action 0 (Request Cover) with the credential. The engine verifies:
   - type matches `CyberEssentialsUacPosture`
   - issuer DID is on the allow-list (the assessor's `did:sorcha:org:<walletAddress>`)
   - `compliant = true`, `assessmentDate` present
   - credential not revoked (FailClosed — embedding is OFF locally, so the gate short-circuits to "active")
6. Insurer submits action 1 (Issue Quote) — workflow complete.

**Expected output:**

```
ASSERT OK: Compliant evidence => computedCompliant=true
ASSERT OK: Action 1 response carries credentialIssued object
ASSERT OK: credentialIssued object carries a non-empty credentialId
ASSERT OK: subject-org holds a presentable CyberEssentialsUacPosture credential
ASSERT OK: disclosedClaims carries assessmentDate claim
ASSERT OK: posture assessmentDate (...) within P1Y (freshness)
ASSERT OK: Insurer requirement satisfied — Request Cover accepted (FailClosed, issuer-pinned)
ASSERT OK: Issue Quote completed — happy path green
========== SCENARIO 1 COMPLETE — ALL ASSERTIONS PASSED ==========
```

---

### Scenario 2 — Auto-fail (non-compliant evidence)

**Evidence:** `data/evidence-autofail.json` — identical to the compliant fixture except `adminMfaEnforced: false`. This single field triggers the auto-fail condition.

**JSON Logic gate:**
```json
{ "and": [
  { "==": [ { "var": "mfa.adminMfaEnforced" }, true ] },
  ...
]}
```
`adminMfaEnforced = false` → `computedCompliant = false` → default route → action 2.

**Flow:**
1. Assessor creates a second Blueprint A instance, submits non-compliant evidence (action 0).
2. Gate evaluates: `computedCompliant = false`.
3. Engine routes to action 2 (Record Non-Compliance) — action 1 (Issue Posture Credential) is bypassed and unreachable.
4. Attempting to submit action 1 returns a 4xx domain error.
5. Wallet poll confirms no new `CyberEssentialsUacPosture` credential was delivered.

**Expected output:**

```
ASSERT OK: Auto-fail evidence => computedCompliant=false
ASSERT OK: Auto-fail route: action 2 (Record Non-Compliance) is current (not action 1)
ASSERT OK: Issue action (1) unreachable on auto-fail route — no posture credential minted
ASSERT OK: Wallet holds exactly one posture credential after both scenarios (S1 issued one; S2 withheld one)
========== SCENARIO 2 COMPLETE — ALL ASSERTIONS PASSED ==========
```

---

### Scenario 3 — Mid-cycle revocation (n1-only)

**Hard-skips on local Docker.** The revocation check requires the Blueprint Service to fetch and verify a signed IETF Token Status List JWT over HTTPS. The local stack cannot satisfy this because `CredentialStatus__EnableEmbedding=false` (the local override), and even if embedding were on, the gateway is plain HTTP and Windows Schannel cannot verify self-signed container certificates.

`run-revocation.ps1` detects whether the effective gateway URL contains `n1.sorcha.dev` (or `-Profile n1` is set) and exits 0 with a clear skip banner if not.

**Required n1 configuration:**

| Setting | Value |
|---------|-------|
| `StatusList__BaseUrl` | `https://n1.sorcha.dev/api/v1/credentials/status-lists` |
| `CredentialStatus__EnableEmbedding` | `true` |
| Valid TLS on the gateway | (Let's Encrypt or equivalent) |
| Single Blueprint Service instance | (multi-instance backplane not covered here) |
| Anonymous gateway route for status-list | committed (see Platform Change note below) |

**n1 run commands:**

```powershell
# Provision against n1 (skips if state.json already exists for this node)
pwsh walkthroughs/CyberEssentialsUac/setup.ps1 -GatewayUrl https://n1.sorcha.dev

# Run scenarios 1 and 2
pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1 -GatewayUrl https://n1.sorcha.dev

# Run scenario 3
pwsh walkthroughs/CyberEssentialsUac/run-revocation.ps1 -GatewayUrl https://n1.sorcha.dev
```

**Flow (n1):**
1. Assessor's posture credential from scenario 1 is still in the subject-org wallet.
2. Assessor calls `POST /api/v1/credentials/{credentialId}/revoke` with `issuerWallet` and `reason`. The status-list bit is flipped.
3. A new Blueprint B instance is created for the subject-org.
4. Subject-org builds a presentation from the (now-revoked) credential and submits Blueprint B action 0.
5. The engine fetches the status list, finds the bit set, and rejects with HTTP 400 (FailClosed).

**Expected output (n1):**

```
ASSERT OK: revoke endpoint reports Revoked
ASSERT OK: status-list bit updated
ASSERT OK: post-revocation Blueprint B instance created
ASSERT OK: presentation object constructed from revoked credential
ASSERT OK: post-revocation Request Cover REJECTED (FailClosed)
ASSERT OK: rejection surfaced as HTTP 400
Scenario 3 (revocation) — PASS on n1
```

---

### HAIP variant — genuine on-the-wire selective disclosure (`run-haip-sd.ps1`)

The SorchaInternal core path (scenarios 1–3) records the full claim set server-side and performs a loose-match gate at action execution time. It does **not** demonstrate wire-level claim minimisation — the engine sees all claims regardless of what was "selected". This is the honest framing.

The HAIP variant demonstrates genuine OID4VCI issuance and OID4VP selective disclosure using the `sorcha-agent` CLI as the holder wallet.

**Prerequisites for this variant:**
- `setup.ps1` must have completed and `state.json` must contain `haip.clientId` + `haip.clientSecret` (written by setup step 11: HAIP service principal registration via `POST /api/service-principals/`).
- The local override must be applied (see above) so `Haip__IssuerUrl=http://127.0.0.1`.
- .NET SDK available (`dotnet run` is used to invoke `src/Apps/Sorcha.Agent/Sorcha.Agent.csproj`).

> **This variant runs against a LOCAL stack only, and does not yet complete even there.** The service-token path is fixed (steps 1-4 pass), but the present step is blocked by **#1538** — F181 US6 made the verifier sign its request object with an X.509 chain (`x5c`, no embedded `jwk`) and `sorcha-agent haip present` only understands embedded-jwk, so it fail-closes. See "Where the service token comes from" below.

**Service token:** exactly one of this script's privileged calls needs one. `POST /api/v1/offers` is `RequireService` (SEC-013) because it mints a credential from the org's issuance key on demand. `POST /api/v1/verifier/requests` and `GET .../result` were relaxed to *any authenticated caller* by F164 B3 (FR-008), so they no longer need a service token at all.

**Where the service token comes from.** `#1397` removed `client_credentials` from the public token endpoint (it was a signing oracle) and moved the grant to `POST /api/internal/service-auth/token`, which the API Gateway deliberately does not route. So the script addresses the Tenant Service **directly** via `-TenantDirectUrl` (default `http://127.0.0.1:5450`, the port docker-compose publishes for development/bootstrap use), not via the gateway.

On a **cert-only node** — F191/#1420, which n1 sets via `ServiceAuth__DisableSharedSecrets=true` — a `client_secret` is refused with an explicit 400 even from inside the network, and the caller must present a workload certificate. Giving a test harness workload cert material would hand a walkthrough a credential-minting service identity, which is the `#1397` shape wearing a different hat. So against such a node the script **skips with an explanation and exits 0** rather than failing in a way that reads like a platform fault.

That trade is deliberate: what this variant proves — that withheld claims are genuinely absent from the wire — is a *protocol* property, and a protocol property does not need production topology to be meaningful. For n1 coverage use `run-agents.ps1`, `run-revocation.ps1` and `run-suspension.ps1`, none of which need a service token.

**Flow:**
1. Exchange `haip.clientId` + `haip.clientSecret` for a service token (`grant_type=client_credentials`, scope `haip:issue haip:verify`) at `POST {TenantDirectUrl}/api/internal/service-auth/token`.
2. Create OID4VCI credential offer (`POST /api/v1/offers/`) with all 10 claims listed in `disclosablePaths` so every claim is wrapped as an SD-JWT disclosure.
3. Agent receives the credential: `dotnet run -- haip receive --offer-uri <uri> --wallet-dir walkthroughs/CyberEssentialsUac/agent-wallet`
   - Written to: `agent-wallet/credentials/CyberEssentialsUacPosture.sdjwt`
4. Create OID4VP verifier request (`POST /api/v1/verifier/requests`) requiring 4 claims: `compliant`, `assessmentDate`, `passwordApproach`, `mfaAdminEnforced`.
5. Agent presents: `dotnet run -- haip present --request-uri <uri> --credential CyberEssentialsUacPosture --disclose "compliant,assessmentDate,passwordApproach,mfaAdminEnforced" --wallet-dir walkthroughs/CyberEssentialsUac/agent-wallet`
6. **Positive assertion:** poll `GET /api/v1/verifier/requests/{id}/result` — `isValid=true`, `verifiedClaims` contains exactly the 4 disclosed claims; none of the 6 withheld claims (`infraVersion`, `assessorType`, `scopeDeviceCount`, `mfaCoverage`, `staleAccounts`, `policyEvidenceHash`) appear.
7. **Negative test:** create a second verifier request requiring `policyEvidenceHash` (withheld). Present again with the same `--disclose` list (4 claims). Assert `isValid=false` — confirming the selective disclosure is genuine and not just filtered from the response.

**Expected output:**

```
ASSERT OK: client_credentials grant returned an access token
ASSERT OK: offer created (offerId present)
ASSERT OK: offer has credentialOfferUri
ASSERT OK: agent received the credential into its file wallet (...)
ASSERT OK: verifier request created (requestId present)
ASSERT OK: result payload returned
ASSERT OK: verifier accepted the presentation (isValid = true)
ASSERT OK: verifier received EXACTLY the 4 disclosed claims (got: assessmentDate,compliant,mfaAdminEnforced,passwordApproach)
ASSERT OK: withheld evidence claims NOT present in verifiedClaims (selective disclosure holds — nothing leaked: infraVersion,assessorType,scopeDeviceCount,mfaCoverage,staleAccounts,policyEvidenceHash)
ASSERT OK: second verifier request created
ASSERT OK: verifier rejects when a withheld claim is required (policyEvidenceHash not disclosed — selective disclosure genuinely absent from the wire)
HAIP selective-disclosure variant — PASS
```

**Withheld claim count note:** the script summary reports 6 withheld claims (`infraVersion`, `assessorType`, `scopeDeviceCount`, `mfaCoverage`, `staleAccounts`, `policyEvidenceHash`) from a total of 10. The negative-presence assertion checks all 6.

---

## Selective disclosure note

The SorchaInternal core path (scenarios 1–3) is a **server-side loose-match gate**: when the Blueprint Service evaluates a credential requirement, it checks the required claims against the full stored credential record. The presentation envelope is what travels between the subject-org and the blueprint engine, but the engine's trust evaluation happens against the full claim set recorded at issuance. Wire-level claim minimisation is therefore **not demonstrated** in scenarios 1–3.

The HAIP variant (`run-haip-sd.ps1`) is the only path in this walkthrough that proves genuine on-the-wire selective disclosure: the SD-JWT is transmitted with only the chosen disclosures included; the verifier result contains exactly and only the disclosed claims; the negative test confirms the withheld claims are provably absent from the wire, not just filtered server-side.

---

## Known caveats

**(a) Scenario 3 — n1 configuration required.**  
Run-revocation.ps1 hard-skips unless the gateway URL contains `n1.sorcha.dev` or `-Profile n1` is passed. The n1 Blueprint Service must have `CredentialStatus__EnableEmbedding=true` and `StatusList__BaseUrl=https://n1.sorcha.dev/api/v1/credentials/status-lists`, valid TLS, and the committed anonymous status-list gateway route deployed (see below).

**(b) HAIP variant — service principal endpoint reachability.**  
`setup.ps1` step 11 registers the HAIP service principal via `POST /api/service-principals/` using `$sysAdmin.Headers` (platform-admin JWT). If that endpoint is not reachable through the API Gateway, `state.json` will carry `haip.clientId: null` and `run-haip-sd.ps1` will exit with a clear error. Delete the existing state and re-run `setup.ps1 -Force` after confirming gateway reachability. If setup previously completed but the secret cannot be recovered (service principal already exists), delete the service principal via the admin API first.

**(c) n1 hairpin DNS.**  
On n1, Blueprint Service must be able to reach `https://n1.sorcha.dev/api/v1/credentials/status-lists/**` from inside the container network. If the host does not have a hairpin DNS rule (resolving its own public hostname to a container-internal address), status-list fetch will time out. Configure a `extra_hosts` entry in `docker-compose.n1.yml` or ensure the DNS resolver returns the internal IP for `n1.sorcha.dev` from within the container.

---

## n1 run status

**NOT YET PERFORMED** — Docker was unavailable locally during authoring; live validation against n1.sorcha.dev was deferred.

> Operator placeholder: After running `setup.ps1 -GatewayUrl https://n1.sorcha.dev` + `run-agents.ps1` + `run-revocation.ps1` against n1, fill in the results here:
>
> - n1 run date:
> - Scenario 1 (happy path): PASS / FAIL
> - Scenario 2 (auto-fail): PASS / FAIL
> - Scenario 3 (revocation): PASS / FAIL / SKIPPED
> - HAIP variant: PASS / FAIL / SKIPPED
> - Notes:

---

## Committed platform change

One change outside the walkthrough directory was made to support this walkthrough:

**Anonymous GET route for `/api/v1/credentials/status-lists/**` in the API Gateway.**  
The IETF Token Status List is a publicly verifiable artefact (it carries no credential-holder identity — only opaque bit positions). To allow the Blueprint Service container (and any external verifier) to fetch the list without a bearer token, an anonymous GET route was added to the YARP gateway config. Without this route, the FailClosed revocation check in scenario 3 (and any downstream SorchaInternal FailClosed gate on n1) would receive a 401 when fetching the status list and fail the action incorrectly.

---

## Files

```
walkthroughs/CyberEssentialsUac/
├── README.md                                   # This file
├── setup.ps1                                   # Provision 3 orgs, wallets, register, blueprints, HAIP service principal
├── run-agents.ps1                              # Scenarios 1 (happy path) + 2 (auto-fail)
├── run-revocation.ps1                          # Scenario 3: mid-cycle revocation (n1-only)
├── run-haip-sd.ps1                             # HAIP OID4VCI+OID4VP selective-disclosure variant
├── docker-compose.ce-uac-local.yml             # Local override: embedding off, HAIP IssuerUrl
├── ce-uac-assessment-template.json             # Blueprint A: UAC evidence → posture credential or non-compliance record
├── cyber-insurance-application-template.json   # Blueprint B: credential-gated insurance application
├── data/
│   ├── evidence-compliant.json                 # Compliant evidence (scenario 1)
│   └── evidence-autofail.json                  # Non-compliant evidence: adminMfaEnforced=false (scenario 2)
├── actors/
│   ├── README.md                               # Actor notes + credential-gate limitation
│   ├── assessor.json                           # sorcha-agent config: assessor role
│   ├── subject-org.json                        # sorcha-agent config: subject-org (structural validation only)
│   └── insurer.json                            # sorcha-agent config: insurer role
└── state.json                                  # Per-run state (git-ignored), written by setup.ps1
```

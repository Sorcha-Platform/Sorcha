# Feature Specification: HAIP Walkthroughs

**Feature Branch**: `101-haip-walkthroughs`
**Created**: 2026-04-11
**Status**: Draft
**Input**: User description: "HAIP walkthroughs: extend Sorcha.Agent with HAIP wallet commands (haip receive, haip present) and create HaipIdentityAttestation + HaipDrivingLicence walkthroughs"

## Context

The HAIP spec set (093-098) is fully implemented and deployed. The Docker stack runs all services including the new Sorcha.Haip.Service. However, there are no end-to-end tests or walkthroughs that exercise the HAIP issuance and verification flows. The existing walkthroughs (ConstructionPermit, SelfBuildHouse, etc.) use the Sorcha-internal credential path — none exercises the external HAIP wallet path.

This spec extends the existing `Sorcha.Agent` CLI tool with HAIP wallet capabilities and creates two walkthroughs that prove the HAIP pipeline works end-to-end against the Docker stack using real-world credential scenarios.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Sorcha.Agent receives a credential via HAIP issuance (Priority: P1)

A Government Identity Authority issues a verified identity credential to a citizen. The citizen's `sorcha-agent` acts as a HAIP wallet: it generates a holder key pair, exchanges a pre-authorized code for an access token, constructs a JWT proof of possession binding the holder key and c_nonce, submits the proof to the credential endpoint, and receives an SD-JWT VC with cnf holder key binding. The credential contains the citizen's persona data (name, email, date of birth, address) with each field selectively disclosable.

**Why this priority**: This is the foundational HAIP wallet capability. Without a tool that can receive credentials via the HAIP protocol, no walkthrough or E2E test can exercise the issuance pipeline. Every other story depends on this.

**Independent Test**: Run `sorcha-agent haip receive --offer-uri <uri>` against a running Docker stack with a valid credential offer. Verify the agent completes the pre-auth code flow, receives a credential, stores it locally, and the credential has a valid cnf claim matching the agent's holder key.

**Acceptance Scenarios**:

1. **Given** a fresh agent with no existing keys, **When** `haip receive` is invoked with a valid offer URI, **Then** the agent generates a P-256 holder key pair and persists it to the wallet directory.
2. **Given** a credential offer with a pre-authorized code, **When** the agent exchanges the code at the token endpoint, **Then** it receives an access token and a c_nonce.
3. **Given** a valid access token and c_nonce, **When** the agent constructs and submits a JWT proof to the credential endpoint, **Then** the proof contains the holder's public key in the header, the c_nonce in the payload, and the credential issuer URL as the audience.
4. **Given** a successful credential response, **When** the agent parses the SD-JWT VC, **Then** the credential's cnf.jwk matches the agent's holder public key and all expected claims are present.
5. **Given** a received credential, **When** the agent stores it locally, **Then** the credential file can be loaded in a subsequent session and its type and claims are retrievable.
6. **Given** an expired pre-authorized code, **When** the agent attempts to exchange it, **Then** the agent reports a clear error indicating the code has expired.
7. **Given** an agent that already has a holder key pair from a previous run, **When** `haip receive` is invoked again, **Then** the agent reuses the existing key pair rather than generating a new one.

---

### User Story 2 — Sorcha.Agent presents a credential via HAIP verification (Priority: P1)

A citizen holds a verified identity credential in their agent's wallet. A Council licensing authority creates a presentation request requiring the identity credential with specific claims disclosed. The agent fetches the signed request object, selects the requested disclosures from the stored credential, constructs a Key Binding JWT proving possession of the holder key (bound to the verifier's nonce and audience), and submits the VP token via direct_post. The verifier validates the presentation and accepts the disclosed claims.

**Why this priority**: Presentation is the second half of the HAIP wallet capability. Without it, the agent can receive credentials but never use them. The driving licence walkthrough depends on this.

**Independent Test**: Run `sorcha-agent haip present --request-uri <uri> --credential VerifiedIdentityCredential --disclose "givenName,familyName"` against a running Docker stack with a valid presentation request. Verify the agent submits a valid VP token, the KB-JWT binds the correct nonce and audience, and the verifier accepts the presentation.

**Acceptance Scenarios**:

1. **Given** an agent with a stored credential and a presentation request URI, **When** `haip present` is invoked, **Then** the agent fetches the request object and extracts the nonce, audience, and required claims.
2. **Given** a credential with selectively disclosable claims, **When** the agent selects disclosures matching the `--disclose` parameter, **Then** only the specified claims are included in the presentation.
3. **Given** the selected disclosures and the request's nonce, **When** the agent constructs a KB-JWT, **Then** the KB-JWT payload contains the correct aud (verifier audience), nonce (from request), iat (current time), and sd_hash (hash of the presentation prefix).
4. **Given** a complete VP token, **When** the agent submits it via direct_post with the state parameter, **Then** the verifier returns a success response.
5. **Given** a credential that does not match the requested type, **When** the agent searches its wallet, **Then** it reports a clear error that no matching credential was found.
6. **Given** a presentation request that has expired, **When** the agent fetches the request object, **Then** it reports a clear error indicating the request has expired.

---

### User Story 3 — HaipIdentityAttestation walkthrough (Priority: P1)

A DevOps engineer or QA tester runs the HaipIdentityAttestation walkthrough against the Docker stack to verify that the HAIP issuance pipeline works end-to-end. The walkthrough provisions the infrastructure (tenant, trust anchor, Government org), creates a credential offer from a citizen's persona data, and invokes the agent to receive the credential. At the end, the walkthrough reports success with a summary of the credential contents.

**Why this priority**: This is the simplest complete walkthrough — it proves issuance works without the complexity of presentation. It's the first thing a new developer or QA engineer would run to verify the HAIP stack.

**Independent Test**: Run `pwsh walkthroughs/HaipIdentityAttestation/setup.ps1` followed by `pwsh walkthroughs/HaipIdentityAttestation/run.ps1` against a fresh Docker stack. Verify the walkthrough completes without errors and the credential is stored in the agent's wallet.

**Acceptance Scenarios**:

1. **Given** a running Docker stack with all services healthy, **When** setup.ps1 is run, **Then** it creates a tenant, provisions a trust anchor, creates and enrols a Government org as a HAIP issuer, and creates a citizen user with persona data.
2. **Given** setup.ps1 has completed, **When** run.ps1 is executed, **Then** it creates a credential offer from the citizen's persona (givenName, familyName, email, dateOfBirth, address), invokes `sorcha-agent haip receive`, and the agent receives a VerifiedIdentityCredential.
3. **Given** the walkthrough has completed, **When** the credential is inspected, **Then** it contains all persona claims, has a valid cnf, and each address field is independently disclosable via JSON Pointer paths.
4. **Given** the walkthrough has already been run, **When** setup.ps1 is run again, **Then** it is idempotent — it reuses existing resources rather than creating duplicates.
5. **Given** a Docker stack where the HAIP service is unhealthy, **When** setup.ps1 runs, **Then** it reports a clear error about the service being unreachable.

---

### User Story 4 — HaipDrivingLicence walkthrough (Priority: P2)

A DevOps engineer runs the HaipDrivingLicence walkthrough which exercises both HAIP verification and issuance in a single Blueprint workflow. A Council licensing authority requires the citizen to present their identity credential, verifies it, and then issues a driving licence credential. The walkthrough checks for the identity credential from the previous walkthrough (and runs it inline if missing), then executes the full round-trip.

**Why this priority**: This is the complex walkthrough that proves the complete HAIP story — present a credential to unlock a workflow action, then receive a new credential as the output. It depends on US1-US3 being implemented first.

**Independent Test**: Run `pwsh walkthroughs/HaipDrivingLicence/setup.ps1` followed by `pwsh walkthroughs/HaipDrivingLicence/run.ps1`. Verify the agent presents the identity credential via direct_post, the Blueprint action resumes, and the agent receives a DrivingLicenceCredential.

**Acceptance Scenarios**:

1. **Given** the HaipIdentityAttestation walkthrough has been completed, **When** HaipDrivingLicence setup.ps1 runs, **Then** it detects the existing identity credential and citizen wallet, creates a Council org, and enrols it as a HAIP issuer.
2. **Given** the HaipIdentityAttestation walkthrough has NOT been run, **When** HaipDrivingLicence setup.ps1 runs, **Then** it runs the identity attestation flow inline before proceeding with its own setup.
3. **Given** setup is complete, **When** run.ps1 starts a Blueprint instance, **Then** Action 1 creates a presentation request requiring a VerifiedIdentityCredential with specific claims.
4. **Given** a presentation request, **When** the agent presents the identity credential with selective disclosure (givenName, familyName, dateOfBirth, address.locality), **Then** the verifier accepts the presentation and the Blueprint action resumes with the verified claims.
5. **Given** the identity verification passes, **When** Action 2 fires, **Then** a credential offer is created for a DrivingLicenceCredential containing licenceNumber, vehicleClass, issuedDate, expiryDate, and the holder's verified name and address.
6. **Given** the licence credential offer, **When** the agent receives the credential, **Then** the DrivingLicenceCredential has nested address disclosure (e.g., `/address/locality` is independently disclosable) and the licence number is present.
7. **Given** the walkthrough completes, **When** the agent's wallet is inspected, **Then** it contains both the VerifiedIdentityCredential and the DrivingLicenceCredential.

---

### Edge Cases

- What happens when the Docker stack is not running? Setup scripts check service health and fail with a clear message.
- What happens when the agent's wallet directory doesn't exist? It is created automatically on first key generation.
- What happens when a credential offer has already been redeemed? The token endpoint returns an error and the agent reports it clearly.
- What happens when the agent tries to present a credential it doesn't have? The agent searches the wallet, finds no match, and reports the available credential types.
- What happens when the network is intermittent during the flow? The agent uses standard HTTP retry logic and reports the failure point.
- What happens when the presentation's nonce has expired? The KB-JWT verification fails with a clock-skew error and the agent reports it.

## Requirements *(mandatory)*

### Functional Requirements

**Sorcha.Agent HAIP wallet commands:**
- **FR-001**: The agent MUST support a `haip receive` command that accepts a credential offer URI and completes the OID4VCI pre-authorized code flow.
- **FR-002**: The agent MUST generate and persist a P-256 holder key pair on first use, and reuse it on subsequent invocations.
- **FR-003**: The agent MUST construct a JWT proof of possession per OpenID4VCI, binding the holder public key and the c_nonce from the token response.
- **FR-004**: The agent MUST store received SD-JWT VC credentials as files in a local wallet directory, organised by credential type.
- **FR-005**: The agent MUST support a `haip present` command that accepts a presentation request URI, a credential type, and a list of claims to disclose.
- **FR-006**: The agent MUST construct a Key Binding JWT per the SD-JWT spec, binding the verifier's nonce and audience, and compute the correct sd_hash.
- **FR-007**: The agent MUST submit the VP token via direct_post with the state parameter from the presentation request.
- **FR-008**: The agent MUST select only the specified disclosures from the stored credential when building the presentation.
- **FR-009**: Both commands MUST report clear, structured errors when any step fails (expired code, invalid nonce, missing credential, service unreachable).
- **FR-010**: The agent MUST fetch issuer metadata from `.well-known/openid-credential-issuer` as part of the receive flow.

**HaipIdentityAttestation walkthrough:**
- **FR-011**: The walkthrough MUST provision a complete trust infrastructure (tenant, trust anchor, HAIP issuer org) via setup.ps1.
- **FR-012**: The walkthrough MUST create a credential offer containing persona-sourced claims (givenName, familyName, email, dateOfBirth, address with street, locality, region, postcode, country).
- **FR-013**: The walkthrough MUST invoke `sorcha-agent haip receive` and verify the credential was received and stored.
- **FR-014**: Setup.ps1 MUST be idempotent — running it twice produces the same result.
- **FR-015**: The walkthrough MUST save state to state.json for use by downstream walkthroughs.

**HaipDrivingLicence walkthrough:**
- **FR-016**: Setup.ps1 MUST check for the identity credential from HaipIdentityAttestation and run it inline if missing.
- **FR-017**: The walkthrough MUST create a Blueprint with a presentation requirement (PresentationSource: HaipExternalWallet) and a credential issuance (TargetAudience: HaipExternalWallet).
- **FR-018**: The walkthrough MUST invoke `sorcha-agent haip present` for the identity verification step.
- **FR-019**: The walkthrough MUST invoke `sorcha-agent haip receive` for the licence issuance step.
- **FR-020**: The walkthrough MUST verify that both credentials exist in the agent's wallet at completion.
- **FR-021**: The driving licence credential MUST have nested address disclosure (at least `/address/locality` independently disclosable).

### Key Entities *(include if feature involves data)*

**HolderKeyPair**: P-256 EC key pair persisted as PEM (private) and JWK (public) in the wallet directory. One per agent identity, reused across all credentials.

**CredentialWallet**: Local file-based storage of SD-JWT VC tokens. One file per credential, named by type. Contains the raw SD-JWT compact serialisation.

**WalkthroughState** (state.json): Persisted between setup and run scripts. Contains tenant ID, org IDs, wallet addresses, user credentials, credential offer URIs, and paths to stored credentials.

**ActorDefinition**: Existing Sorcha.Agent JSON configuration extended with a `haip` section containing holder key algorithm preference and wallet directory path.

## Success Criteria

- Both walkthroughs complete against the Docker stack in under 2 minutes each
- The agent's `haip receive` command completes the full OID4VCI pre-auth code flow without manual intervention
- The agent's `haip present` command submits a valid VP token that the HAIP verifier accepts
- A new developer can run `pwsh walkthroughs/HaipIdentityAttestation/setup.ps1 && pwsh walkthroughs/HaipIdentityAttestation/run.ps1` with no prior knowledge beyond the README
- The driving licence walkthrough produces two distinct credentials in the agent's wallet, each verifiable independently
- Running `walkthroughs/run-all.ps1` includes the HAIP walkthroughs alongside existing ones

## Dependencies

- Docker stack running with all services healthy (including Sorcha.Haip.Service)
- Specs 093-098 merged to master (confirmed)
- Sorcha.Agent CLI tool (`src/Apps/Sorcha.Agent/`)
- `Sorcha.Cryptography.SdJwt` for SD-JWT operations
- `SorchaWalkthrough` PowerShell module (`walkthroughs/modules/`)
- Consumer Persona API (spec 092) for identity claim sourcing

## Assumptions

- The HAIP service uses ephemeral signing keys in Docker mode (acceptable for walkthroughs; production key wiring is out of scope)
- The agent's wallet directory is local to the filesystem (no Redis or database persistence needed for walkthrough scenarios)
- Blueprint creation for the driving licence walkthrough uses the existing JSON file approach, not the Fluent API
- The walkthroughs target the Docker stack via `http://localhost` (API Gateway port 80)
- Persona data for the identity credential is hardcoded in the walkthrough scripts (not fetched from the Persona API at runtime — the API writes are done in setup.ps1)

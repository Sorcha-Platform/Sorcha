# Tasks: Service-to-Service mTLS Workload Identity (F191, #1420)

**Input**: `specs/191-mtls-workload-identity/` — plan.md, research.md (D1–D10), data-model.md, contracts/, quickstart.md

**TDD is required** (repo standard + spec SC-006): tests are written first and observed RED where the
guard is new; guards that come up green first run MUST be mutation-checked (weaken → named test RED →
restore). License header + file-scoped namespaces on every new file. `dotnet build` before `dotnet test`;
`dotnet test` takes ONE project at a time.

## Phase 1: Setup

- [X] T001 Create leaf library project `src/Common/Sorcha.WorkloadIdentity/Sorcha.WorkloadIdentity.csproj` (net10.0, nullable, BCL-only — NO new NuGet deps, no `<Version>` per §14) and add to `Sorcha.sln`
- [X] T002 Create test project `tests/Sorcha.WorkloadIdentity.Tests/Sorcha.WorkloadIdentity.Tests.csproj` (xUnit v3 + FluentAssertions, mirroring an existing Common test csproj) and add to `Sorcha.sln`
- [X] T003 [P] Add `config/workload-certs/` to `.gitignore` (joins `.env`/`docker/certs` per-deploy material precedent)

## Phase 2: Foundational (blocks all user stories)

**Library primitives every story consumes — each is TDD: test file first, RED, then implement.**

- [X] T004 [P] Tests for SPIFFE id build/parse/reject in `tests/Sorcha.WorkloadIdentity.Tests/SpiffeIdTests.cs` (round-trip; Ordinal full-URI equality; reject non-spiffe scheme, wrong path shape, empty domain; trust domain lowercased)
- [X] T005 [P] Implement `src/Common/Sorcha.WorkloadIdentity/SpiffeId.cs` (`spiffe://{installation}/service/{clientId}` build/parse/equality)
- [X] T006 [P] Tests for issuance in `tests/Sorcha.WorkloadIdentity.Tests/WorkloadCertificateAuthorityTests.cs` (CA: P-256 self-signed, CA=true pathlen 0, ~5y; leaf: CA-signed, URI SAN = SpiffeId, DNS SAN, EKU clientAuth, ~2y; server cert: EKU serverAuth, DNS SAN; PFX round-trip with password)
- [X] T007 Implement `src/Common/Sorcha.WorkloadIdentity/WorkloadCertificateAuthority.cs` using BCL `CertificateRequest` (pattern precedent: Tenant `Trust/X509CertificateBuilder.cs` — do NOT reference that project; this is a separate trust rail per research D4)
- [X] T008 [P] Tests for chain validation in `tests/Sorcha.WorkloadIdentity.Tests/WorkloadTrustBundleTests.cs` — the refusal truth table: valid leaf→OK; other-CA leaf→refused; expired→refused; not-yet-valid→refused; multi-root bundle accepts leaves of BOTH roots (rotation overlap); single-root bundle refuses old-root leaf
- [X] T009 Implement `src/Common/Sorcha.WorkloadIdentity/WorkloadTrustBundle.cs` (PEM bundle load; `X509Chain` with `CustomRootTrust` + `X509RevocationMode.NoCheck`; validation helper usable from both Kestrel callback and tests)
- [X] T010 [P] Tests for loading + inventory in `tests/Sorcha.WorkloadIdentity.Tests/WorkloadCertificateLoaderTests.cs` (PFX by path; base64 blob; wrong password / missing file ⇒ typed exception naming the source — fail-fast contract FR-009) and `WorkloadCertificateInventoryTests.cs` (ok/expiring/expired classification vs threshold)
- [X] T011 [P] Implement `src/Common/Sorcha.WorkloadIdentity/WorkloadCertificateLoader.cs` (VerifierCertificate path-or-base64 pattern) and `src/Common/Sorcha.WorkloadIdentity/WorkloadCertificateInventory.cs`
- [X] T012 [P] Implement `src/Common/Sorcha.WorkloadIdentity/WorkloadIdentityConfig.cs` — canonical config-key constants per `contracts/config-keys.md` (client keys, Tenant `ServiceAuth:Mtls:*`, `ServiceAuth:DisableSharedSecrets`, `WorkloadIdentity:ExpiryWarningDays`) + the default 8-principal client_id→DNS map (verify verifier hostname `sorcha-verifier` against docker-compose.yml while here)
- [X] T013 Checkpoint: `dotnet build` clean; `dotnet test tests/Sorcha.WorkloadIdentity.Tests` green; every guard that never ran RED mutation-checked (weaken CustomRootTrust to system roots; skip expiry; case-insensitive SpiffeId compare) with the failing test named in the commit message

## Phase 3: User Story 1 — cert-bound token mint (P1, MVP)

**Goal**: a service with only a workload certificate acquires a byte-identical service token; every invalid-credential case refused distinguishably.
**Independent test**: real-Kestrel integration suite (T020) passes with NO secret configured anywhere in it.

- [ ] T014 [US1] Server tests first in `tests/Sorcha.Tenant.Service.Tests/ServiceAuth/CertificateCredentialServiceTests.cs`: given a validated client cert (unit level — cert passed in), `ServiceAuthService` mints for SAN==expected(client_id), refuses SAN mismatch (log carries both ids), refuses unknown/Suspended/Revoked principal, scope intersection unchanged, token claims identical to secret path (assert claim-set equality against a secret-minted token)
- [ ] T015 [US1] Implement cert-credential path in `src/Services/Sorcha.Tenant.Service/Services/ServiceAuthService.cs` (+`IServiceAuthService` signature as needed): secretless `client_credentials` accepts a chain-validated `X509Certificate2`, extracts URI SAN, matches `SpiffeId` for requested client_id, then reuses the existing principal/scope/token code untouched
- [ ] T016 [US1] Wire endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/ServiceAuthEndpoints.cs`: when `client_secret` absent, pull `HttpContext.Connection.ClientCertificate` into the service call (both `/token` and `/token/delegated` — delegated parity per contract); update `.WithSummary()`/`.WithDescription()`
- [ ] T017 [US1] Kestrel mTLS listener extension in `src/Services/Sorcha.Tenant.Service/Extensions/WorkloadMtlsExtensions.cs` + hook in `Program.cs`: additive `ListenAnyIP(ServiceAuth:Mtls:Port→8443)` ONLY when server cert + bundle configured; `ClientCertificateMode.RequireCertificate`; `ClientCertificateValidation` delegates to `WorkloadTrustBundle`; plaintext :8080 listener untouched; unreadable material ⇒ startup throw (FR-009)
- [ ] T018 [US1] Client tests first in `tests/Sorcha.ServiceClients.Tests/Auth/ServiceAuthClientCertModeTests.cs` (locate the existing project testing `Sorcha.ServiceClients.Http`; create the file alongside its ServiceAuth tests): cert configured ⇒ requests target `ServiceAuth:MtlsTokenAddress` with client cert attached and server validated ONLY against `ServiceAuth:TrustBundle`; cert configured but unreadable ⇒ constructor/startup throws (NEVER falls back to secret); no cert ⇒ existing behaviour byte-for-byte (existing tests stay green untouched — that suite is the no-delta guard); both configured ⇒ cert wins + one startup log
- [ ] T019 [US1] Implement cert mode in `src/Common/Sorcha.ServiceClients.Http/Auth/ServiceAuthClient.cs`: `SocketsHttpHandler.SslOptions` (ClientCertificates + RemoteCertificateValidationCallback via `WorkloadTrustBundle`); add `Sorcha.WorkloadIdentity` ProjectReference to `Sorcha.ServiceClients.Http.csproj`; secret path untouched
- [ ] T020 [US1] THE JOIN (SC-006): real-socket mTLS integration tests in `tests/Sorcha.Tenant.Service.Tests/ServiceAuth/MtlsMintIntegrationTests.cs` — `WebApplicationFactory.UseKestrel` (fallback per research D10: hand-built WebApplication from Tenant's own registration+mapping extensions — production validation code either way, NO test auth handler), Testing env (in-memory stores), certs issued via `WorkloadCertificateAuthority`; matrix: happy mint (then token passes `RequireService` shape assertions); wrong-CA ⇒ handshake fails; expired leaf ⇒ handshake fails; SAN↔client_id mismatch ⇒ refused; suspended principal ⇒ refused; secret path on plaintext listener still mints (coexistence)
- [ ] T021 [US1] Mutation-check T020 (SC-006): weaken bundle validation to accept any root ⇒ wrong-CA test RED; remove SAN comparison ⇒ mismatch test RED; restore; record the perturbations in the test file header comment
- [ ] T022 [US1] Checkpoint: `dotnet build` clean; `dotnet test tests/Sorcha.Tenant.Service.Tests` + ServiceClients test project green; existing ServiceAuth suites untouched-and-green (SC-004 evidence)

## Phase 4: User Story 2 — CLI-owned certificate lifecycle (P2)

**Goal**: `sorcha workload-ca init|status|renew|rotate-ca` owns the full lifecycle; installer provisions via the CLI.
**Independent test**: temp-dir lifecycle round-trip (init idempotent → status exit codes → forced renew → rotate overlap → complete) using only CLI invocations.

- [ ] T023 [P] [US2] CLI tests first in `tests/Sorcha.Cli.Tests/Commands/WorkloadCaCommandTests.cs` (temp dirs; follow existing CLI command-test pattern): init creates CA/bundle/8 leaves/server cert per `contracts/workload-ca-cli.md` layout; re-run ⇒ all `unchanged`; status exit 0/2/1 semantics + threshold; renew only-expiring vs `--all` (fresh keypair — assert new serial+key); rotate-ca ⇒ bundle=[new,old] + all re-issued + `ca.previous.pfx`; `--complete` ⇒ single-root bundle + previous deleted; `--complete` on single-root bundle refuses exit 1
- [ ] T024 [US2] Implement `src/Apps/Sorcha.Cli/Commands/WorkloadCa/` (`WorkloadCaCommand.cs` group + `InitCommand.cs`, `StatusCommand.cs`, `RenewCommand.cs`, `RotateCaCommand.cs`) on System.CommandLine 2.0.2 + Spectre.Console table output; options `--dir/--installation/--password`(env `WORKLOAD_CERT_PASSWORD`)/`--services`/`--threshold-days`/`--all`/`--complete`; temp+rename writes; add `Sorcha.WorkloadIdentity` ProjectReference to `Sorcha.Cli.csproj`; register the group where existing root commands register
- [ ] T025 [US2] `src/Apps/Sorcha.Cli/Dockerfile` (NEW): publish-based image, entrypoint `sorcha`; MUST include §14 `ARG GITHUB_RUN_NUMBER`/`ARG GITHUB_RUN_ATTEMPT` + matching ENV after the `COPY src/` line; verify `scripts/check-dockerfile-version-args.ps1` passes locally
- [ ] T026 [US2] Add CLI image to `.github/workflows/docker-publish.yml` (mirror an existing service entry incl. build-args; note `docker-ci.yml` `SERVICE_PATHS` if applicable — grep the consumer per seam-bug #7 before changing value formats)
- [ ] T027 [US2] Wire installer in `scripts/sorcha-setup.sh`: generate `WORKLOAD_CERT_PASSWORD` into `.env` (existing generator chain beside service secrets); provision via `sorcha` on PATH else `docker run --rm -v ./config/workload-certs:/certs <cli-image> workload-ca init --dir /certs --installation "$INSTALLATION_NAME"`; idempotent re-run; `bash -n` clean
- [ ] T028 [US2] Compose wiring in `docker-compose.yml` per `contracts/config-keys.md`: per service mount ONLY its own `services/{client_id}.pfx` + public `ca/bundle.pem` (NEVER the whole dir — key isolation) + `ServiceAuth__ClientCertificate/Password/TrustBundle` env; tenant additionally server PFX + `ServiceAuth__Mtls__*`; `${*_SERVICE_SECRET:?}` guards UNTOUCHED (coexistence); validate with `docker compose config`
- [ ] T029 [US2] Checkpoint: `dotnet test tests/Sorcha.Cli.Tests` green; `Sorcha.Cli.ContractTests` still green (no server DTO changes expected — confirm); version-args gate + `docker compose config` pass

## Phase 5: User Story 3 — retire shared secrets per deployment (P3)

**Goal**: `ServiceAuth:DisableSharedSecrets=true` refuses every secret-presenting service-auth path while cert path continues.
**Independent test**: flag-matrix unit/integration tests.

- [ ] T030 [P] [US3] Tests first in `tests/Sorcha.Tenant.Service.Tests/ServiceAuth/DisableSharedSecretsTests.cs`: flag false ⇒ both paths mint (coexistence); flag true ⇒ secret `client_credentials` AND secret delegated refused with explicit "shared secrets disabled" error while cert path mints; flag true ⇒ startup log line present (assert via logger capture)
- [ ] T031 [US3] Implement flag in `src/Services/Sorcha.Tenant.Service/Services/ServiceAuthService.cs` (+ prominent startup log in `Program.cs`/extension; note in code that `/rotate-secret` becomes inert by construction when flag on)
- [ ] T032 [US3] Checkpoint: Tenant test project green; refusal error text matches `contracts/service-auth-mtls.md`

## Phase 6: User Story 4 — expiry observability (P3)

**Goal**: health check + metric warn before certs expire.
**Independent test**: health check states for ok/expiring/expired/unconfigured.

- [ ] T033 [P] [US4] Tests first in `tests/Sorcha.ServiceDefaults.Tests/WorkloadCertificateHealthCheckTests.cs` (locate/create alongside existing ServiceDefaults tests): >window ⇒ Healthy; inside window ⇒ Degraded naming cert + expiry; expired ⇒ Unhealthy; unconfigured ⇒ Healthy not-applicable (FR-010/S4 scenarios)
- [ ] T034 [US4] Implement in `src/Common/Sorcha.ServiceDefaults/` (new `WorkloadCertificateHealthCheck.cs` + registration inside `AddServiceDefaults`/health extension): check name `workload-certificate`; gauge `sorcha_workload_cert_days_to_expiry{subject}` on new meter `Sorcha.WorkloadIdentity`; add `Sorcha.WorkloadIdentity` ProjectReference to `Sorcha.ServiceDefaults.csproj`; `WorkloadIdentity:ExpiryWarningDays` default 30
- [ ] T035 [US4] Checkpoint: ServiceDefaults tests green; one service builds+runs with no cert configured and reports Healthy (no dev regression)

## Phase 7: Polish, docs sync, verification

- [ ] T036 [P] Docs sync (CLAUDE.md policy — PR is not approvable without): Tenant Service `README.md` (mTLS listener, cert credential, DisableSharedSecrets); `src/Apps/Sorcha.Cli/README.md` (workload-ca group); `docs/reference/API-DOCUMENTATION.md` (mint contract delta); `docs/getting-started/PORT-CONFIGURATION.md` (tenant 8443 internal); `docs/guides/AUTHENTICATION-SETUP.md` (cert-mode s2s + quickstart retirement runbook)
- [ ] T037 [P] Update `.specify/MASTER-TASKS.md` (F191 entry 📋→🚧→✅ as appropriate) and add issue cross-reference #1420
- [ ] T038 Full gate: `dotnet build` (0 warnings in changed projects), affected test projects each green (WorkloadIdentity, Tenant, ServiceClients, Cli, ServiceDefaults), `scripts/check-secrets.ps1` green (no new material tracked), `scripts/check-dockerfile-version-args.ps1` green, `docker compose config` green
- [ ] T039 Local live smoke (pre-PR, seam discipline): `sorcha workload-ca init` against a scratch dir + run the T020 suite + start the local compose stack with generated certs and observe one real service acquire its token via the mTLS endpoint (log evidence) with golden secret path also still working; record evidence in PR body
- [ ] T040 Create PR against master (branch `191-mtls-workload-identity`): spec/plan/contracts + implementation; PR body carries SC checklist w/ evidence, the coexistence-default statement, and the explicit post-merge n1 live-verification plan (SC-007: deploy → secretless service mints → AIAS golden path → flip DisableSharedSecrets → re-verify) per quickstart.md

## Dependencies

- Phase 2 (library) blocks everything downstream; T005→T007→T009 have build-order coupling (SpiffeId used by issuance; issuance used by bundle tests).
- US1 (Phase 3) depends only on Phase 2 → **MVP = Phases 1–3**.
- US2 (Phase 4) depends on Phase 2; independent of US1 (parallelizable after T013, subject to the one-implementer-per-checkout rule — sequence unless worktree-isolated).
- US3 (Phase 5) touches the same `ServiceAuthService` as US1 → after Phase 3.
- US4 (Phase 6) depends only on Phase 2; parallelizable with US2/US3 (same checkout caveat).
- Phase 7 last; T039 needs Phases 3+4 (compose wiring) complete.

## Implementation strategy

MVP-first: Phases 1–3 prove the credential replacement end-to-end (SC-001/002/004/006) before any
delivery machinery exists. Then US2 makes it operable, US3 makes it enforceable, US4 observable.
Live n1 verification (SC-007) is post-merge, planned in the PR body — the retire flag ships
default-false so merging is behaviour-neutral for every existing deployment.

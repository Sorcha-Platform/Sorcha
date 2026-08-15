# Contract: Configuration & Delivery Wiring (F191)

Canonical key constants live in `Sorcha.WorkloadIdentity.WorkloadIdentityConfig` — call sites
bind through them, never string literals (repo "one home" discipline, CLAUDE.md §15–§17 spirit).

> **Delivery model (amended during implementation):** certificate material is delivered as
> **base64 env vars from `.env`**, not bind mounts. Rationale: bind-mounting per-service PFX
> *files* means a bare `docker compose up` before provisioning makes Docker create junk
> **directories** at the missing host paths, silently poisoning later `workload-ca init` writes —
> exactly the seam-bug class this repo logs. Base64-in-env matches the #1412 secret delivery
> model byte-for-byte: `${VAR:-}` empty defaults keep everything inert, per-service isolation is
> preserved (each container sees only its own material), and the loaders already accept base64
> (`WorkloadCertificateLoader.Load` for PKCS#12; `WorkloadTrustBundle.Resolve` for
> path / inline-PEM / base64-PEM).

## Per-service (client) — compose env additions

```yaml
# every backend service (blueprint shown)
environment:
  ServiceAuth__ClientCertificate: ${BLUEPRINT_WORKLOAD_CERT:-}          # base64 PKCS#12
  ServiceAuth__ClientCertificatePassword: ${WORKLOAD_CERT_PASSWORD:-}
  ServiceAuth__TrustBundle: ${WORKLOAD_TRUST_BUNDLE:-}                  # base64 PEM bundle
  # ServiceAuth__MtlsTokenAddress defaults to https://tenant-service:8443 — set only to override
  # ServiceAuth__ClientSecret: ${..._SERVICE_SECRET:?} stays — coexistence until retire step
```

Per-service env vars (aligned with the `*_SERVICE_SECRET` naming): `BLUEPRINT_WORKLOAD_CERT`,
`WALLET_WORKLOAD_CERT`, `REGISTER_WORKLOAD_CERT`, `TENANT_WORKLOAD_CERT`, `PEER_WORKLOAD_CERT`,
`VALIDATOR_WORKLOAD_CERT`, `VERIFIER_WORKLOAD_CERT`, `HAIP_WORKLOAD_CERT`.

Empty default ⇒ certificate mode inactive ⇒ legacy secret path, byte-for-byte. Set-but-broken
material ⇒ startup failure by design (FR-009) — never a silent fallback.

## Tenant (server) — compose env additions

```yaml
environment:
  ServiceAuth__Mtls__ServerCertificate: ${TENANT_WORKLOAD_SERVER_CERT:-}   # base64 PKCS#12
  ServiceAuth__Mtls__ServerCertificatePassword: ${WORKLOAD_CERT_PASSWORD:-}
  ServiceAuth__Mtls__TrustBundle: ${WORKLOAD_TRUST_BUNDLE:-}
  # ServiceAuth__Mtls__Port: 8443 (default)
  # ServiceAuth__DisableSharedSecrets: "true"   # retire step ONLY — never default in this PR
```

Port 8443 is internal-only (no `ports:` publish). PORT-CONFIGURATION.md gains the row.

## Installer (`sorcha-setup.sh`)

- Generates `WORKLOAD_CERT_PASSWORD` into `.env` (same generator chain as service secrets).
- `ensure_workload_certs`: provisions via `sorcha workload-ca init --dir ./config/workload-certs
  --installation "$INSTALLATION_NAME"` (PATH binary first, else
  `docker run --rm -v <dir>:/certs sorchadev/cli`), then appends a marker-delimited block of
  base64 cert vars to `.env` (idempotent — the block is replaced on re-run).
- Provisioning failure degrades LOUDLY to the shared-secret path (warn + skip) — the install
  never half-configures certificate mode.
- After `workload-ca renew`/`rotate-ca`, re-run `./scripts/sorcha-setup.sh` (keep-existing-.env
  path) to re-encode the refreshed material into `.env`, then recreate containers.

## Ignore rules

`.gitignore` += `config/workload-certs/` (joins `.env` / `docker/certs` precedent). The
secret-scan gate must never see key material; PFX/PEM under that path are untracked by
construction, and the `.env` file was already ignored.

## Dev / Aspire

No keys set ⇒ secret mode with dev literals, zero delta (FR-011). Aspire AppHost is untouched in
this feature.

## HTTPS-redirection carve-out (seam found by the integration suite)

`UseHttpsEnforcement` runs `UseHttpsRedirection()` in every non-Development environment; it was
inert only because no in-container https listener existed. The workload mTLS listener would have
silently activated it and 307-redirected every plaintext internal caller onto the
client-cert-required port. ServiceDefaults now skips redirection when the workload listener is
the ONLY https surface (no explicit `https_port` / `HTTPS_PORT` configured) — preserving the
exact pre-F191 behaviour. Deployments that configure an explicit https port keep redirecting.

## Health/metrics surface

- Health check name: `workload-certificate` (registered by ServiceDefaults; Healthy when
  unconfigured).
- Meter `Sorcha.WorkloadIdentity`, gauge `sorcha_workload_cert_days_to_expiry{subject}`.
- `WorkloadIdentity:ExpiryWarningDays` default 30.

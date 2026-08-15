# Contract: Configuration & Delivery Wiring (F191)

Canonical key constants live in `Sorcha.WorkloadIdentity.WorkloadIdentityConfig` — call sites
bind through them, never string literals (repo "one home" discipline, CLAUDE.md §15–§17 spirit).

## Per-service (client) — compose additions

```yaml
# every backend service (blueprint shown); mounts deliver ONLY this service's key + public bundle
volumes:
  - ./config/workload-certs/services/service-blueprint.pfx:/workload/client.pfx:ro
  - ./config/workload-certs/ca/bundle.pem:/workload/ca-bundle.pem:ro
environment:
  ServiceAuth__ClientCertificate: /workload/client.pfx
  ServiceAuth__ClientCertificatePassword: ${WORKLOAD_CERT_PASSWORD:-}
  ServiceAuth__TrustBundle: /workload/ca-bundle.pem
  # ServiceAuth__MtlsTokenAddress defaults to https://tenant-service:8443 — set only to override
  # ServiceAuth__ClientSecret: ${..._SERVICE_SECRET:?} stays — coexistence until retire step
```

Empty-default rule: `${WORKLOAD_CERT_PASSWORD:-}` keeps bare deployments bootable ONLY because
`ServiceAuth__ClientCertificate` is what activates cert mode; a deployment that mounts certs
must have run the installer (which generates the password). Cert configured + unreadable/wrong
password ⇒ startup failure by design (FR-009).

## Tenant (server) — compose additions

```yaml
volumes:
  - ./config/workload-certs/server/tenant-service.pfx:/workload/server.pfx:ro
  - ./config/workload-certs/ca/bundle.pem:/workload/ca-bundle.pem:ro
  - ./config/workload-certs/services/tenant-service.pfx:/workload/client.pfx:ro   # its own client cert
environment:
  ServiceAuth__Mtls__ServerCertificate: /workload/server.pfx
  ServiceAuth__Mtls__ServerCertificatePassword: ${WORKLOAD_CERT_PASSWORD:-}
  ServiceAuth__Mtls__TrustBundle: /workload/ca-bundle.pem
  # ServiceAuth__Mtls__Port: 8443 (default)
  # ServiceAuth__DisableSharedSecrets: "true"   # retire step ONLY — never default in this PR
```

Port 8443 is internal-only (no `ports:` publish). PORT-CONFIGURATION.md gains the row.

## Installer (`sorcha-setup.sh`)

- Generates `WORKLOAD_CERT_PASSWORD` into `.env` (same generator chain as service secrets).
- Provisions material: `sorcha workload-ca init --dir ./config/workload-certs --installation
  "$INSTALLATION_NAME"` via PATH binary, else CLI docker image.
- Idempotent across re-runs (delegates to the CLI's own idempotence).

## Ignore rules

`.gitignore` += `config/workload-certs/` (joins `.env` / `docker/certs` precedent). The
secret-scan gate must never see key material; PFX/PEM under that path are untracked by
construction.

## Dev / Aspire

No keys set ⇒ secret mode with dev literals, zero delta (FR-011). Aspire AppHost is untouched in
this feature.

## Health/metrics surface

- Health check name: `workload-certificate` (registered by ServiceDefaults; Healthy when
  unconfigured).
- Meter `Sorcha.WorkloadIdentity`, gauge `sorcha_workload_cert_days_to_expiry{subject}`.
- `WorkloadIdentity:ExpiryWarningDays` default 30.

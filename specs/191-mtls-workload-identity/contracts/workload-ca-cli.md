# Contract: `sorcha workload-ca` command group (F191)

Common options: `--dir <path>` (cert directory, default `./config/workload-certs`),
`--installation <name>` (default `sorcha` — must match the deployment's
`JwtSettings:InstallationName`), `--password <pw>` / env `WORKLOAD_CERT_PASSWORD` (PFX password).

## `sorcha workload-ca init`

Creates: Workload CA (EC P-256, 5y) at `ca/ca.pfx` + public `ca/bundle.pem`; one leaf per
service principal (2y, URI SAN `spiffe://{installation}/service/{client_id}`, DNS SAN per the
data-model map) at `services/{client_id}.pfx`; server cert `server/tenant-service.pfx`
(DNS SAN `tenant-service`).

- `--services <client_id=dnshost,...>` overrides the default 8-principal map.
- **Idempotent**: existing valid (parseable, in-validity, correct installation) material is left
  untouched and reported as `unchanged`; missing/invalid pieces are (re)issued.
- Exit codes: 0 success; 1 error (nothing partially written on failure of a single artifact —
  write via temp+rename).

## `sorcha workload-ca status`

Table per artifact: kind, subject, SPIFFE/DNS identity, notAfter, days remaining, state
(`ok` / `expiring` / `expired` / `invalid`).

- `--threshold-days <n>` (default 30).
- Exit codes: 0 all ok; **2** at least one `expiring`/`expired`/`invalid` (scriptable); 1 error.

## `sorcha workload-ca renew`

Re-issues (fresh keypair) leaves + server cert whose remaining validity < threshold, signed by
the current CA. `--all` forces every leaf. CA itself is never renewed here (that is rotate-ca).
Prints: which artifacts were re-issued and the instruction that services load certificates at
startup — recreate the containers to pick them up.

- Exit codes: 0 (including nothing-to-do); 1 error.

## `sorcha workload-ca rotate-ca`

Step 1 (`rotate-ca`): new CA generated; `bundle.pem` becomes [newRoot, oldRoot]; ALL leaves +
server cert re-issued under the new CA; old CA pfx retained as `ca/ca.previous.pfx`. Operator
then recreates containers (services now present new-CA leaves; validators accept both roots).

Step 2 (`rotate-ca --complete`): removes the old root from `bundle.pem` and deletes
`ca/ca.previous.pfx`. Refuses (exit 1, explanation) if step 1's re-issue is not detectable
(bundle has a single root).

- Exit codes: 0 success; 1 error.

## Global guarantees

- Private keys never printed, logged, or written world-readable beyond what the container mount
  model requires (documented file modes; parity with `docker/certs` handling).
- Deterministic, parseable stdout (Spectre tables for humans; `--json` optional output is a
  nice-to-have, not required for v1).
- All commands honour `--dir` layouts produced by any earlier version of the group (layout is
  the contract; see data-model).

## Distribution

- NuGet tool (existing `cli-publish.yml`) — unchanged pipeline picks the group up.
- **New**: `src/Apps/Sorcha.Cli/Dockerfile` + docker-publish entry; image entrypoint `sorcha`.
  MUST carry the §14 `ARG GITHUB_RUN_NUMBER`/`GITHUB_RUN_ATTEMPT` (+ENV) block after the
  restore-invalidating COPY, or the version-args CI gate fails.
- `sorcha-setup.sh` invocation order: `sorcha` on PATH → `docker run --rm -v <dir>:/certs
  <cli-image> workload-ca init --dir /certs …`.

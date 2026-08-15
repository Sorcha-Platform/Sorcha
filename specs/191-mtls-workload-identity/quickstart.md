# Quickstart: Workload-Identity Service Auth (F191)

## Fresh install (nothing to do)

`./scripts/sorcha-setup.sh` provisions everything: generates `WORKLOAD_CERT_PASSWORD`, runs
`sorcha workload-ca init`, and `docker compose up` brings services up minting service tokens via
their workload certificates (secrets still wired as fallback-in-coexistence, unused for token
acquisition once certs are configured).

## Existing deployment — enable cert mode

```bash
git pull && ./scripts/sorcha-setup.sh          # adds WORKLOAD_CERT_PASSWORD + config/workload-certs/
docker compose pull && docker compose up -d --force-recreate <app services>
# verify: any service log shows token acquisition via mTLS endpoint; health check `workload-certificate` Healthy
```

## Verify a service is truly secretless-capable

```bash
# remove ServiceAuth__ClientSecret from ONE service's environment (compose override), recreate it,
# confirm it still acquires a service token and its downstream calls work
```

## Retire shared secrets (per deployment, ONLY after the verification above)

```bash
# tenant-service environment:
#   ServiceAuth__DisableSharedSecrets: "true"
docker compose up -d --force-recreate --no-deps tenant-service
# expected: startup log states secret-based service auth is disabled;
# any secret-presenting mint attempt is refused; cert-based minting unaffected.
# then remove the 8 *_SERVICE_SECRET client wirings at leisure.
```

## Lifecycle

```bash
sorcha workload-ca status                     # exit 2 when anything is inside 30 days
sorcha workload-ca renew                      # re-issue expiring leaves, then recreate containers
sorcha workload-ca rotate-ca                  # new CA, bundle=[new,old], leaves re-issued → recreate
sorcha workload-ca rotate-ca --complete       # after all services are on new-CA leaves
```

## Troubleshooting

- Service fails at startup naming its client certificate → the material is missing/unreadable in
  the container; check the mount + `WORKLOAD_CERT_PASSWORD`. This is deliberate fail-fast — cert
  mode never silently falls back to secrets.
- Mint refused with an identity-mismatch log → the mounted PFX belongs to a different service or
  a different installation name; re-run `workload-ca status` and compare SPIFFE ids.
- TLS handshake failures after a CA rotation → containers not recreated between `rotate-ca` and
  `--complete`, or `--complete` run too early (old-root leaves still live).
- Health check `workload-certificate` Degraded → renewal window; run `renew` + recreate.

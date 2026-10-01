# Quickstart: verifying Feature 197

## Local (docker compose)

```bash
# 1. Drift on a clean node — expect inSync for all four
sorcha system-register drift

# 2. Make the image ahead: edit a presentational field in
#    blueprints/templates/register-governance-v1.json, rebuild register-service, restart it.
docker-compose build register-service && docker-compose up -d --force-recreate register-service
sorcha system-register drift            # register-governance-v1 → imageAhead; /health → Degraded

# 3. Raise a proposal on an ordinary register NOW (pins the current definition, call it v_old)

# 4. Preview, then publish
sorcha system-register publish register-governance-v1 --dry-run
sorcha system-register publish register-governance-v1 --expected-current <current id from dry-run>
sorcha system-register drift            # → inSync once sealed

# 5. Approve the step-3 proposal → it enacts (judged by v_old)
# 6. Raise a new proposal → its governanceDefinitionTxId is the new id
```

Refusal checks: repeat step 4 as an org Administrator (403), with a consumer-tier token (403), with a
stale `--expected-current` (409), and after reverting the template + rebuild (imageBehind → 409). Each
refusal appears in the system org's audit log.

## Live gate (n1 + tiny)

1. Deploy validator-service, then register-service, to both nodes.
2. `drift` on both → inSync (or imageAhead for governance if the release carries the new schema).
3. Raise a proposal on a fresh register (two-org quorum) on n1.
4. Publish the new `register-governance-v1` on n1.
5. Within one minute, tiny's `drift` reports inSync — no action on tiny.
6. Approve + enact the step-3 proposal → enacted.
7. Raise another proposal → pins the new publication id.
8. `sorcha_governance_definition_pin_fallback` reads 0 for steps 3–7 on both nodes.

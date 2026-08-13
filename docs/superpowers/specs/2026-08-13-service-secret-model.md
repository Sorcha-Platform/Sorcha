# Per-deploy service-secret model — design note (needs sign-off)

**Date:** 2026-08-13
**Status: IMPLEMENTED** (branch `fix/1412-per-deploy-service-secrets`, issue #1412). Option A below
shipped as designed: `scripts/sorcha-setup.sh` generates the 8 secrets into `.env`;
`docker-compose.yml` reads each via `${VAR:-<literal>}` on both the client
(`ServiceAuth__ClientSecret`) and the Tenant seed (`Seed__ServicePrincipals__{clientId}`) sides, so a
bare `docker compose up` still boots on the committed literals while a generated `.env` overrides
both in lockstep; `DatabaseInitializer` resolves each principal's secret via the new
`ServicePrincipalSecretResolver.Resolve` (configured → dev-literal-in-Development →
fail-closed-in-Production/Staging → generated-elsewhere). The `.secrets-allowlist` literals are
untouched (still grandfathered `:-` defaults). The analysis below is kept for the rationale trail;
treat "DECISION REQUIRED" language as historical.

**Original status line (superseded):** DECISION REQUIRED before implementation. This is Task 3 of the Public Gates plan
(`docs/superpowers/plans/2026-08-13-public-gates-readiness.md`), the deeper root-cause half of the
#1397 fix. It changes how every node authenticates service-to-service, so it must not be implemented
and deployed without the maintainer's explicit go-ahead.

**Why it is not blocking #1397:** PRs #1407 (service-token minting moved off the public gateway) and
#1408 (system-wallet signing restricted to the Validator principal) already close the #1397 exploit
chain — two independent kill-switches. This task removes the *root cause* (usable committed secrets)
as defence in depth. It is the right fix, but it is riskier than the two kill-switches, so it is
sequenced last and gated on sign-off.

---

## The finding: Production service auth is effectively unfinished

There are **two** sides to each service secret, and today they only agree by both being the same
committed literal:

| Side | What it is | Where it lives |
|------|-----------|----------------|
| **Client** (what a service *sends* to prove itself) | `ServiceAuth__ClientSecret` env var | `docker-compose.yml` — 8 hardcoded literals (`blueprint-service-secret`, …), lines 240/298/397/452/517/588/718/754. No `${VAR}` interpolation. |
| **Server** (what the Tenant Service *accepts*) | `ServicePrincipal.ClientSecretEncrypted` in the Tenant DB | Seeded by `DatabaseInitializer.SeedServicePrincipalsAsync` (`src/Services/Sorcha.Tenant.Service/Data/DatabaseInitializer.cs:389-494`). |

The seeder branches on environment (`DatabaseInitializer.cs:467-481`):

```csharp
var isDevelopment = _configuration["ASPNETCORE_ENVIRONMENT"] == "Development" || ...;
...
var clientSecret = isDevelopment && !string.IsNullOrEmpty(sp.DevSecret)
    ? sp.DevSecret            // the committed literal, e.g. "blueprint-service-secret"
    : GenerateClientSecret(); // a fresh RANDOM 32-byte secret
```

So:

- **In Development** (what n1 runs today): server seeds the literal, client sends the literal → they
  match, auth works — **and the literals are valid credentials for anyone on the internet** (the
  #1397 root cause).
- **In Production**: server seeds a **random** secret the client side never learns, while compose
  still sends the literal → **they do not match, and all inter-service auth fails.** There is no
  mechanism anywhere that surfaces the generated secret back to the client services.

**Conclusion:** flipping n1 to `ASPNETCORE_ENVIRONMENT=Production` is NOT a fix on its own — it would
break the platform (services can't talk). That is exactly why n1 runs as Development, which is what
keeps the committed secrets live. The two must be fixed together.

The installer already has the right shape for the fix but doesn't use it: `scripts/sorcha-setup.sh`
`write_env_file` generates a random `JWT_SIGNING_KEY` and DB passwords into `config/.env`, but never
touches `ServiceAuth__*` — those 8 secrets are the only credentials in the stack that are neither
generated nor `.env`-sourced.

---

## Recommended approach (Option A)

**Generate the 8 service secrets per deploy, and inject each into BOTH the client env and the server
seed, in every environment — so client and server always agree on a value that is unique per
installation and never committed.**

1. **Installer** (`scripts/sorcha-setup.sh`): add `generate_service_secret()` beside
   `generate_jwt_key()`; in `write_env_file`, emit 8 vars, e.g.
   `BLUEPRINT_SERVICE_SECRET`, `WALLET_SERVICE_SECRET`, … into `config/.env`.
2. **Compose** (`docker-compose.yml`): change the 8 client lines from literals to interpolation —
   `ServiceAuth__ClientSecret: ${BLUEPRINT_SERVICE_SECRET}` — AND pass the same 8 into the **Tenant**
   service's environment under a seed-config shape the initializer can read, e.g.
   `Seed__ServicePrincipals__service-blueprint__Secret: ${BLUEPRINT_SERVICE_SECRET}` (exact key shape
   is an implementation detail; it must be a config path `DatabaseInitializer` binds).
3. **Seeder** (`DatabaseInitializer`): read each principal's secret from configuration
   (`_configuration[...]` per `ClientId`) and seed **that** — not the committed literal, not a random
   value the client can't learn. Fall back to fail-closed (refuse to seed) in Production/Staging when
   a secret is absent, mirroring `SorchaIssuer.Resolve`'s fail-closed posture. Remove (or reduce to
   non-functional placeholders) the committed `DevSecret` literals so a bare `git clone && docker
   compose up` with no generated `.env` fails loudly rather than silently shipping known credentials.
4. **Secret-scan gate**: once the compose literals are gone, shrink `.secrets-allowlist` accordingly
   (it may only shrink).

### Why not the alternatives
- **"Just run Production"** — breaks inter-service auth (above).
- **Generate random server secrets only** — the client side never learns them; same breakage.
- **Keep literals but move off the public gateway** — that's #1407, already done; it stops *external*
  minting but leaves the literals valid inside the network and in the repo.

---

## The risk, and why sign-off is required

This rewrites how **every node** authenticates service-to-service. Get the client/server key shapes
out of step and the whole platform stops talking, with failures that surface far from the cause
(a 401 on some downstream call, not "secret mismatch"). On n1 specifically it must be rolled out as a
coordinated change: regenerate `config/.env` with the 8 secrets, recreate the stack so both the
client env and the Tenant seed pick them up, and confirm an end-to-end flow that exercises
inter-service auth (e.g. a credential-issuance walkthrough: Blueprint → Wallet sign) before trusting
it. Because the seed is written once at first-run, an existing DB may need its `ServicePrincipals`
rows re-seeded (or the DB recreated) for the new secrets to take effect — that interacts with the
pre-release "recreate the database" migration posture (CLAUDE.md §19).

**Decisions requested:**
1. Approve Option A (or direct a different approach).
2. Confirm the rollout appetite for n1: is recreating `config/.env` + the stack (and re-seeding /
   recreating the Tenant DB) acceptable, or should this wait for a maintenance window?
3. Confirm the config-key shape for the Tenant seed (`Seed__ServicePrincipals__{clientId}__Secret`
   vs a flatter `ServiceAuth__Seed__{clientId}`), or leave it to the implementer.

Related: #1397 (the two kill-switches shipped), #1409 (`DatabaseInitializer` also hardcodes the admin
password fallback — same file, same "fail-closed in prod" remedy; fix them together), and the
`.secrets-allowlist` grandfathered dev-secret cleanup.

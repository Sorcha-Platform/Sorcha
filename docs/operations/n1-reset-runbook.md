# n1 Reset / Re-Genesis Runbook

> **⚠ PROOF-OF-EXECUTION STATUS: NOT YET RUN.**
> This document is Public Gates readiness plan **Task 5**
> (`docs/superpowers/plans/2026-08-13-public-gates-readiness.md`). Step 1 (draft the runbook) is
> done — this file. **Step 2 ("execute it once, end to end") has NOT been done.** Because the
> procedure below wipes the live n1 demo, it must be run once, deliberately, in a maintenance
> window, and the result recorded here before this runbook can be considered proven. Until that
> entry exists, treat every command below as reviewed-but-unexecuted-as-a-whole (each individual
> command has been run at some point per the source skills — the *end-to-end sequence as written
> here* has not).
>
> **Proof-of-execution log (append here after the first live run):**
> | Date (UTC) | Operator | Genesis fp | `rehearse.ps1` result | Notes |
> |---|---|---|---|---|
> | _pending_ | | | | |

## 1. Purpose & when to run

This is the self-healing reset procedure for **n1.sorcha.dev**, the shared public demo node. Run
it when:

- n1 has been abused or left in a messy/unusable state (spam registers, exhausted rate limits,
  a stuck workflow instance, a poisoned test account) and the cheapest fix is a clean slate, or
- n1 has suffered (or needs) a `docker compose down -v` for any other reason (disk pressure,
  a broken migration, a deliberate schema reset), or
- the F189 governance live-test registers (issue **#1403**, T069) need clearing and a full wipe is
  the chosen way to do it (see §4).

**This procedure is DESTRUCTIVE.** It wipes n1's Postgres/Mongo/Redis data, which means:

- every platform user, organisation, wallet, register and workflow instance on n1 is gone;
- the currently-provisioned **AIAS conference demo** (org, issuer wallet, agent identity, register,
  blueprint) is gone and must be re-provisioned from scratch;
- the **genesis identity of the network changes** unless you deliberately reuse the compiled-in
  genesis (see §3.3) — a fresh mint gives n1 a new trust anchor that `tiny` must adopt too.

**It requires coordinating with `tiny`.** `tiny` is a second node on the same `sorcha-dev` network,
running in **`SyncOnly`** mode against n1 as the seed. If n1 re-genesises, `tiny` must be brought
onto the *same* genesis file within the **`VAL_TIME_002` genesis-freshness window** (default
**1 hour** — `ValidationEngine.GenesisMaxAge`; see the network-bootstrap skill) or its own
ingest-and-seal path will reject the transaction as stale. `tiny` has **no public hostname** — it
is reached only via `ssh tiny`; its live compose directory is `~/sorcha-test`; its gateway is
exposed on the host at **`:8090`**.

## 2. Preconditions

- **SSH access to n1**: `sorcha@51.105.7.135` (RG `sorcha-n1-uk`, VM `sorcha-n1-vm`). The box clock
  is **UTC** — mind this when reasoning about the 1-hour window. `az` CLI access to this RG is
  useful for starting the VM after auto-shutdown (11pm GMT daily) or fixing the SSH NSG rule if
  your IP changed, but is **not required** to run the reset itself — everything below is driven
  over SSH (the n1-deploy skill notes `az` is TLS-blocked on this machine's corporate proxy; SSH is
  not).
- **SSH access to `tiny`**: `ssh tiny` (already configured; no IP/NSG juggling documented here —
  see the n1-deploy skill / MEMORY.md `prodexec-project` topic if `tiny` needs infra changes).
- **Confirm the VM is running**: if idle past 11pm GMT, start it (`az vm start …`) before doing
  anything else — every command below will otherwise hang or fail opaquely.
- **Images**: n1 runs `sorchadev/*:latest` from DockerHub, built by the CI "Docker Publish"
  workflow on every `master` push. Confirm you're deploying what you think you are by checking the
  **`sorcha-ui-web`** footer / `info.version` after the stack is back up — it must read
  **`2.<run>.<attempt>`** (a CI image), never **`2.0.0-dev`** (a locally-built image still
  mounted). See CLAUDE.md §14 and the n1-deploy skill's "footer version is the deploy proof" note.
- **Trust anchor**: the network is `sorcha-dev`; its genesis is **compiled into** the service
  images (`src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json`), not
  externally mounted, *unless* you are running the fresh-mint / GenesisFile-mount path in §3.3
  Option A, which temporarily overrides it via a mounted file. The system register id is
  **deterministic** (`aebf26362e079087571ac0932d4db973`) regardless of which genesis mint is live.
- **Current live IDs are volatile and NOT hard-coded here.** Every genesis re-mint changes the
  AIAS org/identity/cyber register IDs and blueprint IDs. Check `demos/AIAS/state.json` (written by
  `run-demo.ps1`) and/or the MEMORY.md `n1-current-state` topic file for what's live *right now*
  before assuming an ID from an old note is still valid.

## 3. The reset sequence

Run `docker compose ... config --services` before acting on any compose file you've just edited —
it is a one-second gate against the "silent no-op" failure mode documented repeatedly in the
n1-deploy skill (a wrong/prefixed service name, or a duplicate YAML key, aborts the whole batch
with the old container quietly left running).

### 3.1 Snapshot before you wipe

- Note the current genesis fingerprint and AIAS IDs from `demos/AIAS/state.json` / MEMORY.md, in
  case anything needs to be cross-referenced after the reset.
- If T069 cleanup (§4) is being done as a **standalone** sweep rather than as a side-effect of this
  wipe, do it **before** tearing down (the registers won't exist to delete afterwards).

### 3.2 Tear down n1 — and actually remove the volumes

```bash
C="docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml"
ssh sorcha@51.105.7.135 "cd /opt/sorcha && $C down -v --remove-orphans"
```

**Known trap (n1-deploy skill, hit live 2026-08-10): `down -v` does not reliably remove n1's named
volumes.** A prior "clean" reset left Postgres intact while Mongo was recreated — the node came
back with a fresh ledger but the *old* platform users, and AIAS re-provisioning failed three steps
in with `A platform user with email assure-id-agent@aias.local already exists`, which reads like a
demo bug and is actually a dirty teardown. **Explicitly remove the volumes by name and verify:**

```bash
ssh sorcha@51.105.7.135 '
for v in sorcha_postgres-data sorcha_mongodb-data sorcha_redis-data sorcha_dataprotection-keys; do
  docker volume rm -f "$v"
done
docker volume ls | grep sorcha_'
```

Do **NOT** remove `sorcha_wallet-encryption-keys` — it is external-by-design and persists across
resets (network-bootstrap skill, Step 3).

### 3.3 Re-genesis n1

Two options, both documented in the n1-deploy skill. Pick one deliberately — they have different
consequences for `tiny` coordination.

**Option A — fresh mint (new trust anchor; what this runbook assumes by default, since it's what
Task 5's own wording specifies: "coordinated with tiny inside the VAL_TIME_002 window").** Fastest
reliable path per the n1-deploy skill's "⭐ PREFERRED re-genesis path" — no `az`, no
master-commit/Docker-Publish, no embedded-genesis rebuild:

```bash
# 1. Mint locally (fresh signedAt ⇒ inside the 1h VAL_TIME_002 window from this moment):
sorcha system-register create --network-id <NETWORK_ID> --output <scratch>/system-register-genesis.json
# emits BOTH system-register-genesis.json AND genesis-validator-key.json; SSR id is
# deterministically aebf26362e079087571ac0932d4db973 regardless of network-id.

# 2. scp both to the box, then run the on-box ceremony:
scp <scratch>/system-register-genesis.json  sorcha@51.105.7.135:/opt/sorcha/genesis-fresh.json
scp <scratch>/genesis-validator-key.json    sorcha@51.105.7.135:/opt/sorcha/genesis-validator-key.json
ssh sorcha@51.105.7.135 'cd /opt/sorcha && bash n1-regenesis-fixed.sh'
```

`n1-regenesis-fixed.sh` (already on the box) does: `down -v` → pull `:latest` → bring up
`wallet-service` alone → mint an HS256 service token from the box's `JWT_SIGNING_KEY` → POST the
mnemonic to `/api/v1/wallets/system/recover` → bring up the rest with the seed + genesisfile + smtp
+ ports overrides → shred the uploaded key file.

**⚠ DISCREPANCY, do not paper over it:** the two skills disagree on the example `--network-id`.
`network-bootstrap` (Step 1) mints with `--network-id sorcha-dev`; `n1-deploy`'s worked example
under this same preferred path mints with `--network-id n1-dev`. The MEMORY.md topic files
(`n1-current-state`, `sorcha-dev-compiled-in-root-2026-08-06`) show the network *currently* live on
n1 is named `sorcha-dev`. **Confirm the network-id you want before minting** — reusing `sorcha-dev`
preserves continuity with the network name `tiny` already expects; a different network-id is a
deliberate identity change, not a routine reset, and `tiny`'s own compose/subscription config may
need to change with it. This runbook does not resolve the discrepancy; it flags it as a decision
point.

**⚠ SECOND DISCREPANCY:** `n1-regenesis-fixed.sh` is documented as doing its own `down -v`, but
§3.2's trap (volumes surviving `down -v`) was observed on this same box. Do **not** skip the
explicit volume-removal step in §3.2 in reliance on the script doing it for you — run §3.2 first,
independently, then §3.3.

**Option B — reuse the compiled-in genesis (no new trust anchor; no window coordination needed for
`tiny`'s identity, only for the ingest-and-seal freshness check on n1 itself).** Use this when you
want a clean node without changing network identity. The box also carries an idempotent, gated
script for this: `/opt/sorcha/n1-recover.sh` (n1-deploy skill, "Clean re-genesis of n1" section,
2026-08-10). It widens `ValidationEngine__GenesisMaxAge` temporarily (via a **separate** compose
override file — never add a second `environment:` block to an existing service; that produces a
duplicate-YAML-key parse failure that takes the *whole* compose file down, as happened live on
2026-08-10), mounts the byte-identical embedded genesis, and runs the same
wallet-alone → import → up-rest ordering. Read the skill section before using it; it is more
involved than Option A and the on-box script's numbered output is the authority, not this summary.

> ⚠ **The widened window must be derived from the genesis age, and the seal gate must name the
> genesis transaction. Both were wrong, and together they reported a broken node as healthy
> (2026-08-17).** `n1-recover.sh` hard-coded `GenesisMaxAge=4 days`, written when the compiled-in
> genesis was 3 days old. Ten days later `VAL_TIME_002` refused it — but the script's gate only
> asked whether the earliest docket held at least one transaction, and by then the first
> blueprint-seed publish had taken docket 0. The script printed `GENESIS SEALED`, restored the
> 1-hour default, and left a node with no genesis in its ledger, no roster, and a trust anchor
> that had never been established. Everything downstream looked fine: 16 healthy containers,
> `/api/health` 200, blueprints publishing.
>
> The on-box script now sets a 3650-day window during the ceremony and waits for the genesis
> `txId` read out of the genesis file itself, so neither fault can recur. **A fixed window
> silently expires as the embedded genesis ages — never reintroduce one.** More generally: a
> gate that would pass on a node that did the wrong thing is not a gate.

**Ordering is load-bearing in both options:** wallet-service must come up **alone** and have the
validator key imported into it via `/api/v1/wallets/system/recover` **before** register-service or
validator-service start — otherwise those services auto-generate the wrong system wallet
(`CreateOrRetrieveSystemWalletAsync`) and lock the node out of its own genesis. Both on-box scripts
already respect this ordering; do not reorder if hand-rolling.

**Platform bootstrap is automatic.** Once the full stack is healthy, tenant-service's
`DatabaseInitializer` auto-seeds the System Admin Org, Public Org, and the seeded admin
(`admin@sorcha.local` / `Dev_Pass_2025!`, or `Seed:AdminPassword` if overridden). **Do not run the
CLI `bootstrap` command** — it 500s against an already-seeded node after first committing a stray
org (network-bootstrap skill, Step 4).

### 3.4 Verify n1's genesis sealed before touching tiny

⚠ **Check for the GENESIS TRANSACTION, not merely for a non-empty docket 0.** Counting transactions
answers "did this node seal anything", which is true of a node whose genesis was *refused* and whose
docket 0 was taken by the first blueprint-seed publish instead. That is not hypothetical — it
happened on 2026-08-17 and the run reported success (see the trap below). Ask for the txId:

```bash
ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password --authenticationDatabase admin --quiet --eval "
var d=db.getSiblingDB(\"sorcha_register_aebf26362e079087571ac0932d4db973\");
d.transactions.find({},{_id:0,TxId:1,DocketNumber:1,\"MetaData.TrackingData\":1}).forEach(function(t){
  var td=(t.MetaData&&t.MetaData.TrackingData)||{};
  print(\"d\"+(t.DocketNumber?t.DocketNumber.low:\"?\")+\"  \"+(td.Type==\"Genesis\"?\"GENESIS fp=\"+td.Fingerprint:(td.BlueprintId||\"-\")));});"'
```

A correct bootstrap looks **exactly** like this — the genesis in docket 0, then the four seeds:

```
d0  GENESIS fp=d75e14004364867dae55f44330330edf
d1  register-creation-v1
d2  register-governance-v1
d3  create-organisation-v1
d4  join-private-register-v1
```

If `d0` is a blueprint and no `GENESIS` row exists, the genesis was rejected — **stop**, do not
proceed to §3.5, and see the trap below. A genesis-less docket 0 silently breaks `SyncOnly`
replication for `tiny`. Then:

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://n1.sorcha.dev/api/health   # expect 200
# NOTE: plain http://localhost/api/health on the box returns 308 (Caddy https redirect) — not a fault.
```

### 3.5 Coordinate `tiny` inside the window

Only needed if Option A (fresh mint) was used — a fresh mint means `tiny`'s existing anchor is now
stale and it must adopt the new one **within the same `VAL_TIME_002` window** the mint was made in
(the clock started at the `sorcha system-register create` step in §3.3, not at this step — do not
dawdle on §3.3/§3.4). If Option B was used, `tiny` was never invalidated and this step is not
required (though re-syncing it after any n1 wipe is still good practice — see verification below).

```bash
scp <scratch>/system-register-genesis.json sorcha@tiny:~/sorcha-test/genesis-n1.json   # confirm the exact remote path with `ssh tiny 'ls ~/sorcha-test'` first — do not assume
ssh tiny 'cd ~/sorcha-test && docker compose -f docker-compose.yml -f docker-compose.tiny.yml -f docker-compose.genesisfile.yml down -v --remove-orphans'
ssh tiny 'cd ~/sorcha-test && docker compose -f docker-compose.yml -f docker-compose.tiny.yml -f docker-compose.genesisfile.yml pull'
ssh tiny 'cd ~/sorcha-test && docker compose -f docker-compose.yml -f docker-compose.tiny.yml -f docker-compose.genesisfile.yml up -d'
```

Apply the same "actually remove the named volumes" caution from §3.2 to `tiny` before `up -d`.
`tiny` runs `SyncOnly` and pulls the already-sealed genesis docket from n1 — it is not
freshness-window-bound on the *pull* itself (only n1's ingest-and-seal path is), but it still needs
the matching genesis file mounted to verify the sealed docket's signature against the right anchor.

**`tiny`-specific traps (n1-deploy skill / MEMORY.md, 2026-08-10):** `tiny`'s tenant image predates
n1's two-step org-selection login flow, so `POST /api/auth/login` there returns `access_token`
directly — a "failed" login on `tiny` using n1's flow expectations is usually just this. Its
register-subscription DTO binds `register_id` (snake_case), not `registerId` — camelCase 400s.
`tiny`'s gateway is on the host at `:8090` (not 80/443 — it has no public Caddy in front of it).

**Verify:** `tiny`'s SSR docket count > 0, matching n1's (see §5).

### 3.6 Re-provision the AIAS demo

Follow the demo-deploy skill exactly. Rebuild the `sorcha-agent` global tool first if it might be
stale (it must be F176/F183/F184-current or it will decide applications wrong):

```bash
rm -rf scratch-nupkg && dotnet pack src/Apps/Sorcha.Agent/Sorcha.Agent.csproj -c Release -o scratch-nupkg
dotnet tool uninstall --global sorcha.agent
dotnet tool install --global sorcha.agent --version 2.0.0-dev --add-source "$PWD/scratch-nupkg"

./demos/AIAS/run-demo.ps1 -Target n1 -Force
```

`run-demo.ps1` creates the AIAS org, issuer wallet, org VC-issuance master key, the Assure-ID agent
user/wallet/participant, an advertised DevMode register, and the vct-carrying blueprint, then
launches `sorcha-agent` in rules mode. It writes `demos/AIAS/state.json` — this is now the
authoritative source for the live AIAS org/register/blueprint IDs; update the MEMORY.md
`n1-current-state` topic afterwards.

### 3.7 Rehearse

```bash
./demos/AIAS/rehearse.ps1 -Target n1
```

Runs three scripted paths against the live pipeline: an approval (agent approves → credential
minted and delivered), a bad-postcode rejection, and an unverified-email rejection (F183 gate) —
all headless, no browser/phone needed. Exit 0 / "AIAS rehearsal PASSED" is the pass signal for the
whole runbook (§5).

**Known rehearsal gotchas** (demo-deploy skill): the first credential delivery after a re-genesis
can take up to ~60s (cold register seal + wallet sync) — the harness's delivery timeout is 120s, so
don't assume failure early; check the blueprint-service log for `Minted SorchaLocalWallet
credential …` before suspecting a functional break. Two `[!]` warnings about projection surfaces
are benign.

## 4. The T069 register-cleanup sweep (#1403)

Issue **#1403** tracks deleting the F189 governance live-test registers left on n1 (and replicated
to `tiny`): the T085/T086 gate registers, T048's three-org `Unanimous` register
(`a17208cf16a2460b9f1b1d304b2e5263`), the T049/T057 registers, and whatever T062/T063 create.

**If you are running the full destructive wipe in §3 (Option A or B), T069 is satisfied as a
side-effect** — `down -v` plus the explicit named-volume removal in §3.2 destroys every register on
n1, including all of the above, along with everything else. No separate action is needed; note the
sweep as closed in the same session you run this runbook, and update/close #1403 accordingly.

**If you ever need to clear just these test registers WITHOUT a full re-genesis** (e.g. a
lighter-weight cleanup that leaves the rest of n1's data alone), use the owner-attested delete
endpoint per-register instead:

```
DELETE /api/registers/{id}
```

Authorization is **attestation-based**: the caller's `wallet_address` claim must match a
slot-100 (`sorcha:register-attestation`) organisation key recorded in that register's genesis
control record — i.e. you must be signing (or have signed for) that register's owning org. The
endpoint requires the `CanManageRegisters` policy and refuses outright to delete a **system**
register (`src/Services/Sorcha.Register.Service/Program.cs`, `MapDelete("/{id}", …)`).

**Do NOT delete:**
- the system register `aebf26362e079087571ac0932d4db973` — the endpoint refuses this anyway, but
  don't attempt it;
- the AIAS org/identity/cyber registers currently provisioned by `run-demo.ps1` — check
  `demos/AIAS/state.json` for the live IDs before running any sweep, since a fresh mint gives them
  new IDs and an old ID from a note or issue is not safe to assume is still the demo's.

Get the exact IDs to delete from issue #1403 at sweep time (T085/T086/T048/T049/T057/T062/T063 —
some may not exist yet, per the issue's own "and whatever T062/T063 create" wording), confirm each
is NOT one of the two exclusions above, then `DELETE` each individually as the attesting org.

## 5. Verification

The overall pass signal for this runbook is **`rehearse.ps1` exiting 0 / printing "AIAS rehearsal
PASSED"** (§3.7). Record the run in the proof-of-execution table at the top of this document —
date (UTC), operator, the genesis fingerprint that was live, the rehearsal result, and any notes.

Supporting health checks (n1-deploy skill):

```bash
C="docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml"
ssh sorcha@51.105.7.135 "cd /opt/sorcha && $C ps --format '{{.Name}} {{.Status}}'"
ssh sorcha@51.105.7.135 'df -h / && docker system df'          # disk headroom (29GB total on n1 — prune before pulling if tight)
curl -s -o /dev/null -w '%{http_code}\n' https://n1.sorcha.dev/api/health   # expect 200
```

Cross-node check: `tiny`'s SSR docket count should be > 0 and its docket 0 should match n1's
(same `nTx`, same signature) — confirm with the same mongosh query as §3.4, run against `tiny`'s
`sorcha-mongodb` container over `ssh tiny`.

## 6. Cadence

**Recommendation: on-demand only, for now.** Run this runbook reactively — after observed abuse, or
when n1 needs a `down -v` for another operational reason. n1's current traffic does not justify a
standing schedule, and every re-genesis is disruptive (new IDs, AIAS demo gone until
re-provisioned, `tiny` coordination). Revisit this once n1 carries real external traffic (per the
Public Gates plan's WS-FINAL "public invitation" gate) — at that point a **weekly** cadence is a
reasonable starting point, run either:

- from the `prodexec` orchestrator box (`tiny` — see the `prodexec` skill / MEMORY.md
  `prodexec-project` topic) as a scheduled job, or
- as a scheduled GitHub Actions workflow that SSHes into n1 and `tiny` and drives this same
  sequence (the `schedule`/cron-agent tooling already used elsewhere in this environment could host
  it).

Either option should be wired to **post the `rehearse.ps1` result somewhere visible** (a GitHub
issue comment, a Discussions post, or a status page) rather than run silently — a scheduled reset
that starts silently failing is worse than no schedule at all.

## Cross-references

- **`network-bootstrap` skill** — the full genesis ceremony, embedding + Docker Publish path (only
  needed if NOT using the GenesisFile-mount fast path in §3.3 Option A), validator-key-import
  ordering rationale, `VAL_TIME_002` / `GenesisMaxAge` background, DevMode policy.
- **`n1-deploy` skill** — n1 infrastructure facts, the ⭐ preferred re-genesis path
  (`n1-regenesis-fixed.sh`), the newer `n1-recover.sh` compiled-in-genesis path, all deploy traps
  referenced above, health-check and log-inspection recipes.
- **`demo-deploy` skill** — `run-demo.ps1` / `rehearse.ps1` usage, agent freshness requirement,
  rehearsal gotchas.
- Issue **#1403** (T069 cleanup sweep), Public Gates readiness plan Task 5
  (`docs/superpowers/plans/2026-08-13-public-gates-readiness.md`).

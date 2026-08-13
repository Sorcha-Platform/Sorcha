# Network Bootstrap Troubleshooting

Common failures encountered during genesis ceremony and network bootstrap, with root causes and fixes.

## Wallet Service: PendingModelChangesWarning

**Symptom:** Wallet service crashes on startup with `PendingModelChangesWarning` — `MigrateAsync()` refuses to run.

**Root Cause:** An entity property was added to `WalletDbContext` without creating a corresponding EF migration. Since .NET 9, EF Core treats this as an error (not a warning) on fresh databases.

**Fix:**
1. Add missing property config to `WalletDbContext.ConfigureWallet()`
2. Delete all existing migrations in `src/Core/Sorcha.Wallet.Core/Migrations/`
3. Regenerate a single squashed migration:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Core/Sorcha.Wallet.Core \
  --startup-project src/Services/Sorcha.Wallet.Service \
  --output-dir Migrations
```

**Pre-production rule:** All EF migrations should be squashed into a single `InitialCreate` before first production deployment.

**Verify all services are clean:**

```bash
dotnet ef migrations has-pending-model-changes --project src/Services/Sorcha.Tenant.Service
dotnet ef migrations has-pending-model-changes --project src/Services/Sorcha.Blueprint.Service
dotnet ef migrations has-pending-model-changes --project src/Services/Sorcha.Peer.Service
dotnet ef migrations has-pending-model-changes --project src/Core/Sorcha.Wallet.Core \
  --startup-project src/Services/Sorcha.Wallet.Service
```

---

## Validator Service: Base64 FormatException

**Symptom:** `System.FormatException: The input is not a valid Base-64 string` when the validator processes the genesis transaction.

**Root Cause:** The genesis ceremony uses `Convert.ToBase64String()` (standard Base64 with `+/=`), but the validator endpoint uses `Base64Url.DecodeFromChars()` which only accepts URL-safe encoding (`-_`).

**Fix:** In `ValidationEndpoints.cs`, replace direct `Base64Url.DecodeFromChars()` calls with a helper that tries Base64URL first, then falls back to standard Base64:

```csharp
private static byte[] DecodeBase64(string value)
{
    try { return Base64Url.DecodeFromChars(value); }
    catch (FormatException) { return Convert.FromBase64String(value); }
}
```

**Location:** `src/Services/Sorcha.Validator.Service/Endpoints/ValidationEndpoints.cs`

---

## Genesis Docket Write: Register Not Found (404)

**Symptom:** Validator seals the genesis docket but Register Service returns 404 when writing it. Logs show: `Failed to write docket 0 to Register Service for register aebf2636...`

**Root Cause:** The WriteDocket endpoint at `POST /api/registers/{registerId}/dockets` checks if the register exists first (`GetRegisterAsync`). For the genesis docket (docket 0), the register doesn't exist yet — the register IS created by the genesis.

**Fix:** In the WriteDocket endpoint in `Program.cs`, auto-create the system register when writing docket 0 for the well-known system register ID:

```csharp
if (register == null)
{
    if (request.DocketNumber == 0 &&
        registerId == SystemRegisterConstants.SystemRegisterId)
    {
        logger.LogInformation("Auto-creating system register for genesis docket");
        register = await registerManager.CreateRegisterAsync(
            SystemRegisterConstants.SystemRegisterName,
            advertise: false, isFullReplica: true, registerId: registerId,
            description: "Sorcha platform system register",
            purpose: RegisterPurpose.System);
    }
    else
    {
        return Results.NotFound(new { error = "Register not found" });
    }
}
```

**DI note:** `RegisterManager` is scoped (MongoDB deps). Inject it as an endpoint parameter, NOT via `app.Services.GetRequiredService<>()` (root provider).

---

## SSH to n1: Permission Denied

**Symptom:** `Permission denied (publickey)` when SSH-ing to `sorcha@51.105.7.135`.

**Workaround:** Use `az vm run-command invoke` for all remote operations:

```bash
az vm run-command invoke --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --command-id RunShellScript --scripts "<commands>"
```

**To fix SSH:** Upload the public key via Azure CLI:

```bash
az vm user update --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --username sorcha --ssh-key-value "$(cat ~/.ssh/id_rsa.pub)"
```

---

## n1-reset.ps1: Wrong Resource Group

**Symptom:** `n1-reset.ps1` fails to find the VM.

**Root Cause:** Script defaults `$ResourceGroup` to `sorcha-n1`, but the actual RG is `sorcha-n1-uk`.

**Fix:** Always pass `-ResourceGroup sorcha-n1-uk`:

```powershell
.\scripts\n1-reset.ps1 -ResourceGroup sorcha-n1-uk -UpdateCompose -Yes
```

---

## CLI auth login: Console.Read Error

**Symptom:** `sorcha auth login` fails with `Cannot read keys when either application does not have a console`.

**Root Cause:** The CLI prompts interactively even when credentials are provided via flags. Fixed for
the default case (`--interactive` now defaults to `false`, so `--username`/`--password` alone no
longer force a prompt); if it still recurs, first try the current CLI's user-login path directly:

```bash
sorcha auth login --username <email> --password <password> --profile n1
```

(issue #1402 fixed this to POST the JSON `/api/auth/login` endpoint and no longer needs the
service-principal token API at all — the workaround below is for the SERVICE-PRINCIPAL case only,
not user login.)

**Service-principal workaround:** as of #1397/#1406, `client_credentials` minting is an
**internal-only** Tenant Service route (`POST /api/internal/service-auth/token`) — the public API
Gateway does not route `/api/internal/*`, so a curl from outside n1 (even against
`https://n1.sorcha.dev`) will 404. Run it from inside the trust network instead, e.g. via SSH:

```bash
ssh sorcha@n1.sorcha.dev "docker compose exec -T tenant-service curl -s -X POST \
  http://localhost:8080/api/internal/service-auth/token \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'grant_type=client_credentials&client_id=<ID>&client_secret=<SECRET>'"
```

---

## Windows curl: SSL Revocation Check Failure

**Symptom:** `curl: (35) schannel: CRYPT_E_NO_REVOCATION_CHECK`

**Fix:** Add `--ssl-no-revoke` to all curl commands targeting n1.sorcha.dev (Let's Encrypt certs on Windows).

---

## Bootstrap: Subdomain 'dev' is Reserved

**Symptom:** `sorcha bootstrap` returns 500 with `Subdomain 'dev' is reserved`.

**Fix:** Use a different subdomain like `sorcha-dev` instead of `dev`.

---

## Genesis Bootstrap Flow Summary

Understanding the flow prevents debugging the wrong component:

```
1. Register Service starts → SystemRegisterBootstrapper runs
2. Check local: system register exists? → No (fresh DB)
3. Load embedded genesis → verify signature → submit to Validator Service
4. Validator accepts → builds docket 0 → signs with docket-signing key
5. Validator writes docket 0 to Register Service → auto-creates system register
6. Register Service updates height → seeds default blueprints
7. Bootstrap complete
```

**If step 4 fails:** Validator key not imported. Import via Wallet Service API.
**If step 5 fails:** Check Register Service for auto-create errors (DI scoping, missing register).
**If step 6 fails:** Check blueprint seeding errors — usually indicates missing Wallet Service system wallet.

---

## System Register: Genesis ingested but docket never seals

**Symptom:** Register Service logs show `Genesis transaction accepted by Validator Service: TxId=…` followed by `Timed out waiting for genesis docket on system register after 30s`. The `registers` collection in MongoDB never gets a row for `aebf26362e079087571ac0932d4db973` at Height>0.

**Two distinct root causes — diagnose first:**

### Cause A — Wrong validator wallet (most common)

`validator-service` started before the genesis validator key was imported,
auto-generated a fresh wallet via `CreateOrRetrieveSystemWalletAsync`, and
that wallet's `m/44'/0'/0'/0/102` pubkey is not in the embedded genesis's
roster. Validator can't seal because the roster check fails.

**How to confirm:**
```sql
-- Postgres (sorcha_wallet)
SELECT "Address" FROM wallet."Wallets"
WHERE "Owner"='validator:local-validator';
```

If the address starts with anything other than what the imported wallet
returned (the `/system/recover` response), this is your cause.

**Fix:**
1. Stop register-service + validator-service (so nothing else writes to the wallet store)
2. Delete the wrong wallet:
   ```sql
   DELETE FROM wallet."WalletAddresses"
   WHERE "ParentWalletAddress" IN
     (SELECT "Address" FROM wallet."Wallets" WHERE "Owner"='validator:local-validator');
   DELETE FROM wallet."Wallets"
   WHERE "Owner"='validator:local-validator';
   ```
3. Call `POST /api/v1/wallets/system/recover` with the genesis mnemonic
4. Restart register-service + validator-service
5. **If the genesis tx-id is now `MEMPOOL_FULL/duplicate`**, the validator's
   unverified pool has the previous attempt cached. The cleanest fix is
   `docker compose down -v` and start over from a clean state — see
   network-bootstrap SKILL.md "Step 5: Import Validator Key" for the
   correct ordering (wallet-service alone → import → bring up rest).

### Cause B — Validator not enrolled for the system register

Validator-service maintains an in-memory `IRegisterMonitoringRegistry`
populated at startup + on Redis `register:relationship-changed` events +
every 5 minutes. The system register is special — it doesn't exist in
MongoDB until the docket seals, and the docket can't seal until validator
is enrolled. Chicken-and-egg.

**How to confirm:**
- Validator log shows `Validating transaction … for register aebf263…`
  (transactions arrive)
- But no `Registering validator local-validator for register aebf263…`
  log line for the system register
- 5-minute periodic poll doesn't pick it up either

**Status:** Open architectural question tracked in #461 phase 4.
Workaround: post-PR-#465, the genesis ingest now succeeds end-to-end on
fresh nodes when the import order is correct (wallet-service alone →
import → bring up rest). If the wallet is right but the docket still
doesn't seal, this is the gap to escalate.

## Empty genesis docket 0 → SyncOnly replica rejects the system register

**Symptom:** A SyncOnly replica (e.g. local Phaethon pointed at n1) never finishes
`SystemRegisterBootstrapper` — it stays in "System register not found — waiting for
peer sync". The replica's peer-service logs show it DID pull n1's genesis docket but
rejected it:

```
SystemRegisterSyncVerifier: System register genesis docket has no resolvable control
  transaction in cache (register aebf2636…, docket 0, transactionIds=0) — rejecting
DocketFinalizationService: genesis docket rejected: genesis signature does not match
  trusted public key
ValidatorKeyCache: Genesis docket … does not contain ProposerSignature
```

The forward-pull transport is fine (n1 logs `POST /api/registers/bulk-advertise … 200`
from the replica; the repeating gRPC `Unimplemented` is only the reverse-stream through
Caddy, which is non-fatal). The block is the docket CONTENT.

**Root cause:** On the OWNER node, the genesis control transaction landed in **docket 1**
and **docket 0 is empty** (`TransactionIds=[]`). The SyncOnly verifier reads docket 0,
finds no transactions, can't extract the genesis control record (the trust anchor), and
rejects — the "signature does not match" line is a downstream consequence of there being
no control tx to verify.

This happens only on the **system register's Auto-mode ingest** path. `DocketBuilder.
BuildDocketAsync` sealed a genesis docket from whatever was in the verified queue at claim
time, with **no empty-guard on the genesis path** (the normal-docket path guards emptiness
via `AllowEmptyDockets`). When a docket-build trigger fired during bootstrap before the
genesis control tx had been verified+queued, it sealed an empty docket 0 and forced the
genesis tx into docket 1. The `RegisterCreationOrchestrator` path (normal registers like
AssuredIdentity) is unaffected because it submits the creation txs **before** building, so
their docket 0 is correct.

**Fix (shipped):** `DocketBuilder.BuildDocketAsync` now **defers (returns null) when
`needsGenesis` is true but zero transactions are claimed** — docket 0 is created only once
the genesis control tx is queued; a later build seals it correctly. See
`src/Services/Sorcha.Validator.Service/Services/DocketBuilder.cs` (genesis path) and
`DocketBuilderTests.BuildDocketAsync_NeedsGenesisButNoTransactions_ReturnsNullWithoutSealingEmptyGenesis`.

**Recovery on an already-bad node:** an empty docket 0 is sealed and immutable — you cannot
patch it. Re-bootstrap the owner (`down -v`) with the fixed validator image. Because the
genesis is embedded and time-boxed to 1h, this means a **fresh genesis ceremony + Docker
Publish + reset inside the window**. After the reset, verify docket 0 has the genesis tx
(see SKILL.md Step 7 mongosh check) before reseeding replicas.

## Genesis validator-key import returns HTTP 000 but actually succeeded

**Symptom:** The wallet-alone genesis-key import (`n1-setup-remote.sh` or the manual one-shot
curl to `/api/v1/wallets/system/recover`) reports `HTTP 000` and the script aborts. Retrying
the same POST then returns **409 Conflict** ("System wallet … already exists").

**Root cause:** `HTTP 000` means the curl client got no response, **not** that the server
rejected the request. wallet-service often processes the recover and creates the wallet, but
the response is lost (connection/timeout on the stdin-piped one-shot curl, e.g. right after
the health gate). wallet-service running ALONE cannot auto-generate a `validator:<id>` wallet
(only register/validator services do, and they aren't up yet) — so a subsequent 409 proves the
**first** POST succeeded server-side with the correct mnemonic.

**Fix:** Treat a 409-after-000 as success. Do NOT `down -v` and retry — that wastes the genesis
window. Just bring up the rest of the stack (`docker compose … up -d`) and verify the system
register seals. (A 409 with NO prior import attempt is the real "validator raced ahead and
auto-generated the wrong wallet" case — see "Genesis ingested but docket never seals".)

## Validator stops sealing after long idle — healthy but silent (#814)

**Symptom:** After a node sits idle for a long time (hours→days), the validator **stops persisting
sealed dockets**. New submissions "succeed" at the API (register id / tx id returned) but Mongo shows
`dockets=0 tx=0` for the register, and workflow action-execute calls time out at ~60s
(`TimeoutException … not confirmed within 60s`). The validator container is **`running` / `healthy`,
`RestartCount=0`** — nothing looks wrong. `docker restart sorcha-validator-service` fixes it
immediately (sealing returns to ~0.1s).

**Root cause:** a keep-alive connection (Redis or the wallet-service gRPC channel) goes stale after
idle; an `await` in `ValidationEngineService.ProcessRegisterAsync` hangs, its in-memory "already
processing" flag is never released, and every later batch skips that register forever. Restart clears
the in-memory flag + runs the startup pool drain, which is why it recovers. Full analysis:
[[814-validator-idle-stall]].

**Diagnose:** confirm the register DB exists but is empty
(`db.getSiblingDB("sorcha_register_<id>").dockets.countDocuments({})` → 0) while the Redis monitoring
set still lists it (`docker exec sorcha-redis redis-cli SMEMBERS validator:monitoring:registers`) — a
still-monitored-but-not-processed register is the signature. NB the validator logs to **OTLP/Aspire,
not `docker logs`** on the docker stacks, so `docker logs sorcha-validator-service` is empty — use the
Mongo/Redis state, not logs.

**Immediate fix:** `docker restart sorcha-validator-service`.
**Permanent fix (#814):** cycle timeouts + self-healing slot reclaim + Redis keep-alive/reconnect.
Watch `sorcha_validator_validation_cycle_timeout_total` and
`sorcha_validator_validation_slot_reclaimed_total` (a non-zero reclaim counter means the timeout guard
was bypassed and needs a look). **Reproducing on demand is impractical** — it needed a ~16-day idle;
65 min is not enough (onset depends on when a socket actually dies).

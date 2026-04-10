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

**Root Cause:** The CLI prompts interactively even when credentials are provided via flags.

**Workaround:** Use the service principal token API directly:

```bash
curl -s --ssl-no-revoke -X POST "https://n1.sorcha.dev/api/service-auth/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=<ID>&client_secret=<SECRET>"
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

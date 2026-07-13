# Quickstart: AIAS decision integrity & visibility

Build → test → deploy → verify runbook for feature 183.

## Prerequisites

- .NET 10 SDK, Docker Desktop.
- n1 access: SSH `sorcha@51.105.7.135` (Bash tool; box is BST/UTC+1), admin `admin@sorcha.local` / `Dev_Pass_2025!`, public `https://n1.sorcha.dev`, internal gateway on the VM `http://localhost:8880`.
- The Assure-ID agent runs as a LOCAL child process polling n1 and MUST be F176-current (force-reinstall the global tool from a fresh master pack if rebuilt).

## Build & unit test

```bash
dotnet build

# US1 — claim-source seeder (the tight regression on the client bug)
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --filter "FullyQualifiedName~ClaimSourceSeeder"

# US2 — decision-notice write + routing hook
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj --filter "FullyQualifiedName~InboxWriter|FullyQualifiedName~DecisionNotice"
```

(`dotnet build` before tests — stale DLLs cause phantom fails. `dotnet test` takes ONE project. MTP ignores `--filter` on some runners; use the per-project invocation.)

## Rehearsal (end-to-end regression, API-level)

```powershell
./demos/AIAS/rehearse.ps1 -Target docker   # or -Target n1
```

Post-change rehearse asserts: verified applicant → approved + credential; bad postcode → rejected; **unverified applicant (no emailVerified) → rejected + no credential**. It no longer hard-codes `emailVerified`.

## Deploy to n1 (code-only; no `down -v`, no re-genesis)

Two images change (web client + blueprint service) plus a blueprint re-provision:

1. Build + publish `sorcha-ui-web` and `sorcha-blueprint` images (Docker Publish to `:latest`, or `docker save`/`scp`/`load` the two changed services).
2. On n1: pull, then `docker compose -f docker-compose.yml -f <n1> -f <smtp> -f <ports> up -d --force-recreate --no-deps <ui-web> <blueprint>`. **Keep `-f docker-compose.smtp.yml`** (ACS email) in the standing `up`.
3. **Re-provision the AIAS blueprint** so the live schema carries `x-claim-source` + `x-decision-notice` (the seeder/notice only fire off the provisioned blueprint). Re-run the AIAS provisioning; update `demos/AIAS/state.json` + `assure-id.config.json` with the new blueprint id; restart the local agent.

## Live verification (Chrome DevTools MCP against n1)

**Happy path (SC-001/002)**:
1. Sign up a fresh citizen on `https://n1.sorcha.dev/app`; verify email (ACS email, or admin-confirm / demo `Confirm-SorchaUserEmail`).
2. Submit the AIAS Assured Identity application with a real UK postcode (e.g. `EH9 1JA`) + a photo.
3. Capture the action-1 submission network request; confirm **`emailVerified: true` on the wire**.
4. Confirm the agent **approves** and the `AssuredIdentityCredential` is delivered to the wallet.

**Reject visibility (SC-004)**:
1. Cause a gate rejection (unverified email, or a bad postcode).
2. Confirm a **durable bell/inbox entry** appears for the applicant carrying the on-brand reason.
3. Reload the app and re-login (optionally another device) — the entry and reason persist.

**Fault-safety (SC-005)**: covered by the `ActionExecutionService` write-throw unit test (a notification-write failure does not fail the decision).

## Payload decode (DevMode register, if needed)

n1 AIAS register is DevMode: transactions live in Mongo `sorcha_register_<registerId>`; `payloads[0].Data` is BSON Binary — extract via `EJSON.stringify` then base64-decode + JSON to confirm `emailVerified` on the wire.

## Rollback

Code-only: redeploy the prior `:latest` digests for the two services and re-point the agent/state at the prior blueprint id. No data reset needed (no migration).

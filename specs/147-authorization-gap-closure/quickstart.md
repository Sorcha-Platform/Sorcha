# Quickstart: verifying Authorization-gap closure

This feature changes authorization configuration only. Verification is by automated tests (no manual service run required), plus an optional manual probe.

## Run the tests (per affected service — MTP ignores `--filter`, runs the whole project)

```powershell
dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
dotnet test tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj
```

Expected: the new authorization tests pass, asserting the [authorization matrix](./contracts/authorization-matrix.md):

- **Wallet** — `CanRecoverSystemWallet` allows Service and Platform-Admin, denies Anon/Consumer/Platform-without-admin/admin-with-consumer-audience; system-wallet create requires Service; system-wallet create+recover endpoints carry no `AllowAnonymous`; pending-applications requires consumer audience.
- **Blueprint** — `CanManageBlueprints` denies Consumer (even with `org_id`), allows Platform-with-org and Service, denies Platform-without-org.
- **Tenant** — `RequireSystemAdmin` denies a SystemAdmin in a non-system org, allows a system-admin-org SystemAdmin.

## What "done" looks like (maps to Success Criteria)

- **SC-001 / SC-002**: the matrix tests above are green (unauthorized denied, legitimate allowed).
- **SC-003**: the `CanManageBlueprints` matrix test exercises the policy directly, so it covers every bare authoring endpoint through the single central change.
- **SC-004**: the Tenant org-scoping test is green.
- **SC-005**: the endpoint-metadata test asserts the system-wallet endpoints have no `AllowAnonymous` and require their policy.
- **SC-006**: the three project suites pass.

## Optional manual probe (against a running stack)

With a citizen (consumer-tier) token `$CT` and an admin (platform-tier) token `$PT`:

```bash
# H2: consumer must be refused at blueprint authoring (was allowed)
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $CT" http://localhost/api/blueprints      # expect 403

# F124: platform token must be refused at a citizen surface
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $PT" http://localhost/api/v1/wallet/pending-applications  # expect 403

# H1: anonymous must be refused at system-wallet create (was 2xx/anonymous)
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost/api/v1/wallets/system -d '{}'   # expect 401
```

## Caller flows that MUST keep working (no regression)

- Validator Service seating its system wallet on startup (`SystemWalletInitializer` → service token → create).
- `sorcha system-register import-validator-key` during the genesis ceremony (admin platform token → recover).
- Platform-tier admins/designers authoring blueprints and schemas.
- Citizens reading/setting their own pending-application notice.

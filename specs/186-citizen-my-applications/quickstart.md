# Quickstart: Citizen "My Applications" View

**Feature**: 186 | **Branch**: `186-citizen-my-applications`

## Read these first

1. [research.md](./research.md) — three findings change what gets built. R2 and R3 in particular: `IsRejection` is dead code, and a refusal is a route rather than a state.
2. [data-model.md](./data-model.md) §5 — outcome derivation, the table the read path implements.
3. [contracts/me-applications.md](./contracts/me-applications.md) — the wire shape.

## Build and test

```bash
dotnet build                                   # whole solution, not just touched projects

dotnet test tests/Sorcha.Blueprint.Service.Tests
dotnet test tests/Sorcha.UI.Core.Tests
```

`dotnet test` takes one project at a time. To filter within a project (Microsoft.Testing.Platform):

```bash
dotnet test tests/Sorcha.Blueprint.Service.Tests -- --filter-class "*InstanceProjection*"
```

`Sorcha.UI.E2E.Tests` is the exception — NUnit/VSTest, so it takes `--filter` directly and needs Docker up:

```bash
docker-compose up -d
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=MyApplications"
```

## Manual verification

```
http://localhost/app/my-applications          list
http://localhost/app/my-applications/{id}     detail
http://localhost/app/my-workflows             must land on the list, not /new-submissions
```

Sign in as a citizen who has submitted at least one application. `admin@sorcha.local` is a platform admin and is a poor test subject for this page.

## Traps

- **The list is empty and you assume a UI bug.** Check `GET /api/me/applications` directly first. Two separate defects have already produced an empty citizen list here: the `wallet_address` claim consumer tokens omit (fixed in #1355), and the client/server shape mismatch of research R1.
- **A refused application shows as "Completed".** That is the R3 finding, not a regression — it is what happens when outcome derivation is skipped. Check `decisionRouteId` reached the instance.
- **A new `Instance` field silently vanishes.** `EfCoreInstanceStore.UpdateAsync` copies model to entity by hand. The whole-model round-trip test catches it; if you add a field, do not skip that test.
- **A projection test that passes without touching production code.** Building a `ProjectedTransaction` by hand proves the fold, not the join. Test through `InstanceProjectionResolver.ResolveAsync` with a real `RoutingDecision` — that is precisely the gap that let R2 survive.
- **`ResolveMessage` returns `""`, not null**, when a route declares no fallback. Treat empty as "no reason" and omit the field.
- **PWA routes are base-relative.** Not touched in this pass, but do not "fix" a leading slash into any PWA navigation while passing through.

# Baseline — before Feature 195

Recorded 2026-08-24 on branch `195-blueprint-definition-identity` at commit `92d24d920`, so a later
count change is attributable rather than assumed (T001).

## Test counts

| Project | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| `Sorcha.Blueprint.Models.Tests` | 505 | 505 | 0 | 0 |
| `Sorcha.Blueprint.Engine.Tests` | 627 | 626 | 1 | 0 |
| `Sorcha.Blueprint.Service.Tests` | 1223 | 1218 | 5 | 0 |
| `Sorcha.Validator.Service.Tests` | 1097 | 1076 | 21 | 0 |
| `Sorcha.Register.Service.Tests` | 458 | 449 | 9 | 0 |
| **Total** | **3910** | **3874** | **36** | **0** |

All green. Skips are pre-existing (INFRA-gated integration tests).

## ⚠ `dotnet test` reports "Zero tests ran" — run the test EXE directly

Both documented invocations fail identically in this environment:

```
dotnet test tests/X/X.csproj              → Zero tests ran, error: 1, exit 5
dotnet test --project tests/X/X.csproj    → Zero tests ran, error: 1, exit 5
```

…while the same assembly run directly passes all 505:

```bash
dotnet build tests/X/X.csproj -v q --nologo
./tests/X/bin/Debug/net10.0/X.exe
```

**Use the exe for every run in this feature.** `dotnet test`'s exit 5 is indistinguishable from a real
failure at a glance, and its "0 failed" line reads as success — exactly the shape this feature exists
to eliminate elsewhere. The MTP-mode note in CLAUDE.md covers filter syntax but not this.

Filtering with the exe: `X.exe --filter-class "*Name*"` (same MTP filters, no `--`).

⚠ `dotnet build` accepts only **one** project per invocation (`MSB1008`); loop instead of listing.

## Migrations (T002)

`dotnet ef migrations has-pending-model-changes` — see the run recorded below. Checked because F194
lost a deploy to skipping it, and **the container reports HEALTHY when `MigrateAsync` fails**
(`Program.cs` catches and logs it), so health status proves nothing.

**Result (2026-08-24):** `No changes have been made to the model since the last migration.` — clean
starting point, so any pending-changes report later in this feature is mine.

Migration files for T026 (fold, do not add — CLAUDE.md §19):

- `src/Services/Sorcha.Blueprint.Service/Data/Migrations/20260528205017_InitialCreate.cs`
- `src/Services/Sorcha.Blueprint.Service/Data/Migrations/20260528205017_InitialCreate.Designer.cs`
- `src/Services/Sorcha.Blueprint.Service/Data/Migrations/BlueprintDbContextModelSnapshot.cs`

⚠ EF tools 10.0.5 vs runtime 10.0.11 — a warning, not an error, but worth knowing if a generated
artefact looks unexpected.

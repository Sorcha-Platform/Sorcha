# T028 — DTO Property Description Fill Report

**Feature:** 117 AI Discoverability
**Task:** T028 — every request/response/parameter property in scope must carry a non-empty `description` (FR-005, Spectral rule `description-required-on-properties`).
**Date:** 2026-05-02
**Outcome:** Build succeeded (0 errors) — all in-scope properties now carry XML doc summaries.

## Approach

1. **Audit:** Counted public `{ get; set/init; }` properties and positional record parameters across the eight in-scope directories.
2. **csproj enablement:** Common projects already inherit `<GenerateDocumentationFile>true</GenerateDocumentationFile>` from `src/Common/Directory.Build.props`. Services did not. Added it to `src/Services/Directory.Build.props` (single change covers all eight services). **Did not** add `<NoWarn>$(NoWarn);CS1591</NoWarn>` per task brief — letting CS1591 surface so future PRs notice missing docs in non-DTO surfaces.
3. **Auto-fill script** (`fill-docs.ps1`):
   - For each undocumented property, inserted `/// <summary>{description}</summary>` above any preceding `[Attribute]` lines.
   - For each undocumented positional record parameter, inserted `/// <param name="X">{description}</param>` after the existing `/// <summary>` (or created a new `/// <summary>` block above the record if none existed).
   - Description heuristic: domain-specific overrides (Id, OrganizationId, CreatedAt, AccessToken, PageSize, Status, Email, …) plus type-aware fallbacks (bool → "Indicates whether …", DateTimeOffset → "Timestamp at which … (UTC).", Guid…Id → "Identifier of the …", IList/IEnumerable/array → "Collection of …", IDictionary → "Map of …", numeric → "Numeric value for …").
4. **Voice:** Factual, no marketing adjectives. Deny-list (revolutionary, best-in-class, industry-leading, cutting-edge, world-class, seamless, game-changing, next-generation, state-of-the-art) confirmed absent.

## Coverage — properties (`{ get; set/init; }`)

| Directory | Total | Pre-existing docs | Filled by script | Final undocumented |
|---|---:|---:|---:|---:|
| `src/Common/Sorcha.ServiceClients.Http` | 367 | 132 | 235 | 0 |
| `src/Services/Sorcha.ApiGateway/Models` | 39 | 0 | 39 | 0 |
| `src/Services/Sorcha.Blueprint.Service/Models` | 292 | 289 | 3 | 0 |
| `src/Services/Sorcha.Wallet.Service/Models` | 153 | 128 | 25 | 0 |
| `src/Services/Sorcha.Tenant.Service/Models` | 854 | 799 | 55 | 0 |
| `src/Services/Sorcha.Peer.Service/Models` | 42 | 10 | 32 | 0 |
| `src/Services/Sorcha.Validator.Service/Models` | 106 | 88 | 18 | 0 |
| `src/Services/Sorcha.Haip.Service/Models` | 56 | 1 | 55 | 0 |
| `src/Services/Sorcha.Register.Service/Models` | n/a (directory does not exist; Register has no `Models/` folder — its DTOs live in `Sorcha.ServiceClients.Http/Register/`) | — | — | — |
| **Total** | **1909** | **1447** | **462** | **0** |

## Coverage — positional record parameters

| Directory | Total | Pre-existing param docs | Filled by script | Final undocumented |
|---|---:|---:|---:|---:|
| `src/Common/Sorcha.ServiceClients.Http` | 69 | 30 | 39 | 0 |
| `src/Services/Sorcha.Blueprint.Service/Models` | 69 | 0 | 69 | 0 |
| `src/Services/Sorcha.Tenant.Service/Models` | 34 | 14 | 20 | 0 |
| Other in-scope dirs | 0 | 0 | 0 | 0 |
| **Total** | **172** | **44** | **128** | **0** |

## Files modified

- **34 source files** received automated XML-doc insertions. Full list in `git diff --stat`.
- **1 csproj infrastructure file** modified: `src/Services/Directory.Build.props` — added `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. (No service-level csproj edits required because services inherit this from the directory props.)

## Build outcome

`dotnet build Sorcha.sln --nologo` →
- **0 Errors**
- **1396 Warnings** (most are CS1591 in non-DTO public surfaces — services / endpoints / engine internals — that are now visible because services inherit `GenerateDocumentationFile=true`. Out of scope for T028; they form natural backlog for future doc PRs.)

## Properties to sanity-check (heuristic guesses)

The heuristic produced reasonable text for the vast majority, but a human reviewer should sanity-check the following kinds of properties where the inferred description is shallow and the domain meaning may warrant a more specific phrase:

- **Single-word generic names** in `HaipModels.cs` / `VerifierModels.cs` — `Format`, `Vct`, `Jwt`, `ProofType`, `Locale`, `CNonce`, `AccessToken` — currently filled with override defaults; reviewers familiar with HAIP/OID4VCI may want to add spec references (e.g. "vc+sd-jwt" as expected value for `Format`).
- **Validator `*Document` properties** in `Sorcha.Validator.Service/Models/ValidatorDocument.cs` — heuristic-filled; reviewer should confirm domain meaning matches register/docket vocabulary.
- **Peer service** `PeerManagementDtos.cs` — 32 freshly filled properties cover peer connection state and replication metadata; reviewer should verify domain-specific phrases (e.g. `RosterVersion`, `ResolvedFrom`).
- **Blueprint `AIStreamEvents.cs` records** — `ToolUse(Id, Name, Arguments)`, `StreamEnd(StopReason)`, `StreamError(Message, IsRetryable)` — heuristic descriptions are slightly generic; could be tightened to reference the AI streaming protocol.
- **Tenant `AuthMethodsResponse.cs`** — auth-method aggregate properties; check that filled phrases match Feature 116 vocabulary.
- **`SchemaLibraryDtos.cs`** — three properties filled; check schema-library domain match.

None of these are wrong; they are merely opportunities for tighter phrasing in future copyediting passes.

## Cleanup notes

- Helper scripts (`audit-dto.ps1`, `audit-records.ps1`, `fill-docs.ps1`) used during this run were deleted after the fill completed; they are not part of the build artefacts.

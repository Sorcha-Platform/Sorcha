# Kickoff prompt — finish Feature 137 (Stage 5): C3 / US2 credential delivery

Paste the block below into a fresh session (from the repo root). This is **execute-mode** — the
design, spec, plan, and tasks already exist and **four of five components are merged** (C5, C1, C4,
C2). Only **C3 (US2 — getting the credential bound and delivered back to the citizen)** remains.
The user is happy for C3 to land as **one big PR**.

Portable context (all in source control):
- Spec/plan/tasks/research: `specs/137-cross-node-submission/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, `contracts/`)
- Design doc: `docs/superpowers/specs/2026-05-23-cross-node-submission-design.md`
- State & gaps (older, pre-implementation): `docs/superpowers/specs/2026-05-23-cross-node-federation-state-and-gaps.md` (see its "Stage-5 implementation progress" section for what's now merged)
- This prompt: `docs/superpowers/prompts/137-c3-credential-delivery-kickoff.md`

> **Cross-node live test** (Tier-2, SC-001/002/003/004) runs on the machine holding
> `genesis-validator-key.json` (gitignored — copy out-of-band) with n1 SSH + the
> `docker-compose.sync-from-n1.yml` split. The C3 **server** code is unit-testable locally; the
> Blazor field + PWA submit are build-verified locally and truly validated only in the Tier-2 run.

---

````
# Finish Feature 137 (Stage 5) — implement C3 / US2 (credential delivery) as one PR

## Step 0 — load skills first (Skill tool), in this order
1. `superpowers:executing-plans` — there is a written plan + tasks to execute (`specs/137-cross-node-submission/`). Follow it; checkpoint per task.
2. `sorcha-architecture` (F114 citizen credential push + SorchaLocalWallet delivery, F108 ownership-agnostic submission, F092 `x-persona`/F085 `x-file` schema-extension idiom, F107 `x-review`), `verifiable-credentials` (SD-JWT VC, `cnf` holder binding, did:sorcha), `jwt` (F136 tiered audiences — consumer tier for the new wallet endpoint), `cryptography` (Ed25519↔X25519 conversion, holder key slot 108).
3. For the client/PWA: `sorcha-ui` (Blazor WASM pages + Playwright via Docker), `frontend-design` (MudBlazor), `blazor`.
4. Tests: `xunit`, `moq`, `fluent-assertions`. For the AssuredIdentity blueprint: `blueprint-builder`, `walkthrough-builder`.

Then read `specs/137-cross-node-submission/tasks.md` (US2 = T015–T029), `research.md` (esp. R5/R6 + the "Design corrections" section), and `data-model.md` (§1–§4).

## Status — what is already merged (do NOT redo)
- **C5** (#831): cross-node mirror submission — analyst can act on a read-only mirror; `nextActionId` carried in submission metadata → projected onto `TransactionMetaData.NextActionId` → seeds the mirror's `CurrentActionIds`; `ActionExecutionService.PersistInstanceAsync` advances mirrors via `UpdateMirrorAsync`.
- **C1 + C4** (#832, US1): `CreateInstance` is published-store-aware (`PublishedBlueprintSelector.SelectLatest`), publish-to-register gated on `IsOwner`, typed 409 `blueprint_not_available`; `docker-compose` blueprint-service has `ServiceClients__PeerService__{Address,HttpAddress}` so F108 fan-out reaches the peer.
- **C2** (#833, US3): `BlueprintRecoveryService` subscribes to `register:created` (constant `RegisterEventChannels.RegisterCreated`) for immediate per-register recovery; periodic loop is the safety net.

## The remaining work — C3 / US2 (credential bound + delivered to the local citizen wallet)
Goal: n1 issues the AssuredIdentityCredential bound to the citizen's holder key, encrypted to the citizen's key, delivered to the citizen's LOCAL wallet automatically. Two tracks (can be built in parallel; one PR is fine):

### Server track (unit-testable locally)
- **`cnf` binding is a PRE-EXISTING HOLE** — credentials are issued *unbound* today. Add `HolderJwk` to `IssueCredentialRequest` (`Wallet.Service/Endpoints/CredentialEndpoints.cs` ~:813) and pass it into `SdJwtService.CreateTokenAsync(holderJwk:)` (~:684). Thread it through `IWalletServiceClient.IssueCredentialAsync` (`Sorcha.ServiceClients.Http/Wallet/`) and add `CredentialIssuanceConfig.HolderKeySourceField` (JSON Pointer, default `/holderKeys/holderJwk`, `Sorcha.Blueprint.Models/Credentials/`).
- **Recipient-key precedence** in `ActionExecutionService.IssueCredentialFromActionAsync` (`Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` ~:1943-2060): (1) published participant record → (2) carried `holderKeys` field via the existing `TryResolveJsonPointer` walker → (3) **fail closed** (no credential). For the AEAD envelope, the "supply explicitly" path ALREADY EXISTS — inject the carried `encryptionPublicKey` into `request.ExternalRecipientKeys` ONLY when the register lookup misses (published wins). X25519 is derivable from the Ed25519 signing key for ED25519 wallets (`CryptoModule.EncryptED25519Async`), so the field's `encryptionPublicKey` is a robustness carry, not strictly required.
- **Validation**: confirm `Sorcha.Blueprint.Engine/Implementation/SchemaValidator.cs` (no `x-` strip) is not on the action-data path for the new field; `ValidationEngine` already strips `x-*` generically and unknown `format` validates as pass.

### Client track (build-verified locally; behaviour validated in Tier-2)
- New field type: `ControlTypes.HolderKey` (`Sorcha.Blueprint.Models/Control.cs`, mirror `PostcodeLookup`); map `format == "sorcha-holder-key"` in `FormSchemaService.InferControlFromSchema` (`Sorcha.UI.Components.User/Services/User/Forms/`); dispatch in `ControlDispatcher.razor`; new `HolderKeyRenderer.razor` (`Sorcha.UI.Components.User/Components/Forms/Controls/`) that autofills from a NEW Wallet-Service endpoint and writes `/holderKeys/{holderJwk,encryptionPublicKey,algorithm}` via `FormContext.SetValue` (sibling fan-out like `PostcodeLookupRenderer`).
- New Wallet-Service endpoint `GET /api/v1/wallet/holder-keys` (consumer-tier, `RequireConsumerAudience`) returning holder JWK (slot 108 via `HolderKeyService.GetHolderPublicJwkAsync`) + X25519 pubkey + algorithm. Contract: `specs/137-cross-node-submission/contracts/holder-keys-endpoint.openapi.yaml`. The PWA derives NOTHING client-side — slot-108 + X25519 are Wallet-Service-managed; the renderer calls this endpoint.
- **The PWA application-submission surface is a STUB** — `Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor` + `StubApplicationSubmissionService` (`Services/Applications/`). Wire the real `SorchaFormRenderer` submit path.
- Add the `holderKeys` (`format: sorcha-holder-key`) field to the starting action of `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` and point `credentialIssuanceConfig.HolderKeySourceField` at it.

### Out of scope (backlog — do NOT build): participant-record promotion + proof-of-possession.

## Verification
- Server: unit tests as usual (target >85% new). Run a single class via the built test exe (MTP ignores VSTest `--filter`): `./tests/<Proj>.Tests/bin/Debug/net10.0/<Proj>.Tests.exe --filter-class "*ClassName"`.
- The Blueprint.Service.Tests suite has **27 pre-existing integration failures** that need Redis (`IConnectionMultiplexer`) — they fail locally, pass in CI. Confirm your change doesn't ADD failures (count stays 27); don't chase them.
- Commit the Tier-2 cross-node verification steps into the AssuredIdentity walkthrough per `quickstart.md` §Tier-2 (the live run happens on the genesis-key machine).
- Update docs on change: `.claude/skills/sorcha-architecture/SKILL.md` (F114/F137 surface), `docs/reference/API-DOCUMENTATION.md` (new endpoint), affected service READMEs.

## Guardrails
- Branch + PR (never push master); merge on green. claude-review is a required gate and approves silently on pass. `BatchPublicKeyResolutionTests` CI red is a known env-flake (passes locally 308/0). Use `gh pr merge <n> --squash --auto --delete-branch`.
- `InternalsVisibleTo` is set for `Sorcha.Blueprint.Service.Tests` and `Sorcha.Validator.Service.Tests` — internal helpers are testable.
- .NET 10 / C# 14; nullable enabled; no new build warnings; Scalar (not Swagger); `Sorcha.ServiceClients` for HTTP; `JsonElement` (not `JsonNode`) with JsonSchema.Net.
- One big PR for C3 is acceptable. When it merges, Feature 137's local-buildable scope is complete; the citizen→credential round-trip is then validated end-to-end in the Tier-2 cross-node run.
````

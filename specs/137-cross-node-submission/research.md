# Research: Cross-node submission round-trip (Stage 5)

Phase 0 research for feature 137. Resolves the open questions flagged in the design doc
(`docs/superpowers/specs/2026-05-23-cross-node-submission-design.md`) against the live codebase.
All findings are read-only investigation; file:line references are to `master` as of 2026-05-23.

Two design assumptions were **corrected** by this research — see "Design corrections" at the end.

---

## R1 — Blueprint resolution on replicas (C1)

**Decision.** Make `CreateInstance` published-store-aware and gate the register publish on node-ownership.

- `CreateInstance` is `instancesGroup.MapPost("/", …)` at `Sorcha.Blueprint.Service/Program.cs:1864-1969`. It resolves the blueprint via `IBlueprintStore.GetAsync(request.BlueprintId)` (**:1873**, the draft store) and 400s "Blueprint not found" on null — **the wall**.
- It then uses only `blueprint.Actions` (starting-action ids → `CurrentActionIds`, :1917-1925), `blueprint.Participants` (pre-seed `ParticipantWallets`, :1932-1935), and `blueprint.Title` (:1954).
- **No model gap.** `IPublishedBlueprintStore` (`Program.cs:2443-2449`) returns `PublishedBlueprint` (`:3639-3646`) whose `.Blueprint` is the **same** `Sorcha.Blueprint.Models.Blueprint` the draft store returns. Every field `CreateInstance` reads is present. The published store has no `GetAsync(id)` — use `GetVersionsAsync(blueprintId)` and pick latest by `PublishedAt` (the recovery service already does this, `BlueprintRecoveryService.cs:175-177`).
- `PublishBlueprintToRegisterAsync` (`Program.cs:~1890`; `IRegisterServiceClient.cs:271-276`) POSTs a blueprint-publish transaction to the register. **Skip it when the node is not the owner.**

**Rationale.** Resolution: try draft, then published-latest. Publish-gate: a replica must never re-publish onto a register it does not own.

**Alternatives considered.** A dedicated `IBlueprintResolver` abstraction — deferred; a two-line draft→published fallback in `CreateInstance` is sufficient and lower-risk. Always-reconstruct-from-register (Approach C) — rejected in design.

## R2 — Owner-vs-replica relationship API (C1)

**Decision.** Use the already-injected `IRegisterServiceClient.GetLocalRelationshipAsync(registerId)`.

- `IRegisterServiceClient` is already a `CreateInstance` dependency (`Program.cs:1868`) — **no new wiring**.
- `GetLocalRelationshipAsync` (`IRegisterServiceClient.cs:496-498`; impl `RegisterServiceClient.cs:1684-1714`) → `GET /api/registers/{id}/local-relationship`, returns `RegisterLocalRelationship?` (null on 404). Booleans: `IsOwner`, `IsValidator`, `IsSubscriber`, etc. (`Sorcha.Register.Models/LocalRelationship/RegisterLocalRelationship.cs:18-46`).
- Underlying derivation `IRegisterLocalRelationshipService` (Register.Core) runs in Register.Service — Blueprint.Service consumes only the HTTP shape.

**Gate logic.** Skip publish unless `(await registerClient.GetLocalRelationshipAsync(id))?.IsOwner == true`. Treat null as "not owner → skip publish, do not hard-fail".

## R3 — Event-driven recovery channel (C2)

**Decision.** Subscribe `BlueprintRecoveryService` to the **`register:created`** Redis channel and recover that single register on the event; keep the periodic loop as the safety net.

- Today `BlueprintRecoveryService.ExecuteAsync` (`…/BlueprintRecoveryService.cs:35-63`) runs `RunRecoveryAsync` once at boot + a periodic refresh. No event subscription.
- `register:created` (payload `RegisterCreatedEvent{RegisterId,…}`, `Sorcha.Register.Core/Events/RegisterEvents.cs:12-18`) is published unconditionally inside `RegisterManager.CreateRegisterAsync` (`RegisterManager.cs:84-93`). **The PR #829 create-on-sync path goes through `CreateRegisterAsync`** (`Register.Service/Program.cs:1409-1448`), so a newly-replicated register emits it exactly once — the narrowest "a register just appeared locally" signal.
- Candidates rejected: `docket:confirmed` (per-docket, coarser; already consumed by `InstanceMirrorReconstructor`), `register:height-updated` (noisy), `register:relationship-changed` (governance only), `register:sync-state-changed` (indirect).

**Work.** (1) Add `RegisterEventChannels.RegisterCreated = "register:created"` and replace the two inline literals (`RegisterManager.cs:85`, `RegisterEventBridgeService.cs:35`). (2) Add an `IEventSubscriber`/`IConnectionMultiplexer` dependency to `BlueprintRecoveryService` (mirror `InstanceMirrorReconstructor.cs:91-111` or `PresentationSealSubscriber`; guard Redis-unavailable). (3) Expose a per-register recovery entrypoint (refactor the private `RecoverFromRegisterAsync` to open its own scope by `registerId`).

## R4 — Mirror-instance sufficiency for analyst submission (C5) — **the structural gap**

**Verdict.** As the code stands, the analyst **cannot** submit action 2 on n1 against the mirror. Two concrete blockers; both must be fixed.

**Blocker A (fatal) — read-only-mirror write guard.** `ActionExecutionService.ExecuteAsync` has no mirror-aware branch; on a successful submission it calls `UpdateInstanceAfterExecutionAsync` → `_instanceStore.UpdateAsync(instance)` (`ActionExecutionService.cs:1016, 1626`). Both stores throw on a mirror row: `EfCoreInstanceStore.cs:112-118` and `InMemoryInstanceStore.cs:55-59` (`if (entity.IsReadOnlyMirror) throw …`). Mirrors are created `IsReadOnlyMirror=true` (`InstanceMirrorReconstructor.cs:280`), set-once.

**Blocker B (conditional, hit first) — `CurrentActionIds` never seeded.** Submission validates `if (!instance.CurrentActionIds.Contains(actionId) && !actionDef.IsStartingAction) throw "not a current action"` (`ActionExecutionService.cs:208`). The mirror seeds `CurrentActionIds` from `tx.MetaData?.NextActionId` (`InstanceMirrorReconstructor.cs:264,273`), but the authoritative sealed-tx projection **never sets `NextActionId`** (`DocketBuildTriggerService.cs:593-608` writes only RegisterId/BlueprintId/ActionId/TransactionType/InstanceId). So `CurrentActionIds` falls back to `[]` and action 2 trips the check.

**Decision (root-cause fix — v1 LOCKED to 1a + 2a).**
1. **Fix 2a — make `ExecuteAsync` mirror-aware**: when the target instance is a mirror, advance it via `UpdateMirrorAsync` (register-driven) rather than the guarded `UpdateAsync` — the submission travels to the register normally; local mirror state is not authoritatively mutated by the submit path. (Touch `ActionExecutionService.cs:1016/1626`; the guard at `EfCoreInstanceStore.cs:112` / `InMemoryInstanceStore.cs:55`.) Rejected: 2b (relax the write guard) — erodes the F106 invariant.
2. **Fix 1a — seed the next action**: emit `NextActionId` in the authoritative projection (`DocketBuildTriggerService.cs:593-608`) so the mirror seeds `CurrentActionIds`. **Deferred (not v1):** Fix 1b — make the reconstructor blueprint-aware and derive the next action from routing, retiring the `:253-263` self-keyed-`ParticipantWallets` TODO. Per user direction, 1b stays out of v1.

**Confirmed-sound (design was right).** Register-sourced reconstruction works cross-node — `StateReconstructionService.ReconstructAsync` reads sealed txs via `GetTransactionsByInstanceIdAsync` (`StateReconstructionService.cs:73`). Shared `instanceId` travels in tx metadata end-to-end (`Program.cs:1940` → `TransactionBuilderService.cs:84` → `DocketBuildTriggerService.cs:604` → `InstanceMirrorReconstructor.cs:177`). "Local wallet" = `IWalletServiceClient.GetWalletAsync(addr)` non-null (`InstanceMirrorReconstructor.cs:214-215`).

**Rationale.** This is deterministic and structural, not an edge case — so it is a first-class component, not a "confirm". Keeping submissions register-routed preserves the F106 invariant (mirrors are not locally authoritative) while letting the owner node act.

## R5 — X25519 derivability + recipient-key resolution at issuance (C3)

**Decision.** Carry `holderJwk` (mandatory, for `cnf`) and `encryptionPublicKey` (for the AEAD envelope) in the field. Feed `encryptionPublicKey` through the **existing** `ExternalRecipientKeys` path; build **net-new** `cnf` plumbing for `holderJwk`.

- **X25519 is derivable from the Ed25519 public key alone.** `CryptoModule.EncryptED25519Async` (`Sorcha.Cryptography/Core/CryptoModule.cs:498-513`) converts the recipient's Ed25519 public key to Curve25519 via libsodium `ConvertEd25519PublicKeyToCurve25519PublicKey` and `SealedPublicKeyBox.Create` — **no private key**. So for ED25519 wallets n1 can derive the encryption key from the tx signature; carrying `encryptionPublicKey` is a robustness choice (algorithm-agnostic; P-256 wallets take a different path).
- **AEAD "supply explicitly" path already exists.** `ActionExecutionService.ResolveRecipientKeysAsync` (`:2321-2414`) resolves recipient keys with precedence **external-supplied (`request.ExternalRecipientKeys`, `ExternalKeyInfo{PublicKey,Algorithm}`) → register batch lookup** (`ResolvePublicKeysBatchAsync`). The envelope wrap (`EncryptionPipelineService.EncryptGroupAsync:225-266`) needs only the public key. **No change to the encryption pipeline.** Note: today external is checked *first*; the design wants **published-record-first**, so inject the carried key only when the register lookup misses (or reorder).
- **`cnf` binding is a pre-existing hole (net-new work).** `SdJwtService.CreateTokenAsync` only emits `cnf` when passed a `holderJwk` (`Sorcha.Cryptography/SdJwt/SdJwtService.cs:175-184`). The Wallet issuance handler calls it **without** one (`CredentialEndpoints.cs:684-694`); `IssueCredentialRequest` has no holder-JWK field (`:813-877`); `ActionExecutionService.IssueCredentialFromActionAsync` passes none (`:1943-2060`). So credentials are issued **unbound** today. Add `HolderJwk` to `IssueCredentialRequest` + `IWalletServiceClient.IssueCredentialAsync` + `CredentialIssuanceConfig`, thread from `IssueCredentialFromActionAsync`, pass into `CreateTokenAsync(holderJwk:)`.
- **No local wallet row needed for SorchaLocalWallet.** The recipient-copy store (`CredentialEndpoints.cs:727-763`, `GetByAddressAsync`) is already bypassed via `SkipRecipientStore=true` (`ActionExecutionService.cs:2057`). Delivery is the on-register encrypted disclosure → `InboundCredentialDetector` on the recipient node.
- **Caveat:** `InboundCredentialDetector` drops plaintext credentials on non-DevMode registers (`InboundCredentialDetector.cs:303-339`) — cross-node delivery MUST go through the encrypted path with a correctly-resolved recipient key (reinforces fail-closed, FR-012).

**Key-type note.** `holderJwk` (slot 108, `cnf`) is P-256 or Ed25519 per `HolderKeyService` (`crv:"P-256"|"Ed25519"`); the AEAD wrap uses the wallet's Ed25519 signing-derived key. They are genuinely **two distinct keys** — the design's two-field shape is correct.

## R6 — The `sorcha-holder-key` schema field type (C3 client)

**Decision.** Add a `ControlTypes.HolderKey` field rendered by a new `HolderKeyRenderer` that autofills from a **new Wallet-Service public-key endpoint** (server round-trip), following the F103 `x-address-lookup → PostcodeLookup` renderer-autofill pattern (not the `x-file` pattern, which is form-block-driven).

- Recognition: `FormSchemaService.InferControlFromSchema` (`Sorcha.UI.Components.User/Services/User/Forms/FormSchemaService.cs:331-408`, ladder at :376-399) → add `else if (format == "sorcha-holder-key") controlType = ControlTypes.HolderKey`.
- Enum: add `ControlTypes.HolderKey` (`Sorcha.Blueprint.Models/Control.cs:123-195`, mirror `PostcodeLookup` :193-194).
- Dispatch: `ControlDispatcher.razor:65-72` → `case ControlTypes.HolderKey: <HolderKeyRenderer/>`.
- Renderer writes nested pointers `FormContext.SetValue("/holderKeys/holderJwk", …)` + `"/holderKeys/encryptionPublicKey"` (sibling fan-out like `PostcodeLookupRenderer.razor:275-298`), read-only to the user.
- Server read: `ActionExecutionService.BuildClaimsFromMappings`/`TryResolveJsonPointer` (`:1782-1840, 1884-1941`) already walks nested objects — `"/holderKeys/holderJwk"` resolves with no new extraction code.
- Validation: `x-holder-key` is auto-tolerated by the generic `x-` strip (`ValidationEngine.cs:1860-1892`, prefix match :1873); unknown `format` validates as pass (like `file-reference`). **Verify** `SchemaValidator.cs:53-65` (no strip) is not on the action-data path, else apply the same strip.

**PWA reality (correction).** The PWA does **not** derive slot-108 on device — `WebCryptoDeviceKeyService` holds the F114 *device* key, not the holder key. The PWA submission surface is a **stub** (`Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor` placeholder + `StubApplicationSubmissionService`). So C3 also requires: (a) a **new Wallet-Service endpoint** returning the citizen's slot-108 holder JWK + X25519 public key (built on `HolderKeyService.GetHolderPublicJwkAsync`), and (b) **wiring `SorchaFormRenderer` into the real PWA submit path**.

## R7 — F108 fan-out config (C4)

**Decision.** Ensure `IPeerServiceClient` BaseAddress is configured on the submitting node so `DistributeTransactionAsync` reaches the peer service. This is a service-client configuration item (the `BaseAddress must be set` warning from the probe), not an architectural change. Confirm the service-client registration + the per-node config key during implementation; add an integration assertion that fan-out is attempted.

---

## Design corrections (fold back into the design doc)

1. **§10 Risk 1 / C5 is a confirmed structural gap, not a tail risk.** The analyst-submits-on-mirror path has two deterministic blockers (read-only-mirror write guard + unseeded `CurrentActionIds`). C5 is a first-class component with real work in `ActionExecutionService`, the instance stores, `DocketBuildTriggerService`, and/or `InstanceMirrorReconstructor`.
2. **§6 client autofill is a server round-trip, and the PWA submit surface is a stub.** The slot-108 holder key + X25519 key are Wallet-Service-managed; C3 needs a new Wallet-Service public-key endpoint and real PWA `SorchaFormRenderer` wiring. Also: the SD-JWT `cnf` binding is a pre-existing hole — issuing a *bound* credential is net-new plumbing, beneficial beyond cross-node.

These do not change the agreed decisions (trust model, precedence, v1 scope); they expand the effort of C3 and C5 and are reflected in `plan.md` and `tasks.md`.

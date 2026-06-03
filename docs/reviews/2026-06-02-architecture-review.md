# Sorcha Architecture Review — Codebase vs. Skills (sorcha-architecture, verifiable-credentials, blueprint-builder)

**Date:** 2026-06-02
**Reviewer:** Claude (Opus 4.8, 1M) — read-only audit, no source changed
**Method:** 7 parallel audit agents, each pinned to a feature slice with the relevant skill claims + the W3C/IETF standards as the conformance baseline. The three CRITICAL/HIGH security findings were re-verified by hand (cited inline). Sub-agent findings carry file:line evidence; "VERIFIED" marks claims I confirmed directly.

> **Scope note.** This audits the codebase *against the three named skills* and the standards they cite. It is not a line-by-line security pen-test; security findings below are the ones surfaced by architectural review. Worktree copies under `.claude/worktrees/*` were excluded as agent scratch state.

---

## 1. Verdict

The platform is **architecturally coherent and, in its load-bearing core, in good shape.** The hard parts that were recently reworked are genuinely clean: the F145 mirror retirement is real in `master`, the F136 tiered-audience issuer hardening fails closed as designed, the F113 storage fail-fast and validator-mempool lease pattern are correct (including the genesis-confirm landmine), and the snackbar retirement ratchet is fully closed. Chain-validation signature verification is real (`ICryptoModule.VerifyAsync`).

The problems are concentrated in three buckets:

1. **A small number of real security defects** — one CRITICAL (TOTP storage), two HIGH (anonymous system-wallet endpoints; consumer tokens reaching blueprint-authoring), plus a cluster of MEDIUM "for now" shortcuts that need a direct look.
2. **Documentation that has drifted behind the code** — the skills themselves contain the most consequential *contradictions* (the `acceptedIssuers` field they still document was deleted; a "not-yet-built" feature actually shipped; the blueprint validation table describes the wrong validator). Repo docs (`project-structure.md`, `development-status.md`, CLAUDE.md test counts) are stale.
3. **Dead / "kept-until-proven" code and a few dual paths** that are individually low-risk but collectively erode the "single obvious path" property.

Nothing here blocks the current pre-release posture, but items in §2 should be fixed before any production exposure.

---

## 2. Security findings (ranked)

### CRITICAL

**C1 — TOTP secrets are stored as reversible Base64, not encrypted.** *(VERIFIED)*
`src/Services/Sorcha.Tenant.Service/Services/TotpService.cs:369-373`. The method's own doc-comment states *"This implementation uses AES-256-GCM with a machine-derived key"* — but the body is `return $"v1:{Convert.ToBase64String(...)}";`. Anyone with read access to the Tenant DB can recover every user's TOTP seed and mint valid 2FA codes, fully defeating second-factor auth. The doc-comment actively misleads a reader into believing it's encrypted.
**Fix:** real AEAD (AES-256-GCM or XChaCha20-Poly1305) with a KMS-held or machine-derived key. The `v1:` prefix already gives you a clean migration discriminator — add `v2:` for the encrypted form.

### HIGH

**H1 — System-wallet create/recover endpoints are `AllowAnonymous` with no server-side authorization.** *(VERIFIED)*
`src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs:44-65`. `POST /api/v1/wallets/system` and `POST /api/v1/wallets/system/recover` (the latter **imports a BIP39 mnemonic** to seat a validator docket-signing wallet) carry only `.AllowAnonymous()`. The inline comments assert "service-to-service, use service auth" and "admin-only at the CLI layer" — neither is enforced in code. The gateway route `wallet-wallets` is merely `RequireAuthenticated`, so **any** authenticated token (including a low-privilege consumer/citizen token) satisfies it; reached directly on the internal network it is fully unauthenticated.
**Exploit sketch:** a citizen consumer token calls `/system/recover` with an attacker mnemonic → attacker-controlled validator signing wallet.
**Fix:** gate with `RequireService` (or `RequireAdministrator` + `RequirePlatformAudience`) at the endpoint.

**H2 — Consumer/citizen tokens can reach blueprint & schema authoring.** *(agent-reported, strong evidence)*
`CanManageBlueprints` (`Sorcha.Blueprint.Service/Extensions/AuthenticationExtensions.cs:27-33`) passes on `hasOrgId OR isService`. Per F136, consumer-tier citizen tokens **carry `org_id`** (`TokenService.cs:118`), so a citizen satisfies it. These endpoints use `CanManageBlueprints` **without** `RequirePlatformAudience`: the `/api/blueprints` CRUD group (`Program.cs:702`), `SchemaEndpoints.cs:93/105/117/128/138`, `CredentialEndpoints.cs:33`, `StatusListEndpoints.cs:51`. Sibling endpoints (`RehearsalEndpoints.cs:39`, `BlueprintFromPublishedEndpoint.cs:69`) *do* compose `RequirePlatformAudience` — proving the omission is an inconsistency, not intent. Gateway routes are only `RequireAuthenticated`, so they don't compensate.
**Fix:** add `RequirePlatformAudience` to every `CanManageBlueprints` endpoint — better, fold the audience assertion **into** the `CanManageBlueprints` policy definition so it can't be forgotten per-endpoint.

**H3 — PWA-local presentation verifier accepts unverified issuer signatures by default.** *(agent-reported)*
`Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs:103-109` wires `VerifiablePresentationValidator` via the 3-arg back-compat ctor → `requireIssuerSignature:false` + `OptOutIssuerKeyResolver` (always null) → `VerifiablePresentationValidator.cs:196-202` accepts on the holder→device chain alone. This contradicts the "signatures verify for real, fail-closed" narrative the `verifiable-credentials` skill states globally. **Context:** this is the citizen's own offline/doorstep verify in the PWA, not the authoritative server gate (Blueprint Service and `Sorcha.Verifier` correctly default `requireIssuerSignature:true` under F120). Still worth hardening or explicitly documenting as a scoped exception.

### MEDIUM

**M1 — Orphaned V1 `Transaction` with no-op signature verification (latent trap, not live).** *(VERIFIED — downgraded from an agent's CRITICAL)*
`src/Common/Sorcha.TransactionHandler/Core/Transaction.cs:84-159`: `SignAsync` sets `SenderWallet = "ws1temp"` and decodes the WIF "simplified"; `VerifyAsync` returns `Success` after verifying only payloads ("For now, return success as placeholder"). **I confirmed this is not on the live path** — `ITransactionFactory`/`CreateV1Transaction` is never DI-registered in any service, the real validator verifies via `ICryptoModule.VerifyAsync` (`ValidationEngine.cs:733,1012`), and the real builder (`TransactionBuilderService`) doesn't use the factory. It is a dead/legacy module. Risk is purely latent: if anyone ever wires `TransactionFactory` in, signature verification silently becomes a no-op. **Fix:** delete the module, or replace both placeholders with `throw new NotSupportedException` so a future accidental use fails loud.

**M2 — `ICitizenCredentialEventStream` is on the fail-fast audited list but bypasses the audit.** *(agent-reported)*
`AuditedStorageInterfaces.cs:75` lists it, but `Wallet/Program.cs:175-176` registers it with a bare `AddScoped` and **no `storageLog.RegisterPersistent/RegisterInMemory` call**. The fail-fast enforcement, the `storage-providers` health check, and the OTel gauges therefore never see it — its audited-status is inert. This is the exact failure mode F113 was built to prevent (here via *omission* rather than a wrong magic string). **Fix:** route it through `IStorageRegistrationLog` (it's always EF today → `RegisterPersistent`), or remove it from the audited set.

**M3 — "For now" security shortcuts that need a direct look.** *(agent-reported — verify before trusting)*
- `Sorcha.Tenant.Service/Services/OidcExchangeService.cs:295` — "we trust the token came from the configured IDP" (no issuer signature validation on the exchange path).
- `Sorcha.Wallet.Service/Services/Implementation/PasskeyRecoveryService.cs:84` — "verify the wrap exists" (incomplete WebAuthn assertion verification).
- `Sorcha.Haip.Service/Endpoints/CredentialEndpoints.cs:295` — ephemeral key if no config.
These are flagged from comment text; confirm the actual code path before rating, but each is a plausible auth-bypass surface.

**M4 — `MarkCompletedAsync` TOCTOU still open.** *(agent-reported)*
`Sorcha.Haip.Service/Services/PresentationRequestStore.cs:119-143` still carries `TODO(113-followup)` and does a non-atomic Get→mutate→Set on a plain `IDistributedCache` (not `IAtomicDistributedCache`). Concurrent verifier callbacks → last-writer-wins. `TryUpdateIfMatchAsync` already exists to close it.

### LOW

- **`RequireSystemAdmin` weakened to role-only in Tenant Service.** `AddTenantAuthorization` (`AuthenticationExtensions.cs:151-152`) re-registers the policy that `AddSorchaAuthorizationPolicies` defined as org-scoped (`AuthorizationPolicyExtensions.cs:174-181`); the later registration wins, dropping the system-admin-org constraint for `platform-*` gateway routes.
- **Token revocation fails open on Redis error** (`JwtAuthenticationExtensions.cs:287-294`) — deliberate availability tradeoff; note for risk acceptance.
- **F124 pending-application endpoints use plain `.RequireAuthorization()`** not `RequireConsumerAudience` like every sibling citizen surface (`PendingApplicationEndpoints.cs:26`) — a platform token can read/set a citizen's notice.
- **Stale `sorcha:citizen-wallet` audience in dev config** (`Wallet/Tenant appsettings.Development.json`) — dead (code ignores config audiences, uses `SorchaAudiences.All`), remove to avoid confusion.
- **Default service-principal secrets in base appsettings** (`Validator.Service/appsettings.json:51`, `Register.Service/appsettings.json:30`) — must be overridden via env/secrets in prod.
- **Anonymous bootstrap** (`BootstrapEndpoints.cs:30`) is well-guarded (one-shot `BootstrapCompleted` + optional `BOOTSTRAP_SECRET`); set the secret in prod.

### What's solid (verified, give credit)
F136 single-source audiences + issuer fail-closed in Production/Staging (`SorchaIssuer.cs:33-63`, VERIFIED); centralized rate limiting (SEC-002, no per-service `AddRateLimiter`); open-redirect allowlist (F126, HTTPS-only + fail-closed, tokens via URL fragment); CSRF `SameSite=Strict` on Razor auth pages; IDOR scoping derives identity from the caller's JWT (404-indistinguishable cross-user lookups); F113 fail-fast + genesis-confirm lease (`DocketBuilder.cs:108-111`, PR #416 comment intact); presentation consumers never write the register; sign-out enumerate-all IndexedDB wipe; cache evict-and-continue.

---

## 3. Contradictions, confusing & stale statements (the "is it consistent" question)

The most damaging contradictions are **inside the skills themselves** — an AI agent or human trusting them will write code against a contract that no longer exists.

| # | Contradiction | Reality | Severity |
|---|---|---|---|
| D1 | `verifiable-credentials` **and** `blueprint-builder` skills document `acceptedIssuers` on `CredentialRequirement` (in examples, prose, and the `OPEN_CREDENTIAL_ISSUER` rule "fires on empty `acceptedIssuers[]`"). | F135 **removed** the field; replaced by `TrustPolicy`. `CredentialRequirement.cs` has no `AcceptedIssuers`. The warning now keys off `TrustPolicy.Sources` (`BlueprintToolExecutor.cs:1230`). An author copying the skill JSON writes a **silently-ignored field**. Code correct; **skills stale**. | HIGH (doc) |
| D2 | `blueprint-builder` marks the `$ref` core schema library *"Status: lands with the Verified Citizen v2 PR … authoritative direction … once the PR ships."* | **Shipped (F103):** `SchemaRefResolver.cs`, `CoreSchemaSeedService.cs`, all 5 catalog files, child-wins layout merge, publish-time `FlattenActionSchemas` (`Program.cs:3107`). Skill **undersells a live feature**. | MEDIUM (doc) |
| D3 | `blueprint-builder` validation-code table says publish validation "runs in `Sorcha.Validator.Service`" and lists `MIN_PARTICIPANTS`/`INVALID_TITLE`/`OPEN_CREDENTIAL_ISSUER`/etc. | Those **coded** checks live only in the AI-chat `BlueprintToolExecutor`. The **real `/publish` path** is in `Blueprint.Service` (`PublishService.ValidateBlueprint`) and emits **uncoded strings**; `INVALID_TITLE`/`INVALID_DESCRIPTION` are **not enforced at publish at all**. Two divergent validation surfaces that disagree on what's checked. | HIGH (confusion) |
| D4 | `sorcha-architecture` F145 section says legacy `NextActionId` "remains until the US5 sweep" and the `LocallyOwned`/topology removal "is gated after T034 lands." | Code has **already removed** `NextActionId` (gate-enforced) and **completed** T034 (`ForwardSubmissionAsync` has no `LocallyOwned` branch). Skill prose is the laggard. | MEDIUM (doc) |
| D5 | `verifiable-credentials` pitfall: sign with `"sorcha:vc-issuance"` derivation purpose. | No such string in code; real mechanism is F083 `KeyUsage.VCIssuance` via `IssuanceKeyService.cs`. "Never root key" intent holds; the cited string is wrong. | LOW (doc) |
| D6 | Skills cite exact runtime lines (e.g. `ValidationEngine.cs:1027`, `ActionExecutionService.cs:309-332`). | Logic correct, **line numbers drifted ~100-200 lines** (actual: `ValidationEngine.cs:1352`, late-bind `:419-452`). The code's own XML docs carry the same stale numbers. | LOW (doc) |
| D7 | `docs/reference/project-structure.md` (2026-04-21) lists `Sorcha.PeerRouter` (retired, F143) and `Sorcha.Storage.EFCore` (phantom — no csproj); omits 8 real projects incl. the whole `src/Providers/` tier; counts wrong. `development-status.md` still lists PeerRouter "100% live." | Stale docs. | MEDIUM (doc) |
| D8 | CLAUDE.md: "1,200+ tests across 30 projects." | Actual ~**11,095** test methods across **52** test projects (~9× understated). | LOW (doc) |
| D9 | `STANDARDS.md` marks ISO 18013-5 mdoc "planned." | `Sorcha.Cryptography/Mdoc` (MdocService/MdocIssuer/MdocCodec) is implemented (F135). Should be "partial" — under-claim (safe direction). | LOW (doc) |
| D10 | Two `IIssuerKeyResolver` interfaces, same name, different namespaces/contracts (`Blueprint.Engine.Credentials` vs `Verifier.Engine`); two `SorchaInternal` enums with split deprecation (`TargetAudience` is `[Obsolete]`, `PresentationSource` is not). | Naming collisions that make "the deprecated SorchaInternal" ambiguous. | LOW |

---

## 4. Dead code inventory

| Item | Location | Status | Recommendation |
|---|---|---|---|
| `EmptyCitizenCredentialEventStream` | `CitizenSyncService.cs:216-225` | De-registered, unreferenced (confirmed by 3 agents). Skill claims it was "retired." | Delete the class. |
| `StubApplicationSubmissionService` / `IApplicationSubmissionService` (F125) | registered at `ServiceCollectionExtensions.cs:121`, **zero consumers** | Live DI registration of a no-op that fakes submission success with a synthesised instance id. Superseded by F137 `IApplicationActionClient`. Footgun if ever wired to UI. | Delete or scope to tests. |
| US6 imperative-advance cluster (6 methods) | `EnqueueAdvancementAsync`→`TryDrainAdvancementAsync`→`CompleteAfterPresentationAsync`→`UpdateInstanceAfterExecutionAsync`→`ApplyInstanceStateChanges`→`PersistInstanceAsync` | `EnqueueAdvancementAsync` has **zero callers** → whole chain unreachable. Intentional "kept-until-proven." **Double-advance footgun if re-wired** (projector + imperative both advance). | Complete deletion after F111/F127 live-validation; then flip the clean-break gate's `ApplyInstanceStateChanges` to `Enforced=true`. |
| `Sorcha.TransactionHandler` V1 `Transaction`/`TransactionFactory` | `Core/Transaction.cs`, `Versioning/TransactionFactory.cs` | Orphaned (factory not DI-registered), placeholder sign/verify (see M1). | Delete or `throw NotSupportedException`. |
| `Sorcha.PeerRouter` build artifacts | `src/Apps/Sorcha.PeerRouter/obj/` | Project retired (F143); only stale `obj/` remains; not in solution. | Delete the `obj/` dir. |
| Pre-cutover mirror code | `.claude/worktrees/agent-*` (detached, not ancestors of master) | Pollutes repo-wide greps; invisible to the clean-break gate. | `git worktree remove` the stale locked worktrees. |
| Legacy condition-based routing | `Action.Participants` / `Action.Condition` (default always-true `{"==":[0,0]}`) | Live fallback when `Routes` empty; `Action.Condition` effectively unused. | Consider deprecating; document the precedence clearly. |
| `BlueprintServiceClient.PublishBlueprintAsync` | `Sorcha.ServiceClients.Http/Blueprint/BlueprintServiceClient.cs:577` | **`throw new NotImplementedException` (F142 unfinished) on a public consolidated-client method.** Any caller hits it at runtime. | Implement or return a clear 501; don't ship a throwing public client method. |
| `StubVerifierEngine` (PWA) | de-registered, debug-only | Harmless. | Leave or remove. |

---

## 5. Single-obvious-path analysis

**Clean (single path) ✅:** instance state writer (projector only, F145); DID resolution (one `DidResolverRegistry`); storage selection (uniform `IStorageRegistrationLog`); credential delivery to citizen (one `InboundCredentialDetector→CitizenInboxProjector` route); snackbar (allowlist **empty**, fully migrated); validator mempool / atomic-cache / seal-coordinator (one impl pair each).

**Dual / competing paths ⚠️:**

1. **VC verification — two stacks.** (a) F135 unified: `CredentialVerifier`/`HaipPresentationVerifier` → `ICredentialFormatHandler` → `ITrustEvaluator`. (b) F114/F127/PWA: `Sorcha.Verifier.Engine.VerifiablePresentationValidator` with its own SD-JWT parsing, its own `IIssuerKeyResolver`, and a `RequireIssuerSignature` bool instead of `ITrustEvaluator`. SD-JWT compact-form splitting is reimplemented in **3 places** (`SdJwtService`, `VerifiablePresentationValidator`, `PresentationEngine`). The skill's "ONE `ITrustEvaluator`" claim is true only for stack (a). *This is the biggest single-path violation and overlaps H3.*
2. **Blueprint validation — two surfaces** (chat-tool coded vs publish-path uncoded), §3/D3.
3. **UI service clients — two ecosystems**: legacy hand-rolled `Sorcha.UI.Core` API services (~40 `new HttpClient(handler)` registrations) vs consolidated `Sorcha.ServiceClients.Http` in `Sorcha.UI.Components.User` (F122). Migration incomplete. *Not a CLAUDE.md violation (both are browser→gateway, not s2s bypass), but drift.*
4. **JSON serialization** — 90 `JsonSerializerOptions` instantiations across 86 files; canonical providers exist (`RegisterSerializationOptions`, `JsonDefaults`) but most call-sites roll their own camelCase options. Low-grade duplication.

---

## 6. Standards conformance gaps (vs the cited W3C/IETF specs)

- **SD-JWT VC media type drift (MEDIUM interop).** `SdJwtService.cs:200` emits `typ:"vc+sd-jwt"`; the current SD-JWT VC / EUDI profile uses **`dc+sd-jwt`**. KB-JWT `typ:"kb+jwt"` is correct. Strict EUDI verifiers may reject.
- **DID Core `@context` omitted.** `DidDocument` deliberately has no `@context`; DID Core 1.0 lists it as required for a conformant representation. Harmless internally, a gap for external consumers — record as known non-conformance.
- **Bitstring Status List.** Conformant (GZIP + MSB-first bits + `statusPurpose`), but decodes with standard `Convert.FromBase64String` rather than `u`-multibase base64url — a strictly multibase-encoded status list from a third party would fail to decode.
- **Otherwise conformant:** SD-JWT `_sd` disclosure hashing (RFC 9901), `vct`/`cnf` (SD-JWT VC profile), no JSON-LD / Data Integrity Proofs / Newtonsoft anywhere in the VC stack (matches the deliberate ecosystem choice), mdoc trust fails closed (ES256/P-256-only, register-anchor rejected at issuance, deviceMac unverified-by-design in v1).

---

## 7. Obvious errors (your explicit ask)

1. **TOTP "encryption" is base64** — doc says AES-256-GCM, code does `v1:{base64}` (C1).
2. **System-wallet endpoints `AllowAnonymous`** with comments claiming an enforcement that doesn't exist (H1).
3. **`CanManageBlueprints` endpoints missing `RequirePlatformAudience`** while their siblings have it — a clear, inconsistent omission a consumer token walks through (H2).
4. **`ICitizenCredentialEventStream` audited-but-not-logged** — the audit can never fire for it (M2).
5. **`BlueprintServiceClient.PublishBlueprintAsync` throws `NotImplementedException`** on a public client method — a runtime trap for any caller (§4).
6. **Skills document a deleted field (`acceptedIssuers`)** and the wrong validator location — anyone following them writes broken/no-op blueprints (D1, D3).
7. **PWA verifier defaults `requireIssuerSignature:false`** against a stated fail-closed posture (H3).
8. **V1 `Transaction.VerifyAsync` is a no-op** (dead, but a trap) and `SignAsync` ships a `"ws1temp"` placeholder (M1).

---

## 8. Potential improvements (prioritized)

**Do before any production exposure**
1. Encrypt TOTP secrets with real AEAD + KMS/derived key (C1). Use a `v2:` discriminator.
2. Add server-side authz (`RequireService`) to the system-wallet create/recover endpoints (H1).
3. Add `RequirePlatformAudience` to all `CanManageBlueprints` endpoints — ideally fold it into the policy so it can't be omitted per-endpoint (H2).
4. Either harden the PWA-local verifier to fail-closed or document it as an explicit, scoped exception (H3).
5. Wire `ICitizenCredentialEventStream` through `IStorageRegistrationLog` or drop it from the audited set (M2).
6. Directly review the three "for now" auth shortcuts (M3) and decide each.

**Hygiene / clean-break completion**
7. Delete dead code: `EmptyCitizenCredentialEventStream`, `StubApplicationSubmissionService`, orphaned `TransactionHandler` V1, `PeerRouter/obj`, stale worktrees (§4).
8. Finish the F145 US6 deletion once F111/F127 live-validation passes; flip the gate's `ApplyInstanceStateChanges` to `Enforced=true`; extend the clean-break gate to scan `tests/` too.
9. Migrate `MarkCompletedAsync` to `IAtomicDistributedCache.TryUpdateIfMatchAsync` (M4).
10. Fix the `BlueprintServiceClient.PublishBlueprintAsync` throw (§4).

**Single-path consolidation (medium-term)**
11. Unify the two VC verification stacks behind `ITrustEvaluator`, or at minimum collapse the 3 SD-JWT-splitter copies (§5.1).
12. Reconcile the two blueprint validation surfaces — make `/publish` emit the same coded errors as the chat tool, and enforce title/description there (§5.2 / D3).
13. Continue the F122 UI-client migration; introduce one shared `JsonSerializerOptions` provider (§5.3-5.4).
14. Migrate SD-JWT `typ` to `dc+sd-jwt` for EUDI interop (§6).

**Documentation (cheap, high leverage)**
15. Update `sorcha-architecture`, `verifiable-credentials`, `blueprint-builder` skills: `acceptedIssuers`→`TrustPolicy`; mark `$ref` library shipped; correct the validation-surface/location; refresh F145 prose (NextActionId removed, T034 done); fix line numbers; fix `PairingTakeover`/template file paths.
16. Refresh `docs/reference/project-structure.md` (phantom + missing projects), `development-status.md` (PeerRouter), CLAUDE.md test count, `STANDARDS.md` mdoc → partial.

---

## 9. Appendix — verification log

Directly re-verified by the reviewer (not just agent-reported):
- `TotpService.cs:369-373` — base64, not AES (C1, CONFIRMED).
- `WalletEndpoints.cs:44-65` — both endpoints `AllowAnonymous` (H1, CONFIRMED).
- `Transaction.cs:84-159` — placeholder sign/verify (M1, CONFIRMED as code) **and** `ITransactionFactory`/`CreateV1Transaction` never DI-registered + real validator uses `ICryptoModule.VerifyAsync` (`ValidationEngine.cs:733,1012`) + real builder doesn't use the factory ⇒ **not on live path**, downgraded CRITICAL→MEDIUM.

All other findings are sub-agent results carrying file:line evidence; treat the MEDIUM "for now" items (M3) as leads to confirm before action.

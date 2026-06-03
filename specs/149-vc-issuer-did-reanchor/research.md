# Phase 0 Research: Re-anchor org VC-issuer DID + fail-closed issuance

Resolves the open questions from the design before planning. Evidence gathered by code trace (file:line cited inline). All NEEDS CLARIFICATION resolved.

## D1 — How the Wallet Service obtains the canonical operational address A

**Decision:** Add a Tenant internal endpoint `GET /api/internal/orgs/{orgId:guid}/wallet-address` (`RequireService`) returning `Organization.WalletAddress`, plus a thin `IOrgInfoClient.ResolveCanonicalWalletAddressAsync(orgId)` in `Sorcha.ServiceClients.Http` mirroring `IOrgDidDocumentClient`. Inject it into `IssuanceKeyService` so the issuer DID is built from A.

**Rationale:** No existing client returns org→wallet-address by orgId (`IOrgDidDocumentClient` is circular — it reads the published doc's `id`, which is null until issuance; `IParticipantServiceClient` maps wallet→org, the wrong direction). Co-locating the lookup in the Wallet Service (where the DID is constructed in `IssuanceKeyService.cs:233-234`) keeps the anchoring concern in one place; the Wallet Service already calls Tenant via `IOrgDidDocumentClient` and the device clients, so one more internal GET fits the pattern.

**Alternatives considered:** (a) Blueprint Service passes A into `IssueCredentialAsync` — rejected: `senderWallet` (passed today, `ActionExecutionService.cs:2215`) is the action-sender wallet, not guaranteed to equal `Organization.WalletAddress`, reintroducing the fragility we are removing. (b) Anchor to the `orgId` GUID — rejected: changes `did:sorcha:org:` semantics platform-wide and touches every A-consumer.

**Null handling:** `Organization.WalletAddress` is `string?` (`Organization.cs:96`), set at org creation (`OrganizationService.cs:107`) with a reconciliation backstop (`OrgWalletReconciliationService.cs:134`) but provisioning failure is swallowed, so a null window exists. A null A at mint time → **fail closed** (D4), never a fallback.

## D2 — Re-anchoring the issued credential and the published document

**Decision:** In `IssuanceKeyService.GetActiveSigningMaterialAsync`, build `iss = did:sorcha:org:{A}` and `kid = did:sorcha:org:{A}#vc-issuance-{n}` from A (not `derivedRecord.WalletAddress`). The verification method's `publicKeyJwk` stays the derived child **C's** key bytes. Pass **A** as `OrgDidRegenerateRequest.WalletAddress` (`IssuanceKeyService.cs:128,408`).

**Rationale:** `OrgDidDocumentService.RegenerateFromSnapshotAsync` consumes `snapshot.WalletAddress` only at `OrgDidDocumentService.cs:52` (`primaryDid = $"did:sorcha:org:{snapshot.WalletAddress}"`), then propagates it opaquely into the doc `id` and all VM ids (`:131,:138,:163`). Feeding A re-anchors the whole published document to `did:sorcha:org:{A}` with **no change** to `OrgDidDocumentService`. The VM ids it emits (`{primaryDid}#vc-issuance-{n}`, in `assertionMethod` at `:166`) then match the minted `kid` exactly.

## D3 — Verifier resolves the PUBLISHED Tenant document (the critical refinement)

**Decision:** Add a by-DID Tenant route `GET /orgs/by-did/{did}/did.json` (anonymous, mirrors the existing `GET /orgs/{orgId:guid}/did.json` at `OrgDidDocumentEndpoints.cs:24`) backed by a `PrimaryDid` lookup (index already exists, `TenantDbContext.cs:205`). Repoint `SorchaDidResolver.ResolveOrgDidAsync`'s public-DID fetch at this Tenant route and wire the 3-arg (HttpClient) ctor — with the **Tenant** base address — in the hosts whose verifiers resolve org issuer keys: **Blueprint Service** (the P1 engine path) and **HAIP** (also broken by re-anchor). Remove the hardcoded `#vc-issuance-1` local-rebuild for org DIDs; if the Tenant doc is unreachable, resolution returns null → fail closed.

**Rationale (why the naive fix is a trap):** There are two `did.json` sources. The Wallet by-address endpoint `GET /api/v1/wallets/{address}/did-document` (`IssuanceKeyEndpoints.cs:85`) publishes the **wallet row's own key** for *every* VM including `#vc-issuance-{n}` (`:172,:188,:211`) — so after re-anchoring it would serve **A's** key under `#vc-issuance`, but the signature is by **C** → every verification fails. The existing `_publicDidHttp` ctor (`SorchaDidResolver.cs:42,89-91`) points at exactly that Wallet endpoint, so simply "activating it" does **not** work. Only the Tenant document (`OrgDidDocumentService`, fed C's key via `RegenerateAsync`) carries the correct `#vc-issuance-{n} → C` mapping. The Tenant doc is GUID-keyed today; the resolver holds the DID/address, so a by-DID route (using the existing `PrimaryDid` index) lets it fetch from the address alone in one round-trip.

**Blast radius:** `AddDidResolvers` registers the 2-arg ctor by default (`HttpServiceCollectionExtensions.cs:123`); registrants are Blueprint Service (`Program.cs:344`), Wallet Service (`Program.cs:47`), HAIP (overrides to 3-arg → Wallet endpoint, `Program.cs:166-170`), and the Verifier app (2-arg). We override `AddScoped<SorchaDidResolver>` after `AddDidResolvers` in Blueprint Service only, and repoint HAIP's existing HttpClient base address from Wallet → Tenant. The engine (`Sorcha.Blueprint.Engine`) holds only the `IDidResolverRegistry` seam and never news-up `SorchaDidResolver` (WASM-safe). Wallet-DID (`did:sorcha:w:`) resolution is untouched (only `ResolveOrgDidAsync` changes).

**Alternatives considered:** (a) Fix the Wallet by-address endpoint to publish C's keys for org operational wallets — rejected: the Wallet Service can't reliably map operational address A → orgId → `IssuanceKeyState` rows (that linkage is Tenant's), so it can't assemble the correct doc; the Tenant already has it. (b) Resolve in `DidX5cIssuerKeyResolver` directly via a by-DID `IOrgDidDocumentClient` method, bypassing `SorchaDidResolver` — viable and more localized, but leaves HAIP's verifier broken; fixing the shared resolver fixes both consumers.

## D4 — Fail-closed issuance

**Decision:** In `CredentialEndpoints.cs`, guard immediately before the signing-material fallback (~`:598`): when `issuanceMaterial is null`, return an error result (HTTP 409/422 `Results.Problem`) with an actionable message naming the missing master key; delete the `signingIssuer = issuanceMaterial?.IssuerDid ?? walletAddress` / null-`kid` fallback (`:605-606`) so the bare-wallet path no longer exists.

**Rationale:** The mint must never produce an unverifiable credential. The handler already returns `IResult` with validation `Results.BadRequest`/`Problem` returns, so an early error result is idiomatic. For the **SorchaLocalWallet** action path (our scope) this already propagates to a fatal action error: `ActionExecutionService.cs:685-690` throws `[VAL_RUNTIME_CRED_002]` on a null/failed mint — so the action fails closed end-to-end.

**Scope boundary (Option B deferred):** The HAIP/generic Blueprint catch at `ActionExecutionService.cs:2233-2240` swallows issuance failure (action still succeeds, loud error log). We do **not** change it in this PR — that would alter HAIP/external-wallet availability semantics and is outside the native-credential scope. With the mint now returning a clear error instead of silently minting garbage, the existing log is already actionable. Making config-present issuance fatal across all audiences is recorded as a deferred follow-up.

## D5 — Ed25519 (OKP) key-shape on the verify side

**Decision / risk to verify in implementation:** `DidX5cIssuerKeyResolver.ExtractPublicKeyFromJwk` returns **raw 32-byte `x`** for OKP/Ed25519 (`:164-165`) vs SPKI DER for EC (`:161`). Confirm the downstream engine verifier (`SdJwtVcFormatHandler` / `ISdJwtService`) consumes raw-32 for an EdDSA issuer key (the dominant Sorcha case — org wallets default ED25519). HAIP already exercises this path, so it is likely correct, but a unit test pins it (US1 acceptance, EdDSA issuer).

## D6 — Clean break

**Decision:** No `alsoKnownAs` bridge from the old derived-child DID, no migration of already-issued credentials. Dev data wiped and regenerated. Confirmed acceptable: pre-production (FR-008).

## Summary of platform changes

| # | Change | Component |
|---|--------|-----------|
| 1 | New internal `GET /api/internal/orgs/{orgId}/wallet-address` (`RequireService`) | Tenant Service |
| 2 | New `IOrgInfoClient.ResolveCanonicalWalletAddressAsync` | ServiceClients.Http |
| 3 | Build `iss`/`kid`/regenerate-snapshot from A (resolved via #2); null-A → fail | `IssuanceKeyService` (Wallet) |
| 4 | New by-DID route `GET /orgs/by-did/{did}/did.json` (`PrimaryDid` lookup) | Tenant Service |
| 5 | `ResolveOrgDidAsync` fetches the Tenant by-DID doc; drop hardcoded `#vc-issuance-1` rebuild | `SorchaDidResolver` (shared) |
| 6 | Wire 3-arg ctor → Tenant base address | Blueprint `Program.cs` (override) + HAIP `Program.cs` (repoint) |
| 7 | Fail-closed guard; remove bare-wallet/null-kid fallback | `CredentialEndpoints` (Wallet) |
| — | Tests: US1 trusted-issuance (EdDSA), US2 fail-closed, US3 rotation | Wallet / Blueprint / Tenant test projects |

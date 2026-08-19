# Wallet Service — pre-release external review

**Date:** 2026-08-19
**Scope:** `src/Services/Sorcha.Wallet.Service` and its reachable surface (REST, gRPC, SignalR),
viewed from outside as a user, an administrator, and an attacker.
**Method:** static review of every endpoint group, every route's authorization metadata, the
authorization policies they name, the handlers behind them, and the API Gateway routes that expose
them. Reviewed against the F083/F114/F120/F136/F180/F182 designs (`sorcha-architecture` skill) and
against the two sibling services that have already had this class of defect fixed.

**Note on verification:** the review is static. No .NET SDK is available in this environment, so
nothing below was executed. Each finding cites the file and line that establishes it, and the two
most severe rest on a repo-wide fact that is easy to re-check: **there is no `FallbackPolicy`
anywhere in the solution** (`grep -rn FallbackPolicy --include=*.cs --include=*.json .` returns
nothing), so an endpoint with no authorization metadata is anonymous.

---

## Summary

The wallet-scoped REST surface has had a real security pass — the G1 catch-up review (2026-07-29)
introduced `WalletOwnershipGate` and closed the credentials, delegation, and `PATCH`/`DELETE`
`/{address}` holes, and the service README documents both the gate and a list of routes still
awaiting it. That work is sound and the README is unusually honest.

What this review found is that **the perimeter was drawn around one shape of route and three other
shapes were never brought inside it**:

- routes keyed on an **org id** rather than a wallet address (`/api/wallets/org/{orgId}/*`,
  `/api/v1/orgs/{orgId}/issuance-key/*`),
- the **gRPC** surface, which carries no authorization at all,
- two **wallet-scoped groups added after** G1 (`Ethereum`, `Ethereum transactions`) that are absent
  even from the README's own "not yet gated" list.

Two of these are the same defect that was found *and fixed* in a sibling service in the same
security review, with a reusable primitive left behind that was never applied here:

| Defect class | Fixed in | Primitive left behind | Applied in Wallet? |
|---|---|---|---|
| Role/tier check with no caller-org binding | Tenant Service (B2+) | `CallerOrganizationGate` / `.RequireCallerOrganization()` | **No** |
| `MapGrpcService` with no authorization | Validator Service | `ValidatorGrpcAccessInterceptor` | **No** |

The single reason W-4 and W-5 went unnoticed is W-8: the guard test that both the gate's XML doc and
the README describe as asserting "every route carrying a wallet address in its template has it"
does not do that. It iterates a hardcoded list of two groups.

**Recommendation:** W-1 and W-2 should block public release. W-3, W-4, W-5 should block release.
W-8 should be fixed first, because it is what makes the rest re-findable.

| ID | Severity | Finding |
|---|---|---|
| W-1 | **Critical** | Org VC-issuance-key endpoints have no authorization metadata — anonymous at the service, any-authenticated via the gateway, no org binding. `/sign` is a credential-forgery oracle for any organisation. |
| W-2 | **Critical** | Wallet gRPC services carry no authorization. `GetDerivedKey` returns raw private keys; `SignData` signs with the root key; `GetAllLocalAddresses` streams every wallet. |
| W-3 | **High** | `/api/wallets/org/{orgId}/*` never compares `{orgId}` to the caller's `org_id`. Cross-org master-key provisioning (returns the plaintext BIP39 mnemonic), key derivation, rotation, and revocation. |
| W-4 | **High** | `POST /api/v1/wallets/{walletAddress}/siwe/sign` — any authenticated citizen obtains a SIWE prove-control signature for any wallet's Ethereum identity. |
| W-5 | **High** | `POST /api/v1/wallets/{walletAddress}/ethereum/transactions` — any authenticated citizen sends ETH from any wallet. |
| W-6 | Medium | Three mutating routes on any wallet's HD-address subtree (register / update / mark-used), triaged as "needs an individual decision" alongside genuinely benign routes. |
| W-7 | Medium | Any wallet's derived-address graph, accounts, and gap status are readable by any authenticated citizen. |
| W-8 | Medium | The ownership-gate guard test does not assert what its docstring and the README claim. |
| W-9 | Low | Recovery endpoints advertise a working feature in OpenAPI while always returning 501. |
| W-10 | Low | `Features:WalletRecoveryEnabled` is one bool between 501 and unverified recovery, with no environment guard. |
| W-11 | Low | README's gate list contradicts itself on `decapsulate` and includes a route that needs no gate. |
| W-12 | Low | `EncryptPayload` ignores the route address when the body supplies one — a future gate on `{address}` would be bypassable. |
| W-13 | Polish | `DecapsulateKey` hardcodes `"token_type"` / `"service"` instead of `TokenClaimConstants`. |
| W-14 | Polish | `/api/stats` returns the platform-wide wallet count anonymously. |

---

## W-1 (Critical) — Org VC-issuance-key endpoints have no authorization

**Where:** `src/Services/Sorcha.Wallet.Service/Endpoints/IssuanceKeyEndpoints.cs:30-33`

```csharp
var group = app.MapGroup("/api/v1/orgs/{orgId:guid}/issuance-key")
    .WithTags("IssuanceKey")
    .RequireRateLimiting(RateLimitPolicies.Api);   // <-- rate limit only
```

No `.RequireAuthorization(...)` on the group, and none on any of its four routes — `/ensure`,
`/sign`, `/rotate`, `/revoke`. The only authorization-related annotation in the whole file is the
deliberate `.AllowAnonymous()` on the unrelated public DID-document reader at line 84.

Because the solution defines no `FallbackPolicy`, these four routes are **anonymous at the
service**. Through the API Gateway they are reachable by *any authenticated user*:

```jsonc
// src/Services/Sorcha.ApiGateway/appsettings.json:437
"wallet-issuance-key": { "ClusterId": "wallet-cluster",
  "AuthorizationPolicy": "RequireAuthenticated", "Order": -1,
  "Match": { "Path": "/api/v1/orgs/{orgId}/issuance-key/{**remainder}" } },
```

`RequireAuthenticated` is `RequireAuthenticatedUser()` — no tier, no role, no org. So any citizen
with a login satisfies it, for any `{orgId}`.

**Impact.** `POST /api/v1/orgs/{orgId}/issuance-key/sign` signs caller-supplied bytes with the
named organisation's **Active VC issuance private key** and returns the signature together with the
`kid` and issuer DID needed to assemble the JWS header (`SignWithIssuanceKey`, lines 262-300). That
key is the one published in the org's DID document under `assertionMethod` (`ResolveWalletDidDocument`,
lines 195-215) — the key every Sorcha verifier and the F120 `DidResolverBackedIssuerKeyResolver`
resolve to and trust. An attacker can therefore mint a credential of any type, with any claims,
that verifies as genuinely issued by any organisation on the installation. This is not a
confidentiality bug; it is a break of the platform's issuance trust model.

`POST .../revoke` marks a rotation `Revoked`, which drops it from the published document's
`assertionMethod`, so **every credential that organisation has issued stops verifying**.
`POST .../rotate` has the same effect. Both are unauthenticated writes.

The private key itself is never returned and is zeroised after signing (`finally` block, line 296) —
that part is correct. It does not help: an oracle that signs anything is equivalent to holding the
key.

**Why it is a plain omission, not a design choice.** The intended callers are services. The file's
own remarks say *"so any service that mints credentials can trigger the key + DID document publish"*
and *"Used by Sorcha.Haip.Service's /credential endpoint"*, and the F120 notes in the
`sorcha-architecture` skill describe the same service-to-service role. Every comparable internal
surface in this service is gated: `IssuerCertKeyInternalEndpoints` (the F181 sibling that also signs
with an org key) uses `AuthorizationPolicies.RequireService`, as does
`CitizenStatusListInternalEndpoints`.

**Fix.** `.RequireAuthorization(AuthorizationPolicies.RequireService)` on the group, plus a caller-org
binding for any non-service caller if one is ever intended. Move the routes under `/api/internal/`
to match the house convention, and drop the gateway route unless an external caller genuinely needs
them.

---

## W-2 (Critical) — the gRPC surface carries no authorization

**Where:** `src/Services/Sorcha.Wallet.Service/Program.cs`

```csharp
app.MapGrpcService<WalletGrpcService>();
app.MapGrpcService<WalletNotificationGrpcService>();
```

Neither call has `.RequireAuthorization()`. Neither service class nor any of their methods carries
`[Authorize]` (`grep -rn Authorize GrpcServices/` returns nothing). No `Interceptor` is registered —
`AddGrpc()` is called bare. With no `FallbackPolicy`, every method is anonymous on the HTTP/2 port
(`Kestrel:GrpcPort`, default 5001).

**Impact, per method:**

- `GetDerivedKey` (`WalletGrpcService.cs:385-448`) — takes a wallet address and a derivation path,
  decrypts the wallet's master key, derives the child, and returns
  `PrivateKey = ByteString.CopyFrom(privateKey)`. **Raw private key material for any wallet, at any
  BIP32 path, to an unauthenticated caller.**
- `SignData` (lines 177-266) — signs any 32-byte hash with the wallet's key; with `DerivationPath`
  empty it uses the **root** key directly (`privateKey = decryptedKey`, line 236).
- `GetAllLocalAddresses` (`WalletNotificationGrpcService.cs:101`) — streams every wallet on the node,
  paged, with no caller check anywhere in the file.
- `NotifyInboundTransaction` / `...Batch` — unauthenticated injection of inbound-transaction
  notifications.

Note what this bypasses. The REST `SignTransaction` path carries the #1397 hardening: a service
token may only sign with a `validator:*`-owned system wallet when `client_id == "validator-service"`.
gRPC `SignData` reaches the same keys with **no token at all**, so that control is only as strong as
network isolation. The same is true of the whole `WalletOwnershipGate`.

**Mitigating context, stated plainly:** `wallet-service` publishes no ports in `docker-compose.yml`
(`# No ports published - internal service only, accessed via API Gateway`), so unlike the Validator's
`5801:8081` this is not internet-reachable in the reference deployment. It is reachable from every
other container on `sorcha-network`, from every pod in a cluster deployment, and from localhost under
Aspire. For a key-custody service that is a lateral-movement jackpot, not an acceptable boundary —
and it is one compose edit or one ingress rule from being worse.

**Why this is a known-and-solved problem here.** The Validator Service had exactly this defect and
fixed it. From `ValidatorGrpcAccessInterceptor`'s own XML doc:

> Before this, `MapGrpcService<ValidatorGrpcService>()` carried no authorization at all while every
> REST group in the same `Program.cs` did […] and then adds the half that was missing
> **platform-wide**.

The Wallet Service is the other half of "platform-wide", and it is the higher-value target.

**Fix.** These are not federation surfaces — every caller is an internal service. `.RequireAuthorization(
AuthorizationPolicies.RequireService)` on both `MapGrpcService` calls is the direct fix and is
stronger than the Validator's opportunistic-auth interceptor, which only needs to be that permissive
because it also serves federated peers. Separately, reconsider whether `GetDerivedKey` should exist:
returning private key material over the wire contradicts the file's own stated `FR-012` /
`SC-006` ("Root private key never exposed", "Private keys never persisted outside secure storage"),
and every in-tree consumer appears to want a signature rather than a key.

---

## W-3 (High) — org-key endpoints never bind the caller to `{orgId}`

**Where:** `src/Services/Sorcha.Wallet.Service/Endpoints/OrgKeyEndpoints.cs:30-82`

All four routes are gated `.RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")`.
That is a **role** check and a **tier** check.  `RequireAdministrator` is literally
`policy.RequireRole("SystemAdmin", "Administrator")`
(`src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs:152-153`) and never inspects
`org_id`. No handler compares the route's `{orgId}` to the caller's claim, and neither does
`OrgKeyDerivationService` — `ProvisionMasterKeyAsync` takes `organizationId` straight from the route
(`OrgKeyDerivationService.cs:59-101`).

So an Administrator of org A can operate on org B. Reachable in a browser: gateway route
`wallet-org-keys` proxies `/api/wallets/org/{**remainder}` under `RequireAuthenticated`
(`appsettings.json:438`).

**Impact:**

- `POST /api/wallets/org/{B}/master-key` — generates a 24-word BIP39 mnemonic for org B and
  **returns it in the response** (`OrgMasterKeyProvisionResult.Mnemonic`, documented "returned once
  only"). Whoever calls this owns org B's entire HD key hierarchy, permanently. The 409-on-exists
  guard means it only works against an org that has not provisioned yet — which makes it a land-grab:
  a hostile admin can pre-provision every org id they can enumerate, and orgs are created routinely.
- `POST /api/wallets/org/{B}/derive-key` — derives keys under org B's master key for an arbitrary
  `userId`. The response carries no private material (correct), but the derivation happens and a
  wallet is created under B's hierarchy.
- `POST /api/wallets/org/{B}/keys/{id}/rotate` and `DELETE /api/wallets/org/{B}/keys/{id}` — rotate or
  revoke org B's derived keys, and revocation **locks the associated wallet**. Cross-tenant denial of
  service with no precondition at all.

**This is the Tenant Service's B2+ defect, verbatim.** From `CallerOrganizationGate`'s XML doc
(`src/Services/Sorcha.Tenant.Service/Authorization/CallerOrganizationGate.cs`):

> the org-scoped groups were gated on `RequireAdministrator` + `RequirePlatformAudience` — a ROLE and
> TIER check only. `RequireAdministrator` is literally `RequireRole("SystemAdmin", "Administrator")`
> and never inspects `org_id`, and no handler compared the caller's organisation to the route. So an
> Administrator of org A could operate on org B […]
>
> Confirmed empirically before this gate was written: a plain `Administrator` of one organisation
> reached four other organisations' routes with HTTP 200.

Same review date as G1. The gate was built, applied across Tenant, and given marker metadata for a
wiring test — and the Wallet Service's org-scoped group, which is strictly higher-value than the
audit-log and custom-domain routes that motivated it, was not brought in.

**Fix.** `.RequireCallerOrganization()` on `orgKeyGroup`. The gate already probes both
`organizationId` and `orgId` route values, already lets service tokens and platform SystemAdmins
through, and already fails closed when applied to a route with no org id. It is a one-line change
plus a project reference, or a copy of the ~130-line file if the layering is undesirable.

---

## W-4 (High) — any citizen can make any wallet sign a SIWE prove-control message

**Where:** `src/Services/Sorcha.Wallet.Service/Endpoints/EthereumEndpoints.cs:21-41`

```csharp
var walletGroup = app.MapGroup("/api/v1/wallets/{walletAddress}")
    .WithTags("Ethereum")
    .RequireAuthorization("CanManageWallets");   // no .RequireWalletOwnership()

walletGroup.MapPost("/siwe/sign", SignSiwe)
```

`CanManageWallets` is "the token carries any non-empty `org_id`, or is a service token"
(`Extensions/AuthenticationExtensions.cs:34-40`). Consumer-tier citizen tokens carry `org_id` by
design (F136), so every authenticated citizen satisfies it — for every wallet. The handler
(`SignSiwe`, lines 66-97) takes no `HttpContext` and so cannot check ownership even in principle,
and `IEthereumIdentityService.SignSiweAsync` is given the route's `walletAddress` directly.

**Impact.** SIWE (EIP-4361) is a proof-of-control primitive whose entire purpose is authenticating
the holder of an Ethereum address to a relying party. The caller controls `Domain`, `Uri`, `Nonce`,
`ChainId`, `Statement`, `ExpirationTime` and `Resources`. So an attacker requests a signature over
whatever challenge a third-party relying party just issued them, and signs in as the victim's
Ethereum identity. The wallet's own comment notes the address field is overwritten server-side with
the wallet's own address — which is precisely what makes the resulting message a valid assertion
about the victim.

`GET /{walletAddress}/ethereum-address` on the same group discloses any wallet's Ethereum address —
minor on its own, useful for target selection.

`POST /api/v1/siwe/verify` is correctly scoped: it is mapped outside the wallet group under a plain
`.RequireAuthorization()` with a comment explaining that relying-party verification is not
wallet-scoped. That is right.

**Fix.** `.RequireWalletOwnership()` on `walletGroup`.

---

## W-5 (High) — any citizen can send ETH from any wallet

**Where:** `src/Services/Sorcha.Wallet.Service/Endpoints/EthereumTransactionEndpoints.cs:22-46`

```csharp
var walletGroup = app.MapGroup("/api/v1/wallets/{walletAddress}/ethereum/transactions")
    .WithTags("Ethereum")
    .RequireAuthorization("CanTransactEthereum");   // no .RequireWalletOwnership()
```

`CanTransactEthereum` is byte-for-byte the same assertion as `CanManageWallets` —
`hasOrgId || isService` (`AuthenticationExtensions.cs:58-64`). Its comment is candid about this:
*"currently the same requirement as CanManageWallets […] so it can be tightened independently"*. It
has not been tightened, and it was never an ownership check.

`SendTransfer` (lines 62-78) takes no `HttpContext`. `EthereumTransactionService.SendAsync` derives
the signing key from the supplied `walletAddress` and broadcasts
(`EthereumTransactionService.cs:63-97`). There is no ownership check at any layer.

**Impact.** Value transfer out of a wallet the caller does not own, to an attacker-chosen address.

**Current bound, and why it is not a control.** `EthereumTransactionOptions` defaults are genuinely
conservative — `EnabledChainIds = [Sepolia, Holešky]`, `AllowMainnet = false`, and a mainnet-class
chain is refused even if allow-listed. So on a default deployment the loss is testnet ETH. But:

- `MaxValueWei` (default 0.1 ETH) is **per transaction**, not cumulative, and the endpoint can be
  called repeatedly. It does not bound the drain.
- `AllowMainnet` is a documented, supported operator setting. Feature 182 exists so operators can
  transact for real. The day one does, this becomes direct theft.

An authorization defect whose only bound is a config value the feature exists to change should be
treated as the severity it will have when that value changes.

`GET /api/v1/ethereum/transactions/{chainId}/{txHash}` is correctly mapped outside the wallet group
(receipt status is public chain data).

**Fix.** `.RequireWalletOwnership()` on `walletGroup`. Separately consider a cumulative per-wallet
per-period cap, since the per-transaction cap reads as a spending limit and is not one.

---

## W-6 (Medium) — mutating HD-address routes on any wallet

Three routes in `WalletEndpoints.cs` mutate another user's wallet with no ownership check anywhere.
None of their handlers takes an `HttpContext`:

| Route | Handler | Effect |
|---|---|---|
| `POST /{address}/addresses` | `RegisterDerivedAddress` (line 1214) | Inserts a caller-supplied derived address, **public key**, and derivation path into any wallet |
| `PATCH /{address}/addresses/{id}` | `UpdateAddress` (line 1475) | Rewrites label / notes / tags / metadata |
| `POST /{address}/addresses/{id}/mark-used` | `MarkAddressAsUsed` (line 1539) | Flips used state, corrupting BIP44 gap-limit accounting |

`RegisterDerivedAddress` is the significant one. The registered address is fanned out to every
register's bloom filter via `IAddressRegistrationService.NotifyLocalAddressCreatedAsync` (lines
1245-1259), so an attacker-controlled key becomes associated with the victim's wallet in
address-discovery. It also writes an inbox entry attributed to the wallet's real owner (lines
1263-1287), so the victim is notified of a "new derived address" they did not create. The gap-limit
guard (`InvalidOperationException` containing "Gap limit") additionally gives a cheap way to wedge a
victim's address derivation.

**Why this is worth separating from W-7.** The README places these in a bucket described as needing
"an individual decision rather than a blanket sweep", alongside `encrypt` (which deliberately
encrypts *to* a wallet you do not own) and `GET /{address}` (which intentionally honours delegates).
Those two genuinely need thought. A `POST` that writes a foreign public key into someone else's
wallet does not — it is the same class as the `PATCH` / `DELETE` that G1 correctly treated as
must-fix. Mixing them meant the bucket as a whole read as deferrable.

**Fix.** `.RequireWalletOwnership()` on these three routes now; keep `encrypt` and `GET /{address}`
in the deliberate-decision bucket.

---

## W-7 (Medium) — any wallet's address graph is readable

`GET /{address}/addresses` (with filters and paging), `GET /{address}/addresses/{id}`,
`GET /{address}/accounts`, `GET /{address}/gap-status` — all on `CanManageWallets` only, none taking
an `HttpContext`. Any authenticated citizen can enumerate any wallet's full derived-address set,
labels, notes, tags, and BIP44 account structure.

Wallet addresses are public and appear on registers, so this is a practical deanonymisation tool: a
register participant's wallet address is discoverable, and this turns it into their whole address
graph plus operator-authored labels and notes. This is the disclosure half of the DAD model leaking
by omission.

Listed in the README's "not yet gated (tracked)" set. Recording it here with the impact spelled out.

---

## W-8 (Medium) — the guard test does not guard what it claims

`WalletOwnershipGate`'s XML doc:

> a guard test asserts every route carrying a wallet address in its template has it

The service README, same claim:

> `RequireWalletOwnership()` also stamps marker metadata so `WalletOwnershipWiringTests` can assert
> every wallet-scoped route carries it

The test (`tests/Sorcha.Wallet.Service.Tests/Endpoints/WalletOwnershipWiringTests.cs`) does not do
this. `GatedGroups()` is a hardcoded `TheoryData` of **two** entries — `MapCredentialEndpoints` and
`MapDelegationEndpoints` — and `MutatingPerAddressWalletRoutes_CarriesTheOwnershipGate` names two
explicit `(method, template)` pairs. Nothing enumerates the mapped endpoint set and demands the gate.
`MapEthereumEndpoints` and `MapEthereumTransactionEndpoints` both use `{walletAddress}` and would fail
immediately if added to that list; they are simply not in it.

So the control that was supposed to make "the new group forgot the gate" detectable is opt-in per
group, which makes it exactly as reliable as remembering to add the gate. W-4 and W-5 are the
predicted failure, realised.

**Fix.** Invert the test: collect endpoints from *every* `Map*Endpoints` extension the service calls,
filter to templates containing a wallet-address parameter, and require
`WalletOwnershipRequiredMetadata` on each — with an explicit, commented, shrink-only allowlist for
the deliberate exceptions (`encrypt`, `GET /{address}`, the anonymous `did-document` reader). That
inverts the default from "silently ungated" to "must be justified", and matches the ratchet pattern
already used by `.secrets-allowlist`, `.derivation-contexts-allowlist`, and
`.service-address-keys-allowlist`. A single test enumerating all groups is what would have caught
W-4, W-5, and W-6.

---

## W-9 (Low) — recovery endpoints document a feature that never runs

`POST /api/v1/wallets/recover/passkey` and `/recover/org` are feature-gated and return
`501 Not Implemented` unless `Features:WalletRecoveryEnabled` is set
(`WalletEndpoints.cs:2141`, `:2183`). **The gate is correct and the services fail closed too** —
`PasskeyRecoveryService` and `OrgRecoveryService` each throw with "Keep
Features:WalletRecoveryEnabled disabled until it is", so the flag alone cannot open the path. That is
good defence in depth and worth keeping.

The problem is the published contract. The OpenAPI metadata says:

```csharp
.WithDescription("Recovers all wallets for the authenticated user using their FIDO2 passkey. "
    + "Revokes all delegations by default; returns pending review items for selective preservation.")
.Produces<RecoveryResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound)
```

No `501`, no note that the feature is disabled. An integrator reading Scalar — the intended
discovery surface, and one this platform advertises via `/.well-known/openapi.json` — sees a working
passkey-recovery API with a documented success shape. The same applies to `/recover/org`
("Org admin recovers all wallets for a member").

For a pre-public-release polish pass this is the kind of thing that generates a support ticket on
day one.

**Fix.** Add `.Produces(StatusCodes.Status501NotImplemented)` and lead both descriptions with the
disabled state and what is missing (WebAuthn assertion verification / org recovery-key signature
verification). Consider `.ExcludeFromDescription()` until the feature lands.

---

## W-10 (Low) — one bool between 501 and unverified recovery

`Features:WalletRecoveryEnabled` appears in no `appsettings*.json` — it exists only as the two
`GetValue<bool>` reads and the service-layer throws. As noted in W-9 the service layer also fails
closed, so flipping the flag today yields a 500 rather than a recovery, which is the right outcome.

The residual risk is structural: the intended semantics of that flag are "recovery works", and the
throws are placeholders that will be removed when the crypto lands. When they are, the flag becomes
the only thing standing between a deployment and an account-takeover primitive —
`RecoverViaPasskey` accepts an unverified caller-supplied `request.PasskeyCredentialId`, and
`RecoverViaOrg` an unverified `request.OrgRecoveryKeySignature`. Nothing prevents the flag being set
in Production the way Pattern #13's storage audit fails startup on an in-memory
`IWalletRepository`.

**Fix.** Add a startup guard that refuses to boot in `Production` / `Staging` with
`Features:WalletRecoveryEnabled=true` while the verification TODOs stand, mirroring
`StorageRegistrationEnforcement`. Remove it in the same change that implements the verification.

---

## W-11 (Low) — the README's gate list contradicts itself

`README.md`, consecutive paragraphs:

> **Not yet gated (tracked):** the HD-address, accounts, gap-status and encapsulate/decapsulate routes.
> […] `sign`, `decrypt`, `decapsulate` and `GET /{address}` already verify ownership inline.

`decapsulate` is in both lists. The second is correct — `DecapsulateKey` verifies
`wallet.Owner != currentUser` inline (`WalletEndpoints.cs:2064-2070`).

Also `encapsulate` needs no gate at all: `EncapsulateKey` (line 1985) is a pure ML-KEM operation over
a caller-supplied `RecipientPublicKey` and never touches the wallet named in the route — the
`{address}` segment is used only in a log message. Listing it as an outstanding gap overstates the
remaining work and dilutes the entries that matter.

**Fix.** Drop `decapsulate` from the not-gated list; move `encapsulate` to a "deliberately ungated —
route address is unused" line; add the Ethereum groups (W-4, W-5).

---

## W-12 (Low) — `EncryptPayload` ignores the route address when the body supplies one

`WalletEndpoints.cs:1170`:

```csharp
var recipientAddress = request.RecipientAddress ?? address;
```

Not exploitable today — encryption to a public key is a deliberately unowned operation, and the
README explains why `encrypt` is intentionally not gated. It is a trap for the next person: if the
group is ever gated on `{address}`, `WalletOwnershipGate` will evaluate the route value while the
handler operates on the body value, and the gate will be silently bypassable. The gate's own design
note — that a mis-wiring must not look like a working control — applies.

**Fix.** Pick one source. Either drop `RecipientAddress` from the request and always use the route,
or drop it from the route template and mount `encrypt` outside the wallet-scoped group (it is not
wallet-scoped in any meaningful sense).

---

## W-13 (Polish) — hardcoded claim literals in `DecapsulateKey`

`WalletEndpoints.cs:2054`:

```csharp
var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
```

Every other site in the service uses `TokenClaimConstants.TokenType` / `TokenClaimConstants.TokenTypeService`
(see `WalletOwnershipGate.IsServiceToken`, `AuthenticationExtensions`, the F147 handler). A rename of
either constant leaves this comparison silently false, which fails closed here but is the same
single-source-of-truth argument CLAUDE.md §15 and §16 make for derivation contexts and error codes.

---

## W-14 (Polish) — anonymous platform-wide wallet count

`Program.cs` maps `GET /api/stats` with `.AllowAnonymous()`, returning `{ walletCount }` for the whole
node, and swallows failures into `walletCount = 0`.

Deliberate and labelled ("No authentication required"). Flagging it only so it is a conscious
sign-off before public exposure: an unauthenticated, monotonically-increasing platform-size counter
is a business-intelligence disclosure and a growth-rate oracle. The `0`-on-error fallback also means
a database outage is indistinguishable from an empty platform to any monitoring built on it.

---

## What is in good shape

Worth recording, both because it is the majority of the surface and because the fixes above should
not disturb it:

- **`WalletOwnershipGate` itself is well built.** Service-token bypass documented with its
  justification; caller identity resolved as `platform_user_id` falling back to `NameIdentifier`,
  explicitly matched to what `GetCurrentUser` stamps as `Owner`; unknown wallet is 404 for everyone
  so the response does not leak existence; `SEC-AUDIT` log on denial; fails closed when applied to a
  route with no wallet address. The design reasoning in its XML doc is the standard the rest of this
  should be held to.
- **Credentials and delegation groups are correctly gated**, including the escalation
  `GrantAccess` would otherwise have been (granting yourself `Owner` on a foreign wallet).
- **`PersonaCryptoEndpoints`** is the model internal surface: `RequireService` plus a
  `persona:crypto` scope check so a stray Blueprint or Register service token cannot reach it, strict
  rate limiting on the key-derivation path, sanitised `CryptographicException` responses that never
  echo key references, and a gateway-config guard test asserting it is unroutable from outside.
- **`PendingApplicationEndpoints`** correctly uses `RequireConsumerAudience` with a comment noting
  that plain `.RequireAuthorization()` previously let a platform token read a citizen's notice —
  exactly the F136 tier reasoning applied properly.
- **`IssuerCertKeyInternalEndpoints`** and **`CitizenStatusListInternalEndpoints`** are both
  `RequireService`, which is what makes W-1's omission stand out as an oversight rather than a
  convention.
- **The #1397 narrowing on `SignTransaction`** (`validator:*` wallets require
  `client_id == "validator-service"`) is implemented and documented — though see W-2, since gRPC
  reaches the same keys without it.
- **Recovery fails closed at two layers** (endpoint flag and service throw), which is the right shape
  for an unfinished security feature; W-9/W-10 are about the contract and the guard, not the logic.
- **The service README is candid**, including a "not yet gated (tracked)" list. Most of this review's
  medium findings are elaborations of entries already on it. W-4 and W-5 are the genuine omissions.

---

## Suggested order

1. **W-8 first.** The exhaustive wiring test with a shrink-only allowlist. It is what makes the rest
   re-findable and prevents regression; run it before and after the fixes below and the diff is the
   proof.
2. **W-1, W-2.** Both are one-line `RequireAuthorization` additions with the policy already defined.
   Highest severity, lowest effort. Re-check both against a live node afterwards — the values in
   `RateLimitPolicies` and the gateway route mean a 401 is the expected new behaviour, and the HAIP
   caller must be confirmed to present a service token.
3. **W-3, W-4, W-5.** `.RequireCallerOrganization()` and two `.RequireWalletOwnership()` calls.
4. **W-6.** Three `.RequireWalletOwnership()` calls; re-triage `encrypt` / `GET /{address}` as the
   genuinely deliberate exceptions.
5. **W-7** and the polish items as a batch, with the README corrections (W-11) in the same change.

W-1, W-3 and W-4 each warrant a check of whether the surface was reachable in any deployed
environment, since all three are browser-reachable through the gateway by an ordinary authenticated
user.

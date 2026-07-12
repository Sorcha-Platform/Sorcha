---
title: Wallet HTTP Contract Consolidation — Design Proposal
description: Collapse the five hand-copied Wallet DTO families into one immutable, source-generated contracts assembly, using the type system and assembly boundary as the enforcement mechanism.
status: proposal
last_updated: 2026-07-12
---

# Wallet HTTP Contract Consolidation

## 1. Problem

The Wallet HTTP request/response contract is hand-copied into **five** projects. There is no shared
contracts assembly; each consumer re-declares the shapes, and the copies have already drifted in ways
that are *semantic*, not cosmetic — i.e. latent correctness bugs, not just tidiness debt.

| Type | Canonical (service) | Parallel copies |
|------|---------------------|-----------------|
| `WalletDto` | `Sorcha.Wallet.Service/Models/WalletDto.cs` | UI, Demo (`WalletDetails`) |
| `WalletAddressDto` | `Sorcha.Wallet.Service/Models/WalletAddressDto.cs` | UI (byte-identical) |
| `AddressListResponse` | `Sorcha.Wallet.Service/Models/AddressListResponse.cs` | UI (identical, incl. computed `HasMore`) |
| `CreateWalletRequest` | `Sorcha.ServiceClients.Http/Wallet/Models/CreateWalletRequest.cs` (already promoted, self-labelled "consolidated") | UI, CLI |
| `CreateWalletResponse` | `Sorcha.Wallet.Service/Models/CreateWalletResponse.cs` | UI, CLI, Demo |
| `SignTransactionRequest` | `Sorcha.Wallet.Service/Models/SignTransactionRequest.cs` | UI, CLI |
| `SignTransactionResponse` | `Sorcha.Wallet.Service/Models/SignTransactionResponse.cs` | UI, CLI |

**The drift is live and load-bearing:**

- UI `WalletDto` is **missing `SigningMode` and `KmsKeyId`** that the service carries.
- UI `SignTransactionRequest` is **missing `HybridMode` and `PqcWalletAddress`** — so a hybrid/PQC
  signing request built in the web/PWA client silently cannot express those fields.
- `CreateWalletRequest` has **three different validation rules** across copies: the canonical uses a
  `[Bip39WordCount]` attribute; the UI copy substitutes an inline `[RegularExpression("^(12|15|18|21|24)$")]`
  to avoid taking the dependency; the CLI copy has no validation at all.
- CLI renames `WalletDto` → `Wallet`, drops `required`, and annotates every property with
  `[JsonPropertyName]`. Demo (`Sorcha.Demo`) is fully isolated with bespoke shapes
  (`WalletDetails`/`WalletResponse`/`SignatureResponse`) that match none of the others.

A field added service-side does not round-trip through a client until someone remembers to hand-copy
it. That is the exact class of bug that has *already happened twice* here (the missing PQC/hybrid and
`SigningMode`/`KmsKeyId` fields).

**The pattern to fix it already exists but was never finished:** `Sorcha.CitizenWallet.Abstractions`
owns its citizen-wallet DTOs canonically and has every consumer *reference* rather than re-declare.
The one Wallet type that got this treatment — `CreateWalletRequest` in `ServiceClients.Http`, whose
XML doc literally says *"Consolidated DTO shared by Wallet Service and UI"* — proves the intent. It
just never propagated to the rest of the family.

## 2. Thesis — use the type system as the enforcement mechanism

The request mentioned "code access security". A clarification that shapes the whole design: **CAS
(Code Access Security) does not exist in modern .NET** — it was a .NET Framework runtime-sandbox model,
removed in .NET Core and absent from .NET 10. There is no runtime permission grant to reach for.

The modern, and stronger, equivalent is **compile-time and assembly-boundary enforcement**: make the
type system and the serializer contract *structurally prevent* the failure modes, so a violation
doesn't compile or doesn't deserialize — rather than relying on discipline or runtime checks. The repo
targets `net10.0` / **C# 14** / `Nullable=enable` everywhere (set in the per-area `Directory.Build.props`),
so every lever below is available.

| Lever | What it buys | Security / Perf | Where it sits |
|-------|--------------|-----------------|---------------|
| `sealed record` | value equality + immutability + JIT devirtualization of `Equals`/`ToString`/`GetHashCode` | both | every canonical DTO |
| `{ get; init; }` | object is **frozen after construction** — no post-validation mutation (a request cannot be validated then altered before use) | security | every contract property |
| `required` members | **compile-time** guarantee mandatory fields are set — a `SignTransactionRequest` without `WalletAddress`/`TransactionData` does not compile | security | `Address`, `Name`, `TransactionData`, … |
| `internal` + `[InternalsVisibleTo]` | assembly-boundary access control — the *modern* substitute for CAS: only the canonical public contract crosses the seam; wire adapters / builders stay `internal` and unreachable | security | the contracts assembly |
| `JsonSerializerContext` (STJ source-gen) | no reflection at runtime → faster (de)serialize + lower startup + **trim-safe** (critical for the WASM PWA); one canonical wire format for all five consumers | both | one context in the contracts assembly |
| `[JsonConverter(JsonStringEnumConverter<T>)]` | stable, explicit string enum wire form | correctness | enums |
| `Nullable=enable` | null-ness is part of the contract, checked at compile time | security | whole assembly (inherited) |
| single FluentValidation ruleset | one validation truth, replacing the three divergent `CreateWalletRequest` rules | security / correctness | `Validators/` folder |

**The immutability → security chain, concretely.** These DTOs cross a trust boundary: bytes off the
wire are deserialized, validated, then used. A mutable `class` with `{ get; set; }` (today's posture)
permits the object to be changed *after* validation and *before* use — a TOCTOU-shaped seam inside a
single request. `sealed record` + `init` + `required` closes it: the object is fully-formed and frozen
at construction, `sealed` prevents a subtype from smuggling covert state across the serialization
boundary, and `required` makes "partially-constructed request" a compile error rather than a runtime
`null`. Validity becomes a property of *existence*, not of *discipline*.

**What we deliberately do NOT do** (naming these matters as much as the wins, to avoid cargo-culting a
"performance" checklist):

- **`readonly record struct` — not used here.** Value-type DTOs pay off only for *small* shapes (a
  few fields) that are hot and short-lived. The Wallet DTOs are large (`WalletDto` and
  `WalletAddressDto` each carry 16 properties). A 16-field struct copied by value would be *slower*
  and heavier than a heap `record`. Struct DTOs are the wrong tool for this shape; we keep reference
  `record`s.
- **Polymorphic JSON (`[JsonPolymorphic]`/`[JsonDerivedType]`) — not applied.** The `VerificationResult`
  and `IssuedCredentialResponse` families look like discriminated-union candidates but are
  *intentionally layered* per trust tier (see §7). Forcing a shared base there would couple tiers that
  are meant to diverge. The tool is available if a genuine union arises; this isn't one.

## 3. Proposed structure

A new **`src/Common/Sorcha.Wallet.Contracts`** project, mirroring the proven
`Sorcha.CitizenWallet.Abstractions` template:

- **Zero project references.** Pure contract — no transport, no IO, no Sorcha runtime deps. This is
  what makes it referenceable by every consumer with no dependency cycle (§4). Package refs limited to
  `FluentValidation` (validators) and the BCL `System.ComponentModel.DataAnnotations` (the moved
  `Bip39WordCount` attribute).
- **Packable** (`GeneratePackageOnBuild`), `InternalsVisibleTo` its test project — same as the
  precedent.
- Inherits `net10.0` / C# 14 / `Nullable=enable` automatically from `src/Common/Directory.Build.props`.

> **Naming:** `Sorcha.Wallet.Contracts` is recommended over `Sorcha.Wallet.Abstractions`. The repo
> reserves `*.Models` for *mutable model classes* (`Sorcha.Register.Models`, `Sorcha.Blueprint.Models`)
> and has exactly one `*.Abstractions` (which carries validators + an embedded JSON schema, i.e.
> behavioural seams). "Contracts" is the clearest signal for *immutable wire contracts*. Either is
> defensible; this is a low-stakes call to confirm.

```
src/Common/Sorcha.Wallet.Contracts/
  Models/            WalletDto, WalletAddressDto, AddressListResponse,
                     CreateWalletRequest, CreateWalletResponse,
                     SignTransactionRequest, SignTransactionResponse
  Validation/        Bip39WordCountAttribute (moved from ServiceClients.Http)
  Validators/        CreateWalletRequestValidator (single FluentValidation ruleset)
  Serialization/     WalletContractsJsonContext (STJ source-gen)
```

**Canonical source of truth = the service-side superset.** Adopt the shapes that carry the fields the
UI copies dropped (`SigningMode`, `KmsKeyId`, `HybridMode`, `PqcWalletAddress`) — anything less
re-ships the drift.

Illustrative canonical type (posture change from today's mutable `class`):

```csharp
namespace Sorcha.Wallet.Contracts.Models;

public sealed record SignTransactionRequest
{
    [Required] public required string WalletAddress { get; init; }
    [Required] public required string TransactionData { get; init; }
    public bool HybridMode { get; init; }
    public string? PqcWalletAddress { get; init; }
}
```

The source-generated, trim-safe serialization context (public so hosts can register it):

```csharp
namespace Sorcha.Wallet.Contracts.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WalletDto))]
[JsonSerializable(typeof(WalletAddressDto))]
[JsonSerializable(typeof(AddressListResponse))]
[JsonSerializable(typeof(CreateWalletRequest))]
[JsonSerializable(typeof(CreateWalletResponse))]
[JsonSerializable(typeof(SignTransactionRequest))]
[JsonSerializable(typeof(SignTransactionResponse))]
public partial class WalletContractsJsonContext : JsonSerializerContext;
```

Registered per host (services, PWA, CLI) so it wins over reflection:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, WalletContractsJsonContext.Default));
```

The two existing source-gen exemplars in the repo (`Validator.Service` `SnapshotJsonContext`,
`Cryptography` `HybridSignatureJsonContext`) confirm the house pattern; the difference is this one is
`public` because it is registered by multiple hosts.

## 4. Reference-graph plan (verified against the csprojs)

Because the contracts assembly depends on nothing, **no cycle is possible**. The edges:

| Consumer | Today | Change |
|----------|-------|--------|
| `Sorcha.ServiceClients.Http` | owns the half-finished canonical `CreateWalletRequest` | add ProjectReference to `Wallet.Contracts`; retire its `Wallet/Models/` copies |
| `Sorcha.Wallet.Service` | reaches `ServiceClients.Http` **transitively** via `Sorcha.ServiceClients` | gets Contracts transitively; **recommend a direct ProjectReference** to drop the "service depends on the client aggregate" smell |
| `Sorcha.UI.Components.User` | already refs `ServiceClients.Http` | retire its `Models/User/Wallet/` copies; `using Sorcha.Wallet.Contracts.Models` |
| `Sorcha.Cli` | already refs `ServiceClients.Http` | retire `Models/Wallet.cs` copies |
| `Sorcha.Demo` | refs **no** client lib (fully isolated) | **the only genuinely new reference edge** — add ProjectReference to `Wallet.Contracts`; retire bespoke shapes |

Note the existing smell this also documents: today `Wallet.Service` obtains its request DTO
(`CreateWalletRequest`) by transitively depending on the full client stack (SignalR client + Secp256k1
+ Register.Models via `ServiceClients` → `ServiceClients.Http`). A direct ref to a zero-dependency
`Wallet.Contracts` is strictly cleaner.

## 5. Rollout — clean break (pre-release, no back-compat shims)

The product is pre-release; per the standing clean-break discipline we do **not** ship compatibility
type-forwarders. Each phase builds + tests green before the next.

- **Phase 0 — stand up the assembly.** Create `Sorcha.Wallet.Contracts`; author the seven canonical
  `sealed record` types from the service-side superset; move `Bip39WordCountAttribute` (BCL-only,
  moves with zero friction); add the single `CreateWalletRequestValidator`; add `WalletContractsJsonContext`.
- **Phase 1 — ServiceClients.Http.** Reference Contracts; delete `ServiceClients.Http/Wallet/Models/`
  duplicates; fix usings.
- **Phase 2 — Wallet.Service.** Add direct ref; delete `Sorcha.Wallet.Service/Models/Wallet*.cs`
  duplicates; **audit mutation sites** (see risks) since `init`-only is a source break for any
  build-then-mutate call site.
- **Phase 3 — UI.Components.User.** Delete `Models/User/Wallet/` copies; switch usings. The UI's
  `[RegularExpression]` word-count workaround is dropped in favour of the shared `[Bip39WordCount]`.
- **Phase 4 — CLI + Demo.** Retire `Cli/Models/Wallet.cs` and Demo's bespoke shapes; Demo adds its new
  ProjectReference. Confirm the global JSON naming policy so dropping CLI's `[JsonPropertyName]`
  attributes does not change the wire (see risks).
- **Phase 5 — lock it in.** Register `WalletContractsJsonContext` in each host's `JsonSerializerOptions`;
  add a CI grep ratchet (mirroring `check-no-snackbar.ps1` / `check-trust-clean-break.ps1`) that fails
  if any Wallet contract type name is declared outside `Sorcha.Wallet.Contracts`, so the duplication
  cannot silently return.

## 6. Risks

- **Immutability is a source break, not a wire break.** `init`-only will not compile against call
  sites that build a DTO then mutate it. *Mitigation:* the compiler finds every one — grep for
  object-initializer-then-assign and `.Property =` on these types during Phase 2/3; convert to a single
  `with`-expression or full initializer. STJ serializes `sealed record` + `init` **byte-identically**
  to `class` + `get;set;`, so the wire is unchanged (guarded by contract round-trip tests).
- **JSON property naming.** The CLI copy carries explicit `[JsonPropertyName]`. Before deleting them,
  confirm the platform's shared `JsonSerializerOptions` naming policy (camelCase assumed) so the
  source-gen context's `PropertyNamingPolicy` reproduces the exact wire names. This is a verification
  task, not an assumption.
- **Source-gen must be registered or it silently no-ops.** If a host forgets to insert the context
  into its `TypeInfoResolverChain`, STJ falls back to reflection — correct output, but the trim-safety
  and perf win is lost (and the PWA could hit a trimmed-metadata failure in Release). Phase 5 makes the
  registration explicit and testable.
- **Demo is a sample, lowest priority.** If it complicates the PR, Phase 4-Demo can split into a
  follow-up; it is isolated and touches no production path.

## 7. Scope

- **IN:** the seven-type Wallet family + `Bip39WordCountAttribute` + one `JsonSerializerContext` + the
  single validator.
- **Cheap follow-on (separate PR):** the `TokenResponse` / `LoginRequest` auth pair (Tenant server +
  UI + CLI, low risk). Not bundled — keeps this PR one logical change.
- **OUT (deliberately left alone):**
  - `PagedResponse<T>` generic envelope — recurs in ~15 files but with genuinely divergent computed
    properties per endpoint; wide blast radius, low payoff.
  - `VerificationResult` (4 shapes) and `IssuedCredentialResponse` (2 shapes) — name collisions but
    intentionally layered per trust tier / different purposes; consolidating would couple tiers meant
    to diverge.
  - `CredentialStatus` (3 enums) — different value sets for different domains (display badge vs
    credential lifecycle vs passkey lifecycle); correctly separate.

## 8. Success criteria

- Exactly one declaration per Wallet contract type; CI ratchet enforces it.
- UI / CLI / Demo consume the canonical types; the previously-dropped `SigningMode`, `KmsKeyId`,
  `HybridMode`, `PqcWalletAddress` are present on every consumer.
- One validation ruleset for `CreateWalletRequest` (the divergent regex/attribute/none split is gone).
- Wallet contracts (de)serialize via the source-gen context; PWA Release publish is trim-safe.
- Every contract type is a `sealed record` with `init`/`required` and correct nullability.
- Build + full test suite green at each phase; contract round-trip tests prove the wire is unchanged.

## 9. Effort

Medium overall, low-risk once the canonical superset is fixed. Phases 0–1 ~half a day; UI/CLI ~half a
day including the mutation-site audit; Demo small; the CI gate small. The bulk of the value (killing
the already-drifting duplication and the two latent field-drift bugs) lands in Phases 0–3.

# Credential VCT / display / type decoupling

**Date:** 2026-07-15
**Status:** Approved (design)
**Surface:** `Sorcha.Blueprint.Models`, `Sorcha.Blueprint.Engine` credential issuance, the Wallet Service issue path, the Citizen Wallet PWA matcher + display, the AIAS/Assured-Identity blueprints, the verifier presets.

---

## 1. Why

On a real phone, presenting an Assured Identity credential to the PWA verifier failed with **"None of your credentials match this verifier's request for `https://sorcha.dev/vc/assured-identity/v1`"** — even though the citizen holds exactly that credential.

Root cause: **one field, `CredentialIssuanceConfig.CredentialType`, is overloaded to mean three different things** — the credential's `vct` (machine matching identity), the `type` claim, and (via `Humanize`) the human display label. The AIAS blueprint sets it to the bare name `AssuredIdentityCredential`; `CredentialIssuer.cs:45` writes that verbatim into `claims["vct"]`; but every verifier preset, DCQL test, and matcher in the "citizen wallet" world expects the SD-JWT-VC-conformant URI form `https://sorcha.dev/vc/assured-identity/v1`. The PWA matcher does exact string equality, so it never matches.

There are **two disjoint naming worlds** that never reconcile:

- **Blueprint world** — every blueprint's `credentialType` and every `CredentialRequirement.type` is a bare PascalCase name. Internally consistent (bare issues match bare requirements via `OrdinalIgnoreCase`).
- **Citizen-wallet / desk-verifier / test world** — canonical URIs (`https://sorcha.dev/vc/...`). Internally consistent.

The two only meet where a blueprint-issued credential is presented to the URI-expecting PWA — today, the Assured Identity path.

Per the SD-JWT VC profile Sorcha implements, `vct` **should be a URI / collision-resistant name**, so the URI form is the conformant target and the bare name is the off-spec side.

## 2. The fix — separate the three concerns

`CredentialIssuanceConfig` gains two optional fields; the existing `CredentialType` is demoted to a fallback + readable id.

| Concern | Field | Written to | Fallback when null |
|---|---|---|---|
| **Machine matching identity** | new `Vct` (string, optional, absolute URI) | `claims["vct"]` (the SD-JWT VC type claim — see §2.1) and the internal `CredentialEntity.Type` storage column | `CredentialType` |
| **Human display label** | new `DisplayName` (string, optional) | the credential's display carrier → PWA card name | `Humanize(vct)` (current behaviour) |
| **Short internal type name** | existing `CredentialType` (`[Required]`, unchanged) | nothing new; it is the `Vct` fallback and a human-readable blueprint id | — |

The two fallbacks remain as **defensive graceful-degradation** (a hand-authored or external config that omits `Vct` still mints *something* usable), but they are no longer a load-bearing path: **every blueprint shipped in the repo is converted to set `Vct` explicitly** (§6). After this change there is one naming world — canonical URIs — not two. The fallback only catches an authoring omission; it is not the way any real credential is issued.

### 2.1 `vct` only — drop the non-standard `type` claim

SD-JWT VC (§3.2.2.1) defines **`vct` as the sole credential-type identifier; there is no `type` claim** in the profile. Today `CredentialIssuer.cs:45` writes both `claims["vct"]` and `claims["type"]` from the same field — the `type` claim is a non-standard artefact. Since blueprints are sacrificial, we do the conformant thing: **the wire SD-JWT carries `vct` only.**

Internally, the canonical VCT must still land identically in the three places that read it, but those are Sorcha storage/logic, not wire claims:
- `CredentialVerifier.ReadCredentialType` (blueprint-side) reads the `vct` claim (its `type` fallback becomes dead — harmless, `vct` is always present).
- `CredentialMatcher` matches on the internal `CredentialEntity.Type` **storage column**, populated from the credential's `vct` at ingest.
- `PresentationEngine` (PWA) matches on `CachedCredential.MatchIdentifier` = `Vct`, sourced from the synced `CredentialEntity.Type`.

So issuance sets `claims["vct"] = Vct ?? CredentialType` and stops writing `claims["type"]`; `CredentialEntity.Type` (an internal column name we leave as-is) holds that same VCT string. The short `CredentialType` name is never emitted as a claim.

## 3. Display gets its own source (it cannot come from the URI)

`CredentialDisplay.Humanize` reads the **last** URI segment (`CredentialDisplay.cs:66`). For `https://sorcha.dev/vc/assured-identity/v1` that segment is `v1`, so the card would read **"V1"**. Display therefore must not be derived from the VCT once VCTs are URIs.

- Issuance writes `DisplayName` into the credential's display carrier: `CredentialEntity.DisplayConfigJson` / the sync wire `CachedCredentialPayload.DisplayMeta.credentialName`, surfacing as `CachedCredential.DisplayLabel`.
- `CredentialDisplay.Name` **already** prefers `DisplayLabel` over `Humanize(vct)` — no change to the display component's precedence.
- When `DisplayName` is absent, `Humanize(vct)` remains the fallback (bare-name blueprints keep their current card names).
- `DisplayName` defaults from the blueprint's `x-review` header `credentialName` **at authoring time where present** (it already carries "Assured Identity"), but the config field is the source of truth at issuance.

Concretely, the sync-out path (`EfCoreCitizenCredentialEventStream.BuildPayload`, which today emits no label) must populate `DisplayMeta.credentialName` from the stored display config, and `ISyncService.ToCachedCredential` must map it to `DisplayLabel`. These are the two spots the label is currently dropped.

## 4. Case-SENSITIVE exact matching everywhere (spec-conformant)

**Corrects an earlier draft of this design.** SD-JWT VC §3.2.2.1 requires the `vct` value to be a **case-sensitive** `StringOrURI`, and OpenID4VP DCQL matches `vct_values` by exact string. So case-sensitive exact match is the standard; a case-insensitive comparison would be **non-conformant**.

- The PWA matcher (`PresentationEngine.MatchCandidates`, `StringComparison.Ordinal`) is already conformant — **leave it case-sensitive.**
- The blueprint-side matchers (`CredentialVerifier`, `CredentialMatcher`) use `OrdinalIgnoreCase` — the deviation. Align them **to `Ordinal`** for strict conformance. Behaviourally inert given the invariant below, but it removes a non-standard leniency.
- Consistency is guaranteed **not** by a lenient compare but by the **single `VctUris` definition + conformance test** (§5): every issue and require side references one constant, so casing (and the whole path) is identical by construction. That is strictly stronger than case-folding — it also catches a wrong *path*, not just wrong case.
- Convention: **VCT URIs are lowercase kebab-case.** Not a safety net (the compare is exact) — just a house style so the single definitions are predictable.

## 5. Anti-drift — one definition per type, conformance-tested

Canonical VCT URIs currently appear as scattered string literals; only `citizen-device-delegation/v1` has a constant (`VctUris.CitizenDeviceDelegationV1`). We make `VctUris` the **single registry of every platform credential-type VCT**:

- One constant per credential type, e.g. `VctUris.AssuredIdentityV1 = "https://sorcha.dev/vc/assured-identity/v1"`, `VctUris.DrivingLicenceV1`, `VctUris.BlueBadgeV1`, … (full list in §6).
- Every C# reference (verifier presets `DefaultPresetCatalogue`, any request builder) uses the constant, never a literal.
- A **parametrised conformance test** asserts, for every converted blueprint, that its issuance `vct` (and every requirement `type` that names a platform type) equals the matching `VctUris` constant. The JSON cannot import the constant, so this test is the only thing that keeps the whole corpus from drifting — and it covers all types, not just one.

This is still the "convention + validation" decision (the VCT is declared as data in the blueprint; the constant is the C#-side anchor), just applied across the whole type catalogue rather than one type.

## 6. Full conversion — every credential type

Blueprints are sacrificial (product-owner directive, 2026-07-15): rather than convert one type and leave the rest as a latent trap, **every credential type moves to a canonical URI + `displayName` in the same change**, retiring the bare-name world entirely.

The naming rule: `https://sorcha.dev/vc/{kebab-case-type}/v1`, dropping a redundant `Credential`/`Posture` suffix (`AssuredIdentityCredential` → `assured-identity`). `displayName` is the humanised label the card should show.

| Bare name today | Canonical VCT | `displayName` |
|---|---|---|
| `AssuredIdentityCredential` | `.../assured-identity/v1` | Assured Identity |
| `DrivingLicenceCredential` | `.../driving-licence/v1` | Driving Licence |
| `BlueBadgeCredential` | `.../blue-badge/v1` | Blue Badge |
| `MembershipCredential/v1` | `.../membership/v1` | Membership |
| `LicenseCredential` | `.../licence/v1` | Licence |
| `CouncilDigitalIdCredential` | `.../council-digital-id/v1` | Council Digital ID |
| `VerifiedInvoiceCredential` | `.../verified-invoice/v1` | Verified Invoice |
| `TradeFinanceCredential` | `.../trade-finance/v1` | Trade Finance |
| `PlanningPermissionCredential` | `.../planning-permission/v1` | Planning Permission |
| `BuildingWarrantCredential` | `.../building-warrant/v1` | Building Warrant |
| `CompletionCertificateCredential` | `.../completion-certificate/v1` | Completion Certificate |
| `JobAssignmentCredential` | `.../job-assignment/v1` | Job Assignment |
| `ServiceCompletionCredential` | `.../service-completion/v1` | Service Completion |
| `ForestProductDPPCredential` | `.../forest-product-dpp/v1` | Forest Product DPP |
| `CyberEssentialsUacPosture` | `.../cyber-essentials-uac/v1` | Cyber Essentials UAC |
| `RefurbishmentCertificateCredential` | `.../refurbishment-certificate/v1` | Refurbishment Certificate |

**The lockstep invariant:** for each type, the issuer `vct` **and** every `CredentialRequirement.type` that names it move to the same URI together. The parametrised conformance test (§5) enforces this across the corpus, so a missed requirer is a test failure, not a silent runtime no-match. The definitive list of files is derived at implementation time by grepping each bare name across `demos/`, `walkthroughs/`, and `blueprints/` — every occurrence (issue or require) is converted.

Types that are only ever issued and never required cross-blueprint still get a constant + `displayName` for consistency and for future presentation.

## 7. Publish-time validation

When `Vct` is set, it must be a valid **absolute URI**. Enforced at blueprint publish (the existing validation seam that already checks issuance config), failing the publish with a clear message rather than minting an unmatchable credential. Bare-name `CredentialType` (fallback path) keeps its existing `[Required] MinLen1 MaxLen200` rule and is not URI-validated.

## 8. Existing credentials

The credential already in a wallet was minted with `vct = AssuredIdentityCredential`. Per the product owner, **re-issue is acceptable** — no migration and no matcher alias. After this ships, the citizen re-claims the credential and the new one carries the URI VCT. This is called out so it is not a surprise: the *old* credential will still fail to present until replaced.

## 9. Files touched

**Model** — `Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs` (+`Vct`, +`DisplayName`).

**Issuance** — `Sorcha.Blueprint.Engine/Credentials/CredentialIssuer.cs` (write `claims["vct"] = Vct ?? CredentialType`; **stop writing `claims["type"]`**; write `DisplayName` to display config). The Wallet Service direct-issue path (`CredentialEndpoints.cs:705-706`) mirrors the same rule. (HAIP + mdoc paths are out of scope — they don't feed the PWA verifier in the failing flow; note them in the plan as follow-ups.)

**Sync / display carrier** — `EfCoreCitizenCredentialEventStream.BuildPayload` (populate `DisplayMeta.credentialName`), `ISyncService.ToCachedCredential` (map to `DisplayLabel`).

**Matcher** — `CredentialVerifier` + `CredentialMatcher` (`OrdinalIgnoreCase` → `Ordinal`, spec conformance). `PresentationEngine.MatchCandidates` stays `Ordinal` (already conformant).

**Constants + presets** — `VctUris` (one constant per type in §6's table), `DefaultPresetCatalogue` + any C# VCT literal (reference the constants).

**Blueprints** — every blueprint under `demos/`, `walkthroughs/`, `blueprints/` that issues or requires any credential type in §6's table. The exact file set is enumerated by grep at implementation time; the conformance test guarantees none is missed.

**Publish validation** — the issuance-config validation seam.

## 10. Tests

- **Issuance unit:** a config with `Vct` set writes that URI to `vct` and `type`; with `Vct` null, writes `CredentialType` (fallback preserved).
- **Display unit:** `DisplayName` set → card name is the authored label; `DisplayName` null → `Humanize(vct)` (existing `CredentialDisplayTests` table still passes).
- **Matcher unit:** a URI-VCT credential matches a URI-VCT request under `OrdinalIgnoreCase`, including a capitalisation-mismatch case that would have failed under `Ordinal`.
- **Conformance (parametrised across the whole corpus):** for every converted blueprint, its issuance `vct` and every platform-type requirement `type` equal the matching `VctUris` constant. This single test is what proves the lockstep held for all ~16 types and no requirer was missed.
- **End-to-end intent (the regression that started this):** an Assured-Identity credential minted from the converted blueprint satisfies the `DefaultPresetCatalogue` Assured-Identity request — the exact path that shows "None match" today.
- **Publish validation:** a config with a non-URI `Vct` fails publish.

## 11. Success criteria

- **SC-1** An Assured Identity credential issued from the converted blueprint presents successfully to the PWA verifier's Assured-Identity request (the reported bug).
- **SC-2** The citizen's card still reads "Assured Identity", sourced from `DisplayName`, not from parsing the URI.
- **SC-3** Every credential type is now a canonical URI VCT with an authored `displayName`; the bare-name world is gone. The optional-field fallback survives only as defensive degradation for an omitted `Vct`.
- **SC-4** Every cross-blueprint issue↔require pair (Assured Identity ← blue-badge / driving-licence / membership, and any others) still matches after conversion, proven by the parametrised conformance test.
- **SC-5** The AIAS blueprint VCT and the verifier preset constant cannot drift without a failing test.
- **SC-6** VCT matching is case-sensitive exact everywhere (SD-JWT VC / DCQL conformant); consistency is enforced by the single `VctUris` definition + conformance test, not by lenient comparison.

## 12. Standards conformance

Checked against the SD-JWT VC profile (`draft-ietf-oauth-sd-jwt-vc`, §3.2.2.1) and OpenID4VP DCQL:

| Rule | Spec | This design |
|---|---|---|
| `vct` REQUIRED, sole type identifier | §3.2.2.1 | ✓ `vct` written on every credential; it is the only type claim |
| `vct` MUST be a case-sensitive `StringOrURI` + Collision-Resistant Name | §3.2.2.1 | ✓ canonical `https://sorcha.dev/vc/{type}/v1` URIs |
| No `type` claim in the profile | §3.2.2.1 | ✓ **fixed** — stop emitting the non-standard `claims["type"]` |
| `vct` matched case-sensitively / exactly | §3.2.2.1 + DCQL `vct_values` exact match | ✓ **corrected** — case-sensitive everywhere; earlier case-insensitive draft was non-conformant |

No standard is broken by this change; two pre-existing deviations (the `type` claim and the blueprint-side case-insensitive match) are corrected by it.

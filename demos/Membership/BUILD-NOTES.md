# BUILD-NOTES — membership/loyalty credential blueprint

Built against `master` (the `feature/membership-loyalty-blueprint` branch off the
post-#979 master). The brief (`sorcha-discount-demo-design.md`) is an external
cowork artifact and is **not in the repo**, so reconciliation is against the
pasted instructions + repo truth. Where the repo contradicted the brief, the
repo won; those deltas are recorded below.

## `[VERIFY]` flag resolutions (against current master)

| # | Flag | Resolution | Source |
|---|------|-----------|--------|
| 1 | Late binding (F103) | `isStartingAction:true` is the open flag; **VAL_BP_010** fires if a starting action's sender has a non-null `walletAddress`. `applicant` omits `walletAddress`; omitted from any wallet map (late-bound). | `blueprint-builder` skill; `ValidationEngine.cs`, `ActionExecutionService.cs` |
| 2 | Trust policy (F135) | `trustPolicy.sources[]` kinds `register \| x509-tenant \| trustlist \| did-allowlist` (kebab JSON), `combinator` (`anyOf/allOf`), `minAssuranceLevel` (`low/substantial/high`). `TrustSourceRef`: `kind`, `confersAssurance?`, `allowedIssuers?` (did-allowlist), `trustListId?` (trustlist), `options?`. **A `register` source carries NO `registerId`** — it is `{"kind":"register"}`. | `src/Common/Sorcha.Blueprint.Models/Credentials/{TrustPolicy,TrustSourceRef,TrustSourceKind}.cs` |
| 3 | Stacked cards (F107+F111) | Stacked-cards fires only when **one** action declares both `credentialRequirements` + `credentialIssuanceConfig` (+`x-review`). The Blue Badge / membership chain **splits** these across actions 1 and 3 → stacked-cards does **not** fire. (Decision: three-action split, per your answer.) | `sorcha-architecture` skill (F107 x-review) |
| 4 | presentationSource | Enum `SorchaInternal`(0,default) / `HaipExternalWallet`(1) / `SorchaWallet`(2); PascalCase JSON. Blue Badge uses **`SorchaWallet`** end-to-end — adopted here. | `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs:71-91` |
| 5 | Issuance config | `credentialType`, `claimMappings[{claimName,sourceField}]` (flat `/field` pointers), `recipientParticipantId`, `expiryDuration` (ISO-8601), `disclosable[]`, `targetAudience` (default `SorchaLocalWallet`). Optional `registerId`, `usagePolicy/maxPresentations`, `format`, `trustAnchor`, `holderKeySourceField`. Blue Badge omits `registerId`/`usagePolicy` — we do too. | `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs` |
| 6 | Core schema `$ref`s | On disk `blueprints/schemas/sorcha-core/*.json`. `$id`s: `…/core/PersonName/v1` (givenName, middleName, familyName, fullName), `…/DateOfBirth/v1` (dateOfBirth), `…/EmailAddress/v1`, `…/PostalAddress/v1`. | `CoreSchemaSeedService.cs`; on-disk files |
| 7 | Card theming (F141) | `XReviewColourTheme` has only `IdentityNavy`/`LicencePink` (`identity-navy`/`licence-pink`). A "club/loyalty" theme needs enum + `ColourThemeMap` + CSS + `IdCardLayout.razor` switch (4 touch-points). **Deferred.** | `XReviewExtension.cs:83-90`, `SchemaLayoutParser.cs:233-238` |

**F144** = the AssuredIdentity demo provisioning toolkit (`demos/AssuredIdentity/AssuredIdentityDemo.psm1`) — a PowerShell orchestrator over **existing** endpoints; it does **not** stand up a second register/service. The membership leg extends it with a new blueprint + the same org/register/publish/subscribe steps — **no new services**. The second-installation stack (if federating) is `docker-compose.federation.yml` (F138).

## Decisions (surfaced, not guessed)

1. **Credential type:** `MembershipCredential/v1` is the generic primitive (the `credentialType` string becomes the `vct` verbatim). Loyalty/discount is a **config profile**, not a distinct type. Namespace verified clear (no existing membership/loyalty/club/tier credential type — only a CLI help-text mention).
2. **Loyalty ≠ membership only by config** — the loyalty profile differs from base membership solely in `tiers` + `verifierProfile`. No blueprint difference.
3. **Trust source:** `did-allowlist`, single installation — pins the identity issuer's org DID. (Beyond the Blue Badge reference, which omits trustPolicy.)
4. **POS surface:** `Sorcha.Verifier` reference verifier (off-register, DIF PEX), pure config — not a reskinned F127 consumer page.

## Repo deltas vs the brief (repo wins)

- **D1 — OPEN_CREDENTIAL_ISSUER vs the reference.** The brief says "clone Blue Badge" **and** "OPEN_CREDENTIAL_ISSUER must not fire", but Blue Badge's identity requirement has **no `trustPolicy`** and *would* warn. Resolved by adding `trustPolicy.sources` (the CyberEssentials shape) to the membership gate — going beyond the reference. The instance is therefore lint-clean where Blue Badge is not.
- **D2 — `presentationSource` value.** The brief lists "SorchaWallet / HaipExternalWallet". The enum default is actually `SorchaInternal`; the Blue Badge journey exercises **`SorchaWallet`**, which we use. (`SorchaInternal` is the CyberEssentials internal-presentation path; `HaipExternalWallet` is the external-wallet OID4VP path.)
- **D3 — Stacked cards.** The brief asks to confirm stacked-cards fires "on the approval action", but the Blue Badge three-action split keeps `credentialRequirements` (action 1) and `credentialIssuanceConfig` (action 3) on **different** actions, so it does not fire. We kept the split (your decision) — no stacked-card review.
- **D4 — issuance config fields.** The brief's field list included `registerId`/`usagePolicy`; the Blue Badge issuance omits both (they are optional, default `Reusable`). Omitted here for parity.
- **D5 — flat vs nested identity claims.** The brief asks for credential identity fields "by `$ref` to core schemas". The blueprint mints **flat** claim names (`givenName`, `familyName`, `dateOfBirth`) because that is how F127 carries disclosed presentation claims into `claimMappings` (flat `/givenName` pointers). `schemas/membership-credential.schema.json` therefore models flat claims and references the core component `$id`s as field provenance (`$defs.coreName`/`coreDateOfBirth` + per-field descriptions) rather than nesting a `$ref`'d object — keeping the schema honest to the wire form. The action *forms* could `$ref` core components directly if a future profile collects identity (this one does not — identity comes from the presentation).
- **D6 — memberNumber/tier provenance.** Both are **issuer-assigned** at action 3 (not citizen-entered). Action 2's applicant payload is minimal (consent + optional marketing opt-in).
- **D7 — config format.** The brief offered YAML or JSON "match repo convention". Repo configs are JSON (appsettings, `demo-nodes.example.json`); JSON also avoids a YAML dependency in the PowerShell render step. Chose **JSON** (`membership.config.example.json`).

## Lint expectations (publish-time)

The rendered instance is authored to pass with **zero** warnings:

- **VAL_BP_010** — clean: `applicant` (starting-action sender) has no `walletAddress`.
- **OPEN_CREDENTIAL_ISSUER** — clean: the gate's `trustPolicy.sources` is non-empty (did-allowlist).
- **INVALID_CREDENTIAL_RECIPIENT** — clean: `recipientParticipantId: "applicant"` is a declared participant.
- **VAL_BP_011 / VAL_BP_012 / WARN_BP_006** — N/A: no `outputMapping`, no `x-credential-offer` (SorchaLocalWallet path, no HAIP claim action).
- **NO_STARTING_ACTION** — clean: action 1 `isStartingAction:true`.
- **INVALID_TITLE/DESCRIPTION / MIN_PARTICIPANTS / MIN_ACTIONS** — clean.
- Every action declares ≥1 `disclosure`; last action routes terminal `[]`.

> Live publish-lint verification requires a running Blueprint Service. It was not
> run in this session (see "Validation status" below). The above is a static
> audit against the documented validation codes.

## Validation status

- JSON validity of every `.json` artefact: **verified** (parsed clean).
- `Render-MembershipBlueprint.ps1` reproduces an equivalent lint-clean instance
  from `membership.config.example.json`: **verified** (the committed
  `instances/loyalty-discount.blueprint.json` is the canonical hand-rendered
  worked example; the script output is equivalent).
- Sample instances validate against their JSON Schemas: **verified** locally.
- **End-to-end smoke (brief build item 3)** — cold-start enrol → identity VC in
  wallet → membership gate presentation → membership VC issued → POS verify shows
  member + tier with name/DOB withheld: **NOT run this session** (needs a running
  stack; the F114/F127 walkthrough harness + `Sorcha.Verifier` desk). Runnable
  procedure documented in README.md.

## Deferred / out of scope (per brief §5 + decisions)

- **Card theming (F141)** — a `club`/`loyalty` `XReviewColourTheme` (4 touch-points). Deferred.
- **POS UI skin, harvest projector view** — demo-presentation concerns; not built.
- **Consumer/sample page** — a `samples/*` sibling Razor page composing
  `EnrolGateComponent` + `CredentialGateComponent` (mirroring
  `samples/strathcarron-portal/Pages/BlueBadge.razor`) is the integration
  boundary but is a demo-presentation concern; not built here.
- **F144 toolkit command** (`New-MembershipScheme`) — the render step emits the
  artefacts; wiring a one-command provision into `AssuredIdentityDemo.psm1` (or a
  sibling module) is the natural next step, mirroring `New-IssuingAuthority`.
- **Federation (two installations)** — single-installation chosen; the
  did-allowlist trust pin already works cross-installation if federated later.

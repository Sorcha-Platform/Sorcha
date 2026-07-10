# Membership / loyalty credential (generic primitive)

A reusable, parameterised **membership credential** blueprint for the Sorcha
system register. *Membership* is the generic primitive — a credential proving a
subject belongs to a scheme and carrying a **tier**. *Loyalty / discount* and
*payments-rate* are **configured profiles** over that primitive, not separate
credential types.

The credential proves `member: true` + `tier`. **The discount percentage is
never minted into the credential** — a verifier applies a policy to a tier
(`verifierProfile.tierDiscountMap`). That separation is what keeps one blueprint
generic across loyalty, membership, and payments-rate use cases.

The chain is cloned from the F127 credential-gated-service (Blue Badge) pattern:

```
verify-membership-identity   applicant, starting/open (late-bound)
                             credentialRequirements: SorchaWallet gate on an
                             identity credential, did-allowlist trust pin
        │
submit-membership-application applicant
                             scheme-specific gap fields only (identity is
                             carried from the presentation, not re-collected)
        │
issue-membership-credential   issuer
                             SorchaLocalWallet issuance of MembershipCredential
```

## Files

| File | Purpose |
|------|---------|
| `membership.blueprint.template.jsonc` | Generic three-action chain with `{{TOKENS}}`. |
| `instances/loyalty-discount.blueprint.json` | The template rendered for the talk's "discount" profile (lint-clean, publishable). |
| `presentations/membership-pos.presentation.json` | POS (off-register) presentation request — memberNumber + tier only. |
| `schemas/membership-credential.schema.json` | Credential claim model (memberNumber, tier, identity via core-schema provenance). |
| `schemas/submit-application.schema.json` | Action-2 applicant payload (scheme-specific gap fields). |
| `schemas/membership-pos-presentation.schema.json` | POS request/response shape. |
| `membership.config.schema.json` | Validates the deploy config. |
| `membership.config.example.json` | Worked config for the loyalty-discount profile. |
| `Render-MembershipBlueprint.ps1` | Renders a lint-clean instance + POS presentation from config. |
| `BUILD-NOTES.md` | `[VERIFY]` resolutions, repo deltas, deferred items. |

## Required config fields

See `membership.config.schema.json` for the full contract. The essentials:

- `schemeName`, `schemeSlug`, `sector`
- `issuerOrg` `{ name, walletAddress, did? }` — the wallet is baked into the
  issue action so the published blueprint has a resolvable issuer sender.
- `credentialType` — keep `MembershipCredential/vN` for the generic primitive.
- `identityCredentialType` + `requiredIdentityClaims` — the upstream credential
  the citizen presents at the gate.
- `trustPolicy` — **must have ≥1 source** (a `did-allowlist` pinning the
  identity issuer's org DID is the default) so `OPEN_CREDENTIAL_ISSUER` does not
  fire. The Blue Badge reference omits this; we add it deliberately.
- `tiers` + `defaultTier` — the parameterised tier set carried on the credential.
- `applicantFields` / `applicantRequired` — scheme-specific gap fields only.
- `disclosable` — claims made selectively-disclosable (identity + memberNumber + tier).
- `expiryDuration` — ISO-8601 (e.g. `P2Y`).
- `verifierProfile.tierDiscountMap` — **verifier-side only**, never minted.

## Render + publish

```powershell
# 1. Render a lint-clean instance blueprint + POS presentation from config.
./Render-MembershipBlueprint.ps1 -ConfigPath ./membership.config.example.json
#   -> instances/loyalty-discount.blueprint.json
#   -> presentations/loyalty-discount-pos.presentation.json

# 2. Publish the instance blueprint to the register via the normal blueprint
#    publish flow (no new endpoint). The issuer wallet is already baked in;
#    the open `applicant` participant is late-bound at runtime — do NOT add it
#    to any wallet map.
```

Provisioning a scheme onto a federated demo reuses the **F144 toolkit**
(`demos/AssuredIdentity/AssuredIdentityDemo.psm1`) pattern: an issuing-authority
org + register + published blueprint on the issuer node, and a register
subscription from the verifier/POS node. No new services are required — the
membership leg is a new blueprint + the same publish/subscribe endpoints. See
`BUILD-NOTES.md` for the exact extension point.

## Point a verifier at the scheme (POS)

POS verification is **off-register**, via the `Sorcha.Verifier` reference
verifier — pure configuration, no code change:

```csharp
await presentationRequestBuilder.CreateAsync(
    verifierOrgId,
    purpose: "Verify membership at point of sale",
    requiredVct: "MembershipCredential/v1",
    requiredClaims: new[] { "memberNumber", "tier" },
    optionalClaims: Array.Empty<string>(),
    responseBaseUri: posResponseUri);
```

Only `memberNumber`/`tier` appear in the DCQL `claims` list, so name and DOB
are **never disclosed** — the selective-disclosure hero. The
verifier then looks up the discount for the returned `tier` in its own
`verifierProfile.tierDiscountMap`.

## Profiles (same primitive, config only)

| Profile | What changes (config only) |
|---------|----------------------------|
| **Loyalty / discount** | `tiers: [standard, gold]`; `verifierProfile.tierDiscountMap: { standard: 5, gold: 15 }`. POS shows member + tier, applies discount. (Worked example: `membership.config.example.json`.) |
| **Membership (base)** | Same blueprint with no `verifierProfile` — the credential simply proves membership + tier; a verifier decides what a tier unlocks. |
| **Payments-rate** | `tiers: [bronze, silver, gold]`; the verifier maps tier → financing rate instead of discount. Same credential, same blueprint — only the verifier's tier policy differs. |

In every case the **credential is identical in shape** (`member` + `tier` +
selectively-disclosable identity); only config and the verifier's tier policy
differ. The loyalty profile must NOT differ from base membership by anything
other than config.

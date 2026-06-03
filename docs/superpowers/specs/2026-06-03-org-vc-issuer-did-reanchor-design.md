# Design: Re-anchor the org VC-issuer DID to the operational wallet (+ fail-closed issuance)

**Date:** 2026-06-03
**Status:** Approved (brainstorm) → speckit specify/plan/tasks/implement
**Author:** Stuart Fraser + Claude
**Scope:** Platform change, its own PR. The CyberEssentialsUac walkthrough fix and the `typ` media-type migration are **separate** PRs.

---

## Problem

A native Sorcha SD-JWT VC (`targetAudience: SorchaLocalWallet`, Feature 106) is signed by an org's **derived VC-issuance child wallet** (Feature 120, `KeyUsage.VCIssuance`). Today `IssuanceKeyService` anchors the issuer identity on **that derived child's address (C)**:

- `iss = did:sorcha:org:{C}`, `kid = did:sorcha:org:{C}#vc-issuance-{n}`
- the published `did.json` `id` is also `did:sorcha:org:{C}`
- `ResolveCanonicalDidAsync` (the F127 verifier `client_id`) returns `did:sorcha:org:{C}`

But the **rest of the platform** treats the org's **operational wallet (A = `Organization.WalletAddress`)** as canonical: register ownership, governance roster, register invitations, X.509 cert SAN, and trust `did-allowlist` pins all use `did:sorcha:org:{A}`. **A and C are different addresses and never match** (C is a BIP32 child of the F083 master seed B).

### Consequences (all code-confirmed)

1. **No-master-key fallback signs with the root wallet key** and emits `iss` = a **bare wallet address** (not a `did:`), no `kid`, no `jwk` → unresolvable → `TrustEvaluator: issuer signature not verified`. (This is the live CyberEssentialsUac blocker.)
2. A `did-allowlist` pinning `did:sorcha:org:{A}` does **not** match a credential whose `iss` is `did:sorcha:org:{C}` (no `alsoKnownAs` bridge) → adding a master key to an org silently breaks its own trust check.
3. The Blueprint Service verifier (`SorchaDidResolver`, 2-arg ctor, no `HttpClient`) **skips the published `did.json`** and rebuilds the doc locally from the wallet row with a **hardcoded `#vc-issuance-1`** VM → works for rotation index 1 only (latent rotation bug).
4. Standards divergence: the bare-wallet `iss` is unverifiable by any conformant SD-JWT VC verifier.

Background: memory note `org-vc-issuer-did-anchoring`; `verifiable-credentials` skill → "Org VC-Issuer Signing & DID Anchoring".

---

## Goal

A native SD-JWT VC's `iss` is the org's **canonical** `did:sorcha:org:{A}`, with the actual signing key (the derived VC-issuance child C) published as a verification method **under that DID** (`did:sorcha:org:{A}#vc-issuance-{n}`, `publicKeyJwk` = C's key, in `assertionMethod`). Verifiers resolve the **real published document**. The bare-wallet fallback is removed — issuance **fails closed** when no resolvable issuer DID can be produced.

The three changes below are **coupled and atomic**: once `iss = did:sorcha:org:{A}` but the signing key is the sub-key C, the *only* place the `A → C's key` mapping exists is the published `did.json`. The local-rebuild shortcut would resolve **A's** key and fail every signature, so the verifier-read-path fix is mandatory, not optional.

---

## Design

### Change 1 — Re-anchor (`IssuanceKeyService`, Wallet Service)

- Build `iss` and `kid` from the org's canonical `Organization.WalletAddress` (**A**), not `derivedRecord.WalletAddress` (C).
  - `iss = did:sorcha:org:{A}`, `kid = did:sorcha:org:{A}#vc-issuance-{n}`.
  - The VM's `publicKeyJwk` remains **C's** key bytes — the signing key is unchanged; only the DID *subject* moves.
- Pass **A** as `OrgDidRegenerateRequest.WalletAddress` so the published `did.json` `id` = `did:sorcha:org:{A}` and its VM ids match the emitted `kid`. (`OrgDidRegenerateRequest.WalletAddress` is already documented as "the canonical `did:sorcha:org:{addr}` identifier" — it is simply being fed the wrong address today.)
- **How the Wallet Service obtains A:** resolve `Organization.WalletAddress` by `org_id` (the `tenantId` already in scope) via a Tenant lookup. This co-locates the anchoring concern where the DID is constructed; the Wallet Service already calls Tenant (`IOrgDidDocumentClient`, device registry clients), so an additional internal GET is low-friction.
  - **Open verification (planning):** confirm `Organization.WalletAddress` is reliably populated for issuer orgs (the column is nullable). If it is not set during org/wallet provisioning, populating it (or selecting a deterministic canonical wallet) is part of this work — otherwise A is null and the anchor is undefined.

### Change 2 — Verifier reads the published doc (`SorchaDidResolver`, Blueprint Service)

- Wire the public-DID `HttpClient` ctor so the resolver **fetches the Tenant-published `did.json`** for `did:sorcha:org:{A}` instead of rebuilding from the wallet row. The existing `_publicDidHttp` fetch path (currently dead in the Blueprint host because no `HttpClient` is registered) is what gets activated.
- Required because the signing key (C) ≠ the DID-subject wallet's key (A); the published doc is the only source of the `kid → key` mapping. Incidentally fixes the rotation bug (all `#vc-issuance-{n}` resolve correctly, not just index 1).

### Change 3 — Fail-closed issuance (`CredentialEndpoints`, Wallet Service)

- Remove the bare-wallet fallback (`signingIssuer = issuanceMaterial?.IssuerDid ?? walletAddress`, null `kid`).
- When no resolvable issuer DID can be produced (no F083 master key → no vc-issuance key), **fail the mint** with an actionable error: e.g. `"Cannot issue a verifiable credential for org {orgId}: no VC-issuance key. Provision a Feature 083 org master key (Set-SorchaOrgMasterKey)."`
- Effect: turns the deep `TrustEvaluator` mystery into a clear setup error at the point of failure, and closes the standards divergence (no more unverifiable `iss`).

---

## Data flow (after)

```
issue action → Wallet mint
  → IssuanceKeyService resolves A (Organization.WalletAddress via Tenant)
  → iss/kid = did:sorcha:org:{A}#vc-issuance-{n}, sign with C's private key
  → publish did.json (id = did:sorcha:org:{A},
                      VM #vc-issuance-{n} → C's publicKeyJwk, in assertionMethod)
verify
  → DidX5cIssuerKeyResolver(iss = did:sorcha:org:{A})
  → SorchaDidResolver GETs the published did.json
  → kid-match VM → C's key → signature verifies
  → did-allowlist pinning did:sorcha:org:{A} matches ✓
```

---

## Out of scope (separate PRs)

- **CyberEssentialsUac walkthrough fix** — add `Set-SorchaOrgMasterKey` for the assessor; the allowlist already pins `did:sorcha:org:{A}`, which now matches. Walkthrough PR.
- **`typ` `vc+sd-jwt` → `dc+sd-jwt`** media-type migration (with a transition window accepting both on verify). Orthogonal.
- **X.509/EUDI external rail** (LOTL, Ed25519 X.509). Untouched.

---

## Testing

- **Unit (`IssuanceKeyService`):** emits `iss`/`kid` anchored on A with C's `publicKeyJwk`; the regenerate snapshot carries A.
- **Unit (`CredentialEndpoints`):** fail-closed — mint throws an actionable error when no vc-issuance key exists (no silent bare-wallet credential).
- **Integration (Blueprint verifier):** the resolver fetches a published `did.json` and verifies a credential signed by C under `did:sorcha:org:{A}`; a `did-allowlist` pinning A passes; a rotated `#vc-issuance-2` key resolves.
- **Regression:** the CyberEssentialsUac shape (assessor with master key, allowlist on A) verifies end-to-end.
- **Clean-break gate** (`scripts/check-trust-clean-break.ps1`) stays green.

---

## Decisions (locked in brainstorm)

- **Anchor target:** `Organization.WalletAddress` (A) — the canonical operational wallet, consistent with invitations / X.509 SAN / roster / allowlists. (Not the action-sender wallet; not the `orgId` GUID.)
- **Backward-compat:** **clean break** — pre-production, dev data wiped and regenerated. No `alsoKnownAs` bridge, no migration of already-issued credentials.
- **PR scope:** re-anchor **+ fail-closed issuance**. `typ` migration and the walkthrough fix are separate PRs.

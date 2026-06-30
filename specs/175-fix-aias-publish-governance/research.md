# Phase 0 Research: AIAS Demo Publish-Governance Gap

All Technical Context unknowns are resolved below. There were no open `NEEDS CLARIFICATION` markers; the research instead grounds the chosen approach in the actual codebase.

## Decision 1 — Root cause of the 403

**Decision**: The 403 is a register-ownership/governance mismatch, not a token or rehearsal problem.

**Rationale**: The AIAS register is created owned by the sysadmin (docker) bootstrap account, but `POST /api/blueprints/{id}/publish` is driven by the AIAS verification-admin (issuer) wallet. The F142 PublishGate enforces a **hard** governance check: the caller's `wallet_address` claim must match an Owner/Admin/Designer entry on the register's roster. The publishing wallet is absent from that roster, so the gate returns `403 "caller lacks a publish-governance role on register"`. The ~90s participant-publish seal timeout and the public-org auto-subscribe 500 are downstream symptoms of the same missing wallet↔register relationship.

**Alternatives considered**:
- *Rehearsal soft-gate bypass* — rejected. `Publish-SorchaBlueprint` already defaults `-OverrideRehearsal $true`; the failure is the **hard** governance gate, which override does not (and should not) bypass.
- *Re-login to refresh the token* — necessary but insufficient on its own: a fresh token carries `wallet_address`, but if the register is owned by a different wallet the roster still won't match.

## Decision 2 — Confer governance by owning the register with the issuer wallet (Pattern A)

**Decision**: Create the AIAS register owned by the verification-admin (issuer) wallet — `-OwnerUserId <vAdmin.UserId> -OwnerWalletAddress <vWallet.Address>` — mirroring AssuredIdentity. This is FR-002 approach (a), the spec's preferred path.

**Rationale**: Conferring register ownership on the publishing wallet satisfies the PublishGate (ownership ⇒ publish-governance authority), as demonstrated by `demos/AssuredIdentity/AssuredIdentityDemo.psm1:171` and the subsequent publish at `:186`. It is the minimal, established pattern and keeps governance correct for participant publish (FR-004) and public-org subscription (FR-005) as a side effect.

**Alternatives considered**:
- *FR-002 approach (b): grant a publish-governance role on a sysadmin-owned register before publish* — viable fallback, but adds a separate role-grant step and leaves ownership split between two identities. Rejected as non-preferred per FR-002 and spec edge case "Two governance approaches are acceptable."

## Decision 3 — No shared-helper change required

**Decision**: Consume `New-SorchaRegister` as-is; do not modify `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1`.

**Rationale**: `New-SorchaRegister` (`SorchaWalkthrough.psm1:1184`) already accepts `-OwnerUserId`, `-OwnerWalletAddress`, and an optional `-WalletSignerHeaders` for the wallet-owner sign context (defaulting to `-Headers`). The ownership attestation is signed at `:1313-1337` with `-WalletSignerHeaders`. This fully expresses "register owned by the issuer wallet, attestation signed by that wallet's owner" without code change — satisfying FR-007 and protecting FR-009 (no shared-helper regression for AssuredIdentity / Membership / ForestryCertification / TradeFinance).

**Alternatives considered**:
- *Extend the helper* — rejected; the required shape already exists. Touching it would put the FR-009 non-regression burden on the change for no benefit.

## Decision 4 — Fresh verification-admin login before publish

**Decision**: Mint a fresh verification-admin session (`Connect-SorchaUser`) immediately before the publish/participant steps, after the issuer wallet is linked, so the JWT carries the `wallet_address` claim.

**Rationale**: TokenService injects `wallet_address` from the user's first active linked wallet at login time. A token minted before the wallet link lacks the claim and fails the PublishGate even when ownership is correct. TradeFinance does exactly this fresh-inline-login before publish (`walkthroughs/TradeFinance/setup.ps1:488-499`). This directly supports FR-003.

**Alternatives considered**:
- *Reuse an earlier session token* — rejected; risks a stale token without the `wallet_address` claim.

## Decision 5 — Preserve idempotency via register reuse by name

**Decision**: Keep using the shared register-by-name reuse path (`Get-SorchaRegisterByName`) and ensure a reused register's ownership is the issuer wallet, not the sysadmin account.

**Rationale**: FR-008 / User Story 3 require safe re-runs. The shared helpers already reuse registers by name; the fix must not leave a reused register owned by the wrong identity such that publish 403s again. Verification re-runs the demo twice (SC: idempotent authority-ready state).

**Alternatives considered**:
- *Always create a new register* — rejected; regresses the established idempotent developer loop and risks duplicate/conflicting registers.

## Open Items

None. The AIAS demo assets are not present in this working tree (spec Assumptions); the implementation authors them against the AIAS surface using AssuredIdentity as the established reference. This is captured in the plan's Structure Decision and does not block planning.

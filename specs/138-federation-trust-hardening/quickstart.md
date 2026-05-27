# Quickstart: Validating Federation Trust Hardening (Feature 138)

**Date**: 2026-05-24

Each user story is validated **adversarially**: stand up the attack, confirm it is rejected; then confirm the honest path still works. Every check maps to a Success Criterion. This is the red-team acceptance bar (FR-021 / SC-008): *show the forged/unsigned/replayed variant being rejected.*

## Prerequisites
```bash
docker-compose up -d        # full stack
# or: dotnet run --project src/Apps/Sorcha.AppHost   # Aspire (HTTPS 7xxx)
dotnet test                 # all negative tests must pass
```

## US1 — Revocation cannot be forged  → SC-001, SC-002
1. **Forged list**: Serve a status list with a revoked credential's bit cleared, signed with a key NOT in the issuer's sealed DID document (or unsigned). Attempt to verify the credential.
   - ✅ Expect: **rejected**; `sorcha_statuslist_rejected_total{reason="signature"}` increments.
2. **Issuer mismatch**: Serve a validly self-signed list whose `iss` ≠ the credential's org DID.
   - ✅ Expect: **rejected** (`reason="issuer"`).
3. **Fail-closed**: Block the status-list fetch entirely.
   - ✅ Expect: verification **fails closed** (credential unverifiable), no stale-cache serve; `reason="fetch"`.
4. **Honest path**: Genuine signed, fresh list marking the credential revoked.
   - ✅ Expect: verification fails *because revoked* (not because unverifiable). A non-revoked credential with a genuine list verifies.

## US2 — Peer identity & claims are provable  → SC-003
1. **Identity forgery**: Call `RegisterPeer` claiming a `peer_id` without a valid signature over the issued challenge.
   - ✅ Expect: **refused**; `sorcha_peer_registration_rejected_total{reason="signature"}`.
2. **Register spoofing**: From a registered node, send an advertisement for a register it does not hold, unsigned (or signed by the wrong key).
   - ✅ Expect: **dropped, not propagated** (`sorcha_peer_message_rejected_total{reason="unsigned_ad"|"bad_signature"}`).
3. **Replay**: Capture a valid heartbeat; resend it (stale sequence/timestamp).
   - ✅ Expect: **rejected** (`reason="replay_seq"`).
4. **Transport**: In a Production/Staging profile, attempt a cleartext peer connection.
   - ✅ Expect: **refused** (mTLS required). In Development, cleartext still works.
5. **Honest path**: Legitimate node restarts and re-registers under its existing identity key → succeeds.

## US3 — Sealed-roster vote authority  → SC-004, SC-005
1. **Out-of-roster vote**: Submit a consensus vote signed by a key absent from the sealed `RegisterControlRecord.Validators`.
   - ✅ Expect: **zero quorum weight on every node**, deterministically; `sorcha_validator_vote_rejected_total{reason="not_in_sealed_roster"}`.
2. **Default admission**: Create a register with no admission policy.
   - ✅ Expect: defaults to **Consent**; a self-registering validator is `Pending`, casts no counting vote until approved in the sealed roster.
3. **Equivocation**: Make a validator sign two conflicting states for one slot.
   - ✅ Expect: **automatic** `control.validator.eject` sealed; entry → `Ejected`; identical on every honest node; **zero operator actions**; `sorcha_validator_ejected_total{reason="equivocation"}`.
4. **Withholding**: A validator accepts work but never seals it.
   - ✅ Expect: after `LivenessTimeoutSeconds`, a sealed `control.validator.liveness-violation` ejects it automatically.

## US4 — Verified blueprint recovery  → SC-006
1. **Tampered blueprint**: Offer a recovery path a blueprint whose content ≠ the sealed `ContentHash`.
   - ✅ Expect: **rejected, not stored**; `sorcha_blueprint_recovery_rejected_total{reason="hash_mismatch"}`.
2. **No provenance**: Offer a blueprint with no sealed digest available.
   - ✅ Expect: **not stored** (`reason="no_provenance"`).
3. **Honest path**: Correctly-sealed blueprint matching its on-chain digest → accepted and stored.

## US5 — Presentation replay hardening  → SC-007
1. **Expired proof replay**: Capture a valid KB-JWT; replay it after its `exp` (but while the session is still open).
   - ✅ Expect: **rejected** (`sorcha_presentation_replay_rejected_total{reason="kbjwt_expired"}`).
2. **Missing exp**: Present a KB-JWT with no `exp`.
   - ✅ Expect: **rejected** (`reason="kbjwt_missing_exp"`).
3. **Mid-session revocation**: Revoke the device credential after session open; verify a still-fresh proof.
   - ✅ Expect: **rejected** (`reason="revoked_at_verify"`) — revocation re-checked at verify time + US1 fail-closed.

## US6 — Open-participant key binding  → (US6 acceptance)
1. **Key hijack**: Submit into an open, unpublished participant slot with a carried delivery key not bound to any invitation/pre-registration.
   - ✅ Expect: **rejected** (`sorcha_carried_key_rejected_total{reason="unbound"}`).
2. **Honest path**: Carried key bound to a valid, unconsumed invitation (commitment matches the invitation nonce) → accepted. A *published* participant's key path is unaffected.

## Whole-feature gate  → SC-008
- `dotnet test` green, including one negative test per forged/unsigned/replayed/out-of-roster variant above.
- A reviewer can trace each ✅ to a signature-verification step backed by a negative test; **zero "trust the sender/transport" paths remain** in the affected surfaces.

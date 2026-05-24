# Contract: Configuration Keys & Observability Metrics

All thresholds are configurable with secure defaults (spec Assumptions). All security rejections are observable (FR-022).

## Configuration keys

| Key | Default | Applies to | Meaning |
|-----|---------|-----------|---------|
| `Verifier:ClockSkewSeconds` | 60 | US5, US1, US2 | Wall-clock tolerance for KB-JWT exp, delegation exp, status-list freshness, heartbeat timestamp |
| `Verifier:KbJwtMaxLifetimeSeconds` | 120 | US5 | Upper bound enforced on accepted KB-JWT `exp − iat` (reject over-long-lived proofs) |
| `IssuerSignature:Required` (existing) | `true` in prod | US1 | Already defaults true (F120); status-list path now honors the same fail-closed posture |
| `PeerService:EnableTls` | `true` outside Development | US2 | Production/Staging refuse cleartext; Development may run cleartext HTTP/2 |
| `PeerService:ChallengeTtlSeconds` | 30 | US2 | Lifetime of a registration challenge nonce |
| `RateLimiting:*` (existing `RateLimitSettings`) | relaxed pre-release | US2 | Drives the new gRPC `RateLimitInterceptor` |
| `RegisterPolicy` default `RegistrationMode` | **`Consent`** (was `Public`) | US3 | New-register validator-admission default |
| `Consensus:LivenessTimeoutSeconds` (from policy `DocketTimeoutSeconds`) | 30 | US3 | Accept-to-seal deadline before a liveness-violation proof |

## OpenTelemetry metrics (new counters on existing `Sorcha.*` meters)

| Metric | Meter | Tags | Increment when |
|--------|-------|------|----------------|
| `sorcha_statuslist_rejected_total` | `Sorcha.Verifier` | `reason{signature\|issuer\|unresolved\|expired\|fetch}` | US1 status-list rejected |
| `sorcha_presentation_replay_rejected_total` | `Sorcha.Verifier` | `reason{kbjwt_expired\|kbjwt_missing_exp\|revoked_at_verify}` | US5 / US1 verify-time rejection |
| `sorcha_peer_registration_rejected_total` | `Sorcha.Peer` | `reason{signature\|id_mismatch\|stale\|challenge\|rate_limited}` | US2 registration refused |
| `sorcha_peer_message_rejected_total` | `Sorcha.Peer` | `reason{replay_seq\|stale_timestamp\|unsigned_ad\|bad_signature}` | US2 message rejected |
| `sorcha_validator_vote_rejected_total` | `Sorcha.Validator` | `reason{not_in_sealed_roster\|bad_signature\|double_vote}` | US3 vote rejected |
| `sorcha_validator_ejected_total` | `Sorcha.Validator` | `reason{equivocation\|liveness_timeout}` | US3 automatic ejection sealed |
| `sorcha_blueprint_recovery_rejected_total` | `Sorcha.Blueprint` | `reason{hash_mismatch\|no_provenance}` | US4 recovery rejected |
| `sorcha_carried_key_rejected_total` | `Sorcha.Blueprint` | `reason{unbound\|commitment_mismatch}` | US6 open-participant key rejected |

## Health degradation signals

A node that falls back to a weaker posture (e.g., status-list unverifiable so failing closed, or peer transport unable to establish mTLS) surfaces `Degraded` on the relevant health check, consistent with the Storage Registration Log pattern (CLAUDE.md §10/§11), so operators see the condition without it silently weakening security.

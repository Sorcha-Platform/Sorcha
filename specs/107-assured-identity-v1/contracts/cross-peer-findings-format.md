# Contract: Cross-peer smoke test findings document

**Feature**: 107-assured-identity-v1
**Produced by**: `walkthroughs/AssuredIdentity/run-multi-peer.ps1`
**Location**: `walkthroughs/AssuredIdentity/multi-peer-findings.md` (committed baseline) + per-run rolling files (gitignored)

## Purpose

The smoke test produces a structured markdown document on every run regardless of outcome. The document is the only output that matters — pass / fail / anomaly are all equally informative.

## Document shape

```markdown
---
run_timestamp: 2026-04-22T14:30:00Z
peer_a_version: <git sha or version tag of peer A's image>
peer_b_version: <git sha or version tag of peer B's image>
outcome: pass | degraded-pass | fail | env-failure
total_duration_ms: 23847
---

# Cross-peer smoke test — <date>

## Summary

<one or two sentences: what happened, what to take away>

## Topology

- Peer A: <hostname / container name> · <role: issuer>
- Peer B: <hostname / container name> · <role: holder>
- Shared register: <register id>
- Government org DID: <did:sorcha:org:...>
- Citizen wallet: <wallet address>

## Timings

| Milestone | Wall-clock (ms from start) |
|---|---|
| `setup.ps1` complete | 0 |
| Phase 1 submit on peer A | <ms> |
| Assessor approval (agent) on peer A | <ms> |
| Credential minted, sealed disclosure on register | <ms> |
| `InboundCredentialDetector` fires on peer B | <ms> |
| MyCredentials PENDING surfaces on peer B | <ms> |
| Holder Accept transaction signed on peer B | <ms> |
| Accept transaction observed on peer A | <ms> |
| Total | <ms> |

## Anomalies

<one entry per observed anomaly; bullet list when none>

- [SEVERITY] <description> — <reproduction note>

Severities: BLOCKER (test failed) · MAJOR (degraded pass) · MINOR (worth investigating later) · INFO (observation)

## Reproduction notes

- Commands run, in order
- Environment variables / config that mattered
- Any manual intervention performed
- Versions of any external dependencies (Docker, PowerShell, .NET SDK)

## Outcome rationale

<for fail / env-failure outcomes: paragraph explaining why the outcome was assigned>
```

## Outcome semantics

| Outcome | Meaning | Blocks release? |
|---|---|---|
| `pass` | All milestones hit within targeted latency, no anomalies | No (it passed) |
| `degraded-pass` | All milestones eventually hit but at least one exceeded latency budget | No — flag for follow-up |
| `fail` | A required milestone did not occur (e.g., credential never reached peer B) | **No** — feature ships, fix routed to peer-replication owner |
| `env-failure` | Test could not run (docker-compose failure, port conflict, etc.) | No — flag for environment fix |

The smoke test is **measurement-not-gating** by design (per spec assumption). Failures become findings.

## Latency targets

Per spec SC-009: full delivery (Phase 1 submit → MyCredentials PENDING on peer B) MUST complete in **≤ 30 seconds** under normal conditions to count as `pass`. Up to 60 seconds counts as `degraded-pass`. Above 60 seconds OR not arriving counts as `fail`.

## File handling

- **Committed baseline**: `walkthroughs/AssuredIdentity/multi-peer-findings.md` — the latest known-good representative run, committed to the feature branch and updated on each release that runs the smoke test
- **Rolling per-run**: `walkthroughs/AssuredIdentity/multi-peer-findings/<run_timestamp>.md` — gitignored, useful for local debugging

## Acceptance

- A run of `run-multi-peer.ps1` produces a findings document with frontmatter and all sections populated, regardless of outcome
- The committed baseline reflects the latest run on a release-cut commit
- The `outcome` value is computable from the timings (no human judgement needed for pass / degraded-pass; fail and env-failure require explicit script-detected error conditions)

# Phase 0 Research — Notifications & Realtime Architecture

**Date**: 2026-05-05
**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Design**: `docs/superpowers/specs/2026-05-05-notifications-architecture-design.md`

The brainstorm and spec resolved most architectural questions; this document captures the remaining technology and pattern choices, plus the small set of "we picked A over B" decisions that reviewers will reasonably ask about.

---

## R-001 — SignalR Redis backplane package and configuration shape

**Decision**: `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (Microsoft-maintained, current major version aligned with .NET 10) wired in `AddSorchaHub` via `services.AddSignalR().AddStackExchangeRedis(connectionString, options => options.Configuration.ChannelPrefix = new RedisChannel("sorcha:signalr:{serviceShortName}", PatternMode.Literal))`. Connection string resolved through the existing `SorchaConnectionsExtensions.GetSorchaRedisConnectionString()` cascade.

**Rationale**: The package is the canonical SignalR backplane. Versioning aligns with the Aspire 13 stack already in tree. Per-service `ChannelPrefix` isolates pub/sub keyspaces so a busy WalletHub fan-out does not flood TenantHub replicas with events they will discard. The cascade resolution matches the storage-clients pattern shipped in Feature 113 — operators set one `ConnectionStrings:Sorcha:Redis` value and every service inherits it.

**Alternatives considered**:
- *Custom backplane via Redis pub/sub directly.* Rejected — re-implements the framework, no upside.
- *Azure SignalR Service.* Rejected for now — managed service, costs money, locks deployment topology, not needed for our scale. Worth revisiting at production scale; the abstraction this spec lands does not preclude it.
- *Skip channel prefix.* Rejected — per `Complexity Tracking` in plan.md, shared prefix produces unbounded cross-service fan-out.

---

## R-002 — Redis sorted-set as unread-count index

**Decision**: Per-user unread count tracked in a Redis sorted set keyed `sorcha:tenant:inbox:unread:{platformUserId:N}`, scored by entry `OccurredAt` epoch ms, member is the entry GUID. Count read via `ZCARD`; increment is `ZADD`; decrement is `ZREM`. Reuses `IAtomicDistributedCache` from Feature 113 for atomic operations where they cross more than one key.

**Rationale**: O(1) cardinality reads with `ZCARD`. The sorted-set order doubles as a recency index for "show me my last 20 unread" queries without hitting Postgres. Score = epoch ms gives natural retention windowing if we GC by age later. Reuses the audited atomic-cache infrastructure rather than introducing a new pattern.

**Alternatives considered**:
- *`SELECT COUNT(*) WHERE PlatformUserId = X AND ReadAt IS NULL`.* Rejected for hot path — fine at low entry counts, breaches NFR-003 at 10⁵ entries per user. Postgres remains the source of truth and a fallback path; Redis is the read-time accelerator.
- *Hash key per user.* Rejected — cardinality query would require iterating, defeats the purpose.
- *Postgres-only with materialized view.* Rejected — refresh latency is unbounded, conflicts with NFR-002.

**Failure mode**: Redis index unavailable → count read falls back to Postgres `COUNT(*)`. Logged as `Degraded` in storage-providers health check. Production fail-fast covers Redis-missing-at-boot via the storage registration log.

---

## R-003 — Reconnect jitter algorithm for `SorchaHubConnectionBuilder`

**Decision**: Existing reconnect schedule `[0, 2, 5, 10, 30s]` plus subsequent `30s`-forever entries gains a per-step ±20 % uniform jitter applied at each retry, computed as `delay * (1 + random.NextDouble() * 0.4 - 0.2)`. Floor at 100 ms.

**Rationale**: Standard guidance from AWS Architecture Blog ("Exponential Backoff and Jitter") and Google SRE Book. ±20 % is the smallest jitter that meaningfully spreads a thundering-herd reconnect after a deploy without making the reconnect feel laggy in normal operation. 100 ms floor prevents arithmetic from producing 0 ms or negative.

**Alternatives considered**:
- *Full jitter (`delay = random.NextDouble() * delay`).* Rejected — averages 50 % of nominal delay; users would perceive reconnect as slower than they should.
- *Equal jitter (`delay/2 + random * delay/2`).* Rejected — same averaged effect, more arithmetic. No advantage over ±20 %.
- *No jitter.* Status quo. Rejected because the spec explicitly calls out thundering-herd risk after deploys.

**Test surface**: Unit test asserts that, given a fixed seed and the standard schedule, 1000 simulated reconnect attempts spread over the expected jitter band with ≥ 95 % within ±25 % of nominal.

---

## R-004 — Inbox-write transport: HTTP vs Redis event

**Decision**: HTTP. Services emitting inbox-worthy events POST to `https://tenant/api/internal/inbox` over HTTP, gated by the `RequireService` policy and the existing `ServiceAuthClient` token flow.

**Rationale**: Inbox writes are low-volume and require reliable delivery and idempotency on `(PlatformUserId, SourceEventId)`. HTTP gives synchronous confirmation and a clean retry model. The existing service-to-service auth path is well-understood. Redis pub/sub would be at-most-once, which is exactly the failure mode this spec exists to remove from the user-facing path.

**Alternatives considered**:
- *Redis stream (XADD with consumer group).* Rejected for v1 — adds a new infrastructure pattern and a new consumer in Tenant Service. Worth revisiting if write volume grows or if we want write-side decoupling. The HTTP API contract does not preclude swapping the transport later.
- *Direct EF Core write from emitter service to a shared Inbox table.* Rejected — violates microservices-first; cross-service DB writes are outside the constitution.

**Performance check**: Average measured latency for n1 service-to-service HTTP under nominal load is < 30 ms. Inbox-write to `InboxEntryAdded` is HTTP-write + DB-write + Redis-INCR + hub-publish, target NFR-002 of 300 ms p95 has comfortable headroom.

---

## R-005 — `HubSignal` envelope shape and serialization

**Decision**:

```csharp
public sealed record HubSignal(
    string EventType,
    IReadOnlyList<string> Ids,
    DateTimeOffset OccurredAt,
    string TraceId);
```

Serialized as JSON via `System.Text.Json` with the default snake-cased property naming policy for SignalR (matches the existing convention in `EventsHubNotificationBridge`). Hub event method signatures take individual typed parameters (e.g. `Task ActionAvailable(string instanceId, string actionId, DateTimeOffset occurredAt, string traceId)`); the `HubSignal` record is the conceptual envelope, not the wire shape — the wire is the SignalR-method-call argument list.

**Rationale**: SignalR clients consume typed method parameters cleanly. A wrapper record on the wire would obscure parameter names and lose IntelliSense in the typed client interface. The conceptual envelope (record + contract rule) gives the design coherence without forcing client-side unwrapping.

**Alternatives considered**:
- *Wrap every event in a literal `HubSignal` argument.* Rejected — uglier client code, opaque to debuggers.
- *MessagePack for compactness.* Rejected — JSON is in tree, debuggable, and the 200-byte p99 target is comfortably hit with JSON.

---

## R-006 — Correlation key shape

**Decision**: Format `{kind}:{primary-id}[:{secondary-id}...]`. Concrete kinds:
- `tx:{walletAddress}:{txId}` — peer-replicated transaction
- `membership:{orgId}` — org membership change
- `security:{userId}:{eventId}` — security alert
- `system:{messageId}` — system announcement
- `workflow:{instanceId}` — workflow lifecycle

**Rationale**: Hierarchical kind prefix lets the inbox UI group by kind for visual treatment (icons, colour). Primary ID is sortable for replay scenarios. Multi-id keys allow cross-domain correlation (e.g., a membership change triggered by a security event can carry both `userId` and `eventId`). Format-as-string keeps the key opaque to consumers; no implicit parsing logic in the UI.

**Alternatives considered**:
- *GUID correlation key.* Rejected — opaque GUIDs make debugging harder and lose semantic grouping. A string key is debuggable in logs.
- *Structured object.* Rejected — Postgres column type would be JSONB; index lookup slower than string equality with a composite index.

**Index**: Postgres composite `(PlatformUserId, CorrelationKey, OccurredAt)` covers the "find sibling entries within 30s" query.

---

## R-007 — UI grouping window for correlated entries

**Decision**: 30 seconds, hard-coded. Grouping happens client-side in the inbox UI when entries with the same `CorrelationKey` arrive within 30s of each other (measured by `OccurredAt`).

**Rationale**: Empirical — peer-replicated transaction cascades typically resolve within 1–5 s. A 30 s window covers normal latency variance and realistic re-attempt scenarios while keeping unrelated occurrences (e.g., two different actions for the same user) from accidentally grouping. The window is a UI affordance, not a data-model constraint, so it can be tuned by changing the UI constant without a migration.

**Alternatives considered**:
- *5 s window.* Rejected — too tight; backplane delay + write delay can exceed it.
- *5 minutes.* Rejected — would group genuinely unrelated entries.
- *Configurable per user.* Rejected — adds complexity for marginal gain. Phase 5 preferences could revisit.

---

## R-008 — Polling fallback cadence

**Decision**: 15 seconds default with ±20 % jitter, configurable per `HubConnectionWithFallback<TClient>` instantiation. Engages after the SignalR reconnect window exhausts (i.e. after the 30 s entry in the reconnect schedule fires three times without success ≈ 90 s effective). Fires synchronously on first activation, then on the cadence.

**Rationale**: 15 s is the cadence already used in `EncryptionProgressIndicator` today and feels right empirically — fast enough to feel live, slow enough not to hammer the gateway. Engaging only after 90 s of failed reconnect avoids polling during transient network blips. Synchronous first-fire bridges the perceived gap in data freshness.

**Alternatives considered**:
- *5 s polling.* Rejected — too aggressive; spec NFR-005 budgets ≤ 1 KB/s/connection p95, and 5 s polling N concurrent users could blow that.
- *Engage immediately on disconnect.* Rejected — would polling-storm during transient blips and beat the auto-reconnect to the punch.
- *Long-poll instead of polling.* Rejected — adds another connection pattern; the spec optimises for clarity.

---

## R-009 — Migration deprecation window length

**Decision**: One release cycle (defined as: from the release that ships the new behaviour to the next release at minimum, assessed via the `sorcha_signalr_*` metrics). Concretely two weeks at current cadence. Aliases (`/actionshub` → `/hubs/blueprint`) and parallel-fire (EventsHub vs new homes) both observe this window.

**Rationale**: Long enough that customers running n1.sorcha.dev replicas with delayed deploys catch up. Short enough that we don't carry deprecated code longer than necessary. The metric-driven decommission (FR-038) makes the cutoff observable rather than calendar-based — if the gauge stays at zero, the alias removes; if it doesn't, the window extends.

**Alternatives considered**:
- *Calendar-only window.* Rejected — doesn't account for slow-deploy customers.
- *Indefinite alias.* Rejected — accumulates technical debt.

---

## R-010 — RegisterHub `[Authorize]` cutover sequencing

**Decision**: Two-step. Step 1: ship the UI's `RegisterHubConnection` change to start passing the JWT, but keep the server-side hub permissive (no `[Authorize]`). Step 2 (next release): add `[Authorize]` to RegisterHub. Between releases, `sorcha_signalr_connections_total{hub="register",authenticated="false"}` is monitored; if non-zero connections persist past step 1 deploy, step 2 is delayed.

**Rationale**: The flag day is unavoidable (any client without a token must lose its connection at some point). The split-release shipping order ensures all UI sessions have token support before the server stops accepting unauthenticated. The metric makes "are we ready for step 2?" answerable.

**Alternatives considered**:
- *Single-release flag day.* Rejected — would break in-flight UI sessions on deploy.
- *Permanent dual-mode.* Rejected — leaves the unauth hole open indefinitely.

---

## R-011 — Test fixture for cross-replica multi-node verification

**Decision**: `tests/Sorcha.Integration.Tests/MultiNode/HubBackplaneCrossReplicaTests.cs` using a Docker-Compose fixture (`docker-compose.multinode.yml`) that brings up Postgres + Redis + two replicas of one service (Blueprint or Tenant — parameterized). Tests use raw `HubConnectionBuilder` (not `SorchaHubConnectionBuilder`) to control which replica each connects to via explicit YARP routing headers.

**Rationale**: Multi-node correctness is the headline requirement; without a fixture that actually runs two replicas, we cannot verify it. Docker-Compose is the existing pattern for E2E tests in the repo (cf. `docker-compose.federation.yml` from spec 106). Two replicas is sufficient to exercise the bug; more replicas don't add coverage.

**Alternatives considered**:
- *Mock the backplane in-process.* Rejected — would test the test, not the system.
- *Use a stub backplane.* Rejected — backplane behaviour is exactly what we are validating.

**CI integration**: Runs in a dedicated workflow (`multinode-correctness.yml`) on PRs touching `src/Common/Sorcha.ServiceDefaults/Hubs/**` or `src/Services/*/Hubs/**`. Slower than unit tests (~3 min) so kept off the per-commit gate.

---

## Resolved unknowns from Technical Context

| Item | Resolution |
|---|---|
| Backplane package version pin | Microsoft.AspNetCore.SignalR.StackExchangeRedis matching the .NET 10 line currently in tree (managed via `Directory.Packages.props`). |
| Tenant DB EF migration target version | Latest schema version on the `Sorcha.Tenant.Service` migration list at time of land. |
| Channel prefix string format | `sorcha:signalr:{serviceShortName}` where serviceShortName is one of `tenant`, `blueprint`, `wallet`, `register`. ChatHub shares Blueprint's prefix because they live in the same service. |
| OpenTelemetry meter name | `Sorcha.SignalR` (parallels `Sorcha.Storage` from Feature 113). |
| Inbox category enum members | `Action`, `Credential`, `Membership`, `Security`, `System`, `Workflow`, `Custom`. `Custom` reserved for future open-ended use. |
| Inbox severity enum members | `Info`, `Warning`, `ActionRequired`, `Critical`. |
| Default `ChannelHints` per category | `Action` → inbox+push+email; `Credential` → inbox+push; `Membership` → inbox+email; `Security` → inbox+push+email; `System` → inbox; `Workflow` → inbox. |
| `Sorcha.ServiceDefaults.Hubs` namespace home | Inside the existing `Sorcha.ServiceDefaults` project as a sub-namespace and folder, mirroring `Sorcha.ServiceDefaults.Storage`. |

No NEEDS CLARIFICATION items remain.

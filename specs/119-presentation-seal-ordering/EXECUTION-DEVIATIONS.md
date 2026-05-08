# Execution Deviations — Feature 119

This file captures deviations from the design encountered during implementation.

---

## Deviation: T009 / T010 — Coordinator unit tests cannot use a non-existent in-memory Redis test double

**Issue.** `tasks.md` T009 and `research.md` R8 prescribe using "the existing
in-memory Redis test double (`Sorcha.Storage.InMemory.Redis`)" for
`RedisPresentationSealCoordinator` unit tests. No such project or test double
exists in the codebase. Searched: no project named `Sorcha.Storage.InMemory.Redis`,
no `IConnectionMultiplexer` fake, no `IDatabase` fake. Existing Redis-backed
components (`RedisPendingPresentationStore`, `AbandonmentSweeper`) test against
mocked `IConnectionMultiplexer` + `IDatabase` for narrow scenarios only — none
of them exercise `KeysAsync`, batch pipelines, or hash round-trips at the depth
the coordinator needs.

**Decision.** Write the unit-test surface that exercises the coordinator's
behavioural contract through narrow `Mock<IConnectionMultiplexer>` /
`Mock<IDatabase>` setups for the simple paths (enqueue, drain via mocked HSET /
HDEL / HGETALL). Recovery-sweep coverage (KeysAsync iteration, missed-event
poll, TTL fail) is deferred to **T017 / T021** integration tests against real
Redis from the Docker compose stack — exactly where the existing
`RegisterEventBridgeServiceTests` integration tests live. This matches the
intent of R8 (`integration tests use the Docker-stack Redis via the existing
WebApplicationFactory setup pattern`).

**Impact.**
- T009 unit-test obligations 1, 2, 3, 4, 5 (round-trip + idempotence + reject
  paths) covered via mocked Redis — narrow but sufficient for branch coverage
  of the coordinator's logic.
- T009 obligations 6, 7, 8 (sweeper recovery, TTL fail, restart safety) are
  covered by the integration tests in T017 / T021 / T029 (real Redis, real
  TransactionConfirmedEvent fan-out, real persistence across coordinator
  instance disposal).
- T010 (`PresentationSealSubscriber` tests) covered with mocked
  `IPresentationSealCoordinator` and in-memory `InMemoryEventSubscriber` —
  this works because the subscriber is purely orchestration over the two
  collaborators.
- The MVP user-visible win (US1: AssuredIdentity Phase 2 passes) does not
  depend on these deferred unit tests passing — it depends on the
  end-to-end behaviour validated by T016 / T025.

No design change is required; the design is sound. The deviation is purely
about test-infrastructure availability.

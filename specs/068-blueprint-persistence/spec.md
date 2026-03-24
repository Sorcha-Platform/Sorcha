# Feature Specification: Blueprint Service Persistence & Validator Crash Recovery

**Feature Branch**: `068-blueprint-persistence`
**Created**: 2026-03-24
**Status**: Draft
**Input**: User description: "Blueprint Service stores all data in ConcurrentDictionary (lost on restart). Migrate drafts/templates to durable storage, cache published blueprints and instance state from the register (single source of truth), and add validator startup reconciliation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Blueprint Draft Persistence (Priority: P1)

A blueprint designer creates a draft blueprint through the UI or API, saves it, and returns hours later to continue editing. The draft survives service restarts and container redeployments. The designer is the owner of the draft and only they can see and edit it. The system's data model accommodates future collaborative editing by multiple designers, but only single-owner access is enforced now.

**Why this priority**: Drafts represent user work-in-progress. Losing a designer's work on every restart is the most impactful data loss in the current system.

**Independent Test**: Can be fully tested by creating a draft, restarting the Blueprint Service, and verifying the draft is still retrievable with all its content intact.

**Acceptance Scenarios**:

1. **Given** a designer creates a blueprint draft, **When** the Blueprint Service is restarted, **Then** the draft is still available with all content preserved
2. **Given** a designer saves a draft, **When** another user queries drafts, **Then** only the owner's drafts are visible to the owner
3. **Given** a draft exists with an owner, **When** a different authenticated user attempts to access it, **Then** the system denies access
4. **Given** a designer deletes a draft, **When** they query their drafts, **Then** the deleted draft no longer appears
5. **Given** no durable storage is configured, **When** the Blueprint Service starts, **Then** drafts are stored in volatile memory (development fallback) with a log warning

---

### User Story 2 - Template Library Persistence (Priority: P1)

An administrator manages the blueprint template library. Templates are reusable starting points that designers can clone when creating new blueprints. Templates survive service restarts without requiring re-seeding from JSON files. The initial set of templates is populated from the existing JSON template files during the first database migration.

**Why this priority**: Templates are currently re-seeded from JSON files on every restart, which is fragile and prevents runtime additions/modifications from persisting.

**Independent Test**: Can be fully tested by adding a template, restarting the service, and verifying it persists. Also verify that existing JSON templates are available after first-time database setup.

**Acceptance Scenarios**:

1. **Given** the template library contains templates, **When** the Blueprint Service is restarted, **Then** all templates are still available
2. **Given** a fresh installation with an empty database, **When** the Blueprint Service starts for the first time, **Then** the existing JSON template files are migrated into the database as seed data
3. **Given** an administrator adds a new template at runtime, **When** they query the template library, **Then** the new template appears alongside the seeded templates
4. **Given** an administrator modifies a template, **When** the service restarts, **Then** the modifications persist (not overwritten by seed data)

---

### User Story 3 - Published Blueprint Caching (Priority: P1)

When a user or service requests a published blueprint, the system retrieves it from a fast cache layer. The cache is populated from the register (the single source of truth for published blueprints). Cache entries include the blueprint version so that concurrent instances running different versions of the same blueprint each resolve to their correct version. On cache miss, the system fetches the blueprint from the register and populates the cache.

**Why this priority**: Published blueprints are read on every action execution. The current in-memory store loses all published blueprints on restart, requiring re-publishing. Caching from the register eliminates this while respecting the register as the source of truth.

**Independent Test**: Can be fully tested by publishing a blueprint, restarting the service, requesting the blueprint, and verifying it is served from the cache (or fetched from the register on cache miss).

**Acceptance Scenarios**:

1. **Given** a published blueprint exists on the register, **When** the Blueprint Service is restarted and the blueprint is requested, **Then** the system fetches it from the register and caches it for subsequent requests
2. **Given** a blueprint is cached, **When** the same blueprint is requested again, **Then** it is served from cache without querying the register
3. **Given** blueprint version 1 is cached, **When** version 2 is published and an existing instance still runs version 1, **Then** the version 1 instance resolves version 1 from cache and new instances resolve version 2
4. **Given** the cache is unavailable, **When** a published blueprint is requested, **Then** the system falls back to querying the register directly

---

### User Story 4 - Instance State Caching (Priority: P2)

When the Blueprint Service tracks a running blueprint instance, the execution state is cached for fast access. On cache miss (e.g., after a restart), the system reconstructs the instance state by replaying the instance's transactions from the register. This ensures no instance data is permanently lost — the register is the source of truth — while providing fast access for active instances.

**Why this priority**: Instance state is frequently read during action execution. Caching provides performance while the register guarantees durability. This is lower priority than drafts/templates because instance state is already reconstructable (it's just slow without a cache).

**Independent Test**: Can be fully tested by starting an instance, executing some actions, restarting the service, and verifying the instance state is reconstructed from the register.

**Acceptance Scenarios**:

1. **Given** an active instance with accumulated state, **When** the Blueprint Service is restarted, **Then** the instance state is reconstructed from the register's transaction history on first access
2. **Given** an instance state is cached, **When** a new action is executed, **Then** the cache is updated with the new accumulated state
3. **Given** the cache is unavailable, **When** instance state is needed, **Then** the system reconstructs from the register (degraded performance, no data loss)
4. **Given** two instances of the same blueprint at different versions, **When** each is accessed, **Then** each resolves its own state independently

---

### User Story 5 - Validator Startup Reconciliation (Priority: P2)

After a crash or restart, the Validator Service reconciles its state by checking for any transactions that arrived during downtime. It queries the register for the current docket height, checks the unverified transaction pool for pending work, and re-validates those transactions through the normal pipeline before resuming normal operation.

**Why this priority**: Without reconciliation, transactions submitted during validator downtime are stranded in the unverified pool until new transactions arrive. This causes unpredictable delays in transaction processing after restarts.

**Independent Test**: Can be fully tested by submitting transactions to the unverified pool, stopping the validator, restarting it, and verifying those transactions are processed.

**Acceptance Scenarios**:

1. **Given** transactions exist in the unverified pool, **When** the Validator Service starts, **Then** it processes those pending transactions before resuming normal polling
2. **Given** the register has dockets up to height N, **When** the Validator starts, **Then** it acknowledges height N and only processes transactions beyond that point
3. **Given** no pending transactions exist in the unverified pool, **When** the Validator starts, **Then** it resumes normal polling without delay
4. **Given** the register is unreachable at startup, **When** the Validator attempts reconciliation, **Then** it retries with backoff and logs the failure, eventually starting normal polling when the register becomes available

---

### User Story 6 - Infrastructure Wiring (Priority: P1)

The Blueprint Service's durable storage is configured through the standard platform patterns: a dedicated database provisioned through the orchestration layer, connection strings passed via configuration, and automatic schema setup on first start. When no durable storage is configured (development without database), the service falls back to volatile in-memory storage with a warning.

**Why this priority**: Without the infrastructure wiring, none of the other stories can deliver durable persistence. This is the foundation.

**Independent Test**: Can be verified by starting the Blueprint Service with the orchestration layer and confirming the database is created, schema is applied, and data persists across restarts.

**Acceptance Scenarios**:

1. **Given** the orchestration layer is running, **When** the Blueprint Service starts, **Then** it connects to its dedicated database and applies schema automatically
2. **Given** a containerized deployment, **When** the Blueprint Service starts, **Then** it connects to the database using the configured connection string
3. **Given** no database connection is configured, **When** the Blueprint Service starts, **Then** it falls back to in-memory storage and logs a warning
4. **Given** the database was previously initialized, **When** the Blueprint Service starts with a newer schema version, **Then** schema changes are applied automatically without data loss

---

### Edge Cases

- What happens when a draft is being edited and the database becomes temporarily unavailable? The system should return an error to the user rather than silently losing data — fail loudly, not silently.
- What happens when the register contains a published blueprint that fails to deserialize (corrupted data)? The system should log the error and return a cache miss — do not cache corrupt data.
- What happens when the cache reaches memory limits? Entries should be evicted by least-recently-used policy with version-pinned entries for active instances protected from eviction.
- What happens when a blueprint is upgraded and old-version instances are still running? Each instance is pinned to its blueprint version at creation time. The cache must maintain both versions until all old-version instances complete.
- What happens when the validator's reconciliation finds transactions that reference a register it doesn't know about? It should skip those transactions and log a warning — the register may not have been replicated to this node yet.
- What happens when seed templates conflict with existing database templates (same ID, different content)? Database content takes precedence — seed data should only populate empty slots, never overwrite user modifications.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST persist blueprint drafts across service restarts using durable storage
- **FR-002**: System MUST associate each draft with an owner identifier and restrict access to the owner
- **FR-003**: System MUST provide a data model that supports future shared or delegated draft access without requiring schema changes to the core draft entity
- **FR-004**: System MUST persist blueprint templates across service restarts using durable storage
- **FR-005**: System MUST seed the template library from existing JSON template files on first-time database initialization, without overwriting subsequent user modifications
- **FR-006**: System MUST cache published blueprints from the register (single source of truth) for fast access
- **FR-007**: System MUST include blueprint version in cache keys to support concurrent instances running different versions of the same blueprint
- **FR-008**: System MUST fetch published blueprints from the register on cache miss and populate the cache
- **FR-009**: System MUST cache instance execution state for active blueprint instances
- **FR-010**: System MUST reconstruct instance state from register transaction history on cache miss
- **FR-011**: System MUST update the instance state cache when new actions are executed
- **FR-012**: System MUST provision a dedicated database for the Blueprint Service through the orchestration layer
- **FR-013**: System MUST apply database schema automatically on service startup (auto-migration)
- **FR-014**: System MUST fall back to in-memory storage when no database connection is configured, with a log warning
- **FR-015**: On startup after a crash, the Validator MUST check the unverified transaction pool for pending transactions and process them through the validation pipeline
- **FR-016**: On startup, the Validator MUST query the register for the current docket height to determine its reconciliation baseline
- **FR-017**: The Validator's verified transaction queue MUST remain in-memory (ephemeral by design)

### Key Entities

- **BlueprintDraft**: An unpublished, work-in-progress blueprint owned by a designer. Key attributes: unique ID, owner ID, blueprint content (JSON), name, description, status (Draft/Archived), timestamps. Related to future BlueprintDraftAccess entity for collaborative editing.
- **BlueprintTemplate**: A reusable blueprint starting point in the template library. Key attributes: unique ID, name, description, category, content (JSON), source (Seed/UserCreated), timestamps.
- **PublishedBlueprintCache**: A cached representation of a published blueprint from the register. Key attributes: register ID, blueprint ID, version, content, cache timestamp, TTL.
- **InstanceStateCache**: Cached execution state of a running blueprint instance. Key attributes: instance ID, blueprint ID, blueprint version, accumulated state, last transaction ID, cache timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Blueprint drafts survive 100% of service restarts — zero data loss for saved drafts
- **SC-002**: Template library is available within 5 seconds of service startup (no JSON re-parsing delay)
- **SC-003**: Published blueprint lookups complete in under 50ms on cache hit (versus register round-trip)
- **SC-004**: Instance state reconstruction from register completes within 10 seconds for instances with up to 100 transactions
- **SC-005**: After validator restart, pending transactions in the unverified pool are processed within 30 seconds of startup
- **SC-006**: The system operates correctly in degraded mode (no durable storage) with appropriate warnings — no crashes, no silent data loss

## Assumptions

- The existing store interfaces (`IBlueprintStore`, `IPublishedBlueprintStore`, `IActionStore`, `IInstanceStore`) are sufficiently abstracted to swap in durable implementations without changing callers
- The register's transaction query APIs are sufficient to reconstruct instance state (transactions can be queried by instance ID metadata)
- Blueprint versions are immutable once published — the same version number always refers to the same content
- The validator's Redis unverified pool retains transactions across validator restarts (Redis is durable)
- The existing JSON template files in `blueprints/` are the canonical seed data for the template library
- Draft ownership uses the JWT `sub` claim (user ID) as the owner identifier

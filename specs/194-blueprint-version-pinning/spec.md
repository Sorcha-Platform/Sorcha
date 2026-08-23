# Feature Specification: Blueprint Version Pinning

**Feature Branch**: `194-blueprint-version-pinning`

**Created**: 2026-08-23

**Status**: Draft

**Issue**: [#1559](https://github.com/sorcha/sorcha/issues/1559)

**Design contract**: `docs/superpowers/specs/2026-08-23-blueprint-version-pinning-design.md`

**Input**: User description: "Blueprint version pinning — an instance runs the definition it started on, forever, even after the blueprint is republished to the same register."

---

## The problem, in one paragraph

Republishing a blueprint to a register it is already published to is accepted, increments a version
number, and then **silently replaces the executable definition for every instance of that blueprint
id — including instances already in flight**. A participant mid-workflow is then validated against
actions, schemas and routing rules that did not exist when they joined. Nothing errors, nothing
warns, and no operator surface shows that it happened. Confirmed live: three versions of one
blueprint on one register, every republish accepted.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An in-flight application is unaffected by a republish (Priority: P1)

A citizen starts an application. While they are part-way through it, the authority that owns the
workflow republishes the blueprint with a behavioural change — a new required field on a later
step. The citizen continues and completes their application against the rules that were in force
when they started. They are never asked for the new field, never see a step that did not exist when
they applied, and their submission is never refused for failing a rule they could not have known
about.

**Why this priority**: This is the feature. Everything else exists to make this true or to prove it
is true. Forcing a running instance onto a new definition can change what a participant is being
asked to agree to, mid-agreement — the platform's core promise is that a recorded workflow means
what it said it meant.

**Independent Test**: Start an instance, republish the blueprint with a behavioural change, advance
the instance. It must complete under the original rules. Fully testable with one blueprint, one
register and one instance; delivers the entire user-visible value of the feature on its own.

**Acceptance Scenarios**:

1. **Given** an instance started against definition A of a blueprint, **When** the blueprint is
   republished as definition B adding a required field to a later action, **Then** the instance
   advances successfully submitting only the fields definition A required.
2. **Given** an instance started against definition A, **When** the blueprint is republished as
   definition B that removes an action the instance has not yet reached, **Then** the instance still
   reaches and completes that action.
3. **Given** an instance started against definition A, **When** the blueprint is republished as
   definition B that changes a routing rule, **Then** the instance routes by definition A's rule.
4. **Given** an instance running definition A, **When** any of its actions is submitted, **Then**
   the record of that submission states which definition it was executed against, and every node
   holding the register agrees on that answer.

---

### User Story 2 - A publisher upgrades freely, and new applicants get the new rules (Priority: P2)

An authority needs to change a live workflow — a new legal requirement adds a mandatory field. They
republish. The republish is accepted immediately, regardless of how many applications are currently
in flight. Every application started from that moment onward is governed by the new definition;
every application already under way is governed by the one it started on. Both definitions are live
on the register at the same time and neither disturbs the other.

**Why this priority**: Without this, Story 1 could be satisfied by simply refusing to republish —
which would make long-running workflows unupgradable, since they may never have a quiet moment. The
two halves together are what make the feature usable rather than merely safe.

**Independent Test**: With an instance deliberately left in flight, republish and confirm the
publish is accepted with no warning, error or delay attributable to the live instance; then start a
second instance and confirm it is governed by the new definition.

**Acceptance Scenarios**:

1. **Given** one or more instances of a blueprint are in flight, **When** the blueprint is
   republished, **Then** the republish succeeds and no instance is blocked, paused or altered.
2. **Given** a blueprint has been republished as definition B, **When** a new instance is started,
   **Then** it is governed by definition B and enforces definition B's requirements.
3. **Given** instances of both definition A and definition B are in flight simultaneously on one
   register, **When** each is advanced, **Then** each is validated against its own definition.

---

### User Story 3 - The pin survives a restart, a rebuild, and a second node (Priority: P2)

An operator restarts the workflow service — routinely, or because a node was replaced. An
in-flight instance that started on an older definition continues to run that older definition
afterwards. The same is true on a second node that replicates the register: it independently
reaches the same conclusion about which definition each instance is running, without being told.

**Why this priority**: A pin that only holds until the next restart is not a pin. This is also the
step most likely to fail, because it is the only one that depends on older definitions still being
recoverable rather than merely still being in memory — and Sorcha is always a multi-node platform,
so two nodes silently disagreeing about which rules an instance runs under is a divergence, not a
display bug.

**Independent Test**: Run Story 1 to the point where the instance is mid-flow on the old definition,
restart the service, then advance the instance. Separately, confirm a replica node reports the same
pinned definition for the same instance.

**Acceptance Scenarios**:

1. **Given** an instance pinned to definition A and a subsequent republish to definition B, **When**
   the workflow service is restarted, **Then** the instance still advances against definition A.
2. **Given** the same instance, **When** its state is reconstructed from the sealed record alone,
   **Then** the reconstruction reports the identical pinned definition as the live state.
3. **Given** two nodes both holding the register, **When** each reports the instance's pinned
   definition, **Then** they agree.
4. **Given** an instance whose pinned definition cannot be found, **When** it is advanced, **Then**
   it is refused with a named, operator-actionable reason — never silently advanced against a
   different definition.

---

### User Story 4 - Relabelling a field does not strand anyone (Priority: P3)

A workflow author fixes a typo in a field label, or reorders two questions on a form, and
republishes. Nothing about how the workflow executes has changed. No instance is affected, no new
definition needs to be kept alive, and the author is not made to feel that cosmetic edits are
dangerous.

**Why this priority**: If every republish created a new pinned definition, the number of definitions
to keep resolvable would grow with editing activity rather than with real change, and authors would
learn to fear the publish button. The platform already distinguishes presentational from
behavioural change; this story is that distinction paying for itself.

**Independent Test**: Republish with only presentational edits and confirm the pinned definition
identifier is unchanged and no instance is disturbed.

**Acceptance Scenarios**:

1. **Given** a blueprint with in-flight instances, **When** it is republished with only
   presentational changes, **Then** the pinned definition identifier is unchanged.
2. **Given** the same republish, **When** an in-flight instance is advanced, **Then** it behaves
   exactly as before, with no fallback, warning or refusal.
3. **Given** the same republish, **When** the version list is read, **Then** the ordinal version has
   incremented for human bookkeeping while the pinned definition identifier has not.

---

### User Story 5 - An operator can see which definition an instance is running (Priority: P3)

An operator investigating a workflow — or answering a question from a participant — can tell which
definition an instance is running, and can tell the difference between "this instance is on the
older definition, by design" and "this instance is broken".

**Why this priority**: The defect this feature fixes was invisible. A pin that is correct but
unreportable leaves the next investigation as blind as this one was. Lower priority than the
mechanism because it cannot be built before the mechanism exists.

**Independent Test**: Read an instance's detail after a republish and confirm it names the
definition it is pinned to, distinguishably from the blueprint's current latest.

**Acceptance Scenarios**:

1. **Given** an instance pinned to a superseded definition, **When** an operator reads the instance,
   **Then** the pinned definition is reported and is distinguishable from the current latest.
2. **Given** any instance, **When** the displayed ordinal version and the pinned definition are both
   reported, **Then** they cannot disagree — the ordinal is derived from the pin, not recorded
   independently of it.

---

### Edge Cases

- **An instance predating this feature** — its record carries no pinned definition. The platform
  falls back to the current latest definition, records that it did so in a way an operator can see
  and count, and takes the identical fallback everywhere the instance is derived, so two derivations
  of one instance can never disagree. The fallback must never apply to a record that *does* carry a
  pin.
- **A submission claims a definition that cannot be found.** Refused with a named reason. Falling
  back to the latest here would reintroduce the exact defect this feature exists to remove, and
  would do it silently.
- **A subsequent submission claims a different definition from the one the instance is pinned to.**
  Refused. A participant must not be able to move a running instance onto another definition by
  asserting a different one.
- **Two republishes produce the same definition** (a presentational-only change). Same pin, nothing
  changes, ordinal increments only.
- **An action exists in the old definition but not the new one.** Cannot arise for a pinned
  instance — the instance is pinned to the definition that contains the action. This is why no
  migration machinery is needed.
- **Governance, control and lifecycle records** are not instance-scoped and carry no pin. They must
  continue to be exempt, and no pin should be added to them.
- **A definition an instance is pinned to is unresolvable after a cold start.** The instance is
  stuck. This is the failure mode to watch, and the reason Story 3 is a P2 rather than a nice-to-have.

---

## Requirements *(mandatory)*

### Functional Requirements

#### The pin

- **FR-001**: The platform MUST record, for every workflow instance, the exact executable definition
  the instance is running.
- **FR-002**: The pinned definition MUST be identified by the **content of the definition**, not by
  its ordinal version number. The ordinal is a human-facing label only.
- **FR-003**: The pin MUST be derived only from the sealed, shared record of the workflow — never
  from any single node's local state, configuration, or the time at which a node happens to look. Two
  nodes holding the same register MUST always reach the same pin for the same instance.
- **FR-004**: The pin MUST be established at the moment the instance is created, from the definition
  that was current on that register at that moment.
- **FR-005**: The pin MUST be immutable for the life of the instance. Nothing — republishing,
  restarting, node replacement, or any submission by any participant — may change it.

#### Enforcement

- **FR-006**: Every submission that advances an instance MUST be validated against the instance's
  pinned definition, not against the blueprint's current latest definition.
- **FR-007**: The platform MUST refuse a submission that asserts a definition other than the
  instance's pin, with a named reason.
- **FR-008**: The platform MUST refuse a submission that asserts a definition it cannot resolve,
  with a named reason. It MUST NOT fall back to the latest definition in this case.
- **FR-009**: The assertion of which definition a submission was executed against MUST be
  authenticated — carried inside the material the submitter signs and the platform verifies — so
  that it cannot be altered in transit while appearing legitimate.
- **FR-010**: The platform MUST have an automated guard, derived from the shape of the signed
  material itself rather than from a hand-maintained list, that fails if any part of that material is
  left outside what is signed.

#### Upgrade

- **FR-011**: Republishing a blueprint MUST NOT be blocked, delayed, or warned against on the
  grounds that instances of it are in flight.
- **FR-012**: An instance started after a republish MUST be governed by the newly published
  definition.
- **FR-013**: Multiple definitions of one blueprint MUST be able to govern instances on one register
  simultaneously.
- **FR-014**: A republish that changes only presentational aspects of a definition MUST produce the
  same pin, so that no instance is moved and no additional definition needs to be retained.

#### Durability

- **FR-015**: Every published definition of a blueprint MUST remain resolvable after a restart, not
  only the most recently published one.
- **FR-016**: Reconstructing an instance from the sealed record alone MUST produce the identical pin
  to the live instance state.
- **FR-017**: Where an instance predates this feature and its record carries no pin, the platform
  MUST apply one clearly-defined fallback, apply that identical fallback on every path by which the
  instance can be derived, and make each use of it visible to an operator.

#### Reporting

- **FR-018**: An operator MUST be able to read which definition an instance is pinned to, and
  distinguish it from the blueprint's current latest definition.
- **FR-019**: Any human-facing version label shown for an instance MUST be derived from its pin, so
  that the label and the pin can never disagree.

### Explicitly out of scope

- **Migrating a running instance forward onto a new definition.** Ruled out by the hard rule in
  FR-005. If it is ever wanted it is a separate feature needing its own representation in the shared
  record.
- **Any platform-level multi-party gate on upgrading a blueprint.** Where an organisation wants
  sign-off before an upgrade, it authors that as a governance workflow — the platform already has
  that primitive. Building a bespoke gate here is explicitly not wanted, and this is stated so that
  nobody adds one later believing it was an oversight.
- **Changing how ordinal version numbers are assigned.** They remain a display label.
- **Design-time and administrative surfaces** (workflow authoring, the blueprint catalogue, version
  browsing, export) correctly operate on the latest definition and are unchanged.

### Key Entities

- **Executable definition** — the part of a blueprint that determines how it runs: its actions,
  their schemas, their routing and their participants. Excludes purely presentational aspects such
  as labels, help text and field ordering. Two blueprints with the same executable definition behave
  identically.
- **Pinned definition reference** — the content-derived identifier of the executable definition an
  instance is running. Established once, at instance creation; immutable thereafter.
- **Published definition** — one publication of a blueprint to a register, carrying both its
  content-derived identifier and its human-facing ordinal version. Many may exist per blueprint per
  register, and all of them must remain resolvable.
- **Routing assertion** — the signed statement, carried on each submission, of which action was
  completed and where the workflow goes next. This feature extends it to also state which definition
  the submission was executed against.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An application in flight when its workflow is republished with a behavioural change
  completes successfully under the original rules — 100% of the time, with zero submissions refused
  for failing a rule introduced after the application began.
- **SC-002**: Republishing a workflow succeeds with live instances present, in the same time it
  takes with none, on 100% of attempts.
- **SC-003**: An application started after a republish is governed by the new rules — 100% of the
  time, verified by the new requirement actually being enforced against it rather than merely
  recorded.
- **SC-004**: After a restart of the workflow service, an in-flight application on a superseded
  definition still completes under that definition — verified by live execution, not by inspection
  of stored state.
- **SC-005**: Two nodes holding the same register report the same pinned definition for the same
  instance, for every instance checked.
- **SC-006**: A presentational-only republish leaves the pinned definition identifier unchanged and
  disturbs zero in-flight instances.
- **SC-007**: A submission asserting an unresolvable or foreign definition is refused with a named
  reason in 100% of attempts, and in zero cases is silently accepted against a different definition.
- **SC-008**: Every guard introduced by this feature has been observed to fail when the behaviour it
  guards is deliberately broken, and the specific named test that catches each break is recorded.
- **SC-009**: Every use of the pre-feature fallback is countable by an operator, and the count is
  zero on a freshly created register.
- **SC-010**: An operator can determine which definition any instance is running without reading
  service logs or querying a database directly.

---

## Assumptions

- **Pre-release, so no data migration is owed.** Schema changes are folded into each service's
  existing initial migration and out-of-date databases are recreated rather than migrated, per the
  standing platform rule. No installation exists that must be upgraded in place.
- **The pre-feature fallback is genuinely needed, not defensive.** The rollout recreates the
  workflow service's own database but does **not** re-create the register. Sealed submissions
  predating this feature therefore survive and carry no pin, and any instance derived from them needs
  the fallback. This is why FR-017 exists rather than being simplified away. Its deletion becomes
  possible only once no un-pinned submission remains on any register — a trigger, not a date.
- **The existing distinction between presentational and behavioural parts of a definition is
  correct and reused as-is.** This feature does not redefine it. Unknown extensions continue to be
  treated as behavioural, which is the fail-safe direction.
- **Governance over upgrades is authored, not built** (see out of scope). Recorded as an assumption
  as well as an exclusion because it is the kind of thing a later reader adds in good faith.
- **The ordinal version number remains unreliable as an identifier** — it is derived from insert
  order and re-derived on recovery. This feature does not fix that; it stops anything depending on it.
- **Presentation, governance, control and lifecycle records stay exempt** from carrying a pin. They
  are not instance-scoped.
- **Deployment scope is the workflow service and the validating service.** Both are ordinary
  per-service replacements; no network-wide ceremony is required.
- **A live end-to-end run is the acceptance evidence**, not a passing automated suite. This feature
  exists because a live run found what a large, green automated suite did not.

---

## Findings from verification of the design (recorded so they are not rediscovered)

The design was checked against the current source before this spec was written. Its central claims
hold. Four things it does not mention were found, and each changes the work:

1. **There is already a per-version blueprint resolver in the validating service that nothing
   calls.** It has an interface, an implementation, a version-history cache and a test file. Its only
   production caller invalidates its cache; no production code ever asks it to resolve anything. It
   is also wrong for this purpose even if wired, because after selecting a version it fetches the
   definition from the id-keyed cache anyway — returning the latest. It must be either repurposed or
   removed as part of this work; leaving a dormant near-miss beside the real mechanism is how the
   next person resolves the wrong one.
2. **The definition is resolved by id in three places in the validating service, not one.** The
   design cites the declaration only.
3. **There are five hardcoded instance-version writes, not two.** Three further sites exist beyond
   the two the design names. Fixing a defect class means sweeping it.
4. **The definition cache is keyed by id throughout its whole interface**, not in one place — most
   of its methods take an id, one derives the key from the definition itself, and its cross-node
   invalidation signal carries a bare id. Re-keying is an interface change, not a string change.

Separately, the design's rollout section is internally inconsistent about whether the register is
recreated. It is not — see the fallback assumption above.

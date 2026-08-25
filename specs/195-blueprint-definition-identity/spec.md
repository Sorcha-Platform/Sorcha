# Feature Specification: Blueprint Definition Identity

**Feature Branch**: `195-blueprint-definition-identity`

**Created**: 2026-08-24

**Status**: Draft

**Input**: The Option D decision recorded on issue #1563, designed in
`docs/superpowers/specs/2026-08-24-blueprint-lifecycle-design.md`. Evidence base with file:line
citations: `docs/superpowers/specs/2026-08-24-blueprint-lifecycle-current-state-FINDINGS.md`.

**Issues**: #1563, #1566, #1567, #1568, #1570.
**Explicitly out of scope**: #1558 (validation surface reconciliation), #1569 (endpoint auth).

---

## Why this feature exists

Feature 194 established a platform rule: **an in-progress workflow instance runs the definition it
started on**. A participant is never presented with an action, schema or routing rule that did not
exist when they joined.

That rule is not currently kept. Three independent reasons, all silent:

1. Only the **first** definition of a blueprint ever reaches the register, so a superseded definition
   cannot be resolved after a restart and the instance running it stops permanently (#1563).
2. The value the instance pins to **does not address the whole definition**, so several behavioural
   edits produce an identical pin and the instance is handed the newest definition anyway (#1566).
3. The pin is checked when a transaction is sealed but **not when it is submitted**, so the engine
   validates a payload against one definition and labels the result with another (#1567).

Every one of these degrades to plausible behaviour rather than to an error. Nothing in the platform
reports that the rule has been broken.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A published definition survives (Priority: P1)

A service designer publishes a workflow, citizens begin using it, and the designer later publishes a
changed version. Every definition that was ever published remains permanently retrievable, so a
citizen part-way through the older version can finish it — including after the platform is restarted
or the workflow is picked up on a different node.

**Why this priority**: Without it the platform's central versioning promise is unkeepable. Everything
else in this feature refines a guarantee that does not currently hold at all. This story alone, with
nothing else, converts "the instance stops forever" into "the instance completes".

**Independent Test**: Publish a workflow, start an instance, publish a changed version, restart the
platform, and confirm the in-flight instance still resolves and advances on its original definition.
Delivers the durability guarantee on its own.

**Acceptance Scenarios**:

1. **Given** a workflow published to a register, **When** a behaviourally changed version of the same
   workflow is published to the same register, **Then** both definitions are permanently recorded on
   that register and both remain independently retrievable.
2. **Given** an instance started on the earlier definition, **When** the platform is restarted,
   **Then** the instance still resolves its own definition and can advance.
3. **Given** a definition is republished with **no** change of any kind, **When** the publish is
   accepted, **Then** no second record is created — the republish is recognised as the same
   definition.
4. **Given** the same workflow is published to two different registers, **When** both publications
   are recorded, **Then** each has a distinct identity, so neither register's record can be mistaken
   for the other's.
5. **Given** a definition whose identity is recorded, **When** its content is altered in transit,
   **Then** the alteration is detectable from the record itself without consulting any other source.

---

### User Story 2 - The submitted action is judged by the instance's own definition (Priority: P1)

A participant fills in and submits a step of a workflow. The form they were shown, the rules their
answers are checked against, the calculations that run and the route the workflow takes are all taken
from **the definition their instance is running** — not from whatever version the designer has edited
since.

**Why this priority**: Equal-first with Story 1 because it is the half of the guarantee that faces
the participant. Story 1 makes the definition *retrievable*; this makes it the one actually *used*.
It is also independently valuable: it can ship before Story 1 and immediately stops submissions being
judged against a draft.

**Independent Test**: Start an instance, edit the workflow's unpublished draft so it disagrees with
the published version, submit a step, and confirm the submission was validated and routed by the
published definition the instance is pinned to. Testable with no change to how definitions are
recorded.

**Acceptance Scenarios**:

1. **Given** an instance pinned to a definition, **When** a participant submits a step, **Then** the
   payload is validated, the calculations evaluated and the route chosen using that definition.
2. **Given** an unpublished draft that differs from the instance's definition, **When** a participant
   submits a step, **Then** the draft has no effect on the outcome.
3. **Given** two instances of the same workflow pinned to different definitions, **When** both submit
   the same step concurrently, **Then** each is judged by its own definition.
4. **Given** an instance is created, **When** its starting state is established, **Then** that state
   is derived from the same definition the instance is pinned to.
5. **Given** an instance whose definition cannot be found, **When** a participant submits a step,
   **Then** the submission is refused with a diagnosable reason — never silently accepted against a
   substitute definition.

---

### User Story 3 - A behavioural change is recognised as one (Priority: P2)

When a designer changes anything that affects how a workflow behaves, the platform treats it as a new
definition. When a designer only changes wording, labels or field order, the platform recognises that
nothing behavioural changed and does not ask them to re-rehearse.

**Why this priority**: P2 rather than P1 because Story 1's identity covers the whole definition, so a
behavioural edit already produces a new record without this. What this story adds is the *other*
direction — correctly telling a designer whether their edit needs a fresh rehearsal — and closes the
gap where a behavioural edit currently keeps a stale rehearsal pass valid.

**Independent Test**: Make each kind of behavioural edit in turn and confirm each is recognised as
behavioural; make each kind of presentational edit and confirm none is. Testable against the
authoring surface alone.

**Acceptance Scenarios**:

1. **Given** a workflow with a rehearsal recorded, **When** the designer changes what a rejection
   does, how a legacy route is conditioned, a parallel-branch deadline, a decision-notice catalogue,
   a presentation setting, or the instance-reference format, **Then** each is recognised as a
   behavioural change and a fresh rehearsal is required.
2. **Given** the same workflow, **When** the designer only relabels a field, rewords a description or
   reorders questions, **Then** no fresh rehearsal is required.
3. **Given** a property is added to the workflow model in future, **When** nothing classifies it as
   behavioural or presentational, **Then** the platform refuses to guess — the omission is surfaced
   at build time rather than defaulting either way.

---

### User Story 4 - One honest upgrade path (Priority: P3)

A designer wanting to change a live service has exactly one way to do it, and the version labels they
see mean the same thing every time they look.

**Why this priority**: P3 because it is coherence rather than correctness — no participant is harmed
today. But it is cheap once Stories 1–3 land, and leaving it undone keeps a button that looks like
versioning and is not.

**Independent Test**: Amend a published workflow and confirm the result is a new version of the same
workflow, listed alongside its predecessors; restart the platform and confirm the version labels are
unchanged.

**Acceptance Scenarios**:

1. **Given** a published workflow, **When** the designer amends it, **Then** the amendment is a new
   version of that same workflow and appears in its version history.
2. **Given** a workflow with several versions, **When** the platform is restarted, **Then** each
   version's label is unchanged.
3. **Given** a designer selects a specific earlier version to amend, **When** the platform is
   restarted between selection and amendment, **Then** the same definition is selected either way.

---

### Edge Cases

- **A definition is published, then republished byte-identically.** Recognised as the same
  definition; no second record, and the request still succeeds.
- **Two definitions of one workflow are published in rapid succession** before the first is recorded.
  Both are eventually recorded; neither displaces the other.
- **An instance is created but its first step is not submitted until after a further republish.** The
  instance uses the definition it was created against, not the newest.
- **A node holds a register but has never seen a particular definition published** (it arrived by
  replication). It can still resolve that definition and run instances against it.
- **A definition is requested that the register does not hold.** Refused with a diagnosable reason;
  never substituted with the newest.
- **A workflow is published to a second register.** Distinct identity per register.
- **The workflow model gains a new property.** Classified explicitly, or the build fails.
- **A property's serialized name is changed by a refactor.** Detected before release — every existing
  definition's identity would otherwise change silently.
- **A definition contains an unusual but valid document** — duplicate keys, unusual number forms,
  non-ASCII text, deeply nested schemas. Its identity is stable and reproducible.
- **A workflow is amended concurrently by two designers.** Both amendments become versions; neither
  is lost.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Identity of a definition

- **FR-001**: Every blueprint definition published to a register MUST be permanently recorded on that
  register, and MUST remain independently retrievable for as long as the register exists.
- **FR-002**: A definition MUST be identified by the publication that created it. That identity MUST
  be a function of the register it was published to, the blueprint it is a version of, and the
  definition's content — so that identical content on two registers yields two identities, and
  different content on one register yields two identities.
- **FR-003**: The identity MUST be verifiable from the recorded definition alone, without consulting
  any other record, so that alteration of a recorded definition is detectable.
- **FR-004**: Publishing content identical to an existing definition on the same register MUST be
  recognised as the same definition and MUST NOT create a second record. The request MUST still
  succeed.
- **FR-005**: Publishing content that differs in any way from every existing definition of that
  blueprint on that register MUST create a new record.
- **FR-006**: The identity MUST be computed in exactly one place by exactly one component. No other
  component may recompute it; every other component MUST read it.
- **FR-007**: The rules that determine a definition's identity from its content MUST be fixed and
  guarded, such that any change to them fails the build rather than silently changing the identity of
  every existing definition.

#### Which definition an instance runs

- **FR-008**: An instance MUST record which definition it runs, and that record MUST be established
  when the instance is created and MUST NOT change for the life of the instance.
- **FR-009**: The definition an instance is created against and the definition it records MUST be the
  same definition.
- **FR-010**: Every step of an instance MUST be validated, evaluated and routed against the
  definition that instance records — at the moment of submission, not only when the result is sealed.
- **FR-011**: Unpublished work-in-progress MUST NOT influence the execution of any instance.
- **FR-012**: A step whose instance's definition cannot be resolved MUST be refused with a
  distinguishable reason. The platform MUST NOT substitute any other definition, and in particular
  MUST NOT substitute the most recent one.
- **FR-013**: A starting step MUST be anchored to the publication of the definition its instance
  records, and the platform MUST confirm that publication is genuinely recorded on the register
  before the step proceeds.

#### Behavioural versus presentational change

- **FR-014**: The platform MUST distinguish a change that affects how a workflow behaves from one
  that affects only how it is presented, and MUST use that distinction only to decide whether a
  previously recorded rehearsal remains valid.
- **FR-015**: Every aspect of a workflow that affects validation, routing, rejection handling,
  credential issuance, notification of outcomes, presentation-evidence handling, or instance
  identification MUST be treated as behavioural.
- **FR-016**: The behavioural/presentational classification MUST be exhaustive over the workflow
  model: an unclassified aspect MUST fail the build rather than defaulting to either category.

#### One upgrade path and one version label

- **FR-017**: Amending a published workflow MUST produce a new version of that same workflow, listed
  in its version history alongside its predecessors.
- **FR-018**: No part of the platform may select or resolve a definition by its ordinal version
  label.
- **FR-019**: The ordinal version label MUST be derived from the order in which definitions were
  recorded on the register, and MUST be identical before and after a restart.
- **FR-020**: Version-numbering fields that nothing reads MUST be removed rather than left present
  and inert.

#### Recording and recovery

- **FR-021**: Exactly one path may record a definition on a register. Instance creation MUST NOT
  record definitions.
- **FR-022**: A recorded definition MUST be self-contained — executable by a node that holds nothing
  but the register, with no dependency on any catalogue or resource held only by the publishing node.
- **FR-023**: A node rebuilding its state from a register MUST restore every definition that register
  holds, and MUST verify each against its own recorded identity before use.

### Key Entities

- **Blueprint** — the workflow as a thing that persists across changes. Stable identity; many
  definitions over its life.
- **Definition (publication)** — one specific, immutable, executable version of a blueprint as
  published to one register. Identified by its publication. This is the unit an instance runs and a
  starting step anchors to.
- **Instance** — one run of a workflow. Records exactly one definition, permanently.
- **Behavioural signature** — a value derived from only those parts of a definition that affect how it
  behaves. Several definitions may share one. Used solely to decide whether a rehearsal remains valid.
- **Version label** — a human-facing ordinal derived from the order definitions were recorded.
  Presentation only; never an input.
- **Draft** — a designer's unpublished work-in-progress. Node-local, mutable, never recorded on a
  register, never used to execute anything.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a behavioural republish and a full platform restart, **100%** of in-flight
  instances started before the republish still resolve their definition and can complete. Today this
  figure is 0% for instances on any definition after the first.
- **SC-002**: Of the behavioural changes a designer can make, **100%** produce a distinct definition
  identity. Measured by the same probe that recorded 9 failures out of 9 before this feature.
- **SC-003**: **Zero** occurrences of the platform falling back to a substitute definition, across a
  complete live acceptance run on a freshly created register. This is the *positive* check: absence
  of errors is not evidence, because every failure mode of this area degrades to plausible behaviour.
- **SC-004**: A presentational-only republish requires **no** repeat rehearsal, while **every**
  behavioural republish requires one. Both directions verified.
- **SC-005**: The same workflow published to two registers yields **two** distinct definition
  identities, verified on a live multi-register run.
- **SC-006**: **Zero** components other than the single designated owner compute a definition's
  identity, and **zero** components select a definition by ordinal — verified by an automated check
  that fails the build, not by inspection.
- **SC-007**: A participant submitting a step is judged by their own instance's definition in
  **100%** of cases, including while an unpublished draft of the same workflow disagrees with it.
- **SC-008**: A change to any rule governing how a definition's identity is derived from its content
  fails the build **before** merge, in **100%** of cases, including a rename of a serialized property
  name.
- **SC-009**: A node that has only replicated a register — never published to it — can start and run
  instances of every definition that register holds.

---

## Assumptions

- **No migration is required, and none will be written.** The platform is pre-release; the maintainer
  has authorised recreating registers. Every backward-compatibility path that would otherwise be
  needed for definitions recorded under the previous scheme is therefore out of scope. A wipe removes
  migration risk, not the obligation to prove the result works.
- **Acceptance is a live run, not a green test suite.** Every defect this feature addresses is silent
  and degrades to plausible behaviour, so the acceptance bar is a live re-genesis plus the positive
  checks above. This is a project standing rule, not a choice made here.
- **The existing platform decisions from Feature 194 stand and are not reopened**: an in-progress
  instance always runs the definition it started on; publishers upgrade freely with no platform-level
  multi-party gate; migrating a running instance forward is out of scope.
- **The behavioural/presentational distinction remains worth keeping** and keeps its existing job
  (deciding rehearsal validity). This feature narrows its responsibility rather than removing it.
- **Drafts remain node-local and non-durable.** This is a decision, not a gap, and is unchanged.
- **Concurrent publication is rare but must not lose data.** No coordination mechanism is assumed
  beyond what the register already provides.
- **The validation-surface reconciliation (#1558) and the unauthenticated definition-read endpoint
  (#1569) are excluded** and will ship separately. Neither blocks nor is blocked by this feature.
- **Existing platform invariants that this feature depends on are unchanged**: a starting step may
  anchor to a publication with many sibling instances; confirming that publication exists before a
  starting step proceeds is a genuine precondition and is retained.

---

## Dependencies

- The register must record and serve definitions; this feature adds a record per definition rather
  than per blueprint.
- The rehearsal gate (Feature 142) continues to consume the behavioural signature and is unaffected
  in its own behaviour.
- The instance projection (Feature 145) continues to derive instance state from sealed ledger facts;
  the definition an instance records is one such fact and remains carried on the signed routing
  decision.
- Deployment order across services remains significant while both old and new components could
  coexist; the authorised wipe removes this concern for the initial rollout but not for the
  development sequence.

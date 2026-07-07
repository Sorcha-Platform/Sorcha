# Feature Specification: Autonomous agent decides on disclosed application data

**Feature Branch**: `176-agent-disclosed-payload`

**Created**: 2026-07-07

**Status**: Draft

**Input**: User description: "Sorcha-agent must evaluate its external checks against the real disclosed application data, not an empty payload."

## Overview

An autonomous Sorcha agent (e.g. the AIAS "Assure-ID" verifier) assesses incoming applications by
running a set of configured checks (address exists, email verified, photo present, language clean) and
approving or rejecting based on rules over the results of those checks. For the assessment to mean
anything, the agent must evaluate the checks against the **actual data the applicant submitted** — the
data the register has **disclosed to the agent's participant**.

Today the agent decides against an **empty** view of the application: it never receives the disclosed
prior-action data. Every check therefore resolves to its safe default, the rules are applied to blank
facts, and the outcome is meaningless. This was found during the first live validation of the AIAS
Assured-Identity flow on the `n1` network (2026-07-07): a deliberately invalid postcode ("ZZ99 9ZZ")
was **approved** and a credential issued, and a valid application would be judged on the same blank
data. This feature makes the agent decide on the disclosed application data so its assessments are
trustworthy.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Agent decides on the real application (Priority: P1)

An autonomous verification agent, granted disclosure of an application's fields, evaluates its checks
against those actual fields and approves or rejects accordingly. A citizen who submits a valid
application is approved and issued their credential; a citizen who submits an invalid one (e.g. a
non-existent address) is rejected with the configured reason and receives no credential.

**Why this priority**: This is the entire point of an autonomous assessor. Without it the agent's
approve/reject decision is noise, and — worse than useless — it can issue credentials for applications
that should be refused, undermining the trust the credential is meant to convey. It is the minimum
viable slice: implementing only this story restores a working, meaningful autonomous assessment.

**Independent Test**: Submit two applications to a register the agent monitors — one valid, one with a
disqualifying field (invalid address). Confirm the agent approves the first (credential delivered) and
rejects the second (no credential, correct reason recorded), with no human involvement.

**Acceptance Scenarios**:

1. **Given** an agent granted disclosure of an application's fields and a rule that rejects a
   non-existent address, **When** a citizen submits an application with a non-existent address,
   **Then** the agent records a rejection with the configured reason and **no credential is issued**.
2. **Given** the same agent and a clean, valid application (address exists, email verified, photo
   present, language clean), **When** the citizen submits it, **Then** the agent records an approval
   and the citizen's credential is issued and delivered.
3. **Given** an application, **When** the agent evaluates it, **Then** each check is evaluated against
   the applicant's actual submitted value for the relevant field (not a default/blank value).

---

### User Story 2 - The agent never decides on missing data (Priority: P2)

If the agent cannot obtain the disclosed application data it needs — the register is briefly
unreachable, disclosure has not propagated, or the data is genuinely absent — the agent must **hold**
the application for manual review rather than guess. It must never approve (or reject) an application on
the basis of an empty or partial view of the data.

**Why this priority**: A silent "approve on no data" is the exact failure this feature exists to
prevent; a silent "reject on no data" is equally wrong (it refuses legitimate applicants). Fail-closed
holding preserves correctness under transient faults and is consistent with the platform's existing
agent fail-closed policy. P2 because Story 1 delivers the core value, but this guard is what makes it
safe in production.

**Independent Test**: Make the disclosed data temporarily unavailable for an application the agent must
decide, and confirm the agent holds it (no approve, no reject, no credential) and logs an actionable
reason; then restore availability and confirm the same application is decided correctly.

**Acceptance Scenarios**:

1. **Given** an application whose disclosed data cannot be retrieved, **When** the agent processes it,
   **Then** the agent holds it for manual review and issues no credential.
2. **Given** an application whose disclosed data is retrieved but is missing a field a rule depends on,
   **When** the agent processes it, **Then** the agent holds rather than treating the missing field as
   a pass/fail default.
3. **Given** a held application, **When** the disclosed data later becomes available, **Then** the
   agent re-evaluates it and reaches the correct approve/reject decision without manual intervention.

---

### User Story 3 - Every decision is explainable (Priority: P3)

An operator or demo-runner can see, for any application the agent decided, which check results drove the
decision — so a surprising outcome can be diagnosed without guesswork.

**Why this priority**: The original defect was invisible for weeks precisely because the agent gave no
insight into what it evaluated. Explainability turns a future recurrence into a two-minute diagnosis
rather than a multi-hour investigation. P3 because it improves operability rather than the decision
itself.

**Independent Test**: After the agent decides an application, retrieve the record of the check results
that produced the decision and confirm they reflect the applicant's actual data.

**Acceptance Scenarios**:

1. **Given** the agent has decided an application, **When** an operator inspects the agent's record,
   **Then** the check results (and which fields they were derived from) that drove the decision are
   visible.
2. **Given** a rejection, **When** an operator inspects the record, **Then** the specific failing
   check(s) are identifiable.

### Edge Cases

- **Nothing disclosed to the agent**: the agent's participant is not granted disclosure of any field
  the checks need → the agent must hold (Story 2), not approve on blanks.
- **Partial disclosure**: the agent is granted some fields but not one a rule depends on → hold.
- **Encrypted vs. plaintext register**: the agent receives the same disclosed view regardless of whether
  the register stores payloads encrypted or in plaintext (dev mode).
- **Application resubmitted / superseded**: the agent decides against the current disclosed data for the
  action it is assessing, not a stale earlier version.
- **Multiple monitored registers / applications in flight**: each decision uses the disclosed data for
  its own application; data from one application never bleeds into another's assessment.
- **Non-agent consumers unaffected**: existing human-facing surfaces that already show applicants'
  disclosed data continue to work unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The agent MUST obtain, for each action it is assigned to decide, the data of the prior
  action(s) **as disclosed to the agent's own participant** under the register's disclosure model.
- **FR-002**: The agent MUST evaluate each configured check against the applicant's actual submitted
  value for the field that check inspects, sourced from the disclosed data in FR-001.
- **FR-003**: The agent MUST base its approve/reject decision on the results of those checks over the
  real data, such that an application with a disqualifying field is rejected and a fully-valid
  application is approved.
- **FR-004**: The agent MUST NOT issue (or cause the issuance of) a credential for an application it
  rejects.
- **FR-005**: When the disclosed data required to evaluate the checks cannot be obtained, or is missing
  a field a rule depends on, the agent MUST hold the application for manual review and MUST NOT approve
  or reject it. (Fail-closed, consistent with the platform's agent fail-closed policy.)
- **FR-006**: The system MUST only expose to the agent the fields that are disclosed to the agent's
  participant — the agent MUST NOT receive fields the applicant did not disclose to it.
- **FR-007**: A non-human, service-tier agent identity MUST be able to retrieve the disclosed data it is
  authorised to see for the applications on the registers it monitors.
- **FR-008**: The check results that drive a decision MUST be recorded/observable so a decision can be
  explained after the fact (which checks passed/failed, and which fields they were derived from).
- **FR-009**: A held application MUST be re-evaluated and decided correctly once the disclosed data
  becomes available, without manual intervention.
- **FR-010**: The change MUST NOT alter the disclosed data seen by existing human-facing consumers, and
  MUST NOT weaken the disclosure model (no field becomes visible to a party not already entitled to it).

### Key Entities *(include if feature involves data)*

- **Disclosed action data**: the subset of an action's submitted payload that the register has disclosed
  to a given participant, per the disclosure rules of the blueprint. The agent consumes the view
  disclosed to *its* participant.
- **Agent decision**: the outcome (approve / reject / hold) for an application, the human-readable
  reason, and the check results that produced it.
- **Verification agent identity**: the non-human, service-tier identity the agent authenticates as,
  mapped to a workflow participant that has been granted disclosure of the fields its checks need.
- **Check result**: the boolean (and optional detail) produced by one configured check evaluated against
  the disclosed data (e.g. "address exists = false").

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of applications with a disqualifying field (e.g. a non-existent address) are rejected;
  0% are approved.
- **SC-002**: 100% of fully-valid applications are approved and result in a delivered credential.
- **SC-003**: 0 credentials are issued for rejected applications.
- **SC-004**: When required disclosed data is unavailable, 100% of affected applications are held (0 are
  approved or rejected on incomplete data).
- **SC-005**: For every decided application, the check results that drove the decision are retrievable
  and reflect the applicant's actual submitted data.
- **SC-006**: An automated end-to-end check (one valid + one invalid application) passes with no human
  involvement, and can be re-run as a regression guard.

## Assumptions

- The blueprint being assessed grants the agent's participant disclosure of the fields its checks
  require (e.g. the AIAS blueprint already discloses `/*` of the application to the `verification-analyst`
  participant). This feature consumes existing disclosure; it does not grant new disclosure.
- The register's disclosure model (the platform's DAD model) is the single authority on what the agent
  may see; the agent receives exactly the disclosed view, whether the register stores payloads encrypted
  or in dev-mode plaintext.
- The existing autonomous agent, its external-check framework, and its rules engine are reused; this
  feature changes **where the data the checks evaluate comes from**, not the check/rule model itself.
- The agent authenticates as a service-tier identity mapped to a workflow participant (already true for
  the AIAS Assure-ID agent).
- A field-name mismatch already found and provisionally corrected during diagnosis (the agent read a
  differently-named property than the API returns) is in scope to finalise as part of this feature, but
  is not by itself sufficient — the disclosed data must actually be made available to the agent.
- Fixing the source of the disclosed data for the agent will not change the human-facing verifier
  experience, which already renders the disclosed application correctly.

## Out of Scope

- Changing the disclosure model, or granting the agent visibility of fields not already disclosed to its
  participant.
- The human web-verifier experience (already functional).
- New check types or rule-language changes.
- The unrelated `n1` validator idle-stall work (issue #814) and the agent fail-closed hardening
  (#1077), which are prerequisites/relatives, not part of this feature's delivery.

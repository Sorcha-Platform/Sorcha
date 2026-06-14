# Feature Specification: PWA Dual-Tier / Org-Role Work

**Feature Branch**: `153-dual-tier-org-role`

**Created**: 2026-06-14

**Status**: Draft

**Input**: User description: "PWA dual-tier / org-role work (sub-project D): let the same human do their organisational-role workflow work on the phone by lighting up org-role work on the existing context switcher. Tier-aware session; reuse the existing switch-org re-mint + ContextChipSwitcher; the inbox surfaces org-role pending actions when in an org context framed as acting-as-Org; execute org-role actions under the platform/org token; entitlement-aware. No backend change."

**Source design**: `docs/superpowers/specs/2026-06-14-pwa-dual-tier-org-role-design.md` (sub-project D; depends on A).

## User Scenarios & Testing *(mandatory)*

A person who is both a citizen and a member of an organisation uses one wallet on their phone. Today
the phone only does their personal (citizen) work. This feature lets them switch into their
organisation and do the workflow work that is theirs **as a member of that organisation** (for
example, an analyst verifying an application), then switch back to personal — without ever weakening
the boundary between the two.

### User Story 1 - Know which capacity I'm acting in (Priority: P1)

At any time the wallet clearly shows whether the person is acting **personally** or **as a member of
a named organisation**, and switching between them is obvious and reliable.

**Why this priority**: Acting in the wrong capacity is confusing and risky; the indicator and a
reliable switch are the foundation everything else builds on.

**Independent Test**: With a member of at least one organisation, confirm the wallet shows the
current capacity (Personal vs. the org name) and that switching updates it.

**Acceptance Scenarios**:

1. **Given** a signed-in member of one or more organisations, **When** they look at the wallet,
   **Then** it shows their current capacity (Personal, or "acting as <Org>").
2. **Given** they switch to an organisation, **When** the switch succeeds, **Then** the capacity
   indicator updates to that organisation.
3. **Given** they switch back to Personal, **When** the switch succeeds, **Then** the indicator
   returns to Personal **and their personal/citizen surfaces keep working** (no loss of access to
   their own wallet/credentials).

---

### User Story 2 - See and do my organisation-role work (Priority: P1)

While acting as an organisation, the person sees the workflow actions that are theirs to do **in that
organisation role** and can complete them, clearly framed as organisation work.

**Why this priority**: This is the point of the feature — performing org-role workflow work on the
phone. It is the slice that proves the whole capability.

**Independent Test**: With a member who has an outstanding org-role action, switch to that
organisation, see the action in the inbox framed as org work, open it, and complete it successfully.

**Acceptance Scenarios**:

1. **Given** a person acting as an organisation with an outstanding org-role action, **When** they
   open the inbox, **Then** that action is listed and clearly framed as "acting as <Org>".
2. **Given** such an action, **When** they open and submit it, **Then** it is accepted (performed in
   the organisation capacity).
3. **Given** they are acting personally, **When** they open the inbox, **Then** org-role actions are
   not presented as personal work (capacity and listing stay consistent).

---

### User Story 3 - Switching keeps everything consistent (Priority: P2)

When the person switches capacity, the inbox, the outstanding-work count, and what they can do update
to match the new capacity, with no stale or cross-capacity leakage.

**Why this priority**: A switch that leaves the UI showing the previous capacity's work is
misleading; consistency makes the switch trustworthy. Builds on US1/US2.

**Independent Test**: Switch capacity and confirm the inbox + count refresh to the new capacity
without a manual reload.

**Acceptance Scenarios**:

1. **Given** the inbox is open, **When** the person switches capacity, **Then** the inbox and count
   refresh to the new capacity's outstanding work.
2. **Given** a switch fails (e.g. the server declines), **When** it fails, **Then** the current
   capacity is unchanged and the person is told it didn't switch.

---

### User Story 4 - Only offer capacities I'm entitled to (Priority: P3)

The wallet only offers organisations the person is actually a member of, and never lets a person
elevate into a capacity they aren't entitled to.

**Why this priority**: Safety/clarity guardrail; the server already refuses unentitled elevation, and
the UI should match it (only show real memberships).

**Independent Test**: A person with no org memberships sees only Personal; a member sees exactly
their organisations; an attempt to enter a capacity they lack is refused without elevating.

**Acceptance Scenarios**:

1. **Given** a person with no org memberships, **When** they look for a capacity switch, **Then**
   only Personal is available.
2. **Given** a member, **When** they open the switcher, **Then** exactly their organisations are
   listed.
3. **Given** the server refuses a capacity (not entitled / not a member), **When** the switch is
   attempted, **Then** it is declined and the person stays in their current capacity.

### Edge Cases

- **Return to Personal must restore personal access** — after acting as an organisation, returning to
  Personal must leave the person able to use their own wallet/credential surfaces (not stuck holding
  an organisation-only capacity).
- **Switch failure** — a declined or failed switch leaves the current capacity intact, with a clear
  message; never a half-switched state.
- **No org-role work** — acting as an organisation with nothing outstanding shows a friendly empty
  state, not an error.
- **Member with no personal wallet** — out of scope: such a member may see no personal actions;
  documented, not addressed here.
- **The capacity boundary must not be weakened** — a person can never reach an organisation capacity
  they aren't entitled to via the wallet.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wallet MUST always show the person's current acting capacity — Personal, or
  "acting as <Org>".
- **FR-002**: A member MUST be able to switch from Personal to any organisation they belong to, and
  back.
- **FR-003**: Switching to an organisation MUST put the person in that organisation's capacity for
  workflow work (so org-role actions can be performed).
- **FR-004**: Returning to Personal MUST restore the person's personal capacity such that their own
  wallet/credential surfaces continue to work (no residual organisation-only capacity).
- **FR-005**: While acting as an organisation, the inbox MUST surface the person's org-role
  outstanding actions, clearly framed as that organisation's work.
- **FR-006**: The person MUST be able to open and submit an org-role action while acting as that
  organisation, and it MUST be accepted.
- **FR-007**: On a capacity switch, the inbox and outstanding-work count MUST refresh to the new
  capacity without a manual reload.
- **FR-008**: A failed/declined switch MUST leave the current capacity unchanged with a clear message.
- **FR-009**: The wallet MUST offer only organisations the person is actually a member of, and MUST
  NOT allow elevating into a capacity the person is not entitled to (it relies on the server's
  refusal and does not work around it).

### Scope Constraints (carried from the design)

- **SCOPE-001**: No back-end change — reuse the existing organisation-switch + token re-mint and the
  existing pending-actions/execute surfaces.
- **SCOPE-002**: Single active capacity at a time (switch re-mints); holding personal + organisation
  capacities simultaneously is out of scope.
- **SCOPE-003**: An organisation-only member with no personal wallet is out of scope.
- **SCOPE-004**: The entitlement/audience boundary (server refusal of unentitled elevation) is NOT
  weakened by anything here.

### Key Entities

- **Acting capacity** — Personal (citizen) or a specific organisation; determines which token/tier is
  active and which work is shown.
- **Home (personal) session** — the person's personal/citizen capacity, restorable when returning to
  Personal.
- **Org-role outstanding action** — a workflow action that is the person's to do as a member of an
  organisation (reuses the existing outstanding-action concept).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A member can, on the phone, switch into their organisation, complete an org-role action,
  and switch back to Personal — **end to end in the wallet**, with no web app needed.
- **SC-002**: After returning to Personal, the person's own wallet/credential surfaces work in
  **100%** of cases (no residual organisation-only capacity).
- **SC-003**: On a capacity switch, the inbox + count reflect the new capacity **without a manual
  reload**.
- **SC-004**: A person can **never** enter an organisation capacity they are not entitled to via the
  wallet (the server refusal is always respected).
- **SC-005**: The current acting capacity is unambiguous to the person at all times.

## Assumptions

- The organisation switch + token re-mint already exists (`/api/auth/switch-org`) and re-mints at the
  capacity appropriate to the target org; this feature reuses it and adds restoring the personal
  capacity on return.
- The pending-actions and execute surfaces are capacity-agnostic and already return/accept the
  person's org-role actions (bound to their own wallet) when acting in the organisation capacity.
- Organisation membership listing already exists; the switcher reflects real memberships only.
- The entitlement/audience boundary is enforced server-side and is not modified here.
- A person is signed in to the wallet; sign-in itself stays personal/citizen.

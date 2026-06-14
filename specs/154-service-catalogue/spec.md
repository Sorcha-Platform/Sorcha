# Feature Specification: PWA Service Catalogue

**Feature Branch**: `154-service-catalogue`

**Created**: 2026-06-14

**Status**: Draft

**Input**: User description: "PWA service catalogue (sub-project B): let a citizen browse the services they can start and begin a new application, dropping into the existing fill/submit flow. Adds one consumer-tier catalogue read endpoint; replaces the empty Applications.razor stub with browse + start-new over the existing CreateInstance + ApplicationInstance."

**Source design**: `docs/superpowers/specs/2026-06-14-pwa-service-catalogue-design.md` (sub-project B; depends on A).

## User Scenarios & Testing *(mandatory)*

Sub-project A let a citizen do the work waiting on them. B is the other half of discovery: **start
something new**. Today the wallet's "applications" area is an empty stub. This feature lets a citizen
browse the services they can start and begin one, landing straight in the existing fill-and-submit
flow.

### User Story 1 - Start a service (Priority: P1)

A citizen opens the wallet, browses the services available to them, taps one (e.g. "apply for a blue
badge"), and is taken into filling and submitting its first step.

**Why this priority**: This is the whole feature and the minimum viable loop — without it there is no
way to begin a new application from the phone.

**Independent Test**: With at least one startable service available, open the catalogue, pick one,
confirm a new application begins and the first step's form is shown to fill and submit.

**Acceptance Scenarios**:

1. **Given** a signed-in citizen with startable services available, **When** they open the catalogue,
   **Then** they see a list of services, each with a name and a short description.
2. **Given** the catalogue is showing a service, **When** the citizen taps it, **Then** a new
   application is started and they are taken into its first step to fill and submit.
3. **Given** no startable services are available, **When** the citizen opens the catalogue, **Then**
   they see a clear, friendly empty state rather than a blank or broken screen.

---

### User Story 2 - Find a service (Priority: P3)

A citizen with many available services can search or filter to find the one they want.

**Why this priority**: Convenience once the catalogue grows; the browse loop (US1) works without it.

**Independent Test**: With several services, type a query and confirm the list narrows to matches.

**Acceptance Scenarios**:

1. **Given** several available services, **When** the citizen types a search term, **Then** the list
   narrows to services whose name/description matches.
2. **Given** a search with no matches, **When** the citizen searches, **Then** a clear "no matches"
   state is shown.

---

### Edge Cases

- **Only startable services appear** — services a citizen cannot initiate are not listed.
- **Catalogue load failure** — a transient failure shows a non-blocking message, not a blank/broken
  screen.
- **Start failure** — if starting a service fails, the citizen is told and stays in the catalogue
  (no half-started state shown as success).
- **Empty catalogue** — a friendly empty state with no error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wallet MUST present a catalogue of the services a citizen can start, each with a
  name and a short description.
- **FR-002**: The catalogue MUST list only services the citizen is actually able to start (not
  services that must be initiated by someone else).
- **FR-003**: A citizen MUST be able to start a listed service, which begins a new application and
  takes them into its first step to fill and submit (reusing the existing fill/submit flow).
- **FR-004**: The catalogue MUST show a clear empty state when no startable services are available,
  and a clear "no matches" state when a search returns nothing.
- **FR-005**: A transient catalogue-load failure MUST surface a non-blocking message and never a
  blank/broken screen.
- **FR-006**: A failure to start a service MUST be surfaced to the citizen, leaving them in the
  catalogue (no false "started" state).
- **FR-007**: Searching/filtering MUST narrow the list to services whose name or description matches.
- **FR-008**: The catalogue MUST be available to a citizen acting in their personal (consumer)
  capacity and scoped to the services available in their context.

### Scope Constraints (carried from the design)

- **SCOPE-001**: Adds exactly one consumer-tier catalogue **read** endpoint; starting a service
  reuses the existing create-application + fill/submit flow (no change to those).
- **SCOPE-002**: Org-role catalogues (services started as an organisation) are out of scope
  (sub-project D covers org-role work); this is the citizen/personal catalogue.
- **SCOPE-003**: Offline catalogue browsing is out of scope (could layer on sub-project C later).
- **SCOPE-004**: "Startable by a citizen" v1 = a service whose first step can be initiated by the
  citizen (an open first participant); curation rules beyond that are a later refinement.

### Key Entities

- **Catalogue service** — a startable service: a name, a short description, and what's needed to begin
  it (which service + where it runs).
- **Started application** — a new application instance begun from a catalogue service (reuses the
  existing application/instance concept).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen can, starting from the wallet, browse services and **begin a new application
  end-to-end** (into the first step's form) **without leaving the wallet**.
- **SC-002**: 100% of services shown are ones the citizen can actually start (no un-startable services
  listed).
- **SC-003**: Starting a service takes the citizen into its first step in **under a few seconds** for
  a typical service.
- **SC-004**: A catalogue-load or start failure **never** leaves the citizen on a blank/broken screen;
  they always get a clear message and a way forward.

## Assumptions

- A way to create a new application from a service already exists and is reused; this feature adds the
  catalogue (browse) and the start trigger, not the application machinery.
- "Startable by a citizen" is determinable from the service definition (an open first participant);
  v1 uses that, with curation as a later refinement.
- The catalogue is scoped to the citizen's context (their org/home); a citizen sees services relevant
  to them.
- A citizen is signed in to the wallet in their personal capacity.

# Feature Specification: AIAS Assured Identity with photo + autonomous Assure-ID agent

**Feature Branch**: `174-aias-assured-identity`

**Created**: 2026-06-29

**Status**: Draft

**Input**: User description: "M1 — AIAS Assured Identity with photo + autonomous Assure-ID agent. First build milestone of the AIAS conference demo."

> Program context: this is milestone **M1** of the AIAS conference demo. The program north-star —
> narrative, two-credential model, agent role, and M0–M5 decomposition — is
> `docs/superpowers/specs/2026-06-29-aias-conference-demo-design.md`. Read it first.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An anonymous person becomes assured, with their face on the credential (Priority: P1)

An anonymous attendee signs up, applies to **Acme Identity Assurance Services (AIAS)** for an
Assured Identity, includes a photo of themselves, and — once AIAS approves — receives an **Assured
Identity** credential carrying that photo into their wallet. This is the spine of the whole demo:
without a held, photo-bearing Assured Identity, nothing downstream (the cyber credential, the verify
moments) has anything to stand on.

**Why this priority**: It is the minimum viable slice — a person going from "nobody" to "holds a
real, AIAS-branded, photo-bearing credential." If only this ships, the demo already tells a
complete, compelling story on its own.

**Independent Test**: From a clean environment, sign up as a new person, complete the AIAS
application including a photo, and confirm an Assured Identity credential bearing that photo arrives
in the applicant's wallet and can be opened.

**Acceptance Scenarios**:

1. **Given** a freshly provisioned AIAS and a new anonymous user with a verified email, **When** they submit a complete application (real-looking name, an existing postcode, clean wording) and include a photo, **Then** AIAS approves it automatically and the applicant receives an Assured Identity credential whose portrait matches the photo they submitted.
2. **Given** an approved application, **When** the applicant opens the issued credential in their wallet, **Then** the credential is attributed to AIAS (AIAS branding/issuer) and displays the applicant's photo.
3. **Given** an applicant who chooses to skip the photo, **When** they submit an otherwise-valid application, **Then** the application may still be approved and a credential issued without a portrait (photo is optional at this stage).

---

### User Story 2 - AIAS turns down dodgy applications, with personality (Priority: P2)

The AIAS assurance decision is made **autonomously by an agent**, live, with no human in the loop.
When an application is implausible — a postcode that doesn't exist, profane/abusive details — the
agent **rejects** it and gives a humorous, on-brand reason. This is the demo's live-decisioning
theatre: the audience sees AIAS say "no" for honest, funny reasons.

**Why this priority**: The rejections are what make the autonomy *visible and entertaining* on
stage. The happy path alone looks like a rubber stamp; the rejections prove there's a real decision
being made.

**Independent Test**: Submit applications designed to fail each check (non-existent postcode; profane
details) and confirm each is rejected automatically with a distinct, human-readable, on-brand reason,
and that no credential is issued.

**Acceptance Scenarios**:

1. **Given** an application whose address/postcode cannot be found in a real lookup, **When** the agent evaluates it, **Then** it is rejected with a reason that names the unfound location (e.g. *"AIAS could not locate that address on any map"*) and no credential is issued.
2. **Given** an application containing profane or abusive details, **When** the agent evaluates it, **Then** it is rejected with an on-brand reason and no credential is issued.
3. **Given** an application whose email is not verified, **When** the agent evaluates it, **Then** it is rejected (or held) with a reason indicating email verification is required.
4. **Given** any decision (approve or reject), **When** the agent records it, **Then** the outcome and its reason are visible to the applicant and on the immutable record.

---

### User Story 3 - The whole of AIAS rebuilds from a clean network with one script (Priority: P2)

A demo operator can stand up AIAS — org, branding, the application workflow, and the running
assurance agent — on a **freshly wiped network** by running a **single, idempotent script**,
Docker-first and then on n1, and can re-run it after every network reboot without manual steps or
duplicate-state errors.

**Why this priority**: The network *will* be wiped repeatedly. If setup is a manual ceremony, the
demo is fragile and unrepeatable — which defeats the purpose. Repeatability is a stated,
non-negotiable constraint of the program.

**Independent Test**: On a clean Docker stack, run the provisioning script; confirm AIAS exists,
branded, with a live assurance agent, and that a test application flows through to a decision.
Re-run the script and confirm it succeeds without creating duplicates or erroring.

**Acceptance Scenarios**:

1. **Given** a clean network, **When** the operator runs the provisioning script, **Then** AIAS is created with its branding, the application workflow is published, and the assurance agent is running and reachable.
2. **Given** an already-provisioned AIAS, **When** the operator re-runs the script, **Then** it completes successfully without creating duplicate orgs/workflows/agents.
3. **Given** the provisioning succeeded on Docker, **When** the same script is pointed at n1, **Then** it provisions AIAS there using the same steps.

---

### Edge Cases

- **Offline / unavailable address lookup**: when the real postcode lookup cannot be reached (no
  internet at the venue, or the upstream service is down), the postcode check **falls back to a
  bundled fixture / allow-list** (configurable) so the assurance step still functions; it does not
  hard-fail the demo.
- **Photo present but unusable** (too large / wrong format): the existing capture control already
  resizes/validates; an unusable photo results in no portrait on the credential rather than a failed
  application (photo is optional at this stage).
- **Profanity false positive**: a legitimate but unusual name flagged as profane — the rejection
  reason must be clear enough that the operator can recognise and explain it on stage.
- **Agent not running**: if the assurance agent is down, applications sit undecided (no silent
  auto-approval); the operator can see they are pending.
- **Duplicate application** from the same person: handled without crashing; the latest application is
  the one decided.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provision an organisation **"Acme Identity Assurance Services (AIAS)"** with recognisable AIAS branding presented to applicants and on the issued credential, without depending on any unfinished org-branding administration feature.
- **FR-002**: An anonymous person MUST be able to sign up and submit an AIAS Assured Identity application that captures their identity details (including name and address/postcode) and **optionally** a photo of themselves.
- **FR-003**: The application MUST allow the applicant to capture their photo by camera or by uploading a file, and MUST allow them to skip the photo.
- **FR-004**: The assurance decision MUST be made **autonomously by an agent** with no human approval step in the demo path.
- **FR-005**: Before approving, the agent MUST evaluate, at minimum: (a) the applicant's **email is verified**; (b) whether a **photo is present** (recorded as a signal; not required for approval at this stage); (c) the **address/postcode exists** per a real lookup; (d) the submitted details **contain no profanity/abuse**.
- **FR-006**: The address/postcode check MUST use a real external lookup when available, and MUST **degrade gracefully to a bundled offline fallback** (configurable) when the lookup is unreachable, so the assurance step keeps working without internet.
- **FR-007**: On approval, the system MUST issue an **Assured Identity credential** attributed to AIAS that carries the applicant's submitted photo as its portrait (when a usable photo was provided), and the credential MUST be delivered to and claimable by the applicant's wallet.
- **FR-008**: On rejection, the system MUST record a **decision with a human-readable, on-brand (humorous) reason**, MUST NOT issue a credential, and MUST surface the reason to the applicant.
- **FR-009**: Every assurance decision (approve or reject) and its reason MUST be recorded on the immutable record and be attributable to AIAS.
- **FR-010**: The entire AIAS setup — organisation, branding, application workflow, and the running assurance agent — MUST be reproducible from a clean network by a **single idempotent provisioning script**, runnable Docker-first and then against n1, with no manual steps and safe to re-run.
- **FR-011**: The milestone MUST include an automated **test/rehearsal hook** that exercises at least one approval and one rejection end to end against the provisioned environment.
- **FR-012**: The feature MUST NOT regress existing assured-identity / agent behaviour relied upon by other walkthroughs (the existing always-approve demo remains available; the AIAS variant adds the real checks and the reject route).

### Key Entities

- **AIAS Organisation**: the fictional assurance provider; issuer of the Assured Identity credential and owner of the application workflow; carries demo branding.
- **Applicant**: an anonymous person who signs up and applies; becomes the holder of the issued credential.
- **Assured Identity Application**: the submitted application — identity details, address/postcode, optional photo — and its state (submitted / approved / rejected).
- **Assurance Decision**: the agent's outcome (approve/reject) plus the reason; recorded immutably.
- **Assure-ID Agent**: the autonomous decision-maker that applies the assurance checks and records decisions; runs continuously.
- **Assured Identity Credential**: the issued credential attributed to AIAS, optionally carrying the applicant's portrait.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a clean network, a single script makes AIAS demo-ready (org + branding + workflow + running agent) in **one run with no manual steps**, and re-running it produces no duplicates or errors.
- **SC-002**: An applicant can complete the application (including capturing a photo) in **under 2 minutes**.
- **SC-003**: After an application is submitted, the autonomous agent reaches and records a decision within **30 seconds**, with no human intervention.
- **SC-004**: A non-existent postcode and profane details are each **rejected 100% of the time**, each with a distinct, human-readable, on-brand reason, and **no credential is issued** in those cases.
- **SC-005**: On approval with a photo, the issued credential carries a portrait that visibly **matches the submitted photo**, and arrives in the applicant's wallet ready to open.
- **SC-006**: The complete approve-and-reject flow runs **identically on Docker and on n1** from the same script.
- **SC-007**: With no internet available, the application + decision flow still completes (postcode check uses the offline fallback) — the demo does not break offline.

## Assumptions

- **Anonymous signup with email verification already exists** and is reused; "email verified" is a state the agent can rely on at application time.
- **Photo capture, resize, and embedding into a credential already exist** (the shared file-capture control with camera/upload and the credential portrait-embed path); this milestone reuses them rather than building new capture/embed infrastructure.
- **An autonomous agent runtime already exists** (rules-based decisioning over pending actions); this milestone reuses it and adds AIAS-specific rules plus an external-check capability (email/postcode/profanity), not a new agent.
- **The existing assured-identity application workflow is the starting point**; this milestone adds a real reject route and the real checks (the prior demo auto-approved everything).
- **Branding is delivered at the demo level** (e.g. baked into the workflow / issuer presentation), not via the unfinished organisation-branding administration feature.
- **Address lookup targets UK postcodes** via a public lookup, with a bundled offline fallback dataset for venues without reliable internet.
- **Persona-level photo persistence is explicitly out of scope** (the settled model does not need it); the photo lives on the application/credential, not on a stored persona record.
- **Downstream milestones depend on this**: the cyber questionnaire (M2) consumes the Assured Identity credential; this spec stops at a held, photo-bearing Assured Identity.

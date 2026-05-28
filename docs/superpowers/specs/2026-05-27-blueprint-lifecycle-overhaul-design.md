# Blueprint Design Lifecycle Overhaul — Design

**Date:** 2026-05-27
**Status:** Approved (brainstorming) — ready for spec-kit
**Authors:** Stuart Fraser + Claude

---

## Problem

A responsible admin in an authority/control wants to stand up a process that lets *certified individuals apply for a service or grant* — and then keep it running. Today the tooling to do that is real but **incoherent**: it's a toolbox of parallel surfaces, not a journey, and it never teaches the newcomer what they're actually doing.

Concretely, three disconnected surfaces exist:

1. **Designer** (`/designer/blueprint`) — three *parallel tabs*: AI chat → Diagram (topology) → Preview (per-action form). They share state but sit side-by-side as tools.
2. **Publish** (`/blueprints` → a 4-step wizard: validate → pick register → rights check → publish) + a Versions dialog. A separate page.
3. **Run** (`/new-submissions` → `/new-submission/{reg}/{bp}`) — a *separate* area where a published blueprint is instantiated and walked.

The gaps that create the "mental leap":

- **No teaching of the lifecycle.** The designer is a set of tabs, not a staged path. Nothing communicates "you are here / this is what comes next."
- **No safe way to *try* a service before it's live.** `DevMode` exists only as a *register-level* flag that disables field encryption — it is **not** a blueprint test/simulation concept. To walk a flow today you must publish to a register, then leave the designer for `/new-submissions`. There is no "rehearse, then promote" loop.
- **Weak visualisation.** Diagram and Preview are disconnected; a node graph does not convey "certified people apply → officer reviews → grant issued" to a non-technical admin.
- **Half-built surfaces** reinforce the incoherence: the designer's **Load**, **Export**, and validation-popover are stubbed.

Register-publication governance already exists and is good; it must be preserved.

## Goal

Make the **lifecycle itself the UI**: one coherent, staged golden path that teaches the mental model as the admin moves through it, turning a plain-English intent into a **live, governed public service** — and supports amending that service over time.

The golden path:

> **Describe** (guided AI) → **Understand** (see it as a human journey) → **Rehearse** (walk it safely end-to-end) → **Go live** (existing governance gate) → **(loop)** amend → new version → re-rehearse → re-publish.

## Approach (chosen)

**Workspace + persistent "lifecycle rail"** (chosen over a linear wizard and over an adaptive wizard/workspace hybrid).

One designer workspace wrapped in an always-visible rail: `Describe → Understand → Rehearse → Go live`. The author moves freely (design is iterative), but the rail constantly shows *you-are-here / what's next / what's gated*, and **enforces order only where it matters** — Go live is locked until a rehearsal passes. A dismissible first-run guided overlay rides the rail for newcomers (the one idea borrowed from the adaptive approach), avoiding two separate UIs.

This **unifies today's three designer tabs and the publish wizard** into one staged workspace. The citizen-facing run area (`/new-submissions`) stays separate — it's for real applicants — but Rehearse *reuses* its components internally.

### Decisions locked during brainstorming

| Decision | Choice |
|---|---|
| Rehearse model | **Both** — a quick in-designer dry-run (in-memory engine) *and* a full end-to-end rehearsal on an auto-provisioned devMode test register. |
| Go-live model | **Auto test register + first-class Promote** — the journey auto-provisions a private devMode test register; "Go live" promotes the *exact rehearsed version* through the existing publish-governance gate to a chosen live/public register. The admin never hand-manages test registers. |
| Visualisation | **Journey-first** (plain-language story), with a **graph toggle** ("Show technical flow") for power users. |
| On-ramp | **AI-chat-first, but a guided interviewer** — opens with a directed-build option (recognisable patterns) or refines sector → purpose → participants → prerequisites. Not a blank box; not a separate template gallery. |
| Form surface | The **production `SorchaFormRenderer`**, in both preview and an **edit mode** — WYSIWYG, identical to execution. |
| Rail UI | Single compact line; gating/explanatory copy lives in **hover tooltips**, not permanent text. |
| Layout authoring | Tools write **standard `x-*` keywords** onto the action `dataSchemas` — no proprietary format; UI-edited and hand-edited blueprints are identical on disk. |

## The five stages (+ form authoring, + amend loop)

### Shell & lifecycle rail

- One workspace: a header (blueprint title, save/dirty state), a compact one-line rail, and a body split into a **persistent AI chat** (left) and a **stage canvas** (right) that changes with the active stage.
- Rail states per stage: done (✓), current, available, **locked** (🔒 with a hover tooltip explaining why).
- **Gating rules** (the teaching mechanism):
  - *Understand* unlocks once a blueprint exists.
  - *Rehearse* unlocks once the blueprint validates (no blocking errors).
  - *Go live* stays **locked until at least one successful full rehearsal**; the lock tooltip explains it.
- First run: a dismissible guided overlay on the rail.

### Stage 1 · Describe (guided AI on-ramp)

- Opens in the chat. Two ways in, both *through the chat*:
  1. **Directed build** — the AI's opening offers recognisable starting points as chips ("Apply for a grant", "Apply for a permit/licence", "Certify, then apply", "Something else"); picking one seeds the conversation with that shape.
  2. **Conversational refine** — the AI interviews in plain language: sector → purpose → who applies → who decides → must they prove something first → what do they receive.
- As answers land, the **journey builds live in the canvas** — the newcomer watches the service form.
- The AI does **concept translation** silently: "must be a certified resident" → credential-gated open starting action; "an officer decides" → known participant + review action; "they get a grant" → `credentialIssuanceConfig` with `SorchaLocalWallet` delivery. The admin never types `isStartingAction` or `credentialRequirements`.

### Stage 2 · Understand (journey-first, graph on demand)

- The **journey** is the default canvas: role-coloured step cards in plain language.
- Two concept annotations as plain badges: **🛡 "Must prove: <type>"** (from `credentialRequirements`) and **🎓 "Issues: <type>"** (from `credentialIssuanceConfig`).
- **Clicking a step opens its detail**: what that participant *sees* (disclosure rules), what they *decide*, and **the screen they fill** (the form). This is where today's separate Preview tab merges into the journey.
- **"Show technical flow" toggle** swaps the journey for the node graph (routes, conditional/reject branches) — for power users / debugging.

### Stage 2a · Form-layout authoring (the canonical renderer, edit mode)

- **Preview ⇄ Edit layout is the same `SorchaFormRenderer`** — what you arrange is exactly what citizens get.
- **Imported / AI-generated schemas with no layout are first-class**: the renderer infers controls from JSON-Schema types/formats (`FormSchemaService.InferControlFromSchema`), so a layout-less schema works immediately; the author may then redo the layout.
- The full `x-*` toolkit is applyable: `x-sections` (incl. horizontal grouping), `x-pages` (wizard), `x-width`, `x-introduction`, `x-persona` (autofill, with per-field opt-out), `x-file` (capture/resize), `x-review` (id-card page), `x-credential-offer`. Tools are *offered by field shape* (email → persona email; a file-reference field → x-file) but always overridable.
- **Drivable by chat too** — direct manipulation and AI edits write to the *same* schema and stay in sync.

### Stage 3 · Rehearse (try it safely, then unlock Go live)

Two modes sharing the **same stepper UI** (only the backend differs):

- **Quick dry-run** — runs the portable `Sorcha.Blueprint.Engine` (validate→calculate→route→disclose) in-memory; no register, no sealing. For tight iteration. A simulation, so it won't catch encryption/sealing/delivery edge cases.
- **Full rehearsal** — one click **auto-provisions a private devMode test register** (+ per-role test wallets), publishes the blueprint there, and walks the **real** pipeline. A prominent banner makes "private sandbox, nothing public" unmistakable, with Reset/delete (the test register is disposable).

Shared devices:
- **Role switcher** — "You're acting as: <role>" — so one author can walk a multi-party flow end-to-end.
- **Rehearsal log** — plain-language but showing real artifacts (test register provisioned, gate passed, tx sealed in docket N, routed), to earn trust that the rehearsal reflects production.
- Completing a **full** pass sets `rehearsalPassed` → **auto-unlocks Go live**.

### Stage 4 · Go live (Promote through existing governance)

- Reachable only after a passing rehearsal; the header carries the proof ("✓ Rehearsal passed on '<sandbox>'").
- **Promotes the exact rehearsed version** — no divergence between tested and shipped.
- **Register picker is a drop list** of registers the author could publish to. Selecting one reveals a **system-info detail card** from the register's own metadata: **owned by** (this node vs remote owner), **validation** (validator count + required signatures, F086 roster), **visibility** (public/private), **sync state** (F108), **mode** (live vs devMode), **published-count**, and **the author's governance role** (Owner/Admin/Designer/none). No-rights registers are visibly blocked.
- **Review** shows the service name, journey, what it issues, and the version.
- **Permanence + versioning** stated plainly: it becomes a public service; it's immutable; to change it you create a new version and rehearse again before re-publishing.
- Reuses the existing publish-governance substance (validate → register → rights → review); validation is already green from rehearsal, so the steps collapse into one confirmation with governance intact.

### Amend-and-republish loop

- Open an already-published service → it becomes **version N+1 (draft)** → Understand → Rehearse → Go live re-publishes v+1 to the **same** register (governance re-checked).
- One Go-live screen serves both first-publish and re-publish.
- Depends on **wiring the currently-stubbed Load** + a version-derivation (clone-to-draft) step.

## Code mapping (reuse / refactor / new)

| Area | Disposition |
|---|---|
| Shell & rail | *Refactor* `DesignerBlueprint.razor` (replace 3-tab `MudTabs` with rail + stage canvas). *New* `LifecycleRail`. *Extend* `DesignerContext` (current stage, `rehearsalPassed`, version). *Reuse* `DesignerToolbar`; absorb its stubbed Export/validation-popover into stages. |
| Describe | *Reuse wholesale* `AiDesignerPane` + `ChatHubConnection` + `BlueprintUpdated → DesignerContext.ApplyAiUpdate`. *New (mostly backend)* guided opening + plain-language→concept translation in the Blueprint Service chat agent; directed-build chips; live journey render. |
| Understand | *New* `JourneyView` (role-coloured cards + gate/issue chips) — a thin read-model mapper over the blueprint. *Reuse* `DiagramPane` (Blazor.Diagrams) behind the toggle; *reuse* `FormPreviewPane`/`SorchaFormRenderer` for step detail. |
| Form authoring | *Reuse* `SorchaFormRenderer` in a new **edit mode**; *reuse* `FormSchemaService.InferControlFromSchema`. *New* layout tools writing `x-*` keywords; import-schema entry; chat tools for the same edits. **(Heaviest net-new UI.)** |
| Rehearse | *Quick dry-run*: *new* in-WASM harness over `Sorcha.Blueprint.Engine` + in-memory instance/action store shim + stepper/role-switcher. *Full rehearsal*: *new* orchestration reusing register creation + devMode, per-role test wallets, `PublishBlueprintToRegisterAsync`, and the real run components (`NewSubmissionWorkspace`/`ActionWorkspace`/`IWorkflowService.SubmitActionExecuteAsync`) with role-switching; *new* rehearsal-state + sandbox reset/delete. **(Heaviest net-new logic.)** |
| Go live | *Refactor* `PublishBlueprintWizard` into the rail's terminal stage. *Reuse* `IRegisterGovernanceService.GetGovernanceRosterAsync`, `IRegisterReadService`/subscription, `PublishBlueprintToRegisterAsync`, `ValidateBlueprintAsync`. *New* register-info aggregate for the detail card (F108 relationship + sync-state, F086 roster, visibility, devMode, published-count). |
| Amend loop | *Fix* stubbed `Load` in `DesignerToolbar.razor.cs`. *New* "open published → derive v+1 draft" (versions API + clone-to-draft); re-publish via Go live with version bump. |

**Boundary preserved:** the citizen-facing run UI (`/new-submissions`) is unchanged for real applicants; Rehearse reuses its components in a sandbox rather than forking them.

**Effort/risk hotspots:** (1) full-rehearsal orchestration (test-register provisioning + per-role test wallets + role-switching signing); (2) form-authoring edit mode; (3) the chat agent's plain-language→concept + `x-*` tool calls. Shell, journey, and Go-live are largely refactor+reuse.

## Testing

Per the `sorcha-ui` TDD discipline (page object → Playwright test → component, against Docker), `data-testid` on every new interactive element.

- **E2E (Playwright, Docker)** — new `[Category("Designer")]` / `[Category("Lifecycle")]`:
  - **Rail invariant**: Go live disabled until a rehearsal passes; unlocks when one does.
  - Per-stage loads clean (console/network/CSS health from `AuthenticatedDockerTestBase`).
  - Describe: guided opening + directed-build chip seeds a blueprint; journey renders live.
  - Understand: journey renders; step-click opens form detail; graph toggle.
  - Form authoring: layout-less imported schema renders default fields; applying `x-sections`/`x-pages` visibly changes the form; persona/file affordances appear by field shape.
  - Rehearse: dry-run steps through; full rehearsal provisions sandbox, role-switch walks all participants, completion unlocks Go live.
  - Go live: register dropdown + detail card populate; no-rights register can't publish; publish produces a version; amend loop reopens a published service as a v2 draft.
- **Component (bUnit)** — `LifecycleRail` gating states; `JourneyView` chip mapping; form-authoring tools write the correct `x-*` keywords.
- **Unit/engine** — dry-run harness routing/disclosure correctness; a **fidelity test** that dry-run and full-rehearsal produce equivalent step sequences for a sample blueprint; register-info aggregate; blueprint→journey mapper.
- **Leverage:** full rehearsal exercises the real execution path (doubles as an integration test of submission/sealing/issuance). `SorchaFormRenderer` coverage is extended, not replaced.

## Dependencies & open questions

- **Wire the stubbed Load** in `DesignerToolbar.razor.cs` (blocks the amend loop and reopening any draft).
- **Test-register auto-provisioning** service: create devMode register + per-role test wallets, publish, and teardown — likely Blueprint- or Register-service orchestration. Confirm where it lives and how test wallets are minted/funded.
- **Register-info aggregate**: confirm which system-info fields are already exposed (F108 local-relationship + sync-state, F086 validator roster, visibility, devMode) and whether one new aggregate read endpoint is needed.
- **Chat agent** changes (guided interviewer, concept translation, `x-*` layout tools) — extent of backend work in the Blueprint Service chat agent.
- **Full-rehearsal signing**: one author signing as multiple participant wallets in the sandbox — confirm the test-wallet model and that open-participant late-binding behaves under role-switching.

## Out of scope

- Changes to the citizen-facing run/execution UI (`/new-submissions`) beyond reuse in rehearsal.
- New credential formats, trust, or governance mechanics (F135/F136 are upstream and unchanged).
- Multi-author/collaborative editing of a blueprint.
- Any change to register genesis/federation.

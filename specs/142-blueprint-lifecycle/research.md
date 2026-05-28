# Research: Blueprint Design Lifecycle Overhaul (142)

Phase 0 research resolving the unknowns and the two clarify-deferred items. Each decision cites the integration facts it rests on. Source map: the designer is reuse-heavy; the heavy net-new pieces are the rehearsal harness, the server-side publish gate, and form-layout authoring.

---

## D1 — Test-register provisioning & "disposable" sandbox

**Unknown**: Which service provisions the rehearsal test register, and how is it "disposable" given registers have **no HTTP delete** and `RegisterManager.DeleteRemotelyAsync` refuses to delete locally-owned (SyncState=null) registers?

**Decision**: A **reusable per-organisation sandbox register** (lazily auto-provisioned on first full rehearsal), where **"disposable / reset" means discarding the rehearsal's instances + ephemeral identities, not deleting the register**. Provisioning, role-play, and reset are orchestrated by a new **Rehearsal orchestration in the Blueprint Service** (it already owns publish + `ActionExecutionService` execution).

- Provision: lazily create one devMode register per org via the existing initiate/finalize ceremony (`IRegisterServiceClient.CreateRegisterAsync` → `/api/registers/initiate` + `/finalize`), tagged `sandbox=true` in `Metadata`, owned by the org. Reused across rehearsals.
- Each rehearsal runs as a **fresh instance** on that register with **fresh ephemeral per-role wallets**; "reset/delete" clears that rehearsal's instance state and discards its sandbox wallets.
- Sandbox registers are excluded from the Go-live target picker and from normal register listings (filter on the `sandbox` tag).

**Rationale**: Per-rehearsal register creation is heavy (each register needs a genesis + validator key, and is subject to the F137 genesis time-box) and cannot be torn down (no delete path). A reusable devMode sandbox sidesteps both: genesis happens once per org, and "disposable" is satisfied at the instance/identity layer, which we *can* clear. This honours FR-019/FR-020/SC-008 (isolation; resettable; never writes to a live register) while being cheap to repeat (SC-003 <5 min).

**Refines spec**: FR-019 "auto-provisioned" = lazily once per org then reused; FR-020 "disposable" = the rehearsal's instance + ephemeral identities are disposable, the sandbox register persists privately. No contradiction — isolation and resettability hold.

**Alternatives rejected**: (a) per-rehearsal fresh register — genesis cost + no teardown path + time-box risk; (b) a brand-new "ephemeral register with TTL" subsystem — large net-new for no extra user value over a reused sandbox; (c) in-memory-only full rehearsal — would not exercise the real seal/encrypt/deliver pipeline (defeats the point of "full").

**Net-new**: sandbox-register tag + provisioning helper; sandbox exclusion filter in register listing/picker; rehearsal-instance reset (clear instance + discard ephemeral wallets).

---

## D2 — "Act as each participant" signing

**Unknown**: How does one administrator sign as multiple participants in a full rehearsal? (Clarified: system-managed ephemeral identities.)

**Decision**: The Rehearsal orchestration mints **ephemeral per-role sandbox wallets** via `IWalletServiceClient.CreateWalletAsync` (owner = a synthetic `sandbox:{rehearsalId}:{role}`, tenant = org), and **signs server-side as the acting role** via `IWalletServiceClient.SignTransactionAsync` when the administrator submits a step in that role. This reuses the existing server-custodied signing already used by `ActionExecutionService`/credential issuance.

**Rationale**: High fidelity (real signatures → real late-binding + credential-gate behaviour, FR-022) with zero wallet knowledge required of the admin. Ephemeral wallets are discarded on reset (D1).

**Alternatives rejected**: single author identity for all roles (would not exercise late-binding/multi-party distinctions); author-supplied test wallets (burdens a non-technical admin).

**Net-new**: per-role ephemeral wallet minting + a role→wallet map on the rehearsal; server-side "sign as acting role" in the rehearsal path.

---

## D3 — Quick dry-run harness (in-WASM)

**Unknown**: How to run a no-register dry-run; what it omits.

**Decision**: Reuse the portable `Sorcha.Blueprint.Engine` (`ExecutionEngine.ValidateAsync` → `ApplyCalculationsAsync` → `IRoutingEngine.RouteAsync` → `IDisclosureProcessor.ProcessAsync`) in the WASM client with **new in-memory `IInstanceStore`/`IActionStore` stubs** holding the dry-run's accumulated state. The dry-run covers validation, calculations, routing, and disclosure only; it **does not** exercise credential prerequisites or issuance (Clarified Q3) and marks those steps "checked in full rehearsal".

**Rationale**: The engine is dependency-free (no HttpClient) and confirmed WASM-portable; this gives instant iteration with no backend round-trip. Credential proof/issuance are exactly the cryptographic/delivery behaviours that need the real pipeline, so they belong to the full rehearsal (D1/D2).

**Gap vs full execution (documented)**: `ActionExecutionService` adds state reconstruction from sealed transactions, transaction building/encryption/signing, mempool submission, credential verification/issuance, and register/validator coordination — none of which the dry-run performs.

**Net-new**: in-memory store stubs; a dry-run stepper that drives the engine and renders per-step routing/disclosure outcomes; the shared stepper/role-switcher UI (also used by full rehearsal).

---

## D4 — Rehearsal-pass record & server-side soft gate (FR-032)

**Unknown**: How the server enforces "published executable definition was rehearsed", overridable + audited, given **publish-rights are UI-only today**.

**Decision**: Introduce two server-side concepts in the Blueprint Service publish path:
1. **Executable-definition hash** — a canonical hash over the Blueprint's *executable definition* (participants, actions, routes, data schemas, calculations, disclosures, credential prerequisites/issuance, and behavioural form keywords), **excluding presentational layout keywords** (D7). Computed identically client- and server-side.
2. **RehearsalPass record** — persisted `{ blueprintId, execDefHash, rehearsedAt, by }` written when a full rehearsal completes successfully.

At publish: enforce **governance rights hard** (roster role check, now server-side — see D5) and the **rehearsal gate soft** — if no `RehearsalPass` matches the publishing version's `execDefHash`, block with a warning unless the caller holds register publish-governance authority and explicitly confirms an override; persist an **PublishOverride audit record** `{ who, when, blueprintId, version, registerId, execDefHash, reason? }`.

**Rationale**: Makes SC-002 real (gate enforced where it cannot be bypassed) while honouring the Clarified hybrid model (overridable by an authorised user, audited). Hashing the *executable* definition (not full content) implements Q4 — presentational edits don't invalidate the pass.

**Storage**: `RehearsalPass` + `PublishOverride` persisted durably (Postgres, Blueprint Service) so they survive reload and satisfy audit; registered via the F113 `IStorageRegistrationLog` pattern. Rail "current stage" stays ephemeral UI state.

**Net-new**: exec-def canonical hash; `RehearsalPass` + `PublishOverride` stores; server-side publish guard (rights hard + rehearsal soft + override + audit).

---

## D5 — Server-side publish-rights enforcement

**Unknown**: Publish rights are checked UI-side only (`PublishBlueprintWizard` step 3); is there a server guard?

**Decision**: Add a **server-side governance check at the publish endpoint** using the existing `GetGovernanceRosterAsync` roster (Owner/Admin/Designer) before any publish proceeds (refuse with a clear reason, no record written — FR-027). The UI check remains as a convenience mirror.

**Rationale**: Required for FR-027/FR-032 to be trustworthy; a UI-only check is bypassable. Reuses the existing roster read; no new governance mechanics (constitution: surface, don't invent governance).

**Net-new**: the server-side guard in the publish path (the roster read already exists).

---

## D6 — Go-live register system-info card

**Unknown**: Six fields; which exist; aggregate vs separate.

**Decision**: **Client-side aggregation** over existing reads, plus one small addition for visibility:
- Ownership/relationship → `GetLocalRelationshipAsync` ✓
- Sync-state → `GetSyncStateAsync` ✓
- Validator roster + required signatures → from `GetGovernanceRosterAsync` (validator members) ✓
- DevMode → `GetRegisterAsync` (`Register.DevMode`) ✓
- Published count → `GetPublishedBlueprintsAsync` (count of `.Blueprints`) ✓
- Visibility (public/private) → **net-new read**: surface the register's `Advertise` flag on the register read response (`GetRegisterAsync`) rather than adding a new endpoint.

**Rationale**: Five of six already exist; adding `Advertise` to the existing register read is the smallest change. A new aggregate endpoint is unnecessary at this scale (one card, on demand); if profiling later shows latency, an aggregate can be added without changing the contract shape.

**Net-new**: expose `Advertise`/visibility on the register read; a UI view-model that fans out the (now six) reads for the detail card.

---

## D7 — Presentational vs behavioural form keywords

**Unknown**: Which form keywords are presentational (don't re-lock) vs behavioural (re-lock), per Q4.

**Decision**: Initial classification (also recorded in the spec Dependencies):
- **Presentational** (excluded from exec-def hash; do not re-lock): `x-pages`, `x-sections`, `x-width`, `x-introduction`, `x-review`, `x-address-lookup`, `x-persona` (autofill binding).
- **Behavioural** (part of exec-def; re-lock): `x-file` (file-reference → chunked/encrypted transactions), `x-credential-offer` (claim flow), and anything altering data submitted, transactions produced, or credentials consumed/issued.

A single shared classifier MUST be used by both the exec-def hashing (D4) and the re-lock logic (FR-023) so client and server agree.

**Rationale**: `x-file`/`x-credential-offer` change the executed pipeline; the rest only change presentation. Centralising the list prevents client/server drift.

**Open for confirmation in planning**: `x-persona` is treated presentational (it pre-fills values but does not change the schema/validation or what is structurally submitted); revisit if autofill is later made to alter submitted data shape.

**Net-new**: the shared keyword classifier.

---

## D8 — Form-layout authoring (edit mode + x-* read/write)

**Unknown**: Does the renderer support an edit mode / x-* authoring today?

**Decision**: The renderer is **render-only** today and there is **no x-* authoring**. Build an **edit mode** layered on the production `SorchaFormRenderer` (Preview ⇄ Edit toggle) that reads the current layout and **writes standard `x-*` keywords** onto the Action `dataSchemas`. Default rendering of layout-less schemas reuses `IFormSchemaService.AutoGenerateForm`. The same layout operations are exposed as **new chat tools** (D9) so direct-manipulation and AI edits converge on one schema (FR-016/FR-017).

**Rationale**: WYSIWYG fidelity (FR-013) by reusing the production renderer; standard `x-*` keeps hand- and UI-edited blueprints identical on disk (FR-017).

**Net-new**: edit-mode affordances on the renderer; x-* read+write layer; layout-action tools.

> Note: some `x-*` rendering may already exist in the renderer (e.g. review summary); this plan adds *authoring*. Planning/tasks must confirm which keywords already render before building writers.

---

## D9 — Guided AI on-ramp & layout tools (chat agent)

**Unknown**: Where the chat agent lives; how to add the guided interviewer + x-* tools.

**Decision**: Extend the existing Anthropic-backed agent (`ChatHub` → `IChatOrchestrationService` → `BlueprintToolExecutor`, 16 tools, `AnthropicProviderService`). Add:
- A **guided opening** (interviewer system-prompt behaviour + directed-build starting points) — opening-turn/prompt change, plus surfacing directed-build chips in `AiDesignerPane`.
- **Layout tools** mirroring D8's operations (e.g. `set_form_layout`/`set_field_autofill`/`set_review_page`) writing the same `x-*` keywords.
- Concept-translation already happens via existing tools (`require_credential`, `issue_credential`, `add_action`, `set_disclosure`); the guided prompt drives them from plain-language answers (FR-012).

**Rationale**: Reuses the whole agent scaffold; new capability is additive tool definitions + an opening behaviour.

**Net-new**: guided opening behaviour; directed-build chips; layout tool definitions + handlers.

---

## D10 — Amend loop: clone published → draft + wire Load

**Unknown**: Clone-to-draft and the stubbed Load.

**Decision**:
- **Wire Load** (small): `DesignerToolbar` opens a load dialog and calls `Context.SetBlueprint(...)` using the existing `GetBlueprintAsync(id)`.
- **Clone published → draft** (net-new): fetch the published Blueprint JSON (`GetPublishedBlueprintsAsync`/version read), create a new draft (`SaveBlueprintAsync`) carrying version lineage (target register + prior version), and open it in the workspace with Go-live re-locked pending a fresh rehearsal (D4). Re-publish uses the existing publish path to the same register with a version increment (FR-030).

**Rationale**: Load is just dialog wiring; clone-to-draft is the only genuinely new amend primitive, and it composes the existing publish/version APIs.

**Net-new**: load dialog wiring; clone-published-to-draft (with lineage); "amend" entry from the services list.

---

## Cross-cutting: terminology & observability

- **Ubiquitous language** (constitution): keep Blueprint / Action / Participant / Disclosure / Publish for the constructs; "Rehearsal", "Sandbox register", "Go live", "Journey" are additive user-facing terms.
- **Observability**: new metrics on a `Sorcha.Blueprint.Designer` (or existing Blueprint) meter — `rehearsal_run_total{mode,outcome}`, `rehearsal_duration_seconds`, `publish_override_total`, `sandbox_provision_total`; structured logs for overrides (audit) and sandbox provisioning/teardown.

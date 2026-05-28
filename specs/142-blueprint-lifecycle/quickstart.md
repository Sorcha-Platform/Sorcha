# Quickstart: Blueprint Design Lifecycle Overhaul (142)

How to exercise and validate the feature end-to-end. Assumes the Docker stack is up (`docker-compose up -d`) with the UI at `http://localhost:5400` and gateway at `http://localhost:80`; admin `admin@sorcha.local` / `Dev_Pass_2025!`.

## The golden path (manual acceptance walk)

1. **Describe** — open the designer (`/app/designer/blueprint`). The assistant opens as a guided interviewer: pick a directed-build start ("Apply for a grant") or answer sector → purpose → who applies → who decides → must they prove something. Watch the **journey build live** in the canvas. (US4 / FR-010–012)
2. **Understand** — the journey shows role-coloured steps with a **🛡 "Must prove"** badge on the gated start and a **🎓 "Issues"** badge on the issuing step. Toggle **Show technical flow** to see the route graph; click a step to see its disclosure, decision, and form. (US1 / FR-006–009)
3. **Author a form** — open a step whose schema has no layout; confirm it renders with type-inferred fields in the production renderer. Group fields into a section, split into wizard pages, bind an email field to profile autofill. Ask the assistant to make an equivalent change and confirm it converges. (US5 / FR-013–017)
4. **Rehearse** —
   - *Quick dry-run*: step through the flow switching acting role; routing/disclosure shown; credential steps marked "checked in full rehearsal", no register created. (FR-018)
   - *Full rehearsal*: start it; confirm a private devMode **sandbox register** is provisioned (or reused) and clearly marked "nothing public"; walk all roles via the **role switcher**; watch the log show real events (gate passed, **tx sealed in docket N**, routed, credential delivered to the test wallet). Reaching the end **unlocks Go live**. Reset and confirm the rehearsal instance/identities are discarded. (US2 / FR-019–023)
5. **Go live** — open Go live; pick a target register from the drop list (sandbox registers are absent). Selecting one shows the **system-info card** (owner, validators + required sigs, visibility, sync state, mode, published count, your role). Confirm a no-rights register is blocked. Publish; confirm a versioned immutable record is created. (US3 / FR-024–028, FR-032)
6. **Amend** — reopen the now-live service; confirm it becomes a **new draft version** with Go live re-locked; change something executable, re-rehearse, and re-publish to the same register as v2. Confirm the previous version stayed authoritative until v2 published. (US6 / FR-029–031)

## Gate checks (the invariants)

- **UI lock**: Go live is disabled until a full rehearsal passes (FR-004).
- **Server soft gate**: call `POST /api/blueprints/{id}/publish` for an un-rehearsed version directly (bypassing the UI) → expect **409 `REHEARSAL_REQUIRED`** with the `execDefHash`; resend with `override.confirm=true` as an authorised user → publishes and writes a `PublishOverride` audit row (FR-032 / SC-002).
- **Governance hard gate**: publish as a user without register rights → **403**, no record written (FR-027).
- **Re-lock granularity**: after a pass, make a *presentational* edit (section/width) → Go live stays unlocked; make an *executable* edit (add a route/credential) → Go live re-locks (FR-023 / Q4).
- **Isolation**: confirm no rehearsal ever writes to the chosen live register (inspect the live register's transactions; SC-008).

## Automated tests (per `sorcha-ui` discipline)

- **Playwright (Docker)** `[Category("Designer")]` / `[Category("Lifecycle")]`: rail gating invariant, stage loads (console/network/CSS health), journey render + step detail + graph toggle, form-authoring (layout-less render + x-* apply), full-rehearsal walk unlocks Go live, Go-live picker + system-info card + no-rights block, amend → v2.
- **bUnit**: `LifecycleRail` gating states; `JourneyView` badge mapping; layout tools write correct `x-*`.
- **Unit/engine**: in-WASM dry-run routing/disclosure; **fidelity test** (dry-run vs full step sequence equivalence); exec-def hash excludes presentational keywords (D7); publish-gate logic (pass / blocked / override / no-rights).

## Backend smoke (curl via gateway)

```bash
# start full rehearsal
curl -s -X POST localhost/api/blueprints/$BP/rehearsals -H "Authorization: Bearer $TOK" \
  -H 'Content-Type: application/json' -d '{"mode":"full"}'
# publish without rehearsal → 409 REHEARSAL_REQUIRED
curl -s -X POST localhost/api/blueprints/$BP/publish -H "Authorization: Bearer $TOK" \
  -H 'Content-Type: application/json' -d '{"registerId":"'$REG'"}'
# override
curl -s -X POST localhost/api/blueprints/$BP/publish -H "Authorization: Bearer $TOK" \
  -H 'Content-Type: application/json' -d '{"registerId":"'$REG'","override":{"confirm":true,"reason":"hotfix"}}'
```

# Quickstart — AI Designer Unified Shell

**Branch**: `109-designer-shell-redesign`
**Date**: 2026-04-21

This quickstart gets a developer or reviewer running the feature end-to-end locally. All steps assume the repo is cloned and the prerequisites (from the root README) are installed (.NET 10 SDK, Docker Desktop, modern browser).

---

## 1. Set up

```bash
git checkout 109-designer-shell-redesign
dotnet restore
dotnet build Sorcha.sln -nologo
```

Expected: build succeeds with 0 errors and warning count comparable to master baseline (~30–80 warnings depending on sweep state).

---

## 2. Run the unit tests

```bash
# All new unit tests for this feature:
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj \
  --filter "FullyQualifiedName~Services.Designer" \
  --no-build -nologo
```

Expected: ~30 tests pass, 0 failed, 0 skipped. Covers `DesignerContextTests`, `PreviewPagerLogicTests`, `AutoScrollControllerTests`, `TabRouteParserTests`.

---

## 3. Start the platform

```bash
# Bring up Docker services (Postgres, Mongo, Redis, all Sorcha services)
docker-compose up -d

# Wait for health to go green (~30 seconds)
docker-compose ps
```

Platform access points (unchanged):

- Main UI: `http://localhost/app`
- API Gateway: `http://localhost:80`
- Aspire dashboard (if using Aspire instead): `http://localhost:18888`

---

## 4. Exercise the feature manually

### 4a. AI tab — fixed-layout chat (User Story 1)

1. Open `http://localhost/app` and sign in as a user with the `Designer` role.
2. Click **Designer** in the left nav. You land on `/designer/blueprint` with the AI tab active.
3. Observe: the chat fills the full content width. The input sits at the viewport bottom. There's no right-hand preview column.
4. Type: `Create a simple 2-step permit approval workflow with an Applicant and an Assessor.`
5. As the AI responds, keep sending messages until the conversation has ~20 messages. Scroll up through history. The input **stays visible at the bottom** throughout. Messages scroll independently.
6. Observe the shared toolbar: the blueprint title populates, the Save button lights up (dirty indicator).
7. Click **Save**. Confirmation toast appears; URL updates to `/designer/blueprint/{newId}?tab=ai`.

### 4b. Diagram tab — shared state (User Story 1)

1. Click the **Diagram** tab. The blueprint renders as a canvas of action nodes and routes.
2. Drag a node to a new position. Click any action's name and rename it to something memorable ("RENAMED_IN_DIAGRAM"). The Save button lights up.
3. Click the **AI** tab. The canvas's node positions survive the round-trip (verify by clicking Diagram again — your drag persists). Click Save.
4. From the AI tab, ask: `What are the current action names?` The AI's response includes `RENAMED_IN_DIAGRAM` — verifying that hand-edits flow into the context that the AI sees.

### 4c. Preview tab — auto-cursor (User Story 2)

1. Click **Preview**. One action's form renders, with the participant name in the sub-header and the submit button disabled with a "Preview — submission disabled" tooltip.
2. Click **Next** in the pager. The other action's form appears. The Next button disables because you're on the last one.
3. Click **◀ Previous** or press `[` to go back.
4. Click the jump dropdown; select the second action directly.
5. Switch to **AI** and ask: `Rename the first action to "Submit Application"`. Switch back to **Preview**. The cursor has NOT auto-followed because you engaged manual mode.
6. Click **Follow AI**. Preview cursor jumps to whichever action the AI most recently edited, with "Submit Application" as the new title.

### 4d. Legacy URL redirect (User Story 3)

1. In the browser address bar, navigate to `http://localhost/app/designer/chat`. You're redirected to `/designer/blueprint?tab=ai` without a page reload.
2. Navigate to `http://localhost/app/designer`. You're redirected to `/designer/blueprint?tab=diagram`.
3. If you saved a blueprint earlier with ID `X`, try `http://localhost/app/designer/chat/X`. You land on `/designer/blueprint/X?tab=ai` with that blueprint loaded.

---

## 5. Run the E2E suite

```bash
# Ensure Docker is running (needed by DockerTestBase)
docker ps

dotnet test tests/Sorcha.UI.E2E.Tests/Sorcha.UI.E2E.Tests.csproj \
  --filter "FullyQualifiedName~DesignerShell" \
  --no-build -nologo
```

Expected: 10 tests pass.

Noteworthy tests:
- `DesignerShell_InputPinnedAtBottom_AfterManyMessages` — the closure for GAP-011b. Injects 50 synthetic chat messages via `page.evaluate` and asserts the input's bounding box is at the viewport bottom.
- `DesignerShell_PreviewFollowAiToggle_AutoCursor` — the manual-override state-machine end-to-end.
- `DesignerShell_ConsoleNoErrors_DuringTabSwitches` — zero-error gate.

---

## 6. Sanity-check the surface

```bash
# Grep for any stale references to old designer routes
grep -rn "/designer/chat\|\"/designer\"" src/ --include="*.razor" --include="*.cs" \
  | grep -v "// legacy redirect shim"
```

Expected output: empty (all stale refs updated) OR only lines inside `BlueprintChat.razor` / `Designer.razor` whose comment says "legacy redirect shim".

```bash
# Confirm no two toolbars render on the new shell
grep -c "MudToolBar" src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/DesignerBlueprint.razor
```

Expected: 1.

---

## 7. Known limitations (from spec non-goals)

- No bidirectional diff-announce: if you hand-edit in Diagram, the AI's context only picks it up on its next turn (no system message saying "user just edited").
- No full-instance simulation: Preview renders forms, doesn't route-submit-route through them.
- No mobile optimisation: designer is admin-only, desktop-first.
- No undo/redo in any pane.

---

## Troubleshooting

**Tab switches cause a visible flash.** Check that `MudTabs` has `KeepPanelsAlive="true"`. Without it, panels dispose and reinit each switch.

**Input drifts when conversation grows.** Check the AI pane uses CSS Grid `grid-template-rows: 1fr auto`, not a flex column. (See research.md §R2.)

**E2E test for input pinning fails.** Verify Playwright's `boundingBox()` assertion tolerates 2px sub-pixel rounding; verify `page.evaluate` injection path uses the `[JSInvokable]` hook, not a real SignalR round-trip.

**Save button never enables.** Check that every mutation path on `DesignerContext` calls `MarkDirty()`. Easy to miss on Diagram hand-edits.

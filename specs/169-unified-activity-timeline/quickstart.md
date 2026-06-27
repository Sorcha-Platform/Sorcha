# Quickstart / Validation: Unified Activity Timeline Read-Path

Validates the three user stories and the success criteria end-to-end. Implementation details live in `tasks.md`; this is a run/verify guide.

## Prerequisites

- .NET 10 SDK, Docker Desktop.
- Solution builds clean: `dotnet restore && dotnet build` (no new warnings — Constitution V).
- Stack up: `docker-compose up -d` (or `dotnet run --project src/Apps/Sorcha.AppHost`).
- A signed-in test user with a mix of inbox entries spanning categories/severities (Action, Workflow, Security, System; Info → Critical).

## Build & test

```bash
dotnet build
# Targeted unit suites for this feature
dotnet test --filter "FullyQualifiedName~Inbox"
dotnet test --filter "FullyQualifiedName~Classification"
dotnet test --filter "FullyQualifiedName~PersonaInboxWriter"
dotnet test --filter "FullyQualifiedName~EncryptionInboxWriter"
# Shared component tests
dotnet test --filter "FullyQualifiedName~ActivityFeed"
# PWA bundle gate (no designer/admin deps leaked)
pwsh scripts/check-pwa-bundle.ps1
```

## Scenario 1 — Complete activity in one timeline (US1 / SC-001, SC-006, SC-008)

1. Sign in; open the Activity surface on web `/app` (`/activity`) and on the PWA (`/activity`).
2. **Expect**: one reverse-chronological list; every inbox-spine entry present (no category systematically missing); each entry shows title, summary, relative timestamp, category/severity indicator.
3. Narrow the viewport to mobile width → **expect** responsive layout, no horizontal scroll, no truncated essentials.
4. Click an entry with a `DetailHref` → **expect** navigation to its detail; an entry without one is non-navigable (no dead click).
5. Confirm both hosts render the **same component** (`ActivityFeed`) and the same entries for the identity.

## Scenario 2 — Bell shows only what needs me (US2 / SC-002)

1. Ensure the user has both Actionable (e.g. an `Action` entry, or an `ActionRequired`/`Critical`) and Informational (e.g. profile-saved, encryption-complete) entries.
2. Open the bell drawer → **expect** only Actionable entries listed; **0** Informational entries.
3. Check the unread badge → **expect** it counts only **unread Actionable** entries (verify against `GET /api/me/inbox/unread-count`).
4. Open the Activity surface → **expect** both Actionable and Informational entries (not filtered).
5. Act on / acknowledge an Actionable entry (read or dismiss) → **expect** badge drops; entry still visible in the Activity history.

## Scenario 3 — No activity lost when legacy producers move (US3 / SC-003, SC-004, SC-005)

1. **Profile save**: save your profile → **expect** an *Informational* "Profile saved" entry on the Activity timeline (not in the bell).
2. **Profile delete**: delete your profile → **expect** a "Profile deleted" entry (Warning severity → Informational).
3. **Encryption complete**: trigger a successful background encryption → **expect** an *Informational* completion entry for the initiating user (`workItem.UserId`).
4. **Encryption fail**: force a failed background encryption → **expect** a failure entry classified **Actionable** (appears in the bell so the user is alerted).
5. **Parity (SC-004)**: every occurrence that previously wrote a legacy `ActivityEvent` now also produces a spine entry (the legacy emit is retained; verify both exist).
6. **Fault injection (SC-005)**: make the inbox write throw → **expect** the underlying profile/encryption operation still succeeds, and a `LogWarning`/`LogError` is recorded (no rollback).
7. **Idempotency**: re-emit the same source event (retry) → **expect** no duplicate timeline entry (`(PlatformUserId, SourceEventId)` dedup).

## Scenario 4 — Scope guards (SC-007)

1. Confirm the legacy `ActivityEvent` table and its migrations are present and unaltered (no drop, no squash).
2. Open `/operations` (Encryption Operations) → **expect** behaviour identical to before this feature.

## Done when

- [ ] All four scenarios pass on both hosts and both viewport widths.
- [ ] Unit/component suites green; PWA bundle gate green; no new build warnings.
- [ ] Endpoint docs (`.WithSummary`/`.WithDescription`) and XML summaries updated for the changed read API.

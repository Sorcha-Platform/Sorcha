# Phase 1 Data Model: Fix Inbox/Bell Drawer Overflowing Phone Width

**Not applicable.** This feature is a presentation-layer (CSS) styling correction. It introduces no entities, no persisted state, no DTOs, and no schema changes.

The spec confirms this under *Key Entities*: "Not applicable — this is a presentation-layer styling fix with no data model impact."

The only state involved is the existing `InboxPanel.IsOpen` UI flag (already present, unchanged) and the inbox entries it renders (`InboxEntryDto`, owned by Feature 118 — out of scope for this change). No data-model artifact is generated.

If a future change to this feature does introduce data, replace this file with entity definitions (name, fields, relationships, validation rules, state transitions).

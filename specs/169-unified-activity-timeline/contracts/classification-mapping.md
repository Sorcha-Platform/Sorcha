# Contract: Actionable / Informational Classification

A **pure, total, two-valued** function over an inbox entry's `Category` and `Severity`. It is the single source of truth shared (by replication + a shared truth-table test) between the server filter and the client emphasis.

## Definition

```
Classify(category, severity):
    if category == Action:            return Actionable
    if severity >= ActionRequired:    return Actionable      # ActionRequired or Critical
    return Informational                                     # default fallback (FR-011)
```

## Truth table (authoritative — both implementations MUST match)

| Category \ Severity | Info | Warning | ActionRequired | Critical |
|---------------------|------|---------|----------------|----------|
| Action              | Actionable | Actionable | Actionable | Actionable |
| Credential          | Informational | Informational | Actionable | Actionable |
| Membership          | Informational | Informational | Actionable | Actionable |
| Security            | Informational | Informational | Actionable | Actionable |
| System              | Informational | Informational | Actionable | Actionable |
| Workflow            | Informational | Informational | **Actionable** | Actionable |
| Custom              | Informational | Informational | Actionable | Actionable |

(Bold = the encryption-fail cell, `Workflow` + `ActionRequired` → Actionable.)

## Implementations

| Side | Location | Form |
|------|----------|------|
| Server | `Sorcha.Tenant.Service.Services.InboxClassification` | `static bool IsActionable(InboxCategory, InboxSeverity)` + the equivalent EF predicate used by `EfCoreInboxStore`. |
| Client | `Sorcha.UI.Components.User.Services.Shared.ActivityClassification` | `static bool IsActionable(string category, string severity)` over DTO strings; unknown strings → Informational. |

## Guarantees

- **Total**: every `(category, severity)` pair (including unknown client strings) yields exactly one value.
- **Stable**: derivation only — no persistence, no migration (FR-012, FR-019).
- **Authoritative count**: the bell badge uses the *server* predicate (`/api/me/inbox/unread-count`), never the client mirror.
- **Test**: a shared fixture asserts the full truth table against both implementations so they cannot drift.

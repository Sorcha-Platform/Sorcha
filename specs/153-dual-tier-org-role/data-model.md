# Phase 1 Data Model: PWA Dual-Tier / Org-Role Work

**Feature**: 153-dual-tier-org-role | **Date**: 2026-06-14

No new server model; device-local additions only.

## Active capacity (existing, reused)

`IUserContext.ActiveContextOrgId : Guid?` — `null` ⇒ **Personal** (consumer); a value ⇒ acting as
that organisation (platform/org). Display label resolved from `IUserOrgMembershipsClient`
(`MainLayout.ActiveLabel`). `OnContextChanged` fires on every switch.

## Home (personal) token slot (new)

Extend `IAccessTokenStore` with a second persisted record (IndexedDB `device` store, key
`home-access-token`):

| Op | Behaviour |
|----|-----------|
| `SetHomeAsync(record)` | Persist the personal/consumer token snapshot |
| `GetHomeAsync()` | Return it, or null if absent/expired (purge on expiry, like the active token) |
| `ClearHomeAsync()` | Remove it (called on sign-out alongside `ClearAsync`) |

`AccessTokenRecord` shape is unchanged (reused for both active + home).

## Capacity transitions (client-owned, in ManagedUserContext)

```
Personal --(switch to Org X)--> snapshot active(consumer) → Home; switch-org re-mint → active = Org X token
Org X    --(switch to Org Y)--> switch-org re-mint → active = Org Y token (Home unchanged)
Org *    --(switch to Personal)--> active = Home token (restored); if Home missing/expired → signed-out/refresh
sign out --> ClearAsync + ClearHomeAsync
```

## Inbox capacity framing (Actions.razor)

- `actingAsOrgLabel : string?` — non-null ⇒ render the "acting as <Org>" banner; sourced from the
  active context label.
- The pending list + count come from the **active** token (unchanged) — in an org context they are
  the member's org-role actions.

## Invariants

- After return to Personal, the active token is a **consumer** token (or signed-out) — never a
  residual platform token (FR-004 / SC-002).
- The wallet never holds a capacity the server didn't grant (switch-org is the only elevation path;
  it enforces membership + tier) (FR-009 / SC-004).

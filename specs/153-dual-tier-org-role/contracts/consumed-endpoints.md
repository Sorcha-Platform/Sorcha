# Consumed Endpoints (read-only): PWA Dual-Tier / Org-Role Work

**Feature**: 153-dual-tier-org-role | **Date**: 2026-06-14

D adds **no endpoints** and changes none. It consumes existing ones unchanged.

| Use | Route | Notes |
|-----|-------|-------|
| Switch to an org capacity (re-mint platform/org token) | `POST /api/auth/switch-org` (Tenant `AuthEndpoints.cs:956`) | Verifies membership (403 if not a member); re-mints at the tier appropriate to the org (downgrade-not-refuse). **Cannot** mint a personal/consumer token (requires an org) — hence the home-token restore. |
| List the user's org memberships (switcher options) | `GET /api/auth/me/organizations` (`IUserOrgMembershipsClient`) | Drives entitlement-aware switcher. |
| Org-role pending actions + count (active token) | `GET /api/actions/pending`, `/api/actions/pending/count` | From A; capacity-agnostic — returns the member's org-role actions when the active token is the org token. |
| Execute an org-role action (active token) | `POST /api/instances/{id}/actions/{actionId}/execute` | From A; needs `org_id`+roles (present in the org token). **Live-validate** in an org context. |

**Boundary (do NOT weaken):** the only elevation path is `switch-org`, which enforces membership +
tier server-side. The PWA never mints/forges a platform capacity; a non-entitled switch returns 403
and the UI stays in the current capacity.

**Drift guard:** if `switch-org`'s response shape or tier behaviour changes, the context-switch +
home-restore tests should fail — re-align rather than work around.

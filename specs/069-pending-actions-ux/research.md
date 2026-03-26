# Research: 069-pending-actions-ux

## Decision 1: Where to Enrich Action Titles

**Decision**: Enrich at the `EfCoreInstanceStore.GetPendingActionsByWalletAsync()` level using `ActionResolverService` to look up blueprint action definitions.

**Rationale**: The `ActionResolverService` already caches blueprints (10-min distributed cache TTL) and builds an action index for O(1) lookup. The pending actions endpoint already runs in Blueprint Service which has access to this service. Adding a blueprint lookup per-instance (not per-action) is cheap given caching.

**Alternatives considered**:
- Client-side blueprint fetch per pending action — rejected: N+1 problem, slow UX
- Store action titles in instance metadata at creation — rejected: action titles are static blueprint data, duplicating them violates DRY
- Denormalize into a separate "enriched pending actions" table — rejected: over-engineering for MVP

## Decision 2: Instance Reference Generation Trigger

**Decision**: Generate the instance reference in the Blueprint Engine's action execution path, after the first action completes and `AccumulatedData` is populated.

**Rationale**: `Instance.AccumulatedData` is populated after each action with the payload field values. After Action 1, it contains all the fields the reference template needs (e.g., projectName, siteAddress). The engine already updates instance metadata at this point. The reference is written to `Instance.Metadata["instanceReference"]`.

**Alternatives considered**:
- Generate at instance creation time — rejected: no payload data available yet
- Generate client-side — rejected: reference must be consistent across all participants
- Generate in a background job — rejected: unnecessary complexity, synchronous generation in the action execution path is fast enough

## Decision 3: Instance Reference Template Location

**Decision**: Add an `InstanceReference` property to the `Blueprint` model class, alongside existing `Metadata` dictionary.

**Rationale**: The reference template is a first-class blueprint feature, not arbitrary metadata. It has a defined schema (prefix + components with transforms). Making it a typed property enables validation during blueprint publishing and clear documentation. The existing `NotificationConfig.SummaryTemplate` with `{{payload.field}}` pattern is a precedent for template references in the model.

**Alternatives considered**:
- Store in Blueprint.Metadata as JSON string — rejected: no type safety, no validation
- Separate configuration file — rejected: must travel with the blueprint definition
- Use NotificationConfig.SummaryTemplate for dual purpose — rejected: different output format (reference vs. human sentence)

## Decision 4: Execute Action Schema Fetch

**Decision**: Fetch the full blueprint via `GET /api/blueprints/{blueprintId}` when the user clicks TAKE ACTION, extract the action's `DataSchemas`, and pass to the `ActionForm` dialog.

**Rationale**: The endpoint already exists, blueprints are cached (5-min server cache), and the full blueprint is needed anyway for form rendering (schema, disclosures, form control definitions). The `ActionResolverService.GetActionDefinition()` method provides O(1) action lookup from a fetched blueprint.

**Alternatives considered**:
- New endpoint `GET /api/blueprints/{id}/actions/{actionId}/schema` — rejected: premature optimization, extra endpoint to maintain
- Include schemas in pending actions response — rejected: schemas are large, most won't be opened
- Cache schemas client-side in localStorage — rejected: blueprints can be versioned, stale cache risk

## Decision 5: View Preference Storage

**Decision**: Use browser localStorage with a key like `sorcha:pendingActions:viewMode` storing "cards" or "table".

**Rationale**: Simple, immediate, no server round-trip. Survives logout/login within the same browser. Consistent with how other Blazor WASM apps handle UI preferences. No server-side user preferences infrastructure exists in Sorcha currently.

**Alternatives considered**:
- Server-side user preferences API — rejected: no existing infrastructure, over-engineering for a view toggle
- Cookie-based — rejected: localStorage is simpler for SPA state
- IndexedDB — rejected: overkill for a single string preference

## Decision 6: Reference Uniqueness Strategy

**Decision**: Append a 4-character base36 hash derived from the instance ID (UUID) to the reference. Format: `{PREFIX}-{COMP1}-{COMP2}-{hash}`.

**Rationale**: Instance IDs are UUIDs, guaranteed unique. Taking the first 4 characters of a base36 encoding of the UUID's first 8 bytes gives ~1.6M combinations — sufficient for uniqueness within a register. The hash is deterministic (same instance always produces the same reference).

**Alternatives considered**:
- Sequential counter per register — rejected: requires atomic counter, race conditions
- Full UUID suffix — rejected: defeats the purpose of human-readable references
- No uniqueness suffix — rejected: "CP-RIV-14W" would collide for same-address applications

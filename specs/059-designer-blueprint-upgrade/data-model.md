# Data Model: Designer & Blueprint Instructions Upgrade

**Feature**: 059-designer-blueprint-upgrade
**Date**: 2026-03-16

## New Entities

### BlueprintInstructions

Top-level instructions container added as a property on `Blueprint`.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Overview | string? | max 5000 | Markdown overview of the workflow |
| Locale | string? | BCP 47, max 10 | Primary language code (e.g., "en-GB") |
| ActionInstructions | Dictionary<int, string>? | key = action ID, value max 5000 | Per-action guidance keyed by action ID |
| ParticipantInstructions | Dictionary<string, string>? | key = participant name, value max 2000 | Per-participant role guidance |
| InstructionSets | List\<InstructionSet\>? | | Linked translations |
| GovernanceRoles | Dictionary<string, string>? | key = role name, value = DID URI or participant ID | Blueprint-defined governance participants |

### InstructionSet

Linked external instruction document for translations.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Locale | string | required, BCP 47, max 10 | Language code (e.g., "fr-FR") |
| Source | string | required, max 500 | DID URI or URL to instruction document |
| Version | string? | max 20 | Version of the translation document |

### BlueprintVersion

Semantic version attached to published blueprints in the register.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Major | int | >= 0 | Structural version number |
| Minor | int | >= 0 | Documentation version number |
| ChangeType | string | "structural" or "documentation" | Type of change from previous version |
| StructuralHash | string | SHA-256 hex, 64 chars | Hash of blueprint excluding instructions |
| PublishedAt | DateTimeOffset | | Publish timestamp |
| PublishedBy | string | | Signer wallet address |
| TransactionId | string | | Register transaction ID |

### SchemaField

Extracted from JSON Schema property definitions for editor field resolution.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Name | string | required | Property name from schema |
| Type | string | | JSON Schema type (string, number, etc.) |
| Description | string? | | Schema property description (help text fallback) |
| IsRequired | bool | | Whether field is in schema's `required` array |
| Constraints | Dictionary<string, string>? | | Extracted constraints (minLength, maxLength, pattern, enum) |

## Modified Entities

### Blueprint (existing)

| Field | Change | Type | Constraints |
|-------|--------|------|-------------|
| Instructions | ADD | BlueprintInstructions? | nullable, JSON serialized |
| VersionMajor | ADD | int | defaults to 1 |
| VersionMinor | ADD | int | defaults to 0 |

- `Instructions` is `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` for backwards compatibility
- Existing `Version` (int) property is kept for backwards compatibility but deprecated in favour of `VersionMajor.VersionMinor`

### Action (existing)

| Field | Change | Type | Constraints |
|-------|--------|------|-------------|
| Instructions | ADD | string? | max 5000, Markdown |

### Control (existing)

| Field | Change | Type | Constraints |
|-------|--------|------|-------------|
| Instructions | ADD | string? | max 500, Markdown |

### Participant (existing)

| Field | Change | Type | Constraints |
|-------|--------|------|-------------|
| Instructions | ADD | string? | max 2000, Markdown |

## State Transitions

### Publishing Workflow States

```
Draft → Submitted → Under Review → Approved → Published
                  ↘ Rejected → Draft (cycle)
```

- **Draft**: Blueprint being authored/edited. Not yet submitted.
- **Submitted**: Blueprint submitted for review via publishing workflow. Immutable snapshot.
- **Under Review**: Reviewer examining the blueprint (full review for structural, documentation review for instructions-only).
- **Approved**: Reviewer approved. Awaiting publisher signature.
- **Rejected**: Reviewer rejected with comments. Author can revise and resubmit.
- **Published**: Publisher signed and committed to system register. Immutable.

### Version Lifecycle

```
New blueprint → v1.0 (first publish, structural)
v1.0 → v1.1 (instructions-only update)
v1.1 → v1.2 (more instruction updates)
v1.2 → v2.0 (structural change, minor resets)
```

## Relationships

```
Blueprint 1──1 BlueprintInstructions (optional, embedded)
BlueprintInstructions 1──* InstructionSet (linked translations)
BlueprintInstructions 1──* GovernanceRoles (per-blueprint governance)
Blueprint 1──* Action (min 1)
Blueprint 1──* Participant (min 2)
Action 1──1 Control (form definition)
Action *──* Route (routing rules)
Control 1──* Control (nested elements, recursive)

PublishedBlueprint 1──* BlueprintVersion (version history in register)
BlueprintVersion ──1 Transaction (signed register transaction)
```

## Validation Rules

- `BlueprintInstructions.Locale` must be valid BCP 47 if provided
- `InstructionSet.Locale` must be unique within a blueprint's instruction sets (no duplicate locales)
- `InstructionSet.Source` must be a valid URI (DID or URL format)
- `Action.Instructions` and `Control.Instructions` must be valid Markdown (no raw HTML script tags)
- `GovernanceRoles` values must be valid DID URIs or participant IDs
- Structural hash comparison: strip `Instructions` at Blueprint, Action, Control, and Participant levels, serialize to canonical JSON, SHA-256
- Version major must increment by exactly 1 on structural change
- Version minor must increment by exactly 1 on documentation change
- Version minor must reset to 0 on major increment

# Data Model: 069-pending-actions-ux

## New Entity: InstanceReferenceTemplate

Added as a property on the `Blueprint` model.

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Prefix | string | 1-5 uppercase alpha chars (e.g., "CP") | Required, ^[A-Z]{1,5}$ |
| Components | List\<ReferenceComponent\> | Ordered field extractions | Required, 1-5 items |

### ReferenceComponent

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Field | string | JSON Pointer to payload field (e.g., "/projectName") | Required, valid JSON Pointer |
| Transform | string | How to extract display text | Required, enum: "first-word", "truncate", "uppercase" |
| Chars | int | Max characters for this component | Required, 2-10, default 3 |

## Modified Entity: Blueprint

| Field | Change | Description |
|-------|--------|-------------|
| InstanceReference | **Added** | InstanceReferenceTemplate (nullable). Defines how to generate human-readable instance references. |

## Modified Entity: Instance

| Field | Change | Description |
|-------|--------|-------------|
| Metadata["instanceReference"] | **Convention** | Stores the generated reference string (e.g., "CP-RIV-14W-a7k3"). Set after first action completes. |

No schema migration needed — `Metadata` is already a JSON dictionary column.

## Modified Entity: PendingActionSummary (DTO)

| Field | Change | Description |
|-------|--------|-------------|
| ActionTitle | **Enriched** | Now populated from blueprint action definition (`Action.Title`) instead of placeholder "Action {id}" |
| BlueprintTitle | **Existing** | Already populated from instance metadata |
| InstanceReference | **Added** | The human-readable reference from instance metadata |

## Entity Relationships

```
Blueprint (1) ──── InstanceReferenceTemplate (0..1)
    │                     │
    │                     └── ReferenceComponent (1..5)
    │
    └── Action (1..*) ──── Title (used to enrich PendingActionSummary)
           │
Instance (1) ──── Metadata["instanceReference"] (0..1)
    │
    └── PendingActionSummary (0..*) ──── enriched with ActionTitle + InstanceReference
```

## State Transitions

### Instance Reference Lifecycle

```
[No Reference] ──(Action 1 completes)──> [Reference Generated]
                                              │
                                              └── Stored in Instance.Metadata["instanceReference"]
                                              └── Immutable after generation
```

### Reference Generation Flow

1. Action 1 payload submitted
2. Engine updates `Instance.AccumulatedData` with payload fields
3. Engine checks `Blueprint.InstanceReference` template
4. If template exists: evaluate components against AccumulatedData, append hash
5. If template missing: generate fallback (`{first 2 chars of title}-{hash}`)
6. Write to `Instance.Metadata["instanceReference"]`

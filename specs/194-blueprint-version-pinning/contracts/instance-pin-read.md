# Contract: reading an instance's pinned definition

**Feature**: 194 | **Status**: proposed | **Satisfies**: FR-018, FR-019, SC-010

The defect this feature fixes was invisible. A pin that is correct but unreportable leaves the next
investigation as blind as this one was, so the read surface is part of the feature rather than a
follow-up.

---

## Additive fields — no new endpoints

Three existing responses gain fields. No route, policy or existing field changes.

### `GET /api/instances/{instanceId}`

```jsonc
{
  "id": "…",
  "blueprintId": "contractor-cert-…",
  "blueprintVersion": 1,                 // display label — now DERIVED from the pin
  "blueprintExecDefHash": "9f2c…",       // NEW — the pin
  "isPinnedToLatest": false,             // NEW — pin == blueprint's current latest on this register
  "currentActionIds": [ 2 ],
  "state": "Active"
}
```

- `blueprintVersion` is resolved by looking the pin up in the published store and reading that
  entry's ordinal. It is **not** read from a stored column any more. This is what makes FR-019's
  "cannot disagree" true rather than merely intended.
- `isPinnedToLatest` is computed at read time, never stored. It is what turns "this instance is on
  an older definition" from something an operator must infer by comparing two hex strings into
  something the response states.

### `GET /api/blueprints/{id}/versions`

Each entry gains `execDefHash`, so an operator can match an instance's pin to a named version
without a database query.

### `GET /api/me/applications/{instanceId}` (F186 citizen surface)

**No new field.** A citizen has no use for a hash and should not be shown one. The change here is
behavioural and invisible: the decision-notice message is resolved from the **pinned** definition's
`reasons` catalogue rather than the latest one, so a refused applicant reads the wording that was in
force when they applied.

---

## Failure and degraded states

| Situation | Response |
|---|---|
| Pin set and resolvable | Both fields populated; `blueprintVersion` from the resolved entry. |
| Pin set, definition not resolvable on this node | `blueprintExecDefHash` populated, `blueprintVersion` **null**, `isPinnedToLatest` **null**. Do not substitute a guess — a wrong version label here is worse than an absent one, and this is exactly the stuck-instance state the operator needs to see. |
| Pre-feature instance, no pin | `blueprintExecDefHash` null, `isPinnedToLatest` null. Distinguishable from "resolvable but stale". |

Three states, three distinguishable renderings. Collapsing "unresolvable" into "unpinned" would hide
the one failure mode Story 3 exists to surface.

---

## Observability

On the existing `Sorcha.Blueprint.Service` meter:

| Instrument | Tags | Purpose |
|---|---|---|
| `sorcha_instance_pin_fallback_total` | `path` ∈ `projector` \| `rebuild` | Every use of the pre-feature fallback (FR-017, SC-009). Must read zero on a freshly created register. |
| `sorcha_instance_pin_mismatch_total` | `reason` ∈ `unresolvable` \| `foreign` | Refusals under FR-007 / FR-008. |

Both carry opaque identifiers only — no participant data, no payload, consistent with the platform's
thin-signal convention.

`sorcha_instance_pin_fallback_total` is the instrument that makes R-009's removal trigger checkable:
the fallback can be deleted when this has been flat at zero across a full re-genesis.

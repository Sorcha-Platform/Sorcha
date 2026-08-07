# Feature 190 — Reference-data registers

**Branch:** `190-reference-data-registers`
**Status:** Specified, not implemented
**Depends on:** Feature 103 (`$ref` core primitives, `x-address-lookup`), UT-017 (selection label/value split)

---

## Why

A form field like `country` needs two different strings: a **stable code** that gets signed into the
payload and matched by downstream logic (`GB`), and a **friendly name** the citizen reads
("United Kingdom"). UT-017 gave selections that split. This feature gives them somewhere to come from.

Today `PostalAddress.v1.json` types `country` as a free-text string of 2–100 characters. A citizen can
type "UK", "United Kingdom", "Great Britain", "england", or a typo, and all of them are signed and
stored as equally valid. Nothing downstream can group, compare, or check them — and it is the signed
value, so it cannot be cleaned up afterwards without breaking the signature.

### Why not just put the list in the schema

The obvious fix — an `enum` of 249 ISO codes plus `x-enumNames` in the core `PostalAddress` schema —
is worse than it looks:

1. **It is copied into every blueprint.** `SchemaRefResolver.Flatten` inlines core `$ref`s at publish
   time; the published blueprint carries the resolved schema, not the reference. Every blueprint that
   touches an address would embed the full country list, twice (values + labels), in its canonical
   content — and that content is what gets hashed and sealed.
2. **Updating the list means republishing every blueprint that embeds it.** A country list is not
   static: codes are added (`SS` in 2011), withdrawn (`CS`, `AN`), and renamed. A blueprint published
   last year would keep offering the old list forever, silently.
3. **It answers the wrong question at verification time.** A payload signed in 2026 containing `CS`
   must still be resolvable in 2031, after the code has been withdrawn. An inline enum records what
   was offered, but nothing records *which version of the list* was in force, so a verifier has no way
   to reconstruct what the citizen was actually shown.

### Why a register is the right home

Reference data is exactly what this platform's core primitive is for: a shared, replicated,
append-only, provenance-carrying list that several organisations must agree on and that must remain
interpretable long after it was written. Putting a code list on a register gets replication, sealing,
and history for free, and makes "what did `CS` mean when this was signed" a ledger query rather than
an archaeology exercise.

`SchemaRefResolver` already reserved the seam. `DidSorchaPrefix` (`did:sorcha:`) is documented in
source as *"Reserved future URI scheme for primitives published to a Sorcha register … out of scope
for Feature 103 and reserved for a follow-up"*, and the resolver deliberately **throws** on it rather
than failing silently, so the gap stays loud. This feature is that follow-up.

---

## Scope

### In

- A **reference-data register** concept: a register whose transactions publish versioned code lists.
- A seeded `iso-3166-1` code list (alpha-2 code + English short name) as the first citizen.
- An `x-reference` schema extension naming a code list, resolved at **render** time, not publish time.
- A read-side projection + cache so a form can populate a dropdown without a ledger read per keystroke.
- A `ReferenceLookupRenderer` control, dispatched the way `x-address-lookup` dispatches
  `PostcodeLookupRenderer`.
- Version pinning: a signed payload records which list version was in force.
- `country` in `PostalAddress.v1.json` switched from free text to a reference lookup.

### Out

- Authoring UI for code lists (they are seeded and updated by operators for now).
- Localised labels. The first list is English-only; the model must not *preclude* locales, but
  delivering them is a separate feature.
- Hierarchical or dependent lists (country → region). The model should not preclude it.
- Migrating existing free-text country values in already-signed payloads. They are signed; they stay.

---

## User stories

### US1 — A citizen picks a country from a list (P1)

A citizen filling an address form sees a searchable dropdown of country names, picks "United
Kingdom", and the payload stores `GB`.

**Independently testable:** submit the AIAS identity application on n1, confirm the rendered form
offers named countries and the sealed transaction payload contains the ISO code.

### US2 — The list is a register, not a hardcoded table (P1)

The code list is published to a register as a transaction, replicates to peer nodes, and the
resolved list on a replica is byte-identical to the origin.

**Independently testable:** publish on n1, read the projection on tiny, compare.

### US3 — A signed payload stays interpretable after the list changes (P2)

A payload signed against list version 1 still resolves to the label the citizen saw, after version 2
withdraws that code.

**Independently testable:** publish v2 withdrawing a code, re-read a v1-signed payload, confirm the
historical label resolves and is marked withdrawn rather than unknown.

### US4 — A form degrades honestly when the list is unreachable (P2)

If the projection cannot be reached, the field renders as a plain text input with a visible note,
rather than an empty dropdown that silently blocks submission.

This mirrors `PostcodeLookupRenderer`'s existing `NoProvider` fallback. An empty dropdown is the
failure mode to design against: it looks like "no countries exist" and gives the citizen nothing to do.

**Independently testable:** point the client at a dead endpoint; confirm the field is still usable.

---

## Requirements

| ID | Requirement |
|----|-------------|
| FR-01 | A code list has an id, a version, and an ordered set of entries `{code, label, status}` where status ∈ `active`/`withdrawn`. |
| FR-02 | Codes are immutable within a list id: a code's meaning never changes, it is only withdrawn. Reusing a code for a different meaning must be rejected at publish. |
| FR-03 | A list version is published as a register transaction and is sealed before it is servable. |
| FR-04 | `x-reference: "<list-id>"` on a string field marks it as reference-backed. |
| FR-05 | `x-reference` is resolved at **render** time. It must NOT be inlined by `SchemaRefResolver.Flatten` — that is the whole point. |
| FR-06 | The signed payload stores only the code. The label is presentation. |
| FR-07 | The instance records the list version in force at submission, so the label is reproducible. |
| FR-08 | Validation accepts only codes present in the pinned version, active or withdrawn. |
| FR-09 | The read path is cached; a form render must not require a ledger read. |
| FR-10 | An unreachable list degrades to free text with a visible note (US4), never to an empty dropdown. |

---

## Open questions for planning

1. **Which register?** The system register is already replicated everywhere and already carries system
   blueprints and core schemas — but it is also the most sensitive thing on the network, and
   Feature 189 has only just made it governable. A dedicated per-installation reference register is
   the alternative. This is the first thing to settle.
2. **Where does version pinning live?** On the instance, on the transaction metadata, or in the
   payload beside the code. Payload placement changes the signed shape and needs a schema decision.
3. **Does `x-reference` compose with `enum`?** A schema could carry both (enum as a subset filter of
   a larger list). Probably yes, but the interaction with FR-08 needs stating.
4. **Interaction with the Validator.** FR-08 implies the Validator can resolve a list version. That
   makes the reference register a validation dependency — acceptable for a replicated system list,
   less so for a tenant-authored one.
5. **Does this subsume `x-address-lookup`?** Both are "field whose values come from somewhere else".
   They are probably siblings rather than one replacing the other, since a gazetteer is a query and a
   code list is an enumeration, but the renderers should share a fallback story.

---

## Evidence standard

Consistent with Features 188 and 189: a green suite does not settle this. Acceptance requires the list
published and sealed on n1, resolved in a rendered form, a submitted payload containing the code, and
the same list resolving identically on the tiny replica.

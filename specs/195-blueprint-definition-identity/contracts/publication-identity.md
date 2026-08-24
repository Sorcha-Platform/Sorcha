# Contract — Publication Identity

**Feature 195.** The normative definition of how a blueprint definition's identity is derived.
This is the contract every other part of the feature is defined in terms of, and the one thing that
must be settled before any other task starts.

---

## 1. The construction

```
publicationTxId = lowercase_hex( SHA-256( preimage ) )

preimage = utf8("sorcha:blueprint-publication:v1")
         ‖ 0x1F ‖ utf8(registerId)
         ‖ 0x1F ‖ utf8(blueprintId)
         ‖ 0x1F ‖ utf8(canonicalDefinitionJson)
```

`0x1F` is the ASCII unit separator, matching `InstanceIdentity.Derive`
(`Blueprint/Services/Implementation/InstanceIdentity.cs:35-53`). Output is lowercase hex — a
transaction id, safe in URLs and store keys.

### Why each field is present

| Field | Reason it is REQUIRED |
|---|---|
| domain tag | `InstanceIdentity` is already `SHA-256(registerId ␟ blueprintId ␟ …)`. Without a tag these are the **same preimage construction sharing their first two fields** — two kinds of identity indistinguishable by shape. |
| `registerId` | A definition published to two registers is byte-identical **by construction** (same template, same model, same serializer). Without it one id names two ledger facts and every `(registerId, txId)` lookup, receipt and inclusion proof is ambiguous. |
| `blueprintId` | Binds the identity to the blueprint even if a future canonical form were to normalise or omit the `id` property. Cheap; makes the preimage self-describing. |
| `canonicalDefinitionJson` | The content. Makes the id a content address, so verification is self-anchoring. |

### Version tag

The literal `v1` is part of the tag. Any future change to this construction **or to the canonical
form** takes `v2` and is a deliberate re-identification of every definition, never a silent one.

---

## 2. Canonical form

`canonicalDefinitionJson` is produced by **parse → serialize with fixed rules**:

| Rule | Value |
|---|---|
| Object keys | **recursively sorted**, ordinal by UTF-16 code unit (RFC 8785 ordering) |
| Array order | **preserved** — arrays are ordered data, not sets |
| Whitespace | none |
| Duplicate object keys | **rejected** — throw, do not silently take the last |
| Property names | as they appear (already camelCase, from `[JsonPropertyName]` on the model) |
| Null properties | as they appear (already governed by `JsonIgnoreCondition` on the model) |
| Numbers | **preserved as written** unless the implementation task decides to normalise; either way pinned by golden vector |
| Encoding | UTF-8 |

### What does NOT need a rule, and why

Whitespace and string escaping **do not survive a parse** — `&` and `&` parse to the same
string. So the *producer's* encoder cannot affect the id, provided the hash is taken after this
canonicalisation. This is why the existing arrangement (Register canonicalises what it receives)
already works, and it independently supports §3.

⚠ The existing `RegisterSerializationOptions.Canonical`
(`Register.Models/RegisterSerializationOptions.cs:45-60`) is **not** this: it sets whitespace, naming
policy, null handling and encoder, but **does not sort keys**. Neither does `BlueprintContentHash`,
which re-serializes a parsed `JsonDocument` and thereby preserves input order. Both are
*serializer-output* addresses. Neither may be reused as the canonicaliser.

---

## 3. One producer

**Only the Register Service computes a publication id.**

| Component | May compute? | What it does instead |
|---|---|---|
| Register Service | **yes — the sole producer** | computes at publish; the value becomes the transaction id |
| Blueprint Service | no | records the id returned by the publish call on `PublishedBlueprint.PublicationTxId` |
| Blueprint Service (recovery) | **verification only** | recomputes from received bytes and compares to the transaction's own id; never mints an id |
| Validator Service | no | resolves by the pin carried on the transaction |
| Engine / execution path | no | reads `instance.BlueprintDefinitionTxId` |
| Tests | yes | the golden vector must reach it |

**Enforced by an architecture gate**, not by placement — the type lives in
`Sorcha.Blueprint.Models` so tests can reach it, and the gate restricts the *callers*.

Rationale: the four existing copies of the current derivation exist **only because
`PublishedBlueprint` never recorded the transaction id**. Recording it removes every reason to
recompute. This is stronger than CLAUDE.md §15/§16's "one shared leaf" — there is one producer, not
one shared formula.

---

## 4. Behaviour

| Situation | Required behaviour |
|---|---|
| Content identical to an existing publication on the same register | Same id ⇒ idempotent. **No second record. The request still succeeds.** |
| Content differing in any way | Different id ⇒ **new record**, alongside the existing one. Never a replacement. |
| Same content, different register | **Different id.** |
| Same content, different blueprintId | Different id. |
| Recorded definition altered in transit | Recomputed id ≠ transaction id ⇒ **rejected**. |
| Malformed JSON | Rejected before hashing, with a diagnosable reason. |
| Duplicate keys in the document | **Rejected.** |

---

## 5. What must be guarded, and by which test

Each mutation must fail **exactly** its named test (research R-015).

| Mutation | Killing test |
|---|---|
| Remove recursive key sorting | golden vector |
| Rename any `[JsonPropertyName]` on the blueprint graph | golden vector |
| Add/remove a `JsonIgnoreCondition` on the graph | golden vector |
| Change the number rule | golden vector |
| Drop the domain tag | preimage-separation test (asserts a publication id ≠ an `InstanceIdentity` over the same first two fields) |
| Drop `registerId` from the preimage | two-register distinctness test |
| Drop `blueprintId` from the preimage | two-blueprint distinctness test |
| Replace `0x1F` with a plain concatenation | boundary-ambiguity test (`("ab","c")` vs `("a","bc")`) |
| Accept duplicate keys instead of rejecting | duplicate-key test |
| Compute the id anywhere but the Register Service call path | architecture gate |

**The golden vector is a fixed blueprint fixture and a known id, committed.** It is the only guard
that catches all six degrees of freedom at once, and the only one that catches a property rename —
which is otherwise a refactor with no compile-time consequence that silently re-identifies every
definition on every register.

⚠ The golden vector must be written **before** the canonicaliser and must be seen to fail. A guard
written after the code is green never ran red naturally and proves nothing until a discriminating
mutation kills it.

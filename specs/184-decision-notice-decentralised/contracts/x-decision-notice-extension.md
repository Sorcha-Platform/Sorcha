# Contract: `x-decision-notice` route extension (v2 — codified)

**Supersedes** the F183 v1 shape (`reasonField`). Pre-release clean break — v1 blueprints must be
re-authored; there is no fallback path.

## Where it goes

On a **route**, in any blueprint. When that route is the one taken, the recipient participant gets a
durable notification carrying a reason resolved from this annotation.

```jsonc
{
  "id": "rejected-terminal",
  "nextActionIds": [],
  "isDefault": true,
  "condition": { "==": [ { "var": "decision" }, "rejected" ] },
  "x-decision-notice": {
    "recipientParticipantId": "citizen",
    "reasonCodeField": "/reasonCode",
    "title": "AIAS could not assure your identity",
    "severity": "Warning",
    "reasons": {
      "postcode-not-found": "AIAS could not locate that address on any map. We assure real people at real places — try a postcode that exists.",
      "profanity":          "AIAS does not assure identities described in such… colourful terms. Please reapply with your Sunday-best vocabulary.",
      "email-unverified":   "AIAS needs a verified email before it can assure you. Confirm your email and reapply."
    },
    "fallbackMessage": "Your application was not approved."
  }
}
```

## Fields

| Field | Required | Meaning |
|---|---|---|
| `recipientParticipantId` | yes | The blueprint participant to notify. Resolved to a wallet through the instance's participant bindings. For a citizen-facing flow this is the starting (open, late-bound) participant. |
| `title` | yes | The notification title. |
| `reasonCodeField` | no | JSON Pointer into the **submitted action payload** for the reason code. Omit for a route whose notice needs no per-reason variation (then `fallbackMessage` is always used). |
| `reasons` | no | Code → citizen-facing message. This is where the citizen-facing copy lives. |
| `fallbackMessage` | no | Used when no code is carried or the code is not in `reasons`. Omitting it means an unknown code yields an empty summary — declare it. |
| `severity` | no | Inbox severity. Defaults to `Warning`. |

## The rules a blueprint author must honour

1. **The action's schema must declare the code field.** Add the property named by `reasonCodeField` to the
   deciding action's `dataSchema`; an `enum` of the valid codes is strongly recommended so a typo in an
   agent rules file fails validation rather than silently degrading to the fallback message.

   ```jsonc
   "reasonCode": {
     "type": "string",
     "title": "Reason code",
     "enum": ["postcode-not-found", "profanity", "email-unverified"]
   }
   ```

2. **Codes are non-sensitive and public.** The code is written to the transaction's **clear** metadata and
   is readable by every node holding the register. Use a stable, opaque-ish slug describing the *class* of
   reason — never a name, an address, a value from the application, or free prose.

3. **The citizen-facing copy is blueprint copy.** It ships with the published blueprint, is replicated to
   every node, and is what the applicant reads. An autonomous agent's own prose (e.g. `verificationNotes`)
   stays on the ledger as the audit record; it is not the delivery mechanism.

4. **Notices are for decisions the recipient would not otherwise learn of.** Approval is already surfaced
   (the credential arrives), so annotate the reject/return routes, not the approve route.

5. **Terminal and non-terminal routes both work.** A "returned for more information" route can carry a
   notice.

## What the runtime does with it

At submit, on the deciding participant's node: the taken route's id and the resolved code are stamped onto
the sender-signed `RoutingDecision`, which rides the transaction's clear metadata and is verified at seal.

At fold, on **every** node holding the register: the node reads the route id from the sealed decision,
looks the route up in the blueprint it already holds, resolves `reasons[reasonCode] ?? fallbackMessage`,
and writes the durable inbox entry **only if it hosts the recipient's wallet**. No payload is decrypted;
no delegated authority is needed. The recipient's own node delivers; every other node skips.

## Agent-side (rules files)

The reason code is emitted as an ordinary payload field — no agent code change:

```jsonc
{
  "actionName": "Verify Assured Identity Application",
  "condition": { "==": [ { "var": "checks.postcodeExists" }, false ] },
  "decision": "submit",
  "payload": {
    "decision": "rejected",
    "reasonCode": "postcode-not-found",
    "verificationNotes": "AIAS could not locate that address on any map. …"
  }
}
```

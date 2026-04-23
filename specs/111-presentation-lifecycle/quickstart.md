# Quickstart: Timebound Presentation Lifecycle

**Feature**: 111-presentation-lifecycle
**Audience**: Developers implementing or consuming the lifecycle, plus auditors reading lifecycle transactions.

---

## TL;DR

When a blueprint action requires a credential presentation from an external wallet (today: HAIP), the register now records **three** events instead of one:

1. `presentation-initiated` — citizen submitted, QR rendered, no credential presented yet
2. `presentation-outcome` — verifier callback resolved the attempt (success or decline)
3. `presentation-abandoned` — TTL expired without a callback (opt-in per blueprint)

The action is complete only when a `presentation-outcome` with `kind=success` lands. Retry is a first-class flow — a new `presentation-initiated` can follow a decline.

---

## Running the feature end-to-end locally

**Prerequisites**: Docker Desktop, PowerShell 7.5+, `.secrets/passwords.json` generated.

```powershell
docker compose up -d
pwsh walkthroughs/AssuredIdentity/setup.ps1 -SkipHealthCheck
pwsh walkthroughs/AssuredIdentity/run.ps1
```

The existing AssuredIdentity walkthrough exercises the HAIP presentation flow and will serve as the acceptance harness once this feature ships. Its Phase 2 action (`Issue Driving Licence`) requires presentation of an `AssuredIdentityCredential`.

**Post-feature**: check the register for the new transaction types:

```bash
docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password \
  --authenticationDatabase admin --quiet --eval "
  db = db.getSiblingDB('sorcha_register_<REG_ID>');
  db.transactions.find({'MetaData.TransactionType': {\$in:
    ['PresentationInitiated', 'PresentationOutcome', 'PresentationAbandoned']}})
    .project({'MetaData.TransactionType': 1, 'PayloadType': 1, TxId: 1})
    .toArray()
"
```

---

## Authoring a blueprint with lifecycle config

```jsonc
{
  "id": "driving-licence-v1",
  "title": "Driving Licence Issuance",
  "presentationConfig": {
    "recordAbandonment": true,
    "outcomeDetailLevel": "minimal",
    "presentationValidityWindowSeconds": 600
  },
  "actions": [
    {
      "id": 1,
      "title": "Apply",
      "sender": "citizen",
      "credentialRequirements": [
        {
          "type": "AssuredIdentityCredential",
          "presentationSource": "HaipExternalWallet",
          "requiredClaims": [
            { "claimName": "holderName" },
            { "claimName": "holderDateOfBirth" }
          ]
        }
      ]
    }
  ]
}
```

All three `presentationConfig` fields are optional; each has a default (see `data-model.md` §3.3).

---

## Authoring a new consumer (non-HAIP)

Implement `IPresentationConsumer` from `Sorcha.PresentationLifecycle.Abstractions`, register it in your service's DI, and add your `ConsumerName` to the blueprint schema's `PresentationSource` enum.

```csharp
public sealed class FileUploadDeadlineConsumer : IPresentationConsumer
{
    public string ConsumerName => "file-upload-deadline";

    public async Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken ct)
    {
        var upload = (FileUploadPayload)verifierPayload;
        // ... your verification logic
        return new PresentationOutcome(
            Kind: PresentationOutcomeKind.Success,
            VerifiedClaims: new Dictionary<string, object> { ["fileHash"] = upload.Hash },
            Reason: null,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: upload.Hash);
    }
}
```

Your service POSTs to `POST /api/presentations/callbacks/file-upload-deadline` on Blueprint Service with the requestId and your consumer-specific payload shape. Blueprint Service relays to your consumer, writes the outcome transaction, and advances the workflow on success.

---

## Reading lifecycle transactions as an auditor

A complete citizen session for a single action can show any of these chains on the register:

**Happy path:**
```
presentation-initiated → presentation-outcome (success)
```

**Retry after decline:**
```
presentation-initiated#1 → presentation-outcome (decline: expired-credential)
presentation-initiated#2 → presentation-outcome (success)
```

**Abandoned (opted in):**
```
presentation-initiated → presentation-abandoned
```

**Abandoned then late success (edge):**
```
presentation-initiated → presentation-abandoned → presentation-outcome (success)
```

All transactions carry the `presentationRequestId`, so grouping is trivial. Each tx also carries the originating `instanceId` and `actionId` for cross-reference with the workflow instance.

---

## Polling lifecycle state from a client

```bash
curl -H "Authorization: Bearer $USER_TOKEN" \
  http://localhost/api/presentations/{presentationRequestId}/status
```

Returns one of:
- `initiated` — awaiting verifier callback
- `success` — outcome success written
- `declined` — outcome decline written
- `abandoned` — abandonment written
- `unknown` — no such request or already cleaned up (TTL + 1h has elapsed)

A UI polls this every few seconds while the citizen is scanning their QR.

---

## Local testing matrix

| Scenario | How to reproduce |
|---|---|
| Happy success | Run AssuredIdentity Phase 2, let the `sorcha-agent haip receive` finish |
| Decline (expired credential) | Manipulate holder wallet to present expired SD-JWT; verifier rejects |
| Decline (wrong issuer) | Point holder wallet at a second issuer; constraint mismatch |
| Abandon (opt-in blueprint) | Start Phase 2, kill agent before scan, wait 10 min, observe `PresentationAbandoned` tx |
| Abandon (opt-out blueprint) | Same but with `recordAbandonment: false` — only the initiated tx persists |
| Retry after decline | Reproduce decline then re-submit action; expect fresh requestId |
| Rate-limit exceeded | Submit same action 11 times quickly from same wallet; last gets HTTP 429 |
| Duplicate callback | Replay verifier callback twice; second is no-op |
| Late callback after abandonment | Script abandonment-first race; both txs on register |

All 9 scenarios are covered by `PresentationLifecycleIntegrationTests` and the AssuredIdentity walkthrough variants (to be added in Phase 2).

---

## Operational notes

- **Redis is a hard dependency** — the lifecycle service refuses to start if Redis is unreachable. Existing HAIP service already has this requirement.
- **Sweeper leader election** — in HA deployments, only one Blueprint Service replica runs the abandonment sweep loop at a time, selected via Redis SET NX on `sorcha:presentation:sweeper-lock`.
- **Metrics to watch** — `sorcha_presentation_abandoned_total` rising without corresponding `outcome_total` suggests verifier callbacks failing; `sorcha_presentation_ratelimit_rejected_total` trending up suggests either adversarial behaviour or UX problem causing legitimate users to retry excessively.
- **Retention** — lifecycle transactions are permanent like all register transactions; they participate in the sliding-window storage tiering from research 111's (future) retention design.

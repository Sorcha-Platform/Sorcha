# Quickstart: author and verify a decision notice

## 1. Annotate the reject route

In the deciding action's route (the one taken on rejection), add the `x-decision-notice` extension — see
[contracts/x-decision-notice-extension.md](./contracts/x-decision-notice-extension.md) for the full shape.
Add the code field to that action's `dataSchema` with an `enum` of the valid codes.

## 2. Emit the code from the decider

For an autonomous agent, add `reasonCode` to each reject rule's `payload` in its rules file. For a human
reviewer, render the code field as a select bound to the same enum.

## 3. Verify locally

```bash
dotnet build
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
```

The load-bearing unit assertions: the reason code is inside `ComputeSignableBytes()`; the dispatcher fires
only for a wallet hosted on the folding node; it is idempotent on replay.

## 4. Verify on n1 (live acceptance)

```powershell
# Republish the blueprint (existing register + existing agent — no restart, no org churn)
Publish-AiasBlueprint -Force

# Drive a reject through the API
./demos/AIAS/rehearse.ps1     # unverified-email / bad-postcode / profanity cases
```

Then, as the applicant, in the browser: open the bell drawer. You should see the decision entry with the
**blueprint's** wording for the code the agent emitted. Reload the page and sign out / back in — it is a
durable inbox row, so it survives both.

Confirm the wire shape too: the sealed transaction's clear metadata carries
`routingDecision.routeId` + `routingDecision.reasonCode`, and **no** free-text reason.

## 5. What "working" looks like on a federated split

The deciding agent's node folds the same sealed transaction and writes **nothing** — its wallet probe for
the citizen's wallet returns null (the wallet is not hosted there). The citizen's node folds it and writes
the entry. On a single-node deployment (n1) these are the same node, so one entry is written; that is the
degenerate case of the same code path, not a special case.

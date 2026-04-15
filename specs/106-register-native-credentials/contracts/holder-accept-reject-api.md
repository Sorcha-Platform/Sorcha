# Contract: Holder accept / reject API surface

**Feature**: 106-register-native-credentials
**Surface**: Client-facing HTTP API for holder acceptance or rejection of a pending credential
**Layer**: Wallet Service (local status update) + Blueprint Service (register transaction submission)
**Binds**: FR-013, FR-014, FR-015, FR-016, FR-017, FR-023

## Shape

A holder's accept or decline is a **two-call operation**: one local Wallet Service PATCH to update the credential's in-store status, and one Blueprint Service POST to seal the acceptance/rejection transaction to the register. The client runs both in parallel.

Neither call depends on the other's success — they operate on different data stores (Wallet store vs. Register) — but both SHOULD complete for a clean user experience. The client surfaces the overall outcome in a "partial success" state if one fails.

## Accept flow

### 1. Local status update (Wallet Service)

**Endpoint**: `PATCH /api/v1/wallets/{walletAddress}/credentials/{credentialId}`

**Auth**: Standard user JWT (the authenticated user must own the wallet).

**Request body**:
```json
{
  "status": "Active"
}
```

**Response** (200):
```json
{
  "credentialId": "urn:uuid:...",
  "type": "VerifiedCitizenCredential",
  "issuerDid": "https://n1.sorcha.dev/issuers/gov-identity",
  "subjectDid": "did:sorcha:wallet:ws11q...",
  "status": "Active",
  "issuedAt": "2026-04-15T21:30:00Z",
  "expiresAt": "2027-04-15T21:30:00Z",
  "issuanceTxId": "...",
  "issuanceBlueprintId": "haip-verified-citizen-..."
}
```

**State machine enforcement**: the PATCH handler MUST reject transitions that violate `CredentialStatus` invariants INV-1 through INV-4 (see `data-model.md`). Attempting to set `Active` on a row that is not in `PendingAcceptance` returns `409 Conflict` with `{ error: "invalid-transition", from: "Declined", to: "Active" }`.

**Idempotency**: If the row is already `Active`, the PATCH is a successful no-op — returns 200 with the current row. No 409.

### 2. Register acceptance transaction (Blueprint Service)

**Endpoint**: `POST /api/instances/{instanceId}/actions/{actionId}/execute`

Where `actionId = 3` for the Verified Citizen pattern (the symbolic terminal claim action). Blueprint authors can choose other action IDs; the spec treats 3 as an example.

**Auth**: Standard user JWT. The signature on the submitted transaction must be produced by the holder's wallet — this is enforced by the existing `ActionExecutionService` signature verification path, unchanged.

**Request body**:
```json
{
  "blueprintId": "haip-verified-citizen-...",
  "actionId": "3",
  "instanceId": "...",
  "senderWallet": "ws11q...",
  "registerAddress": "af7b1040...",
  "payloadData": {}
}
```

The payload is empty — Action 3 has no form fields (the blueprint author declares an empty `dataSchema` or a schema with no required properties).

**Response** (200): standard `ActionExecuteResponse` — transaction id, next actions, instance state.

**Behaviour**: executes through the existing `ActionExecutionService` pathway. No special handling for Feature 106 — the engine treats Action 3 like any other action execution. The resulting transaction is sealed to the register, peer-synced, and observed by the issuer's Blueprint Service mirror reconstructor or normal execution path (depending on which node owns the instance authoritatively).

**Mirror rows**: The holder's Blueprint Service's mirror row is READ-ONLY. The client-issued `POST /execute` call against a mirrored instance on the holder's node MUST be routed to — or fail and fall back to — the authoritative node. **RESOLUTION**: the Blueprint Service's `/actions/{actionId}/execute` endpoint is the same endpoint regardless of whether the instance is locally authoritative or a mirror. The engine signs the transaction with the holder's wallet and submits to the validator. The validator's consensus path peer-replicates to all subscribed nodes, including the authoritative issuer's node, which then updates its own instance state from the execute transaction. The holder's mirror is eventually updated by the reconstructor observing its own copy of the execute transaction on subsequent `docket:confirmed` events — completing the loop.

## Decline flow

### 1. Local status update (Wallet Service)

**Endpoint**: `PATCH /api/v1/wallets/{walletAddress}/credentials/{credentialId}`

**Request body**:
```json
{
  "status": "Declined"
}
```

**Response** (200): same shape as accept, with `status: "Declined"`.

**Retention**: the row is NOT deleted. See data-model.md INV-3 — `Declined` is a terminal retained state.

### 2. Register rejection transaction (Blueprint Service)

**Endpoint**: `POST /api/instances/{instanceId}/actions/{actionId}/reject`

Where `actionId = 3`. Uses the existing blueprint engine rejection protocol — this endpoint already exists from wave 14b for the claim-card decline path.

**Request body**:
```json
{
  "reason": "(optional free-text reason)"
}
```

**Response** (200): standard `ActionRejectResponse` — transaction id, terminal state confirmation.

**Behaviour**: executes through the existing rejection pathway keyed on `RejectionConfig.IsTerminal = true` for Action 3. Seals a rejection transaction to the register. Issuer's instance transitions to `Rejected` when observed.

## Hard delete (optional, explicit)

For declined credentials, the holder can explicitly hard-delete the row:

**Endpoint**: `DELETE /api/v1/wallets/{walletAddress}/credentials/{credentialId}`

**Behaviour**: removes the row entirely. Already exists on the Wallet Service — no changes required. Mentioned here for completeness because FR-015 requires the explicit-delete path remain available.

## Client-side orchestration (accept path)

```csharp
// From MyActions or MyCredentials claim card click handler
public async Task OnAcceptClickedAsync(PendingCredential credential, CancellationToken ct)
{
    _acceptButton.Disabled = true;

    try
    {
        // Fire both in parallel
        var localUpdateTask = _walletClient.PatchCredentialStatusAsync(
            credential.WalletAddress, credential.Id, CredentialStatus.Active, ct);
        var executeTask = _blueprintClient.ExecuteActionAsync(
            new ActionExecuteRequest
            {
                BlueprintId = credential.IssuanceBlueprintId,
                ActionId = "3",
                InstanceId = credential.InstanceId,
                SenderWallet = credential.WalletAddress,
                RegisterAddress = credential.RegisterId,
                PayloadData = new Dictionary<string, object>()
            }, ct);

        await Task.WhenAll(localUpdateTask, executeTask);

        _snackbar.Show("VerifiedCitizenCredential is now active in your wallet");
        _onAccepted?.Invoke();
    }
    catch (Exception ex)
    {
        // One of the two calls failed. Reconcile.
        await ReconcileAcceptanceFailureAsync(credential, ex, ct);
    }
    finally
    {
        _acceptButton.Disabled = false;
    }
}
```

### Partial failure reconciliation

If **only the local PATCH succeeds**: the credential shows as `Active` in the local wallet store but the issuer's instance stays in pending-action state. The client MUST retry the register execute call on the next UI interaction or via a background retry. The credential is usable locally for presentations; the issuer just doesn't know yet.

If **only the register execute succeeds**: the credential shows as `PendingAcceptance` in the local wallet store but the register has an acceptance transaction. The inbound detector will observe the acceptance transaction on its own peer sync and can update the local status, OR the client can reconcile by issuing the PATCH retroactively on next load.

If **both fail**: UI shows an error snackbar and the credential stays in `PendingAcceptance`. User can retry.

The design's pragmatic position: in practice, both calls succeeding is the overwhelmingly common case, and partial failures are rare edge cases worth handling but not worth blocking the MVP on. The reconcile logic is best-effort.

## Validation contract (publish-time + run-time)

**Publish-time**:

- **VAL_BP_CRED_ACCEPT_001**: When an action's `credentialIssuanceConfig.targetAudience == SorchaLocalWallet` and the route from it targets a next action, the next action MUST have `RejectionConfig.IsTerminal == true` so the reject path terminates cleanly. (Warning, not error — an author can choose to route from the reject into another state if they want more complex flows.)

**Run-time**:

- PATCH handlers MUST enforce the state machine invariants. A PATCH to `Active` on a non-`PendingAcceptance` row returns 409. A PATCH to `Declined` on a non-`PendingAcceptance` row returns 409.
- The execute/reject endpoints MUST verify the transaction signature matches the holder's wallet. Unchanged from existing behaviour — the engine already enforces this.
- An accept or reject on a mirror-row instance on the holder's node MUST succeed — the execute call produces a signed transaction that the validator accepts regardless of whether the instance originates locally.

## Testing contract

- **Unit tests** (Wallet Service PATCH handler): happy-path accept, happy-path decline, invalid transitions return 409, idempotent no-op.
- **Unit tests** (client-side orchestration): mock the wallet client + blueprint client, assert parallel execution, assert reconcile logic handles each partial failure case.
- **Integration test**: end-to-end accept from a Playwright browser test — click CLAIM CREDENTIAL, assert status transitions to Active, assert transaction appears on register.
- **Integration test**: end-to-end decline from a Playwright browser test — click DECLINE, assert status transitions to Declined, assert rejection transaction appears on register.
- **Edge case test**: partial failure simulation — mock one of the two calls to fail, assert reconcile behaviour matches the contract.

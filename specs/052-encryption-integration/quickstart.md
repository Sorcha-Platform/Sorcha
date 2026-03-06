# Quickstart: Envelope Encryption Integration

**Feature**: 052-encryption-integration

## Prerequisites

- Docker Compose running (all services)
- Valid JWT token with wallet access
- A multi-recipient blueprint published to a register

## Scenario 1: UI Action Submission with Encryption Progress (US1)

1. Log in to the UI and navigate to "My Actions"
2. You should see a pending action on a multi-recipient blueprint
3. Click "Take Action" and fill in the form
4. Submit the action
5. **Expected**: An inline progress indicator appears showing:
   - Current stage (Resolving keys → Encrypting → Building transaction → Signing)
   - Progress percentage (10% → 30% → 60% → 80% → 100%)
   - Recipient count ("3 of 5 recipients")
6. When complete, the indicator shows the transaction reference and the action disappears from the pending list

## Scenario 2: Encryption Failure and Retry (US2)

1. Submit an action where one recipient's wallet has no published public key
2. **Expected**: Progress indicator reaches "Resolving keys" stage and then shows an error:
   - Error message: "Failed to resolve public key for recipient did:sorcha:w:..."
   - A "Retry" button appears
3. Fix the recipient's wallet (publish their public key)
4. Click "Retry"
5. **Expected**: A new progress indicator starts from 0%, completes successfully

## Scenario 3: Cross-Page Notification (US3)

1. Submit an action that triggers encryption
2. While the progress indicator is showing, navigate to the "Dashboard" page
3. **Expected**: A brief banner appears: "Encryption in progress — you'll be notified when it completes"
4. Wait for the encryption to finish
5. **Expected**: A toast notification appears on the Dashboard page with the transaction reference

## Scenario 4: Operations History (US4)

1. Submit several actions (mix of successful and failing ones)
2. Navigate to the "Operations" page
3. **Expected**: A paginated list showing all operations with:
   - Status icons (in-progress spinner, success check, failure X)
   - Blueprint/action name, recipient count, timestamps
4. Click a completed operation → see transaction reference
5. Click a failed operation → see error details and retry button

## Scenario 5: CLI Blocking Mode (US5)

```bash
# Submit action with default blocking behavior
sorcha action execute \
  --blueprint bp-001 \
  --action 1 \
  --instance inst-001 \
  --wallet did:sorcha:w:abc123 \
  --register reg-001 \
  --payload '{"field1": "value1"}'

# Expected output:
# ⠋ Resolving recipient keys... (10%)
# ⠋ Encrypting payloads... (30%)
# ⠋ Building transaction... (60%)
# ⠋ Signing and submitting... (80%)
# ✓ Transaction submitted: a1b2c3d4e5f6...
# Exit code: 0
```

## Scenario 6: CLI Non-Blocking Mode (US5)

```bash
# Submit action with --no-wait flag
sorcha action execute \
  --blueprint bp-001 \
  --action 1 \
  --instance inst-001 \
  --wallet did:sorcha:w:abc123 \
  --register reg-001 \
  --payload '{"field1": "value1"}' \
  --no-wait

# Expected output:
# Operation started: abc123def456
# Use 'sorcha operation status abc123def456' to check progress.
# Exit code: 0

# Check status later
sorcha operation status abc123def456

# Expected output:
# ┌──────────────────┬───────────────────────┐
# │ Field            │ Value                 │
# ├──────────────────┼───────────────────────┤
# │ Operation ID     │ abc123def456          │
# │ Stage            │ complete              │
# │ Progress         │ 100%                  │
# │ Recipients       │ 5 / 5                 │
# │ Transaction      │ a1b2c3d4e5f6...       │
# └──────────────────┴───────────────────────┘
```

## Scenario 7: SignalR Real-Time Updates (US6)

1. Open browser dev tools → Network tab → WS filter
2. Submit an action that triggers encryption
3. **Expected**: WebSocket messages appear on the ActionsHub connection:
   - `EncryptionProgress` messages at each stage transition
   - `EncryptionComplete` or `EncryptionFailed` at the end
4. Verify the progress indicator updates within 1 second of each message

## Validation Checklist

- [ ] Progress indicator appears immediately after async submission
- [ ] All 4 stages display correctly with percentages
- [ ] Error messages show failure reason and affected recipient
- [ ] Retry button re-submits without data re-entry
- [ ] Toast notification appears on non-actions pages
- [ ] Navigation warning banner is non-blocking
- [ ] Operations history shows all past operations
- [ ] Operations history paginates correctly
- [ ] CLI blocking mode shows progress and waits
- [ ] CLI --no-wait mode returns immediately with operation ID
- [ ] Existing `sorcha operation status` works with encryption operation IDs
- [ ] SignalR push updates arrive within 1 second
- [ ] Polling fallback works when SignalR is disconnected

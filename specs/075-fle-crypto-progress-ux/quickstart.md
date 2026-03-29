# Quickstart: FLE Completion & Crypto Progress UX

## Validation Scenarios

### Scenario 1: Per-Recipient Progress (US1 + US2)

**Setup**: Docker Compose environment running, non-DevMode register with published blueprint (council credential flow).

**Steps**:
1. Open the Sorcha UI and navigate to the workflow catalogue
2. Select the council credential workflow and click "New Submission"
3. Fill in the form and submit
4. **Observe**: A floating panel appears bottom-right showing "Securing your submission"
5. **Observe**: The panel shows 3 recipients with status transitions:
   - Citizen — decision field → waiting → encrypting → secured
   - ID Department — all fields → waiting → encrypting → secured
   - Planning Office — site details → waiting → encrypting → secured
6. **Observe**: On completion, panel shows "Submission secured — 3 recipients can now access their disclosed fields" with a "View transaction" link
7. Click "View transaction" — navigates to transaction explorer showing the encrypted transaction

**Expected**: Per-recipient updates appear in real-time. No cryptographic terminology visible.

### Scenario 2: Minimise and Dismiss (US1)

**Steps**:
1. Submit an action (as above)
2. While the floating panel is showing progress, click the minimise button (—)
3. **Observe**: Panel collapses to a compact pill: "Securing — 1/3 recipients" with mini progress bar
4. Click the pill to expand back to full panel
5. Submit another action
6. While the panel is showing progress, click the dismiss button (×)
7. **Observe**: Panel disappears entirely
8. **Observe**: When encryption completes, a toast appears: "Submission secured — 3 recipients..."

### Scenario 3: Navigate Away During Encryption (US1)

**Steps**:
1. Submit an action
2. While the floating panel is showing progress, navigate to a different page (e.g., Registers)
3. **Observe**: The floating panel persists on the new page and continues showing progress
4. **Observe**: On completion, success state shown on whatever page the user is on

### Scenario 4: Encryption Failure (US5)

**Setup**: Revoke a participant's published record on the register before submitting.

**Steps**:
1. Revoke the Planning Office participant record
2. Submit an action that discloses to the Planning Office
3. **Observe**: Floating panel shows:
   - Citizen → secured
   - ID Department → secured
   - Planning Office → **failed** (red)
4. **Observe**: Error message: "Encryption failed — Could not resolve key for Planning Office — participant record may be revoked"
5. Click "Retry" — new operation begins

### Scenario 5: DevMode Register (US3)

**Steps**:
1. Create a register with `devMode: true`
2. Submit an action
3. **Observe**: No encryption progress panel appears (encryption bypassed)
4. Query the action as the citizen — only disclosed fields returned
5. Query the register directly in MongoDB — payload is plaintext JSON

### Scenario 6: DevMode Toggle (US3)

**Steps**:
1. With a DevMode register, submit an action — payload stored as plaintext
2. Toggle DevMode off: `PUT /api/registers/{id}/devmode` with `{ "enabled": false }`
3. Submit another action — encryption progress panel appears, payload encrypted
4. Query each participant — only their disclosed fields are decrypted

### Scenario 7: Polling Fallback (US2)

**Steps**:
1. Disconnect SignalR (disable WebSocket in browser dev tools)
2. Submit an action
3. **Observe**: Floating panel still shows progress (via 2-second polling)
4. **Observe**: Per-recipient status updates arrive (slightly delayed vs SignalR)
5. Re-enable WebSocket — panel switches to real-time updates

## Verification Checklist

- [ ] Per-recipient progress events arrive via SignalR for each recipient
- [ ] Floating panel shows expanded, minimised, and dismissed states
- [ ] Panel persists across page navigation
- [ ] Dismissed panel triggers toast on completion
- [ ] Error states show failing recipient name and actionable retry
- [ ] DevMode register skips encryption pipeline
- [ ] DevMode toggle endpoint requires CanManageRegisters authorisation
- [ ] Polling fallback works when SignalR is unavailable
- [ ] Multiple concurrent operations tracked (badge counter)
- [ ] All existing tests pass (zero regressions)

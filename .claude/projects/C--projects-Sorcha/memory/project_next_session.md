---
name: project_next_session
description: P0 priority — fix SignalR real-time notification for next participant after action execution
type: project
---

## P0: SignalR ActionAvailable notification not reaching next participant

**Why:** Agents currently discover pending actions via 30s polling only. SignalR should deliver sub-second notifications but the async execution pipeline doesn't trigger `NotifyParticipantsAsync` at the right point.

**How to apply:** Fix this before any other walkthrough work — it's the difference between 30s cycles and sub-second cycles.

### What's done
- `NotificationService.NotifyActionAvailableAsync` now sends to both `instance:{id}` and `wallet:{address}` groups
- `SignalRInboxListener` handles both notification shapes (instance-based and wallet-based)
- Agents connect to `/actionshub`, subscribe to wallet, and listen for `ActionAvailable`

### What's broken
- `NotifyParticipantsAsync` in `ActionExecutionService.cs:1097` IS called after routing determines next actions
- BUT: the action execution pipeline is async — the endpoint returns immediately, the Validator confirms the transaction later
- The notification may only fire on the synchronous path, not after the async validator callback
- Docker logs show NO "Sent ActionAvailable" entries during agent test runs

### Investigation path
1. Trace `ActionExecutionService.ExecuteActionAsync` — does it await the validator confirmation before calling `NotifyParticipantsAsync`, or does it fire-and-forget?
2. Check `WaitForTransactionConfirmationAsync` (line 1121) — is this called before or after notification?
3. If notification fires before confirmation, the instance `CurrentActionIds` may not have advanced yet
4. The fix may be to move `NotifyParticipantsAsync` to AFTER `WaitForTransactionConfirmationAsync` completes

### Key files
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` (lines 108-637, 1097-1119)
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/NotificationService.cs` (lines 118-150)
- `src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs`

# Known Issues: 086 Validator Key Roster

## EDGE-001: Relay sync returns 0 dockets for registers with only genesis (height=1)

**Status**: Open  
**Severity**: Low (only affects registers with no sealed transactions yet)  
**Observed**: 2026-04-06

**Symptom**: When a remote peer subscribes to a register that only has the genesis docket (height=1, docket at index 0), the relay sync fallback serves 0 dockets. Registers with multiple dockets (height > 1) sync correctly — confirmed with 50 dockets + 62 transactions.

**Root cause candidates**:
1. The relay request `FromDocketVersion` may arrive as 0 (default `long`) instead of -1 due to JSON serialization of the initial subscription state
2. The `HasMore` logic post-loop condition may have an off-by-one (claude-review item #2)
3. The `ReadDocketAsync` call may return null for docket 0 in certain timing conditions

**Workaround**: Once a register has at least 2 dockets (any transaction sealed), sync works correctly. Genesis-only registers are transient — they gain dockets as soon as any transaction is submitted.

**Investigation path**: Add diagnostic logging for `fromVersion` value received in `PopulateResponseFromRegisterServiceAsync` to confirm whether the relay request carries -1 or 0.

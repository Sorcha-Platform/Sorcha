# Known Issues: 086 Validator Key Roster

## EDGE-001: Relay sync returns 0 dockets for registers with only genesis (height=1)

**Status**: RESOLVED  
**Severity**: Low (only affects registers with no sealed transactions yet)  
**Observed**: 2026-04-06

**Symptom**: When a remote peer subscribes to a register that only has the genesis docket (height=1, docket at index 0), the relay sync fallback serves 0 dockets. Registers with multiple dockets (height > 1) sync correctly — confirmed with 50 dockets + 62 transactions.

**Root cause**: `RegisterSubscriptionEntity.LastSyncedDocketVersion` in `PeerDbContext.cs` defaulted to `0` while the domain model `RegisterSubscription` defaulted to `-1`. When a subscription was persisted to the database (during the Subscribing → Syncing transition) and loaded back on the next sync cycle, the entity default of 0 took over. This meant "I already have docket 0, give me from docket 1" — skipping genesis entirely. For height=1 registers, docket 1 doesn't exist → 0 dockets served.

**Fix**: Changed `RegisterSubscriptionEntity.LastSyncedDocketVersion` default from `0` to `-1` to match the domain model.

**Workaround**: Once a register has at least 2 dockets (any transaction sealed), sync works correctly. Genesis-only registers are transient — they gain dockets as soon as any transaction is submitted.

**Investigation path**: Add diagnostic logging for `fromVersion` value received in `PopulateResponseFromRegisterServiceAsync` to confirm whether the relay request carries -1 or 0.

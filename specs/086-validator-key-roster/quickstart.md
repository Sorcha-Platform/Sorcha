# Quickstart: Validator Key Roster

## Scenario 1: Create Register with Validator Roster (P1)

**Setup**: Two nodes — Node A (local, NAT'd) and Node B (n1.sorcha.dev). Both running updated peer service.

**Steps**:
1. Create a new register on Node A via the standard creation flow (initiate + finalize)
2. Inspect the genesis control transaction — verify `validators` field exists with one entry
3. The entry should contain the validator's purpose-derived public key (not the system wallet root key)
4. Subscribe Node B to the register
5. Wait for full-replica sync via relay
6. Verify all dockets finalize successfully on Node B (register height matches Node A)

**Verify**:
- Genesis control record JSON contains `"validators": { "validators": [...], "requiredSignatures": 1, "version": 1 }`
- Validator entry has `status: "Active"`, `derivationContext: "sorcha:docket-signing"`
- Node B's register height equals Node A's register height
- No "validator key not available" errors in Node B's peer service logs

## Scenario 2: Docket Signed by Unknown Key is Rejected (P1)

**Steps**:
1. Create a register on Node A with the validator roster
2. Manually craft a docket signed by a different key (not in the roster)
3. Attempt to finalize it on Node B

**Verify**:
- Docket finalization rejects with "signer not authorized" error
- Register height does not advance

## Scenario 3: Add Validator via Governance (P2)

**Steps**:
1. Create register on Node A (1 validator in roster)
2. Submit governance proposal: operation `add-validator` with Node B's validator public key
3. Approve the proposal (owner quorum)
4. Verify new control transaction recorded with 2 validators
5. Submit a transaction on the register, let Node B's validator seal the docket
6. Sync to a third node — verify the docket signed by Node B's validator is accepted

**Verify**:
- Control transaction contains 2 validators (both Active)
- Dockets from either validator pass verification
- `requiredSignatures` remains 1 (single-signer mode)

## Key Commands

```bash
# Create register (standard flow — validator roster auto-populated)
# Uses existing walkthrough scripts or CLI

# Inspect genesis control record
curl -s "http://localhost/api/register/registers/{registerId}/dockets/0" \
  -H "Authorization: Bearer $TOKEN" | python -m json.tool

# Check peer sync status
curl -s "http://localhost/api/peers" \
  -H "Authorization: Bearer $TOKEN" | python -m json.tool

# Check register height on remote node
curl -sk "https://n1.sorcha.dev/api/registers" \
  -H "Authorization: Bearer $N1_TOKEN" | python -m json.tool
```

# Construction Permit Walkthrough Results

**Date:** _TBD (run the walkthrough to populate)_
**Status:** Pending
**Profile:** gateway

---

## Summary

| Metric | Value |
|--------|-------|
| Organization | Construction Permit Demo |
| Participants | 5 (across 4 organisations) |
| Wallets | 6 (ED25519) |
| Register | _TBD_ (public) |
| Blueprint | _TBD_ (published) |
| Scenarios Run | A, B, C |
| Total Duration | _TBD_ |

---

## Scenario Results

### Scenario A: Low-Risk Residential

| Step | Action | Participant | Status | Notes |
|------|--------|-------------|--------|-------|
| 1 | Submit Application | contractor | _TBD_ | 3 storeys, 800 m2, residential, £500k |
| 2 | Structural Assessment | structural-engineer | _TBD_ | riskScore = 6.1 (calculated) |
| 3 | Planning Review | planning-officer | _TBD_ | riskScore < 7 → skip environmental |
| — | _Environmental skipped_ | — | — | Low risk, routed directly to action 5 |
| 5 | Building Control | building-control | _TBD_ | permitFee = £2,200 (calculated) |
| 6 | Final Approval | planning-officer | _TBD_ | Building Permit VC issued |

**Expected:** 5 actions, riskScore 6.1, permitFee £2,200, BuildingPermitCredential issued
**Actual:** _TBD_

### Scenario B: High-Risk Commercial

| Step | Action | Participant | Status | Notes |
|------|--------|-------------|--------|-------|
| 1 | Submit Application | contractor | _TBD_ | 8 storeys, 3500 m2, commercial, £5M |
| 2 | Structural Assessment | structural-engineer | _TBD_ | riskScore = 22.8 (calculated) |
| 3 | Planning Review | planning-officer | _TBD_ | riskScore >= 7 → environmental review |
| 4 | Environmental Assessment | environmental-assessor | _TBD_ | impact: medium, mitigation: true |
| 5 | Building Control | building-control | _TBD_ | permitFee = £15,250 (calculated) |
| 6 | Final Approval | planning-officer | _TBD_ | Building Permit VC with env conditions |

**Expected:** 6 actions, riskScore 22.8, permitFee £15,250, BuildingPermitCredential issued
**Actual:** _TBD_

### Scenario C: Rejection

| Step | Action | Participant | Status | Notes |
|------|--------|-------------|--------|-------|
| 1 | Submit Application | contractor | _TBD_ | 4 storeys, 1200 m2, commercial, £2M |
| 2 | Structural Assessment | structural-engineer | _TBD_ | riskScore = 10.08 (calculated) |
| 3 | Planning Review | planning-officer | _TBD_ | zoningCompliant: false → REJECT |

**Expected:** 3 actions, rejected at action 3, routed back to contractor
**Actual:** _TBD_

---

## Capabilities Verified

| Capability | Status | Notes |
|------------|--------|-------|
| Multi-org participation (4 orgs) | _TBD_ | Stoniebridge, Murchison, Strathcarron, Heatherbank |
| Same-org multi-user | _TBD_ | planning-officer + building-control = Strathcarron Council |
| JSON Logic calculation (riskScore) | _TBD_ | (storeys × 1.5 + area/500) × type multiplier |
| JSON Logic calculation (permitFee) | _TBD_ | max(250, value × 0.002 + area × 1.50) |
| Conditional routing | _TBD_ | riskScore >= 7 → environmental review |
| Rejection routing | _TBD_ | Actions 3, 4, 6 → back to action 1 |
| Selective disclosure | _TBD_ | Each participant sees only relevant data |
| Verifiable credential issuance | _TBD_ | BuildingPermitCredential on final approval |
| Public register | _TBD_ | Register created with isPublic: true |

---

## Known Limitations

- All actions are executed under a single admin JWT with delegation token (no per-org auth)
- Verifiable credential issuance is verified by successful action execution, not by inspecting the VC itself
- Environmental review skip is verified by checking the action path, not by inspecting routing decisions
- Rejection routing verification depends on the Blueprint Service's rejection handling implementation

---

## Troubleshooting

### Common Issues

| Issue | Solution |
|-------|----------|
| `Connection refused` | Ensure Docker services are running: `docker-compose up -d` |
| `401 Unauthorized` | JWT token expired — re-run walkthrough to get fresh token |
| `409 Conflict` on bootstrap | Organization already exists — script falls back to login |
| `Blueprint publish failed` | Check for cycle warnings (expected for rejection paths) |
| `Action execution failed` | Check Blueprint Service logs: `docker-compose logs blueprint-service` |
| `Register creation failed` | Check Register Service logs: `docker-compose logs register-service` |

### Viewing Logs

```powershell
# Blueprint Service logs
docker-compose logs -f blueprint-service

# Register Service logs
docker-compose logs -f register-service

# All service logs
docker-compose logs -f
```

---

## Next Steps

After running this walkthrough:

1. **Verify VC contents** — Query the register for the Building Permit credential
2. **Test with real multi-org** — Bootstrap 4 separate organizations with independent JWT tokens
3. **UI walkthrough** — Execute the same scenarios through the Sorcha UI
4. **Distributed test** — Run across multiple Sorcha nodes (see DistributedRegister walkthrough)

# Quickstart: validate the re-anchored issuer DID end-to-end

Validates SC-001 (trusted issuance), SC-002 (canonical `iss`), SC-003 (fail closed), SC-004 (rotation). Run against the local Docker stack after rebuilding the touched service images.

## Rebuild (clean break — wipe dev data)

```powershell
docker compose down -v                         # wipe — clean break, no migration
docker compose build tenant-service wallet-service blueprint-service haip-service
docker compose up -d
```

## Path A — trusted issuance accepted (SC-001, SC-002)

Use the CyberEssentialsUac shape (assessor issues, insurer requires with a `did-allowlist` pinned to the assessor's canonical DID):

```powershell
pwsh walkthroughs/CyberEssentialsUac/setup.ps1        # NOTE: the separate walkthrough PR adds Set-SorchaOrgMasterKey
pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1   # expect S1-6 trust check to PASS, reach S1-7
```

Manual assertions:

1. Decode the issued credential's JWS header + payload:
   - `iss == did:sorcha:org:{assessor operational wallet A}` (NOT a derived child address, NOT a bare `ws1q...`).
   - `kid == did:sorcha:org:{A}#vc-issuance-1`.
2. `GET http://localhost/api/orgs/by-did/{urlencoded did:sorcha:org:A}/did.json` → 200, contains a VM `…#vc-issuance-1` with the issuance key JWK, referenced from `assertionMethod`.
3. Insurer trust step logs issuer signature **verified** and issuer **trusted** (allowlist on A matches `iss`).

## Path B — fail closed (SC-003)

Issue from an org that has **no** master key (skip `Set-SorchaOrgMasterKey`):

```powershell
# attempt a SorchaLocalWallet issuance for a key-less org
```

Expect: the Wallet mint returns 409/422 with an actionable message naming the missing master key; the SorchaLocalWallet action **fails** (`[VAL_RUNTIME_CRED_002]`); **no** credential is delivered. No bare-wallet `iss` credential exists anywhere.

## Path C — rotation (SC-004)

```powershell
# rotate the assessor's issuance key (governance op), then issue again
```

Expect: the new credential's `kid == …#vc-issuance-2`; the published `did.json` lists both active VMs; verification of the index-2 credential resolves and passes.

## Logs

```powershell
docker logs sorcha-blueprint-service --since 3m | Select-String "TrustEvaluator|issuer signature|not trusted|published"
docker logs sorcha-wallet-service    --since 3m | Select-String "issuance key|fail|vc-issuance|wallet-address"
docker logs sorcha-tenant-service    --since 3m | Select-String "did.json|by-did|wallet-address"
```

## Regression

```powershell
dotnet test --filter "FullyQualifiedName~Credential|FullyQualifiedName~Did|FullyQualifiedName~Issuance|FullyQualifiedName~Trust"
pwsh scripts/check-trust-clean-break.ps1
```

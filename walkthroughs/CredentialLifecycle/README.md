# CredentialLifecycle — the standard credential conformance check

Drives one credential through **every state the two status-list specifications define**, and after
each transition asks the platform's own gate whether the credential is usable.

```bash
pwsh walkthroughs/CredentialLifecycle/setup.ps1          -Profile n1
pwsh walkthroughs/CredentialLifecycle/run-conformance.ps1 -Profile n1
```

Exit code 0 means every check passed. A per-phase summary and an explicit list of failures — each
with why it matters — is printed either way.

## What it checks

| Phase | Check |
|---|---|
| **P0** | A **new** credential is issued and delivered; it declares a revocation entry *and* a suspension entry; the two share one index but use **different lists** |
| **P1** | Active → gate **accepts** |
| **P2** | Suspend → reports `Suspended` (not `Revoked`), flips a bit, gate **refuses** |
| **P3** | Reinstate → gate **accepts the same credential again** |
| **P4** | Revoke → gate **refuses** |
| **P5** | Reinstate after revoke → **refused**, and with a 4xx decision rather than a 5xx failure |
| **P6** | W3C Bitstring Status List: retrievable, declares its `statusPurpose`, `encodedList` decodes (multibase/base64 + GZip), and the credential's own bit reads correctly MSB-first |
| **P7** | IETF Token Status List: compact JWS, `typ: statuslist+jwt`, `sub`+`iat`, `bits` ∈ {1,2,4,8}, `lst` **wide enough for the width it declares**, entry reads `0x01` INVALID |
| **P8** | A second credential has its own entry number, is **accepted while the first is revoked**, and suspending it does **not** set its revocation bit |

39 checks in total.

## Why it is shaped this way

**P3 is the assertion a refusal test can never make.** Suspension and revocation both refuse, so a
regression that quietly makes suspension terminal passes every "is it blocked?" test ever written.
Only getting the *same* credential accepted again proves the reversibility is real.

**P5 is its mirror.** Revocation is "not reversible" (W3C) and means "revoked, annulled, taken back,
recalled or cancelled" (IETF). A platform that lets a revoked credential be reinstated is not
lenient, it is non-conformant.

**P6/P7 read what we publish, decoded from the credential's own `credentialStatus` entries** rather
than from any convenience API. A verifier we have never met reads those bytes. Asserting that our
reader agrees with our writer proves nothing — which is exactly how #1492 shipped a `bits: 2` header
over a 1-bit array that our own checker then misread. P7's width check is that defect, pinned.

**P8 exists because #1491, #1492 and #1502 were all the same shape**: the right operation applied to
the wrong entry. That is invisible unless a second credential is watching.

**The gate's verdict is the evidence** — never the wallet's status field, and never the HTTP 202 of
a submission. A schema violation returns 202 and a transaction id, and then simply never seals.

## Design notes

- **Two participants, both pre-bound.** A conformance check should fail for one reason at a time, so
  open participants, late binding and multi-org routing are deliberately left out. The gate blueprint
  carries a third `verifier` participant only because a blueprint requires at least two, and it is
  given no action of its own so the run never has to advance an instance between submissions.
- **Every credential is pinned by id, never by type.** Selecting by type in a wallet that accumulates
  credentials is the direct cause of #1477 defect 2, #1483 and #1503 — including one false security
  report. `New-ConformanceCredential` snapshots the wallet before issuing and requires a credential
  that is genuinely new.
- **Nothing aborts the run.** Lifecycle calls return a result object instead of throwing, so a
  platform failure in P4 still lets P5–P8 report. Dying on the first 500 tells you one thing when the
  run was about to tell you eight.
- **A 5xx is not a refusal.** P5 distinguishes "the platform declined" from "the platform fell over".

## Setup requirements this encodes

Each of these was a defect before it was a step:

1. **Org-scoped operators**, never public users promoted into an org — multi-org breaks the OAuth
   password grant with a 401.
2. **Re-login after linking a wallet.** `wallet_address` enters the JWT only at login, so the token
   held while creating the wallet does not carry it and the blueprint publish 403s.
3. **Wait for the register-genesis roster to seal** before publishing — an empty roster fail-closes
   with the same 403 as a missing claim.
4. **Publish participants onto the register, and prove their keys resolve.** Registering a
   participant links a wallet in the *tenant*; it does not put the key on the *register*. Without it:
   every recipient is skipped → the payload has no disclosure-group envelope → it cannot be decrypted
   → **claim mappings find nothing and are dropped, so the credential mints with no claims** → and it
   is never delivered. Four silent steps behind HTTP 202/200, surfacing as a claim-mapping warning
   that points at the schema. `setup.ps1` calls `resolve-public-keys` and **throws** if the keys are
   missing, rather than letting the run continue and fail somewhere unrecognisable.
5. **A Feature 083 master key for the issuer.** Without it the mint silently falls back to the org's
   root wallet key and produces a credential with no `kid` and no `jwk` — unverifiable, and the gate
   then refuses everything with "issuer signature not verified".
6. **Pin the issuance DID, not the operational one.** With a master key, `iss` carries a *derived*
   vc-issuance child address. Pinning the operational wallet DID yields a policy that matches nothing.
   Resolve it from the org's `did.json`; never reconstruct the string.

## Adding a check

Call `Check <phase> <name> <bool> [<detail>]`. The detail should say *what it means that this
failed*, not restate the assertion — it is what someone reads at 2am. Prefer asserting the gate's
verdict or the published bytes over any status field the platform reports about itself.

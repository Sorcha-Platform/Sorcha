# EncryptionAtRest — is a Normal register's payload actually ciphertext?

Sorcha's central claim is that a **Normal** (non-DevMode) register stores field **values** as
ciphertext. Until this walkthrough, nothing verified that from what is actually stored.

`Payloads[].Data` is Base64Url-encoded either way, so an unencrypted DevMode payload looks exactly
as opaque in `mongosh` as a real ciphertext. The only distinguishing field is
`contentEncoding: "encrypted"`, and no automated check had ever decoded the bytes to see which one
was really there. The closest thing that existed — `TradeFinance/run.ps1 -VerifyFLE` — read the
register through the API as different role-holders and printed `(encrypted)` wherever a field was
missing from the **response body**. It asked the API what it was willing to show and treated a
filtered response as evidence about storage.

Issues: **#1580** (encryption at rest is unverified) and **#1579** (the DevMode → FLE promotion had
no working automated coverage — the one walkthrough that claimed to drive it called a route that
does not exist).

## Run it

```bash
pwsh walkthroughs/EncryptionAtRest/setup.ps1        -Profile n1 -Force
pwsh walkthroughs/EncryptionAtRest/run-conformance.ps1 -Profile n1

# with the replica half as well
pwsh walkthroughs/EncryptionAtRest/run-conformance.ps1 -Profile n1 \
     -ReplicaSshHost tiny -ReplicaGatewayUrl http://tiny:8090
```

25 checks. ~90 seconds after setup. The probe reaches MongoDB over `ssh` + `docker exec`; against
`-Profile n1` the ssh host is derived automatically, and for a local Docker stack it uses `docker`
on this machine. Override with `-OwnerSshHost`.

**The register is single-use.** Promotion is one-way, so `setup.ps1` provisions a fresh,
stamp-named register on every run and refuses to reuse one.

## Why it is shaped this way

### The pairing is the whole design

**"The sentinel is absent" is not evidence of encryption.** It is equally the result of a probe
looking at the wrong register, at the wrong transaction, or one that silently failed to decode —
the vacuous pass this codebase keeps producing. Only a pair discriminates: the **same** probe must
**find** a known value while the register is in DevMode and **fail to find it** once it is Normal.

So Phase 1 runs first and is a **hard gate**. If the probe cannot see plaintext that is
demonstrably there, the run stops and says so rather than reporting the encrypted half.

### One register, promoted mid-run — not two registers

Comparing two static registers never exercises the transition, which is the operation that changes
whether stored payloads are readable and the one thing #1579 says nothing tests. Promoting one
register also removes a confounder: the two halves are the same register, the same blueprint, the
**same action**, and the same field names, executed on two instances. Only the register's mode
differs. Comparing action 1 with action 2 would let a difference be attributed to the action.

### Ciphertext versus an encoding

A payload that was merely **encoded** would pass any check that only looks for the raw value. So:

- every sentinel is searched for as raw text, base64, base64url, hex and UTF-16 (`P3.3`);
- the `encryptedPayloads[].ciphertext` is base64-decoded and checked directly — it must not parse
  as JSON, and it must not contain the payload's **field names**, which an encoding would preserve
  even for a field whose value nobody thought to use as a sentinel (`P3.4`).

### Three guards against the harness lying to itself

| Check | Guards against |
|---|---|
| `P1.5` | the probe being a **yes-machine** — a sentinel that was never submitted must be absent from the very bytes where the real ones were found |
| `P3.0` | a shape predicate hard-wired to `encrypted` — the same predicate must return `plaintext` for the DevMode transaction |
| `P3.7` | the search being a **no-machine** on the encrypted envelope — the same `Find-SorchaSentinel` call, over the same bytes, must still find a field name that is in the clear there |

Without `P3.7` in particular, a search that simply failed to read the encrypted envelope would
report every value absent and look like a perfect pass.

### The replica half is gated on the transaction being there

A node that never received the transaction trivially cannot read it. Scoring that as "the replica
cannot decrypt" would be the emptiest claim in the design, so `P6.1` asserts the replica genuinely
holds the transaction first; if it does not, the replica checks report **NOT RUN**, never PASS.

## What the checks cover

| Phase | What it establishes |
|---|---|
| **0** | the storage probe can reach the owning node's MongoDB |
| **1** | *(hard gate)* in DevMode the probe **finds every submitted value**, the envelope is the plaintext shape, and an unsubmitted sentinel is absent |
| **2** | `POST /api/registers/{id}/disable-dev-mode` is accepted, returns a control-transaction id, the register reports `devMode=false` only once it **seals**, and that transaction is on the ledger — not a local flag flip |
| **3** | the Normal payload is the encrypted shape, **no** submitted value is recoverable in any encoding, the ciphertext is opaque, field **names** are in the clear as intended, recipients are still named, plus the two self-tests above |
| **4** | the next action in the same flow is encrypted too |
| **5** | promotion is **not retrospective** — payloads sealed before it stay plaintext forever |
| **6** | a node that legitimately replicates the register still cannot read the values |

## Findings this shape surfaces

**Promotion protects what a register stores next, not what it already holds** (`P5.1`). The ledger
is immutable, so everything sealed while the register was in DevMode remains plaintext permanently.
That is correct behaviour, and operators need to be told it rather than infer it.

**`P3.2` is the check that catches the fail-open.** On a Normal register, if no recipient key
resolves, `ActionExecutionService` falls through to the plaintext transaction builder and writes
clear values — with only a `recipient skipped` warning. `setup.ps1` therefore asserts
`resolve-public-keys` returns no `notFound` **before** anything runs, so this check measures
encryption rather than a fail-open.

## Files

| File | Purpose |
|---|---|
| `StorageProbe.psm1` | reads what a node actually stored, straight out of MongoDB |
| `setup.ps1` | org, wallets, published participants, DevMode register, blueprint |
| `run-conformance.ps1` | the 25 checks |
| `blueprints/encryption-at-rest.json` | two actions whose only job is to carry distinctive values |

### `StorageProbe.psm1` — three things it has to get right

1. **`Payloads[].Data` is a BSON Binary**, not a string. `print(...)` renders
   `Binary.createFromBase64(...)` and `.Data.length` is a *function*, so a naive read yields
   misaligned bytes that fail to decode — and a probe that fails to decode finds no sentinel, which
   is indistinguishable from "the value is encrypted". Extraction goes through `EJSON.stringify`.
2. **Quoting.** The script must survive PowerShell → ssh → `docker exec` → `mongosh`. Instead of
   escaping through four layers it is base64'd here and decoded on the far side. `mongosh` also
   evaluates piped stdin **one line at a time**, so every script is collapsed to a single line —
   a multi-line statement arrives as a series of syntax errors and produces no output at all.
3. **An empty result is never a negative result.** Every read distinguishes "this node holds no
   database for that register", "the register is here but the transaction is not", and "the
   transaction is here". A probe failure raises rather than returning "not found".

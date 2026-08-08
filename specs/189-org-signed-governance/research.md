# Phase 0 Research: Real register governance (Feature 189)

All findings below were established by reading current source and, where marked **[LIVE]**, by
executing against the running n1 node on 2026-08-06. Log excerpts are quoted verbatim.

---

## R-001: Why governance cannot complete today **[LIVE]**

**Finding.** A governance control transaction is signed by the **node's system wallet** via
`ISystemWalletSigningService` at `sorcha:register-control` (slot 101). A register's governance
roster is reconstructed from its genesis attestations, which record the **organisation owner's**
wallet key. `RightsEnforcementService` matches the transaction's signer against the roster and
rejects when absent.

```
RightsEnforcementService: Transaction fd028b6b… on register 6b0760aa…: submitter not found in roster
ValidationEngineService:  Register 6b0760aa…: validated 0 transactions, rejected 1
```

Roster (1 member): `role=Owner subject=did:sorcha:w:ws11qzujalxsw… publicKey=uS780HTirYET…=`
Signer: `ws11qzu6qq9k5lmvasvxta7g…` — the node system wallet. An identity mismatch, not encoding.

**Consequence.** No governance operation completes on any register whose genesis has sealed —
crypto-policy changes, Add/Remove/Transfer, and validator-key operations alike.

**Decision.** Governance control transactions are signed by an organisation on the roster, using
its governance key. `ISystemWalletSigningService` remains correct for *node* duties (genesis
ingestion, docket signing) and is not touched.

---

## R-002: The apparent success before genesis seals is a race, not a feature **[LIVE]**

**Finding.** `RightsEnforcementService.ValidateGovernanceRightsAsync` returns success when
`roster == null`, commented as allowing the register's first (genesis) control transaction.
Before genesis seals there is no roster, so **any** control transaction is admitted.

A first live run of `POST /disable-dev-mode` appeared to pass for exactly this reason: the register
had `height=0` and the policy update was swept into the genesis docket. Re-running against a
register whose genesis had sealed produced the R-001 rejection.

**Implication for the plan.** Any test that promotes DevMode on a freshly-created register may pass
without exercising the intended path. Tests and live verification MUST act on a register whose
genesis has sealed (`height >= 1`).

**Decision.** Keep the `roster == null` allowance — genesis genuinely precedes the roster — but
constrain it: it applies only to a transaction that *creates* the roster. Alternatives considered:
removing it entirely (breaks register creation), and gating on docket height (height is not
available to the validator at that point without a round trip).

---

## R-003: Roster key comparison is broken independently of who signs

**Finding.** `RegisterAttestation.PublicKey` is documented and stored as **standard base64**
(observed live: `uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc=` — note the `=` padding).
`RightsEnforcementService` compares it against:

```csharp
var submitterPublicKey = Base64Url.EncodeToString(transaction.Signatures[0].PublicKey);
… Attestations.FirstOrDefault(a => a.PublicKey == submitterPublicKey)
```

`Base64Url.EncodeToString` produces **unpadded base64url** — a different alphabet (`-_` vs `+/`)
and no padding. The two strings cannot match for any key containing `+`, `/`, or requiring padding.

**This is a second, independent defect.** Fixing only the signer (R-001) would still leave the
comparison failing, and the symptom would be identical — "submitter not found in roster" — which
would read as an unfixed R-001 and cost a full deploy cycle to diagnose.

**Decision.** Compare **decoded key bytes**, not encoded strings. A shared helper decodes either
encoding (padded/unpadded, base64/base64url) to bytes and compares with a fixed-time byte
comparison. Applies everywhere roster keys are matched.

**Alternatives considered.** Normalising the roster to base64url at genesis — rejected: it changes
the on-ledger representation of an immutable record, and still fails for existing registers.
Normalising at read time to a canonical string — acceptable, but byte comparison is stricter and
removes the encoding question entirely.

---

## R-004: `/propose` transactions bypass roster enforcement entirely

**Finding.** `RightsEnforcementService.IsGovernanceTransaction` returns true when
`Metadata["transactionType"] == "Control"`. But across the platform that key carries
`GovernanceOperation` / `CryptoPolicyUpdate` / `BlueprintPublish`; the key carrying `"Control"` is
`Metadata["Type"]` (this is what `DocketRegisterProjection.ResolveTransactionType` reads).

A `/propose` transaction is built with `BlueprintId = string.Empty` and
`transactionType = "GovernanceOperation"`, so it matches **none** of the three arms and governance
rights are **not enforced** on it — a hole in the opposite direction to R-001.

It is currently masked: the same transaction carries an empty `BlueprintId`, which
`TransactionValidator` rejects earlier with `TX_003 "Blueprint ID is required"`. Fixing TX_003
without fixing this would *open* the hole.

**Decision.** Detect governance transactions on `Metadata["Type"] == "Control"` (the platform-wide
convention) plus the governance `BlueprintId`, and give `/propose` a non-empty `BlueprintId`. The
two changes must land together.

---

## R-005: `BlueprintId` for governance transactions is constrained on both sides **[LIVE]**

**Finding.** Empty fails `TX_003`. `"genesis"` is worse: `TransactionTypeClassifier.IsGenesisTransaction`
matches that exact value, which would classify a routine governance transaction as the network's
pre-signed genesis and judge it against the short `GenesisMaxAge` freshness window.

**Decision.** `register-governance-v1` — the governance control blueprint, and the same value
`RegisterPolicy.Governance.BlueprintVersion` already defaults to per register. Verified live: the
transaction sealed with `BlueprintId: 'register-governance-v1'`.

Note this value also toggles `IsGovernanceTransaction`, so using it opts governance transactions
*into* enforcement — which is correct and is what R-004 generalises.

---

## R-006: Register Service can sign with an organisation's wallet

**Finding.** `WalletEndpoints.SignTransaction` bypasses the wallet-ownership check for callers
holding a service token:

> *"Service tokens (`token_type=service`) bypass this check — they are trusted internal
> service-to-service calls (e.g., Blueprint Service signing actions). User tokens must own the
> wallet or have delegated access."*

So `IWalletServiceClient.SignTransactionAsync(orgWalletAddress, hash, derivationPath, isPreHashed)`
called with Register Service's service principal will sign with the organisation's key.

**Decision.** Use this for US1. The owner's wallet address is parsed from the roster attestation's
`Subject` (`did:sorcha:w:{walletAddress}`).

**⚠ Trust property that must be recorded, not glossed.** This means *any* service principal can
sign as *any* organisation. For US1 (an administrator of the owning organisation asks its own node
to make a change) that is consistent with every other server-custodied wallet operation on the
platform, and is not a regression. For US2 it is weaker than it looks: an approval produced by a
service principal on an organisation's behalf is not cryptographic evidence that *the organisation*
approved — only that some service asked for its key to be used. Consortium governance across
mutually-distrusting organisations ultimately wants each organisation to authorise from its own
session or its own node. Recorded as a limitation in the plan; closing it is out of scope here.

---

## R-007: The quorum machinery exists and must be reused, not rebuilt

**Finding.** Already present and correct:

| Capability | Where |
|---|---|
| Voting members, quorum threshold | `RegisterControlRecord.GetVotingMembers()` / `GetQuorumThreshold(excludeDid, formula)` |
| `StrictMajority` / `Supermajority` / **`Unanimous`** | `QuorumFormula` |
| Per-register rule | `RegisterPolicy.Governance.QuorumFormula` |
| Approval counting + owner override | `GovernanceRosterService.ValidateQuorumAsync(registerId, operation, approvals, ct)` |

The owner override grants quorum 1-of-1 when the proposer is the Owner, and **deliberately excludes
`Transfer`** — so ownership transfer always routes through counting. With a single owner the
threshold is 1 and the owner's own approval satisfies it, so **US4 still requires the approval
surface to exist**.

**Missing:** no endpoint anywhere produces approvals (`ValidateQuorumAsync` takes them as a
parameter); `GovernanceOperationType` has no crypto-policy member.

**Decision.** Build the approval surface and add the operation type. Do not reimplement quorum
arithmetic.

---

## R-008: Both system blueprints are declarative only

**Finding.** `register-governance-v1` and `register-creation-v1` are seeded to the system register,
but **nothing in `src/` instantiates either**. `register-governance-v1` is referenced only as a
version string, as the magic `BlueprintId` above, and in tests.

`register-governance-v1` already models the intended workflow (proposer/voter/target; Assert
Ownership → Propose → Collect Quorum ⟲ → Accept Role → Record Control Transaction, with
Transfer-skips-quorum and owner-override routes) but has drifted: it hardcodes
`approvalPercentage >= 50.01` (cannot express Supermajority or Unanimous), has no crypto-policy
operation, no `dataSchemas` (no payload contract for proposals or votes), and its "Accept Role"
step is meaningless for a policy change.

**Decision.** US3 makes it execute. The blueprint is revised to express the register's configured
rule rather than a hardcoded percentage, gains a crypto-policy operation and payload schemas, and
"Accept Role" becomes conditional on the operation type.

---

## R-009: Governance instances under Feature 145

**Finding.** F145 makes a workflow instance a deterministic projection of the sealed ledger:
`InstanceProjector` folds `docket:confirmed`, and routing is carried on the transaction as a
sender-signed `RoutingDecision` validated by `VAL_ROUTING_001/002`. Instance identity is
`InstanceIdentity.Derive(registerId, blueprintId, startingActionTxHash)`.

**Implication.** A governance proposal executed as a workflow becomes a ledger projection like any
other — which is what gives US3 its audit trail for free. But it also means **quorum cannot be
evaluated in application state**: the routing decision (proceed to enact vs keep collecting) is
carried on the transaction and re-validated by the validator, so the quorum evaluation must be
deterministic from sealed ledger content on every node.

**Decision.** Approvals are ledger transactions (action submissions), not rows in a table. Quorum is
evaluated from the sealed approval transactions plus the roster snapshot, so every node folds to the
same answer. This is the single most consequential design constraint in the feature and drives the
data model.

**Alternative considered and rejected.** Collect approvals in a service-side store and write only
the final enactment to the ledger. Simpler, but it makes approvals invisible to other nodes,
unauditable (contradicting US3/FR-019/FR-020), and non-deterministic under projection — the node
holding the store becomes authoritative, which is precisely the centralisation the platform exists
to avoid.

---

## R-010: Roster snapshot semantics (resolved by maintainer)

**Decision (spec Clarifications, 2026-08-06).** Evaluate a proposal against the roster and rule as
they stood **when it was raised**; any enacted roster change **invalidates** every open proposal on
that register, recorded as a discoverable outcome.

**Implementation consequence.** The proposal transaction must capture the roster snapshot it was
raised against — concretely, the identifier of the control transaction that established the roster
(`GovernanceRoster.LastControlTxId` is already reconstructed and available). Invalidation is then a
comparison at count time: if the register's current roster-establishing transaction differs from the
one the proposal captured, the proposal is invalid. No timers, no background sweeper, and
deterministic on every node — which R-009 requires.

---

## R-011: Clean break, and what it costs

**Decision (maintainer).** Attestation signing moves to slot 100 with no compatibility window.

**⚠ Correction (found during implementation, T010).** An earlier statement in this document and in
the plan — that `SorchaDerivationPaths.RegisterAttestation` was referenced nowhere in `src/` — was
**wrong**. The original grep omitted `.razor` files. The admin UI's `CreateRegisterWizard.razor`
*already* signed attestations at slot 100. The real defect was narrower and worse than "an unused
constant": **three of the four creation paths disagreed with the fourth**. CLI `RegisterCommands`,
`SandboxRegisterProvider` and the walkthrough module passed no derivation path and signed with the
wallet's primary key, so whether a register was governable depended on which tool created it —
with nothing to detect the difference, because both produce a valid signature and a well-formed
roster. This strengthens rather than weakens the decision: slot 100 was always the intended
design, implemented in one path and missed in three. It also means registers created through the
admin UI may already carry slot-100 rosters and be governable once the enforcement fixes land.

**Consequence.** A register's roster is immutable, so this applies only to registers created after
the change. Existing registers — the AIAS demo registers and the system register minted 2026-08-06 —
carry primary-key rosters and become permanently ungovernable. They must be recreated; the network
must be re-genesised for the system register (the CLI ceremony signs its attestations too).

**Testability finding that makes this cheap.** A *normal* register's roster is written at its own
genesis, so US1 and US2 are fully testable by creating a new register with the updated code — **no
network re-genesis required**. Only US4 (system register ownership) needs the ceremony change and a
re-genesis.

**Decision.** Sequence US4 last, and pair the re-genesis with re-provisioning the AIAS demo so the
demo is never left broken mid-flight.

---

## R-012: Evidence standard

**Finding.** Every defect above was invisible to a green suite: Register.Service 351 passed,
Register.Core 320, Validator 1035, Gateway 60 — while `POST /disable-dev-mode` returned
`200 {"status":"submitted"}` and did nothing. `RegisterManager.DisableDevModeAsync` had dedicated
passing tests and **no `src/` caller at all**. One live test then passed for the wrong reason (R-002).

**Decision.** Per SC-009, each user story's acceptance requires live execution on n1 plus the tiny
replica. The minimum evidence for a governance change is the **transaction id present in a sealed
docket's `TransactionIds`** (not merely present in the transactions collection) *and* the resulting
state observed on the second node. Mock-validator unit tests cannot establish either — the mock
accepts anything, which is exactly how the missing `BlueprintId` (R-005) reached a live run.

---

## R-013: The approval digest does not bind what it authorises

**Finding.** `GovernanceApprovalStatement` (v1, US1) binds a hand-picked field list: domain tag,
`registerId`, `OperationType`, `ProposerDid`, `TargetDid`, `TargetRole`, `ProposedAt`, `approverDid`,
approve/reject. `GovernanceOperation` carries more: `ValidatorEntry`, `RosterSnapshotId`,
`QuorumFormulaAtRaise`, `ExpiresAt`. None of those are bound.

The sharp case is `AddValidator`. An approval binds "add a validator" and **not which one** — the
validator's public key and endpoint sit outside the digest entirely.

**Why it was survivable until now.** Under server-side signing the server both builds and signs the
operation, so there is no separate party to mislead. The gap is close to inert.

**Why it stops being survivable.** R-014 makes signing external. The premise becomes that an outside
party reviews something and signs it — so any unbound field is a way to display one thing and enact
another, leaving a cryptographically valid signature on the ledger and no record anywhere of the
substitution.

**Decision.** Statement **v2** binds the canonical serialisation of the whole operation — domain tag
`sorcha:governance-approval:v2`, `registerId`, `proposalId`, `approverDid`, approve/reject, plus a
hash of the operation's canonical JSON excluding derived/mutable members (`ApprovalSignatures`,
`Status`). Clean break: v1 signatures MUST NOT verify under v2.

**Alternative considered and rejected.** Extend the v1 field list to cover the four missing fields.
It closes today's gap and reopens it the next time a property is added to `GovernanceOperation` —
silently, with no compiler error and no failing test. A hand-maintained field list is the same smell
as a hand-maintained type↔serialisation mapping, which this codebase has repeatedly been caught by.

**Guard.** A reflection-driven test enumerates `GovernanceOperation`'s properties and asserts that
mutating any non-excluded one changes the digest, so a property added later is covered automatically.
It must go RED today for the four fields above.

---

## R-014: Who holds the slot-100 key

**Finding.** `GovernanceApprovalService` signs server-side, and the drafted contract states it
outright. Any service-tier token can name any organisation (issue #1380), so under `Unanimous` — the
setting where protection should be strongest — one principal can satisfy an entire consortium.

**Decision.** Approvals for multi-party registers are produced by **detached signature over a
canonical digest**: the server publishes a signing request, something external signs it, the server
assembles the result onto the ledger. The human UI and an autonomous bot become two clients of one
protocol rather than two features — which is what makes "human or privileged bot, the platform does
not care which" satisfiable by a single mechanism.

**Carve-out.** Single-owner registers keep the existing Owner override, unattended (FR-031). Without
it this is a regression for every register that exists. #1380 therefore narrows rather than closes.

**Alternative considered and rejected.** Keep server-side signing but require an authenticated org
admin session. It closes the "any principal, any org" hole and is far smaller — but the server still
holds the key and still decides, and the ledger reads identically either way, so it improves
authorisation without improving evidence. Its good idea survives as R-015.

---

## R-015: Organisation authority, individual accountability

**Finding.** An org-key signature records "org X approved". It cannot record which human authorised
it, which is precisely what US3 exists to provide.

**Decision.** An approval may carry a **co-signature** from the authorising individual's own key
alongside the organisation's. The org key carries authority; the individual's key carries
accountability. Platform-tier tokens already carry `wallet_address` (`TokenService`, `Tier.Platform`
branch), so org admins already hold a key — no new provisioning. `Signatures` is already a `List` and
US1 already iterates every entry, so no new transport.

**Consequence that fails silently if missed.** The validator matches every signature against the
roster, and only organisations are on the roster. A co-signature must be treated as attestation
metadata, not a roster claim, or it is rejected as "not on roster".

**Superseded 2026-08-07 — there is no asymmetry.** The claim above was that a bot "has no individual
behind it". That is wrong: a machine external to the platform was **empowered by a human to act**, so
accountability is not absent, it is *delegated*. See R-017 — every approval links to a named
individual, directly or through a signed delegation.

---

## R-016: Web and mobile are the same client, not the same assurance

**Finding.** The signing client is device-agnostic — same envelope, same digest, same validation — so
it is built once. Assurance is not equivalent: `WebCryptoDeviceKeyService` yields non-extractable keys
either way, but a phone is typically secure-enclave backed with biometric unlock, whereas a desktop
browser profile is a file on disk reachable by anything running as that user. Reviewing in the console
and signing on the same machine also collapses the isolation the split exists to create.

**Decision.** Do not mandate a device. Record `authMethod` on the ledger so a register can set its own
bar (e.g. `Unanimous` requires hardware-backed), making it enforceable and auditable rather than
assumed, and leaving admins without phones unblocked.


---

## R-017: An autonomous approver is delegated, not unaccountable

**Correction.** R-015 first proposed that a co-signature be required for human approvals and absent
for autonomous ones, accepting that bot approvals would carry weaker evidence. The maintainer's
objection is decisive: *if a bot is external to the system it has to have been empowered by a human
to enact.* Accountability is therefore deferred, never missing, and the asymmetry was an artefact of
the model rather than a property of the problem.

**Finding.** The platform already models this. `RequireDelegatedAuthority`
(`AuthorizationPolicyExtensions`) requires `token_type=service` **and** a `delegated_user_id` claim —
described in source as "service token acting on behalf of a user". Structurally a bot is the same
thing as a citizen's paired device (F114): it holds its own key, acts for a principal, and derives
authority from a delegation that can be scoped and revoked.

**Why the existing claim is not sufficient on its own.** `delegated_user_id` is a JWT claim, and the
**server mints the token**. A delegation the server can assert is one it can forge — which defeats the
point of moving signing outside the server (R-014). The delegation must be signed by the empowering
individual's own key and carried as evidence.

**Decision.** Every approval carries an `authorisation` in one of two forms:

| Form | Who signs | Accountability |
|---|---|---|
| **Direct** | the individual's own key, alongside the organisation's | that individual |
| **Delegated** | the autonomous approver's key, plus a delegation record signed by the empowering individual | the individual named in the delegation |

The delegation names the approver's public key, the organisation, a **scope** (which
`GovernanceOperationType` values it may approve) and an **expiry**, and is revocable. Scope matters
in practice: a bot can be empowered to approve routine crypto-policy updates while `Transfer` still
requires a human.

Validity must be determinable from sealed ledger content so every node folds identically (R-009) —
so the delegation and its revocation are ledger records, not service state.

**Consequence.** `coSignature` is replaced by `authorisation`. FR-032's rule still holds in the
stronger form: an approval whose authorisation is invalid, out of scope, expired or revoked is
**refused outright**, never accepted with the authorisation quietly dropped.

---

## R-018: Individuals do not reliably have a key (found live on n1, 2026-08-07)

**R-015 claimed "no new provisioning".** It reasoned that a platform-tier token carries
`wallet_address` (`TokenService`, `Tier.Platform` branch calls `AddWalletAddressClaimAsync`), so org
admins already hold a key the Direct authorisation form can use. That is **wrong as a general claim**,
and live execution shows why.

**Evidence.** The seeded admin's token on n1 (`sub 00000000-0000-0001-0000-000000000001`, aud
`n1.sorcha.dev:platform`) carries claims `sub, email, jti, name, token_type, platform_user_id,
email_verified, org_id, org_name, role, nbf, exp, iat, iss, aud` — and **no `wallet_address`**.
`wallet."Wallets"` on n1 holds org signing wallets (`org-sorcha-local-signing`,
`org-public-signing`, `org-aias-signing`) and wallets for real end users, but **none for the seeded
admin**.

`AddWalletAddressClaimAsync` resolves a wallet and logs a warning on failure — it does not create
one. So the claim appears only when the individual already has a wallet, which an administrator may
never have: admins arrive through org provisioning, not the citizen wallet journey that mints one.

**Why it matters.** FR-029 requires every approval to resolve to a named individual, and both
authorisation forms need that individual to hold a key:

- **Direct** — the individual signs. No key, no approval.
- **Delegated** — the *empowering* individual signs the grant. No key, no delegation can be issued.

So an organisation whose admins have no wallets cannot govern at all under this design. That is a
harder failure than the one this feature set out to fix.

**Decision needed (not settled here).** Options, in rough order of preference:

1. **Provision a governance key for an admin on demand**, derived like any other, at the point they
   first approve or grant. Keeps one key per person and no new concept.
2. **Derive the individual's governance key from their platform identity** so it exists implicitly.
3. **Let the organisation nominate approver identities explicitly**, decoupling "who may approve"
   from "who happens to have a wallet".

(1) is the smallest change and preserves the property that matters — the key is the person's, not the
server's. Whichever is chosen, it must not reintroduce server-side signing (R-014), which rules out
"the server holds an admin key on their behalf".

**WITHDRAWN 2026-08-07 — this was my error.** The claim "no path provisions a wallet for a platform
user" came from searching only the Tenant Service. It is wrong. Users create wallets through
`POST /api/v1/wallets` — the first-login flow the UI drives (`Pages/Wallets/CreateWallet.razor` →
`IWalletApiService.CreateWalletAsync`). The seeded admin simply had never been through it, having been
created by `DatabaseInitializer` rather than by logging in.

Proven by doing it: creating a wallet for the admin that way made the 403 in R-021 disappear
immediately — they signed the owner attestation at slot 100 and the register was created. There was
never a missing capability, only an account that had skipped the flow. **T094 is closed: the answer is
the existing first-login wallet creation, whether reached through the UI or the API.**

The original observation still holds narrowly and is worth keeping: all three `CreateWalletAsync` call
sites in the Tenant Service create **organisation** wallets (`org-{subdomain}-signing`), and The user wallets present on n1 all belong to citizen
accounts minted by the application journey (`aias-rehearse-*@example.test`). So an administrator
having no key is the norm, not an anomaly of the seeded account.

**Status.** Blocks the live happy-path verification of T076 and everything downstream that needs a
real approval. The endpoint itself is deployed and correct (see below).

---

## R-019: T076 live verification, n1, 2026-08-07

**Deployed** `sorchadev/register-service:f189-t076` (local build of branch
`189-governance-approval-surface`, loaded via save/scp/load — never `compose pull` after loading).
Container healthy.

**Proven live:**

- The endpoint is reachable and correctly routed. A request for a non-existent proposal returns
  `HTTP 404` with `content-type: application/json` and the handler's own message. That specifically
  rules out F188's gateway trap, where a new `/api/` prefix with no YARP route falls through to the
  `ui-static` catch-all and returns a **bodiless 404** — the whole surface silently unreachable.
  `register-catchall` (`/api/registers/{**catch-all}`) covers this path.
- The handler runs, reaches the repository and reports accurately.

**NOT proven:** the happy path. No governance proposal exists on n1 (`/governance/proposals` returns
`total: 0` on the system register, the only register on the node after re-genesis), and raising one is
blocked by R-018. So the operation-reconstruction path — `Payloads[0].Data` →
`ControlTransactionPayload.Operation` → `GovernanceSigningRequest` — is **unexercised against real
data**, which is exactly the join this project's history says to distrust until executed.


---

## R-020: `/propose` still signs with the NODE's system wallet — US1 is incomplete

**Proven by code trace, 2026-08-07.** The `/governance/propose` handler takes
`ISystemWalletSigningService` and signs with:

```csharp
// 11. Sign with system wallet
var signResult = await signingService.SignAsync(
    registerId: registerId, txId: txId, payloadHash: payloadHashHex,
    derivationPath: SorchaDerivationPaths.RegisterControl,   // slot 101 — the NODE's key
    transactionType: "Control");
```

…and submits `Signatures = [systemSignature]`.

`SorchaDerivationPaths.RegisterControl` is slot 101, the node system wallet. A register's governance
roster is built from its genesis attestations, which record the **organisation's** key at slot 100.

**This is the original Feature 189 defect, unfixed on this path.** US1 moved `/disable-dev-mode` and
`/governance/crypto-policy` onto `IGovernanceSigningService` (slot 100) and left `/propose` behind. So
every roster-change proposal — `Add`, `Remove`, `Transfer`, and all validator operations — is still
node-signed and will be rejected by `RightsEnforcementService` as *"submitter not found in roster"* on
any register whose genesis has sealed.

**Consequences.**

- US1's checkpoint ("governance operations complete on a sealed register") holds only for
  crypto-policy, not for the roster changes the feature is named after.
- **Withdrawn inference (maintainer, 2026-08-07).** I offered "zero proposals on n1" as
  corroboration. It is not: the SSR is the *only* register on the node and is deliberately unique —
  its genesis is pre-signed offline and making it governable by this path is explicitly deferred
  (T007 → US4). Zero proposals there says nothing about the general path. **R-020 stands on the code
  trace alone, which is where its evidence actually is.**
- It compounds R-018 — even once individuals have keys, the proposal itself is signed by the wrong
  party.

**Fix.** Route `/propose` through `IGovernanceSigningService` exactly as the crypto-policy path was,
signing as the proposing organisation at slot 100.

**How to test it, and how NOT to.** The gate must run against an **ordinary register**, not the SSR.
The system register is unique by design — pre-signed offline genesis, deliberately outside this path
until US4 — so it can neither confirm nor refute the general behaviour. Create a normal register,
seal its genesis, then raise a roster-change proposal against it.

---

## R-021: T093 live gate is blocked by the access model, not by the fix

**Attempted on n1, 2026-08-07,** against an ordinary register (not the SSR — see R-020).

| Step | Result |
|---|---|
| `POST /api/registers/initiate` | **200.** Register `161de0d636a54109905abd9b8e6ab6ee`, one owner attestation to sign, 5-minute window. |
| `POST /api/v1/wallets/{orgWallet}/sign` with `derivationPath: sorcha:register-attestation` | **403.** |

The platform admin (`admin@sorcha.local`, `SystemAdmin` + `Administrator`, aud
`n1.sorcha.dev:platform`) **cannot exercise the organisation's signing wallet**. That wallet
(`org-sorcha-local-signing`) is owned by `00000000-0000-0000-0002-000000000006`, not by the admin.

**This is R-018's sibling, and together they are the real blocker.** An administrator:

- has **no personal wallet** — no provisioning path creates one (R-018, confirmed); and
- **cannot use the organisation's** wallet either (this finding).

So there is currently no identity through which a human administrator can sign anything: not a
governance approval, not a delegation grant, and not even the owner attestation that creates a
register. The feature's premise — that signing moves outside the server to a party that holds a key —
has no such party available today for an administrator.

**What this does NOT show.** It says nothing about whether T092's fix is correct; that path was never
reached. T092 remains code-verified only, and T093 remains genuinely outstanding.

**Why this matters more than a test blockage.** The demo and walkthrough scripts create registers, so
*something* can sign these attestations — but it is not a human administrator acting through the API
with their own credentials. Whatever that something is, it is the de facto answer to "who holds
governance authority", and it was never a deliberate decision. T094 should settle it explicitly
rather than inherit it.


---

## R-022: T093 PASSES — proven live on n1, 2026-08-07

**Fixture** (the point of R-020: the SSR cannot stand in). Ordinary register
`cbb1fa4c1bc942b7a1f86eabcfb96ea6`, DevMode, owner = the admin's own wallet
`ws11qq269…`, attestation signed at slot 100. Genesis `73fb4c4e…` confirmed **sealed into docket 0
before proposing** — the precondition whose absence produced this feature's earlier false PASS.

**Result.** `POST /governance/propose` (operation `Add`) → `200`, and:

```
docket 1 → TransactionIds: ['360bf52a115dd84fa1366c3838523c6d6e04b9673c04994dab04f4a845e6b152']
Register cbb1fa4c…: validated 1 transactions, rejected 0
```

The proposal **landed in a sealed docket** on a register whose genesis had already sealed — precisely
where R-020 predicted `"submitter not found in roster"`. T092's fix is now live-proven, not merely
code-verified.

**Owner override observed, not assumed.** The response reported
`quorum: { isQuorumMet: true, votesRequired: 1, votesReceived: 1, isOwnerOverride: true }`, so the
single-organisation degenerate path still completes unattended. That is T086's no-regression property,
observed in passing.

### Three API defects found by driving it by hand

**All three are filed as #1384.** The enum fix was attempted and **reverted**: annotating the request
properties made numeric input on the *nullable* `RegisterRole?` deserialise to `null`, so existing
callers would have silently lost the value on a 200 — a worse failure than the one being fixed. The
generic converter did not resolve it either. Reverted rather than shipped unproven; the tests that
caught it are not in the PR because they pin behaviour that does not exist yet.

1. **`/propose` requires numeric enums.** `"operationType": "Add"` returns `400` with a raw
   `System.Text.Json` / `BadHttpRequestException` stack trace in the response body. Any client
   hand-writing this JSON hits it, and the error leaks internals to the caller. The endpoint should
   accept the string names its own responses emit (it *returns* `"operationType":"add"`), or reject
   with a clean message.
2. **`/finalize` needs the whole `attestationData` object returned**, not the flattened
   `userId`/`walletId`/`role`. The flat shape yields `"Unknown attestation:  (Owner)"` — with an empty
   name, which reads like missing data rather than a wrong shape.
3. **Attestation windows are 5 minutes**, which is tight for any interactive flow where a human signs
   on a separate device. Worth revisiting before the PWA signing surface (T083) is built against it.

---

## R-023: Delegation policy (decided by the maintainer, 2026-08-08)

**Revocation is unilateral, not quorum-gated.** Revoking a delegation you granted is recorded as a
lighter ledger record signed by the granting individual — it does not go through the proposal path.
Requiring quorum to revoke would leave a compromised autonomous approver **live while votes are
collected**, which inverts the point of having revocation at all. Granting authority is the thing that
deserves ceremony; withdrawing it is not.

Validity stays ledger-derived so every node folds identically (R-009) — the validator's injected
`isRevoked` predicate reads sealed content.

**Only an Owner may grant a delegation.** A delegation is a standing key carrying the organisation's
governance authority. If any administrator can mint one, the weakest admin account sets the
organisation's governance blast radius. Owner-only is narrower than may eventually be wanted, and
narrow is reversible in a way that wide is not.

*Implementation note:* `Sorcha.Register.Models` is a zero-dependency leaf and knows nothing of
organisational roles, so this check cannot live with the structural validation. It belongs in the
service layer where the roster and org membership are resolvable — tracked as T095.

**`authMethod` is recorded, not enforced.** The field captures whether a key was hardware-backed so a
register *can* later require a minimum standard. Enforcing one now, before anyone has hardware-backed
governance keys provisioned, would lock organisations out of their own registers. Per-register
enforcement is a later policy decision, not a default.

**Interactive signing windows are 15 minutes; scripted flows keep 5.** The observed 5-minute
attestation window (R-022) is adequate for a script and not for a human signing on a separate device —
find the phone, unlock it, read the operation, approve. A window that expires mid-review trains people
to approve without reading, which defeats FR-027.

**#1380 stays narrowed, not closed.** External signing closes it for multi-party registers.
Single-owner registers keep the unattended Owner override by earlier decision, so the server still
signs there. Closing it fully means retiring that override, which is what makes single-owner registers
work headlessly — a separate decision deserving its own consideration.

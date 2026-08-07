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

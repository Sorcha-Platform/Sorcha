# What's Real vs Demo — Maturity and Known Limitations

Sorcha is pre-release software: MVD (Minimum Viable Demonstration) complete, hardening toward
production. This page is the plain-language companion for anyone about to test the platform — human
or agent — who needs to know what to trust, what to treat as illustrative, and what is a genuinely
open gap rather than an oversight.

It complements two other documents rather than repeating them:

- [`docs/reference/development-status.md`](development-status.md) — the feature-by-feature completion
  record (per-service percentages, what shipped when, live-validation notes).
- [`docs/security-model.md`](../security-model.md) — the cryptographic evidence model and its "Honest
  Gaps" section, which this page draws several limitations from directly.

## What's production-shaped

These are architectural properties that hold regardless of deployment polish — the cryptography and
the ledger model are not demo shortcuts:

- **Every action is signed, every disclosure is cryptographically bounded.** Selective disclosure is
  enforced by per-recipient key wrapping, not a policy check — the platform genuinely cannot read a
  field it wasn't given the key for. See `docs/security-model.md` § "Selective Disclosure —
  Architectural, Not Policy".
- **Post-quantum signing (ML-DSA) and key encapsulation (ML-KEM) are the internal default**, not a
  feature flag. The only classical fallback is at the HAIP wallet-ecosystem wire boundary, where the
  standard itself mandates classical suites.
- **The ledger model — Merkle-chained dockets, validator quorum consensus, peer replication — is the
  real mechanism**, not a mock. Governance changes to a register (adding/removing a validator,
  transferring ownership) are themselves ledger transactions, re-verified independently on every node
  rather than trusted from whichever node first admitted them (Feature 189).
- **Storage durability is fail-fast, not silent.** Production and Staging deployments refuse to start
  if a durability-critical interface (wallet repository, register repository, instance/action stores,
  the validator mempool queue) is wired to an in-memory fallback instead of a real backing store — see
  CLAUDE.md pattern #10 (Storage Registration Log).

## Known limitations a tester should know

These are not bugs waiting to be filed — they are open, named gaps. Treat each as "true today,"
not "will never be true":

### An organisation's governance approval proves key custody, not organisational consent (#1380)

A register's governance roster (who can add/remove validators, transfer ownership, change quorum
rules) is built from organisation-level signatures. But an organisation's governance key is a wallet
held in **the platform's own custody** — so any principal authorised to call the Wallet Service's
signing endpoint for that wallet can produce a valid-looking approval from that organisation. A sealed
approval on the ledger proves *the node was asked to use the organisation's key*; it does not prove the
organisation's people actually decided anything. This is worst under a `Unanimous` quorum policy,
where a single sufficiently-privileged principal can satisfy an entire consortium's vote.

Feature 189 narrowed this without closing it: approvals are produced outside the platform's automatic
signing path, every counted approval must name a responsible individual, and both signatures are
re-verified independently on every node. But the named individual's key is custodied the same way, so
the accountability record shows *who was named*, not *who consented*. Closing this requires the
organisation's governance key to live outside platform custody entirely — tracked as
[issue #1380](https://github.com/Sorcha-Platform/Sorcha/issues/1380) and detailed in
`docs/security-model.md` → "Honest Gaps".

**What this means for testing:** treat a register's governance trail as an audit record of key use,
not as non-repudiable proof that named organisations consented to a change.

### Rate limiting: relaxed by default, production posture is a live-deploy question

`CLAUDE.md` pattern #8 documents the model — every service goes through one centralised rate-limiting
extension (`builder.AddRateLimiting()`), driven by `RateLimitSettings` bound from configuration. The
**default values ship very relaxed** (around 100k requests/minute) because they're tuned for
pre-release development, not public exposure. Tightened limits for public-facing services (Tenant
auth, Wallet signing, the API Gateway) are layered in via environment-specific configuration.

**What this means for testing:** whether a specific public deployment (including n1.sorcha.dev) is
currently running the relaxed dev defaults or a tightened production profile depends on what has been
deployed to it at the time you're testing — it is a property of the *deployment*, not a property of
the codebase you're reading. Don't assume either direction; if you're probing rate-limit behaviour
specifically, verify what's actually configured on the node you're hitting rather than inferring it
from this document.

### Replication follows an explicit subscription, not advertisement

A register created on one node does **not** automatically appear on another node in the network, even
if both nodes are otherwise healthy and peered. A subscribing node's Peer service has to explicitly
request `full-replica` sync for that specific register (`POST /api/registers/{id}/subscribe`) before
any docket data crosses. Until that subscription exists, the register's absence on the second node is
expected behaviour, not a replication defect.

This was confirmed the hard way during live testing: a walkthrough that checked for a register on a
second node before subscribing it there reported a false replication failure — the register genuinely
hadn't replicated yet, because nothing had asked it to.

**What this means for testing:** if you create a register on one node and don't see it on another, check
whether a `full-replica` subscription was ever requested before concluding replication is broken.

### n1.sorcha.dev is a shared, wipe-able public sandbox

n1 is the platform's shared public demo node. It is periodically reset — genesis re-ceremony, full data
wipe — as part of normal operation, and it is shared with other testers. See
[`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md) for the full scope statement: no production secrets, no expectation
of data privacy or durability on n1.

### Pre-release schema policy: no upgrade path yet

Per CLAUDE.md §19, every service's EF Core migration set is currently squashed into a single
`InitialCreate` migration, amended in place as the schema evolves. This is deliberate while there are no
real installations to preserve — but it means there is currently **no supported in-place upgrade path**
between schema versions; the remedy today is to recreate the database (`docker compose down -v` +
re-genesis), which is fine for a demo node and would not be acceptable for a production installation.
The switch to additive, non-destructive migrations is a deliberate future cutover, tracked as
[issue #1365](https://github.com/Sorcha-Platform/Sorcha/issues/1365).

### Other named gaps (see `docs/security-model.md` → "Honest Gaps" for full detail)

- **mTLS is not yet enforced on inter-service hops.** Internal service-to-service calls are
  JWT-authenticated but not mutually-TLS-authenticated. A defence-in-depth gap, not a
  cryptographic-evidence gap — signatures on wallet actions and dockets remain independently verifiable
  regardless of transport.
- **SLH-DSA (FIPS 205)** and **BBS+ selective-disclosure signatures** are not yet implemented; both are
  named `planned` in [`STANDARDS.md`](../../STANDARDS.md), not silently absent.
- **DID method registration** — `did:sorcha:org:` and `did:sorcha:holder:` are implemented and used
  internally but are not registered with the W3C DID method registry, so cross-platform DID resolution
  outside Sorcha's own network requires bilateral agreement.

## How to read the completion numbers

`docs/reference/development-status.md` reports per-service completion percentages and a "100% MVD"
overall figure. Read "100% MVD" as *"every planned demonstration-scope feature is implemented and has
a passing test suite,"* not as *"production-ready."* Multiple live-execution passes across recent
features (Feature 145, 176, 183, 184, 188, 189 — see that document's "Recent Completions" section) found
real defects that a fully green unit-test suite had missed, because the defect lived at a seam between
two individually-correct components (a mapping, a serialisation shape, a timing assumption) that no
single test exercised end to end. The project's practice in response is to require **live re-execution**
as evidence for anything security- or correctness-critical, not just a green build — but that also means
"green tests" alone should not be read as "verified in production conditions."

## If you find something this page doesn't mention

That's useful information on its own — either the gap wasn't known, or it's known but not documented
here yet. See [`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md) for anything security-relevant, or open a regular
GitHub issue otherwise.

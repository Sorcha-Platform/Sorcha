# MCP P1 — making the surface completable

**Date:** 2026-09-06
**Status:** Draft for review
**Predecessor:** `docs/superpowers/specs/2026-09-05-mcp-agent-experience-design.md` (accepted; P0 landed as #1608)

## Why

P0 restored a surface that had been advertised and wholly non-functional for six days. It
did not make the surface *completable*: an agent still cannot create a register, publish a
blueprint onto one, or start a workflow instance, and `sorcha_action_submit` requires an
`instanceId` that nothing on the surface produces.

Two controlled cold-start experiments both scored **3/10** end to end. The second was given
one extra file — `blueprint.schema.json` — and immediately wrote correct selective-disclosure
JSON with real judgement. **The score did not move.** That cleanly separates a documentation
problem from a missing-capability problem, and identifies the lifecycle as the ceiling.

P1's goal: a cold-start agent reaches a running, auditable workflow.

## What changed since the predecessor doc

The predecessor listed P1 as "resources, prompts, four lifecycle tools" and put annotations
in P2. Three things moved.

1. **Lifecycle goes first**, because the A/B proved it is the binding constraint.
2. **Annotations move into P1 for the lifecycle tools only.** The human sign-off model below
   rests on them, so they stop being a P2 nicety.
3. **A human sign-off model was added.** It is not in the predecessor doc at all; it arose
   from the constraint that people must approve operationally-consequential acts.

## The human sign-off model

### What was rejected, and why it matters

The first shape considered was a register field classifying registers by whether their
participants are human. **This is wrong twice over and both errors are instructive.**

- **Human-vs-machine is not knowable and not ours to record.** It is a property of a
  participant, and recording it is a disclosure the platform has no business making. Sorcha
  manages disclosure; it does not manufacture it.
- **The relevant question was never about participants.** The attestation in register
  creation is signed by the register's *owner*. The question is who authorised the creation.

And that question cannot be answered server-side either. The MCP server forwards the caller's
bearer and deliberately holds no identity of its own. **An agent holding a user's token is
that user** to every service behind the gateway. Making the platform able to tell them apart
would mean giving the MCP server its own identity, which the architecture refuses.

So any register field recording "an agent did this" would be a value the agent supplies about
itself — exactly the defect CLAUDE.md pattern 23 exists to prevent (an exemption granted from
a claimed label rather than proved authority). It would be `Metadata["Type"] == "Genesis"` in
a new costume.

### Where the sign-off actually lives

**The MCP client, because that is where the human is.** Two native mechanisms, both present in
`ModelContextProtocol` 2.2.0, which the project already references:

- `ToolAnnotations.DestructiveHint` — declarative; the client decides whether to prompt.
- **Elicitation** (`ElicitAsync`) — imperative; the server interrupts its own tool call to
  demand a human answer and blocks until it gets one.

Elicitation is the real sign-off. It also removes a timing worry: elicit **before** calling
`/initiate`, so the human's thinking time does not run against the 5-minute pending-registration
TTL (`RegisterCreationOrchestrator.cs:41`).

### Three client states, not two — measured

A spike connected a fresh Claude Code process (`2.1.261`) to a throwaway probe server. It
declares:

```json
{ "roots": { "listChanged": true }, "elicitation": {} }
```

and `elicitation/create` round-trips end to end. But in a **non-interactive** (`-p`) session it
answered automatically:

```json
{ "action": "cancel" }
```

So a client may declare the capability and still have no human behind it. Capability
negotiation alone is not a sufficient check — a headless agent would sail through a gate that
only asked "can you elicit?"

**Rule: only `action: "accept"` is approval.** `decline` and `cancel` both refuse, and a client
that does not declare `elicitation` is refused before any backend call is made. Fail closed,
in every environment, with no bypass flag — consistent with pattern 23's treatment of
unresolvable authority.

This yields exactly the wanted behaviour: a headless agent cannot create a register; an
interactive one puts a real prompt in front of a real person.

## Component 1 — lifecycle tools

### `sorcha_register_create`

A **composite** tool. The ceremony is `initiate` then sign then `finalize`, and splitting it
across three MCP calls buys nothing once elicitation supplies the human moment, while costing
three inference gaps against a 5-minute TTL and giving the agent two more chances to name the
wrong wallet.

```
sorcha_register_create(name, description, devMode?, advertise?)
  1. refuse unless the client declared `elicitation`
  2. ElicitAsync -> proceed only on action == "accept"   (TTL clock not yet started)
  3. POST /api/registers/initiate
  4. for each attestationsToSign:
       POST /v1/wallets/{walletId}/sign
         derivationPath = "sorcha:register-attestation"   (fixed in code)
         isPreHashed    = true
  5. POST /api/registers/finalize
  -> { registerId, genesisTransactionId }
```

Two values the agent must never choose:

- **The derivation path.** Slot 100 is the organisation's governance key. The register's roster
  records whatever key signs the attestation, and the validator authorises later governance
  transactions by matching it. A wrong value does not throw — it derives a different, perfectly
  valid key, and the register is **silently ungovernable from creation**. The constant comes
  from `SorchaDerivationPaths` (CLAUDE.md pattern 15), never a literal.
- **The owning wallet.** Resolved from the caller's `org_id` claim via the Tenant Service, which
  carries `walletAddress` since #1525. An agent naming a wallet cannot forge a signature, but it
  can produce a working register whose roster records the wrong key — the same silent-wrong
  failure in a different place.

No signing primitive is added to the surface. `sorcha_wallet_sign` stays unregistered
(spec 139 T029); this tool signs one server-supplied attestation under a fixed path, which is
not an oracle.

**Provenance.** `InitiateRegisterCreationRequest.Metadata` carries `createdVia: "mcp"` plus the
tool version. This is an audit fact about the creation event, claims nothing about participants,
and **nothing in the platform may ever branch on it** — a test asserts no production read site
exists.

### `sorcha_blueprint_publish`

Calls the Feature 142 path, `POST /api/blueprints/{id}/publish`, via the existing
`IBlueprintServiceClient.PublishBlueprintAsync` (present and currently uncalled).

`PublishGate` blocks **any** publish lacking a matching rehearsal pass with
`409 REHEARSAL_REQUIRED`, so a newly-authored agent blueprint always hits it. The tool
therefore:

1. attempts the publish with **no** `override`;
2. on `REHEARSAL_REQUIRED`, elicits — naming the blueprint and stating plainly that it has not
   been rehearsed;
3. sends `override: { confirm: true, reason }` only on `accept`.

This is what the override was designed for: `ProceedWithOverride` is an *authorised, audited*
outcome attributed to `PlatformUserId`. The agent never silently bypasses the gate, and the
one behavioural check on the definition is only waived by a person who was told what they were
waiving. Rehearsal *tools* — which would let an agent clear the gate properly rather than
override it — are the highest-value P1 follow-up and are named in Deferred below.

### `sorcha_instance_create`

`POST /api/instances/` with `CreateInstanceRequest { BlueprintId, RegisterId, TenantId?, Metadata? }`.
No typed client method exists; one is added to `IBlueprintServiceClient` alongside the others.
This closes the loop: it produces the `instanceId` that `sorcha_action_submit` already requires.

Non-destructive (it creates a workflow, not governance), so it carries annotations but no
elicitation.

## Component 2 — annotations (lifecycle tools only)

| Tool | readOnly | destructive | idempotent |
|---|---|---|---|
| `sorcha_register_create` | false | **true** | false |
| `sorcha_blueprint_publish` | false | **true** | false |
| `sorcha_instance_create` | false | false | false |

The sweep across the other ~66 tools stays in P2. This slice exists because the sign-off model
depends on it, and it gives P2 a worked pattern.

## Component 3 — resources

There are currently **zero** resources in the codebase; `WithToolsFromAssembly()` is the only
registration. Tools are verbs; resources are the nouns.

| Resource | Source | Why |
|---|---|---|
| `sorcha://schema/blueprint` | `src/Common/blueprint.schema.json` | The single measured intervention that closed the authoring gap. |
| `sorcha://examples/{name}` | 3-4 shipped walkthrough blueprints | Worked, executable examples, not illustrative fragments. |
| `sorcha://glossary` | new | Register, blueprint, action, participant, disclosure group, docket, publication id vs execDefHash. |
| `sorcha://registers` and `sorcha://instances` | live, caller-scoped | State the agent can read without spending a tool call. |

Example blueprints are drawn from files the walkthrough suite actually executes, so a stale
example is a failing walkthrough rather than a silent lie.

## Component 4 — prompts

Also zero today. Three, matching the journeys the cold-start experiment attempted:

- *Set up a two-party data exchange with selective disclosure*
- *Issue a credential*
- *Prove a transaction to a regulator*

## Component 5 — `ServerInstructions`

Eight lines restating role names today. It becomes the map: the lifecycle order
(register, blueprint, publish, instance, action), the fact that some tools require a human
confirmation and why, and pointers to the resources above.

## Component 6 — folded-in defects from the P0 live verification

Both were found by executing tools against the CI image; no test or gate catches either.

### `sorcha_tenant_list` returns nothing while 27 tenants exist

It deserializes `{ Items, Page, PageSize, TotalPages }`. The Tenant Service returns
`OrganizationListResponse { Organizations, TotalCount }`. Live result: `"Retrieved 0 tenant(s)"`
with `totalCount: 27`. Its own description tells an agent to call it "to check whether an
organisation is already provisioned before creating a new one", so it answers *no* for every
organisation that exists. It also sends `page=` where the endpoint binds `pageNumber=`, so a
request for page 3 silently returns page 1.

**The route is mapped, so `check-mcp-routes.ps1` is green.** Nothing verifies response shape,
which is the same gap that let nine of the ten tools repointed in P0 also be wrong about their
response shape.

**Fix:** correct the DTO and the query parameter, and add a **response-shape gate**, sibling to
the route gate: for each tool that deserializes a service response, assert every property it
reads exists on the type the endpoint actually produces. Ratchet allowlist, seeded with whatever
is broken today and permitted only to shrink. This is the P1 analogue of
`Sorcha.Cli.ContractTests` (CLAUDE.md pattern 18), which exists for exactly this failure mode on
the CLI and has an empty baseline.

### A missing required argument is indistinguishable from a dead server

Calling `sorcha_user_list` without `organizationId` returns
`"An error occurred invoking 'sorcha_user_list'."` — byte-identical to what the six-day outage
produced. The real cause (`The arguments dictionary is missing a value for the required
parameter 'organizationId'`) never leaves the server.

This opacity is *why* the outage went unnoticed. **Fix:** surface argument-binding failures as
actionable text naming the missing parameter. An agent that cannot tell "you forgot a field"
from "the platform is broken" will report the wrong thing and give up.

## Testing

- **In-process:** every new tool invoked against a mocked backend, asserting request shape and
  response mapping field by field. The P0 lesson was that nine of ten repointed tools were
  wrong about response shape *after* the URL was right.
- **Elicitation:** all three client states — capability absent (refuse before any backend call),
  `accept` (proceed), `decline`/`cancel` (refuse). The `cancel` case is the one a
  capability-only check would miss.
- **Fixed-value assertions:** the derivation path is `SorchaDerivationPaths.RegisterAttestation`
  and the wallet comes from the `org_id` claim. Both are silent-failure values, so both pinned.
- **No-branch assertion:** no production code reads the `createdVia` provenance key.
- **Gate:** the response-shape gate runs in CI with a shrink-only allowlist.

## Validation

Re-run the cold-start experiment: a fresh agent, no Sorcha context, given only `llms.txt` and
the tool catalogue, asked to set up a two-party data exchange with selective disclosure and a
regulator-facing record. Baseline 3/10, hard stop at "no way to create a register or start an
instance".

**The elicitation design changes how this must be run.** A headless agent now receives `cancel`
and is correctly refused, so it cannot reach a running workflow unattended. The run is therefore
**interactive**, with a person answering the confirmations, which is the design working rather
than a workaround. The measurement is whether the agent reaches a running, auditable workflow
with only those confirmations supplied.

## Deferred

- **Rehearsal tools** (`StartRehearsalAsync` and friends already exist on the typed client).
  These would let an agent *clear* the publish gate rather than ask a human to waive it, and are
  the highest-value follow-up. Out of P1 because rehearsal is a multi-step interactive loop.
- **A longer-lived pending registration**, which an agent-initiates / human-signs-later flow
  would need. The current 5-minute TTL suits a machine and not a person; elicitation avoids
  needing it, so this is only for a future asynchronous handoff.
- P2 as previously scoped: schema enrichment across all tools, catalogue unification, OAuth
  protected-resource metadata.
- P3: trimming the ~14.8k-token `tools/list`.

## Scope boundaries

- Validation honesty (#1573 / #1605 / #1606) stays where it is, but note that P1 is the first
  work that lets an agent reach submission at all, so it becomes reachable.
- `sorcha_wallet_sign` remains unregistered.
- Org wallet creation stays human-gated by design (#1525).
- No `ServiceAuth__*` credentials for the MCP server, ever.

---

## Measured outcome (Task 10, step 5 — 2026-09-08)

Re-ran the cold-start experiment against the deployed P1 surface on n1. Same one-line prompt as the
baseline, a fresh Claude Code session in an empty directory holding only `llms.txt`, with
`sorcha-n1` connected over Streamable HTTP and a platform-tier token.

### The headline: the run did not reach a running instance, and the ceiling was NOT the agent

**Baseline (3/10)** hard-stopped at *"no way to create a register or start an instance"* — a missing
capability. P1 added that capability, and the agent used it. It then stopped anyway, on three
platform defects and one client gap, none of which existed as known blockers when Task 10 was written:

| # | Blocker | Nature |
|---|---|---|
| — | **Claude Code declares no MCP `elicitation.Form` capability** | Client gap. `IHumanApproval` returns `NotSupported`, surfaced as `ApprovalRequired`. **The entire P1 human-approval seam is unreachable from the flagship client.** |
| #1618 | Publish dropped any **chunked** request body, reporting the caller's own `registerId` as missing | Platform. Broke `sorcha_blueprint_publish` AND `sorcha blueprint publish`; the browser UI worked because browsers set `Content-Length`. Fixed, PR #1619. |
| #1620 | A register owned by the **org signing wallet** cannot be published to by anyone | Platform. The publish gate matches the caller's *linked user wallet*; no user token ever carries the org wallet. **The platform's own happy path — org wallet (#1525) → org register → publish — does not connect.** Open. |
| #1617 | `sorcha_org_wallet_status` reported *"All 0 organisation(s) have a signing wallet"* and *"Organisation not found"* for an org that exists | Platform. Open. |
| #1616 | `sorcha_register_stats` reported 0 registers while listing five in the same response | Platform. Open. |

**So the question Task 10 set out to answer — can an agent get from cold start to a running instance
— is still unanswered, because the path is broken.** Reporting a score against 3/10 would imply a
comparison the run could not make. What can be said precisely is below.

### What the agent achieved before the wall

- **Read the resources unprompted.** Used `dataPointers`, `calculations`, `isStartingAction`,
  `requiredPriorActions` — schema vocabulary absent from `llms.txt`. The P1 resources were consumed
  without being mentioned.
- **Expressed selective disclosure correctly**, which **baseline agent #1 could not do at all**.
  Three-tier disclosure over four payload groups, withholding `/commercial` and `/contact` from the
  regulator and disclosing a JSON-Logic–derived `thresholdExceeded` boolean instead — supervision
  against a threshold without ever sealing the amount. It described the mechanism correctly:
  *"scoped at seal time rather than filtered on read"*.
- **Created and independently verified** the blueprint on n1, by round-tripping the export rather
  than trusting the create response.
- **Stopped itself at `sorcha_register_create`** and explained the once-only BIP39 phrase, reading
  the constraint out of the tool description rather than discovering it by failing. The P1 approval
  model worked exactly as designed — right up to the client that could not answer it.
- **Verified `devMode: false` in both the register record and its cryptoPolicy** before letting
  anything go on-ledger, and caught that the UI had silently dropped the `advertise: false` choice.
- **Found #1617 unaided**, and correctly called the all-clear vacuous rather than reassuring.
- **Inferred node state** — "freshly re-genesised, bootstrap not fully completed" — from uniform
  timestamps and zero blueprints. n1 *was* re-genesised on 2026-08-29.

### Human interventions, and why each was needed

Four, none of them nudges about design:

1. Two architectural questions answered with the agent's own recommendation (domain-neutral; silent
   CC'd regulator) — deliberately choosing its option so the design stayed its judgement.
2. **Org wallet creation** — human-gated by design (#1525). Correct.
3. **Register creation via the UI** — forced by the elicitation client gap, not by design.
4. **A factual correction about #1618** — given because the agent had explicitly flagged its
   diagnosis as unproven and named the deciding evidence it lacked.

Intervention 4 is itself a finding. The agent asked for the publish status code; `log_query` and
`audit_query` both return `NotSupported`, so it could not get it. An operator with SSH could read it
in seconds. **A surface that points at evidence it cannot then provide is worse than one that points
nowhere** — the agent's own words, and it is right.

### Where the agent's reasoning failed, in its own analysis

Its postmortem is more useful than the score would have been:

- It proposed **granting itself a Designer role** on the governance roster. The gate matches wallet
  addresses, not roster roles, so the remedy did not follow from its own evidence.
- It stated a **mechanism it had not observed** as fact — *"the CLI holds the value and doesn't
  transmit it"* — when the observable was right and the cause was #1618.
- It **decomposed by transport rather than by cause**, calling the MCP and CLI failures "two
  independent blockers" when they shared one auth wall with a transport bug hiding it on one path.

And the miss underneath all three, which is the most valuable sentence produced by the run:

> *"Authorisation is a match between two sides. I investigated the register's side exhaustively —
> roster, attestations, derived roles — and never once investigated the caller's."*

Worth recording that **the reviewing session made the same error from the other direction**: it read
the roster and three hours of logs, saw no 403, and concluded there was no authorisation problem —
when the correct reading was *never reached*, because #1618 returned 400 first. The agent's
suspicion was right and was scored wrong. Two independent parties looked at one half of a match.

### Still open, recorded rather than dropped

- Whether `simulate` / `validate` require a **published** blueprint (hypothesised, never tested).
- Whether a **calculated field is addressable as a disclosure pointer** (`/thresholdExceeded`) —
  the most interesting question in the design, and still unanswered. It bears directly on #1609,
  the served schema being incomplete: the agent found the gap by hitting it.

### What this changes

1. **The elicitation gap is now the top P1 follow-up.** An approval seam only the UI can satisfy is
   not an approval seam for agents. Either Claude Code must declare `elicitation.Form`, or the
   lifecycle tools need a second, capability-independent confirmation path.
2. **#1620 blocks the happy path** and should be fixed before any further cold-start measurement;
   until then the experiment cannot reach an instance regardless of agent quality.
3. **A re-run is required** once #1618 (merged), #1620 and the elicitation gap are addressed. Only
   then does the 3/10 comparison become meaningful.

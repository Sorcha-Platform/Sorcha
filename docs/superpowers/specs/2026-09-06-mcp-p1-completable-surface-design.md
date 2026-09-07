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

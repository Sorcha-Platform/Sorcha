# Assured Identity Demo Environment — design

- **Status:** ✅ APPROVED — ready for implementation planning (`writing-plans`).
- **Date:** 2026-05-31
- **Supersedes the open questions in:** `docs/superpowers/specs/2026-05-30-assured-identity-demo-environment-design.md` (the parked note — now resolved; see its header).
- **Builds on (proven, live):** Feature 143 reverse-stream rendezvous (#879) + cross-installation hardening (#880/#881/#883/#884/#885, tests #886); F137 cross-node credential delivery; F142 Designer amend loop; F124/F126/F128 citizen onboarding surfaces; `sorcha-agent` rules/ai engines.

---

## 1. What this is (and isn't)

A **standing, node-agnostic demo environment** plus a **self-service provisioning toolkit**, layered on the already-proven F143 cross-installation Assured Identity loop. A tester goes — **unscripted, through the real product UIs** — from anonymous sign-up → submitted application → approval by an identity-validator agent → an Assured Identity credential in their Citizen Wallet PWA.

It is **not** a new product surface. The only things built are a thin **operability layer**: coherent provisioning, readiness gating, agent-mode selection, reset, status, and a node inventory. Everything the *tester* touches is the real product.

### Core concept: a demo is a mature walkthrough

- A **walkthrough** is a scripted, dev-facing validation of a flow (programmatic actors, hardcoded identities, `setup.ps1` + `run-*.ps1`). It proves a path works.
- A **demo** is a walkthrough that has *graduated*: coherent and complete in its own right, node-agnostic, operable by parameter, and exercised by a human through real UIs rather than by a script driving API calls.
- The Assured Identity flow has reached that bar. It therefore **moves from `walkthroughs/AssuredIdentity/` to a first-class demo**, and the legacy scripts are retired (§7). Henceforth "the Assured Identity walkthrough" refers to **this demo**.

---

## 2. Decisions (resolved open questions)

| Question | Decision |
|---|---|
| **Agent decision mode** | **Operator-selectable per run**, default `rules`. `New-IssuingAuthority -AgentMode rules\|ai\|human`. |
| **Tester journey** | **Real UIs, no scaffolding.** Web SPA `/new-submissions` to apply; real F128 onboarding + Citizen Wallet PWA to receive. No concierge, no automation of tester clicks. |
| **Provisioning / rebrand coherence** | **Parameterised command with sensible defaults**; `-AgencyName` is the single source written into org + register + published-participant + blueprint `x-review.header.issuerName`. Deep customisation via the real **F142 Designer amend loop**. |
| **Operational readiness** | **Provisioning gates on readiness** — the subscriber-side step polls subscription `Active` + blueprint recovery green before declaring ready, so the tester never hits transient `blueprint_not_available`. Plus `Get-DemoStatus`. |
| **Topology** *(already resolved 2026-05-31)* | Two+ separate installations; NAT'd issuer/owner dials a public rendezvous; trust boundary = the register (wallet sigs + roster), not JWT. |

---

## 3. Node-agnostic configuration & multi-node

Nothing hardcodes `tiny`/`n1`. The toolkit reads a small **node inventory** — one entry per installation:

```jsonc
// demo-nodes.json (gitignored; secrets stay in deploy/keys.env)
{
  "nodes": [
    { "id": "tiny", "role": "issuer",     "gateway": "http://tiny:8090",
      "installationName": "tiny.sorcha.dev", "rendezvousCapable": false },
    { "id": "n1",   "role": "subscriber", "gateway": "https://n1.sorcha.dev",
      "installationName": "n1.sorcha.dev",  "rendezvousCapable": true }
  ]
}
```

- **Swap / rename installations** by editing the inventory — `tiny`/`n1` are defaults, not assumptions. `-IssuerNode` / `-SubscriberNode` select inventory entries by `id`.
- **Additional independent nodes** can subscribe to and host/replicate the advertised register: `Connect-Subscriber -SubscriberNode <id>` is repeatable across N public nodes (F143 already models the subscriber as initiator; the rendezvous is a per-node capability, not a singleton). Each added subscriber gets its own readiness gate.
- The issuer need not be the NAT'd node in every deployment — `rendezvousCapable` + role drive whether a node dials out or accepts reverse streams. The proven default is NAT'd-issuer/public-subscriber, but the inventory does not bake that in.
- **Trust stays register-scoped.** Each installation keeps its own genesis + JWT signing key (`deploy/keys.env`); a new node joins by subscribing to the advertised register and verifying against its embedded validator roster (F086), never by sharing JWT keys.

---

## 4. Provisioning toolkit (the backbone)

Promote the gitignored `deploy/twoinstall-issuer.ps1` + `deploy/twoinstall-citizen-n1.ps1` into a coherent, parameterised, **idempotent** module under the demo (new home `demos/AssuredIdentity/` — §7). Four commands:

### `New-IssuingAuthority` — runs against the issuer node
- Sensible zero-arg defaults (a complete default-named authority stands up clean).
- Params: `-AgencyName` (single source of the agency identity), `-IssuerNode`, `-AgentMode rules|ai|human`.
- `-AgencyName` flows into: org name, register name, published-participant org name, and the blueprint's `x-review.header.issuerName` (template-injected at publish — §5). The credential-issuer DID stays valid across renames because it derives from the wallet address, which is unchanged.
- **Idempotent:** detects + reuses an existing org / wallet / advertised register / published blueprint. Handles the known reuse footgun — the walkthrough's "register already exists" check reads `OrganizationRegisterSubscriptions`; a stale row pointing at a Mongo-absent register must be reconciled (reuse or clean), not blindly reused.
- Provisions the analyst (Tier-3 Consumer) + wallet + published participant, exactly as the proven `twoinstall-issuer.ps1` does today, but parameterised.
- Wires the agent (§5).

### `Connect-Subscriber` — runs against a subscriber node
- Subscribes the subscriber's public org to the issuer's advertised register.
- **Gates on readiness:** polls until the subscription is `Active` **and** blueprint recovery is green (the ≤60s window) before returning "ready". Absorbs the transient `blueprint_not_available` so a tester arriving afterwards never sees it.
- Repeatable across multiple subscriber nodes (§3).

### `Reset-Demo`
- The documented clean reset: walkthrough Wallets (issuer + analyst + citizen), non-system Mongo register DBs, `OrganizationRegisterSubscriptions` rows, `state.json`. Per-node aware (reset issuer, a subscriber, or both).

### `Get-DemoStatus`
- Cross-node health: container health, subscription state, blueprint-recovery state, agent running/ idle, last sealed docket. One glance answers "is the demo ready for a tester right now?"

---

## 5. Agent mode + template injection

### Agent (operator-selectable, default `rules`)
`New-IssuingAuthority` generates the `sorcha-agent` analyst actor config and acts on `-AgentMode`:
- **`rules`** (default) — JSON Logic auto-approve `actor.json`; command launches `sorcha-agent run`. Deterministic; a live demo never stalls.
- **`ai`** — `"mode":"ai"` + persona file + `ANTHROPIC_API_KEY`. The showcase ("the assessor read your application and decided"). Same launch path.
- **`human`** — no agent launched; command prints "log into the issuer node as the analyst and approve Action 2" instructions.

Actor templates (rules + ai persona) ship with the demo; the analyst wallet/register/org IDs are substituted from provisioning `state.json` via the agent's existing `{{placeholder}}` resolution.

### Blueprint template injection
The blueprint template (migrated from `walkthroughs/AssuredIdentity/blueprints/assured-identity.json`) carries an `x-review.header.issuerName` token that `New-IssuingAuthority` replaces with `-AgencyName` at publish. The action-1 `sorcha-holder-key` field (F137) stays — it auto-populates read-only in the real form for cnf-bound cross-node delivery.

---

## 6. Tester journey (real UIs, no scaffolding) — documented, not built

1. **Sign up** on the subscriber web app (real auth, public org).
2. **Onboard a device:** real F128 cold-start surfaces nudge → `/setup/add-device` → enrol on the Citizen Wallet PWA. This gives the citizen a holder key + `CitizenHolderIndex` row so the credential can route back.
3. **Apply:** web app → **`/new-submissions`** → the agency's service appears (the node is subscribed) → **Start** → fill Action 1 (`sorcha-holder-key` auto-populated) → submit. `POST /api/instances` (policy `CanExecuteBlueprints` = any authenticated user) creates the instance; Action 1 is brokered cross-node to the issuer over the reverse stream (async, #885).
4. **Approve:** the agent (`rules`/`ai`) or a human approves Action 2 on the issuer node.
5. **Receive:** credential delivered cross-node → F124 pending-application waiting card → first-credential takeover in the PWA.

**Verified off-path (not used, not built):** the PWA `/applications` page is a "coming soon" placeholder (in-flight display only), and the `samples/strathcarron-portal` Blue Badge / Driving-Licence pages are non-functional stubs awaiting an unlanded backend PR. The web SPA `/new-submissions` is the working entry; the demo uses it and the spec records the other two as explicitly out of scope.

---

## 7. Graduation & cleanup (walkthrough → demo)

Once the demo is **tested working end-to-end** on the default node pair:

- **New home:** `demos/AssuredIdentity/` — provisioning module, `demo-nodes.json` example, agent actor templates, blueprint template, `DEMO.md` runbook.
- **Retire the legacy scripts:** remove `walkthroughs/AssuredIdentity/` (`setup.ps1`, `run-phase1-identity.ps1`, `run-phase2-licence.ps1`, `run-agents.ps1`, `run-crossnode-*.ps1`, `run-multi-peer.ps1`, scratch logs/state) and the `deploy/twoinstall-*.ps1` + `twoinstall-*state.json` scratch. The reusable `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` helpers stay (shared infra) — the demo module imports them.
- **Sequencing:** cleanup is a **distinct, final phase**, gated on a green demo run. Do not delete the legacy path before the demo replaces it.

### Skills + memory alignment (concept: "a demo is a mature walkthrough")
- **`walkthrough-builder` skill** — add the demo concept: when a walkthrough graduates (coherent, node-agnostic, human-operable via real UIs), it becomes a demo under `demos/`; scripted actor walkthroughs remain the dev-facing tool. Cross-reference the demo location.
- **`n1-deploy` / `network-bootstrap` skills** — point the Assured Identity references at `demos/AssuredIdentity/` and the node-inventory model.
- **`sorcha-architecture` skill** — under the F143 section, note the demo as the operable surface over the proven loop.
- **CLAUDE.md** — if a demos/ taxonomy line is warranted, add it (brief).
- **Memory** — update `f143-two-installation-demo.md` + `MEMORY.md` so "Assured Identity walkthrough" resolves to the demo, and record the node-agnostic + graduation concept.

---

## 8. Deliverables

1. Provisioning module — `New-IssuingAuthority`, `Connect-Subscriber`, `Reset-Demo`, `Get-DemoStatus` (idempotent, node-inventory-driven, readiness-gating).
2. `demo-nodes.json` example + inventory loader; `deploy/keys.env` stays the secret store.
3. `sorcha-agent` actor templates (rules + ai persona) with placeholder substitution.
4. Parameterised blueprint template with `x-review.header.issuerName` injection.
5. `DEMO.md` — operator runbook (provision/connect/reset/status, agent modes, multi-node) + tester runbook (the §6 journey).
6. Skill + memory updates (§7).
7. Cleanup phase: retire legacy walkthrough + scratch scripts after a green run.

---

## 9. Deliberately NOT building (YAGNI)

- No concierge / demo-specific UI; no automation of tester clicks.
- No product hardening of instance-creation (readiness gating in provisioning is sufficient).
- No completing the PWA `/applications` apply page or the `strathcarron-portal` stubs — deferred / off-path.
- No anchor-set gossip / multi-hop mesh (F143 deferred scope) — multi-node here is N subscribers against one advertised register, not a relayed mesh.

---

## 10. Open risks / to confirm during planning

- **Multi-subscriber** beyond the proven single n1 subscriber is designed-for but not yet live-verified across 3+ nodes — first multi-node run is a validation checkpoint, not an assumption.
- **`ai` agent mode** in a live demo needs a guardrail (timeout / fallback to rules) so a slow or refusing LLM doesn't strand a tester mid-flow — confirm the agent's failure behaviour during planning.
- **Idempotency edges** on re-provision (the `OrganizationRegisterSubscriptions` vs Mongo reconciliation) need explicit test coverage — the live-state notes show this footgun bit before.

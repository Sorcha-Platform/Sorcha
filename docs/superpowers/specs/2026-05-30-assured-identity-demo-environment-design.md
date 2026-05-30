# Assured Identity Demo Environment — PARKED design note

- **Status:** ⏸️ PARKED — brainstorm incomplete. Deliberately deferred.
- **Date:** 2026-05-30
- **BLOCKED ON:** Peer-service **reverse data plane** (a NAT'd owner node must be
  reachable by public subscribers). See the trigger condition below.
- **Resume by:** re-opening the brainstorming flow on this note once the peer
  spike's success criteria are met.

> This is **not** an approved design. It captures a brainstorm-in-progress so the
> thinking isn't lost while we build the foundation it depends on. Several
> sections are open questions, not decisions.

---

## Vision

Move the AssuredIdentity **walkthrough** (scripted, single-node) to a standing
**demo environment**: a multi-node identity-assurance setup where a tester goes,
unscripted through the real UIs, from **anonymous sign-up → submitted application
→ approval by an identity-validator agent → a fully assured-identity credential
in their wallet (PWA)**.

The desired shape (subject to the connectivity decision below):

- One node is the **issuing authority**: owns the assured-identity register,
  runs the validator, and runs an **identity-validator agent** that stands in for
  a human verification analyst.
- The other node is **subscribed** to that register and is where **testers
  (the operator) sign up and use the service**.
- A tester can take an **official SSR blueprint template, modify it** (e.g.
  change the issuing-agency name), and **redeploy** it as their own register —
  with provisioning identities kept correct.

## What already exists (reuse, don't rebuild)

- **`sorcha-agent`** (`src/Apps/Sorcha.Agent`) — autonomous actor CLI with two
  decision engines: **`rules`** (JSON Logic, deterministic) and **`ai`** (Claude
  persona via prompt file). Plus **persona mode** (initiator loop, Feature 110)
  and HAIP receive/present. This is the "identity-validator agent" — "give it a
  person or a ruleset" maps directly onto `ai` vs `rules`. The
  `verification-analyst` actor config already exists in `rules` mode.
- **AssuredIdentity walkthrough** (`walkthroughs/AssuredIdentity/`) — `setup.ps1`
  provisions orgs/wallets/blueprint/register; `run-phase1-identity.ps1` drives
  submit→approve→claim. The `assured-identity.json` blueprint (3 actions:
  submit → verify → claim, `SorchaLocalWallet` delivery, open-participant
  late-binding, `cnf`-bound cross-node delivery via `holderKeySourceField`).
- **F137 cross-node delivery** — credential bound to the citizen's holder key and
  encrypted to their wallet, delivered back across a node split (Tier-2,
  live-verified in the **subscriber-dials-public-owner** direction).
- **F142 amend loop** — `POST /api/blueprints/from-published`
  (`BlueprintFromPublishedEndpoint`) clones a published blueprint back to an
  editable draft, stamping `x-source-*` lineage. Foundation for
  "take SSR template → modify → redeploy".
- **SSR (System Register)** genesis ceremony + `n1-deploy` / `network-bootstrap`
  skills.

## Decided constraints (from the 2026-05-30 brainstorm)

1. **Connectivity rule (load-bearing):** the Sorcha peer protocol has the
   **subscriber initiate every cross-node connection** (submit fan-out, docket
   pull, live subscription — all subscriber→owner). Evidence:
   `TransactionDistributionService.cs:129`, `RegisterReplicationService.cs:185`
   & `:526`, `PeerConnectionPool.cs:105`. The owner's mempool is strictly local;
   **there is no path where the owner pulls an unsealed transaction from a
   subscriber.** Consequence: **the owner/issuer node must be inbound-reachable
   by its subscribers** — unless a reverse-reachability mechanism exists (below).
2. **`tiny` is NAT'd** (outbound-only: public `81.111.103.112`, LAN
   `192.168.51.11`; no Tailscale/WireGuard; has a Calico/k8s install but Sorcha
   runs on plain Docker — 20 cores / 62 GB / Docker 28 + Compose v2). **n1** is a
   public Azure VM (`51.105.7.135`). So a `tiny`-as-owner topology needs the
   reverse data plane; an `n1`-as-owner topology works today.
3. **The relay that would solve this is RETIRED.** `Sorcha.PeerRouter` +
   `RelayCommunicationService.EstablishReverseStreamAsync` (NAT'd peer holds an
   outbound duplex stream; router bridges inbound over it) still exists in code
   with unit/integration tests, but `docker-compose.yml:531-533` states the
   PeerRouter has been **retired** in favour of "peers self-introduce via
   RegisterPeer". Self-introduce solves **discovery**, not **reverse data-plane
   reachability** — no evidence found that `SubmitTransaction`/`PullDocketChain`
   get bridged back over a self-introduced peer's outbound connection without the
   router. Two-machine federation has never been proven E2E
   (`multi-peer-findings.md` = unexercised baseline).
4. **Operator decision (2026-05-30):** the demo waits for the environment; we
   build the **reverse data plane first**. The operator's preferred topology is
   `tiny` = issuer/agent, `n1` = subscriber/tester — which *requires* the reverse
   data plane.

## Open questions (resume here)

- **Topology (final):** `tiny`-issuer + `n1`-tester (needs reverse data plane —
  the chosen path) vs the proven inversion vs a Tailscale shortcut.
- **Agent decision mode:** `rules` vs `ai` persona vs human-in-the-loop — and is
  it operator-selectable per demo run?
- **Template-modify-then-redeploy UX:** Designer amend loop (`from-published`) vs
  a parameterised template + CLI re-publish. Where does "change the issuing-agency
  name" get edited, and how do the *provisioning identities* (issuer org +
  wallet + register roster + credential issuer DID + `x-review.header.issuerName`)
  stay coherent after a rename?
- **Provisioning self-service:** one idempotent command to stand up a fresh
  issuing authority with a chosen agency name (orgs, wallets, register genesis,
  agent config, seed wiring) — and a clean reset.
- **Tester journey:** fully manual through web UI + PWA vs minimal scaffolding.
- **`tiny` prep:** docker reset + fresh CI image pull (operator-noted the 9-day
  stale `sorcha-citizen-wallet`/`sorcha-citizen-verifier` containers) — folds into
  provisioning.

## Trigger to un-park

Resume this brainstorm when the peer reverse-data-plane work proves:

> **A NAT'd owner node (tiny) can receive forwarded action submissions and serve
> docket sync to a public subscriber (n1), proven end-to-end across the real
> tiny↔n1 network.**

## Related

- Peer reverse-data-plane spec: _(to be created — the next active project)_
- `walkthroughs/AssuredIdentity/`, `src/Apps/Sorcha.Agent/`,
  `src/Apps/Sorcha.PeerRouter/`, `specs/137-cross-node-submission/`,
  `specs/108-register-local-relationship/`.

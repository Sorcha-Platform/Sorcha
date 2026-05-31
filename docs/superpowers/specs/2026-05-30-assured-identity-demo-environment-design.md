# Assured Identity Demo Environment — design note (READY TO RESUME)

- **Status:** 🟢 READY TO RESUME — the blocking trigger is **MET**. The reverse data
  plane works and the full loop is proven E2E. (Was: ⏸️ PARKED, 2026-05-30.)
- **Date:** 2026-05-30 (parked) · 2026-05-31 (unblocked)
- **WAS BLOCKED ON:** Peer-service **reverse data plane** (a NAT'd owner node must be
  reachable by public subscribers) — **now delivered as Feature 143** (merged #879;
  cross-installation hardening + fixes #880/#881/#883/#884/#885, regression tests #886).
- **Resume by:** re-opening the brainstorming flow on this note (the foundation it
  waited on is done). Most of "Decided constraints" is now proven; "Open questions"
  is the live agenda.

> **Foundation proven (2026-05-31).** Verified E2E across the real `tiny`↔`n1` network,
> clean from a fresh dual-node reset:
> - A NAT'd **owner** (`tiny`) receives forwarded action submissions over its held
>   reverse stream and serves docket sync back to a public **subscriber** (`n1`) — the
>   exact trigger condition below.
> - The full Assured Identity loop runs cross-installation: anonymous citizen signs up
>   on `n1` → submits → brokered to `tiny` → `tiny`'s validator seals → docket syncs back
>   to `n1` → analyst on `tiny` approves → `AssuredIdentityCredential` (cnf-bound to the
>   citizen's holder key) is delivered to the citizen's wallet on `n1`.
> - **Topology DECIDED + proven:** two separate installations (separate JWT signing keys;
>   `n1` holds the sorcha system-register genesis, `tiny` its own); trust boundary is the
>   **register** (wallet signatures + roster), not JWT. `tiny` = NAT'd issuer (dials
>   `n1:50051` over Caddy TLS); `n1` = public tester surface + rendezvous.
> - References: F143 design `docs/superpowers/specs/2026-05-30-peer-nat-traversal-design.md`;
>   mechanism write-up `docs/reference/two-installation-cross-subscription.md`; live recipe +
>   state in auto-memory `f143-two-installation-demo.md`; local orchestration scripts (gitignored)
>   `deploy/twoinstall-issuer.ps1` + `deploy/twoinstall-citizen-n1.ps1`.
>
> This is still **not** an approved design — the remaining "Open questions" below
> (agent mode, template-modify-redeploy UX, self-service provisioning, tester journey)
> are the agenda for the resumed brainstorm.

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

- ~~**Topology (final):** `tiny`-issuer + `n1`-tester (needs reverse data plane —
  the chosen path) vs the proven inversion vs a Tailscale shortcut.~~ **RESOLVED
  (2026-05-31):** `tiny` = NAT'd issuer/owner + `n1` = public tester/subscriber,
  two separate installations, reverse data plane via Feature 143 — proven E2E.
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

## Trigger to un-park — ✅ MET (2026-05-31)

> **A NAT'd owner node (tiny) can receive forwarded action submissions and serve
> docket sync to a public subscriber (n1), proven end-to-end across the real
> tiny↔n1 network.**

**Status: satisfied.** Delivered by Feature 143 (reverse-stream rendezvous) plus the
cross-installation hardening fixes #880/#881/#883/#884/#885 (regression tests #886). The
trigger scenario — and the full anonymous-signup→credential loop on top of it — was run
clean end-to-end from a fresh dual-node reset. See the "Foundation proven" note at the top.
This brainstorm is unblocked; resume it against the "Open questions" section.

## Related

- Peer reverse-data-plane spec: _(to be created — the next active project)_
- `walkthroughs/AssuredIdentity/`, `src/Apps/Sorcha.Agent/`,
  `src/Apps/Sorcha.PeerRouter/`, `specs/137-cross-node-submission/`,
  `specs/108-register-local-relationship/`.

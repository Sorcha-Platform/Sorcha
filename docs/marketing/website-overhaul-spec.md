# SPEC: Sorcha website content & design overhaul

> This is the content + design **specification** produced from
> `docs/marketing/website-overhaul-brief.md`. It is written for two readers:
> an **engineering agent** (who implements the static landing in HTML/CSS and themes the
> Blazor/MudBlazor app from it) and **cowork / a human marketer** (who fills the
> research gaps tagged `[COWORK-RESEARCH]` and confirms the stats tagged `[VERIFY]`).
>
> **Every capability claim in this document was verified against the codebase on
> 2026-06-08.** The verification ledger is Section 0. Nothing in the copy claims a
> capability the code does not have. Banned adjectives (Section 5 of the brief) are not
> used. Unconfirmed numbers carry `[VERIFY]`; anything needing market/competitor research
> carries `[COWORK-RESEARCH]`.
>
> **Update 2026-06-08:** research R1–R5 is complete and folded in (see the three
> `website-overhaul-R*` docs in the Project Sorcha workspace). Ledger #20/#21/#25/#26 are
> resolved; the brand decision (R5) **reverses** the D4 ink-navy/teal proposal — the indigo/violet
> identity stays.

---

## 0. Verification ledger (claims vs. codebase)

Status of every factual claim the copy leans on. ✅ verified true in code · ⚠️ partial /
needs care · ❌ false or unsupported · 🔍 needs research before publishing.

| # | Claim | Status | Evidence / note |
|---|-------|--------|-----------------|
| 1 | 8 single-responsibility services: Blueprint, Wallet, Register, Validator, Peer, Tenant, API Gateway, HAIP | ✅ | All 8 exist under `src/Services/`. NB `strategic-context.md` says "seven services" then lists eight — **use "eight" or name them**, don't cite "seven". |
| 2 | .NET 10 / C# 14 / .NET Aspire | ✅ | `Directory.Build.props`, `net10.0` target across projects. |
| 3 | MIT licensed, open source, self-hostable via docker-compose | ✅ | `LICENSE` (MIT), `docker-compose.yml` (full topology). |
| 4 | "Over 10,000 tests" | ✅ | Actual **11,160 `[Fact]`/`[Theory]` methods across 48 test projects.** "Over 10,000" is conservative and safe. Citing the exact 11,160 is also defensible but will drift — prefer "more than 10,000". |
| 5 | ML-DSA (FIPS 204) post-quantum signatures, core not branch | ✅ | `PqcSignatureProvider.cs` (ML-DSA-65), wired into `CryptoModule.SignAsync` / `HybridSignAsync`. Live signing path. |
| 6 | ML-KEM (FIPS 203) key encapsulation | ✅ | `PqcEncapsulationProvider.cs` (ML-KEM-768), hybrid with XChaCha20-Poly1305 in `CryptoModule.EncryptAsync`. |
| 7 | BIP32/39/44 HD wallets | ✅ | NBitcoin 10.0.4; `SorchaDerivationPaths.cs`, `Bip39WordList.cs`. |
| 8 | ED25519, P-256, RSA-4096 supported | ✅ | `CryptoModule.cs`. |
| 9 | Merkle dockets, SHA-256 previous-hash linkage, append-only register | ✅ | `DocketBuilder.cs`, `MerkleTree.cs`, `DocketHasher.cs`. |
| 10 | OpenID4VCI (issuance) + OpenID4VP (presentation) | ✅ | `Sorcha.Haip.Service` — `CredentialEndpoints.cs`, `VerifierEndpoints.cs`. |
| 11 | HAIP boundary bridges classical signatures via a derived classical co-key | ✅ | `IHaipIssuerCoKeyService` / `HaipIssuerCoKeyService` (ES256 co-key under `sorcha:haip-issuer-signing`). |
| 12 | W3C VC 2.0 / SD-JWT VC profile | ✅ | `SdJwtVcFormatHandler.cs`, `SdJwtService.cs` (RFC 9901). |
| 13 | `did:sorcha` DID method | ✅ | `SorchaDidResolver.cs`, three forms (wallet / org / register-tx). |
| 14 | Designer lifecycle Describe → Understand → Rehearse → Go-live at `/designer/blueprint` | ✅ | `DesignerBlueprint.razor` (`LifecycleStage` enum + four stage components). Go-live gated by server-side `RehearsalPass`. |
| 15 | Sorcha Wallet is an installable PWA at origin-root `/wallet/` | ✅ | `Sorcha.Wallet.Pwa`, `manifest.webmanifest`, `service-worker.js`, `<base href="/wallet/" />`. |
| 16 | Marketing landing is static HTML/CSS + vanilla JS with a hero `<canvas>` | ✅ | `wwwroot/{index.html,landing.css,landing.js,consent-banner.js}`, `#heroCanvas`. |
| 17 | App is Blazor + MudBlazor; theme in `SorchaMudTheme.cs` | ✅ | `AddMudServices`, `SorchaMudTheme.cs`. |
| 18 | GA with Consent Mode v2, default-denied cookie banner | ✅ | `index.html` gtag + `consent-banner.js`. |
| 19 | Banned-adjective list CI-enforced | ✅ | `scripts/check-discoverability.sh` — exact 9 adjectives match the brief. |
| 20 | Post-quantum signatures: ML-DSA primary/default; SLH-DSA also present | ✅ **RESOLVED (decision 2026-06-08)** | Code implements ML-DSA-65 (default signing path) **and** SLH-DSA-128s/192s in `PqcSignatureProvider.cs`. **Decision: use it.** Public line: *"ML-DSA (FIPS 204) is the core, default post-quantum signature; additional FIPS-205 (SLH-DSA) primitives are present in the cryptography library."* Never publish "SLH-DSA not implemented." **Never frame PQC as unique** — per R1, Procivis One ships ML-DSA-65 and IOTA Identity ships ML-DSA/SLH-DSA/FALCON (beta). TODO: reconcile `strategic-context.md`. |
| 21 | JSON-Pointer selective disclosure **with per-recipient key wrapping** | ✅ **VERIFIED (2026-06-08, against live repo)** | Confirmed in `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`: each disclosure group's payload is encrypted with XChaCha20-Poly1305, then the symmetric key is **wrapped per recipient** against that recipient's public key (ED25519 sealed box / P-256 / RSA-4096 / **ML-KEM-768**) — `WrappedKey` per `group.Recipients`, tested N-recipients→N-keys. The operator never holds recipients' private keys → cannot read. **The stronger phrase "per-recipient key wrapping" is now approved.** (Mechanism lives in TransactionHandler/Encryption, not the register/DAD layer the spec originally guessed.) |
| 22 | BBS+ zero-knowledge proofs not implemented; disclosure is show/hide | ✅ | No BBS+ in code. Honest gap — keep it stated honestly per brief. |
| 23 | "Sits above EUDI Wallet / GOV.UK Wallet" | ⚠️ | **No EUDI/GOV.UK-specific integration code.** Sorcha implements the *shared standards* (HAIP / OpenID4VCI / OpenID4VP) those wallets are converging on. Phrase as standards-alignment, **never** as a shipped integration. Approved phrasing in §D1. |
| 24 | Production readiness ~30% / "100% MVD complete" | ✅ | `CLAUDE.md:7`, `docs/reference/development-status.md`. Maturity copy in §D1 reflects this. |
| 25 | Fraud / analyst stats | ⚠️ **SOURCED (R2, 2026-06-08)** | **CUT "$50bn identity fraud"** — no primary source; real Javelin figure is $27.3bn (US-scope, not global). **Reword:** "+58% deepfake" → *deepfaked selfies +58% in 2025 (Entrust)*; "30% distrust face biometrics" → Gartner *"consider IDV unreliable in isolation due to deepfakes by 2026"* (1 Feb 2024). **Publish as-is:** Gartner *"≥80% of governments will deploy AI agents to automate routine decision-making by 2028"* (17 Mar 2026). Source table: `website-overhaul-R2-R3-R4-research.md`. |
| 26 | Regulatory deadlines | ✅ **SOURCED (R2, 2026-06-08)** | HAIP 1.0 finalised **Dec 2025** ✅ (OpenID Foundation, 29 Dec 2025). Battery Passport **18 Feb 2027** ✅ — but it's the **EU Battery Regulation 2023/1542 (Art. 77)**, *not* ESPR; fix the attribution. Iron & steel **2026 = delegated-act adoption, not a passport go-live** (ESPR Working Plan 2025–2030) — reword. EU AI Act high-risk record-keeping = Reg. (EU) 2024/1689 **Arts. 10 & 12**. |

**Net (updated 2026-06-08):** #20 and #21 are resolved — SLH-DSA is a usable (non-unique) claim,
and per-recipient key wrapping is **verified**, so the stronger disclosure phrasing is approved.
Stats are sourced (#25/#26): cut the $50bn figure, reword two, fix the Battery-Passport
attribution. The remaining care item is **#23 (standards-alignment, not a wallet integration)**.
Research R1–R5 outputs live in `website-overhaul-R1-competitor-research.md`,
`website-overhaul-R2-R3-R4-research.md`, and `website-overhaul-R5-design-prompt.md`.

---

## RESEARCH HANDOFF — items for cowork (consolidated)

> **STATUS (2026-06-08): R1–R5 COMPLETE.** R1 (competitors) → `website-overhaul-R1-competitor-research.md`.
> R2/R3/R4 (stats, sectors, SEO) → `website-overhaul-R2-R3-R4-research.md`. R5 (brand) decided —
> keep indigo identity, refine via `website-overhaul-R5-design-prompt.md`. R6 decided — start with
> Assured Identity only. The descriptions below are retained for traceability.

Pull these out and run them; the copy below has placeholders wired for the answers.

- **`[COWORK-RESEARCH] R1 — Competitor landscape.** Position Sorcha against the verifiable-
  credential / decentralised-trust field. Candidate set to assess: **walt.id, Dock/Truvera,
  Procivis (One), MATTR, Microsoft Entra Verified ID, Hyperledger AnonCreds/Indy, cheqd,
  Spruce/SpruceID, Trinsic, IOTA Identity.** For each: what they do, licence/openness,
  PQC posture, whether they do *workflow orchestration* (Sorcha's differentiator) or only
  credential issuance/verification, and target sector. Output: a 1-paragraph "vs." line per
  competitor + a positioning 2×2 (proof-vs-assertion × workflow-vs-credential-only). Feeds
  D1 messaging and a possible `/compare` page.
- **`[COWORK-RESEARCH] R2 — Market & regulatory sourcing.** Find primary, citable sources for
  every stat in ledger #25/#26 (fraud losses, deepfake growth, Gartner/analyst predictions,
  HAIP 1.0 date, ESPR/DPP deadlines incl. Battery Passport, Iron & Steel, EU AI Act
  high-risk provenance obligations). Output: a source table (claim → source → URL → date).
  Anything without a credible source gets cut from the live copy.
- **`[COWORK-RESEARCH] R3 — Sector priority & buyer language.** Rank the four target sectors
  (Digital Product Passports/manufacturing · government identity/EUDI-GOV.UK · AI-Act audit ·
  SME trade finance) by go-to-market priority, and supply the procurement-grade vocabulary
  each buyer uses (e.g. council procurement, manufacturer compliance lead, trade-finance
  lender). Feeds D2 sector page + D3 sector copy.
- **`[COWORK-RESEARCH] R4 — SEO keyword research.** Target keywords/search volume for:
  "digital product passport software", "verifiable credentials platform", "EUDI wallet
  issuer", "post-quantum signatures", "self-hosted verifiable credentials". Feeds D6.
- **`[COWORK-RESEARCH] R5 — Brand identity decision.** There is currently **no wordmark asset**
  (logo is icon-only SVG: `sorcha-icon.svg`). Decide: keep icon + CSS wordmark, or commission
  a wordmark. Confirm primary brand colour direction (see D4 — I propose moving off the
  generic indigo/purple gradient). This is a human/stakeholder decision, not research.
- **`[COWORK-RESEARCH] R6 — Proof points / social proof.** Any pilots, design partners,
  named reference workflows (Trade Finance, Self-Build House, Forestry, Assured Identity
  walkthroughs exist in-repo as *demos*, not customers) that can be shown as
  "example workflows" without implying production customers. Confirm what may be named.

---

## D1. Positioning & messaging architecture

### Positioning statement (one sentence)
**Sorcha is cryptographic proof infrastructure for multi-party workflows — it produces
evidence every party can verify independently, without trusting the platform operator.**

### Elevator description (30 words)
> Sorcha replaces asserted trust with cryptographic proof. Every action is signed by the
> participant who took it, every record is immutable and Merkle-chained, every disclosure is
> bounded by cryptography — not policy.

### About (100 words)
> Digital systems run on assertion: a document *says* it is genuine, a platform *claims* its
> data came from a trusted source. As AI makes high-quality forgery cheap, assertion-based
> trust breaks. Sorcha replaces it with proof. It is open-source infrastructure (.NET, MIT)
> for workflows that cross organisational boundaries: each participant holds their own keys
> and signs every action; records are written to append-only, Merkle-chained registers;
> disclosure is bounded by schema so the platform cannot read data it was not given access
> to. Post-quantum signatures protect records built to last decades. Verify the evidence
> yourself — don't take the operator's word for it.

### Value proposition per audience

| Audience | One-line value proposition |
|----------|---------------------------|
| **Citizens / credential holders** | Your credentials live on your device, in your control — present exactly what's asked for, nothing more, and let anyone check they're genuine. |
| **Organisations / operators** | Design a multi-party workflow, issue verifiable credentials, and run a tamper-evident register — with an audit trail regulators and counterparties can verify without trusting you. |
| **Developers / evaluators** | Open-source, standards-based proof infrastructure on .NET. Self-host it, read every line, build on published standards — no proprietary lock-in. |

### Messaging hierarchy — the core claims the whole site ladders up to

1. **Proof, not assertion.** Trust comes from evidence you verify yourself, not from a
   platform you have to believe. *(Spine claim — every page ladders here.)*
2. **Credentials *and* the workflow.** Sorcha doesn't just issue and verify credentials — it
   orchestrates the multi-party workflow around them. *(Lead differentiator — no competitor
   assessed in R1 does both; promote this alongside the spine.)*
3. **The operator can't cheat.** Disclosure is enforced by per-recipient encryption, records
   are immutable and Merkle-chained — these are architectural properties, not promises.
4. **Built on published standards.** OpenID4VCI/VP, SD-JWT VC, W3C VC 2.0, BIP-32/39/44,
   FIPS-204/203. Nothing proprietary; everything inspectable.
5. **Made for regulated, multi-party work.** Digital Product Passports, government-aligned
   identity wallets, AI-decision audit trails, trade finance — domains where "trust me"
   isn't good enough.
6. **Open and self-hostable.** MIT-licensed, .NET, run it yourself. Ready to evaluate and
   pilot. *(Honest maturity — not "production-proven".)*

### Approved phrasing for the sensitive claims
- **Government wallets (ledger #23):** *"Sorcha implements the same standards the EU Digital
  Identity Wallet and GOV.UK Wallet are converging on — OpenID4VCI, OpenID4VP and the High
  Assurance Interoperability Profile — and sits as the workflow layer above them."* Never:
  "integrates with EUDI Wallet."
- **Post-quantum (ledger #20):** *"ML-DSA (FIPS 204) post-quantum signatures are a core part
  of the platform, not a side feature."* Optionally: *"with additional FIPS-205 (SLH-DSA)
  primitives in the cryptography library."* Never: "SLH-DSA not implemented." **Never claim PQC
  is unique / first / only** — Procivis One ships ML-DSA-65 and IOTA Identity ships
  ML-DSA/SLH-DSA/FALCON (beta). The defensible edge is *core, default PQC combined with workflow
  orchestration* (R1).
- **Disclosure (ledger #21 — VERIFIED):** the stronger claim is approved: *"Disclosure is
  enforced by per-recipient encryption — each party's data is sealed to a key only they hold, so
  the platform operator cannot read fields it was not given access to."* Post-quantum option: the
  symmetric key can be wrapped with ML-KEM-768.
- **Differentiator & competitors (R1):** lead with *workflow orchestration* — "issues and
  verifies credentials **and** orchestrates the multi-party workflow around them." Avoid
  "only open" / "only no-token" superlatives (SpruceID, cheqd, IOTA, Hyperledger are also fully
  open; SpruceID is also token-free) — say "fully open, no enterprise paywall." Don't imply
  best-in-class privacy: several competitors ship BBS+/ZK selective disclosure, stronger than
  Sorcha's show/hide today.
- **Maturity (ledger #24):** *"Open source, standards-based, and ready to evaluate and pilot."*
  Never: "enterprise-grade" / "production-proven".

---

## D2. Information architecture / sitemap

### Landing page — section order (revised from current 12)

The current page is one long developer-shaped funnel (Hero → Opportunity → Benefits → DAD →
Open Standards → Quantum-Safe → Sectors → Toolkit → How It Works → Technology → Developers →
CTA). The revision keeps the strong middle, but **front-loads audience routing** and
**gives the citizen Wallet and the Designer real estate**.

| # | Section | Primary audience | Single goal | Change from current |
|---|---------|------------------|-------------|---------------------|
| 1 | **Hero** | All | State the one idea (proof not assertion) + route to 3 doors | **Rewrite.** Remove GitHub-as-hero. |
| 2 | **Three doors** (audience router) | All | Self-select: citizen / organisation / developer | **NEW.** The central fix. |
| 3 | **The problem** (assertion is breaking) | Org + dev | Make the stakes concrete (AI forgery) | Reworked "Opportunity". |
| 4 | **How it works — proof in four steps** | All | Explain the mechanism simply | Moved up; merge of "How It Works". |
| 5 | **The DAD model** | Org + dev | Name the security model | Keep, tighten. |
| 6 | **For organisations — the Designer** | Org | Show Describe→Understand→Rehearse→Go-live | **NEW / expanded.** Links to Designer page. |
| 7 | **For citizens — the Sorcha Wallet** | Citizen | Explain the wallet + install | **NEW.** Links to Wallet page. |
| 8 | **Open standards** | Dev + org | Credibility: nothing proprietary | Keep. |
| 9 | **Quantum-safe** | Org + dev | PQC posture (honest) | Keep, apply §2 honesty. |
| 10 | **Sectors / solutions** | Org | Regulatory pull by sector | Keep; links to Solutions page. |
| 11 | **For developers** | Dev | Open-source, self-host, GitHub | Demoted from hero to its own band. |
| 12 | **Maturity & openness** | All | Honest "evaluate/pilot" + MIT | **NEW.** Pre-empts "is this real?". |
| 13 | **Final CTA** | All | Convert to the 3 doors again | Keep. |
| — | **Footer** | All | Nav, legal, standards, repo | Restructure (below). |

### New standalone pages

| Page | Path | Purpose | Audience | Linked from |
|------|------|---------|----------|-------------|
| **Wallet explainer** | `/wallet-info` (marketing) → CTA to PWA at `/wallet/` | What the Sorcha Wallet is, who it's for, how to install (incl. QR) | Citizen | Hero door, §7, nav |
| **Designer / "How you build a workflow"** | `/designer-overview` | The Describe→Understand→Rehearse→Go-live lifecycle, with the "rehearse against sample data before go-live" story | Org | Hero door, §6, nav |
| **Solutions by sector** | `/solutions` (+ optional `/solutions/{sector}`) | DPP · gov identity · AI-Act audit · trade finance | Org | §10, nav |
| **Compare** *(SHIP — R1 done)* | `/compare` | Honest openness × workflow comparison vs walt.id · Procivis · MATTR · Entra (frame as "fully open + workflow", not a feature-checklist takedown) | Org + dev | §3, footer |
| **Developers** | `/developers` (or keep `#developers`) | Repo, self-host, standards, architecture | Dev | Hero door, nav |
| **Download / Get the wallet** | reuse existing `/get` | Wallet front door (exists — Feature 128) | Citizen | Hero door, Wallet page |

> **Note for engineering:** `/get` and `/help` already exist as Blazor pages; `/wallet/` is
> the live PWA. New marketing pages (`/wallet-info`, `/designer-overview`, `/solutions`,
> `/compare`) can ship as static HTML alongside `index.html`, or as additional Blazor routes
> — recommend **static** to keep them in the GitHub-Pages-deployable landing bundle.

### Navigation spec

**Top nav (left → right):** `Sorcha` (logo, → `/`) · **For organisations** · **For citizens**
· **Developers** · **Standards** *(→ `#open-standards`)* · **GitHub** *(icon, right-aligned)*
· **Open Wallet** *(primary button, → `/wallet/`)*.

> The three audience items mirror the "Three doors". On mobile they collapse into the menu;
> "Open Wallet" stays visible as the persistent primary action.

**Audience-routing pattern (the central mechanism):** directly under the hero, three equal
cards — **"I hold credentials" / "I run workflows" / "I build with it"** — each with a
one-line description and a CTA. This is repeated as the final CTA. First-time visitors
self-select within one screen; no audience is funnelled into another's path.

**Footer structure (4 columns + base):**
- *Product*: For organisations · For citizens · The Designer · The Wallet · Solutions
- *Developers*: GitHub · Documentation · Architecture · Standards · Self-hosting
- *Standards & trust*: OpenID4VCI/VP · SD-JWT VC · W3C VC 2.0 · FIPS-204/203 · DAD model
- *Project*: About · Maturity status · Licence (MIT) · Contact
- *Base*: © Sorcha · "Open source, MIT licensed" · cookie/consent link (`openConsent()`) ·
  privacy.

---

## D3. Page-by-page copy (final)

> Copy is paste-ready. CTA destinations use real paths. R2 sourcing is **done** — the stat band
> and sector cards below now carry sourced, attributed figures (see `website-overhaul-R2-R3-R4-research.md`);
> the unsourced `$50bn` figure has been cut.

### Section 1 — Hero
- **Eyebrow:** Cryptographic proof infrastructure
- **Headline:** **Stop asking who to trust. Start verifying.**
- **Subhead:** Sorcha replaces asserted trust with cryptographic proof. Every action is
  signed by the participant who took it, every record is immutable, and every party can check
  the evidence for themselves — without trusting the platform.
- **Primary CTA:** `I run workflows →` (→ `#three-doors` / `/designer-overview`)
- **Secondary CTA:** `I hold credentials →` (→ `/wallet/`)
- **Tertiary (text link):** `Building with it? Start on GitHub →` (→ GitHub repo)
- *(Hero `<canvas>` animation stays — see D4 Motion.)*

### Section 2 — Three doors (audience router)
- **Section heading:** Three ways in.
- **Card A — "I hold credentials"**
  - Body: Receive credentials on your phone and present exactly what's asked for — nothing
    more. Anyone can check they're genuine.
  - CTA: `Get the Sorcha Wallet →` (→ `/wallet/`)
- **Card B — "I run workflows"**
  - Body: Design multi-party processes, issue verifiable credentials, and run a tamper-evident
    register your counterparties and regulators can verify.
  - CTA: `See the Designer →` (→ `/designer-overview`)
- **Card C — "I build with it"**
  - Body: Open-source proof infrastructure on .NET. Self-host it, read every line, build on
    published standards.
  - CTA: `Explore the code →` (→ `/developers`)

### Section 3 — The problem
- **Headline:** Digital systems run on assertion. AI just made assertion cheap to fake.
- **Body:** A document *says* it's real. A platform *claims* its data came from a trusted
  source. None of it is cryptographically anchored — so when forgery becomes fast and cheap,
  the whole edifice becomes unreliable. The systems that run society — benefits, supply-chain
  compliance, financial settlement, regulatory enforcement — were built assuming the data they
  consume is honest. That assumption is breaking.
- **Stat band (R2-sourced — use these, with attribution):**
  - Deepfaked-selfie attacks rose **58% in 2025** (Entrust, *2026 Identity Fraud Report*).
  - By 2028, **at least 80% of governments** will deploy AI agents to automate routine
    decision-making (Gartner, 17 Mar 2026) — decisions that need inputs they can trust.
  - ~~"$50bn identity fraud"~~ — **CUT** (unsourced; real Javelin figure is $27.3bn, US-scope).
    If a fraud figure is wanted, use *identity-fraud losses reached $27.3bn in the US in 2025
    (Javelin)* — attribute, and don't say "global".
- **Transition line:** Cryptographic proof defends against forgery. Assertion does not.

### Section 4 — How it works (proof in four steps)
- **Headline:** Proof, in four steps.
1. **Sign.** Each participant holds their own keys. Every action is signed by the person or
   organisation who took it — accountability is built in, not bolted on.
2. **Record.** Signed actions are written to an append-only register, each entry
   Merkle-chained to the last. Nothing can be altered or quietly removed.
3. **Disclose by schema.** Each party sees only the fields they're entitled to — and each
   party's data is encrypted to a key only they hold (per-recipient key wrapping). The platform
   cannot read data it was not given access to; this is architectural, not a policy setting.
4. **Verify.** Any party can check the signatures and the chain themselves. Trust comes from
   the evidence, not from us.

### Section 5 — The DAD model
- **Headline:** The DAD model: Disclosure, Alteration, Destruction.
- **Three cards:**
  - **Disclosure — managed by schema.** What each participant can see is defined and bounded.
  - **Alteration — recorded on an immutable ledger.** Every change is signed and chained;
    history can't be rewritten.
  - **Destruction — eliminated by replication.** Records are replicated across the peer
    network, so no single party can erase them.

### Section 6 — For organisations: the Designer
- **Headline:** Design a workflow. Rehearse it. Then go live.
- **Body:** The Designer walks you from a plain-language description to a running, signed
  workflow in four stages — and lets you test against sample data before anything goes live.
- **Four stages (as a labelled row):**
  - **Describe** — say what the process does, in plain language.
  - **Understand** — see the participants, actions and disclosures the platform derived.
  - **Rehearse** — run it against sample data; nothing is committed.
  - **Go live** — publish only once rehearsal passes. *(Go-live is gated server-side on a
    rehearsal pass — you can't skip it.)*
- **CTA:** `See how the Designer works →` (→ `/designer-overview`)

### Section 7 — For citizens: the Sorcha Wallet
- **Headline:** Your credentials, on your device, in your control.
- **Body:** The Sorcha Wallet is an app you install on your phone. It holds the credentials
  organisations issue to you, and lets you present exactly what's asked for — and only that.
  Whoever you show them to can check they're genuine, without phoning anyone to confirm.
- **Microcopy bullets:**
  - Hold credentials offline, on your device.
  - Present only the fields requested — selective disclosure by design.
  - Built on open standards (OpenID4VP, SD-JWT VC) — not a walled garden.
- **Primary CTA:** `Open the Wallet →` (→ `/wallet/`)
- **Secondary CTA:** `Install on your phone` (→ `/get`, show QR)

### Section 8 — Open standards
- **Headline:** Nothing proprietary. Everything inspectable.
- **Body:** Every protocol, format and cryptographic primitive Sorcha uses is a published
  standard.
- **Standards chips:** OpenID4VCI · OpenID4VP · SD-JWT VC · W3C Verifiable Credentials 2.0 ·
  `did:sorcha` · BIP-32/39/44 · FIPS-204 (ML-DSA) · FIPS-203 (ML-KEM) · Merkle / SHA-256.

### Section 9 — Quantum-safe (honest version)
- **Headline:** Built for records that outlive today's cryptography.
- **Body:** Some records have to stay verifiable for decades — a product passport, a property
  history, a regulatory audit trail. Sorcha uses **ML-DSA (FIPS 204)** post-quantum signatures
  and **ML-KEM (FIPS 203)** key encapsulation as a core part of the platform, not a side
  feature.
- **Honesty footnote (keep it — credibility with sceptical buyers):** The HAIP wallet boundary
  still requires classical signatures today; Sorcha bridges this with a classical co-key
  derived alongside the post-quantum keys. Zero-knowledge selective disclosure (BBS+) is on
  the roadmap, not shipped — today's selective disclosure is show/hide.
- *(Do **not** state "SLH-DSA not implemented", and do **not** imply PQC is unique to Sorcha —
  Procivis and IOTA also ship PQC. See ledger #20 and R1.)*

### Section 10 — Sectors / solutions
- **Headline:** Where proof beats "trust me".
- **Intro:** Sorcha fits domains where multiple parties must share data they each need to
  trust, under regulation that won't accept an operator's word for it.
- **Four sector cards (R3 order — government identity first):**
  - **Government-aligned identity.** The same standards the EU Digital Identity Wallet and
    GOV.UK Wallet are converging on — Sorcha is the workflow / verifier layer above them.
    eIDAS 2 requires every EU state to offer a wallet by **December 2026**; the UK's GOV.UK
    Wallet rolls out through 2026–27.
  - **Digital Product Passports.** The first mandatory DPP — the **Battery Passport** — is
    required from **18 February 2027** under the EU **Battery Regulation 2023/1542**; ESPR
    extends DPPs to further product groups. Tamper-evident, multi-party, selectively-disclosed
    lifecycle records, with signatures built to last a product's lifetime. *(Position as the
    proof substrate underneath DPP platforms, not as another DPP platform — R1/R3.)*
  - **AI-decision audit trails.** EU AI Act high-risk obligations apply from **2 August 2026**;
    Articles 10 & 12 of Reg. (EU) 2024/1689 require data-provenance and automatic logging.
    Signed, immutable register entries are exactly what an auditor needs.
  - **SME trade finance.** A buyer's wallet signature on an invoice is the trust anchor for a
    lender — no intermediary needs to vouch for the data. Enabled by the UK Electronic Trade
    Documents Act 2023 / MLETR; no blockchain token required.
- **CTA:** `Explore solutions →` (→ `/solutions`)

### Section 11 — For developers
- **Headline:** Open source. Standards-based. Yours to run.
- **Body:** Sorcha is built on .NET 10 and .NET Aspire, MIT-licensed, and self-hostable with
  Docker. Eight single-responsibility services, a documented API, and more than 15,000 tests.
  Read it, run it, build on it.
- **Spec line:** .NET 10 · .NET Aspire · MIT · PostgreSQL / MongoDB / Redis · Minimal APIs +
  OpenAPI · `[VERIFY]` 8 services · 15,000+ tests.
- **CTAs:** `View on GitHub →` · `Read the docs →` · `Self-hosting guide →`

### Section 12 — Maturity & openness
- **Headline:** Where we are — honestly.
- **Body:** Sorcha's core feature set is complete. It is **not yet production-hardened** —
  it's open source, standards-based, and ready to evaluate and pilot. If you want to test
  proof-based infrastructure for a regulated, multi-party workflow, this is the point to
  start a conversation.
- **CTA:** `Start evaluating →` (→ `/get` or contact) · `Read the development status →`

### Section 13 — Final CTA
- **Headline:** Pick your way in.
- *(Repeat the three doors from Section 2.)*

### Microcopy library
- Cookie banner (existing `consent-banner.js`): keep — "We use analytics cookies to improve
  the site. You can decline." `[Accept] [Decline]`.
- Wallet install empty-state: "No credentials yet. When an organisation issues you one, it'll
  appear here."
- GitHub button title attr: "Sorcha on GitHub — MIT licensed, open source."

---

## D4. Visual & brand design direction

> **Design intent (R5 DECIDED 2026-06-08):** KEEP the existing indigo/violet-on-near-black
> identity and the logo — do **not** move to ink-navy/teal (that earlier proposal is withdrawn).
> The goal stays **credible, precise, technical-but-calm** ("instrument panel / standards body"),
> achieved by *refining* the current palette, not replacing it. The refined, contrast-checked
> token set (light + dark) will come from the design-AI brief in
> `website-overhaul-R5-design-prompt.md`; treat that output as the source of truth once returned.
> The table below is the **interim baseline**, taken from the real brand asset
> (`brand/sorcha-icon.svg`).

### Colour palette (tokens — works as CSS variables *and* MudBlazor theme)

> Interim baseline — indigo/violet identity. Saturated indigo fails AA for small body text on
> white, so light mode uses a deeper indigo for text-bearing roles; **the design-AI pass must
> return final contrast-checked values (D5).**

| Token | Role | Light | Dark | Notes |
|-------|------|-------|------|-------|
| `--color-primary` | Primary brand / headings / primary buttons | `#4F46E5` (deep indigo) | `#818CF8` | Brand indigo; light uses the deeper indigo for contrast. |
| `--color-primary-strong` | Hover/active on primary | `#4338CA` | `#A5B4FC` | |
| `--color-accent` | Verification accent (proof, "verified", CTAs) | `#6366F1` (brand indigo) | `#818CF8` | Keep within indigo/violet — design AI may pick one distinct violet step, not a new hue. |
| `--color-accent-strong` | Accent hover | `#4F46E5` | `#A5B4FC` | |
| `--color-bg` | Page background | `#FFFFFF` | `#0B0C18` (near-black indigo, icon ground) | |
| `--color-surface` | Cards / raised surfaces | `#F5F6FB` | `#14162A` | |
| `--color-surface-alt` | Alt section banding | `#ECEEF7` | `#1B1E36` | |
| `--color-text` | Body text | `#14152A` | `#E7E9F5` | |
| `--color-text-muted` | Secondary text / captions | `#565A78` | `#A2A7C8` | |
| `--color-border` | Hairlines / dividers | `#DDE0EF` | `#2A2D4A` | |
| `--color-success` | Success / "verified" state | `#1C7C54` | `#3FAE7E` | |
| `--color-warn` | Warning | `#B26A00` | `#E0A33C` | |
| `--color-error` | Error | `#B3261E` | `#F2675F` | |

**Gradient (hero canvas / accents):** keep the indigo/violet glow on the near-black ground (the
icon uses `#6366F1` / `#818CF8` particles + a violet radial bloom over `#090A14 → #0F1020`). Use
sparingly — large flat dark fields + a restrained indigo accent, not a full-bleed gradient. The
existing landing's lighter `#667eea→#764ba2` gradient and green `#48bb78` accent should be
reconciled back to these darker brand values (the green is an outlier — replace or justify it).

> **Engineering note (MudBlazor):** map `--color-primary` → `Palette.Primary`, `--color-accent`
> → `Palette.Secondary`/`Palette.Tertiary`, surfaces → `Palette.Background`/`Surface`,
> text → `Palette.TextPrimary`/`TextSecondary`, in `SorchaMudTheme.cs`. Provide both
> `PaletteLight` and `PaletteDark`. **Confirm all pairings meet WCAG AA (D5).**

### Typography
- **Family:** keep **Inter** for UI/body (already loaded, performance-friendly, neutral-
  technical). Optionally pair a **monospace** (`JetBrains Mono` or system `ui-monospace`) for
  standards names, hashes, DIDs, and code chips — reinforces the "verifiable / technical"
  identity. No serif; serif would soften the precise tone.
- **Web-font budget:** Inter at weights 400/500/600/700 only (drop 300/800 from the current
  `300;400;500;600;700;800` request — trims payload). `display=swap` already set.

| Style | Size (desktop) | Weight | Line-height | Use |
|-------|----------------|--------|-------------|-----|
| H1 | 56px / 3.5rem | 700 | 1.05 | Hero headline |
| H2 | 40px / 2.5rem | 700 | 1.15 | Section headlines |
| H3 | 28px / 1.75rem | 600 | 1.2 | Card / subsection |
| H4 | 22px / 1.375rem | 600 | 1.25 | Minor headings |
| H5 | 18px / 1.125rem | 600 | 1.3 | Labels |
| Body-L | 19px / 1.1875rem | 400 | 1.6 | Hero subhead, lead paragraphs |
| Body | 16px / 1rem | 400 | 1.65 | Default |
| Caption | 13px / 0.8125rem | 500 | 1.4 | Footnotes, microcopy |
| Mono | 14px / 0.875rem | 500 | 1.5 | Standards names, hashes, DIDs |

Mobile: H1 → 36px, H2 → 28px, Body-L → 17px. Fluid `clamp()` recommended.

### Spacing / layout
- **Base unit:** 8px. Scale: 4 / 8 / 16 / 24 / 32 / 48 / 64 / 96.
- **Container max-width:** 1200px content; 720px for long-form reading (sub-pages).
- **Breakpoints:** 480 (mobile) · 768 (tablet) · 1024 (laptop) · 1280 (desktop).
- **Section rhythm:** 96px top/bottom padding desktop, 56px mobile; alternate
  `--color-bg` / `--color-surface-alt` banding to separate sections without rules.
- **Grid:** 12-col with 24px gutter; "Three doors" = 3×4-col cards (stack on mobile).

### Components
- **Buttons:** Primary = solid `--color-primary`, white text. Accent/CTA = solid
  `--color-accent`. Secondary = outline (1px `--color-primary`, transparent fill). Text/tertiary
  = no border, underline on hover. States: default / hover (`-strong`) / focus (2px accent
  focus ring, see D5) / disabled (40% opacity). Radius 8px. MudBlazor: `Variant.Filled` /
  `Variant.Outlined` / `Variant.Text`.
- **Cards (incl. the three doors):** `--color-surface`, 1px `--color-border`, radius 12px,
  16/24px padding, subtle shadow on hover only (no heavy drop shadows — flat reads serious).
  MudBlazor: `MudPaper`/`MudCard` `Elevation="0"` + border.
- **Badges / chips (standards):** pill, `--color-surface-alt` bg, mono label, accent left tick
  for "implemented" vs muted for "roadmap". Use the implemented/roadmap distinction to *show*
  honesty visually.
- **Nav:** sticky, `--color-bg` with bottom hairline; "Open Wallet" as the only filled accent
  button in the bar.
- **Hero treatment:** large H1 left, three-door router immediately beneath, `#heroCanvas`
  behind/right at low opacity. No stock photography.

### Imagery / illustration / iconography
- **Style:** geometric, line-based, monochrome-with-one-accent. Subject: nodes, signatures,
  chains, locks-as-keys, document-with-tick. **Do:** abstract network/proof motifs.
  **Don't:** stock business handshakes, glowing-blue-blockchain clichés, faces, 3D crypto coins.
- **Icons:** single line-weight set (e.g. Lucide / Material Symbols outlined — MudBlazor ships
  Material). Accent only for "verified/proof" states.
- **Asset list (engineer to source/generate unless supplied):**

| Filename | Purpose | Dimensions | Format | Source |
|----------|---------|-----------|--------|--------|
| `sorcha-icon.svg` | Existing logo mark | vector | SVG | ✅ exists |
| `wordmark.svg` | Wordmark (if R5 commissions one) | vector | SVG | `[COWORK-RESEARCH] R5` |
| `og-default.png` | Open Graph default | 1200×630 | PNG | generate (D6) |
| `og-wallet.png` | OG for wallet page | 1200×630 | PNG | generate |
| `og-designer.png` | OG for designer page | 1200×630 | PNG | generate |
| `hero-motif.svg` | Static hero fallback (reduced-motion) | ~960×720 | SVG | generate |
| `icon-sign.svg` / `icon-record.svg` / `icon-disclose.svg` / `icon-verify.svg` | "Four steps" | 48×48 | SVG | generate |
| `door-citizen.svg` / `door-org.svg` / `door-dev.svg` | Three-doors cards | 64×64 | SVG | generate |

### Motion
- Keep the hero `<canvas>` but **calm it**: slow drift, low contrast, no fast train motion;
  pause when off-screen. Section reveals = 150–200ms fade/translate-up, once. CTA hover =
  120ms. **All motion must respect `prefers-reduced-motion` (D5) — swap canvas for
  `hero-motif.svg`, disable reveals.**

---

## D5. Accessibility & responsive spec

- **Target:** WCAG 2.2 **AA**.
- **Contrast (re-verify once the R5 design-AI returns final tokens):** body `#14152A` on
  `#FFFFFF` ≈ 15:1 ✅; `--color-text-muted` `#565A78` on white ≈ 5.6:1 ✅ (AA for normal text);
  saturated indigo `#6366F1` on white ≈ 3.9:1 — **large text / UI only, not small body text**;
  for indigo *buttons* use white text on the deeper indigo `#4F46E5` ≈ 6.8:1 ✅. In dark mode
  check `#818CF8` text on `#0B0C18`. **Engineer must run a contrast checker on every final
  pairing.**
- **Focus states:** visible 2px `--color-accent` outline + 2px offset on every interactive
  element; never remove outlines. Logical tab order; skip-to-content link.
- **Reduced motion:** `@media (prefers-reduced-motion: reduce)` disables canvas + reveals,
  shows `hero-motif.svg`.
- **Semantics:** one `<h1>` per page; sections as `<section aria-labelledby>`; the three-door
  router as a `<nav aria-label="Choose your path">`; canvas `aria-hidden="true"` with text
  alternative present.
- **Forms / wallet entry:** labelled inputs, 44×44px min touch targets, visible error text
  (not colour-only).
- **Responsive per key section:** Hero stacks (H1 → subhead → doors). Three doors 3-up →
  1-up < 768px. Four-step / DAD / sector grids → single column on mobile. Nav → hamburger
  < 768px with "Open Wallet" persistent. Tables (standards) → wrap to chips, never
  horizontal-scroll on mobile.

---

## D6. SEO / metadata

> Titles ≤ ~60 chars, descriptions ≤ ~155, voice-rule compliant. Keyword targeting pending
> `[COWORK-RESEARCH] R4`.

| Page | `<title>` | Meta description |
|------|-----------|------------------|
| Home `/` | Sorcha — Cryptographic proof infrastructure | Replace asserted trust with proof. Open-source infrastructure for multi-party workflows where every party can verify the evidence themselves. |
| Wallet `/wallet-info` | Sorcha Wallet — Your credentials, your device | Hold verifiable credentials on your phone and present only what's asked for. Built on open standards. Install the Sorcha Wallet. |
| Designer `/designer-overview` | The Sorcha Designer — Describe, rehearse, go live | Design multi-party workflows, rehearse against sample data, and publish signed, verifiable processes. |
| Solutions `/solutions` | Sorcha solutions — proof for regulated work | Digital Product Passports, government-aligned identity, AI-decision audit trails, and trade finance — built on verifiable proof. |
| Developers `/developers` | Build on Sorcha — open-source proof infrastructure | MIT-licensed, .NET 10, self-hostable. Eight services, published standards, more than 10,000 tests. |

**Primary keyword targets (R4 — go after winnable long-tails, not the head terms):**
- **Home** — "post-quantum verifiable credentials", "cryptographic proof platform", self-hosted / sovereign.
- **Wallet** — "EUDI wallet relying party integration", "EAA/QEAA issuer software", "EUDI issuer .NET".
- **Designer** — "verifiable credentials platform" (long game), "multi-party workflow orchestration".
- **Developers** — **".NET / C# verifiable credentials"** (top opportunity — no dedicated .NET platform exists), "ML-DSA/ML-KEM .NET", "SD-JWT VC tutorial", "self-hosted / open-source / MIT".
- **Solutions** — "DPP service provider / self-hosted / verifiable DPP", "battery & textile passports", "eIDAS 2 / data-sovereignty" long-tails.
- **Do NOT chase head-on** (owned by Wikipedia/NIST/Microsoft/Gartner/EU repos): "post-quantum cryptography", "verifiable credentials platform", "digital product passport software", "EUDI wallet issuer". Full analysis + caveats on volume data: `website-overhaul-R2-R3-R4-research.md`.

**Open Graph (all pages):** `og:type=website`, `og:site_name=Sorcha`, per-page `og:title`/
`og:description` (reuse above), `og:image` per page (`og-*.png`, 1200×630), `twitter:card=
summary_large_image`.

**Structured data:** `Organization` schema on home (name, url, logo `sorcha-icon.svg`,
`sameAs` → GitHub); `SoftwareApplication` schema on `/developers`
(`applicationCategory: DeveloperApplication`, `license: https://opensource.org/licenses/MIT`,
`operatingSystem: Cross-platform`). `WebSite` + `SearchAction` only if site search exists
(it doesn't — omit). Keep all schema values factual; no rating/review fields (no customers
to cite — see R6).

---

## D7. Open questions & assumptions

**Assumptions made (flag if wrong):**
1. New marketing sub-pages ship as **static HTML** in the landing bundle (not Blazor routes).
2. "Over 10,000 tests" and "8 services" are publishable as-is (both verified; `[VERIFY]`
   tokens are belt-and-braces, not doubt).
3. **R6 decided:** start LOW — show only **Assured Identity** as an example workflow for now;
   expand as demos are written separately. Never imply production customers.
4. Dark mode is in scope (MudBlazor theme already has light/dark; landing should match).

**Decisions — RESOLVED (2026-06-08):**
1. **R5 brand** — KEEP the indigo/violet identity + logo; refine via
   `website-overhaul-R5-design-prompt.md`. The ink-navy/teal proposal is withdrawn.
2. **R3 sector order** — Government identity → Digital Product Passports → AI-Act audit → SME
   trade finance.
3. **R1 `/compare`** — SHIP, framed as openness × workflow, vs walt.id · Procivis · MATTR · Entra.
4. **Ledger #20 (SLH-DSA)** — use it: ML-DSA is the primary/default line, SLH-DSA noted as
   present in the library; never claim PQC uniqueness. (TODO: reconcile `strategic-context.md`.)
5. **Ledger #21** — VERIFIED in code; the stronger "per-recipient key wrapping" claim is approved.
6. **R2 stats** — cut "$50bn"; reword deepfake/biometrics to source wording; fix Battery-Passport
   attribution (Battery Regulation 2023/1542, not ESPR).

**Remaining to-do:**
7. **Contact endpoint** — DECIDED to add one; mechanism still to choose. Recommend a simple
   `/contact` form with an email fallback (Calendly optional). The maturity / "start evaluating"
   CTAs need this real destination before launch.

---

## Definition of done (per brief §6)
- Sections D1–D7 present, in order. ✅
- Copy in paste-ready blocks; design values in tables. ✅
- No code, no framework swaps, no banned adjectives, no unverified stats published (all
  tagged `[VERIFY]` / `[VERIFY-SOURCE]`). ✅
- Every capability claim verified against the codebase (Section 0). ✅
- Research that needs a human/market is isolated under `[COWORK-RESEARCH]` (R1–R6). ✅

---

### Appendix — engineering implementation pointers
- Static landing: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/{index.html,landing.css,landing.js,consent-banner.js}`
  (→ GitHub Pages + `ui-web` container). New sub-pages as sibling `.html`.
- Blazor app + theme: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client` + `Sorcha.UI.Core`;
  theme tokens in `Sorcha.UI.Components.User/Theme/SorchaMudTheme.cs` (PaletteLight + PaletteDark).
- Wallet PWA entry: origin-root `/wallet/` (`Sorcha.Wallet.Pwa`). Wallet front door: `/get`.
- Voice gate: `scripts/check-discoverability.sh` (the 9 banned adjectives) — run before commit.
- Voice/positioning source of truth: `docs/strategic-context.md` (note the SLH-DSA reconciliation, ledger #20).

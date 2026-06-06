# BRIEF: Sorcha website content & design overhaul

> Hand this to a marketing/design AI (or a human marketing manager + designer). It is
> self-contained — it carries the product facts needed to work without the codebase. The
> output is a content + design **specification** that a separate engineering agent will
> implement against a static HTML/CSS landing page and a Blazor (MudBlazor) web app.

---

## 1. Your role
You are acting as a **marketing manager + product designer** for Sorcha. Your job is to
produce the **content and design specification** for an overhaul of Sorcha's public-facing
website. You are NOT writing code. Your output will be handed to a separate engineering
agent who will implement it against a static HTML/CSS landing page and a Blazor (MudBlazor /
Material Design) web application. Produce **final copy and design specs**, not placeholders,
not code, not a different tech stack.

## 2. What Sorcha actually is (use these facts; do not invent capabilities)
Sorcha is **cryptographic proof infrastructure for multi-party workflows**. It produces
evidence — wallet signatures, Merkle-chained ledger entries, immutable register records —
that any party can verify independently *without trusting the platform operator*.

Core idea: digital systems run on **assertion, not proof**. A document *says* it's real; a
platform *claims* data came from a trusted source. AI makes high-quality forgery cheap, so
assertion-based trust is breaking. Sorcha replaces assertion with proof: every action is
signed by the participant who took it; every record is immutable and Merkle-chained; every
disclosure is cryptographically bounded (the platform literally cannot read data it wasn't
given the key for — this is architectural, not policy).

The security model is **DAD**: **D**isclosure managed by schema, **A**lteration recorded on
an immutable ledger, **D**estruction eliminated by peer-network replication.

**Architecture in one line:** a small set of single-responsibility services — Blueprints
(workflow definitions), Wallets (keys + signing), Registers (append-only Merkle ledgers),
Validator (quorum consensus), Peer (decentralised replication), Tenant (multi-tenancy),
API Gateway (single external surface), and a HAIP service (the boundary to the
OpenID4VC / EUDI / GOV.UK wallet ecosystem). Everything uses published standards; nothing
is proprietary.

**Why now (the timing story you can lean on):**
- AI-generated fraud is scaling (identity-fraud losses >$50bn in 2025; deepfake attempts
  up ~58% YoY). Cryptographic proof defends against forgery; assertion does not.
- AI systems are becoming decision-makers, and regulation (EU AI Act) requires documented
  data provenance for high-risk automated decisions. Sorcha is the verifiable data layer
  AI systems can consume with confidence.

**Strongest regulatory pull (good for sector messaging):**
- **EU ESPR / Digital Product Passports** — legally mandated, hard deadlines (Battery
  Passport Feb 2027). Post-quantum signatures genuinely matter for 30-year product lifetimes.
- **HAIP / EUDI Wallet / GOV.UK Wallet** — HAIP 1.0 finalised Dec 2025. Sorcha implements
  OpenID4VCI (issuance) + OpenID4VP (presentation) and sits as the *workflow layer above*
  these government wallets.
- **AI Act compliance / automated-decision audit trails.**
- **SME trade finance** — a buyer's wallet signature on an invoice is the trust anchor for
  lenders; no intermediary needs to vouch for the data.

**Cryptographic posture to highlight (true):** ML-DSA (NIST FIPS 204) + ML-KEM (FIPS 203)
post-quantum signatures as core (not a side feature); BIP32/39/44 HD wallets; JSON-Pointer
selective disclosure with per-recipient key wrapping; Merkle dockets with SHA-256 hash
linkage. **Honest gaps you must NOT paper over:** HAIP 1.0 mandates classical signatures at
the wallet boundary (Sorcha bridges with a classical co-key); SLH-DSA and BBS+
zero-knowledge proofs are not yet implemented (current selective disclosure is show/hide,
not zero-knowledge).

**What Sorcha is NOT (be precise — these are common mischaracterisations):** not a public
blockchain (it's a *permissioned proof network*); not a message bus/queue (it's a ledger);
not an identity provider (it integrates with IdPs); not a smart-contract platform
(Blueprints are schema-validated workflows, not Turing-complete); not a data warehouse;
not a replacement for GOV.UK/EUDI Wallet (it's the infrastructure those wallets sit above).

**Maturity (do not overclaim):** the core feature set is complete ("MVD complete"), but the
product is **not yet GA / production-hardened** (production readiness ~30%). Do not imply
it's battle-tested at scale. "Open source, standards-based, ready to evaluate / pilot" is
honest; "enterprise-grade, production-proven" is not (yet).

## 3. The three audiences (the overhaul's central problem)
The current site speaks almost entirely to **developers** ("View on GitHub" is the hero CTA)
and is silent on two audiences that the product now serves first-class. Your messaging
architecture must serve all three and route each cleanly:

1. **Citizens / credential holders** — people who receive and present credentials via the
   **Sorcha Wallet** (an installable PWA at `/wallet/`). Until very recently this had zero
   presence on the site. Plain-language, mobile-first, "your credentials on your device."
2. **Organisations / operators (enterprise & public sector)** — councils, manufacturers,
   regulators, lenders who *design workflows, issue credentials, and run registers*. This is
   the buyer/decision-maker. Speaks to compliance (ESPR, EUDI, AI Act), trust, control.
   Key product surface: the **Designer** (a guided "Describe → Understand → Rehearse →
   Go-live" workflow-authoring lifecycle, where you test against sample data before going
   live) — currently undescribed on the site.
3. **Developers / technical evaluators** — open-source (MIT), standards-based, .NET/Aspire,
   self-hostable. Keep the existing GitHub/architecture path but as ONE of three doors,
   not the only one.

## 4. Current site state & the gap to close
The marketing landing is a single long static page (`sorcha.dev`) with ~12 sections in this
order: Hero → The Opportunity → Benefits → DAD Model → Open Standards → Quantum-Safe →
Sectors → Toolkit → How It Works → Technology → Developers → CTA. There are also small
app pages: `/get` (a wallet "get started" front door), `/help`, and the in-app shell.

Problems to fix:
- **Dev-toy framing.** Hero leads with GitHub. No enterprise/citizen narrative.
- **The citizen Wallet is barely present.** No explanation of what it is or who it's for.
- **The Designer lifecycle (Describe→Understand→Rehearse→Go-live) is invisible.**
- **No audience routing.** A council procurement lead and a citizen and a developer all
  land in the same developer-shaped funnel.
- **Positioning hasn't caught up** to "enterprise trust infrastructure + citizen credential
  wallet for regulated sectors."

## 5. NON-NEGOTIABLE constraints (read before writing any copy)

### Voice (CI-enforced — violations literally fail the build)
The following marketing adjectives are **banned** and checked automatically. Do NOT use them
or close synonyms: **revolutionary, best-in-class, industry-leading, cutting-edge,
world-class, seamless, game-changing, next-generation, state-of-the-art.**

Voice principles: **factual over aspirational; precise over impressive; honest about
boundaries.** State what exists, not what's planned. "Implements OpenID4VCI with SD-JWT VC"
beats "industry-leading credential issuance." Confident and clear is good; hype is banned.

### Accuracy
Every capability claim must be true per Section 2. If you want to state a number (e.g.
"N microservices", "X tests"), flag it as `[VERIFY]` so the engineer confirms it — do not
publish unverified stats. Never imply standards compliance that's only partial; use
"implements / supports / partial / planned" honestly.

### Technical reality (so deliverables are implementable)
- The **marketing landing is static HTML + CSS + a little vanilla JS** (no server-side
  rendering, no React/Vue/Svelte). It's deployed to GitHub Pages *and* served from a
  container. Design must be deliverable as semantic HTML + CSS. A hero `<canvas>` animation
  exists and can stay/evolve.
- The **web application** (`/app`, `/get`, etc.) is **Blazor with MudBlazor (Material
  Design)**. In-app design must map to Material components and a MudBlazor theme
  (palette + typography tokens), not arbitrary CSS frameworks.
- Fonts currently: **Inter**. You may propose alternatives but justify and keep it
  performance-friendly (web-font budget matters).
- Google Analytics with **Consent Mode v2** (cookie banner, default-denied) is in place —
  keep any analytics/consent implications in mind.
- The wallet PWA lives at origin-root `/wallet/`; links to it from the marketing site and
  the app are full-page navigations. Account for "Open Wallet" / "Install on your phone"
  (QR) entry points.

## 6. DELIVERABLES (this is what is needed back, in this structure)

Produce a single structured document with these sections. Final words and concrete specs —
no "lorem", no "TBD".

**D1. Positioning & messaging architecture**
- One-sentence positioning statement; a 30-word elevator description; a 100-word "about".
- Primary value proposition per audience (citizen / organisation / developer).
- Messaging hierarchy: the 3–5 core claims the whole site ladders up to.

**D2. Information architecture / sitemap**
- The full landing-page section list **in order**, each tagged with its primary audience and
  its single goal. Add, merge, reorder, or cut from the current 12 sections as needed and
  justify changes briefly.
- Any **new standalone pages** you recommend (e.g. a Wallet explainer, a Designer/"how you
  build a workflow" page, a Solutions-by-sector page, a `/download` page). For each: purpose,
  audience, and where it's linked.
- Navigation spec: top-nav items + order, footer structure, and the **audience-routing
  pattern** (how a first-time visitor self-selects citizen vs organisation vs developer).

**D3. Page-by-page copy (final)**
For every section/page in D2:
- Headline, subhead, body copy, bullet lists, and **all CTA button labels + their
  destinations** (use real paths: `/wallet/`, `/app`, GitHub, `/get`, `#section`, etc.).
- Microcopy (badges, captions, form/empty-state text where relevant).
- Provide copy as clearly labelled blocks so it can be pasted directly.

**D4. Visual & brand design direction (as implementable tokens)**
- **Colour palette**: hex values with roles (primary, secondary, accent, background,
  surface, text, success/warn/error), plus a dark-mode variant if proposed. Provide as a
  token table (works for both CSS variables and a MudBlazor theme).
- **Typography**: font family/families, the type scale (sizes + weights + line-heights for
  h1–h6, body, caption), and pairing rationale.
- **Spacing/layout**: base spacing unit, grid/breakpoints, container widths, section rhythm.
- **Components**: buttons (variants/states), cards, badges, nav, hero treatment. For in-app
  pages, express these as MudBlazor theme/variant choices.
- **Imagery/illustration/iconography direction**: style, subject matter, do/don't. Note
  which assets you'll supply vs which the engineer should source/generate, as an **asset
  list** (filename, purpose, dimensions, format).
- **Motion**: any animation intent (incl. the hero canvas) kept restrained and accessible.

**D5. Accessibility & responsive spec**
- WCAG target (assume AA), colour-contrast confirmations for the palette, focus states,
  reduced-motion behaviour, and mobile-first responsive notes per key section.

**D6. SEO / metadata**
- Per page: `<title>`, meta description, Open Graph title/description/image, and any
  structured-data (schema.org) updates. Keep within the voice rules.

**D7. Open questions & assumptions**
- List anything assumed, and any decisions needed from the stakeholder to confirm (brand
  logo/wordmark availability, primary-audience priority, sector focus order, whether a
  `/download` page is wanted, etc.).

## 7. Output format & definition of done
- One Markdown document, sections D1–D7, in that order.
- Copy in paste-ready blocks; design values in tables.
- No code, no framework swaps, no banned adjectives, no unverified stats (tag `[VERIFY]`).
- Definition of done: an engineer could implement the static landing and theme the Blazor
  app from the document **without having to invent any copy, colour, or layout decision.**

## 8. Things to explicitly avoid
- Inventing features, integrations, customers, or metrics.
- Implying GA/production-hardened maturity.
- Proposing a JS SPA, headless CMS, or any stack the engineer can't ship on static
  HTML/CSS + Blazor/MudBlazor.
- Generic SaaS hype. This is trust infrastructure for regulated, sceptical buyers — the
  tone is credible, precise, and standards-literate.

---

## Appendix — for the engineering agent (not the marketer)
Source of truth for voice/positioning: `docs/strategic-context.md`. The banned-adjective
list is CI-enforced via `scripts/check-discoverability.sh`. Editable surfaces:
`src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/{index.html,landing.css,landing.js,consent-banner.js}`
(static landing → GitHub Pages + `ui-web` container) and the Blazor app under
`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client` + theme/components in `Sorcha.UI.Core`
(MudBlazor). Wallet PWA entry point is origin-root `/wallet/`.

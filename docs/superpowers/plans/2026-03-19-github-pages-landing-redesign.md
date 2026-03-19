# GitHub Pages Landing Page Redesign

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure www.sorcha.dev landing page from tech-first to business-first narrative, deploy via GitHub Pages from the Sorcha-Platform/Sorcha repo.

**Architecture:** Single static HTML page (index.html + landing.css + landing.js) deployed to GitHub Pages via a dedicated workflow. The page is self-contained — no static site generator. A copy of the landing assets lives in `docs/site/` for GitHub Pages, separate from the Blazor UI's copy in `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/`.

**Tech Stack:** HTML5, CSS3, vanilla JS, GitHub Actions, GitHub Pages

---

## File Structure

**Create:**
- `docs/site/index.html` — GitHub Pages landing page (business-first narrative)
- `docs/site/landing.css` — Updated styles (new sections: problem, comparison, sectors, toolkit)
- `docs/site/landing.js` — Copy from existing with animation selectors updated
- `docs/site/favicon.png` — Copy from existing
- `docs/site/CNAME` — Custom domain: `www.sorcha.dev`
- `docs/site/.nojekyll` — Skip Jekyll processing
- `.github/workflows/gh-pages.yml` — Deploy docs/site/ to GitHub Pages

**Modify:**
- `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/index.html` — Sync narrative changes + keep /auth/login links
- Footer GitHub link: change `StuartF303/Sorcha` → `Sorcha-Platform/Sorcha`

## Section Narrative (Business-First Order)

1. **Nav** — Sorcha brand, links: Why, How, Sectors, Toolkit, Developers, Help, Sign In (GitHub Pages: disabled login, links to hosted service)
2. **Hero** — "Collaborate Across Boundaries" headline, business-focused subtitle about the pain of sharing data across orgs, CTA buttons
3. **The Problem** (NEW) — "Without Sorcha" vs "With Sorcha" comparison table. Pain points: trust, fraud, over-disclosure, no audit trail, single point of failure
4. **Benefits** (NEW) — 4 outcome cards: Provable Actions (anti-fraud), Privacy by Design, Tamper-Proof Records, Resilient by Default
5. **DAD Security Model** — Reframed as "How Sorcha Protects You" with business language first
6. **Open Standards** — Reframed as "Built on Open Trust Frameworks"
7. **Quantum-Safe** — Reframed as "Future-Proof Your Records"
8. **Sectors** (RESTRUCTURED) — Each sector as: problem → why Sorcha → benefits. Supply Chain, Healthcare, Financial Services, Education, Government, Consortium
9. **The Toolkit** (NEW) — Blueprints (the IP), AI Blueprint Builder, Visual Designer, HD Wallets, Verifiable Credentials
10. **How It Works** — Keep 4-step flow
11. **Technology** — Keep tech grid
12. **Developers** (NEW) — GitHub repo, Issues, OpenAPI docs, Getting started, MIT license
13. **CTA** — Business-focused call to action
14. **Footer** — Updated links including GitHub repo (corrected), OpenAPI docs

## Diagrams & Graphics

All inline SVG/CSS — no external dependencies:
- Comparison table (HTML/CSS) for "Without vs With Sorcha"
- Benefit cards with inline SVG icons (reuse existing icon style)
- Sector cards enhanced with problem/solution/benefit structure
- Toolkit cards with descriptions
- Developer links section with GitHub/API icons

---

### Task 1: GitHub Pages Infrastructure

**Files:**
- Create: `docs/site/CNAME`
- Create: `docs/site/.nojekyll`
- Create: `.github/workflows/gh-pages.yml`
- Create: `docs/site/favicon.png` (copy)

- [ ] **Step 1: Create docs/site directory with CNAME and .nojekyll**
- [ ] **Step 2: Copy favicon.png from wwwroot**
- [ ] **Step 3: Create GitHub Pages deployment workflow**
- [ ] **Step 4: Commit infrastructure**

### Task 2: Restructured Landing Page HTML

**Files:**
- Create: `docs/site/index.html`

Rewrite the entire page with business-first narrative order:
- [ ] **Step 1: Write nav + hero + problem comparison section**
- [ ] **Step 2: Write benefits + DAD model sections**
- [ ] **Step 3: Write standards + quantum-safe sections**
- [ ] **Step 4: Write sectors section (problem/solution/benefit per sector)**
- [ ] **Step 5: Write toolkit + how-it-works sections**
- [ ] **Step 6: Write developers + CTA + footer**
- [ ] **Step 7: Verify all internal anchor links work**

### Task 3: Updated Landing CSS

**Files:**
- Create: `docs/site/landing.css`

Add styles for new sections while preserving existing design system:
- [ ] **Step 1: Copy existing CSS as base**
- [ ] **Step 2: Add comparison table styles (.problem-section, .comparison-table)**
- [ ] **Step 3: Add benefits section styles (.benefits-section, .benefit-card)**
- [ ] **Step 4: Add restructured sector styles (.sector-card with problem/solution/benefit)**
- [ ] **Step 5: Add toolkit section styles (.toolkit-section, .toolkit-card)**
- [ ] **Step 6: Add developer section styles (.developer-section)**
- [ ] **Step 7: Update responsive breakpoints for new sections**

### Task 4: Landing JS + GitHub Pages Adaptations

**Files:**
- Create: `docs/site/landing.js`

- [ ] **Step 1: Copy existing JS and update animation selectors for new section classes**
- [ ] **Step 2: Verify smooth scroll targets match new anchor IDs**

### Task 5: Sync Blazor UI Landing Page

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/index.html`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/landing.css`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/landing.js`

- [ ] **Step 1: Sync HTML changes (keep /auth/login links for hosted version)**
- [ ] **Step 2: Sync CSS changes**
- [ ] **Step 3: Sync JS changes**
- [ ] **Step 4: Verify hosted landing page still works with docker-compose**

### Task 6: Commit, Push, PR, Merge

- [ ] **Step 1: Create feature branch, commit all changes**
- [ ] **Step 2: Push and create PR**
- [ ] **Step 3: Merge**
- [ ] **Step 4: Verify GitHub Pages deployment (may need repo settings configured)**

# T009 — Source narratives inventory for User Story 6

**Spec**: 117-ai-discoverability · **Task**: T009 · **Date**: 2026-05-02 · **Status**: 🚨 **Phase 8 blocker**

Inventory of the four planning-folder narratives required by T098–T101 (User Story 6). The spec's Assumption (line 304) is that "the four planning-folder narratives referenced by US6 exist and are publication-ready with minor adaptation." This audit tests that assumption.

## Result

**0 of 4 narratives present in the repository.** All four are missing.

| # | Narrative file | Required for | Status |
|---|---|---|---|
| 1 | `sorcha-architecture-narrative.md` | T098 → `docs/architecture.md` | ❌ Not found |
| 2 | `sorcha-openid4vc-mdl-integration.md` | T099 → `docs/openid4vc-haip-integration.md` | ❌ Not found |
| 3 | `sorcha-applicability.md` | T100 → `docs/applicability.md` | ❌ Not found |
| 4 | `sorcha-architecture-evaluation.md` | T101 → `docs/security-model.md` (synthesised with #3) | ❌ Not found |

Verification: a repo-wide `find` for each filename (and partial-match equivalents) returned zero hits, excluding `node_modules` and `.git`. The audit included `.specify/`, `.planning/`, `docs/`, `docs/superpowers/`, and the project root.

## What does exist (alternative authoring sources)

The good news: the repo has authoritative content for the same subjects, just not consolidated under those filenames.

| Topic | Available source(s) |
|---|---|
| Architecture | `CLAUDE.md` § Architecture · `docs/architecture.md` · `docs/reference/project-structure.md` · `.specify/constitution.md` |
| Strategic framing & voice | `docs/strategic-context.md` ✅ (exists; canonical per spec) |
| OpenID4VC + HAIP integration | Specs 094/097/098 in `specs/` · `.claude/skills/sorcha-architecture/SKILL.md` § verifiable credentials · `.claude/skills/verifiable-credentials/SKILL.md` |
| Applicability (DPP, trade finance, IPC-1782, municipal) | `walkthroughs/TradeFinance/`, `walkthroughs/AssuredIdentity/` (real implementations) · `docs/strategic-context.md` § Target Markets · individual feature specs |
| Security model | `.specify/constitution.md` § II Security First · spec 099 (System Register Genesis) · spec 113 (Storage Durability Audit) · spec 079 (Trust Hardening) · `docs/strategic-context.md` § Cryptographic Posture |

## Phase 8 mitigation

T098–T101 cannot proceed by adapting existing narratives. Three options:

1. **Author fresh against `docs/strategic-context.md` plus existing repo content** (recommended). The strategic-context document is already mandated as the voice source. The technical content is sourced from CLAUDE.md, the constitution, the existing `docs/reference/` files, the `.claude/skills/sorcha-architecture/` skill, and feature-specific specs. This is the path the spec already implies — the planning-folder narratives were a convenience source, not a hard dependency.
2. **Down-scope US6 to fewer documents.** Deliver `architecture.md` and `security-model.md` only, defer `openid4vc-haip-integration.md` and `applicability.md` to a later spec. Lower delivery, easier scope.
3. **Author the four planning narratives first as a separate prep task, then run T098–T101 against them.** Highest fidelity to the original spec assumption but adds a phase.

**Recommendation: option (1).** The fresh-authoring path is no more effort than adapting four planning narratives that don't exist, and it keeps the strategic-context voice consistent across all four documents from the start. Update the T098–T101 task descriptions during Phase 8 kick-off to read "**Authored against `docs/strategic-context.md` and the listed repo-content sources**" rather than "Source: planning-folder `<file>.md`".

## Recommended adaptation effort (revised)

Under option (1):

| Doc | Expected effort | Notes |
|---|---|---|
| `docs/architecture.md` (T098) | Medium | Substantial existing content in CLAUDE.md + docs/architecture.md to lift. |
| `docs/openid4vc-haip-integration.md` (T099) | Medium-heavy | Specs 094/097/098 are dense and need synthesising into a single coherent narrative. |
| `docs/applicability.md` (T100) | Medium | Walkthroughs (TradeFinance, AssuredIdentity) provide the worked examples; rest is strategic-context. |
| `docs/security-model.md` (T101) | Heavy | Synthesises constitution + multiple specs; honest-gaps section needs care to land accurately. |

## Action

- Spec / plan should be updated to acknowledge the missing narratives. The Assumption on spec.md:304 needs softening: "If a narrative is missing or unsuitable, the implementation phase will surface it and either find an alternative source or down-scope the document" already covers this — no spec edit strictly required, but T098–T101 task descriptions should be updated when Phase 8 kicks off (not now — preserve task-numbering stability).
- **Phase 8 is not blocked by this audit's findings**, contrary to the audit-status flag at the top: option (1) is a viable path. The 🚨 marker reflects "the assumption underlying T098–T101 is wrong"; the consequence is recasting the source rather than gating delivery.

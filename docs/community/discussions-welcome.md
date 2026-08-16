<!--
  This file is the version-controlled SOURCE for a pinned GitHub Discussions post.
  It is not itself rendered anywhere — a maintainer with repo admin access copies the body
  below into a new Discussion (category: General or Q&A) and pins it, after confirming the
  four categories referenced below (Q&A, Ideas, Show-and-tell, Feedback) exist under the
  repository's Discussions settings.

  Keep this file in sync if the pinned post is edited in place on GitHub — it should always
  be possible to reconstruct the live post from this file.
-->

# Welcome — how to give feedback, what we're looking for, what's demo-grade

Thanks for trying Sorcha. This post explains where to put feedback, what kind of feedback is
most useful right now, and what to expect from the platform's current state before you start
poking at it.

## Where things go

- **Bugs and feature requests → [Issues](https://github.com/Sorcha-Platform/Sorcha/issues).**
  Something didn't work the way the docs said it would, a workflow broke, an endpoint returned
  the wrong thing — file it as an issue. See [`CONTRIBUTING.md`](../../CONTRIBUTING.md) → "Issue
  Reporting" for what makes a bug report or feature request actionable.
- **Open-ended stuff → Discussions**, in one of four categories:
  - **Q&A** — "how do I…", "why does X work this way", "is Y supported". If your question turns
    out to reveal a real bug or gap, we'll spin it out into an issue from there.
  - **Ideas** — feature proposals, architecture suggestions, "what if Sorcha did…". Doesn't need
    to be fully worked out — half-formed ideas are welcome.
  - **Show-and-tell** — built something with Sorcha? A blueprint, an integration, a demo? Post it
    here. This is genuinely one of the most useful categories for a pre-release project — seeing
    what real usage looks like tells us more than almost anything else.
  - **Feedback** — general impressions, rough edges, "this confused me," naming/terminology
    critique, anything that doesn't fit a bug report but is still worth us hearing.
- **Security vulnerabilities → do NOT use Issues or Discussions.** Use GitHub's private
  vulnerability reporting (Security tab → "Report a vulnerability"). See
  [`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md) for what's in scope and what to expect.

## What we're looking for

Sorcha is pre-release, hardening toward production. The single most valuable kind of feedback
right now is **"I ran X and it didn't do what the docs said"** — a live-execution defect, not a
theoretical one. This project's own experience has repeatedly found real bugs that a fully green
test suite missed, because the defect lived at a seam between two individually-correct pieces
(a mapping, a serialisation shape, a timing assumption) that no single test exercised end to end.
If you hit one of those, that report is gold — please include exactly what you ran and what
happened, not just what you expected.

Also genuinely useful, roughly in order of how much we can act on it fast:

1. A setup step that didn't work as documented (see [`docs/quickstart.md`](../quickstart.md) —
   if something there is wrong, that's a fast, high-value fix).
2. An AI agent (yours, via MCP) that got confused or blocked — see the
   [external-agent MCP quickstart](../guides/mcp-agent-quickstart.md). Agent-usability feedback
   is a first-class use case for this project, not an afterthought.
3. Terminology or documentation that reads as internal-only or unexplained to someone with zero
   context on the codebase.
4. Anything that looks like a security or trust-model gap — see the "in scope" list in
   [`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md) before deciding whether it's a public Discussion/Issue or a
   private security report.

## What's demo-grade right now

Before you conclude something is broken, it's worth knowing what's genuinely still rough versus
what's an intentional current limitation. **Read
[`docs/reference/maturity-and-limitations.md`](../reference/maturity-and-limitations.md) first** —
it lays out, in plain language, what's production-shaped (the cryptography, the ledger model,
fail-fast storage durability) versus what's an open, named gap (governance-key custody, relaxed
default rate limits, replication requiring an explicit subscription, no in-place schema upgrade
path yet). If your finding is already named there, it's not a surprise to us — but a Discussion
or Issue with a concrete repro is still useful, because "confirmed independently, here's exactly
how" is worth more than the abstract description on that page.

If you're testing against the shared public sandbox at `n1.sorcha.dev`: it's genuinely public,
shared with other testers, and periodically wiped. Don't put real data or secrets through it —
see [`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md) for the full scope statement.

## Thanks

However small — a typo report, a confused "why does this work this way," a fully-reproduced
live bug — it's useful. Pre-release software gets better from exactly this kind of feedback loop,
and we'd rather hear about a rough edge from you now than have it be someone's first bad
impression later.

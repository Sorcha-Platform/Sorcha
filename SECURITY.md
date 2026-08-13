# Security Policy

Sorcha is pre-release software. This document explains what's in scope for a security report, how to
report a vulnerability responsibly, and what to expect once you do.

## Supported scope

Sorcha is **pre-release demo software** — hardening toward production, not yet production-hardened.
There is no supported release branch with a backport policy; `master` is the only line that receives
fixes.

**`n1.sorcha.dev` is a shared public sandbox, not a production deployment.** It exists so anyone can
try the platform without standing up their own stack. Specifically:

- **Do not run production secrets, real personal data, or anything sensitive against n1.** Treat every
  credential, wallet, and register you create there as disposable.
- **Do not treat data on n1 as private.** Other testers — and the maintainer, for operational reasons —
  can see it. n1 is periodically wiped and re-genesised; nothing there is durable.
- Findings that only affect n1's *operational* state (e.g. "the demo data looks stale") are not security
  reports — open a regular issue instead.

Findings that **are** in scope: anything that lets one participant read, alter, or destroy data they are
not authorised to; anything that breaks the DAD guarantees (Disclosure / Alteration / Destruction)
described in [`docs/security-model.md`](docs/security-model.md); authentication or authorization
bypasses; signing-key or secret exposure; and supply-chain issues in the platform's own code or
dependencies.

## Reporting a vulnerability

**Preferred channel: GitHub private vulnerability reporting.**

Go to the repository's **Security** tab → **Report a vulnerability**
(<https://github.com/Sorcha-Platform/Sorcha/security/advisories/new>). This opens a private draft
security advisory visible only to you and the maintainer — nothing is public until a fix is ready and
you and the maintainer agree to disclose.

Please do **not** open a public GitHub issue for a suspected vulnerability. Use the private advisory
flow above so the report isn't visible to other users before there's a fix.

<!-- MAINTAINER: confirm/enable private vulnerability reporting in repo settings, or add a disclosure email here -->

### What to include

- What you found and why it matters (which DAD guarantee it breaks, what an attacker gains).
- Steps to reproduce — ideally against a throwaway register/org on n1 or a local Docker stack, not
  against real data.
- Any relevant logs, request/response payloads, or a minimal script.

A strong example of the kind of report that's valuable here is issue **#1397** — an external,
internet-reachable signing oracle found via live probing rather than unit tests. Reports that show the
issue actually being exploitable (not just theoretically possible) are the most useful.

## Response expectations

This is a small, mostly solo-maintained project. Response is **best-effort**, not SLA-backed:

- Acknowledgement: typically within a few days.
- Triage and a rough sense of severity/timeline: typically within a week or two, depending on
  complexity.
- Fix timeline depends on severity and what else is in flight — a critical, actively-exploitable issue
  gets prioritised over everything else; a lower-severity design gap may be tracked as an issue and
  fixed on the normal development cadence.

If you haven't heard back in two weeks, it's fine to follow up on the same advisory thread.

## Disclosure

Coordinated disclosure is the default: please give the maintainer a reasonable window to ship a fix
before any public write-up. Once a fix is released, the advisory is published (with credit, if you want
it) so other users understand what changed and why.

## See also

- [`docs/security-model.md`](docs/security-model.md) — the platform's cryptographic evidence model and
  its honestly-named gaps (including known ones like [#1380](https://github.com/Sorcha-Platform/Sorcha/issues/1380)).
- [`docs/reference/maturity-and-limitations.md`](docs/reference/maturity-and-limitations.md) — what's
  production-shaped versus demo-grade right now.

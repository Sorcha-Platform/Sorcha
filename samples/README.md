# Samples

Code in `samples/` is **application-specific**. These artifacts exist to demonstrate what an integrating consumer of the Sorcha platform looks like — they are NOT part of the platform.

## The boundary rule

> **Application-specific = out (here). Shared infrastructure = in (`src/`).**

Driver: deployment realism. The demo is only honest if a council page deploys the way a real council page would deploy — a separate runtime, calling Sorcha over the public API only. Full rationale in [`docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`](../docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md).

## Constraints (CI-enforced)

Every csproj under `samples/`:

- **MAY** `ProjectReference` `Sorcha.UI.Components.User` — the published consumer-facing component library.
- **MAY** `ProjectReference` libraries intended for third-party consumption (`Sorcha.ServiceClients`, `Sorcha.CitizenWallet.Abstractions`, `Sorcha.Blueprint.Models`, etc.). If a real council would NuGet-install a library, samples can `ProjectReference` it.
- **MUST NOT** `ProjectReference` anything else under `src/Apps/Sorcha.UI/` (e.g. `Sorcha.UI.Core`, `Sorcha.UI.Web.Client`). These are internal-only.
- **MUST** build to its own container image and ship its own `docker-compose.yml` overlay file (NOT a service block in the root `docker-compose.yml` — samples are opt-in, not part of the platform deliverable).

CI grep gate (`scripts/check-samples-references.ps1`, wired into [`samples-boundary-check.yml`](../.github/workflows/samples-boundary-check.yml)) fails the build on a forbidden reference.

## What lives here

| Folder | Purpose | Container image | Run |
|---|---|---|---|
| [`strathcarron-portal/`](./strathcarron-portal/) | F127 demo — Strathcarron Council citizen portal hosting the Driving Licence form (F126) and Blue Badge form (F127). Reference for "what does a council consumer of Sorcha look like?" | `sorcha-sample-strathcarron-portal` | `docker compose -f docker-compose.yml -f samples/strathcarron-portal/docker-compose.yml up -d` |

## What does NOT live here

- Operator scripts that exercise the platform from outside (these are scripts, not deployables) → `walkthroughs/`.
- Platform-side library components / services / endpoints → `src/`.
- Sorcha-branded citizen surfaces (the wallet PWA, the verifier desk, any future "MySorcha" Web surface) → `src/Apps/`.

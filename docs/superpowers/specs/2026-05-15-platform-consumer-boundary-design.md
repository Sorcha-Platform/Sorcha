# Platform vs. Consumer Boundary — Design

**Date:** 2026-05-15
**Status:** Design locked. Cross-cutting rule that applies to all current and future Strathcarron citizen-arc specs (and beyond).
**Umbrella:** [`2026-05-13-strathcarron-citizen-arc.md`](2026-05-13-strathcarron-citizen-arc.md)
**Forward-amends:** [`2026-05-15-spec-4-credential-gated-second-service-design.md`](2026-05-15-spec-4-credential-gated-second-service-design.md) (§1, §5, §12, §13)

## Why this doc exists

Closing out Feature 126 (Spec 3 of the Strathcarron citizen arc) the operator flagged that the MyStrathcarron portal — and by extension, any council-branded citizen-facing page — is not supposed to be built into or hosted within Sorcha. Sorcha is platform infrastructure: registers, validators, wallet service, tenant service, the shared `Sorcha.UI.Components.User` Razor component library, the reference wallet PWA, the reference verifier desk. A council portal is a *consumer* of that platform, not part of it.

This was raised as a retroactive concern about already-shipped F126 work (the example council page `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor` is on the wrong side of the line) and a forward concern about Spec 4's locked design (which carries the same in-tree pattern in its worked example) and Spec 5 (where the MyStrathcarron portal is the central deliverable).

This doc captures the rule, the build-topology mechanism that enforces it, and the cleanup plan. It is the cross-cutting architectural decision; Specs 4 and 5 honour it, and future specs reference it rather than relitigate it.

## §1 — The rule

> **Application-specific = out. Shared infrastructure = in.**

Driver: **deployment realism.** The demo is only honest if the council page deploys the way a real council page deploys — a separate runtime, calling Sorcha over the public API only.

The rule resolves four lenses (who hosts it / how it calls Sorcha / whose brand it wears / whether it's tenant-specific) into a single test a contributor can apply:

> If the artifact exists to serve one specific application or tenant, it lives outside `src/`. If it's shared infrastructure consumed across many, it lives inside.

Enforcement is **build-topology**, not convention. Outside-the-line artifacts live in `samples/`, build to their own container image, ship their own `docker-compose` entry, and **must not** add a `ProjectReference` into `src/Apps/Sorcha.UI/` beyond the consumer-facing library `Sorcha.UI.Components.User`. They consume the user-facing library the same way a third-party council deployment would. If a build wires it differently, the line has been crossed and CI fails.

## §2 — Where each artifact sits

Applying the rule to the current codebase:

| Artifact | Side | Notes |
|---|---|---|
| `Sorcha.UI.Components.User` (EnrolGate, HybridQrAffordance, CredentialGate-to-be, ConsentSheet, ReviewSummaryRenderer, IdCardLayout, picker) | **In** | Shared infrastructure. The whole point of this library is third-party consumability. |
| `ITierProbeService`, `IEnrolPairingSignal`, `IPresentationSignal` (Spec 4), `IEnrolSessionRedeemer` | **In** | Library services. Tenant-agnostic. |
| `Sorcha.Wallet.Pwa` | **In** | Sorcha's reference citizen surface. Tenant-agnostic; receives credentials from any issuer. |
| `Sorcha.UI.Web.Client` (admin, designer, explorer, Sorcha-branded citizen Web surfaces) | **In** | Charter narrows — see §5. |
| `Sorcha.Verifier` (verifier desk) | **In** | Sorcha's reference verifier surface. Tenant-agnostic. Third-party-verifier *demos* are different — those are samples. |
| `walkthroughs/Strathcarron/` PowerShell seeders + blueprint JSON | **In** | Scripts that exercise the platform from outside. Not deployables. Stay in `walkthroughs/` because they're operator tooling, not citizen-facing UI. |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor` | **Out (currently misplaced)** | The F126 stand-in. Moves to `samples/strathcarron-portal/` in Spec 4 PR-A. |
| Spec 4's Blue Badge page | **Out** | Ships directly in `samples/strathcarron-portal/`. |
| Spec 5's MyStrathcarron portal | **Out** | The portal IS the consumer artifact. Owned by `samples/strathcarron-portal/`. |
| Third-party verifier demos in Spec 5 (parking enforcement, refuse, concessionary travel) | **Out** | Each gets its own `samples/<vendor>-*/` artifact OR they share `samples/strathcarron-portal/` as embedded vendor screens. Granularity deferred to Spec 5's brainstorm. The rule is set; only the granularity is open. |

## §3 — `samples/` build topology

The mechanism that enforces "out = out":

```
samples/
└── strathcarron-portal/
    ├── Sorcha.Sample.StrathcarronPortal.csproj   # Blazor WASM host (sample picks framework)
    ├── Dockerfile
    ├── Program.cs
    ├── Pages/
    │   ├── DrivingLicence.razor                  # moved from src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/
    │   └── BlueBadge.razor                       # new in Spec 4
    ├── Components/
    │   ├── CouncilHeader.razor                   # plausible council chrome
    │   └── CouncilFooter.razor
    ├── wwwroot/
    │   └── css/                                  # council styling, not Material defaults
    └── README.md                                 # "this is a sample consumer; treat it as a reference for council integrators"
```

**csproj constraints (CI-enforced):**

- `ProjectReference` or `PackageReference` to `Sorcha.UI.Components.User` — **allowed**. This is the published consumer surface; samples consume it exactly as third-party councils will.
- `ProjectReference` to anything else under `src/Apps/Sorcha.UI/` (e.g. `Sorcha.UI.Core`, `Sorcha.UI.Web.Client`) — **forbidden**. A grep gate in CI on `samples/**/*.csproj` fails the build if it sees one.
- `ProjectReference` to libraries intended for third-party consumption (`Sorcha.ServiceClients`, `Sorcha.CitizenWallet.Abstractions`, etc.) — **allowed**. The principle: if a library is intended for third-party consumption, samples consume it the same way; if it's internal-only, samples cannot.

**Container & compose:**

- New image `sorcha-sample-strathcarron-portal` published by CI alongside the existing service images.
- New service `strathcarron-portal` in `docker-compose.yml`, depending on `gateway` (which fronts Sorcha's APIs).
- Local dev only in Spec 4 / Spec 5. **No n1 deployment** — that's operator-owned work blocked on new domain / services. Success in Spec 4 is "runs locally as a separate container against the rest of the docker-compose stack."

**Chrome and content:**

Realism extends to chrome. The sample isn't a bare `<MudContainer>` around a form: it carries plausible council scaffolding — header with Strathcarron Council logotype, a "Services / About / Contact us" nav, footer with council address and accessibility links, neutral page styling that doesn't read as Material Design defaults. Spec 4 PR-A delivers enough chrome to land "this is a council site" on first glance; Spec 5 deepens the IA when MyStrathcarron lands.

**Auth model the sample uses against Sorcha:**

Samples authenticate against Sorcha as **third-party API consumers** — public OIDC / federated identity / API-key shape, whatever a real council would use. Samples do NOT reach into internal auth contracts. This forces Sorcha to expose a real third-party-integrator authentication path, which is also useful platform pressure. Concrete mechanism is Spec 5's brainstorm; the rule is "nothing internal-only."

**What the rule does NOT mandate:**

- Same UI framework as Sorcha.UI.Web (samples can be Razor Pages, MVC, even a different framework). Spec 4 picks Blazor WASM because it lets the same library components light up unchanged.
- Same theming or visual language. Council samples are encouraged to look different — that's the realism payoff.

## §4 — Spec 4 amendment

The locked Spec 4 design doc (`2026-05-15-spec-4-credential-gated-second-service-design.md`) is edited in place:

**§1 "What ships" — restated:**

Spec 4 ships **two artifacts**:
1. **Platform-side:** the credential-gated blueprint contract (`prerequisites.presentationRequests`), the two new Blueprint Service endpoints, the `CredentialGateComponent` library component in `Sorcha.UI.Components.User`, the `Sorcha.Verifier.Engine` server-side wiring.
2. **Consumer-side:** the Blue Badge page hosted by `samples/strathcarron-portal/`. The PR-A extract of the F126 driving-licence page into that sample is the structural prerequisite.

**§5 "Library component growth" — composition example reframed:**

The Razor composition tree is unchanged; the host is reframed. The example now reads "here's what the consumer's page in `samples/strathcarron-portal/Pages/BlueBadge.razor` looks like" rather than implying an in-tree host. A one-line note added: the library is intentionally designed so the consumer's Razor file is the same shape it would be in a third-party deployment.

**§12 "Success criteria" — add SC-4-007:**

> SC-4-007: `samples/strathcarron-portal/` builds and runs as a standalone container against the rest of the docker-compose stack. Its csproj contains no `ProjectReference` into `src/Apps/Sorcha.UI/` other than `Sorcha.UI.Components.User`. CI grep gate enforces.

**§13 "Open items for plan-phase" — rewrite #1, add #4:**

- #1 unchanged: `CredentialGateComponent` lives in `Sorcha.UI.Components.User`.
- New #4: Spec 4 PR-A scope = extract F126 page to `samples/strathcarron-portal/`, set up csproj + Dockerfile + compose entry, baseline council-shape chrome (header, nav, footer). PR-B onward adds Blue Badge in the sample.

## §5 — `Sorcha.UI.Web.Client` charter refresh

**Charter (post-cleanup):**

> `Sorcha.UI.Web.Client` hosts Sorcha-branded platform surfaces only — admin, designer, explorer, configuration, and any Sorcha-branded citizen Web surfaces (wallet web counterpart, history, profile, devices). It does not host tenant-named or council-named pages.

**Immediate consequence:**

The `/strathcarron/services/driving-licence` route disappears from `Sorcha.UI.Web.Client` when Spec 4 PR-A lands. The new home is `samples/strathcarron-portal/Pages/DrivingLicence.razor`, routed at the sample's own `/services/driving-licence` (or whatever Spec 4 picks — the sample owns its IA).

**Reconciling with F125 umbrella invariant #7:**

The umbrella's invariant #7 ("PWA and Sorcha.UI.Web are co-equal citizen surfaces") is preserved, not reframed. It always referred to *Sorcha-branded* citizen surfaces — the PWA holds Sarah's credentials in mobile form; the Web client holds them in desktop form. Council-branded forms were never the subject of #7. F126's `/strathcarron/*` routes were a category error, not a fulfilment of #7.

This doc adds a one-word clarification to invariant #7's wording in the umbrella (`PWA and Sorcha.UI.Web are co-equal **Sorcha-branded** citizen surfaces`) so a future reader does not make the same mistake.

**What stays in `Sorcha.UI.Web.Client` long-term:**

Admin / designer / explorer (already covered by the audience-tag convention in `Sorcha.UI.Components.User/README.md`), plus a Sorcha-branded citizen Web surface that currently is mostly hypothetical (MyDevices / MyAuthMethods / MyProfile pages exist but are admin-flavoured). Spec 5 may grow this; Spec 5's brainstorm decides whether "MySorcha" is a thing the platform ships.

## §6 — Spec 5 sketch

Just enough to start `/speckit.specify`; Spec 5's brainstorm owns the detail.

**Hosted shape:**

- MyStrathcarron portal = pages added to `samples/strathcarron-portal/` (the sample created in Spec 4 PR-A). Not a second sample.
- Third-party verifier demos (parking enforcement, refuse contractor, concessionary travel) — granularity deferred. Two shapes Spec 5 picks between:
  - **One sample per vendor** (`samples/heatherbank-verifier/`, etc.). Most realistic; each verifier is operationally independent. Higher setup cost per vendor.
  - **One umbrella `samples/strathcarron-ecosystem/`** with sub-routes for each vendor. Cheaper; trades a measure of realism for shared chrome.

The rule (§1) is satisfied either way.

**Local-only deployment:**

Spec 5 ships its samples to `docker-compose.yml` for local dev. No n1 deployment in Spec 5. When the operator stands up `strathcarron.<something>` (and any vendor subdomains), n1 cutover is a separate operator-owned PR after both specs are done.

**Auth:**

Same rule as §3: samples authenticate as third-party API consumers. Spec 5 picks the concrete shape (OIDC against Sorcha's tenant service? Cookie-bridge? API-key for service-to-service?). Spec 5's brainstorm.

**What Spec 5's brainstorm still owns (out of scope for this boundary doc):**

- Multi-credential home layout in the PWA.
- Cross-service activity log.
- Recovery flow.
- MyStrathcarron portal IA (home, my services, my credentials, account).
- Production `IIssuerKeyResolver` (the cryptographically-honest verifier-isn't-issuer story).
- Vendor sample granularity (see above).
- Whether `Sorcha.UI.Web.Client` grows a "MySorcha" citizen Web surface.

## §7 — Memory and umbrella propagation

The brainstorm produces three artifacts beyond this design doc:

1. **New memory file** `feedback_platform_consumer_boundary.md` recording the rule + driver + how-to-apply. Index entry added to `MEMORY.md`.
2. **One-word edit** to the umbrella (`2026-05-13-strathcarron-citizen-arc.md`) — invariant #7 gets the word *Sorcha-branded* added so it can't be misread again.
3. **Architectural-flag line** in `MEMORY.md > Current Branch` is cleared — the flag is codified as a rule, no longer open.

## §8 — Success criteria for the boundary work itself

| ID | Criterion | Verify |
|---|---|---|
| **SC-B-001** | A contributor presented with a new file can decide "Sorcha repo or sample?" using the §1 rule alone, without referring back to this doc. | Operator test — read the rule, classify three test artifacts (a new EnrolGate sub-component, a Heatherbank verifier page, a new admin dashboard widget), check correctness. |
| **SC-B-002** | CI fails when a `samples/**/*.csproj` adds a forbidden `ProjectReference`. | Grep gate test; assert it catches a deliberate violation. |
| **SC-B-003** | After Spec 4 PR-A lands, `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor` no longer exists and `samples/strathcarron-portal/` builds and runs as a standalone container against docker-compose. | Build + manual walk. |
| **SC-B-004** | F125 umbrella invariant #7 carries the *Sorcha-branded* qualifier. | Doc grep. |

## §9 — Open items for plan-phase

The implementation plan (next step after this brainstorm) lays out:

1. Branch / PR sequence for Spec 4 PR-A (the structural extract). Likely standalone PR — *not* bundled with the credential-gating work, because the extract is risky enough to want clean review.
2. CI grep gate implementation (where it runs, what message it surfaces).
3. The minimal council chrome (header + nav + footer) — design pass within Spec 4 PR-A scope.
4. Docker-compose plumbing (port, hostname, dependency edges).
5. Spec 4 design doc in-place edits (§1, §5, §12, §13).
6. Umbrella one-word edit + MEMORY propagation.

## References

- Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`
- Spec 4 design (to be amended): `docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md`
- F126 stand-in page (to be moved): `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor`
- Shared component library: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/README.md`
- F126 walkthrough seeder: `walkthroughs/Strathcarron/setup-cold-start-demo.ps1`

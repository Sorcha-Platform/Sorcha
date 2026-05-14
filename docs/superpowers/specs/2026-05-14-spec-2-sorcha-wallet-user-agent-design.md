# Spec 2 — Sorcha Wallet (Full User-Agent v1)

**Date:** 2026-05-14
**Status:** Design locked — awaiting plan-phase.
**Umbrella:** [`2026-05-13-strathcarron-citizen-arc.md`](2026-05-13-strathcarron-citizen-arc.md)
**Supersedes:** [`2026-05-10-user-agent-unification-design.md`](2026-05-10-user-agent-unification-design.md) — that doc's "Direction agreed" and "Open questions for v1" sections are resolved here.
**Spec 1 precedent:** [`2026-05-13-spec-1-assured-identity-on-pwa-design.md`](2026-05-13-spec-1-assured-identity-on-pwa-design.md)

## Purpose

Take today's credential-only Citizen Wallet PWA and grow it into the **Sorcha Wallet** — the in-pocket end-user agent for *anyone* with a Sorcha identity, paired with the desktop web shell. The wallet absorbs the user-facing surfaces that today only live on `Sorcha.UI.Web` (form submission, persona, transaction history, devices/auth settings), adds a brand-new capability (citizen-as-verifier doorstep verification), and resolves the cross-shell architecture decisions parked in the 2026-05-10 user-agent-unification design.

This is the second sub-spec of the Strathcarron citizen arc. Spec 1 (Feature 124) delivered the welcome takeover and the SorchaLocalWallet target audience for credential delivery. Spec 2 is the foundation everything later in the arc — Spec 3's enrol-inside-wizard seam, Spec 4's credential-gated second service, Spec 5's MyStrathcarron portal — depends on.

## Decisions captured (brainstorm summary)

The 2026-05-14 brainstorm settled ten decisions. Each is restated below as a locked premise of this spec.

| # | Decision | Where in spec |
|---|----------|---------------|
| 1 | **Naming**: `Sorcha.Citizen.Wallet` → `Sorcha.Wallet.Pwa`; `Sorcha.Citizen.Verifier` → `Sorcha.Verifier`. URL stays `/wallet/`. Positioning: "Sorcha Wallet — your credentials, applications, evidence, identity in one place." | §3 |
| 2 | **Managed-mode = v1 default.** Today's hybrid (server-anchored holder key, browser-local device key, delegation in the middle) is formalised as managed mode. Sarah never sees a BIP39 phrase; recovery = sign in on a new device + re-enrol. | §4 |
| 3 | **Self-custody opt-in deferred to v2.** Real users who need BIP39-on-device land it via a future spec, paired with co-signed v2. | §4, §16 |
| 4 | **`IUserSigner`-style seam in v1.** Library exposes one signing abstraction; only the managed implementation lands now. Self-custody slots in later without UI rewrites. | §4 |
| 5 | **Scope = full user-agent v1.** PWA absorbs form submission, photo/file upload, persona, transaction history, devices/auth settings. Verify capability added (see #9). | §6, §7, §8, §9 |
| 6 | **Role-neutral copy, multi-persona-capable demos.** Wallet copy never says "Hi Sarah" or "your council application"; it says "Welcome back" and "your application." Demos exercise multiple personas (citizen, industrial worker). | §5, §11 |
| 7 | **Multi-context UI = active chip with peek.** Persistent context chip in header showing the current org/persona; quiet "+ N credentials in another context" footer below content; tap either to switch. | §5 |
| 8 | **Home IA = multi-section dashboard with hero Present + hero Verify** above Needs-attention / Credentials / Recent bands. One tap to present (or two if there's a choice). Same for verify. | §5 |
| 9 | **Verify is verify** — one capability with multiple shells. Wallet PWA gets a Verify Home action that runs the same OID4VP pipeline `Sorcha.Verifier` runs today. Citizen-at-door, council-clerk-at-counter, parking-officer-in-field — all the same engine, different shells. | §6 |
| 10 | **Demo narrative carries three beats**: doorstep verification, application-from-phone, context switching. Spec 2 designs around all three rather than picking one headline. | §2 |

## §2 — What ships

Three demoable beats anchor the spec. The supporting cast (transaction history, devices, persona, novice-user polish) ships alongside but isn't structured as a separate "beat."

### Beat 1 — Doorstep verification

> Sarah's elderly neighbour Margaret has a Caledonian Water engineer at the door. Margaret opens the Sorcha Wallet on her phone, taps **Verify**, scans the engineer's QR code. The wallet shows a green trust panel: *"Liam Buchanan, Water Engineer, Caledonian Water. Badge issued 2025-11-04, valid until 2027-11-04. Identity confirmed."* Margaret lets him in.

**Why it matters:** Inverts the credential conversation. Sorcha goes from "manages my stuff" to "protects me from doorstep scams." Genuinely differentiating — no comparable demo exists in the project. Resonates with public-sector audiences who care about safeguarding.

### Beat 2 — Application from phone

> Sarah has just enrolled her wallet (Spec 1's flow). She opens it, sees the dashboard, taps her recently-issued Assured Identity. The wallet suggests *"Start a Driving Licence application — uses your verified identity automatically."* Sarah taps. The form appears on her phone (5 pages), her persona auto-fills name and address, page 3 asks for a portrait — she taps the camera button, takes a selfie, the wallet auto-resizes and embeds it. Page 5 reviews. She submits. The wallet's Home now shows *"Application in review"* in the Needs-attention band.

**Why it matters:** Proves the PWA replaces the web form for citizens who prefer their phone. Exercises form rendering + portrait capture + persona autofill + state machine integration in a single flow. Tightest follow-on to Spec 1's headline.

### Beat 3 — Context switching

> A construction worker, Ben, has the same Sorcha Wallet Sarah does. He has two memberships: Personal (his own credentials) and Caledonian Builders Ltd (his employer). At home in the morning he taps the context chip → Personal → checks his Assured Identity. On site at midday he taps the chip → Caledonian Builders → his Home now shows site-safety certs and a "Submit incident report" application. Same wallet, two personas, no confusion about which one is active.

**Why it matters:** Proves the PWA is for anyone on the move, not just citizens. Resolves the 2026-05-10 doc's open question about role-flexibility. Sets up Spec 4 (credential-gated second service) and Spec 5 (third-party verifiers) by making clear that multiple personas can live in one wallet.

### Supporting cast (no separate beat, but ship in Spec 2)

- **Transaction history** — Wallet surfaces issuance + presentation history via the existing `TransactionDetailDrawer` / `ReceiptProofCard` / `TransactionLifecycleTicks` from the shared library.
- **Devices & auth settings** — `MyDevices` page surface (currently web-only) becomes wallet-native. Auth-method management (passkey / social / email-password) from Feature 116 lifts over.
- **Novice-user polish** — Empty-state-with-CTA across surfaces. Onboarding tooltips for non-obvious affordances. Error-recovery scaffolds. Guided tour on first wallet load after enrolment.

## §3 — Naming & rename mechanics

### Rename impact

| Today | New |
|---|---|
| `Sorcha.Citizen.Wallet` (project) | `Sorcha.Wallet.Pwa` |
| `Sorcha.Citizen.Wallet.Tests` | `Sorcha.Wallet.Pwa.Tests` |
| `Sorcha.Citizen.Verifier` | `Sorcha.Verifier` |
| `Sorcha.Citizen.Verifier.Tests` | `Sorcha.Verifier.Tests` |
| Namespace `Sorcha.Citizen.Wallet.*` | `Sorcha.Wallet.Pwa.*` |
| Namespace `Sorcha.Citizen.Verifier.*` | `Sorcha.Verifier.*` |
| Container image `sorchadev/citizen-wallet` | `sorchadev/wallet-pwa` |
| Container image `sorchadev/citizen-verifier` | `sorchadev/verifier` |
| Docker-compose service `sorcha-citizen-wallet` | `sorcha-wallet-pwa` |
| Docker-compose service `sorcha-citizen-verifier` | `sorcha-verifier` |
| Existing `Sorcha.Wallet.Service` | **unchanged** — server-side wallet service keeps its name |
| URL `/wallet/` | **unchanged** — no user-visible URL break |
| User-visible app name "Citizen Wallet" | **"Sorcha Wallet"** |

The URL stays `/wallet/` because changing it would invalidate every installed PWA and bookmark. The user-visible name (in `<title>`, manifest, header chrome, copy) changes to "Sorcha Wallet."

### Code-rename strategy

A single PR handles the rename mechanically. The cross-cutting touchpoints:

- Project file renames + `.sln` updates.
- `using Sorcha.Citizen.Wallet.*` → `using Sorcha.Wallet.Pwa.*` across all consumers (tests, ServiceClients references).
- DI registrations in `Sorcha.AppHost` and docker-compose service definitions.
- Documentation: README, CLAUDE.md feature references, sorcha-architecture skill, sorcha-ui skill, F114/F124 spec docs (footnote the rename rather than rewrite history).
- The `data-testid` selectors from PR #701 stay unchanged (`enrol-device-button`, etc.) — they're decoupled from the project name.

### Positioning copy

The wallet's tagline (in onboarding, README, the install prompt): **"Sorcha Wallet — your credentials, applications, evidence, identity in one place."** Stated capabilities (in the Settings → About surface): credential hold, presentation, doorstep verification, application submission, evidence capture, identity management.

No reference to "citizen" in user-facing copy. Demos still feature citizen scenarios; the surface itself is role-neutral.

## §4 — Architecture seams

### `IUserSigner` abstraction

Today's `Sorcha.Citizen.Wallet.Services.IDeviceKeyService` signs locally on the device using WebCrypto. That works for managed mode (the holder key derives server-side; the device key is the local half of the delegation chain). For self-custody (deferred to v2), the user would hold the holder key locally too, derived from a BIP39 seed.

We don't ship self-custody in v1, but we **do** ship the abstraction that lets it land later without rewriting consuming UI. The seam:

```csharp
public interface IUserSigner
{
    /// <summary>The custody mode this signer implements.</summary>
    UserCustodyMode CustodyMode { get; }

    /// <summary>The user-visible label for the active signing identity.</summary>
    string DisplayLabel { get; }

    /// <summary>
    /// Signs a payload under the current user/context identity. May require user
    /// consent — typically routed through an IConsentChallengeService surface
    /// in the UI before the cryptographic operation runs.
    /// </summary>
    Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct);
}

public enum UserCustodyMode
{
    Managed,         // v1 default — Wallet Service holds holder key, device key local
    SelfCustody,     // v2 — holder key local from BIP39, no server custody
    CoSigned         // v2 backlog — collector + org dual signature
}
```

**v1 implementations**:
- `ManagedUserSigner` (production) — derives the device-key locally, calls Wallet Service for holder-side signing under the active context's identity. Wraps the existing `IDeviceKeyService` + the holder-side delegation flow.

**v2 implementations** (carved out, not in v1):
- `SelfCustodyUserSigner` — local-only, no server round-trip for signing. Recovery via BIP39.
- `CoSignedUserSigner` — Key A on device, Key B server-mediated, both required.

**Consumers don't see the difference.** `ConsentSheet`, `PresentationSubmitDialog`, action-submission flows — all of them depend on `IUserSigner`, never on the concrete implementation. When self-custody lands the only changes are the DI registration and an opt-in flow in Settings.

### Context-aware signing

Sarah-personal and Sarah-as-industrial-worker have different keys. The active context determines which `IUserSigner` instance is injected at signing time. The wallet's `IUserContext` service exposes the active context; `IUserSigner` resolves based on it. Switching context invalidates any in-flight signing operation and prompts the user to re-confirm under the new context.

### Library growth — what's added to `Sorcha.UI.Components.User`

| Component | Purpose | New / refactor |
|---|---|---|
| `ContextChipSwitcher` | The active-chip-with-peek surface from §5. Used by both PWA and (eventually) the web shell when it grows multi-context. | New |
| `VerifyHomeAction` | Hero Verify card on Home. Wraps camera/QR scanning + presentation request flow. | New |
| `PresentHomeAction` | Hero Present card on Home. One-tap-to-present (or two with picker). | New |
| `NeedsAttentionBand` | Surfaces pending applications, expiring credentials, action-needed items. | New |
| `RecentActivityFeed` | Time-ordered feed of recent issuance / presentation / verification events. | New |
| `GuidedTourScaffold` | Reusable first-time tour primitive. Steps, dismiss-persistence, ARIA-friendly. | New |
| `ErrorRecoveryScaffold` | Standard error display with retry / alternate-path affordances. | New |
| `EmptyStateWithCta` | Empty state + suggested action. Refactor of today's `EmptyState`. | Refactor |
| `IdCardLayout` | Already shared. Spec 2 adds proper claim disclosures + issuer org branding to the body (Spec 1 deferred this). | Enhance |
| `MyDevices` page surface | Today web-only via `Sorcha.UI.Web`. Lifts into the library. | Migrate |
| `MyAuthMethods` page surface | Feature 116 (passkeys / social / email-password). Lifts into the library. | Migrate |
| `TransactionHistoryFeed` | Wraps the existing `TransactionDetailDrawer` + `ReceiptProofCard` + `TransactionLifecycleTicks` as a top-level history view. | New (composition) |
| `PortraitCaptureControl` | Native-camera capture for `x-file.capture: "user"` + `embedAs` resize. Mobile-camera-first; falls back to file-upload on desktop. | New |

The pattern remains: **`Sorcha.UI.Components.User` is the contract**. Pages in the PWA and the web shell are thin wrappers that compose library components with surface-specific layout.

## §5 — Home IA & multi-context UI

### Home structure

```
┌────────────────────────────────────────────┐
│  ◌ Personal ▾                              │  context chip (always visible)
├────────────────────────────────────────────┤
│ ┌─────────────────────┐ ┌────────────────┐ │
│ │ 🪪 Present          │ │ 🔍 Verify       │ │  hero actions — two taps to use
│ └─────────────────────┘ └────────────────┘ │
├────────────────────────────────────────────┤
│  Needs attention                           │  conditional — only renders
│  ⏳ Driving Licence application in review   │  when there's something
├────────────────────────────────────────────┤
│  Your credentials  (3) →                   │  summary band — taps into list
├────────────────────────────────────────────┤
│  Recent                                    │
│  ↗ Presented to Caledonian Water · 2 days  │
│  ✓ Driving Licence issued · 4 days         │
├────────────────────────────────────────────┤
│  + 1 credential in Caledonian Builders     │  the peek footer
└────────────────────────────────────────────┘
       ⌂        📱        ⏱        ⚙           footer nav (Home/Devices/Activity/Settings)
```

### Section behaviour

| Section | When visible | Content rule |
|---|---|---|
| Context chip | Always | Active context name; arrow indicates switchable |
| Hero Present | Always (if user has ≥0 credentials) | Tap → if exactly 1 credential, opens it for presentation; if multiple, opens the credential picker |
| Hero Verify | Always | Tap → opens camera/QR scanner for inbound presentation |
| Needs attention | Conditional | Visible iff there's a pending application, expiring credential, action-required notification, or revocation alert. Empty → section omitted. |
| Your credentials | Visible if user has ≥1 credential in active context | Summary count; tap → full credentials list. If zero credentials, replaced by an `EmptyStateWithCta` band: *"No credentials yet — submit a council application to get started"* |
| Recent | Visible if there's at least one recent event in the last 30 days | Last 3-5 events; tap → full Activity page |
| Peek footer | Visible iff user has memberships in other contexts with content | Quiet hint of what's elsewhere. Tap → context switcher |

### Context switcher flow

Tap context chip → bottom sheet slides up with:

- Current context indicated by checkmark
- All other contexts listed with their content summary ("Caledonian Builders Ltd — 2 credentials, 1 pending application")
- "Personal" always present even if the user hasn't acted on it
- Tap a context → wallet content refreshes to that context; chip updates; bottom sheet closes
- Persona auto-fill rebinds to the per-context persona (each context can have its own persona — Personal Sarah has personal address, work-Sarah uses the construction firm's office address)

### Per-context content scoping

When the active context is "Personal":
- Credentials: only those issued to Sarah's Personal context
- Presentation picker: only personal credentials
- Activity log: only events in personal context
- Persona auto-fill: personal persona
- New-application surfaces: only show services available to the Personal context

When the active context is "Caledonian Builders Ltd":
- Credentials: only those issued under Ben's employee identity
- Presentation picker: only employer-context credentials (site-safety, training certs)
- Activity log: only employer-context events
- Persona auto-fill: employer persona (work email, office address)

The wallet's `IUserContext` service exposes the active context to every consuming component. Switching context triggers a render refresh; in-flight signing operations are cancelled with a "switched context — please retry" notice.

### Why this shape

Multi-section dashboards work across the user's whole journey: empty wallet (a single hero Present is dimmed, hero Verify is active, Needs-attention shows the welcome; everything else collapses), mid-state (one credential, some recent activity), busy wallet (multiple credentials, multiple pending applications, multi-context). Familiar pattern (Gmail, banking apps). Each band can grow without forcing a rewrite.

Hero Present + Verify satisfies the **one-tap-max-two** UX bar from the brainstorm: someone asks for Sarah's ID, she opens the wallet, hero Present is right there, one tap if there's one credential (the common case), two if she has multiple. Same for verify.

## §6 — Verify capability in the wallet

### The flow

1. **Hero Verify tap** opens the camera/QR scanner full-screen.
2. **Scan or NFC tap** of the presenter's QR / NFC card. The QR carries an OID4VP presentation offer (the engineer's wallet exposes their credential as a verifiable presentation, signed by their holder→device chain).
3. **Wallet generates an ephemeral verifier identity** for this verification session — a per-session `client_id` for audience-binding. No registration with the platform required (the verifier role is anonymous; the verification itself doesn't require platform-side credentials).
4. **Wallet sends the presentation request** to the presenter's wallet (either via the QR's response URL or a direct connection over NFC).
5. **Presenter's wallet responds** with the verifiable presentation containing the requested claims.
6. **Wallet runs the same verification pipeline** `Sorcha.Verifier` runs today:
   - Verifies the credential JWT against the issuer's signing key (`IIssuerKeyResolver`)
   - Verifies the holder→device delegation chain
   - Checks the status list for revocation
   - Validates claim values, presentation freshness (nonce, timestamp)
7. **Result displayed** via `VerificationTrustView` (already in the shared library):
   - Green panel + "Identity confirmed" when all checks pass
   - Yellow/expanded panel when any check warns (e.g., status list expired, signature kid mismatch)
   - Red/expanded panel when any check fails

### Ephemeral verifier identity

The verifier identity used per session:
- Generated client-side as a fresh EC P-256 key
- `client_id` = the public-key thumbprint
- Lifetime = single verification (discarded after the result is displayed)
- Not registered with the platform; the trust model is "the presenter trusts the verifier by visual confirmation that the right person is doing the verification, not by checking a registry"

This matches the doorstep scenario: Margaret-the-elderly-homeowner has no organisational identity, isn't a registered verifier, and shouldn't need to be. The verification protocol still works because OID4VP doesn't require the verifier to prove anything — only the presenter does.

### Reused components

Everything below the UI layer is reused from `Sorcha.Verifier`:

- `IIssuerKeyResolver` (production hardening path — DID-resolved, not `OptOutIssuerKeyResolver`)
- The verifiable presentation validator (`VerifiablePresentationValidator`)
- The status list check (`CitizenStatusListChecker`)
- `VerificationTrustView` (display)

### What's new

- A wallet-side QR/NFC scanner with mobile-camera-first UX
- The ephemeral-verifier-identity generator
- The wallet Home → Verify entry point
- A "verification history" section (kept by the wallet for the user's records — *"I verified Liam Buchanan, Water Engineer, on 2026-05-14"*; under each entry, the user can re-display the trust result)

### Out of scope for v1

- Persistent verifier sessions (mid-flow pause and resume)
- Multi-credential sessions in one verification interaction
- "Saved trusted parties" feature (verified-this-engineer-before recall)

These are valid `Sorcha.Verifier` extensions but not needed for the doorstep beat. Future spec.

## §7 — Application from phone

### The flow

1. Sarah opens the wallet (managed-mode, signed in via session, no prompt).
2. Home shows her recently-issued Assured Identity in Credentials band, and **"Recommended for you — Driving Licence application (uses Assured Identity)"** in Needs-attention.
3. Tap → form opens in the wallet. The form is rendered by `SorchaFormRenderer` (already shared — already used by `Sorcha.UI.Web`).
4. Pages 1-3 of the application form load — `SorchaFormRenderer` already supports the schema, `PersonaAutofillResolver` auto-fills name/dob/address, the existing `x-review` extension renders page 5 as the ID-card review.
5. Page 4 asks for a portrait. The `PortraitCaptureControl` opens the device camera (Feature 107 `x-file.capture: "user"` + `embedAs: "image-token-jpeg-240x320"`). Sarah takes a selfie, the wallet client-side resizes it to the token, embeds it.
6. Page 5 reviews her details as a draft id-card (`IdCardLayout` with `Watermark = Draft`).
7. Sarah submits → the wallet calls the blueprint instance creation + action submission endpoints under her managed-mode identity. The submission flow is identical to `Sorcha.UI.Web`'s: only the shell differs.
8. Home updates: pending-application notice fires (the F124 mechanism), the Needs-attention band shows "Driving Licence application in review."

### What lifts over from Sorcha.UI.Web

Already shared (no migration needed):
- `SorchaFormRenderer`
- All field renderers (`TextLineRenderer`, `DateTimeRenderer`, `FileRenderer`, etc.)
- All layout renderers (`CategorizationRenderer`, `GroupRenderer`, etc.)
- `ReviewSummaryRenderer` + `IdCardLayout`
- `PersonaAutofillResolver`
- `PersonaFillSummary`
- `CredentialGatePanel`

What needs to migrate (PWA-incompatible bits):
- The web-shell's `_Imports.razor` directives that the form renderer relies on — verify these are all in the PWA's `_Imports.razor` already (they are; Feature 122 work).
- The mobile-camera variant of `FileRenderer` — today the web app uses an HTML file input. The PWA needs `PortraitCaptureControl` to wrap the camera flow.

### What's new

- `PortraitCaptureControl` in the shared library — full-screen mobile camera capture, in-browser canvas resize to 240×320 JPEG, on-screen retake button. Falls back to file upload on desktop.
- The wallet's blueprint-instance navigation wiring (today only the web shell knows how to navigate "Home → Find services → Apply for X → start an instance").

### IUserSigner integration

Action submission requires signing the transaction with the user's identity (under the active context). The wallet's submission path resolves `IUserSigner` for the active context, calls `SignAsync` with the action payload, and submits. The managed-mode signer routes the signing operation through `Sorcha.Wallet.Service` server-side under Sarah's holder identity. From the consuming UI's perspective: one call, one result. No mode-awareness needed.

## §8 — Transaction history & receipts

### The flow

Sarah taps the **Activity** footer-nav button → `TransactionHistoryFeed` page. Each entry is:

- An icon indicating event type (issuance, presentation, verification-of-someone-else, revocation, status update)
- One-line summary ("Driving Licence issued by Acme Licensing")
- Timestamp + lifecycle tick state (grey / blue / double-blue per Feature 079)
- Tap → detail drawer (`TransactionDetailDrawer`)

Detail drawer shows:
- Full transaction metadata
- `ReceiptProofCard` if a receipt exists (Feature 079 trust hardening)
- Status list lookup result if revocation was checked
- A "Verify this transaction" affordance — re-runs the inclusion proof + receipt verification on demand

### What's reused

All components exist in the library today, used by `Sorcha.UI.Web`'s transaction explorer:

- `TransactionLifecycleTicks`
- `TransactionDetailDrawer`
- `ReceiptProofCard`
- `VerificationTrustView`

### What's new

- `TransactionHistoryFeed` — a wallet-shaped, mobile-first feed wrapper. The web shell already has a transaction explorer surface; the wallet's feed is a sibling design optimised for thumb-scroll.
- Per-context filtering: history is scoped to the active context.

### Receipt freshness

The wallet polls for receipt updates on visible entries every 30s while the Activity page is open (matching the existing web shell's polling cadence). On hub-push events (`TransactionConfirmed`), the feed updates immediately.

## §9 — Devices & auth settings

### The flow

Sarah taps the **Devices** footer-nav button → her enrolled devices list. Today this lives at `/devices/` in the PWA already (Feature 114). Spec 2 lifts the equivalent web-shell pages from `Sorcha.UI.Web` into the library so both surfaces share the same `MyDevices` and `MyAuthMethods` components.

`MyDevices`:
- List of enrolled devices for the active `PlatformUser` (cross-context — the user's devices aren't context-scoped, they're per-user)
- Each device: label, platform, enrolled date, delegation expiry, status
- Actions: rename, revoke (existing `ConfirmRevokeDialog` from PWA-local; lifts into the library)
- "Lost my phone" copy + recovery action ("Sign in on another device and revoke from there")

`MyAuthMethods`:
- Email + password (Feature 116)
- Passkeys list — add/remove (Feature 116)
- Linked social accounts — add/remove (Feature 116)
- Recovery: each method is a way back into the account if another is lost

### Settings IA

The Settings footer-nav button leads to a Settings hub with sections:

- **Profile** — display name, persona management (Feature 092's `MyProfile` lifted in)
- **Devices** — link to MyDevices
- **Auth methods** — link to MyAuthMethods
- **Contexts** — list of org memberships, link to organisation management on the web shell ("Add an organisation membership opens in the web app")
- **Notifications** — preferences for pushes
- **About** — version, capability list, legal links
- **Lock now** — biometric / passkey lock (future polish)
- **Sign out**

The novice-user tour points users at Profile and Devices first ("here's where your information lives; here's where you manage your devices").

## §10 — Adaptation rules — mobile vs desktop

### The principle

`Sorcha.UI.Components.User` components render in **both** the web shell and the PWA. Where they need to look or behave differently per form factor, they take adaptation parameters — they don't fork.

### Concrete adaptations

| Component | Mobile (PWA) | Desktop (web shell) | Mechanism |
|---|---|---|---|
| `CredentialCardList` | Single-column list, thumb-friendly tap targets | Multi-column grid (2-3 columns) | `Layout="Layout.Grid \| Layout.List"` parameter, defaults inferred from viewport |
| `PresentationRequestDialog` | Bottom sheet (slides up from bottom) | Centred modal dialog | `Variant="DialogVariant.Sheet \| DialogVariant.Modal"`, MudBlazor's responsive dialog handles this |
| `TransactionDetailDrawer` | Full-screen takeover with back button | Right-aligned drawer | `Variant` parameter |
| `IdCardLayout` | One per row, full-width | Two per row in grid mode | Grid container handles arrangement |
| `ContextChipSwitcher` | Bottom sheet for picker | Dropdown menu for picker | `PickerVariant` parameter |

### Form-factor detection

Components read `MediaQueryService.IsMobile` (already used in `Sorcha.UI.Web` for responsive behaviour) and apply sensible defaults. Adaptation parameters override defaults when the caller wants explicit control (e.g., a presentation kiosk on a tablet might want sheet-style dialogs even though the form factor is large).

### What this rules out

- Forking components per form factor (today's anti-pattern — see #698 where the PWA reimplemented credential listing inline rather than calling `CredentialCardList`).
- Surface-specific styling overrides that drift from the library defaults.
- Components that only work in one shell.

If a component genuinely can't adapt (e.g., the doorstep verifier camera flow is mobile-only), it's documented as mobile-only and the library exposes it that way. The web shell either falls back gracefully (shows a "scan with your phone instead" message) or hides the affordance.

## §11 — Novice-user UX bar

### Guided tour on first wallet load

After enrolment (Spec 1's flow completes), the wallet's first Home render includes a `GuidedTourScaffold` overlay:

- **Step 1**: highlight the hero Present action — *"Tap here when someone asks to see your credential."*
- **Step 2**: highlight the hero Verify action — *"Tap here to check someone else's credential — useful for doorstep callers."*
- **Step 3**: highlight the context chip — *"You're currently in your Personal context. Tap here to switch to another organisation."*
- **Step 4**: highlight the footer nav — *"Devices, your history, settings — all here."*
- Dismiss persists per device (similar to F124's `WelcomedAt` flag).

The tour fires only once per device. Sarah can replay it from Settings → About → Replay tour.

### Empty-state-with-CTA

Every band on Home, every page, every dialog has an explicit empty-state-with-CTA pattern via `EmptyStateWithCta`:

- Empty Credentials band: *"No credentials yet — submit a council application to get started"* with a CTA button linking to a placeholder for now (Spec 4's credential-gated second service will populate the real catalog).
- Empty Activity feed: *"Nothing recent — your activity will show here once you start using credentials"*.
- Empty Devices list: shouldn't happen post-enrolment, but if it does, show *"No devices enrolled — re-enrol to recover"* with the enrolment CTA.
- Empty Needs-attention: section is omitted entirely (no empty render).

### Error-recovery scaffolds

Every error surface uses `ErrorRecoveryScaffold`:

- Error title + plain-English description
- "What just happened" expandable section with technical detail (for support / debugging)
- Recovery action ("Try again" / "Sign in again" / "Contact support" depending on context)
- Never a bare exception message; always a recovery path

Examples:
- Verification fails (status list expired): "We couldn't reach the credential's registry. The credential might still be valid — try again in a moment, or ask the person to wait while you check on another network."
- Session expired during submission: "Your session expired. Sign in again and we'll bring you back to where you were."
- Camera permission denied: "The camera is needed to verify or capture evidence. Open your browser settings to grant camera access, or scan the QR code with a friend's phone."

### Onboarding tooltips for non-obvious affordances

A small `?` icon next to: hero Verify (first time), context chip (first time), portrait capture (first time). Tap → short tooltip explaining the feature. Persists dismissed state per affordance per device.

### Reading age + plain language

All copy aims for a Year-8 (~13-year-old) reading age. Capability descriptions favour action verbs and concrete nouns. Specifically avoid:

- Technical jargon ("credential," "claim," "presentation" only used when nothing else fits — and accompanied by a tooltip)
- Latin-derived institutional language ("authentication," "verification," "delegation")
- Conditional / passive constructions

Where jargon is necessary (e.g., on Settings → Auth Methods → Passkeys), a short hover/tap-to-explain is provided.

## §12 — Design tokens

### Today's state

The PWA and the web shell both use MudBlazor's default theme. There are no explicit Sorcha design tokens (typography scale, spacing scale, colour palette). `IdCardLayout` introduced an explicit `XReviewColourTheme` enum (`IdentityNavy`, `LicencePink`) for credential-card theming — that's the closest thing to a token system.

### What Spec 2 does

Spec 2 doesn't introduce a full design-tokens overhaul (that would be its own spec). It does:

- **Extend `XReviewColourTheme`** to cover the new contexts and credential types Spec 2 introduces.
- **Document the existing implicit tokens** — what spacings, font sizes, colours are in use across `Sorcha.UI.Components.User` — so future work has a baseline.
- **Define motion tokens** for the new transitions (context-switch animation, hero-action press feedback, guided-tour highlights). Stored in `welcome-takeover.css` (which from Spec 1 already hosts shared keyframes) — or migrated to a dedicated `sorcha-motion.css` if the file grows large.

### What's deferred

A full design-system tokens spec (typography scale, spacing scale, semantic colour palette, motion primitives) is a separate future spec. Spec 2's job is to use what's there consistently and document gaps, not redesign the foundation.

## §13 — Library coverage gaps

### New shared primitives Spec 2 adds

Listed in §4 — the table summarising what's added to `Sorcha.UI.Components.User`. Restated tightly:

- **Navigation/structure**: `ContextChipSwitcher`, `NeedsAttentionBand`, `RecentActivityFeed`, `TransactionHistoryFeed`
- **Hero actions**: `PresentHomeAction`, `VerifyHomeAction`
- **Patterns**: `GuidedTourScaffold`, `ErrorRecoveryScaffold`, `EmptyStateWithCta` (refactor of existing)
- **Capability**: `PortraitCaptureControl`
- **Migrations**: `MyDevices`, `MyAuthMethods`, `MyProfile` (lift from `Sorcha.UI.Web` into library)
- **Enhancements**: `IdCardLayout` (fuller body — claim disclosures, issuer org branding)

### PWA-local components that stay PWA-local

- `WelcomeTakeover` (Spec 1's first-credential ceremony — wallet-specific) — but keep an eye on it for Spec 4 if/when the second credential's arrival pattern needs to share its lineage.
- `WaitingCard` (Spec 1's skeleton placeholder — wallet-specific).
- PWA `Pages/` (route handlers — can't be shared by virtue of `@page` declarations).

### Coverage gaps in scope vs out

In scope for Spec 2: the gaps listed above.

Out of scope: a complete library audit of every minor MudBlazor reimplementation across the PWA. Spec 2 closes the gaps that the new capabilities introduce; future cleanup specs handle the rest.

## §14 — Testing strategy

### Unit tests

- `IUserSigner` abstraction: tests against a fake signer verify consuming components don't leak custody-mode awareness.
- `ContextChipSwitcher`: bUnit or Playwright component tests for context-scoping behaviour.
- New library components (`VerifyHomeAction`, `PresentHomeAction`, `GuidedTourScaffold`, `PortraitCaptureControl`): unit-level tests per component.
- Per-context content scoping rules: tests against fake `IUserContext` covering credential lists, persona resolver, history feed.

### Playwright E2E

Issue #700 Phase 2 closure lands as part of Spec 2:

- Post-redeploy cache test (Phase 2 of #700 — currently deferred).
- Auth-gated navigation tests (Phase 2 of #700 — Enrol Done → Home, credential-card tap-through, CredentialDetail nav buttons).

Spec 2-specific E2E:

- **Doorstep verification flow** — wallet scans a generated QR, sees the verification trust panel, dismisses. Catches regressions in the verify pipeline integration.
- **Application from phone** — wallet renders the form, persona autofills, portrait capture (uses a fixed-image test mode), submits. Catches regressions in form-renderer wiring + `IUserSigner` integration.
- **Context switching** — wallet enrols Sarah in two contexts, switches between them, asserts content swap (credentials, persona, history).
- **Verify cache-header test** — extends #702's regression guard with the new entry-point JS files (if any).

### Demo verification

Each of the three demo beats becomes a tagged Playwright test:
- `[Demo("doorstep-verify")]`
- `[Demo("application-from-phone")]`
- `[Demo("context-switching")]`

Tagged tests can be run as a group to verify all three beats land cleanly before any release. Equivalent to F124's `quickstart.md`-driven verification but automated.

### Regression coverage

Existing Spec 1 / Feature 124 tests (`CitizenWalletNavigationTests`, `CitizenWalletPushTests`, etc.) continue to pass after the rename. The rename PR includes a checklist verifying every existing test is updated for the new namespace.

## §15 — PR decomposition

Rough 6-PR sketch in dependency order. Plan-phase will refine.

| PR | Title | Scope |
|---|---|---|
| PR-A | Rename + library skeleton | `Sorcha.Citizen.Wallet` → `Sorcha.Wallet.Pwa` + `Sorcha.Citizen.Verifier` → `Sorcha.Verifier`. Library skeleton for new primitives (empty/stub components, DI registration). `IUserSigner` interface lands. All existing tests passing under new namespaces. |
| PR-B | Multi-context + Home IA | `ContextChipSwitcher`, `IUserContext`, context-scoped content rules. Home redesign: hero Present + hero Verify, Needs-attention/Credentials/Recent bands, peek footer. PWA's `Index.razor` rewritten to consume library components. |
| PR-C | Verify in wallet | `VerifyHomeAction`, camera/QR scanner, ephemeral verifier identity, verification pipeline reuse from `Sorcha.Verifier`. `VerificationTrustView` integration. Doorstep verification demo beat lands here. |
| PR-D | Application from phone | `SorchaFormRenderer` wired into the wallet's blueprint-instance flow. `PortraitCaptureControl` lands. `PersonaAutofillResolver` wired through wallet context. Application-from-phone demo beat lands here. |
| PR-E | History + Devices + Auth | `TransactionHistoryFeed`. `MyDevices`, `MyAuthMethods`, `MyProfile` migrate from `Sorcha.UI.Web` to library. Wallet Settings hub. Activity nav populated. |
| PR-F | Novice-user polish + docs | `GuidedTourScaffold` with first-time content. `ErrorRecoveryScaffold` adopted across surfaces. `EmptyStateWithCta` refactor. Tooltip overlays. Reading-age sweep across all wallet copy. Issue #700 Phase 2 closure (post-redeploy + auth-gated nav tests). Docs: skill updates (sorcha-architecture, sorcha-ui), API reference, memory. |

Each PR is independently mergeable and passes existing test suites; each ships behind no feature flags. Demo verification is the gate before each PR's merge — the existing test pattern from F124 (CI green + manual quickstart pass).

## §16 — Out of scope / deferred

Carved out from Spec 2; tracked for later specs.

- **Self-custody opt-in** (BIP39 on device, no server-side holder key) — v2. Likely paired with co-signed v2 in a future foundational spec.
- **Co-signed v2** (collector + org dual signature) — backlog. Already designed at high level in the 2026-05-10 doc.
- **`Sorcha.Verifier` consolidation** — keep the desk/counter shell as-is for v1. Future spec can decide whether to fold it into the wallet entirely or keep both shells.
- **Multi-credential / persistent verifier sessions** — Spec 2 ships single-shot doorstep verification only.
- **Full design-tokens overhaul** — typography scale, spacing scale, semantic colour palette, motion primitives as a system. Future foundational spec.
- **Field-vs-desk pairing UX** — handoff flows between the wallet (in-pocket) and `Sorcha.UI.Web` (desk) for the same user mid-task. Future spec, probably Spec 5 of the citizen arc or a sibling.
- **"Saved trusted parties"** — verify-this-engineer-before recall. Future verifier extension.
- **Spec 4-shaped credential-gated second service flow** — Spec 4 owns this; Spec 2 just makes sure the wallet's UI surface is ready for it.

## Success criteria

Spec 2 succeeds when:

1. **The three demo beats land cleanly.** Doorstep verification, application-from-phone, context-switching — each runnable as a quickstart demo (manual or Playwright), each producing the result described in §2.
2. **The wallet's pages consume `Sorcha.UI.Components.User` components.** No PWA-local reimplementation of `CredentialCard`, `CredentialDetailView`, `MyDevices`, `MyAuthMethods`. Pre-existing drift (#698 lineage) closed.
3. **No regression in existing test suites** post-rename. `Sorcha.Wallet.Service.Tests`, `Sorcha.Wallet.Pwa.Tests` (was `Sorcha.Citizen.Wallet.Tests`), `Sorcha.UI.E2E.Tests` all pass.
4. **Issue #700 Phase 2 closed.** Post-redeploy cache test + auth-gated nav tests in CI.
5. **Novice-user UX bar met.** First-time tour fires after enrolment; every surface has empty-state-with-CTA; every error path has recovery scaffold; reading-age sweep complete.
6. **`IUserSigner` seam is consumer-blind.** When a future v2 self-custody implementation lands, no consuming UI (ConsentSheet, presentation flow, action submission) requires changes.

## Open items for plan-phase

Carried forward, not litigated here:

- Exact wireframes for: bottom-sheet context switcher, full-screen doorstep verifier camera, on-screen portrait retake.
- Whether `GuidedTourScaffold` uses an existing tour library or is hand-rolled (lean toward hand-rolled to avoid bundle weight).
- Whether `PortraitCaptureControl`'s desktop fallback is the existing `FileRenderer` or a new sibling.
- Detailed schema for per-context persona separation (today's `MyProfile.razor` assumes single-context).
- Exact list of `Sorcha.UI.Web` pages migrating to library vs. staying web-shell-local for v1.

## References

- Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`
- Predecessor design (superseded by this): `docs/superpowers/specs/2026-05-10-user-agent-unification-design.md`
- Spec 1 design: `docs/superpowers/specs/2026-05-13-spec-1-assured-identity-on-pwa-design.md`
- Spec 1 implementation tag: `spec-124-complete` at `0b9f46ea`
- Feature 122 (shared user components library): `specs/122-shared-user-components/`
- Feature 123 (audience-tag convention): `specs/123-*/`
- Feature 124 (Spec 1): `specs/124-assured-identity-pwa/`
- sorcha-architecture skill: §"Citizen Wallet PWA (Feature 114)" + §"AssuredIdentity on the PWA (Feature 124)"
- sorcha-ui skill: §"Citizen Wallet PWA — path-prefix gotchas"
- VC UX design: `docs/superpowers/specs/2026-03-25-verifiable-credentials-ux-design.md`

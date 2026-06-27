# Feature Specification: Social Provider Brand Icons on Login & Signup

**Feature Branch**: `166-auth-social-brand-icons`

**Created**: 2026-06-27

**Status**: Draft

**Input**: User description: "Auth hardening A: social provider brand icons on login/signup buttons (web inline SVG + PWA Icons.Custom.Brands). Spec: docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md Workstream A"

## Overview

Today the social sign-in choices on Sorcha's authentication screens are text-only buttons ("Continue with Google", "Continue with Microsoft", etc.) on both the web app and the citizen wallet PWA. Without a recognisable brand mark, the choices are slower to scan, look unpolished, and don't match the visual conventions every mainstream sign-in screen uses. This feature adds the correct, recognisable brand icon to each social provider button on the login and signup surfaces so people can identify their provider at a glance.

This is **Workstream A** of the broader auth login hardening effort. It is intentionally scoped to the *visual treatment* of provider choices — it does not change which providers are offered, how authentication works, or any token/redirect behaviour.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recognise my provider at a glance on the web login (Priority: P1)

A returning person opens the web login screen. Each available social sign-in option shows the provider's official brand icon next to its label, so they immediately spot the provider they normally use and select it.

**Why this priority**: Login is the highest-traffic auth surface and the most common entry point. A recognisable icon directly speeds the most frequent task (choosing the right provider) and is the clearest, most visible win.

**Independent Test**: Load the web login page with one or more social providers configured; confirm each social button displays the matching brand icon alongside its text, the icon is legible, and selecting the button still starts the existing social sign-in flow unchanged.

**Acceptance Scenarios**:

1. **Given** the web login page with Google, Microsoft, GitHub and Apple configured, **When** the page renders, **Then** each social button shows that provider's brand icon to the left of its label.
2. **Given** a social button with a brand icon, **When** the person selects it, **Then** the same sign-in flow that runs today is triggered (no behavioural change).
3. **Given** only a subset of providers is configured, **When** the page renders, **Then** only the configured providers appear, each with the correct icon and no broken or placeholder images.

---

### User Story 2 - Recognise my provider at a glance on signup (Priority: P2)

A new person opens the web signup screen and chooses to register with a social provider. Each provider option in the social signup choice shows the provider's brand icon so they can confidently pick the account they want to register with.

**Why this priority**: Signup is lower volume than login but is a first impression for new users; consistent iconography across login and signup avoids a jarring mismatch.

**Independent Test**: Open the web signup social option with providers configured; confirm each provider choice shows the correct brand icon and that registration proceeds as it does today.

**Acceptance Scenarios**:

1. **Given** the web signup page social option, **When** it renders, **Then** each provider choice shows the matching brand icon next to its label.
2. **Given** the signup and login screens side by side, **When** comparing the same provider, **Then** the brand icon is visually consistent across both.

---

### User Story 3 - Recognise my provider in the citizen wallet PWA (Priority: P2)

A citizen opens the wallet PWA sign-in screen on their phone. Each social provider button shows the provider's brand icon, matching the look of the web experience so the two surfaces feel like one product.

**Why this priority**: The PWA is the citizen-facing companion surface; visual parity with the web app is important for trust, but it serves a narrower audience than the web login, so it sits just below the primary login story.

**Independent Test**: Open the PWA sign-in screen with providers configured; confirm each social button shows the correct brand icon rendered through the PWA's icon system, and that selecting a button starts the existing PWA social flow unchanged.

**Acceptance Scenarios**:

1. **Given** the PWA sign-in screen with providers configured, **When** it renders, **Then** each social button shows the matching brand icon as a leading icon.
2. **Given** the PWA passkey button already uses a leading icon, **When** comparing it to the social buttons, **Then** icon size, alignment and spacing are visually consistent.
3. **Given** a brand icon on a PWA button, **When** the button is selected, **Then** the existing PWA social sign-in flow runs unchanged.

---

### Edge Cases

- **Unknown / future provider**: If a configured provider has no defined brand icon, the button MUST still render with its text label and a neutral fallback mark (never a broken image, blank space, or error).
- **No providers configured**: When no social providers are configured, no social buttons render and no icon assets are requested — the screens behave exactly as they do today.
- **Icon legibility on theme/background**: Icons MUST remain legible against the button background in both light and dark presentation, including brand marks that are predominantly white or black (e.g. Apple, GitHub).
- **Small viewports**: On narrow mobile widths the icon plus label MUST not overflow, wrap awkwardly, or clip the icon.
- **Assistive technology**: The icon is decorative relative to the existing text label; it MUST NOT introduce duplicate, misleading, or empty announcements for screen-reader users (the existing accessible label remains the source of truth).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each social provider button on the web login surface MUST display the corresponding provider's recognisable brand icon adjacent to its existing text label.
- **FR-002**: Each social provider option on the web signup surface MUST display the corresponding provider's brand icon adjacent to its existing text label.
- **FR-003**: Each social provider button on the citizen wallet PWA sign-in surface MUST display the corresponding provider's brand icon as a leading icon, consistent with the existing passkey button treatment.
- **FR-004**: Brand icons MUST be provided for every currently supported provider: Google, Microsoft, GitHub, and Apple.
- **FR-005**: The set of providers shown MUST continue to be driven by existing provider configuration — this feature MUST NOT add, remove, or reorder providers, nor render a provider that is not configured.
- **FR-006**: Selecting any social button MUST trigger exactly the same authentication flow that exists today; this feature MUST NOT alter redirect, token, callback, or error behaviour.
- **FR-007**: When a configured provider has no defined brand icon, the button MUST fall back to a neutral generic mark and remain fully functional, with no broken or missing image.
- **FR-008**: Brand icons MUST be legible across the supported light and dark presentations of the auth screens.
- **FR-009**: Icon presentation (size, alignment, spacing relative to the label) MUST be visually consistent within each surface and reasonably consistent across the web and PWA surfaces.
- **FR-010**: Icons MUST NOT degrade accessibility: the existing text label remains the accessible name, and the icon MUST NOT produce duplicate or empty announcements for assistive technologies.
- **FR-011**: Each provider's icon MUST visually match that provider's official, widely recognised brand mark so users are not misled about which provider a button represents.

### Key Entities

- **Social Provider Choice**: A selectable sign-in/registration option representing one external identity provider. Attributes relevant here: provider key (e.g. `google`, `microsoft`, `github`, `apple`), human-readable label, and now an associated brand icon. Whether a choice appears is governed by existing configuration.
- **Brand Icon**: The recognisable visual mark for a provider, rendered inline on the web surfaces and through the PWA's icon system on the PWA surface. Has a defined fallback for providers without a specific mark.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of social provider buttons shown on the web login, web signup, and PWA sign-in surfaces display the correct brand icon for every supported provider (Google, Microsoft, GitHub, Apple).
- **SC-002**: Zero broken, missing, or placeholder-error icons appear for any configured provider across all three surfaces, including a configured provider with no specific brand mark (which shows the defined fallback).
- **SC-003**: A person can correctly identify their intended provider button by its icon alone (label hidden) for all four supported providers — verifiable in usability/visual review.
- **SC-004**: No change in authentication success or failure outcomes is observed before vs. after the change — the sign-in/registration flows complete exactly as they did previously.
- **SC-005**: Auth screens introduce no new accessibility violations attributable to the icons (icons are decorative; existing labels remain the accessible name).
- **SC-006**: Brand icons render legibly in both light and dark presentation on every supported surface, with no provider mark disappearing into the background.

## Assumptions

- The supported provider set for this feature is the four currently configured providers — Google, Microsoft, GitHub, and Apple — matching existing auth configuration. New providers added later inherit the fallback behaviour until a specific icon is defined.
- The web login and signup surfaces are server-rendered and require icons delivered as inline vector markup; the citizen wallet PWA renders through a component framework whose existing custom brand-icon set is the intended source for those buttons. (This split is the "web inline SVG + PWA Icons.Custom.Brands" intent of the request.)
- This workstream is **visual only** — it reuses the existing provider configuration, sign-in flows, token handling, and callback behaviour without modification.
- Provider ordering, labels, and the wording "Continue with …" remain as they are today; only the icon is added.
- Use of provider brand marks follows each provider's brand guidelines for sign-in buttons; this is treated as a design/asset concern handled during implementation, not a new legal/approval workflow for this spec.
- The canonical design document referenced in the request (`docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md`, Workstream A) was not present in the repository at spec-writing time; this spec is derived from the request text and the existing auth surfaces. If that design document is added, this spec should be reconciled against it.

# Phase 0 Research: Social Provider Brand Icons

All `NEEDS CLARIFICATION` items from Technical Context are resolved below. This feature is
visual-only; research focuses on icon delivery, legibility, accessibility, and fallback.

## R1 — Web icon delivery mechanism (Razor Pages)

**Decision**: Render decorative inline `<svg aria-hidden="true">` markup as a leading element inside
the existing `.social-btn`, produced by a server-side resolver `SocialProviderBrandIcon.For(string providerKey)`
returning an `HtmlString` (pre-trusted, author-authored SVG — no user data interpolated).

**Rationale**:
- The web auth surface is server-rendered Razor Pages (`Sorcha.Tenant.Service/Pages/Auth/*.cshtml`),
  not Blazor — so MudBlazor icon components are unavailable here. Inline SVG needs zero extra network
  requests, no external asset hosting, and renders identically regardless of CSS/JS load order.
- A resolver keeps the SVG strings in one testable place and gives `Login.cshtml` and `Signup.cshtml`
  an identical one-liner, guaranteeing login/signup consistency (FR-002, SC-001).
- `.social-btn` already uses `display:flex; align-items:center; gap:0.5rem`, so a leading SVG slots in
  with no layout rework.

**Alternatives considered**:
- *External SVG files / `<img>`*: rejected — extra requests, possible broken-image state (violates
  FR-007/SC-002), and harder to recolour for dark mode.
- *Icon font / emoji*: rejected — no faithful brand marks (FR-011); the current passkey emoji is a
  placeholder, not a precedent to extend for brand identity.
- *Sprite sheet (`<use href="#google">`)*: rejected as over-engineering for 4 static marks on 2 pages.

## R2 — PWA icon delivery mechanism (MudBlazor)

**Decision**: Map provider key → `Icons.Custom.Brands.{Google|Microsoft|GitHub|Apple}` and pass as the
existing `MudButton`'s `StartIcon`. Implement as a private `static string ProviderIcon(string)` switch
on `SignIn.razor`, mirroring the established precedent in
`Sorcha.UI.Components.User/Components/Security/SocialLinksSection.razor` (~L187-194).

**Rationale**:
- MudBlazor **9.5.0** (confirmed in `Directory.Packages.props`) ships all four brand marks under
  `Icons.Custom.Brands`, already used elsewhere in the repo — zero new assets.
- `StartIcon` is exactly how the passkey button renders its leading `Fingerprint` icon, so size,
  alignment, and spacing match for free (FR-003, PWA acceptance scenario 2).

**Alternatives considered**:
- *Inline SVG in the PWA too*: rejected — fights the framework; MudBlazor icons already give consistent
  sizing/theming and match the passkey button. The spec explicitly intends `Icons.Custom.Brands` here.

## R3 — Provider-key matching across surfaces

**Decision**: Both resolvers match **case-insensitively** on the provider key (`google`, `microsoft`,
`github`, `apple`).

**Rationale**: Web `GetConfiguredProviderNames()` returns capitalised names (`"Google"`); the PWA
`ISocialProvidersClient.GetConfiguredAsync()` returns lowercase (`"google"`). A case-insensitive switch
(normalise to lower-invariant) makes one vocabulary work on both surfaces and tolerates future config
casing drift. No change to the provider source on either side (FR-005, FR-006).

## R4 — Light/dark legibility (FR-008, SC-006)

**Decision**: Multi-colour marks keep official brand colour; monochrome marks adapt to theme.
- **Google**: official 4-colour "G" — fixed colours (legible on light/neutral button background).
- **Microsoft**: official 4-square logo — fixed colours.
- **GitHub**: monochrome Octocat/mark — `fill="currentColor"` so it follows the button text colour.
- **Apple**: monochrome apple — `fill="currentColor"` so it follows the button text colour.

**Rationale**: Predominantly-black marks (Apple, GitHub) vanish on a dark background and predominantly
white marks vanish on light; binding them to `currentColor` makes them track the existing label colour,
which the theme already keeps legible against the button background. Google/Microsoft are intrinsically
multi-colour and remain legible on the neutral `.social-btn` background in both presentations. On the
PWA, MudBlazor `Icons.Custom.Brands` marks render in the button's content colour (single-colour glyphs),
inheriting the same legibility guarantee.

**Verification anchor**: inspect actual `.social-btn` background in `auth.css` for light/dark during
implementation; if the button background is itself dark in dark mode, the `currentColor` choice is
confirmed correct. (Captured as a quickstart visual check rather than a blocking unknown.)

**Alternatives considered**:
- *Always-coloured official marks for all four*: rejected — black Apple/GitHub marks fail on dark mode.
- *White circular "chip" behind every mark (Google button guideline style)*: deferred — heavier visual
  change than the spec's "add the icon" scope; revisit only if review finds a legibility gap.

## R5 — Accessibility (FR-010, SC-005)

**Decision**: Icons are **decorative**. Web: `aria-hidden="true"` on the inline SVG (and no `alt`/`title`).
PWA: MudBlazor `StartIcon` renders an `aria-hidden` SVG by default; the button's visible text
("Continue with Google") remains the accessible name. No `aria-label` added that would duplicate text.

**Rationale**: The text label already names the provider; the icon must not introduce duplicate or empty
announcements. This matches the existing decorative-emoji `aria-hidden` pattern already in the auth pages.

## R6 — Unknown / future provider fallback (FR-007, SC-002)

**Decision**: A configured provider with no defined mark resolves to a neutral generic glyph, never a
broken image or blank.
- Web: resolver returns a neutral inline "globe/public" SVG (`currentColor`).
- PWA: switch `_ =>` arm returns `Icons.Material.Filled.Public` (matches `SocialLinksSection.razor`).

**Rationale**: Guarantees every configured button renders a functional, non-broken leading mark even
for providers added later before a brand mark is defined.

## R7 — Testing strategy

**Decision**:
- **Unit (xUnit + FluentAssertions)** on the web `SocialProviderBrandIcon` resolver: each supported key
  (any casing) → non-empty SVG containing an `<svg` and `aria-hidden`; unknown key → the neutral
  fallback; no key throws. This is the highest-value deterministic test and meets the >85% new-code bar.
- **Visual/behavioural (existing Playwright Docker infra)**: load web login/signup and PWA sign-in with
  providers configured; assert each social button shows a leading icon, no broken images, and that
  clicking still triggers the unchanged flow (SC-004). PWA provider→icon switch is trivial enough to be
  covered by the Playwright leading-icon assertion rather than a separate bUnit test.

**Rationale**: Concentrates assertable logic in one unit-testable resolver; uses the repo's standard
E2E path for the inherently visual claims (legibility, alignment, no-broken-image).

## Resolved unknowns summary

| Item | Resolution |
|------|-----------|
| Web delivery | Inline `aria-hidden` SVG via server-side resolver → `HtmlString` |
| PWA delivery | `Icons.Custom.Brands.*` as `MudButton.StartIcon`, fallback `Icons.Material.Filled.Public` |
| MudBlazor brand icons available? | Yes — 9.5.0, all four confirmed |
| Provider key casing | Case-insensitive match on both surfaces |
| Dark-mode legibility | Google/Microsoft fixed colour; Apple/GitHub `currentColor` |
| Accessibility | Decorative, `aria-hidden`; text label stays accessible name |
| Fallback | Neutral globe (web) / `Icons.Material.Filled.Public` (PWA) |
| Behaviour change | None — config, flow, tokens, callbacks untouched |

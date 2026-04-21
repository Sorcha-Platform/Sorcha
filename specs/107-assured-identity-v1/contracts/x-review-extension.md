# Contract: `x-review` schema extension + IdCardLayout

**Feature**: 107-assured-identity-v1
**Status**: Specification (implementation owned by Phase 1)

## Purpose

A blueprint schema extension that marks a wizard page as a **read-only review summary** of prior pages' submitted values. Renders via a parameterised layout component. Used identically for citizen-side draft review, assessor-side pending review, and (with `editable: false`) the issued credential's wallet detail view.

## Schema shape

Placement: as a sibling extension on a `type: object` page within a blueprint's `x-pages` list, alongside the existing `x-sections`, `x-introduction`, `x-width` extensions.

```jsonc
{
  "type": "object",
  "x-pages": [
    /* ... earlier wizard pages ... */
    {
      "title": "Review your details",
      "description": "What you'll receive once issued.",
      "x-review": {
        "layout": "id-card",
        "editable": true,
        "header": {
          "issuerName": "Acme Verification Co.",
          "credentialName": "Assured Identity",
          "colourTheme": "identity-navy"
        }
      }
    }
  ]
}
```

### Field schema

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `layout` | enum | yes | — | One of `id-card`, `passport-page`, `tabular`, `receipt`. v1 implements only `id-card`. Unknown values produce a publish-time warning and the renderer falls back to a tabular minimal display. |
| `editable` | bool | no | `true` | When true, the renderer generates an Edit button per detected section (sourced from prior pages' `x-sections` titles or page titles). |
| `header.issuerName` | string | yes | — | Rendered as "Issued by &lt;issuerName&gt;" in the card header |
| `header.credentialName` | string | yes | — | Rendered as the card type label |
| `header.colourTheme` | enum | no | `identity-navy` | Visual variant. v1 ships `identity-navy` (Assured Identity) and `licence-pink` (Driving Licence). |

## Parser changes (Sorcha.Blueprint.Models)

`SchemaLayoutParser` extended to recognise `x-review` on a page and emit `XReviewExtension`:

```csharp
public sealed record XReviewExtension(
    XReviewLayoutVariant Layout,
    bool Editable,
    XReviewHeader Header);

public sealed record XReviewHeader(
    string IssuerName,
    string CredentialName,
    XReviewColourTheme ColourTheme);

public enum XReviewLayoutVariant { IdCard, PassportPage, Tabular, Receipt }
public enum XReviewColourTheme { IdentityNavy, LicencePink }
```

Validation at parse time:
- `header.issuerName` and `header.credentialName` must be present and non-empty
- Unknown `layout` values logged at publish time as warning `WARN_BP_REVIEW_001` (no fail), renderer falls back

## Renderer dispatch (Sorcha.UI.Core)

`ControlDispatcher.razor` extended: if a page has `XReviewExtension` set, dispatch to `ReviewSummaryRenderer.razor` instead of the default form-page render path.

`ReviewSummaryRenderer.razor`:
- Reads the `XReviewExtension` from the page
- Builds an `IdCardLayoutConfig` (see below) by pulling all submitted values from `FormContext.FormData` for fields declared on prior pages
- Dispatches by `Layout` enum to the matching component (`IdCardLayout.razor` for `IdCard`)
- Generates Edit-X buttons (when `Editable: true`) wired to `Wizard.NavigateToPage(pageIndex)` while preserving form state

`IdCardLayout.razor` parameters:

```csharp
[Parameter] public required IdCardLayoutConfig Config { get; init; }
[Parameter] public EventCallback<int> OnEditSection { get; set; }
[Parameter] public RenderFragment? FooterActions { get; set; }
```

`IdCardLayoutConfig`:

```csharp
public sealed record IdCardLayoutConfig(
    string IssuerName,
    string CredentialName,
    XReviewColourTheme ColourTheme,
    IdCardWatermark Watermark,
    IReadOnlyDictionary<string, object?> FieldValues,
    IReadOnlyList<IdCardSection> Sections,
    bool Editable);

public enum IdCardWatermark { None, Draft, Pending, Issued }

public sealed record IdCardSection(
    string Title,
    int OriginatingPageIndex,
    IReadOnlyList<string> FieldPointers);
```

## State / context derivation

`Watermark` is **not** specified by the extension; it is derived by the renderer from the hosting action's runtime state:

| Action context | Watermark | Footer actions |
|---|---|---|
| Citizen on their own pre-submission review | `Draft` | Edit (per section) + Submit |
| Assessor on a pending application | `Pending` | Approve + Reject |
| Citizen viewing an issued credential in My Credentials | `Issued` | (none — read-only) |
| Citizen viewing a not-yet-issued credential offer | `None` | (none — preview only) |

Footer actions come from the hosting action's `routes` (existing blueprint pattern) — the extension does not redeclare them.

## Two-card stacked variant

For credential-chain workflows (e.g. licensing review of presented identity + licence-to-be), a single `x-review` extension on a single page can produce two stacked cards. The renderer detects this when the page also declares a `credentialRequirements` block (presented credential) AND a `credentialIssuanceConfig` block (credential-to-be):

- Top card: presented credential (claims pulled from the verified presentation context, withheld claims rendered as faded "— — —" with explanatory caption)
- Bottom card: credential-to-be (claims pulled from the action payload + the action's `credentialIssuanceConfig.claimMappings`)
- Each card uses its own `IdCardLayoutConfig`. Top card's theme defaults to `identity-navy` (verified state badge); bottom card uses the theme declared in `header.colourTheme` (e.g. `licence-pink` for the licence).

## CSS theming

Each `colourTheme` is a CSS custom-property set applied to the `IdCardLayout` root:

```css
.id-card[data-theme="identity-navy"] {
  --card-bg-start: #0b2545;
  --card-bg-mid: #13315c;
  --card-bg-end: #1b4b7a;
  --card-accent: #d4a017;
  --card-label: #a8c5e6;
}
.id-card[data-theme="licence-pink"] {
  --card-bg-start: #6d1b3c;
  --card-bg-mid: #8e2350;
  --card-bg-end: #ad2c63;
  --card-accent: #ffe5b4;
  --card-label: #f5c2d6;
}
```

Adding a new theme = adding one CSS block + one enum value.

## Acceptance

- A blueprint declaring `x-review: { layout: "id-card", editable: true, header: {...} }` on a page renders that page as a read-only ID card with all prior pages' values populated.
- Edit buttons (when `editable: true`) navigate to the originating page with form state intact.
- Same component renders Citizen draft (Draft watermark + Edit/Submit), Assessor pending (Pending watermark + Approve/Reject), and Wallet detail (Issued/no watermark + no actions).
- Two-card stacked variant produces presented + credential-to-be on a single review page when the action declares both `credentialRequirements` and `credentialIssuanceConfig`.

## Test surface

- `XReviewExtensionParserTests` — parses valid extensions; warns on unknown layout; rejects missing required fields
- `ReviewSummaryRendererTests` — dispatches by layout; pulls correct values; Edit buttons fire correct page index
- `IdCardLayoutTests` — applies correct CSS theme by enum; renders watermark per state; renders footer actions
- `ReviewSummaryDataSourceTests` — pulls field values from FormContext for prior-page fields only

# Contract — Credential Card Family

## Components

| Component | Source path | Target path |
|-----------|-------------|-------------|
| `CredentialCard.razor` | `Sorcha.UI.Core/Components/Credentials/` | `Sorcha.UI.Components.User/Components/Credentials/` |
| `CredentialCardList.razor` | same source | same target |
| `CredentialDetailView.razor` | same source | same target |
| `CredentialAcceptCard.razor` | same source | same target |
| `CredentialLifecycleDialog.razor` | same source | same target |
| `IssuanceSummaryPanel.razor` | same source | same target |
| `VerificationTrustView.razor` | same source | same target |

## Parameters (preserved verbatim)

- `CredentialCard.Credential` — the credential model to render
- `CredentialCard.OnSelected` — callback raised when the user activates the card
- `CredentialCardList.Credentials` — `IReadOnlyList<CredentialModel>` to render
- `CredentialDetailView.Credential` — model
- `CredentialAcceptCard.Credential` — `PendingAcceptance` credential to confirm
- `CredentialAcceptCard.OnAccepted` / `OnDeclined` — outcome callbacks

## Injected services

| Service | Owner | Registration |
|---------|-------|--------------|
| `ICredentialApiService` | `Sorcha.UI.Components.User/Services/Credentials/` | Library-registered via `AddSorchaUserComponents()` |
| `IQrPresentationService` | same | same |

The credential card itself is render-only and has no service injections; the surrounding family (`CredentialDetailView`, `CredentialAcceptCard`) inject the two services above.

## Callbacks

- `OnSelected` / `OnAccepted` / `OnDeclined` / `OnRevoked` — outcome callbacks raised to the host page. The host is responsible for navigating, refetching, or persisting after the callback.

## Host responsibilities

1. Call `AddSorchaUserComponents()` to register the credential services.
2. Provide credential models to the card via the `Credential` parameter — both shells already have wallet-side data sources that produce these models.
3. Handle outcome callbacks (e.g., navigate to the credential detail page on `OnSelected`).
4. For accept-card UX, the host owns the post-accept side-effect (refresh inbox, update local cache).

## Out of contract

- Credential issuance. Cards display existing credentials and let the user accept/decline pending ones; they do not initiate issuance.
- Cryptographic verification of the credential signature. That happens in the verifier or service layer; the trust badge surfaced by `VerificationTrustView` reflects an already-computed trust state passed in via parameter.
- Storage of the credential. The PWA's `ICredentialCache` and the web app's credential listing are host-owned.

## Verification

1. **Given** a credential model with three claims, **When** rendered through `CredentialCard`, **Then** the produced markup contains the issuer name, the credential type label, and the three claim labels — verified by an existing bUnit test (moved into the new test project).
2. **Given** the PWA host registers `AddSorchaUserComponents()` and provides a `Credential` parameter, **When** the PWA renders `CredentialDetailView`, **Then** the component instantiates without dependency-injection exceptions — verified during commit 3 (PWA integration proof).
3. **Given** a `PendingAcceptance` credential, **When** the user clicks Accept on `CredentialAcceptCard`, **Then** `OnAccepted` fires with the credential id — verified by bUnit interaction test.

# MAUI Blazor Credential Wallet UI

UI patterns for the credential wallet, scoped to render-mode-agnostic Razor components plus platform-specific services behind narrow interfaces. Read this when building or modifying anything under `Sorcha.UI.Core/Components/Credentials/`.

**Naming note.** The Wallet Service already has `Sorcha.Wallet.Service.Credentials.ICredentialUiStore` for server-side credential persistence. The UI-side interface must be called `ICredentialUiStore` to avoid collision — this is a client-side abstraction over `SecureStorage` / IndexedDB, *not* a repository.

## Architectural Rule

Razor components live in `Sorcha.UI.Core` and **must not** reference `Microsoft.Maui.*` or `System.Security.Cryptography.*` directly. Platform services are injected via interfaces and registered from the host project (MAUI vs WASM).

```
Sorcha.UI.Core/Components/Credentials/  ← Razor (InteractiveServer + InteractiveWebAssembly)
     │
     ▼ depends on
Sorcha.UI.Core/Services/Credentials/     ← ICredentialUiStore, IBiometricGate, IQrScanner
     │
     ▼ implemented by
Sorcha.UI.Maui/Services/                 ← MauiCredentialUiStore, MauiBiometricGate
Sorcha.UI.Web.Client/Services/           ← IndexedDbCredentialUiStore, WebCameraQrScanner
```

## Platform Service Interfaces

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Credentials;

public interface ICredentialUiStore
{
    ValueTask<IReadOnlyList<StoredCredential>> ListAsync(CancellationToken ct = default);
    ValueTask<StoredCredential?> GetAsync(string id, CancellationToken ct = default);
    ValueTask StoreAsync(StoredCredential credential, CancellationToken ct = default);
    ValueTask DeleteAsync(string id, CancellationToken ct = default);
    ValueTask<bool> IsUnlockedAsync(CancellationToken ct = default);
}

public interface IBiometricGate
{
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);
    ValueTask<bool> UnlockAsync(string purpose, CancellationToken ct = default);
}

public interface IQrScanner
{
    ValueTask<string?> ScanOnceAsync(CancellationToken ct = default);
}

public sealed record StoredCredential
{
    public required string Id { get; init; }
    public required VerifiableCredential Credential { get; init; }
    public required DateTimeOffset StoredAt { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
}
```

## MAUI Implementation (SecureStorage + Biometrics)

```csharp
// Sorcha.UI.Maui/Services/MauiCredentialUiStore.cs
public sealed class MauiCredentialUiStore(
    IBiometricGate biometricGate,
    ISymmetricCrypto symmetricCrypto,
    ILogger<MauiCredentialUiStore> logger) : ICredentialUiStore
{
    private const string IndexKey = "sorcha.credentials.index";
    private const string ContentKeyPrefix = "sorcha.credential.";

    public async ValueTask StoreAsync(StoredCredential credential, CancellationToken ct = default)
    {
        // SecureStorage enforces a max value length on some platforms (Android ~4KB).
        // Compress + split if needed — CredentialSerializer handles this centrally.
        var bytes = CredentialSerializer.ToCanonicalBytes(credential.Credential);
        var payload = await symmetricCrypto.EncryptAsync(bytes, "credential-store");

        await SecureStorage.Default.SetAsync(ContentKeyPrefix + credential.Id, Convert.ToBase64String(payload));
        await UpdateIndexAsync(index => index.Add(credential.Id));
        logger.LogInformation("Stored credential {CredentialId}", credential.Id);
    }

    public async ValueTask<StoredCredential?> GetAsync(string id, CancellationToken ct = default)
    {
        if (!await biometricGate.UnlockAsync("Access credential wallet", ct))
            return null;

        var raw = await SecureStorage.Default.GetAsync(ContentKeyPrefix + id);
        if (raw is null) return null;

        var ciphertext = Convert.FromBase64String(raw);
        var plaintext = await symmetricCrypto.DecryptAsync(ciphertext, "credential-store");
        var vc = CredentialSerializer.FromCanonicalBytes(plaintext);
        // ... hydrate StoredCredential wrapper
    }
}
```

Biometric implementation uses `Plugin.Fingerprint` (cross-platform biometric abstraction):

```csharp
public sealed class MauiBiometricGate(IFingerprint fingerprint) : IBiometricGate
{
    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
        => (await fingerprint.GetAvailabilityAsync()) == FingerprintAvailability.Available;

    public async ValueTask<bool> UnlockAsync(string purpose, CancellationToken ct = default)
    {
        var request = new AuthenticationRequestConfiguration("Sorcha Wallet", purpose)
        {
            CancelTitle = "Cancel",
            FallbackTitle = "Use passcode",
            AllowAlternativeAuthentication = true,
        };
        var result = await fingerprint.AuthenticateAsync(request, ct);
        return result.Authenticated;
    }
}
```

## WASM Implementation (IndexedDB)

```csharp
// Sorcha.UI.Web.Client/Services/IndexedDbCredentialUiStore.cs
public sealed class IndexedDbCredentialUiStore(
    IJSRuntime js,
    ISymmetricCrypto symmetricCrypto) : ICredentialUiStore
{
    public async ValueTask StoreAsync(StoredCredential credential, CancellationToken ct = default)
    {
        var bytes = CredentialSerializer.ToCanonicalBytes(credential.Credential);
        var ciphertext = await symmetricCrypto.EncryptAsync(bytes, "credential-store");
        await js.InvokeVoidAsync("sorchaCredentialDb.put", ct, credential.Id, ciphertext);
    }
    // ... List/Get/Delete follow the same pattern
}

public sealed class NoOpBiometricGate : IBiometricGate
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default) => ValueTask.FromResult(false);
    public ValueTask<bool> UnlockAsync(string purpose, CancellationToken ct = default) => ValueTask.FromResult(true);
}
```

## Razor Components

### CredentialList.razor

```razor
@using Sorcha.Blueprint.Engine.Credentials.Models
@inject ICredentialUiStore Store

<MudList T="StoredCredential" Dense="true">
    @foreach (var item in _items)
    {
        <MudListItem T="StoredCredential" OnClick="@(() => Select(item))">
            <div class="d-flex flex-column">
                <MudText Typo="Typo.body1">@DisplayName(item.Credential)</MudText>
                <MudText Typo="Typo.caption">@item.Credential.Issuer</MudText>
                @if (IsExpired(item.Credential))
                {
                    <MudChip T="string" Color="Color.Warning" Size="Size.Small">Expired</MudChip>
                }
            </div>
        </MudListItem>
    }
</MudList>

@code {
    [Parameter] public EventCallback<StoredCredential> OnSelected { get; set; }

    private IReadOnlyList<StoredCredential> _items = [];

    protected override async Task OnInitializedAsync()
        => _items = await Store.ListAsync();

    private Task Select(StoredCredential credential) => OnSelected.InvokeAsync(credential);

    private static string DisplayName(VerifiableCredential vc)
        => vc.Type.FirstOrDefault(t => t != "VerifiableCredential") ?? "Credential";

    private static bool IsExpired(VerifiableCredential vc)
        => vc.ValidUntil is { } until && until < DateTimeOffset.UtcNow;
}
```

### CredentialDetail.razor

Shows the full `credentialSubject` tree, verification status badge, and per-claim disclosure toggles ready for the presentation flow.

```razor
<MudCard Class="credential-detail">
    <MudCardHeader>
        <MudText Typo="Typo.h6">@DisplayName</MudText>
        <VerificationBadge Result="@_verificationResult" />
    </MudCardHeader>
    <MudCardContent>
        <ClaimTree Claims="@Credential.CredentialSubject.Claims"
                   Selectable="@AllowSelection"
                   SelectedPointers="@_selectedPointers" />
    </MudCardContent>
    <MudCardActions>
        <MudButton OnClick="HandlePresent" Color="Color.Primary">Present</MudButton>
        <MudButton OnClick="HandleRevoke" Color="Color.Error">Revoke</MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter] public required VerifiableCredential Credential { get; set; }
    [Parameter] public bool AllowSelection { get; set; }

    private CredentialVerificationResult? _verificationResult;
    private HashSet<string> _selectedPointers = new();

    protected override async Task OnInitializedAsync()
        => _verificationResult = await Verifier.VerifyAsync(Credential, new VerificationOptions(), default);
}
```

## QR Code / Deep Link Flows

### Credential offer (issuer → holder)

Use the `openid4vc://` URI scheme — standard across the ecosystem. The holder scans a QR code, which triggers the offer handler:

```csharp
public sealed class CredentialOfferHandler(IHolderClient holder, NavigationManager nav)
{
    public async Task HandleAsync(Uri offerUri)
    {
        if (offerUri.Scheme is not "openid4vc")
            throw new ArgumentException("Unsupported credential offer scheme", nameof(offerUri));

        var query = HttpUtility.ParseQueryString(offerUri.Query);
        var issuerUrl = query["credential_issuer"] ?? throw new InvalidOperationException();
        var preAuthCode = query["pre-authorized_code"];

        var offer = await holder.FetchOfferAsync(issuerUrl, preAuthCode);
        nav.NavigateTo($"/wallet/offer/{offer.Id}");
    }
}
```

Register the handler:

- **MAUI:** `App.xaml.cs` `OnAppLinkRequestReceived` → inject `CredentialOfferHandler`.
- **WASM:** Subscribe to a window message from a `postMessage` bridge in `wwwroot/credential-offer.js`.

### Presentation request (verifier → holder)

Same scheme, different path — `openid4vc://verify?request_uri=...`. The holder fetches the `PresentationRequest` JSON, shows the review screen, and only builds the VP after the biometric gate unlocks.

```razor
@page "/wallet/verify/{RequestId}"
@inject IVerifierClient Verifier
@inject IBiometricGate Biometric
@inject ICredentialUiStore Store

<PresentationReview Request="@_request"
                    Candidates="@_candidates"
                    OnConfirm="HandleConfirmAsync" />

@code {
    [Parameter] public required string RequestId { get; set; }

    private PresentationRequest? _request;
    private IReadOnlyList<StoredCredential> _candidates = [];

    protected override async Task OnInitializedAsync()
    {
        _request = await Verifier.FetchRequestAsync(RequestId);
        var all = await Store.ListAsync();
        _candidates = all.Where(c => _request.Matches(c.Credential)).ToList();
    }

    private async Task HandleConfirmAsync(PresentationSelection selection)
    {
        if (!await Biometric.UnlockAsync("Confirm credential presentation"))
            return;

        var vp = await Store.BuildPresentationAsync(_request!, selection.SelectedCredentials);
        await Verifier.SubmitPresentationAsync(_request!.Id, vp);
    }
}
```

## Playwright Testing

Wallet UI tests go in `Sorcha.UI.E2E.Tests/Credentials/` and use the Docker compose test infrastructure (see the `sorcha-ui` skill).

- Mock the biometric gate with a test double that always returns `true`.
- Pre-seed `ICredentialUiStore` with fixture VCs before the test navigates.
- Assert the verification badge renders green after `OnInitializedAsync` completes — wait on the badge's `data-verification-state="valid"` attribute, not an arbitrary timer.

## Render Mode Gotchas

- The MAUI host uses `InteractiveServer`; the WASM host uses `InteractiveWebAssembly`. Components must be compatible with **both**.
- Do not inject a service that only exists on one platform. Use `IServiceProvider.GetService<T>()` with a null check when the capability is optional (e.g. `IQrScanner` may be absent on desktop WASM).
- `System.Timers.Timer` disposal still requires the `_disposed` flag guard from `CLAUDE.md` — this catches the race where the UI reloads during a pending biometric prompt.

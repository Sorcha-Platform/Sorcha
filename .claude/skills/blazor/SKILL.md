---
name: blazor
description: |
  Builds Blazor WASM components for admin and main UI applications.
  Use when: Creating/modifying Razor components, configuring render modes, implementing authentication, managing component state, or working with MudBlazor components.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Blazor Skill

Sorcha uses Blazor with hybrid rendering (Server + WebAssembly). The Admin UI (`src/Apps/Sorcha.Admin/`) runs behind YARP API Gateway. Components use MudBlazor for UI and support three render modes: static server, interactive server, and interactive WASM.

## Quick Start

### Render Mode Selection

```razor
@* WASM - Complex interactive pages (Designer, Diagrams) *@
@page "/designer"
@rendermode InteractiveWebAssembly
@attribute [Authorize]

@* Server - Admin pages needing real-time SignalR *@
@page "/admin/audit"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Authorize(Roles = "Administrator")]

@* Static - Public pages (Login) - no @rendermode directive *@
@page "/login"
@attribute [AllowAnonymous]
```

### Component with Loading State

```razor
@inject HttpClient Http

<MudPaper Elevation="2" Class="pa-4">
    @if (_isLoading && !_hasLoadedOnce)
    {
        <MudProgressCircular Indeterminate="true" Size="Size.Small" />
    }
    else if (_data != null)
    {
        <MudText>@_data.Title</MudText>
    }
    else if (_errorMessage != null)
    {
        <MudAlert Severity="Severity.Error">@_errorMessage</MudAlert>
    }
</MudPaper>

@code {
    private DataDto? _data;
    private string? _errorMessage;
    private bool _isLoading;
    private bool _hasLoadedOnce;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _isLoading = true;
        try
        {
            _data = await Http.GetFromJsonAsync<DataDto>("/api/data");
            _hasLoadedOnce = true;
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }
}
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| Render Mode | Control where component runs | `@rendermode InteractiveWebAssembly` |
| CascadingParameter | Receive parent state | `[CascadingParameter] MudBlazor.IDialogReference? MudDialog` |
| OnAfterRenderAsync | Initialize after DOM ready | `if (firstRender) await LoadAsync();` |
| StateHasChanged | Trigger re-render | Call after async state updates |
| NavigationManager | Programmatic navigation | `Navigation.NavigateTo("/", forceLoad: true)` |

## Project Structure

| Project | Purpose | Render Mode |
|---------|---------|-------------|
| `Sorcha.Admin` | Server host, auth, API proxy | Server + prerender |
| `Sorcha.Admin.Client` | WASM components | WebAssembly |
| `Sorcha.UI.Core` | Shared components | Both |
| `Sorcha.UI.Web` | Main UI server | Server |
| `Sorcha.UI.Web.Client` | Main UI WASM | WebAssembly |

## Common Patterns

### MudBlazor Dialog

```razor
<MudDialog DisableSidePadding="false">
    <DialogContent>
        <MudTextField @bind-Value="_value" Label="Input" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Color="Color.Primary" OnClick="Submit">OK</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] MudBlazor.IDialogReference? MudDialog { get; set; }
    private string _value = "";
    
    private void Cancel() => MudDialog?.Close();
    private void Submit() => MudDialog?.Close(DialogResult.Ok(_value));
}
```

### Opening Dialog from Parent

```csharp
var dialog = await DialogService.ShowAsync<LoginDialog>("Login");
var result = await dialog.Result;
if (result is { Canceled: false })
{
    // Handle success
}
```

### PWA navigation when mounted under a path prefix

Blazor's `NavigationManager` treats paths with a **leading slash** as origin-relative (absolute), NOT base-href-relative. When the app is mounted under a prefix like `/wallet/` (via API Gateway `PathRemovePrefix` or similar) with `<base href="/wallet/" />` in `index.html`, this matters:

```razor
@* WRONG — resolves to https://host/enrol — 404 in production *@
<MudButton OnClick="@(() => Nav.NavigateTo("/enrol"))">Enrol</MudButton>
<MudLink Href="/settings">Settings</MudLink>

@* RIGHT — resolves to https://host/wallet/enrol via base href *@
<MudButton OnClick="@(() => Nav.NavigateTo("enrol"))">Enrol</MudButton>
<MudLink Href="settings">Settings</MudLink>

@* RIGHT — Home button: NavigateTo("") lands at base, NOT NavigateTo("/") *@
<MudIconButton OnClick="@(() => Nav.NavigateTo(""))" />
```

**Why it ships broken**: localhost-dev often serves the PWA at the origin root (no prefix), so leading-slash calls *happen to work*. The bug only manifests when the PWA is served under a prefix in production. Citizen Wallet PWA hit this on n1 — see PR #698.

**Test coverage**: every nav-triggering element must have a click+URL-assertion test. The page-object-without-clicks anti-pattern (see the **playwright** skill) is how this slips through CI.

### nginx caching for Blazor WASM — fingerprinted vs entry-point assets

Two classes of file live under `_framework/` and they need different cache policies:

| Class | Example | URL stable? | Contents change? | Cache |
|---|---|---|---|---|
| Fingerprinted | `Sorcha.Citizen.Wallet.b0fgy5dpkq.wasm` | No (hash in URL) | No (hash *is* the contents key) | `immutable, max-age=31536000` ✅ |
| Entry-point | `dotnet.js`, `blazor.webassembly.js` | Yes | **Yes** — embeds the fingerprint manifest of which wasm files to load | `no-cache, no-store, must-revalidate` ✅ |

A nginx regex like `location ~* ^/_framework/.*\.(dll|wasm|js)$` that marks **everything** as `immutable` is the trap — it catches the non-fingerprinted entry points. On redeploy, the wasm fingerprints rotate but browsers holding the year-cached `dotnet.js` keep referencing dead hashes. Manifests as "the wallet won't navigate after a redeploy on a returning device." See PR #699.

**Correct nginx pattern** (exact-match locations win over regex):

```nginx
# Non-fingerprinted entry points — revalidate every visit.
location = /_framework/dotnet.js {
    add_header Cache-Control "no-cache, no-store, must-revalidate";
    try_files $uri =404;
}
location = /_framework/blazor.webassembly.js {
    add_header Cache-Control "no-cache, no-store, must-revalidate";
    try_files $uri =404;
}

# Fingerprinted assets — safe to cache forever.
location ~* ^/_framework/.*\.(dll|wasm|js|json|blat|dat)$ {
    expires 1y;
    add_header Cache-Control "public, immutable";
    try_files $uri =404;
}
```

**Regression guard**: HTTP probe test that asserts `dotnet.js` does *not* contain `immutable` in `Cache-Control`. See `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletNginxCacheHeadersTests.cs`.

**The one-time penalty**: existing browsers that visited before the fix landed still hold the year-cached `dotnet.js`. They keep showing broken nav until their cache TTL expires OR they hard-refresh. New visitors (and incognito sessions) are unaffected.

## See Also

- [patterns](references/patterns.md) - Component and authentication patterns
- [workflows](references/workflows.md) - Development and deployment workflows

## Related Skills

- See the **aspire** skill for service discovery configuration
- See the **signalr** skill for real-time notifications
- See the **jwt** skill for authentication token handling
- See the **yarp** skill for API Gateway configuration
- See the **mudblazor** skill for component library details

## Documentation Resources

> Fetch latest Blazor/MudBlazor documentation with Context7.

**Library ID:** `/websites/mudblazor` _(MudBlazor component library documentation)_

**Recommended Queries:**
- "MudBlazor dialog service usage"
- "MudBlazor form validation"
- "MudBlazor data grid filtering"
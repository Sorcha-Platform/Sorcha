// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Designer;

namespace Sorcha.UI.Web.Client.Pages.DesignerShell;

/// <summary>
/// Shell page for the Feature 142 staged designer. Hosts the shared toolbar, the always-visible
/// lifecycle rail, the persistent AI chat pane, and the stage canvas that switches on
/// <see cref="LifecycleState.CurrentStage"/>. Keeps the active stage in sync with the
/// <c>?stage=</c> query-string parameter (accepting the legacy <c>?tab=</c> values as an alias).
/// </summary>
public partial class DesignerBlueprint : ComponentBase, IDisposable
{
    private string? _loadedBlueprintId;
    private bool _stageInitialised;

#if DEBUG || E2E_TEST_HOOKS
    private DotNetObjectReference<DesignerBlueprint>? _testHookRef;
#endif

    /// <summary>Optional blueprint identifier taken from the URL (/designer/blueprint/{id}).</summary>
    [Parameter]
    public string? BlueprintId { get; set; }

    [Inject] private IBlueprintApiService BlueprintApi { get; set; } = default!;
    [Inject] private ILogger<DesignerBlueprint> Logger { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        ApplyStageFromUri(Navigation.Uri, isInitial: true);
        Context.Changed += OnContextChanged;
        Navigation.LocationChanged += OnLocationChanged;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrEmpty(BlueprintId) || BlueprintId == _loadedBlueprintId)
        {
            return;
        }

        // Only load if the context doesn't already hold this blueprint (e.g. set via
        // AI session bootstrap). Avoids clobbering an in-progress session.
        if (Context.Blueprint?.Id == BlueprintId)
        {
            _loadedBlueprintId = BlueprintId;
            return;
        }

        try
        {
            var bp = await BlueprintApi.GetBlueprintDetailAsync(BlueprintId).ConfigureAwait(true);
            if (bp is not null)
            {
                Context.SetBlueprint(bp);
                _loadedBlueprintId = BlueprintId;

                // Feature 142 / T057 / US6 — rehydrate AmendContext from the lineage metadata that
                // POST /api/blueprints/from-published stamps onto the new draft. When present the
                // designer knows the draft is an amendment of a specific published version on a
                // specific register, which the Go-live stage uses to anchor the re-publish target.
                Context.Lifecycle.AmendContext = TryReadAmendLineage(bp);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load blueprint {BlueprintId}; leaving Context.Blueprint null.", BlueprintId);
        }
    }

    /// <summary>
    /// Reads the lineage metadata stamped by the amend / clone-to-draft endpoint
    /// (<see cref="BlueprintFromPublishedEndpointKeys"/>) off the loaded blueprint and returns a
    /// populated <see cref="AmendContext"/> when all three keys resolve. Returns null otherwise —
    /// non-amend drafts simply have no lineage and stay in the regular create-to-live loop.
    /// </summary>
    private static Sorcha.UI.Core.Services.Designer.AmendContext? TryReadAmendLineage(
        Sorcha.Blueprint.Models.Blueprint bp)
    {
        var metadata = bp.Metadata;
        if (metadata is null
            || !metadata.TryGetValue(BlueprintFromPublishedEndpointKeys.SourceRegister, out var registerId)
            || !metadata.TryGetValue(BlueprintFromPublishedEndpointKeys.SourceBlueprint, out var sourceBlueprintId)
            || !metadata.TryGetValue(BlueprintFromPublishedEndpointKeys.SourceVersion, out var versionRaw))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(registerId)
            || string.IsNullOrWhiteSpace(sourceBlueprintId)
            || !int.TryParse(versionRaw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var sourceVersion)
            || sourceVersion < 1)
        {
            return null;
        }

        return new Sorcha.UI.Core.Services.Designer.AmendContext(registerId, sourceBlueprintId, sourceVersion);
    }

    /// <summary>
    /// Mirrors the lineage metadata keys defined server-side in
    /// <c>BlueprintFromPublishedEndpoint</c> (Sorcha.Blueprint.Service). Centralised so the UI
    /// reads exactly what the server writes — a string-drift fix touches one place.
    /// </summary>
    private static class BlueprintFromPublishedEndpointKeys
    {
        public const string SourceRegister = "x-source-register";
        public const string SourceBlueprint = "x-source-blueprint-id";
        public const string SourceVersion = "x-source-version";
    }

    private void OnContextChanged()
    {
        SyncUrlToStage();
        InvokeAsync(StateHasChanged);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => ApplyStageFromUri(e.Location, isInitial: false);

    /// <summary>
    /// Resolves the requested stage from the URL and applies it to the context. On initial load,
    /// an absent <c>?stage=</c> (and legacy <c>?tab=</c>) defaults to <see cref="LifecycleStage.Understand"/>
    /// when a blueprint is already loaded, otherwise <see cref="LifecycleStage.Describe"/>.
    /// </summary>
    private void ApplyStageFromUri(string uri, bool isInitial)
    {
        var resolved = ParseStageFromUri(uri);

        if (resolved is null)
        {
            if (!isInitial)
            {
                // A location change without a stage hint (e.g. blueprint-id navigation) leaves the
                // current stage untouched.
                return;
            }

            resolved = Context.Blueprint is not null ? LifecycleStage.Understand : LifecycleStage.Describe;
        }

        _stageInitialised = true;

        if (Context.Lifecycle.CurrentStage != resolved.Value)
        {
            Context.SetStage(resolved.Value);
        }
        else if (!isInitial)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Mirrors the current stage to the <c>?stage=</c> query string (replace, no force-load).</summary>
    private void SyncUrlToStage()
    {
        if (!_stageInitialised)
        {
            return;
        }

        var current = StageRouteParser.ToQuery(Context.Lifecycle.CurrentStage);
        if (string.Equals(ParseStageQueryValue(Navigation.Uri), current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = new Uri(Navigation.Uri).GetLeftPart(UriPartial.Path);
        Navigation.NavigateTo($"{path}?stage={current}", forceLoad: false, replace: true);
    }

    /// <summary>
    /// Parses the requested stage from a URI, preferring an explicit <c>?stage=</c> value and
    /// falling back to the legacy <c>?tab=</c> alias. Returns <c>null</c> when neither is present.
    /// </summary>
    private static LifecycleStage? ParseStageFromUri(string uri)
    {
        var query = new Uri(uri).Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        string? stageValue = null;
        string? tabValue = null;

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..eq]);
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);

            if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
            {
                stageValue = value;
            }
            else if (string.Equals(key, "tab", StringComparison.OrdinalIgnoreCase))
            {
                tabValue = value;
            }
        }

        return StageRouteParser.Parse(stageValue) ?? StageRouteParser.FromLegacyTab(tabValue);
    }

    /// <summary>Returns the raw <c>stage=</c> value from a URI, or <c>null</c> when absent.</summary>
    private static string? ParseStageQueryValue(string uri)
    {
        var query = new Uri(uri).Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..eq]);
            if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        return null;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

#if DEBUG || E2E_TEST_HOOKS
        _testHookRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("eval",
                "window.sorcha = window.sorcha || {};" +
                "window.sorcha.designer = window.sorcha.designer || {};" +
                "window.sorcha.designer.shellRef = arguments[0];",
                _testHookRef);
        }
        catch
        {
            // Ignore — test hook registration is best-effort.
        }
#else
        await Task.CompletedTask;
#endif
    }

#if DEBUG || E2E_TEST_HOOKS
    /// <summary>
    /// Test seam: marks the current executable definition's rehearsal as passed so the Go-live rail
    /// lock opens. Mirrors the production <see cref="DesignerContext.RecordRehearsalPassed"/> path.
    /// </summary>
    [JSInvokable]
    public void TestInject_MarkRehearsalPassed()
    {
        Context.RecordRehearsalPassed();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Test seam: navigates the rail to the named stage (<c>describe|understand|rehearse|golive</c>).
    /// Unknown values are ignored.
    /// </summary>
    /// <param name="stage">The stage token to navigate to.</param>
    [JSInvokable]
    public Task TestInject_SetStage(string stage)
    {
        var parsed = StageRouteParser.Parse(stage);
        if (parsed is not null)
        {
            Context.SetStage(parsed.Value);
        }

        return InvokeAsync(StateHasChanged);
    }
#endif

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Changed -= OnContextChanged;
        Navigation.LocationChanged -= OnLocationChanged;
#if DEBUG || E2E_TEST_HOOKS
        _testHookRef?.Dispose();
#endif
    }
}

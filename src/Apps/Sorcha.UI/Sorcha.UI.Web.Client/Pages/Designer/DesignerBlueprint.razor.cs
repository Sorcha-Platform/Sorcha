// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Designer;

namespace Sorcha.UI.Web.Client.Pages.DesignerShell;

/// <summary>
/// Shell page for the AI Designer unified view. Hosts the shared toolbar, the three
/// tab panes (AI / Diagram / Preview) and keeps the active tab in sync with the
/// <c>?tab=</c> query-string parameter.
/// </summary>
public partial class DesignerBlueprint : ComponentBase, IDisposable
{
    private DesignerTab _activeTab;
    private string? _loadedBlueprintId;

    /// <summary>Optional blueprint identifier taken from the URL (/designer/blueprint/{id}).</summary>
    [Parameter]
    public string? BlueprintId { get; set; }

    [Inject] private IBlueprintApiService BlueprintApi { get; set; } = default!;
    [Inject] private ILogger<DesignerBlueprint> Logger { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _activeTab = ParseTabFromUri(Navigation.Uri);
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
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load blueprint {BlueprintId}; leaving Context.Blueprint null.", BlueprintId);
        }
    }

    private void OnContextChanged() => InvokeAsync(StateHasChanged);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var parsed = ParseTabFromUri(e.Location);
        if (parsed != _activeTab)
        {
            _activeTab = parsed;
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnTabChanged(int index)
    {
        var tab = (DesignerTab)index;
        if (tab == _activeTab)
        {
            return;
        }

        _activeTab = tab;
        var path = new Uri(Navigation.Uri).GetLeftPart(UriPartial.Path);
        var query = TabRouteParser.ToQuery(tab);
        var target = query is null ? path : $"{path}?tab={query}";
        Navigation.NavigateTo(target, forceLoad: false, replace: true);
    }

    private static DesignerTab ParseTabFromUri(string uri)
    {
        var query = new Uri(uri).Query;
        if (string.IsNullOrEmpty(query))
        {
            return TabRouteParser.Parse(null);
        }

        // Query starts with '?'; split on '&' and look for tab=…
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            if (string.Equals(key, "tab", StringComparison.OrdinalIgnoreCase))
            {
                return TabRouteParser.Parse(Uri.UnescapeDataString(part[(eq + 1)..]));
            }
        }
        return TabRouteParser.Parse(null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Changed -= OnContextChanged;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}

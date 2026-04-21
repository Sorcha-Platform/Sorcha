// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Controls auto-scroll behaviour for the AI chat message region.
/// Auto-scroll stays enabled while the user remains within 40px of the
/// scroll container's bottom; scrolling up beyond that threshold disables
/// it until they return to the bottom.
/// </summary>
public class AutoScrollController
{
    /// <summary>Distance-from-bottom threshold (pixels) that toggles auto-scroll.</summary>
    private const double ThresholdPx = 40;

    private readonly IJSRuntime _jsRuntime;
    private bool _autoScrollEnabled = true;
    private double _lastScrollTop;

    /// <summary>Creates a new controller that invokes JS interop via the provided runtime.</summary>
    public AutoScrollController(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>True when the next appended chunk will trigger a scroll-to-bottom.</summary>
    public bool IsAutoScrollEnabled => _autoScrollEnabled;

    /// <summary>
    /// Called after content has been appended to the scroll region. If auto-scroll is
    /// enabled, asks the browser to scroll the element identified by <paramref name="elementId"/>
    /// to its bottom.
    /// </summary>
    public async Task OnContentAppendedAsync(string elementId)
    {
        if (!_autoScrollEnabled)
        {
            return;
        }

        try
        {
            await _jsRuntime.InvokeVoidAsync("sorcha.designer.scrollToBottom", elementId).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone — nothing to scroll.
        }
        catch (TaskCanceledException)
        {
            // Circuit tearing down — safe to swallow.
        }
    }

    /// <summary>
    /// Called on every user scroll event. Updates the enabled flag based on the current
    /// distance from the bottom of the scroll container.
    /// </summary>
    public void OnUserScroll(double scrollTop, double scrollHeight, double clientHeight)
    {
        var distanceFromBottom = scrollHeight - (scrollTop + clientHeight);
        _autoScrollEnabled = distanceFromBottom <= ThresholdPx;
        _lastScrollTop = scrollTop;
    }
}

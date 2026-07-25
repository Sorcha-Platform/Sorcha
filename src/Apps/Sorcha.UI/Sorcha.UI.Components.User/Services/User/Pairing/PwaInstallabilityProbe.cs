// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Sorcha.UI.Core.Services.User.Pairing;

/// <summary>
/// JS-interop implementation of <see cref="IPwaInstallabilityProbe"/>.
/// The companion JS module (<c>pwa-install-probe.js</c>) captures the
/// <c>beforeinstallprompt</c> event eagerly on script load so the
/// probe can resolve quickly when invoked.
/// </summary>
public sealed class PwaInstallabilityProbe : IPwaInstallabilityProbe
{
    private const string ModulePath = "./js/pwa-install-probe.js";

    private readonly IJSRuntime _js;
    private readonly ILogger<PwaInstallabilityProbe> _logger;
    private IJSObjectReference? _module;
    private PwaInstallabilityVerdict? _cachedVerdict;
    private bool? _cachedIsMobile;

    public PwaInstallabilityProbe(IJSRuntime js, ILogger<PwaInstallabilityProbe> logger)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PwaInstallabilityVerdict> ProbeAsync(CancellationToken ct = default)
    {
        if (_cachedVerdict.HasValue)
        {
            return _cachedVerdict.Value;
        }

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ct, ModulePath)
                .ConfigureAwait(false);

            var verdict = await _module.InvokeAsync<string>("probe", ct).ConfigureAwait(false);
            _cachedVerdict = verdict switch
            {
                "programmatic" => PwaInstallabilityVerdict.CanInstallProgrammatically,
                "manual" => PwaInstallabilityVerdict.CanInstallManually,
                _ => PwaInstallabilityVerdict.CannotInstall,
            };
            return _cachedVerdict.Value;
        }
        catch (Exception ex)
        {
            // Fail-safe: when JS interop is unavailable (prerender,
            // sandboxed iframe, etc.) treat as not-installable so the
            // surface falls through to the QR variant.
            _logger.LogWarning(ex,
                "PwaInstallabilityProbe failed — defaulting to CannotInstall");
            _cachedVerdict = PwaInstallabilityVerdict.CannotInstall;
            return _cachedVerdict.Value;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsMobileAsync(CancellationToken ct = default)
    {
        if (_cachedIsMobile.HasValue)
        {
            return _cachedIsMobile.Value;
        }

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ct, ModulePath)
                .ConfigureAwait(false);

            _cachedIsMobile = await _module.InvokeAsync<bool>("isMobile", ct).ConfigureAwait(false);
            return _cachedIsMobile.Value;
        }
        catch (Exception ex)
        {
            // Fail-safe to desktop, which is what the surface did before this axis existed — so a
            // JS-interop failure degrades to the previous behaviour rather than to a new one.
            _logger.LogWarning(ex, "PwaInstallabilityProbe mobile check failed — assuming desktop");
            _cachedIsMobile = false;
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TriggerInstallAsync(CancellationToken ct = default)
    {
        if (_module is null)
        {
            return false;
        }

        try
        {
            return await _module.InvokeAsync<bool>("triggerInstall", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PwaInstallabilityProbe install-prompt failed");
            return false;
        }
    }
}

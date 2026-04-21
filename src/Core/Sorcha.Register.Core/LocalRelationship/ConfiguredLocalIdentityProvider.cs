// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.Register.Core.LocalRelationship;

/// <summary>
/// Options for <see cref="ConfiguredLocalIdentityProvider"/> (Feature 108).
/// </summary>
/// <remarks>
/// Configured via <c>LocalIdentity</c> section of <c>appsettings.json</c>:
/// <code>
/// "LocalIdentity": {
///   "WalletAddresses": ["ws11qq..."],
///   "ValidatorPublicKeyBase64": "BASE64..."
/// }
/// </code>
/// For v1 this is a simple static-config provider. A Wallet.Service-backed dynamic
/// provider can be introduced later without changing the interface.
/// </remarks>
public sealed class LocalIdentityOptions
{
    public string[] WalletAddresses { get; set; } = Array.Empty<string>();
    public string? ValidatorPublicKeyBase64 { get; set; }
}

/// <summary>
/// Default <see cref="ILocalIdentityProvider"/> implementation backed by configuration.
/// Returns an empty identity when no config is provided — nodes then derive all
/// relationships as <c>IsSubscriber == true</c>, which is safe (no spurious ownership).
/// </summary>
public sealed class ConfiguredLocalIdentityProvider : ILocalIdentityProvider
{
    private readonly IOptionsMonitor<LocalIdentityOptions> _options;
    private readonly ILogger<ConfiguredLocalIdentityProvider>? _logger;
    private LocalIdentitySnapshot? _cached;
    private readonly object _gate = new();

    public ConfiguredLocalIdentityProvider(
        IOptionsMonitor<LocalIdentityOptions> options,
        ILogger<ConfiguredLocalIdentityProvider>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _options.OnChange(_ => Invalidate());
    }

    /// <inheritdoc />
    public ValueTask<LocalIdentitySnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } cached)
            return ValueTask.FromResult(cached);

        lock (_gate)
        {
            if (_cached is { } again)
                return ValueTask.FromResult(again);

            var opt = _options.CurrentValue;
            byte[]? validatorKey = null;
            if (!string.IsNullOrWhiteSpace(opt.ValidatorPublicKeyBase64))
            {
                try
                {
                    validatorKey = Convert.FromBase64String(opt.ValidatorPublicKeyBase64);
                }
                catch (FormatException ex)
                {
                    _logger?.LogWarning(ex,
                        "LocalIdentity.ValidatorPublicKeyBase64 is not valid Base64; treating as absent");
                }
            }

            _cached = new LocalIdentitySnapshot(
                WalletAddresses: opt.WalletAddresses ?? Array.Empty<string>(),
                ValidatorPublicKey: validatorKey);

            _logger?.LogInformation(
                "LocalIdentity resolved — {WalletCount} wallet(s), validator key {KeyPresent}",
                _cached.WalletAddresses.Count,
                _cached.ValidatorPublicKey is null ? "absent" : "present");

            return ValueTask.FromResult(_cached);
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }
}

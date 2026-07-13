// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Sorcha.Proximity;

namespace Sorcha.Proximity.Capacitor;

/// <summary>
/// <see cref="IProximityTransport"/> over the native <c>sorcha-proximity</c> Capacitor plugin.
/// </summary>
/// <remarks>
/// <para>
/// The C# half of the thin-native seam. It marshals opaque bytes across the JS boundary and nothing more —
/// every byte of ISO 18013-5 protocol lives in <c>Sorcha.Mdoc</c>, shared with the reader and already proven
/// end-to-end over <see cref="LoopbackProximityTransport"/> with no radio at all.
/// </para>
/// <para>
/// Two established conventions are followed deliberately rather than reinvented: bytes cross as <b>base64
/// strings</b> (as in <c>WebCryptoDeviceKeyService</c> and <c>SorchaIndexedDb</c> — <c>byte[]</c> is never
/// marshalled), and push-from-JS uses <see cref="DotNetObjectReference"/> + <c>[JSInvokable]</c> (as in
/// <c>BrowserConnectivity</c>, the only such pattern in this wallet).
/// </para>
/// <para>
/// <b>The plugin is absent in a plain browser</b>, and that is a normal state, not an error: the wallet is a
/// PWA and `capacitor.config.json` points the native shell at the hosted build, so the very same code runs in
/// both. <see cref="ProbeAsync"/> therefore answers honestly instead of throwing, and the UI hides in-person
/// sharing rather than offering something broken.
/// </para>
/// </remarks>
public sealed class CapacitorProximityTransport : IProximityTransport
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<CapacitorProximityTransport>? _selfRef;
    private bool _registered;
    private bool _stopped;

    public CapacitorProximityTransport(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    /// <inheritdoc />
    public event Action<byte[]>? Received;

    /// <inheritdoc />
    public event Action<ProximityDisconnectReason>? Disconnected;

    /// <inheritdoc />
    public async Task<ProximityCapability> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _js.InvokeAsync<string>("SorchaProximity.probe", ct);
            var probe = JsonSerializer.Deserialize<ProbeResult>(json, JsonOptions);

            if (probe is null)
                return Unsupported(ProximityUnsupportedReason.NoPlugin);

            return new ProximityCapability(
                probe.Supported,
                probe.BluetoothEnabled,
                probe.PermissionGranted,
                MapReason(probe.Reason));
        }
        catch (JSException)
        {
            // The bridge script is not loaded at all. Still not an error the citizen should see — it just
            // means this device cannot do it.
            return Unsupported(ProximityUnsupportedReason.NoPlugin);
        }
        catch (InvalidOperationException)
        {
            // Prerendering, or JS interop unavailable. Same answer.
            return Unsupported(ProximityUnsupportedReason.NoPlugin);
        }
    }

    /// <inheritdoc />
    public async Task StartPeripheralAsync(ProximityAdvert advert, CancellationToken ct = default)
    {
        await EnsureRegisteredAsync(ct).ConfigureAwait(false);
        await _js.InvokeVoidAsync("SorchaProximity.startPeripheral", ct, advert.ServiceUuid.ToString());
    }

    /// <inheritdoc />
    public async Task ConnectCentralAsync(ProximityTarget target, CancellationToken ct = default)
    {
        await EnsureRegisteredAsync(ct).ConfigureAwait(false);
        await _js.InvokeVoidAsync("SorchaProximity.connectCentral", ct, target.ServiceUuid.ToString());
    }

    /// <inheritdoc />
    public async Task SendAsync(byte[] payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(_stopped, this);

        await _js.InvokeVoidAsync("SorchaProximity.send", ct, Convert.ToBase64String(payload));
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_stopped)
            return;   // idempotent

        _stopped = true;

        try
        {
            await _js.InvokeVoidAsync("SorchaProximity.stop", ct);
            await _js.InvokeVoidAsync("SorchaProximity.unregister", ct);
        }
        catch (JSException)
        {
            // The page may be tearing down. Failing to tear down cleanly must never itself surface as an
            // error — the session is over either way.
        }
        catch (InvalidOperationException)
        {
        }

        _registered = false;
    }

    /// <summary>Invoked from JS when a whole message arrives.</summary>
    [JSInvokable]
    public void OnReceived(string dataBase64)
    {
        if (_stopped)
            return;

        try
        {
            Received?.Invoke(Convert.FromBase64String(dataBase64));
        }
        catch (FormatException)
        {
            // A malformed payload is not a message. Dropping it is right: the protocol layer must never be
            // handed bytes we could not decode.
        }
    }

    /// <summary>Invoked from JS when the channel ends.</summary>
    [JSInvokable]
    public void OnDisconnected(string reason)
    {
        Disconnected?.Invoke(reason switch
        {
            "peerDisconnected" => ProximityDisconnectReason.PeerDisconnected,
            "timeout" => ProximityDisconnectReason.Timeout,
            "localStop" => ProximityDisconnectReason.LocalStop,
            _ => ProximityDisconnectReason.TransportError
        });
    }

    private async Task EnsureRegisteredAsync(CancellationToken ct)
    {
        if (_registered)
            return;

        _selfRef ??= DotNetObjectReference.Create(this);
        _registered = await _js.InvokeAsync<bool>("SorchaProximity.register", ct, _selfRef);

        if (!_registered)
            throw new InvalidOperationException(
                "In-person sharing needs the Sorcha app — the native proximity plugin is not present. " +
                "Probe before offering it, so this is never reached.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        _selfRef?.Dispose();
        _selfRef = null;
    }

    private static ProximityCapability Unsupported(ProximityUnsupportedReason reason) =>
        new(Supported: false, BluetoothEnabled: false, PermissionGranted: false, reason);

    private static ProximityUnsupportedReason MapReason(string? reason) => reason switch
    {
        "noPlugin" => ProximityUnsupportedReason.NoPlugin,
        "noBluetoothHardware" => ProximityUnsupportedReason.NoBluetoothHardware,
        "bluetoothOff" => ProximityUnsupportedReason.BluetoothOff,
        "permissionDenied" => ProximityUnsupportedReason.PermissionDenied,
        "permissionNotYetRequested" => ProximityUnsupportedReason.PermissionNotYetRequested,
        _ => ProximityUnsupportedReason.None
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ProbeResult
    {
        [JsonPropertyName("supported")]
        public bool Supported { get; init; }

        [JsonPropertyName("bluetoothEnabled")]
        public bool BluetoothEnabled { get; init; }

        [JsonPropertyName("permissionGranted")]
        public bool PermissionGranted { get; init; }

        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }
}

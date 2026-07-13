// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Proximity;

/// <summary>What a device can do, asked <b>before</b> any UI offers in-person sharing.</summary>
/// <param name="Supported">Whether an in-person exchange is possible at all here.</param>
/// <param name="BluetoothEnabled">Whether the radio is currently switched on.</param>
/// <param name="PermissionGranted">Whether the user has granted the necessary permissions.</param>
/// <param name="Reason">Why not, when <paramref name="Supported"/> is false. Drives what the UI says.</param>
public readonly record struct ProximityCapability(
    bool Supported,
    bool BluetoothEnabled,
    bool PermissionGranted,
    ProximityUnsupportedReason Reason = ProximityUnsupportedReason.None);

/// <summary>Why in-person sharing is unavailable — distinguishing "can't" from "not yet".</summary>
/// <remarks>
/// The distinction matters to the citizen: "your phone can't do this" is a dead end, while "turn Bluetooth
/// on" or "grant permission" is one tap from working. Collapsing them into a single "unsupported" is a bad
/// experience masquerading as a simple one.
/// </remarks>
public enum ProximityUnsupportedReason
{
    /// <summary>Supported.</summary>
    None,

    /// <summary>Running somewhere with no native plugin — e.g. a plain browser rather than the app.</summary>
    NoPlugin,

    /// <summary>The device has no Bluetooth radio.</summary>
    NoBluetoothHardware,

    /// <summary>Bluetooth is switched off. Recoverable: the citizen can turn it on.</summary>
    BluetoothOff,

    /// <summary>Permission was refused. Recoverable via system settings.</summary>
    PermissionDenied,

    /// <summary>Permission has not been asked for yet. Recoverable: just ask.</summary>
    PermissionNotYetRequested
}

/// <summary>What the holder advertises. The service UUID comes from the <c>DeviceEngagement</c>.</summary>
public readonly record struct ProximityAdvert(Guid ServiceUuid);

/// <summary>What the reader connects to, taken from the scanned <c>DeviceEngagement</c>.</summary>
public readonly record struct ProximityTarget(Guid ServiceUuid);

/// <summary>Why a channel ended.</summary>
public enum ProximityDisconnectReason
{
    /// <summary>The peer went away — walked off, locked their screen, moved out of range.</summary>
    PeerDisconnected,

    /// <summary>Nothing happened for too long.</summary>
    Timeout,

    /// <summary>The transport itself failed.</summary>
    TransportError,

    /// <summary>We closed it.</summary>
    LocalStop
}

/// <summary>
/// A bidirectional, message-oriented channel between two nearby devices.
/// </summary>
/// <remarks>
/// <para>
/// <b>This seam carries opaque bytes and knows nothing about CBOR, mdoc, credentials or sessions — and that
/// ignorance is load-bearing.</b> It is what allows <see cref="LoopbackProximityTransport"/> to stand in for
/// the radio, so the entire ISO 18013-5 exchange runs in ordinary tests with no Bluetooth and no phones. If
/// protocol knowledge leaks in here, that substitution stops working and the protocol becomes untestable
/// without hardware — which is the single thing this design exists to prevent.
/// </para>
/// <para>
/// Implementations must deliver a payload passed to <see cref="SendAsync"/> to the peer's
/// <see cref="Received"/> <b>whole or not at all</b>. Chunking and MTU are the implementation's problem; the
/// protocol layer must never see half a CBOR item.
/// </para>
/// </remarks>
public interface IProximityTransport : IAsyncDisposable
{
    /// <summary>
    /// Reports what this device can do. <b>Never throws</b> — an unsupported device returns
    /// <c>Supported: false</c> with a reason, so the UI can hide the affordance rather than offer something
    /// broken.
    /// </summary>
    Task<ProximityCapability> ProbeAsync(CancellationToken ct = default);

    /// <summary>Holder role: advertise and accept <b>one</b> connection.</summary>
    Task StartPeripheralAsync(ProximityAdvert advert, CancellationToken ct = default);

    /// <summary>Reader role: connect to the advertised peer.</summary>
    Task ConnectCentralAsync(ProximityTarget target, CancellationToken ct = default);

    /// <summary>Sends one whole message to the connected peer.</summary>
    Task SendAsync(byte[] payload, CancellationToken ct = default);

    /// <summary>Raised once per whole message received.</summary>
    event Action<byte[]>? Received;

    /// <summary>Raised when the channel ends. <b>Terminal</b> — the transport is not reusable afterwards.</summary>
    event Action<ProximityDisconnectReason>? Disconnected;

    /// <summary>Tears down and stops advertising/scanning. <b>Idempotent</b> and safe from any state.</summary>
    Task StopAsync(CancellationToken ct = default);
}

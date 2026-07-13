// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Proximity;

/// <summary>
/// An in-process <see cref="IProximityTransport"/> that wires a holder directly to a reader — <b>no
/// Bluetooth, no phones</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the centrepiece of the feature's test strategy.</b> The ISO 18013-5 exchange is the large,
/// exacting, silently-failing part of the work: one byte wrong in the session transcript and every signature
/// and MAC fails, with no diagnostic that tells you why. Bluetooth, by contrast, fails <em>visibly</em>.
/// </para>
/// <para>
/// So the protocol is proven here, in ordinary tests that run in CI on every change, and the radio is left
/// responsible only for moving bytes. Build BLE first and you get the appearance of progress while the part
/// that can actually sink the project stays unproven.
/// </para>
/// <para>
/// Fault injection (<see cref="DropNextMessage"/>, <see cref="CorruptNextMessage"/>) is how the tamper and
/// abandonment scenarios are tested without a soldering iron.
/// </para>
/// </remarks>
public sealed class LoopbackProximityTransport : IProximityTransport
{
    private LoopbackProximityTransport? _peer;
    private bool _stopped;

    private LoopbackProximityTransport() { }

    /// <summary>Creates a connected holder/reader pair.</summary>
    /// <remarks>
    /// They are wired immediately: <see cref="StartPeripheralAsync"/> and <see cref="ConnectCentralAsync"/>
    /// are accepted and recorded, but there is no discovery to do. The engines still call them, so the same
    /// code path runs here as on a real radio.
    /// </remarks>
    public static (LoopbackProximityTransport Holder, LoopbackProximityTransport Reader) CreatePair()
    {
        var holder = new LoopbackProximityTransport();
        var reader = new LoopbackProximityTransport();
        holder._peer = reader;
        reader._peer = holder;
        return (holder, reader);
    }

    /// <summary>The service UUID this transport was asked to advertise or connect to, if any.</summary>
    public Guid? ServiceUuid { get; private set; }

    /// <summary>Every payload this transport sent, in order. For assertions about what crossed the wire.</summary>
    /// <remarks>
    /// Inspecting what was actually <em>transmitted</em> — rather than what the reader chose to display — is
    /// how selective disclosure is proven (SC-005). A verifier that renders only what it was asked to render
    /// would look correct even if the holder sent everything.
    /// </remarks>
    public IReadOnlyList<byte[]> Sent => _sent;
    private readonly List<byte[]> _sent = [];

    /// <summary>Drops the next message instead of delivering it — simulates a peer walking away mid-exchange.</summary>
    public bool DropNextMessage { get; set; }

    /// <summary>Flips one bit of the next message in flight — simulates tampering or corruption.</summary>
    public bool CorruptNextMessage { get; set; }

    /// <inheritdoc />
    public event Action<byte[]>? Received;

    /// <inheritdoc />
    public event Action<ProximityDisconnectReason>? Disconnected;

    /// <inheritdoc />
    public Task<ProximityCapability> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProximityCapability(Supported: true, BluetoothEnabled: true, PermissionGranted: true));

    /// <inheritdoc />
    public Task StartPeripheralAsync(ProximityAdvert advert, CancellationToken ct = default)
    {
        ServiceUuid = advert.ServiceUuid;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ConnectCentralAsync(ProximityTarget target, CancellationToken ct = default)
    {
        ServiceUuid = target.ServiceUuid;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(byte[] payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(_stopped, this);

        _sent.Add(payload);

        if (DropNextMessage)
        {
            DropNextMessage = false;
            return Task.CompletedTask;   // silently swallowed, exactly as a lost connection would
        }

        var delivered = (byte[])payload.Clone();

        if (CorruptNextMessage)
        {
            CorruptNextMessage = false;
            if (delivered.Length > 0)
                delivered[^1] ^= 0x01;   // one bit — enough, and the verifier must catch it
        }

        _peer?.Deliver(delivered);
        return Task.CompletedTask;
    }

    private void Deliver(byte[] payload)
    {
        if (_stopped) return;
        Received?.Invoke(payload);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct = default)
    {
        if (_stopped) return Task.CompletedTask;   // idempotent

        _stopped = true;
        Disconnected?.Invoke(ProximityDisconnectReason.LocalStop);
        _peer?.NotifyPeerGone();
        return Task.CompletedTask;
    }

    private void NotifyPeerGone()
    {
        if (_stopped) return;
        Disconnected?.Invoke(ProximityDisconnectReason.PeerDisconnected);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

# Contract — `IProximityTransport` (the C# seam)

**Project**: `src/Common/Sorcha.Proximity.Abstractions/`

The single seam between the ISO protocol (C#, shared) and the radio (native, per-platform). **It carries
opaque bytes and knows nothing about CBOR, mdoc, credentials, or sessions.** That ignorance is what makes
`LoopbackProximityTransport` possible, and the loopback harness is the entire de-risking strategy for this
feature (FR-022 / SC-002).

```csharp
namespace Sorcha.Proximity.Abstractions;

/// <summary>A bidirectional, message-oriented channel between two nearby devices.</summary>
/// <remarks>
/// Implementations move opaque byte payloads and nothing more. Framing, chunking and MTU are the
/// implementation's concern; a payload handed to <see cref="SendAsync"/> arrives at the peer's
/// <see cref="Received"/> whole or not at all.
/// </remarks>
public interface IProximityTransport : IAsyncDisposable
{
    /// <summary>Reports what this device can do, before any UI offers it.</summary>
    Task<ProximityCapability> ProbeAsync(CancellationToken ct = default);

    /// <summary>Holder role: advertise <paramref name="advert"/> and accept one connection.</summary>
    Task StartPeripheralAsync(ProximityAdvert advert, CancellationToken ct = default);

    /// <summary>Reader role: connect to the peer described by <paramref name="target"/>.</summary>
    Task ConnectCentralAsync(ProximityTarget target, CancellationToken ct = default);

    /// <summary>Sends one whole message to the connected peer.</summary>
    Task SendAsync(byte[] payload, CancellationToken ct = default);

    /// <summary>Raised once per whole message received from the peer.</summary>
    event Action<byte[]>? Received;

    /// <summary>Raised when the peer disconnects or the channel fails. Terminal.</summary>
    event Action<ProximityDisconnectReason>? Disconnected;

    /// <summary>Tears the channel down and stops advertising/scanning. Idempotent.</summary>
    Task StopAsync(CancellationToken ct = default);
}

public sealed record ProximityCapability(
    bool Supported,
    bool BluetoothEnabled,
    bool PermissionGranted,
    ProximityUnsupportedReason? Reason);

public enum ProximityUnsupportedReason
{
    None, NoPlugin, NoBluetoothHardware, BluetoothOff, PermissionDenied, PermissionNotYetRequested
}

/// <summary>What the holder advertises. The service UUID comes from the DeviceEngagement.</summary>
public sealed record ProximityAdvert(Guid ServiceUuid);

/// <summary>What the reader connects to, taken from the scanned DeviceEngagement.</summary>
public sealed record ProximityTarget(Guid ServiceUuid);

public enum ProximityDisconnectReason { PeerDisconnected, Timeout, TransportError, LocalStop }
```

## Behavioural contract

| Rule | Why |
|---|---|
| A payload passed to `SendAsync` arrives at the peer's `Received` **whole or not at all**. Chunking and reassembly are the implementation's problem. | The protocol layer must never see a partial CBOR item. |
| **One peer at a time.** While connected, a second connection attempt is refused. | Spec edge case; also an ISO requirement in peripheral-server mode. |
| `ProbeAsync` **never throws** — an unsupported device returns `Supported: false` with a reason. | FR-020: the UI hides the capability rather than offering something broken. `Reason` is what lets the UI say *why* (FR-021: "turn on Bluetooth" vs "grant permission" vs "not supported"). |
| `StopAsync` is **idempotent** and safe from any state. | Sessions abandon from arbitrary states (screen lock, walk-away). |
| `Disconnected` is **terminal** — the transport is not reusable after it fires. | Forces the session engines to abandon cleanly rather than limp on. |
| The transport **MUST NOT** log payload bytes. | Payloads are encrypted credential data. |

## Implementations

| Implementation | Where | Purpose |
|---|---|---|
| `CapacitorProximityTransport` | `Sorcha.Wallet.Pwa` / `Sorcha.Verifier.Pwa` | JS interop to the native plugin. See `capacitor-plugin.md`. |
| `LoopbackProximityTransport` | `Sorcha.Proximity.Abstractions` | In-process. `CreatePair()` returns two transports whose `SendAsync` feeds the other's `Received`. **The whole protocol runs over this in CI, with no phone and no BLE.** Supports fault injection — drop, delay, and **single-byte corruption** — which is how the tamper and replay acceptance scenarios (US1 #2, #3) are tested. |

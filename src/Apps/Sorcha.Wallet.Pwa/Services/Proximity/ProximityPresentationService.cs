// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Mdoc;
using Sorcha.Mdoc.Proximity;
using Sorcha.Proximity;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Wallet.Pwa.Services.Presentation;

namespace Sorcha.Wallet.Pwa.Services.Proximity;

/// <summary>Drives one in-person presentation, from the wallet's point of view.</summary>
public interface IProximityPresentationService : IAsyncDisposable
{
    /// <summary>What this device can do. Ask before offering in-person sharing at all.</summary>
    Task<ProximityCapability> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts an exchange and returns the <c>DeviceEngagement</c> bytes to render as a QR code.
    /// </summary>
    /// <remarks>The QR carries an ephemeral key and a random service UUID — nothing about the citizen.</remarks>
    Task<byte[]> StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Waits for the reader's request. On return, the citizen must be shown
    /// <see cref="RequestedElements"/> — <b>including each element's <c>IntentToRetain</c></b> — and nothing
    /// has been disclosed.
    /// </summary>
    Task WaitForRequestAsync(CancellationToken ct = default);

    /// <summary>What the reader asked for.</summary>
    IReadOnlyList<RequestedElement> RequestedElements { get; }

    /// <summary>Which of those this citizen can actually satisfy.</summary>
    IReadOnlyList<RequestedElement> SatisfiableElements { get; }

    /// <summary>Discloses exactly <paramref name="approved"/> — nothing more — and completes the exchange.</summary>
    Task ApproveAsync(IReadOnlyCollection<RequestedElement> approved, CancellationToken ct = default);

    /// <summary>The citizen said no. Nothing is disclosed.</summary>
    Task DeclineAsync(CancellationToken ct = default);

    /// <summary>Where the exchange is.</summary>
    HolderSessionState State { get; }

    /// <summary>Why it failed, when it did.</summary>
    string? FailureReason { get; }
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// A thin composition: the ISO protocol lives in <c>Sorcha.Mdoc</c> / <c>Sorcha.Proximity.Abstractions</c>
/// and is already proven end-to-end over a loopback transport with no radio. This class only supplies the
/// three things that are wallet-specific — the citizen's mdocs, the citizen's device key, and the native
/// transport — and gets out of the way.
/// </para>
/// <para>
/// It deliberately does <b>not</b> re-implement anything the session engine already does. If a rule about
/// disclosure or consent seems to be missing here, it is because it is enforced structurally in
/// <see cref="ProximityHolderSession"/>, which is the right place for it.
/// </para>
/// </remarks>
public sealed class ProximityPresentationService : IProximityPresentationService
{
    private readonly IProximityTransport _transport;
    private readonly ICredentialCache _credentials;
    private readonly IMdocDeviceKeyService _deviceKey;
    private readonly ILogger<ProximityPresentationService> _logger;

    private ProximityHolderSession? _session;

    public ProximityPresentationService(
        IProximityTransport transport,
        ICredentialCache credentials,
        IMdocDeviceKeyService deviceKey,
        ILogger<ProximityPresentationService> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _deviceKey = deviceKey ?? throw new ArgumentNullException(nameof(deviceKey));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public HolderSessionState State => _session?.State ?? HolderSessionState.Idle;

    /// <inheritdoc />
    public string? FailureReason => _session?.FailureReason;

    /// <inheritdoc />
    public IReadOnlyList<RequestedElement> RequestedElements => _session?.RequestedElements ?? [];

    /// <inheritdoc />
    public IReadOnlyList<RequestedElement> SatisfiableElements => _session?.SatisfiableElements ?? [];

    /// <inheritdoc />
    public Task<ProximityCapability> ProbeAsync(CancellationToken ct = default) =>
        _transport.ProbeAsync(ct);

    /// <inheritdoc />
    public async Task<byte[]> StartAsync(CancellationToken ct = default)
    {
        if (_session is not null)
            throw new InvalidOperationException("An exchange is already in progress.");

        var held = await LoadHeldMdocsAsync(ct).ConfigureAwait(false);
        if (held.Count == 0)
            throw new InvalidOperationException(
                "This wallet holds no mdoc credentials, so there is nothing to present in person. " +
                "(SD-JWT credentials are presented online — the proximity rail is mdoc-shaped.)");

        _session = new ProximityHolderSession(_transport, held, _deviceKey.AsHolderDeviceKey());

        return await _session.StartAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WaitForRequestAsync(CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("No exchange has been started.");
        await session.WaitForRequestAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApproveAsync(IReadOnlyCollection<RequestedElement> approved, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("No exchange has been started.");
        await session.ApproveAsync(approved, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeclineAsync(CancellationToken ct = default)
    {
        if (_session is null)
            return;

        await _session.DeclineAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the citizen's mdoc credentials from the cache, in the shape the protocol needs.
    /// </summary>
    /// <remarks>
    /// SD-JWT credentials are skipped: the proximity rail is mdoc-shaped, and offering an SD-JWT here would be
    /// offering something the reader cannot consume. A credential whose stored CBOR will not decode is skipped
    /// with a warning rather than thrown on — the cache is a mirror of server state, and one bad row must
    /// never take the whole exchange down.
    /// </remarks>
    private async Task<IReadOnlyList<HeldMdoc>> LoadHeldMdocsAsync(CancellationToken ct)
    {
        var cached = await _credentials.ListAsync(ct).ConfigureAwait(false);

        var held = new List<HeldMdoc>();

        foreach (var credential in cached)
        {
            if (credential.Format != CachedCredentialFormat.MsoMdoc)
                continue;

            if (credential.DocType is not { Length: > 0 } docType || credential.IssuerSignedCbor is not { Length: > 0 } cbor)
            {
                _logger.LogWarning(
                    "Skipping mdoc credential {CredentialId}: it carries no docType or no IssuerSigned CBOR.",
                    credential.Id);
                continue;
            }

            try
            {
                held.Add(new HeldMdoc(docType, MdocCodec.DecodeIssuerSigned(cbor)));
            }
            catch (Exception ex)
            {
                // Evict-and-continue, as the credential cache itself does. A row we cannot decode is one
                // credential the citizen cannot present — not a reason to fail the whole presentation.
                _logger.LogWarning(ex,
                    "Skipping mdoc credential {CredentialId}: its stored CBOR does not decode.",
                    credential.Id);
            }
        }

        return held;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            // Abandon rather than just drop: the reader is left waiting otherwise, and the session's own
            // teardown zeroises the session keys.
            await _session.AbandonAsync().ConfigureAwait(false);
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }
    }
}

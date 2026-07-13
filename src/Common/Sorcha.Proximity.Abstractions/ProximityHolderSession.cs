// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;
using Sorcha.Mdoc.Proximity;

namespace Sorcha.Proximity;

/// <summary>Where the holder is in an exchange.</summary>
public enum HolderSessionState
{
    /// <summary>Nothing started.</summary>
    Idle,

    /// <summary>Engagement QR is showing; advertising; waiting for a reader.</summary>
    Engaging,

    /// <summary>Keys agreed; the reader's request has been decrypted.</summary>
    RequestReceived,

    /// <summary>The citizen is being shown what was asked for. <b>Nothing has been disclosed.</b></summary>
    AwaitingConsent,

    /// <summary>A response was sent.</summary>
    Complete,

    /// <summary>The citizen said no. Nothing was disclosed.</summary>
    Declined,

    /// <summary>The exchange ended without completing. Nothing was disclosed.</summary>
    Abandoned,

    /// <summary>Something went wrong. Nothing was disclosed.</summary>
    Failed
}

/// <summary>
/// The holder's static, ECDH-capable device key — the one an mdoc's <c>MSO.DeviceKey</c> is bound to —
/// expressed as an <b>agreement operation</b> rather than as key bytes.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ It must be an <b>ECDH</b> key, not the ECDSA key the wallet uses for SD-JWT key binding. ISO 18013-5
/// derives <c>EMacKey</c> by ECDH between this key and the reader's ephemeral key, and in WebCrypto a key's
/// usages are fixed at generation: <b>a key cannot be both ECDSA and ECDH</b>. That is why the wallet carries
/// two device keys (feature 185, design §3).
/// </para>
/// <para>
/// <b>And it is an operation, not a key.</b> On a phone this key is non-extractable — C# can never hold its
/// private half, which is precisely what makes it worth having. So the caller supplies a function that
/// performs the agreement (in WebCrypto, on the device) and returns the shared secret. Tests and the server
/// pass an in-memory implementation. Modelling this as a private key would compile perfectly and then be
/// impossible to wire to the only key that matters.
/// </para>
/// </remarks>
/// <param name="AgreeWithReaderEphemeral">
/// Given the reader's ephemeral public key, performs the ECDH and returns the raw shared secret (32 bytes).
/// </param>
public sealed record HolderDeviceKey(
    Func<ECPublicKeyParameters, CancellationToken, Task<byte[]>> AgreeWithReaderEphemeral)
{
    /// <summary>Builds a holder key from an in-memory private key — for tests and the server.</summary>
    /// <remarks>A real device cannot use this: its key is in hardware and cannot be loaded into one of these.</remarks>
    public static HolderDeviceKey FromInMemoryKey(ECPrivateKeyParameters staticDevicePrivate)
    {
        ArgumentNullException.ThrowIfNull(staticDevicePrivate);

        return new HolderDeviceKey((readerEphemeralPublic, ct) =>
            StaticDeviceKeyMaterial
                .ForHolder(staticDevicePrivate, readerEphemeralPublic)
                .AgreeAsync(ct));
    }
}

/// <summary>
/// Adapts a <see cref="HolderDeviceKey"/> (an agreement <em>operation</em>) to the
/// <see cref="StaticDeviceKeyMaterial"/> seam the crypto layer expects.
/// </summary>
/// <remarks>
/// The indirection exists so that a hardware-held, non-extractable device key can take part in the ECDH at
/// all. It is not ceremony — without it, the one key we actually want to use is the one key we cannot use.
/// </remarks>
internal sealed class DelegatedStaticDeviceKey(HolderDeviceKey deviceKey, ECPublicKeyParameters readerEphemeral)
    : StaticDeviceKeyMaterial
{
    public override Task<byte[]> AgreeAsync(CancellationToken ct = default)
        => deviceKey.AgreeWithReaderEphemeral(readerEphemeral, ct);
}

/// <summary>
/// The holder's half of an ISO 18013-5 proximity exchange, as a state machine over
/// <see cref="IProximityTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The disclosure invariant is structural, not merely intended.</b> There is exactly one method that can
/// encode credential data — <see cref="ApproveAsync"/> — and it is reachable only from
/// <see cref="HolderSessionState.AwaitingConsent"/>. Every other exit (<see cref="DeclineAsync"/>,
/// <see cref="AbandonAsync"/>, transport failure) leaves the session without ever having built a response.
/// This is what makes FR-010 ("if the citizen declines, no credential data leaves the device") a property of
/// the type rather than a promise in a comment.
/// </para>
/// </remarks>
public sealed class ProximityHolderSession : IAsyncDisposable
{
    private readonly IProximityTransport _transport;
    private readonly IReadOnlyList<HeldMdoc> _credentials;
    private readonly HolderDeviceKey _deviceKey;

    private AsymmetricCipherKeyPairAdapter? _ephemeral;
    private ProximitySessionTranscript? _transcript;
    private MdocSessionKeys? _keys;
    private DeviceEngagement? _engagement;
    private MdocDeviceRequest? _request;
    private HeldMdoc? _matched;

    private uint _readerCounter;
    private uint _holderCounter;

    private TaskCompletionSource<MdocDeviceRequest>? _requestArrived;

    /// <summary>Creates a holder session.</summary>
    /// <param name="transport">The channel. In tests, a <see cref="LoopbackProximityTransport"/>.</param>
    /// <param name="credentials">The mdocs this citizen holds.</param>
    /// <param name="deviceKey">The static ECDH device key the credentials are bound to.</param>
    public ProximityHolderSession(
        IProximityTransport transport,
        IReadOnlyList<HeldMdoc> credentials,
        HolderDeviceKey deviceKey)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _deviceKey = deviceKey ?? throw new ArgumentNullException(nameof(deviceKey));

        _transport.Received += OnReceived;
        _transport.Disconnected += OnDisconnected;
    }

    /// <summary>Current state.</summary>
    public HolderSessionState State { get; private set; } = HolderSessionState.Idle;

    /// <summary>Why the session failed, when it did.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// What the reader asked for — available once <see cref="State"/> is
    /// <see cref="HolderSessionState.AwaitingConsent"/>. <b>This is what the citizen must be shown</b>,
    /// including each element's <c>IntentToRetain</c>.
    /// </summary>
    public IReadOnlyList<RequestedElement> RequestedElements =>
        _request?.AllElements.ToList() ?? [];

    /// <summary>The elements the reader asked for that this citizen can actually satisfy.</summary>
    /// <remarks>
    /// When this is empty but <see cref="RequestedElements"/> is not, the citizen holds nothing that fits —
    /// they should be told that plainly rather than shown an empty approval screen.
    /// </remarks>
    public IReadOnlyList<RequestedElement> SatisfiableElements
    {
        get
        {
            if (_request is null || _matched is null)
                return [];

            var held = _matched.IssuerSigned.NameSpaces
                .SelectMany(ns => ns.Value.Select(i => (ns.Key, i.Item.ElementIdentifier)))
                .ToHashSet();

            return _request.AllElements
                .Where(e => held.Contains((e.Namespace, e.ElementIdentifier)))
                .ToList();
        }
    }

    /// <summary>
    /// Starts the exchange: generates an ephemeral key, builds the engagement, and starts advertising.
    /// </summary>
    /// <returns>The <c>DeviceEngagement</c> bytes to render as a QR code.</returns>
    public async Task<byte[]> StartAsync(CancellationToken ct = default)
    {
        if (State != HolderSessionState.Idle)
            throw new InvalidOperationException($"Cannot start from {State}.");

        // Fresh per session. Reusing either would let two presentations be linked to the same phone.
        _ephemeral = AsymmetricCipherKeyPairAdapter.Generate();
        var serviceUuid = Guid.NewGuid();

        _engagement = DeviceEngagement.ForBlePeripheralServer(_ephemeral.Public, serviceUuid);
        _requestArrived = new TaskCompletionSource<MdocDeviceRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _transport.StartPeripheralAsync(new ProximityAdvert(serviceUuid), ct).ConfigureAwait(false);

        State = HolderSessionState.Engaging;
        return _engagement.Encode();
    }

    /// <summary>
    /// Waits for the reader's request. On return, <see cref="State"/> is
    /// <see cref="HolderSessionState.AwaitingConsent"/> and <see cref="RequestedElements"/> is populated —
    /// <b>and nothing has been disclosed.</b>
    /// </summary>
    public async Task<MdocDeviceRequest> WaitForRequestAsync(CancellationToken ct = default)
    {
        // Deliberately tolerant of the request having ALREADY arrived. A transport may deliver synchronously
        // (the loopback one does, and a fast radio effectively can), so demanding that we still be in
        // Engaging here would be a race — and one that only shows up under the very conditions we test with.
        // The completion source carries the result either way.
        if (State is not (HolderSessionState.Engaging or HolderSessionState.AwaitingConsent))
            throw new InvalidOperationException($"Not awaiting a request in {State}.");

        var tcs = _requestArrived
            ?? throw new InvalidOperationException("Session was not started.");

        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// <b>The only method that can encode credential data.</b> Discloses exactly
    /// <paramref name="approved"/> — nothing more — and sends the response.
    /// </summary>
    /// <param name="approved">
    /// The elements the citizen approved. Must be a subset of what was requested; anything else is a bug and
    /// is rejected rather than silently disclosed.
    /// </param>
    public async Task ApproveAsync(IReadOnlyCollection<RequestedElement> approved, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(approved);

        if (State != HolderSessionState.AwaitingConsent)
            throw new InvalidOperationException(
                $"Cannot disclose from {State}. Credential data is only ever encoded out of AwaitingConsent — " +
                "that restriction is the mechanism behind FR-010, not a formality.");

        if (_matched is null || _transcript is null || _keys is null || _request is null)
            throw new InvalidOperationException("Session is not established.");

        // Never disclose something that was not asked for, even if a caller passes it.
        var requested = _request.AllElements.ToHashSet();
        var unrequested = approved.Where(a => !requested.Contains(a)).ToList();
        if (unrequested.Count > 0)
            throw new InvalidOperationException(
                $"Refusing to disclose {unrequested.Count} element(s) the reader never asked for. " +
                "The approved set must be a subset of the requested set.");

        var response = MdocDeviceResponseBuilder.BuildWithMac(_matched, approved, _transcript, _keys.EMacKey);

        var sessionData = new SessionData { Data = Encrypt(response) };
        await _transport.SendAsync(sessionData.Encode(), ct).ConfigureAwait(false);

        State = HolderSessionState.Complete;
    }

    /// <summary>The citizen declined. Terminates the session having disclosed nothing.</summary>
    public async Task DeclineAsync(CancellationToken ct = default)
    {
        if (State is HolderSessionState.Complete or HolderSessionState.Declined)
            return;

        await SendTerminationAsync(ct).ConfigureAwait(false);
        State = HolderSessionState.Declined;
    }

    /// <summary>
    /// The exchange ended without completing — screen lock, walking away, a second reader butting in.
    /// Discloses nothing and leaves both sides clean.
    /// </summary>
    public async Task AbandonAsync(CancellationToken ct = default)
    {
        if (State is HolderSessionState.Complete or HolderSessionState.Declined or HolderSessionState.Abandoned)
            return;

        await SendTerminationAsync(ct).ConfigureAwait(false);
        State = HolderSessionState.Abandoned;
    }

    private async Task SendTerminationAsync(CancellationToken ct)
    {
        try
        {
            await _transport.SendAsync(SessionData.Termination().Encode(), ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The peer may already be gone. Terminating is a courtesy; failing to is not an error, and it
            // must never stop us reaching a clean terminal state.
        }
    }

    private void OnReceived(byte[] payload)
    {
        // The transport's Received event is synchronous, but key agreement is not: on a phone the static
        // device key lives in WebCrypto and the ECDH crosses the JS-interop boundary. So the work is
        // continued on a task, with every failure funnelled into Fail() — a faulted task here must never
        // become an unobserved exception that silently strands the session.
        _ = HandleReceivedAsync(payload);
    }

    private async Task HandleReceivedAsync(byte[] payload)
    {
        try
        {
            switch (State)
            {
                case HolderSessionState.Engaging:
                    await HandleSessionEstablishmentAsync(payload).ConfigureAwait(false);
                    break;

                case HolderSessionState.AwaitingConsent:
                case HolderSessionState.RequestReceived:
                    // Only a termination is meaningful here. Anything else is ignored: we do not let a
                    // second message from the reader race the citizen's decision.
                    HandleMidSessionMessage(payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            Fail($"Malformed message from the reader: {ex.Message}");
        }
    }

    private async Task HandleSessionEstablishmentAsync(byte[] payload)
    {
        var establishment = SessionEstablishment.Decode(payload);

        var readerEphemeral = CoseKey.TryParseEc2PublicKey(MdocCbor.UnwrapTag24(establishment.EReaderKeyBytes));
        if (readerEphemeral is null)
        {
            Fail("The reader's ephemeral key is not a valid P-256 EC2 COSE_Key.");
            return;
        }

        _transcript = ProximitySessionTranscript.ForQrEngagement(
            MdocCbor.WrapTag24(_engagement!.Encode()),
            establishment.EReaderKeyBytes);

        _keys = await MdocSessionCrypto.DeriveKeysAsync(
            _transcript.TaggedBytes,
            _ephemeral!.Private,
            readerEphemeral,
            // EMacKey: OUR static device key, agreed with THEIR ephemeral public key.
            //
            // On a phone the agreement happens inside WebCrypto — we never see the private key, which is the
            // whole point of it being non-extractable. Hence a delegate rather than a key.
            new DelegatedStaticDeviceKey(_deviceKey, readerEphemeral)).ConfigureAwait(false);

        _readerCounter++;
        var plaintext = MdocSessionCrypto.Decrypt(
            _keys.SkReader, establishment.Data, _readerCounter, MdocSessionRole.Reader);

        if (plaintext is null)
        {
            Fail("The reader's request did not authenticate. Session keys disagree, or it was tampered with.");
            return;
        }

        _request = MdocDeviceRequest.Decode(plaintext);
        _matched = _credentials.FirstOrDefault(c =>
            _request.DocRequests.Any(d => d.DocType == c.DocType));

        State = HolderSessionState.AwaitingConsent;
        _requestArrived?.TrySetResult(_request);
    }

    private void HandleMidSessionMessage(byte[] payload)
    {
        var data = SessionData.Decode(payload);
        if (data.Status is not null)
        {
            State = HolderSessionState.Abandoned;
            _requestArrived?.TrySetCanceled();
        }
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        _holderCounter++;
        return MdocSessionCrypto.Encrypt(_keys!.SkDevice, plaintext, _holderCounter, MdocSessionRole.Holder);
    }

    private void OnDisconnected(ProximityDisconnectReason reason)
    {
        if (State is HolderSessionState.Complete or HolderSessionState.Declined or HolderSessionState.Failed)
            return;

        State = HolderSessionState.Abandoned;
        _requestArrived?.TrySetCanceled();
    }

    private void Fail(string reason)
    {
        FailureReason = reason;
        State = HolderSessionState.Failed;
        _requestArrived?.TrySetException(new InvalidOperationException(reason));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _transport.Received -= OnReceived;
        _transport.Disconnected -= OnDisconnected;

        // Session keys are secrets and the process outlives the session.
        _keys?.Dispose();

        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}

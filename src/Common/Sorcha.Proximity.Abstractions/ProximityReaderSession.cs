// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc;
using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;
using Sorcha.Mdoc.Proximity;

namespace Sorcha.Proximity;

/// <summary>Where the reader is in an exchange.</summary>
public enum ReaderSessionState
{
    /// <summary>Nothing started.</summary>
    Idle,

    /// <summary>Engagement scanned; connected; request sent.</summary>
    RequestSent,

    /// <summary>A response arrived and was verified.</summary>
    Verified,

    /// <summary>The holder declined or walked away.</summary>
    Abandoned,

    /// <summary>Something went wrong, or the response did not verify.</summary>
    Failed
}

/// <summary>What the reader ended up with.</summary>
/// <param name="Accepted">Whether the presentation is acceptable.</param>
/// <param name="DocType">The document type presented.</param>
/// <param name="DisclosedClaims">The elements the citizen actually disclosed.</param>
/// <param name="IssuerSignatureValid">Whether the issuer's signature over the MSO verified.</param>
/// <param name="DigestsValid">Whether every disclosed element matched the issuer's digests.</param>
/// <param name="DeviceBindingValid">Whether the holder proved possession of the bound device key.</param>
/// <param name="ValidityOk">Whether the credential is within its validity window.</param>
/// <param name="Errors">Why it was rejected, when it was.</param>
public sealed record ProximityVerdict(
    bool Accepted,
    string DocType,
    IReadOnlyDictionary<string, object> DisclosedClaims,
    bool IssuerSignatureValid,
    bool DigestsValid,
    bool DeviceBindingValid,
    bool ValidityOk,
    IReadOnlyList<string> Errors);

/// <summary>
/// The reader's half of an ISO 18013-5 proximity exchange.
/// </summary>
/// <remarks>
/// Produces a <see cref="ProximityVerdict"/> from the cryptographic facts. The richer four-layer verdict
/// trail (which additionally reports the register anchor as <em>not checked</em> when offline) is assembled
/// by the reader app on top of this — the engine deliberately performs no network I/O, which is what lets it
/// run on a device with no signal.
/// </remarks>
public sealed class ProximityReaderSession : IAsyncDisposable
{
    private readonly IProximityTransport _transport;
    private readonly IMdocService _mdocService;

    private AsymmetricCipherKeyPairAdapter? _ephemeral;
    private ProximitySessionTranscript? _transcript;
    private MdocSessionKeys? _keys;

    private uint _readerCounter;
    private uint _holderCounter;

    private TaskCompletionSource<ProximityVerdict>? _verdictReady;

    /// <summary>Creates a reader session.</summary>
    public ProximityReaderSession(IProximityTransport transport, IMdocService? mdocService = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _mdocService = mdocService ?? new MdocService();

        _transport.Received += OnReceived;
        _transport.Disconnected += OnDisconnected;
    }

    /// <summary>Current state.</summary>
    public ReaderSessionState State { get; private set; } = ReaderSessionState.Idle;

    /// <summary>Why the session failed, when it did.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Scans the holder's engagement, connects, and sends the request.
    /// </summary>
    /// <param name="deviceEngagementBytes">The <c>DeviceEngagement</c> decoded from the holder's QR.</param>
    /// <param name="request">What the reader wants to know.</param>
    public async Task RequestAsync(
        byte[] deviceEngagementBytes, MdocDeviceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deviceEngagementBytes);
        ArgumentNullException.ThrowIfNull(request);

        if (State != ReaderSessionState.Idle)
            throw new InvalidOperationException($"Cannot start from {State}.");

        var engagement = DeviceEngagement.Decode(deviceEngagementBytes);

        var holderEphemeral = engagement.TryGetEphemeralPublicKey();
        if (holderEphemeral is null)
            throw new InvalidOperationException("The engagement carries no usable ephemeral key.");

        if (engagement.Ble.PeripheralServerUuid is not { } serviceUuid)
            throw new InvalidOperationException(
                "The engagement does not offer BLE peripheral-server mode, which is the only transport this " +
                "reader speaks.");

        _ephemeral = AsymmetricCipherKeyPairAdapter.Generate();
        _verdictReady = new TaskCompletionSource<ProximityVerdict>(TaskCreationOptions.RunContinuationsAsynchronously);

        var eReaderKeyTagged = MdocCbor.WrapTag24(CoseKey.EncodeEc2PublicKey(_ephemeral.Public));

        _transcript = ProximitySessionTranscript.ForQrEngagement(
            MdocCbor.WrapTag24(deviceEngagementBytes),
            eReaderKeyTagged);

        // The reader has no static device key of its own to agree with — EMacKey comes from the holder's
        // static key, which the reader learns from the MSO in the response. So it is derived later, once we
        // can see the credential. (This asymmetry is inherent to the standard, not an artefact of our design.)
        _keys = await MdocSessionCrypto.DeriveKeysAsync(
            _transcript.TaggedBytes,
            _ephemeral.Private,
            holderEphemeral,
            staticDeviceKey: null,
            ct).ConfigureAwait(false);

        await _transport.ConnectCentralAsync(new ProximityTarget(serviceUuid), ct).ConfigureAwait(false);

        _readerCounter++;
        var establishment = new SessionEstablishment
        {
            EReaderKeyBytes = eReaderKeyTagged,
            Data = MdocSessionCrypto.Encrypt(
                _keys.SkReader, request.Encode(), _readerCounter, MdocSessionRole.Reader)
        };

        await _transport.SendAsync(establishment.Encode(), ct).ConfigureAwait(false);
        State = ReaderSessionState.RequestSent;
    }

    /// <summary>Waits for the holder's response and returns the verdict.</summary>
    public async Task<ProximityVerdict> WaitForVerdictAsync(CancellationToken ct = default)
    {
        var tcs = _verdictReady
            ?? throw new InvalidOperationException("No request has been sent.");

        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task.ConfigureAwait(false);
    }

    private void OnReceived(byte[] payload)
    {
        // Deriving EMacKey needs an ECDH, which is async for symmetry with the holder (whose key is in
        // hardware). Continue on a task and funnel every failure into Fail() — a faulted task here must never
        // become an unobserved exception that strands the verdict.
        _ = HandleReceivedAsync(payload);
    }

    private async Task HandleReceivedAsync(byte[] payload)
    {
        if (State != ReaderSessionState.RequestSent)
            return;

        try
        {
            var sessionData = SessionData.Decode(payload);

            if (sessionData.Data is null)
            {
                // Status-only: the holder declined or walked away.
                State = ReaderSessionState.Abandoned;
                _verdictReady?.TrySetCanceled();
                return;
            }

            _holderCounter++;
            var responseBytes = MdocSessionCrypto.Decrypt(
                _keys!.SkDevice, sessionData.Data, _holderCounter, MdocSessionRole.Holder);

            if (responseBytes is null)
            {
                Fail("The response did not authenticate — it was tampered with, replayed, or is not ours.");
                return;
            }

            _verdictReady?.TrySetResult(await VerifyAsync(responseBytes).ConfigureAwait(false));
            State = ReaderSessionState.Verified;
        }
        catch (Exception ex)
        {
            Fail($"Malformed response from the holder: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies the response, deriving <c>EMacKey</c> from the static device key the MSO carries.
    /// </summary>
    private async Task<ProximityVerdict> VerifyAsync(byte[] responseBytes)
    {
        // The holder's static device key is published in the MSO. Only now can the reader complete the
        // EMacKey agreement — its own ephemeral private key against that static public key.
        var eMacKey = await TryDeriveEMacKeyAsync(responseBytes).ConfigureAwait(false);

        var result = _mdocService.Verify(responseBytes, _transcript!.Encoded, eMacKey);

        // A response that discloses NOTHING must not read as a pass.
        //
        // The format-level digest check iterates the elements that ARE present, so an empty disclosure
        // satisfies it vacuously — every one of zero digests matches. Cryptographically that is correct
        // (nothing was claimed, so nothing is unsound), but a verdict of "Accepted" over an empty claim set
        // would tell an operator their check succeeded when in fact they learned nothing. Fail closed.
        var errors = result.Errors.ToList();
        if (result.IsValid && result.Claims.Count == 0)
        {
            errors.Add(
                "The holder disclosed no elements. Nothing was proven — this is not an acceptance. " +
                "(It usually means the reader asked for a namespace or element the credential does not carry.)");

            return new ProximityVerdict(
                Accepted: false,
                DocType: result.DocType,
                DisclosedClaims: result.Claims,
                IssuerSignatureValid: result.IssuerSignatureValid,
                DigestsValid: result.DigestsValid,
                DeviceBindingValid: result.DeviceBindingValid,
                ValidityOk: result.ValidityOk,
                Errors: errors);
        }

        return new ProximityVerdict(
            Accepted: result.IsValid,
            DocType: result.DocType,
            DisclosedClaims: result.Claims,
            IssuerSignatureValid: result.IssuerSignatureValid,
            DigestsValid: result.DigestsValid,
            DeviceBindingValid: result.DeviceBindingValid,
            ValidityOk: result.ValidityOk,
            Errors: errors);
    }

    private async Task<byte[]?> TryDeriveEMacKeyAsync(byte[] responseBytes)
    {
        try
        {
            var response = MdocCodec.DecodeDeviceResponse(responseBytes);
            if (response.Documents.Count == 0)
                return null;

            var issuerAuth = response.Documents[0].IssuerSigned.IssuerAuth;
            if (issuerAuth.Content is not { } content)
                return null;

            var mso = MdocCodec.DecodeMso(MdocCbor.UnwrapTag24(content));
            var staticDevicePublic = CoseKey.TryParseEc2PublicKey(mso.DeviceKeyCose);
            if (staticDevicePublic is null)
                return null;

            using var macKeys = await MdocSessionCrypto.DeriveKeysAsync(
                _transcript!.TaggedBytes,
                _ephemeral!.Private,
                _ephemeral.Public,   // unused for EMacKey; the static agreement below is what matters
                StaticDeviceKeyMaterial.ForReader(_ephemeral.Private, staticDevicePublic)).ConfigureAwait(false);

            return (byte[])macKeys.EMacKey.Clone();
        }
        catch (Exception)
        {
            // A response we cannot parse cannot yield a MAC key. Verification will then reject the
            // deviceMac rather than wave it through — fail closed.
            return null;
        }
    }

    private void OnDisconnected(ProximityDisconnectReason reason)
    {
        if (State is ReaderSessionState.Verified or ReaderSessionState.Failed)
            return;

        State = ReaderSessionState.Abandoned;
        _verdictReady?.TrySetCanceled();
    }

    private void Fail(string reason)
    {
        FailureReason = reason;
        State = ReaderSessionState.Failed;
        _verdictReady?.TrySetException(new InvalidOperationException(reason));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _transport.Received -= OnReceived;
        _transport.Disconnected -= OnDisconnected;

        _keys?.Dispose();

        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}

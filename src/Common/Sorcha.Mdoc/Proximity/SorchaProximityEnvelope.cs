// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using System.Security.Cryptography;
using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Proximity;

/// <summary>Which credential format a proximity response carries.</summary>
public enum ProximityPayloadFormat
{
    /// <summary>An ISO 18013-5 <c>DeviceResponse</c> (CBOR). The interop rail.</summary>
    MsoMdoc = 0,

    /// <summary>An SD-JWT VC presentation (compact form). Sorcha's native rail.</summary>
    SdJwtVc = 1
}

/// <summary>
/// The envelope that lets <b>both</b> credential formats travel over the <b>one</b> ISO 18013-5 session.
/// </summary>
/// <remarks>
/// <para>
/// The session layer — engagement, ECDH, session encryption, the transcript — is format-agnostic and is
/// written once. Only the payload inside <c>SessionData.data</c> differs: an mdoc exchange carries a
/// <c>DeviceResponse</c>; a Sorcha-native exchange carries an SD-JWT VC presentation in this envelope.
/// </para>
/// <para>
/// <b>Why bother, when mdoc is the standard?</b> Because mdoc is what the world can read, and SD-JWT VC is
/// what Sorcha citizens actually <em>hold</em>. Shipping only mdoc would make in-person sharing useless to
/// every current holder; shipping only SD-JWT would dead-end it against the wider ecosystem. Both rails, one
/// session.
/// </para>
/// <para>
/// <b>The security property that makes this honest.</b> Replay resistance must hold <em>identically</em> for
/// both formats (FR-005). mdoc gets it from the session transcript inside <c>DeviceAuthentication</c>. The
/// SD-JWT path gets it by binding the KB-JWT's <c>aud</c> and <c>nonce</c> to
/// <see cref="SessionBinding"/> — the hash of this session's transcript — instead of to an HTTPS
/// <c>response_uri</c>, which does not exist here. Without that, the SD-JWT path would have <b>no session
/// binding at all</b> and a captured presentation would replay anywhere.
/// </para>
/// </remarks>
public sealed class SorchaProximityEnvelope
{
    /// <summary>Which format <see cref="Payload"/> is.</summary>
    public required ProximityPayloadFormat Format { get; init; }

    /// <summary>The presentation: a CBOR <c>DeviceResponse</c>, or a UTF-8 SD-JWT compact presentation.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// The value an SD-JWT KB-JWT must bind to: the base64url SHA-256 of the session transcript.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the SD-JWT rail's equivalent of the session transcript that mdoc splices into
    /// <c>DeviceAuthentication</c>. It is what ties a presentation to <em>this</em> engagement, with
    /// <em>this</em> reader, and therefore what makes a captured presentation worthless in any other session.
    /// </para>
    /// <para>
    /// It goes in <b>both</b> the KB-JWT's <c>aud</c> and its <c>nonce</c>: <c>aud</c> so the presentation
    /// names the session it is for, <c>nonce</c> so it cannot be lifted into a different one. A verifier that
    /// checks only one of them can still be replayed against.
    /// </para>
    /// </remarks>
    public static string SessionBinding(ProximitySessionTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        // The TAG-24-wrapped form — the same bytes the HKDF salt hashes. Using the bare array here instead
        // would still "work" (both sides would agree) but would gratuitously invent a third hash of a
        // structure the standard already tells us how to hash. One rule, one form.
        var hash = SHA256.HashData(transcript.TaggedBytes);

        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Encodes to CBOR: <c>{ "format": uint, "payload": bstr }</c>.</summary>
    public byte[] Encode() => MdocCbor.Encode(w =>
    {
        w.WriteStartMap(2);
        w.WriteTextString("format");
        w.WriteUInt32((uint)Format);
        w.WriteTextString("payload");
        w.WriteByteString(Payload);
        w.WriteEndMap();
    });

    /// <summary>
    /// Decodes a response payload, tolerating a <b>bare</b> <c>DeviceResponse</c> for interoperability.
    /// </summary>
    /// <remarks>
    /// A conformant third-party wallet knows nothing about this envelope and will send a plain
    /// <c>DeviceResponse</c>. Refusing that would make our reader unable to read any wallet but our own —
    /// which would defeat the point of implementing the standard at all. So: if it parses as our envelope,
    /// use it; otherwise treat the bytes as an mdoc <c>DeviceResponse</c>, which is what the standard says
    /// they are.
    /// </remarks>
    public static SorchaProximityEnvelope Decode(byte[] responsePayload)
    {
        ArgumentNullException.ThrowIfNull(responsePayload);

        if (TryDecodeEnvelope(responsePayload, out var envelope))
            return envelope;

        return new SorchaProximityEnvelope
        {
            Format = ProximityPayloadFormat.MsoMdoc,
            Payload = responsePayload
        };
    }

    private static bool TryDecodeEnvelope(byte[] bytes, out SorchaProximityEnvelope envelope)
    {
        envelope = null!;

        try
        {
            uint? format = null;
            byte[]? payload = null;

            var r = new CborReader(bytes, CborConformanceMode.Lax);

            // A DeviceResponse is a map too, so shape alone is not enough to tell them apart — the KEYS are.
            // A DeviceResponse has "version"/"documents"/"status"; ours has "format"/"payload".
            if (r.PeekState() != CborReaderState.StartMap)
                return false;

            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                if (r.PeekState() != CborReaderState.TextString)
                    return false;

                switch (r.ReadTextString())
                {
                    case "format": format = r.ReadUInt32(); break;
                    case "payload": payload = r.ReadByteString(); break;
                    default: return false;   // an unexpected key means this is not our envelope
                }
            }
            r.ReadEndMap();

            if (format is null || payload is null)
                return false;

            if (!Enum.IsDefined(typeof(ProximityPayloadFormat), (int)format.Value))
                return false;

            envelope = new SorchaProximityEnvelope
            {
                Format = (ProximityPayloadFormat)format.Value,
                Payload = payload
            };
            return true;
        }
        catch (CborContentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

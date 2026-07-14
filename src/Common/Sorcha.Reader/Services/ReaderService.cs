// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Mdoc.Proximity;
using Sorcha.Proximity;

namespace Sorcha.Reader.Services;

/// <inheritdoc />
/// <remarks>
/// A thin composition, exactly like the wallet's side. The protocol lives in
/// <see cref="ProximityReaderSession"/> and is already proven end-to-end over a loopback transport with no
/// radio; this class supplies the transport and the question, and gets out of the way.
/// </remarks>
public sealed class ReaderService : IReaderService
{
    private readonly IProximityTransport _transport;

    private ProximityReaderSession? _session;

    public ReaderService(IProximityTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <inheritdoc />
    public ReaderSessionState State => _session?.State ?? ReaderSessionState.Idle;

    /// <inheritdoc />
    public string? FailureReason => _session?.FailureReason;

    /// <inheritdoc />
    public Task<ProximityCapability> ProbeAsync(CancellationToken ct = default) =>
        _transport.ProbeAsync(ct);

    /// <inheritdoc />
    public async Task<ProximityVerdict> ReadAsync(
        string engagementQrPayload, ReaderQuestion question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementQrPayload);
        ArgumentNullException.ThrowIfNull(question);

        if (_session is not null)
            throw new InvalidOperationException("A read is already in progress.");

        var engagement = DecodeEngagement(engagementQrPayload);

        _session = new ProximityReaderSession(_transport);

        var request = new MdocDeviceRequest
        {
            DocRequests =
            [
                new ItemsRequest
                {
                    DocType = question.DocType,
                    Elements = question.Elements,
                }
            ]
        };

        await _session.RequestAsync(engagement, request, ct).ConfigureAwait(false);

        return await _session.WaitForVerdictAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes the ISO 18013-5 QR-engagement payload: <c>mdoc:</c> + base64url of the <c>DeviceEngagement</c>.
    /// </summary>
    /// <remarks>
    /// Anything else is rejected outright. A reader that will "have a go" at an arbitrary QR is a reader that
    /// can be pointed at something else entirely.
    /// </remarks>
    internal static byte[] DecodeEngagement(string qrPayload)
    {
        const string Scheme = "mdoc:";

        var trimmed = qrPayload.Trim();

        if (!trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                "That is not a Sorcha engagement code. Ask them to open 'Share in person' in their wallet.");

        var payload = trimmed[Scheme.Length..];

        try
        {
            return FromBase64Url(payload);
        }
        catch (FormatException)
        {
            throw new FormatException("That engagement code is damaged. Ask them to show it again.");
        }
    }

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }
    }
}

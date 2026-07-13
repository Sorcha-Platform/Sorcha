// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc.Proximity;

namespace Sorcha.Proximity.Tests.Support;

/// <summary>
/// A holder and a reader, wired together over <see cref="LoopbackProximityTransport"/> — a complete
/// proximity exchange with no radio.
/// </summary>
public sealed class ProximityFixture : IAsyncDisposable
{
    private ProximityFixture(
        LoopbackProximityTransport holderTransport,
        LoopbackProximityTransport readerTransport,
        ProximityHolderSession holder,
        ProximityReaderSession reader,
        HeldMdoc credential,
        (ECPrivateKeyParameters Private, ECPublicKeyParameters Public) deviceKey)
    {
        HolderTransport = holderTransport;
        ReaderTransport = readerTransport;
        Holder = holder;
        Reader = reader;
        Credential = credential;
        DeviceKey = deviceKey;
    }

    /// <summary>The holder's transport. Carries the fault-injection switches.</summary>
    public LoopbackProximityTransport HolderTransport { get; }

    /// <summary>The reader's transport.</summary>
    public LoopbackProximityTransport ReaderTransport { get; }

    /// <summary>The holder session.</summary>
    public ProximityHolderSession Holder { get; }

    /// <summary>The reader session.</summary>
    public ProximityReaderSession Reader { get; }

    /// <summary>The credential the citizen holds.</summary>
    public HeldMdoc Credential { get; }

    /// <summary>The static ECDH device key the credential is bound to.</summary>
    public (ECPrivateKeyParameters Private, ECPublicKeyParameters Public) DeviceKey { get; }

    private byte[]? _engagement;

    /// <summary>
    /// Creates a fixture. Pass an existing credential and key to reuse them across sessions — which is what
    /// the replay test needs, so that the only thing differing between the two sessions is the session itself.
    /// </summary>
    public static ProximityFixture Create(
        HeldMdoc? credential = null,
        (ECPrivateKeyParameters Private, ECPublicKeyParameters Public)? deviceKey = null)
    {
        var key = deviceKey ?? TestMdoc.GenerateDeviceKey();
        var mdoc = credential ?? TestMdoc.Issue(key.Public);

        var (holderTransport, readerTransport) = LoopbackProximityTransport.CreatePair();

        var holder = new ProximityHolderSession(holderTransport, [mdoc], new HolderDeviceKey(key.Private));
        var reader = new ProximityReaderSession(readerTransport);

        return new ProximityFixture(holderTransport, readerTransport, holder, reader, mdoc, key);
    }

    /// <summary>The reader's ask: an age check it won't retain, and a name it will.</summary>
    public static MdocDeviceRequest StandardRequest() => new()
    {
        DocRequests =
        [
            new ItemsRequest
            {
                DocType = TestMdoc.DocType,
                Elements =
                [
                    new RequestedElement(TestMdoc.Namespace, "age_over_18", IntentToRetain: false),
                    new RequestedElement(TestMdoc.Namespace, "family_name", IntentToRetain: true)
                ]
            }
        ]
    };

    /// <summary>Runs the exchange up to the point where the citizen is being asked to consent.</summary>
    public async Task StartAndRequestAsync()
    {
        _engagement = await Holder.StartAsync();
        await Reader.RequestAsync(_engagement, StandardRequest());
        await Holder.WaitForRequestAsync();
    }

    /// <summary>Runs a complete exchange and returns the reader's verdict.</summary>
    /// <param name="approveAll">Approve everything the reader asked for.</param>
    /// <param name="approve">Or approve selectively.</param>
    public async Task<ProximityVerdict> RunAsync(
        bool approveAll = false, Func<RequestedElement, bool>? approve = null)
    {
        await StartAndRequestAsync();

        var approved = Holder.RequestedElements
            .Where(e => approveAll || (approve?.Invoke(e) ?? false))
            .ToList();

        await Holder.ApproveAsync(approved);

        return await Reader.WaitForVerdictAsync();
    }

    /// <summary>
    /// Decrypts what the holder actually transmitted, so a test can assert on the <b>wire bytes</b> rather
    /// than on the reader's rendering of them.
    /// </summary>
    public byte[] DecryptHolderResponse()
    {
        var sessionData = SessionData.Decode(HolderTransport.Sent[^1]);
        if (sessionData.Data is null)
            throw new InvalidOperationException("The holder's last message carried no data.");

        // Re-derive the reader's view of the keys to open it.
        var transcriptField = typeof(ProximityReaderSession)
            .GetField("_keys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var keys = (MdocSessionKeys)transcriptField.GetValue(Reader)!;

        return MdocSessionCrypto.Decrypt(keys.SkDevice, sessionData.Data, 1, MdocSessionRole.Holder)
            ?? throw new InvalidOperationException("Could not decrypt the holder's response.");
    }

    /// <summary>Delivers raw bytes straight to the reader — for replaying a captured message.</summary>
    public void DeliverToReader(byte[] payload) => HolderTransport.SendAsync(payload).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Holder.DisposeAsync();
        await Reader.DisposeAsync();
    }
}

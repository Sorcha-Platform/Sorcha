// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc.Proximity;
using Sorcha.Proximity;
using Sorcha.Proximity.Tests.Support;
using Xunit;

namespace Sorcha.Proximity.Tests;

/// <summary>
/// Pins the seam that lets a <b>hardware-held</b> device key take part in the <c>EMacKey</c> ECDH.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this seam exists.</b> On a phone the mdoc's static device key is a non-extractable WebCrypto key:
/// .NET can never hold its private half, and that is precisely what makes it worth having. So
/// <see cref="HolderDeviceKey"/> is an <em>agreement operation</em>, not a key.
/// </para>
/// <para>
/// The first cut of this modelled it as an <c>ECPrivateKeyParameters</c>. That compiled, passed every test,
/// and was <b>impossible to wire to the only key that matters</b> — a defect that would not have surfaced
/// until the wallet tried to use its real device key on a real phone. These tests exist so it cannot come back.
/// </para>
/// </remarks>
public class HolderDeviceKeySeamTests
{
    /// <summary>
    /// A holder whose key is reachable ONLY as an async operation — never as bytes — completes the exchange.
    /// </summary>
    /// <remarks>
    /// This is the WebCrypto shape, simulated: the private key is closed over and never handed to the
    /// session. If the session ever needs the key itself rather than the agreement, this test fails.
    /// </remarks>
    [Fact]
    public async Task AHolderWhoseKeyNeverLeavesItsKeystore_CanStillCompleteTheExchange()
    {
        var (privateKey, publicKey) = TestMdoc.GenerateDeviceKey();
        var credential = TestMdoc.Issue(publicKey);

        var agreementCalls = 0;

        // The device key as a phone actually presents it: an operation, behind an async boundary, with the
        // private key sealed away where the session cannot reach it.
        var hardwareHeldKey = new HolderDeviceKey(async (readerEphemeralPublic, ct) =>
        {
            agreementCalls++;
            await Task.Yield();   // crossing the JS-interop boundary is not synchronous
            return await StaticDeviceKeyMaterial
                .ForHolder(privateKey, readerEphemeralPublic)
                .AgreeAsync(ct);
        });

        var (holderTransport, readerTransport) = LoopbackProximityTransport.CreatePair();
        await using var holder = new ProximityHolderSession(holderTransport, [credential], hardwareHeldKey);
        await using var reader = new ProximityReaderSession(readerTransport);

        var engagement = await holder.StartAsync();
        await reader.RequestAsync(engagement, ProximityFixture.StandardRequest());
        await holder.WaitForRequestAsync();

        await holder.ApproveAsync(holder.RequestedElements);

        var verdict = await reader.WaitForVerdictAsync();

        verdict.Accepted.Should().BeTrue(because: string.Join("; ", verdict.Errors));
        verdict.DeviceBindingValid.Should().BeTrue(because:
            "the deviceMac was produced by a key the session never held — which is the entire point");

        agreementCalls.Should().Be(1, because:
            "the ECDH is performed exactly once, by the keystore, and never re-derived from key material we " +
            "do not have");
    }

    /// <summary>
    /// The convenience constructor for in-memory keys is exactly equivalent — so tests and the server are not
    /// exercising a different code path from the phone.
    /// </summary>
    [Fact]
    public async Task AnInMemoryKeyAndAnOperationBackedKey_ProduceTheSameSharedSecret()
    {
        var (privateKey, _) = TestMdoc.GenerateDeviceKey();
        var (_, readerEphemeralPublic) = TestMdoc.GenerateDeviceKey();

        var fromConvenience = HolderDeviceKey.FromInMemoryKey(privateKey);

        var fromOperation = new HolderDeviceKey((peer, ct) =>
            StaticDeviceKeyMaterial.ForHolder(privateKey, peer).AgreeAsync(ct));

        var a = await fromConvenience.AgreeWithReaderEphemeral(readerEphemeralPublic, default);
        var b = await fromOperation.AgreeWithReaderEphemeral(readerEphemeralPublic, default);

        a.Should().Equal(b);
        a.Should().HaveCount(32, because: "the shared secret is the P-256 x-coordinate");
    }

    /// <summary>
    /// A device key that cannot agree (revoked, or a keystore failure) must fail the session — <b>not</b>
    /// silently fall back to producing an unauthenticated presentation.
    /// </summary>
    [Fact]
    public async Task AKeystoreFailure_FailsTheSession_RatherThanDiscloseWithoutBinding()
    {
        var (_, publicKey) = TestMdoc.GenerateDeviceKey();
        var credential = TestMdoc.Issue(publicKey);

        var brokenKey = new HolderDeviceKey((_, _) =>
            Task.FromException<byte[]>(new InvalidOperationException("keystore unavailable")));

        var (holderTransport, readerTransport) = LoopbackProximityTransport.CreatePair();
        await using var holder = new ProximityHolderSession(holderTransport, [credential], brokenKey);
        await using var reader = new ProximityReaderSession(readerTransport);

        var engagement = await holder.StartAsync();
        await reader.RequestAsync(engagement, ProximityFixture.StandardRequest());

        var act = () => holder.WaitForRequestAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        holder.State.Should().Be(HolderSessionState.Failed);
        holderTransport.Sent.Should().BeEmpty(because:
            "a holder that cannot prove possession of its device key must disclose NOTHING — an unbound " +
            "presentation is worse than no presentation");
    }
}

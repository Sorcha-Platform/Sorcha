// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Mdoc;
using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Proximity;
using Sorcha.Proximity;
using Sorcha.Proximity.Tests.Support;
using Xunit;

namespace Sorcha.Proximity.Tests;

/// <summary>
/// The whole ISO 18013-5 proximity exchange, holder to reader, <b>with no Bluetooth and no phones</b>.
/// </summary>
/// <remarks>
/// This is the de-risking that the entire feature plan is built around (FR-022 / SC-002). Engagement, ECDH,
/// session encryption, the request, consent, selective disclosure, <c>DeviceResponse</c> assembly,
/// <c>deviceMac</c>, and verification all run here, in CI, on every change. What is left for the phones is
/// moving bytes — which fails visibly, unlike the protocol, which fails silently.
/// </remarks>
public class FullExchangeTests
{
    /// <summary>US1 scenario 1 — the exchange completes and the reader gets exactly what it asked for.</summary>
    [Fact]
    public async Task Exchange_Completes_AndTheReaderReceivesExactlyWhatWasApproved()
    {
        await using var fixture = ProximityFixture.Create();

        var verdict = await fixture.RunAsync(approveAll: true);

        verdict.Accepted.Should().BeTrue(because: string.Join("; ", verdict.Errors));
        verdict.IssuerSignatureValid.Should().BeTrue();
        verdict.DigestsValid.Should().BeTrue();
        verdict.DeviceBindingValid.Should().BeTrue(because:
            "the holder proved possession via deviceMac — the path feature 135 could not verify at all");
        verdict.ValidityOk.Should().BeTrue();

        verdict.DisclosedClaims.Keys.Should().BeEquivalentTo(["family_name", "age_over_18"]);
    }

    /// <summary>
    /// SC-005 — disclosure minimality, asserted on <b>what crossed the wire</b>, not on what the reader
    /// chose to render.
    /// </summary>
    /// <remarks>
    /// A reader that displays only the claims it asked for would look correct even if the holder had sent the
    /// entire credential. The only honest place to check this is the transmitted bytes.
    /// </remarks>
    [Fact]
    public async Task Exchange_DisclosesOnlyApprovedElements_CheckedOnTheWire()
    {
        await using var fixture = ProximityFixture.Create();

        // The citizen approves only the age check — not their name, and not the birth date the credential
        // also contains.
        var verdict = await fixture.RunAsync(approve: e => e.ElementIdentifier == "age_over_18");

        verdict.Accepted.Should().BeTrue(because: string.Join("; ", verdict.Errors));
        verdict.DisclosedClaims.Should().ContainKey("age_over_18");
        verdict.DisclosedClaims.Should().NotContainKey("family_name");
        verdict.DisclosedClaims.Should().NotContainKey("birth_date");

        // And prove it by inspecting the raw response the holder actually transmitted.
        var responseBytes = fixture.DecryptHolderResponse();
        var response = MdocCodec.DecodeDeviceResponse(responseBytes);

        var transmitted = response.Documents[0].IssuerSigned.NameSpaces
            .SelectMany(ns => ns.Value.Select(i => i.Item.ElementIdentifier))
            .ToList();

        transmitted.Should().BeEquivalentTo(["age_over_18"], because:
            "the withheld elements must be ABSENT FROM THE TRANSMITTED BYTES — not merely absent from the " +
            "reader's display");
    }

    /// <summary>US1 scenario 2 — a single flipped bit in the response is rejected.</summary>
    [Fact]
    public async Task Exchange_TamperedResponse_IsRejected()
    {
        await using var fixture = ProximityFixture.Create();

        fixture.HolderTransport.CorruptNextMessage = true;   // flip one bit in flight

        var act = () => fixture.RunAsync(approveAll: true);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*did not authenticate*", because:
                "AES-GCM authenticates the ciphertext; a single flipped bit must fail the tag");

        fixture.Reader.State.Should().Be(ReaderSessionState.Failed);
    }

    /// <summary>
    /// US1 scenario 3 — a response captured from one session is worthless in another.
    /// </summary>
    /// <remarks>
    /// This is the property the whole session-transcript apparatus exists to provide. The captured response
    /// is replayed verbatim into a completely fresh session; its <c>deviceMac</c> was computed over the old
    /// session's transcript, so it cannot authenticate under the new one.
    /// </remarks>
    [Fact]
    public async Task Exchange_ReplayedIntoAFreshSession_IsRejected()
    {
        // Session 1: capture a genuine, valid response.
        await using var first = ProximityFixture.Create();
        var firstVerdict = await first.RunAsync(approveAll: true);
        firstVerdict.Accepted.Should().BeTrue(because: "the captured response must be genuinely valid, or " +
            "this test proves nothing about replay");

        var capturedSessionData = first.HolderTransport.Sent[^1];

        // Session 2: same credential, same device key — a completely fresh exchange.
        await using var second = ProximityFixture.Create(first.Credential, first.DeviceKey);
        await second.StartAndRequestAsync();

        // Replay the captured bytes verbatim.
        second.DeliverToReader(capturedSessionData);

        second.Reader.State.Should().Be(ReaderSessionState.Failed);
        second.Reader.FailureReason.Should().Contain("did not authenticate", because:
            "the replayed response was encrypted under the FIRST session's keys, which were bound to the " +
            "first session's transcript — the whole point of the transcript");
    }

    /// <summary>FR-010 — declining discloses nothing. Checked on the wire, not on intent.</summary>
    [Fact]
    public async Task Decline_TransmitsNoCredentialData()
    {
        await using var fixture = ProximityFixture.Create();
        await fixture.StartAndRequestAsync();

        fixture.Holder.State.Should().Be(HolderSessionState.AwaitingConsent);

        await fixture.Holder.DeclineAsync();

        fixture.Holder.State.Should().Be(HolderSessionState.Declined);

        // The only thing the holder ever sent is a status-only termination.
        fixture.HolderTransport.Sent.Should().HaveCount(1);
        var sent = SessionData.Decode(fixture.HolderTransport.Sent[0]);
        sent.Data.Should().BeNull(because: "a decline carries a status and NO data — not even encrypted data");
        sent.Status.Should().Be(SessionData.StatusSessionTermination);
    }

    /// <summary>
    /// The disclosure invariant is structural: there is no way to encode credential data except by approving.
    /// </summary>
    [Fact]
    public async Task Approve_FromAnyStateOtherThanAwaitingConsent_IsRefused()
    {
        await using var fixture = ProximityFixture.Create();

        // Before the reader has even asked.
        var act = () => fixture.Holder.ApproveAsync([]);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*only ever encoded out of AwaitingConsent*");
    }

    /// <summary>The holder refuses to disclose anything the reader never asked for, even if told to.</summary>
    [Fact]
    public async Task Approve_WithAnUnrequestedElement_IsRefused()
    {
        await using var fixture = ProximityFixture.Create();
        await fixture.StartAndRequestAsync();

        var sneaky = new RequestedElement(TestMdoc.Namespace, "birth_date", IntentToRetain: true);

        var act = () => fixture.Holder.ApproveAsync([sneaky]);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*never asked for*", because:
                "a bug in the consent UI must not be able to widen disclosure beyond the request");
    }

    /// <summary>The citizen is shown every element asked for, and whether the reader intends to keep it.</summary>
    [Fact]
    public async Task RequestedElements_SurfaceIntentToRetain_ForTheConsentScreen()
    {
        await using var fixture = ProximityFixture.Create();
        await fixture.StartAndRequestAsync();

        fixture.Holder.RequestedElements.Should().HaveCount(2);

        var ageCheck = fixture.Holder.RequestedElements.Single(e => e.ElementIdentifier == "age_over_18");
        ageCheck.IntentToRetain.Should().BeFalse();

        var name = fixture.Holder.RequestedElements.Single(e => e.ElementIdentifier == "family_name");
        name.IntentToRetain.Should().BeTrue(because:
            "'prove you are over 18' and 'prove you are over 18 and I am keeping your name' are materially " +
            "different asks — the citizen is entitled to see which one they are agreeing to");
    }

    /// <summary>
    /// A response that discloses <b>nothing</b> must not be reported as an acceptance.
    /// </summary>
    /// <remarks>
    /// The format-level digest check iterates the elements that are present, so an empty disclosure satisfies
    /// it <em>vacuously</em> — all zero of its digests match. Cryptographically that is sound, but reporting
    /// "Accepted" over an empty claim set would tell an operator their check succeeded when they in fact
    /// learned nothing at all. This is the kind of pass that is worse than a failure, because it is silent.
    /// </remarks>
    [Fact]
    public async Task Exchange_WhereTheCitizenApprovesNothing_IsNotReportedAsAccepted()
    {
        await using var fixture = ProximityFixture.Create();

        var verdict = await fixture.RunAsync(approve: _ => false);   // approves nothing

        verdict.DisclosedClaims.Should().BeEmpty();
        verdict.Accepted.Should().BeFalse(because:
            "zero disclosed elements means nothing was proven — a vacuous digest check is not an acceptance");
        verdict.Errors.Should().ContainMatch("*disclosed no elements*");
    }

    /// <summary>The peer walking away mid-exchange leaves a clean terminal state and discloses nothing.</summary>
    [Fact]
    public async Task PeerDisconnect_MidExchange_AbandonsCleanly()
    {
        await using var fixture = ProximityFixture.Create();
        await fixture.StartAndRequestAsync();

        await fixture.ReaderTransport.StopAsync();   // the reader walks away

        fixture.Holder.State.Should().Be(HolderSessionState.Abandoned);
        fixture.HolderTransport.Sent.Should().BeEmpty(because: "nothing was ever disclosed");
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Mdoc.Proximity;
using Sorcha.Proximity;

namespace Sorcha.Reader.Services;

/// <summary>What the verifier wants to know. Deliberately few, and phrased as questions, not fields.</summary>
/// <remarks>
/// <para>
/// The presets exist to make the <b>minimal</b> ask the <b>easy</b> ask. A verifier confronted with a list of
/// checkboxes will tend to tick more than they need; a verifier asked "what do you need to know?" tends to
/// need one thing.
/// </para>
/// <para>
/// "Are they over 18?" therefore requests <c>age_over_18</c> and <b>nothing else</b> — not date of birth, from
/// which the answer could be derived along with a great deal more. That distinction is the entire point of an
/// age-attestation element existing in the standard at all.
/// </para>
/// </remarks>
public sealed record ReaderQuestion(string Id, string Label, string Detail, IReadOnlyList<RequestedElement> Elements)
{
    private const string Ns = "org.iso.18013.5.1.mDL";

    /// <summary>The minimal age check: the answer, and not the birth date it could be derived from.</summary>
    public static readonly ReaderQuestion OverEighteen = new(
        "over-18",
        "Are they over 18?",
        "Asks only for a yes/no. Not their date of birth.",
        [new RequestedElement(Ns, "age_over_18", IntentToRetain: false)]);

    /// <summary>Identity confirmation: name and photo, kept only as long as the check.</summary>
    public static readonly ReaderQuestion ConfirmIdentity = new(
        "identity",
        "Is this them?",
        "Asks for their name and photo.",
        [
            new RequestedElement(Ns, "given_name", IntentToRetain: false),
            new RequestedElement(Ns, "family_name", IntentToRetain: false),
            new RequestedElement(Ns, "portrait", IntentToRetain: false)
        ]);

    /// <summary>A licence check that the verifier will keep a record of.</summary>
    /// <remarks>
    /// <c>IntentToRetain</c> is <b>true</b> here, and truthfully so. Declaring it lets the citizen see it
    /// before consenting — which is the whole reason the flag exists, and lying about it would be worse than
    /// not asking.
    /// </remarks>
    public static readonly ReaderQuestion LicenceOnRecord = new(
        "licence-record",
        "Check their licence, and keep a record",
        "Asks for their name, document number and expiry — and tells them you will keep it.",
        [
            new RequestedElement(Ns, "given_name", IntentToRetain: true),
            new RequestedElement(Ns, "family_name", IntentToRetain: true),
            new RequestedElement(Ns, "document_number", IntentToRetain: true),
            new RequestedElement(Ns, "expiry_date", IntentToRetain: true)
        ]);

    /// <summary>Every preset, in the order they should be offered.</summary>
    public static IReadOnlyList<ReaderQuestion> All => [OverEighteen, ConfirmIdentity, LicenceOnRecord];

    /// <summary>The mdoc document type this question asks about.</summary>
    public string DocType => Ns;
}

/// <summary>Drives one in-person read, from the verifier's point of view.</summary>
public interface IReaderService : IAsyncDisposable
{
    /// <summary>What this device can do. Ask before offering to read at all.</summary>
    Task<ProximityCapability> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the credential: connects to the holder's advertised service, asks
    /// <paramref name="question"/>, and returns the verdict.
    /// </summary>
    /// <param name="engagementQrPayload">
    /// The scanned QR payload — the <c>mdoc:</c> URI the holder is displaying.
    /// </param>
    Task<ProximityVerdict> ReadAsync(
        string engagementQrPayload, ReaderQuestion question, CancellationToken ct = default);

    /// <summary>Where the read is.</summary>
    ReaderSessionState State { get; }

    /// <summary>Why it failed, when it did.</summary>
    string? FailureReason { get; }
}

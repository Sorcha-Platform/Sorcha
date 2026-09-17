// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Service.Models.Responses;

/// <summary>
/// What a caller needs to build a valid <c>POST /api/instances/{instanceId}/actions/{actionId}/execute</c>
/// request (#1658). The execute endpoint binds <c>ActionSubmissionRequest</c>, which requires the
/// blueprint id, the register address and the sender wallet as well as the payload; none of those were
/// readable by a participant who only held an instance id and an action id.
/// </summary>
/// <remarks>
/// The sender wallet is ADVICE, not authority. The execute path still checks that the caller owns the
/// wallet (SEC-006), the validator re-checks sender authorisation on the ledger (<c>VAL_BP_002</c>), and
/// the Wallet Service checks ownership again at signing time. The value here is computed by
/// <see cref="Services.Implementation.SenderWalletResolver"/> to agree with the VALIDATOR's rule, so a
/// client that follows it is not accepted and then silently refused (#1664).
/// </remarks>
public sealed record ActionSubmissionContext
{
    /// <summary>The blueprint the instance runs. Send as <c>blueprintId</c>.</summary>
    public required string BlueprintId { get; init; }

    /// <summary>The register the instance lives on. Send as <c>registerAddress</c>.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Whether a sender wallet could be chosen for the caller, and if not, why.</summary>
    public required SenderWalletStatus SenderWalletStatus { get; init; }

    /// <summary>
    /// The caller's wallet to send as <c>senderWallet</c>. Null unless
    /// <see cref="SenderWalletStatus"/> is <see cref="SenderWalletStatus.Resolved"/>.
    /// </summary>
    public string? SenderWallet { get; init; }

    /// <summary>
    /// The caller's own wallets that could submit, when there is more than one and nothing decides between
    /// them. Only ever the caller's wallets: another participant's wallet is never listed.
    /// </summary>
    public IReadOnlyList<string> CandidateWallets { get; init; } = [];

    /// <summary>
    /// The blueprint participant this action's sender names, when nothing binds it to a wallet yet
    /// (<see cref="SenderWalletStatus.AwaitingParticipantRecord"/>). It is the role a participant record
    /// must be published for, so the caller knows exactly what is missing. Null otherwise.
    /// </summary>
    public string? UnboundParticipantId { get; init; }
}

/// <summary>Outcome of choosing which of the caller's wallets submits an action.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SenderWalletStatus>))]
public enum SenderWalletStatus
{
    /// <summary>Exactly one of the caller's wallets can submit; it is <see cref="ActionSubmissionContext.SenderWallet"/>.</summary>
    [JsonStringEnumMemberName("resolved")]
    Resolved,

    /// <summary>
    /// The sender is not yet bound and the caller holds several wallets. Submitting binds the chosen wallet
    /// to the participant for the life of the instance, so the caller must choose.
    /// </summary>
    [JsonStringEnumMemberName("ambiguous")]
    Ambiguous,

    /// <summary>The action's sender is bound to a wallet the caller does not hold.</summary>
    [JsonStringEnumMemberName("notYours")]
    NotYours,

    /// <summary>No wallet could be resolved for the caller.</summary>
    [JsonStringEnumMemberName("noWallet")]
    NoWallet,

    /// <summary>
    /// Nothing binds this action's sender to a wallet, and the action is not a starting action, so it
    /// cannot late-bind one (#1664). NOBODY can submit it until the participating organisation publishes a
    /// participant record for <see cref="ActionSubmissionContext.UnboundParticipantId"/> on this register.
    /// Submitting anyway is accepted with a 202 and then refused by the validator with <c>VAL_BP_002</c>,
    /// a refusal that reaches no audit log.
    /// </summary>
    [JsonStringEnumMemberName("awaitingParticipantRecord")]
    AwaitingParticipantRecord,
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Prompts;

/// <summary>
/// Guided recipes for the three most common agent-facing Sorcha workflows: standing up a
/// two-party exchange, issuing a credential, and assembling proof of a sealed transaction for a
/// regulator. Each prompt returns a step-by-step brief — it does not itself call any tool — that
/// names the resources to read first, walks the lifecycle tools in order, and says which steps
/// put a decision to a person rather than an agent.
/// </summary>
[McpServerPromptType]
public static class SorchaPrompts
{
    // Ruling 5 (task-8): "your client does not support elicitation" understates the failure
    // surface — a client can DECLARE the elicitation capability and still refuse every request
    // automatically when running headlessly (Claude Code in `-p` mode answers `cancel` to every
    // elicit without a person ever seeing it). Only an explicit `accept` approves; `decline`, a
    // silent `cancel`, and "capability never declared" are three different states that all
    // refuse the same way. Every prompt repeats this so an agent reading only one of them still
    // gets the full picture.
    private const string HumanGateReminder =
        "Register creation, and publishing a blueprint that has never been rehearsed, put the " +
        "decision to a person via MCP elicitation and proceed only on an explicit `accept` to " +
        "confirm — a decline, a silent cancel, and a client that never declared the elicitation " +
        "capability all refuse in the same way. A client can declare the capability and still " +
        "auto-cancel every request when running headlessly (Claude Code in `-p` mode does " +
        "exactly this), so do not assume elicitation support means a person will actually be " +
        "asked; expect the tool to refuse cleanly rather than proceed unsupervised.";

    /// <summary>
    /// Guides an agent through setting up a two-party data exchange with selective disclosure,
    /// from an empty workspace to a running instance.
    /// </summary>
    /// <param name="firstParty">The organisation that starts the exchange.</param>
    /// <param name="secondParty">The organisation that responds.</param>
    /// <param name="subject">What is being exchanged, e.g. 'invoice totals'.</param>
    /// <returns>A step-by-step brief naming the resources and tools to use, in order.</returns>
    [McpServerPrompt(Name = "sorcha_two_party_exchange")]
    [Description("Set up a two-party data exchange with selective disclosure between two organisations.")]
    public static string TwoPartyExchange(
        [Description("The organisation that starts the exchange")] string firstParty,
        [Description("The organisation that responds")] string secondParty,
        [Description("What is being exchanged, e.g. 'invoice totals'")] string subject)
        => $"""
            Set up a two-party exchange of {subject} between {firstParty} and {secondParty}.

            1. Read sorcha://schema/blueprint before writing anything. It is accurate for
               participants, actions and data schemas, but does not define `routes` or
               `isStartingAction`, so also read a worked example such as
               sorcha://examples/ping-pong for those two constructs.
            2. sorcha_blueprint_create — define a blueprint with {firstParty} and {secondParty}
               as participants, one action per side of the exchange, and a data schema scoped
               to {subject}. Connect the actions with `routes` and mark exactly one action
               `isStartingAction`.
            3. sorcha_register_create — create the ledger this exchange runs on.
            4. sorcha_blueprint_publish — publish the definition to that register.
            5. sorcha_instance_create — start a running instance from the published definition.
            6. sorcha_action_submit — {firstParty} submits the first action's data, then
               {secondParty} submits theirs in turn.

            {HumanGateReminder}
            """;

    /// <summary>
    /// Guides an agent through issuing a verifiable credential from an issuer organisation to a
    /// subject, using an action-level <c>credentialIssuanceConfig</c>.
    /// </summary>
    /// <param name="issuer">The organisation issuing the credential.</param>
    /// <param name="subject">Who or what the credential is about.</param>
    /// <param name="credentialType">The credential type being issued, e.g. 'ProofOfAddress'.</param>
    /// <returns>A step-by-step brief naming the resources and tools to use, in order.</returns>
    [McpServerPrompt(Name = "sorcha_issue_credential")]
    [Description("Issue a verifiable credential from an issuer organisation to a subject, with selective disclosure of its claims.")]
    public static string IssueCredential(
        [Description("The organisation issuing the credential")] string issuer,
        [Description("Who or what the credential is about")] string subject,
        [Description("The credential type being issued, e.g. 'ProofOfAddress'")] string credentialType)
        => $"""
            Issue a {credentialType} credential from {issuer} to {subject}.

            1. Read sorcha://schema/blueprint, then sorcha://examples/assured-identity. The
               schema does not define `credentialIssuanceConfig`; that example is the working
               reference for it, including selective disclosure of its claims.
            2. sorcha_blueprint_create — define an application/approval workflow: one action
               where {subject} submits the {credentialType} claims, and an approval action on
               {issuer}'s side carrying `credentialIssuanceConfig`. Set an `issuanceCondition`
               so a declined applicant is never issued a credential.
            3. sorcha_register_create — create the ledger this issuance workflow runs on.
            4. sorcha_blueprint_publish — publish the definition to that register.
            5. sorcha_instance_create — start a running instance.
            6. sorcha_action_submit — {subject} submits their claims, then {issuer} submits the
               approval action, which mints the credential per `credentialIssuanceConfig`.

            If {subject} holds a standards-compliant external wallet rather than a Sorcha
            participant identity, use sorcha_credential_offer after approval to push an OID4VCI
            offer they can collect by scanning a QR code, instead of a direct action submission.

            {HumanGateReminder}
            """;

    /// <summary>
    /// Guides an agent through assembling verifiable proof of a sealed transaction for a
    /// regulator or auditor.
    /// </summary>
    /// <param name="registerId">The register the transaction was sealed on.</param>
    /// <param name="transactionId">The transaction to prove.</param>
    /// <returns>A step-by-step brief naming the resources and tools to use, in order.</returns>
    [McpServerPrompt(Name = "sorcha_prove_to_regulator")]
    [Description("Assemble verifiable proof of a sealed transaction — inclusion proof, verification bundle, and any data the regulator is entitled to see — for a regulator or auditor.")]
    public static string ProveToRegulator(
        [Description("The register the transaction was sealed on")] string registerId,
        [Description("The transaction to prove")] string transactionId)
        => $"""
            Assemble proof of transaction {transactionId} on register {registerId} for a
            regulator.

            Steps 2-4 (sorcha_transaction_status, sorcha_transaction_inclusion_proof,
            sorcha_transaction_verification_bundle) are Administrator-entitled tools. Prompts are
            not entitlement-filtered the way tools are, so a designer- or participant-only caller
            will be offered this recipe but find those three steps missing from their own tool
            list — that is not a bug in the recipe, it means an administrator must run this part.

            1. Read sorcha://glossary if you need a refresher on docket, publicationTxId and
               execDefHash first — proof of a transaction is proof of the docket that sealed it.
            2. sorcha_transaction_status — confirm transaction {transactionId} is sealed, not
               pending, before building proof around it; an unsealed transaction has nothing to
               prove yet.
            3. sorcha_transaction_inclusion_proof — get the inclusion proof tying
               {transactionId} to its sealed docket on register {registerId}.
            4. sorcha_transaction_verification_bundle — get the full verification bundle (the
               inclusion proof plus the validator signatures over the docket) a regulator can
               check independently, without taking this server's word for it.
            5. sorcha_disclosed_data — if the regulator is entitled to see the underlying
               payload rather than only proof it exists, fetch what is disclosed to your
               identity; this returns exactly what your disclosure scope allows, never the full
               sealed payload.

            These are read-only lookups and need no person to confirm them. But if the trail
            leads you to conclude the workflow itself needs to change — reissuing a corrected
            credential, say — that goes through sorcha_register_create or
            sorcha_blueprint_publish. {HumanGateReminder}
            """;
}

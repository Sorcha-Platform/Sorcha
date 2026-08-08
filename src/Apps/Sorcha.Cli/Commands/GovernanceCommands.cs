// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;
using Sorcha.Register.Models;
using Sorcha.Wallet.Contracts.Constants;
using Sorcha.Wallet.Contracts.Models;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Register governance commands (Feature 189).
/// </summary>
public class GovernanceCommand : Command
{
    public GovernanceCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("governance",
            "Approve and inspect register governance proposals\n\nExamples:\n"
            + "  sorcha governance proposals --register <register-id>\n"
            + "  sorcha governance show --register <register-id> --proposal <tx-id> --as <org-wallet>\n"
            + "  sorcha governance approve --register <register-id> --proposal <tx-id> --as <org-wallet> --individual <wallet>")
    {
        Subcommands.Add(new GovernanceProposalsCommand(clientFactory, authService, configService));
        Subcommands.Add(new GovernanceShowCommand(clientFactory, authService, configService));
        Subcommands.Add(new GovernanceApproveCommand(clientFactory, authService, configService));
    }
}

/// <summary>Lists the governance proposals recorded on a register.</summary>
public class GovernanceProposalsCommand : Command
{
    private readonly Option<string> _registerOption;

    public GovernanceProposalsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("proposals", "List governance proposals on a register")
    {
        _registerOption = new Option<string>("--register", "-r") { Description = "Register id", Required = true };
        Options.Add(_registerOption);

        this.SetAction(async (parseResult, _) =>
        {
            var registerId = parseResult.GetValue(_registerOption)!;

            var (token, profileName, error) = await GovernanceCliHelpers
                .AuthenticateAsync(authService, configService);
            if (error != 0) return error;

            var registerClient = await clientFactory.CreateRegisterServiceClientAsync(profileName);

            try
            {
                var response = await registerClient.ListGovernanceProposalsAsync(registerId, $"Bearer {token}");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"Could not list proposals ({(int)response.StatusCode}): {body}");
                    return ExitCodes.GeneralError;
                }

                ConsoleHelper.WriteInfo(body);
                return ExitCodes.Success;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"Register service returned {(int)ex.StatusCode}: {ex.Content}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Shows exactly what an approver would be signing, without signing it.
/// </summary>
/// <remarks>
/// FR-027: signing an opaque value is not approval. This is the read-only half of
/// <see cref="GovernanceApproveCommand"/>, so an operator (or a bot's author) can inspect a proposal
/// and its locally-derived digest before committing to it.
/// </remarks>
public class GovernanceShowCommand : Command
{
    private readonly Option<string> _registerOption;
    private readonly Option<string> _proposalOption;
    private readonly Option<string> _asOption;

    public GovernanceShowCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("show", "Show what an approver would sign for a proposal, and the digest it derives")
    {
        _registerOption = new Option<string>("--register", "-r") { Description = "Register id", Required = true };
        _proposalOption = new Option<string>("--proposal", "-p") { Description = "Proposal transaction id", Required = true };
        _asOption = new Option<string>("--as") { Description = "Approving organisation's wallet address", Required = true };
        Options.Add(_registerOption);
        Options.Add(_proposalOption);
        Options.Add(_asOption);

        this.SetAction(async (parseResult, _) =>
        {
            var registerId = parseResult.GetValue(_registerOption)!;
            var proposalId = parseResult.GetValue(_proposalOption)!;
            var approverDid = GovernanceCliHelpers.ToWalletDid(parseResult.GetValue(_asOption)!);

            var (token, profileName, error) = await GovernanceCliHelpers
                .AuthenticateAsync(authService, configService);
            if (error != 0) return error;

            var registerClient = await clientFactory.CreateRegisterServiceClientAsync(profileName);

            var request = await GovernanceCliHelpers.FetchSigningRequestAsync(
                registerClient, registerId, proposalId, approverDid, token!);
            if (request is null) return ExitCodes.GeneralError;

            GovernanceCliHelpers.RenderOperation(request, approverDid);

            var approveDigest = GovernanceApprovalStatement.ComputeDigest(
                registerId, request.Operation, approverDid, isApproval: true);
            var rejectDigest = GovernanceApprovalStatement.ComputeDigest(
                registerId, request.Operation, approverDid, isApproval: false);

            ConsoleHelper.WriteInfo("");
            ConsoleHelper.WriteInfo("Digests derived locally from the operation above:");
            ConsoleHelper.WriteInfo($"  approve : {Convert.ToHexString(approveDigest).ToLowerInvariant()}");
            ConsoleHelper.WriteInfo($"  reject  : {Convert.ToHexString(rejectDigest).ToLowerInvariant()}");

            return ExitCodes.Success;
        });
    }
}

/// <summary>
/// Signs and submits a detached approval of a governance proposal (T082).
/// </summary>
/// <remarks>
/// <para>
/// This is the autonomous-approver path, and the same protocol the console and the wallet PWA speak
/// — one protocol, several clients (R-016). Two signatures are produced over the <b>same</b>
/// statement: the organisation's slot-100 key carries the authority, and a named individual's own key
/// carries the accountability. Every approval must resolve to a person (FR-029), so
/// <c>--individual</c> is not optional.
/// </para>
/// <para>
/// <b>Detached in protocol, not yet in custody.</b> The keys here are server-custodied wallets, so
/// this exercises the wire protocol end to end without proving that the server cannot produce an
/// approval on its own. A genuinely external holder signs the same digest with a key the platform
/// never sees and posts the same submission — nothing on the server side can tell the difference,
/// which is the point of a detached protocol.
/// </para>
/// </remarks>
public class GovernanceApproveCommand : Command
{
    private readonly Option<string> _registerOption;
    private readonly Option<string> _proposalOption;
    private readonly Option<string> _asOption;
    private readonly Option<string> _individualOption;
    private readonly Option<bool> _rejectOption;
    private readonly Option<string?> _commentOption;
    private readonly Option<bool> _yesOption;

    public GovernanceApproveCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("approve", "Review, sign and submit an approval (or rejection) of a governance proposal")
    {
        _registerOption = new Option<string>("--register", "-r") { Description = "Register id", Required = true };
        _proposalOption = new Option<string>("--proposal", "-p") { Description = "Proposal transaction id", Required = true };
        _asOption = new Option<string>("--as")
        {
            Description = "Approving organisation's wallet address (its slot-100 key signs)",
            Required = true
        };
        _individualOption = new Option<string>("--individual")
        {
            Description = "Wallet address of the person accountable for this approval",
            Required = true
        };
        _rejectOption = new Option<bool>("--reject") { Description = "Record a rejection instead of an approval" };
        _commentOption = new Option<string?>("--comment") { Description = "Free text recorded with the vote" };
        _yesOption = new Option<bool>("--yes", "-y") { Description = "Skip the confirmation prompt" };

        Options.Add(_registerOption);
        Options.Add(_proposalOption);
        Options.Add(_asOption);
        Options.Add(_individualOption);
        Options.Add(_rejectOption);
        Options.Add(_commentOption);
        Options.Add(_yesOption);

        this.SetAction(async (parseResult, _) =>
        {
            var registerId = parseResult.GetValue(_registerOption)!;
            var proposalId = parseResult.GetValue(_proposalOption)!;
            var orgWallet = parseResult.GetValue(_asOption)!;
            var individualWallet = parseResult.GetValue(_individualOption)!;
            var isApproval = !parseResult.GetValue(_rejectOption);
            var comment = parseResult.GetValue(_commentOption);
            var skipPrompt = parseResult.GetValue(_yesOption);

            var approverDid = GovernanceCliHelpers.ToWalletDid(orgWallet);
            var individualDid = GovernanceCliHelpers.ToWalletDid(individualWallet);

            var (token, profileName, error) = await GovernanceCliHelpers
                .AuthenticateAsync(authService, configService);
            if (error != 0) return error;

            var registerClient = await clientFactory.CreateRegisterServiceClientAsync(profileName);
            var walletClient = await clientFactory.CreateWalletServiceClientAsync(profileName);

            var request = await GovernanceCliHelpers.FetchSigningRequestAsync(
                registerClient, registerId, proposalId, approverDid, token!);
            if (request is null) return ExitCodes.GeneralError;

            // FR-027: render before signing. An approver who cannot see what they authorise has not
            // approved it, whatever the signature says.
            GovernanceCliHelpers.RenderOperation(request, approverDid);
            ConsoleHelper.WriteInfo("");
            ConsoleHelper.WriteInfo($"  Vote        : {(isApproval ? "APPROVE" : "REJECT")}");
            ConsoleHelper.WriteInfo($"  Accountable : {individualDid}");

            if (!skipPrompt)
            {
                Console.Write($"\nSign this as {approverDid}? [y/N] ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleHelper.WriteInfo("Abandoned. Nothing was signed or submitted.");
                    return ExitCodes.Success;
                }
            }

            // Derived locally from the operation just rendered — never taken from the server, so the
            // two cannot disagree about what is being authorised (FR-028).
            var digest = GovernanceApprovalStatement.ComputeDigest(
                registerId, request.Operation, approverDid, isApproval);
            var digestBase64 = Convert.ToBase64String(digest);

            SignTransactionResponse orgSignature;
            SignTransactionResponse individualSignature;
            try
            {
                // Slot 100 — the key the register's roster records for this organisation. Signing with
                // the wallet's primary key here produces a signature the Validator cannot match.
                orgSignature = await walletClient.SignTransactionAsync(orgWallet, new SignTransactionRequest
                {
                    TransactionData = digestBase64,
                    DerivationPath = SorchaDerivationPaths.RegisterAttestation,
                    IsPreHashed = true
                }, $"Bearer {token}");

                // The individual signs with their wallet's PRIMARY key, deliberately: a wallet address
                // derives from that key, so re-deriving it is what binds the signature to the
                // did:sorcha:w:{address} named as accountable (FR-035). A derived-slot key would not
                // reproduce the address and the approval would be refused.
                individualSignature = await walletClient.SignTransactionAsync(individualWallet, new SignTransactionRequest
                {
                    TransactionData = digestBase64,
                    DerivationPath = null,
                    IsPreHashed = true
                }, $"Bearer {token}");
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError("A wallet in this approval was not found. Check --as and --individual.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"Wallet service returned {(int)ex.StatusCode}: {ex.Content}");
                return ExitCodes.GeneralError;
            }

            if (string.IsNullOrWhiteSpace(orgSignature.PublicKey)
                || string.IsNullOrWhiteSpace(individualSignature.PublicKey))
            {
                // Without the public keys the submission cannot be matched to the roster or bound to
                // the individual, and a submission that will certainly be refused is not worth sending.
                ConsoleHelper.WriteError(
                    "The wallet service did not return a public key for one of the signatures, so the "
                    + "approval cannot be bound to the roster or to the individual.");
                return ExitCodes.GeneralError;
            }

            var submission = new GovernanceApprovalSubmission
            {
                RequestId = proposalId,
                ApproverDid = approverDid,
                IsApproval = isApproval,
                Signature = orgSignature.Signature,
                PublicKey = orgSignature.PublicKey!,
                AuthMethod = ApprovalAuthMethod.Service,
                Comment = comment,
                Authorisation = new ApprovalAuthorisation
                {
                    Kind = AuthorisationKind.Direct,
                    IndividualDid = individualDid,
                    Signature = individualSignature.Signature,
                    PublicKey = individualSignature.PublicKey!,
                    AuthMethod = ApprovalAuthMethod.Service,
                    Algorithm = SignatureAlgorithm.ED25519,
                }
            };

            try
            {
                var response = await registerClient.ApproveGovernanceProposalAsync(
                    registerId, proposalId, submission, $"Bearer {token}");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"Approval refused ({(int)response.StatusCode}): {body}");
                    return response.StatusCode switch
                    {
                        HttpStatusCode.NotFound => ExitCodes.NotFound,
                        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => ExitCodes.AuthenticationError,
                        _ => ExitCodes.GeneralError,
                    };
                }

                ConsoleHelper.WriteSuccess("Approval submitted.");
                ConsoleHelper.WriteInfo(body);
                // 202, not 200 — worth stating, because "accepted" reads like "done".
                ConsoleHelper.WriteInfo(
                    "\nIt counts once it seals into a docket. Check the register's transactions, not this response.");
                return ExitCodes.Success;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"Register service returned {(int)ex.StatusCode}: {ex.Content}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>Shared helpers for the governance commands.</summary>
internal static class GovernanceCliHelpers
{
    private const string WalletDidPrefix = "did:sorcha:w:";

    /// <summary>Accepts either a bare wallet address or a full wallet DID.</summary>
    internal static string ToWalletDid(string walletAddressOrDid) =>
        walletAddressOrDid.StartsWith(WalletDidPrefix, StringComparison.Ordinal)
            ? walletAddressOrDid
            : WalletDidPrefix + walletAddressOrDid;

    internal static async Task<(string? Token, string ProfileName, int Error)> AuthenticateAsync(
        IAuthenticationService authService, IConfigurationService configService)
    {
        var profile = await configService.GetActiveProfileAsync();
        var profileName = profile?.Name ?? "dev";

        var token = await authService.GetAccessTokenAsync(profileName);
        if (string.IsNullOrEmpty(token))
        {
            ConsoleHelper.WriteError("You must be authenticated to use governance commands.");
            ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
            return (null, profileName, ExitCodes.AuthenticationError);
        }

        return (token, profileName, 0);
    }

    internal static async Task<GovernanceSigningRequest?> FetchSigningRequestAsync(
        IRegisterServiceClient registerClient,
        string registerId,
        string proposalId,
        string approverDid,
        string token)
    {
        try
        {
            return await registerClient.GetGovernanceSigningRequestAsync(
                registerId, proposalId, approverDid, $"Bearer {token}");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ConsoleHelper.WriteError($"No proposal '{proposalId}' on register '{registerId}'.");
            return null;
        }
        catch (ApiException ex)
        {
            ConsoleHelper.WriteError($"Could not read the proposal ({(int)ex.StatusCode}): {ex.Content}");
            return null;
        }
    }

    /// <summary>
    /// Renders the operation an approver is being asked to authorise.
    /// </summary>
    /// <remarks>
    /// Deliberately field by field rather than as a JSON blob: approving what you cannot read is
    /// FR-027 defeated in the human rather than in the protocol. The raw operation is printed
    /// underneath so nothing is hidden by the summary.
    /// </remarks>
    internal static void RenderOperation(GovernanceSigningRequest request, string approverDid)
    {
        var op = request.Operation;

        ConsoleHelper.WriteInfo("");
        ConsoleHelper.WriteInfo("You are being asked to authorise:");
        ConsoleHelper.WriteInfo($"  Register    : {request.RegisterId}");
        ConsoleHelper.WriteInfo($"  Proposal    : {request.RequestId}");
        ConsoleHelper.WriteInfo($"  Operation   : {op.OperationType}");
        ConsoleHelper.WriteInfo($"  Subject     : {op.TargetDid}");
        ConsoleHelper.WriteInfo($"  Role        : {op.TargetRole}");
        ConsoleHelper.WriteInfo($"  Proposed by : {op.ProposerDid}");
        ConsoleHelper.WriteInfo($"  Proposed at : {op.ProposedAt:u}");
        ConsoleHelper.WriteInfo($"  Expires     : {op.ExpiresAt:u}");
        ConsoleHelper.WriteInfo($"  Rule        : {op.QuorumFormulaAtRaise}");
        ConsoleHelper.WriteInfo($"  Roster      : {op.RosterSnapshotId}");

        if (!string.IsNullOrWhiteSpace(op.Justification))
        {
            ConsoleHelper.WriteInfo($"  Because     : {op.Justification}");
        }

        if (op.ValidatorEntry is not null)
        {
            // The field an approval used to leave unbound: "add a validator" without saying WHICH.
            ConsoleHelper.WriteInfo($"  Validator   : {op.ValidatorEntry.ValidatorId}");
            ConsoleHelper.WriteInfo($"  Its key     : {op.ValidatorEntry.PublicKey}");
        }

        ConsoleHelper.WriteInfo($"  Signing as  : {approverDid}");
        ConsoleHelper.WriteInfo($"  Statement   : {request.StatementVersion}");

        ConsoleHelper.WriteInfo("");
        ConsoleHelper.WriteInfo("Full operation, as stored on the ledger:");
        ConsoleHelper.WriteInfo(JsonSerializer.Serialize(op, new JsonSerializerOptions { WriteIndented = true }));
    }
}

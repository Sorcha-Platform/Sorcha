// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using Refit;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Sorcha.Cli.Infrastructure;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Manage the ORGANISATION's own signing wallet (#1525).
/// </summary>
public class OrgWalletCommand : Command
{
    /// <summary>Creates the <c>org wallet</c> command group.</summary>
    public OrgWalletCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("wallet", "Manage the organisation's signing wallet\n\nExamples:\n  sorcha org wallet create <org-id>")
    {
        Subcommands.Add(new OrgWalletCreateCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Creates the organisation's canonical signing wallet and records it against the organisation.
/// </summary>
/// <remarks>
/// <para>
/// Create-then-link in one command. The wallet is created against the Wallet Service with the
/// organisation as owner — which returns the BIP39 recovery phrase ONCE, to whoever runs this — and
/// is then recorded on the organisation. The phrase never passes through the Tenant Service.
/// </para>
/// <para>
/// Run it as an administrator OF THAT ORGANISATION. A platform admin is refused: the phrase is shown
/// once and never stored, so whoever runs this is the only person who will ever hold it, and it is
/// not the platform's secret to hold.
/// </para>
/// </remarks>
public class OrgWalletCreateCommand : Command
{
    private readonly Argument<string> _orgIdArgument;
    private readonly Option<string> _algorithmOption;
    private readonly Option<string?> _nameOption;

    /// <summary>Creates the <c>org wallet create</c> command.</summary>
    public OrgWalletCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create the organisation's signing wallet (returns the recovery phrase once)")
    {
        _orgIdArgument = new Argument<string>("orgId") { Description = "Organisation ID (must be YOUR organisation)" };
        _algorithmOption = new Option<string>("--algorithm", "-a")
        {
            Description = "Signing algorithm (default ED25519)",
            DefaultValueFactory = _ => "ED25519"
        };
        _nameOption = new Option<string?>("--name", "-n")
        {
            Description = "Wallet name (default org-{orgId}-signing)"
        };
        Arguments.Add(_orgIdArgument);
        Options.Add(_algorithmOption);
        Options.Add(_nameOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdArgument)!;
            var algorithm = parseResult.GetValue(_algorithmOption)!;
            var name = parseResult.GetValue(_nameOption) ?? $"org-{orgId}-signing";

            if (!Guid.TryParse(orgId, out var orgGuid))
            {
                ConsoleHelper.WriteError($"'{orgId}' is not a valid organisation ID.");
                return ExitCodes.ValidationError;
            }

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to create an organisation wallet.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // 1. Create it, owned by the organisation. The recovery phrase comes back here and
                //    nowhere else — it is never stored, on either side.
                var walletClient = await clientFactory.CreateWalletServiceClientAsync(profileName);
                var created = await walletClient.CreateWalletAsync(
                    new Sorcha.Wallet.Contracts.Models.CreateWalletRequest
                    {
                        Name = name,
                        Algorithm = algorithm,
                        OrganizationId = orgGuid
                    },
                    $"Bearer {token}");

                var address = created.Wallet?.Address;
                if (string.IsNullOrEmpty(address))
                {
                    ConsoleHelper.WriteError("Wallet creation returned no address.");
                    return ExitCodes.GeneralError;
                }

                // 2. Record it on the organisation. Only the address travels.
                var tenantClient = await clientFactory.CreateTenantServiceClientAsync(profileName);
                var org = await tenantClient.LinkOrganizationWalletAsync(
                    orgId, new LinkOrganizationWalletRequest { WalletAddress = address }, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, new
                    {
                        organizationId = orgId,
                        walletAddress = address,
                        algorithm = created.Wallet?.Algorithm,
                        mnemonicWords = created.MnemonicWords
                    });
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Organisation wallet created for '{org?.Name ?? orgId}'.");
                Console.WriteLine($"  Address:    {address}");
                Console.WriteLine($"  Algorithm:  {created.Wallet?.Algorithm}");
                Console.WriteLine();
                ConsoleHelper.WriteWarning("RECOVERY PHRASE — shown once, NOT stored anywhere. Back it up now:");
                Console.WriteLine();
                Console.WriteLine($"  {string.Join(" ", created.MnemonicWords)}");
                Console.WriteLine();
                ConsoleHelper.WriteWarning("This is the ORGANISATION's wallet: its issuer DID anchors on it and its");
                ConsoleHelper.WriteWarning("governance identity is matched against it. Lose this phrase and the");
                ConsoleHelper.WriteWarning("organisation cannot be recovered — nobody, including Sorcha, can reissue it.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError($"Organisation '{orgId}' already has a wallet.");
                ConsoleHelper.WriteInfo("Replacing it would orphan every credential issued under the old one, so it is refused.");
                return ExitCodes.GeneralError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("Refused: an organisation's wallet must be created by an administrator of THAT organisation.");
                ConsoleHelper.WriteInfo("Its recovery phrase is shown once and never stored, so it is not the platform's to hold.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null) ConsoleHelper.WriteError($"Details: {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to create the organisation wallet: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

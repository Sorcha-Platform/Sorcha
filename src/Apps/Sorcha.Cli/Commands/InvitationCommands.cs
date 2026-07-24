// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;

using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;
using Sorcha.ServiceClients.Invitation;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Register invitation management commands.
/// </summary>
/// <remarks>
/// These commands talk to the Tenant Service through the <b>shared</b>
/// <see cref="IRegisterInvitationServiceClient"/> from Sorcha.ServiceClients.Http — the same client
/// the Blazor admin UI uses. The CLI previously carried its own Refit interface and its own copies
/// of the four invitation DTOs, which had drifted from the server contract badly enough that every
/// subcommand failed against a live server. There is now one definition of this wire contract.
/// </remarks>
public class InvitationCommand : Command
{
    public InvitationCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("invitation", "Manage register invitations\n\nExamples:\n  sorcha invitation create --register-id <id> --target-org-did <did>\n  sorcha invitation list --direction sent\n  sorcha invitation accept --token <token>\n  sorcha invitation revoke --id <id>")
    {
        Subcommands.Add(new InvitationCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new InvitationListCommand(clientFactory, authService, configService));
        Subcommands.Add(new InvitationAcceptCommand(clientFactory, authService, configService));
        Subcommands.Add(new InvitationRevokeCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Shared plumbing for the invitation subcommands: resolve the active profile, fetch the cached
/// access token, and derive the caller's organisation id from it.
/// </summary>
/// <remarks>
/// Each subcommand previously repeated this preamble and carried its own private copy of the
/// <c>org_id</c> extraction — four identical methods in one file, each with the claim name
/// hard-coded. The extraction now lives in <see cref="AccessTokenClaims"/> and reads the claim name
/// from the shared <c>TokenClaimConstants</c>.
/// </remarks>
internal static class InvitationCommandContext
{
    /// <summary>Resolved caller context, or the exit code explaining why it could not be resolved.</summary>
    internal readonly record struct Result(
        string ProfileName,
        string AccessToken,
        Guid OrgId,
        int? FailureExitCode)
    {
        internal bool Ok => FailureExitCode is null;
    }

    internal static async Task<Result> ResolveAsync(
        IAuthenticationService authService,
        IConfigurationService configService)
    {
        var profile = await configService.GetActiveProfileAsync();
        var profileName = profile?.Name ?? "dev";

        var token = await authService.GetAccessTokenAsync(profileName);
        if (string.IsNullOrEmpty(token))
        {
            ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
            return new Result(profileName, string.Empty, Guid.Empty, ExitCodes.AuthenticationError);
        }

        var orgIdClaim = AccessTokenClaims.TryGetOrgId(token);
        if (string.IsNullOrEmpty(orgIdClaim))
        {
            ConsoleHelper.WriteError("Could not determine organization ID from token.");
            return new Result(profileName, token, Guid.Empty, ExitCodes.AuthenticationError);
        }

        // The shared client is typed on Guid — a consumer-tier token carrying a non-Guid org id is
        // an authentication problem, not a malformed request, so it is reported as one.
        if (!Guid.TryParse(orgIdClaim, out var orgId))
        {
            ConsoleHelper.WriteError($"Organization ID in token is not a valid GUID: '{orgIdClaim}'.");
            return new Result(profileName, token, Guid.Empty, ExitCodes.AuthenticationError);
        }

        return new Result(profileName, token, orgId, null);
    }
}

/// <summary>
/// Creates a new register invitation.
/// </summary>
public class InvitationCreateCommand : Command
{
    public InvitationCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a register invitation")
    {
        var registerIdOption = new Option<string>("--register-id") { Description = "Register ID to invite to", Required = true };
        var targetOrgDidOption = new Option<string>("--target-org-did") { Description = "Target organization DID", Required = true };
        var expiresInOption = new Option<int?>("--expires-in-days") { Description = "Days until the invitation expires (1-90, default: 7)" };

        Options.Add(registerIdOption);
        Options.Add(targetOrgDidOption);
        Options.Add(expiresInOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var context = await InvitationCommandContext.ResolveAsync(authService, configService);
                if (!context.Ok)
                {
                    return context.FailureExitCode!.Value;
                }

                var client = await clientFactory.CreateRegisterInvitationClientAsync(
                    context.ProfileName, context.AccessToken);

                var request = new CreateRegisterInvitationRequest
                {
                    RegisterId = parseResult.GetValue(registerIdOption)!,
                    TargetOrgDid = parseResult.GetValue(targetOrgDidOption)!,
                    // The server contract is DAYS. The previous CLI-local DTO said hours, which the
                    // server never bound, so the value was silently ignored and every invitation
                    // took the 7-day default.
                    ExpiresInDays = parseResult.GetValue(expiresInOption) ?? 7,
                };

                var result = await client.CreateAsync(context.OrgId, request, ct);

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Invitation created successfully.");
                Console.WriteLine($"  ID:         {result.InvitationId}");
                Console.WriteLine($"  Register:   {result.RegisterId}");
                Console.WriteLine($"  Target DID: {result.TargetOrgDid}");
                Console.WriteLine($"  Created:    {result.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Expires:    {result.ExpiresAt:yyyy-MM-dd HH:mm:ss}");

                Console.WriteLine();
                ConsoleHelper.WriteInfo("Share this token with the target organization:");
                Console.WriteLine(result.InvitationToken);

                return ExitCodes.Success;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to create invitations.");
                return ExitCodes.AuthorizationError;
            }
            catch (InvitationApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to create invitation: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists register invitations.
/// </summary>
public class InvitationListCommand : Command
{
    public InvitationListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List register invitations")
    {
        var registerIdOption = new Option<string?>("--register-id") { Description = "Filter by register ID" };
        var directionOption = new Option<string?>("--direction") { Description = "Filter by direction (sent, received, all)" };

        Options.Add(registerIdOption);
        Options.Add(directionOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var context = await InvitationCommandContext.ResolveAsync(authService, configService);
                if (!context.Ok)
                {
                    return context.FailureExitCode!.Value;
                }

                var client = await clientFactory.CreateRegisterInvitationClientAsync(
                    context.ProfileName, context.AccessToken);

                var direction = parseResult.GetValue(directionOption) ?? "all";

                // The server returns a { invitations, total_count } envelope, not a bare array.
                var response = await client.ListAsync(context.OrgId, direction, ct);

                IReadOnlyList<InvitationSummary> invitations = response.Invitations;

                // Filter by register ID client-side if specified.
                var registerId = parseResult.GetValue(registerIdOption);
                if (!string.IsNullOrEmpty(registerId))
                {
                    invitations = invitations.Where(i => i.RegisterId == registerId).ToList();
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, invitations);
                    return ExitCodes.Success;
                }

                if (invitations.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No invitations found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {invitations.Count} invitation(s):");
                Console.WriteLine();

                Console.WriteLine($"{"ID",-34} {"Register",-34} {"Target DID",-35} {"Direction",-10} {"Status",-10} {"Expires"}");
                Console.WriteLine(new string('-', 140));

                foreach (var inv in invitations)
                {
                    Console.WriteLine($"{inv.InvitationId,-34} {inv.RegisterId,-34} {inv.TargetOrgDid,-35} {inv.Direction,-10} {inv.Status,-10} {inv.ExpiresAt:yyyy-MM-dd HH:mm}");
                }

                return ExitCodes.Success;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (InvitationApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list invitations: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Accepts a register invitation.
/// </summary>
public class InvitationAcceptCommand : Command
{
    public InvitationAcceptCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("accept", "Accept a register invitation")
    {
        var tokenOption = new Option<string>("--token") { Description = "Invitation token to accept", Required = true };
        Options.Add(tokenOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var context = await InvitationCommandContext.ResolveAsync(authService, configService);
                if (!context.Ok)
                {
                    return context.FailureExitCode!.Value;
                }

                var client = await clientFactory.CreateRegisterInvitationClientAsync(
                    context.ProfileName, context.AccessToken);

                var request = new AcceptInvitationRequest
                {
                    InvitationToken = parseResult.GetValue(tokenOption)!,
                };

                var result = await client.AcceptAsync(context.OrgId, request, ct);

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Invitation accepted successfully.");
                Console.WriteLine($"  Register ID:     {result.RegisterId}");
                if (!string.IsNullOrEmpty(result.RegisterName))
                {
                    Console.WriteLine($"  Register:        {result.RegisterName}");
                }

                Console.WriteLine($"  Subscription ID: {result.SubscriptionId}");
                Console.WriteLine($"  Status:          {result.SubscriptionStatus}");
                Console.WriteLine($"  From:            {result.SourceOrgName ?? result.SourceOrgDid}");
                Console.WriteLine($"  Accepted:        {result.AcceptedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid invitation token: {ex.Message}");
                return ExitCodes.ValidationError;
            }
            catch (InvitationApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to accept invitation: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Revokes a pending register invitation.
/// </summary>
public class InvitationRevokeCommand : Command
{
    public InvitationRevokeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("revoke", "Revoke a pending invitation")
    {
        var idOption = new Option<string>("--id") { Description = "Invitation ID to revoke", Required = true };
        Options.Add(idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var context = await InvitationCommandContext.ResolveAsync(authService, configService);
                if (!context.Ok)
                {
                    return context.FailureExitCode!.Value;
                }

                var invitationId = parseResult.GetValue(idOption)!;

                var client = await clientFactory.CreateRegisterInvitationClientAsync(
                    context.ProfileName, context.AccessToken);

                await client.RevokeAsync(context.OrgId, invitationId, ct);

                ConsoleHelper.WriteSuccess($"Invitation '{invitationId}' has been revoked.");
                return ExitCodes.Success;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (InvitationApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError("Invitation not found.");
                return ExitCodes.NotFound;
            }
            catch (InvitationApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to revoke invitation: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

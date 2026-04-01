// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Register invitation management commands.
/// </summary>
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
        var expiresInOption = new Option<int?>("--expires-in") { Description = "Expiration time in hours (default: server-side default)" };

        Options.Add(registerIdOption);
        Options.Add(targetOrgDidOption);
        Options.Add(expiresInOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var registerId = parseResult.GetValue(registerIdOption)!;
                var targetOrgDid = parseResult.GetValue(targetOrgDidOption)!;
                var expiresIn = parseResult.GetValue(expiresInOption);

                // Extract org ID from JWT token
                var orgId = ExtractOrgId(token);
                if (string.IsNullOrEmpty(orgId))
                {
                    ConsoleHelper.WriteError("Could not determine organization ID from token.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateInvitationServiceClientAsync(profileName);

                var request = new CreateInvitationRequest
                {
                    RegisterId = registerId,
                    TargetOrgDid = targetOrgDid,
                    ExpiresInHours = expiresIn
                };

                var result = await client.CreateInvitationAsync(orgId, request, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Invitation created successfully.");
                Console.WriteLine($"  ID:         {result.Id}");
                Console.WriteLine($"  Register:   {result.RegisterId}");
                Console.WriteLine($"  Target DID: {result.TargetOrgDid}");
                Console.WriteLine($"  Status:     {result.Status}");
                Console.WriteLine($"  Expires:    {result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}");

                if (!string.IsNullOrEmpty(result.Token))
                {
                    Console.WriteLine();
                    ConsoleHelper.WriteInfo("Share this token with the target organization:");
                    Console.WriteLine(result.Token);
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to create invitations.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
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

    private static string? ExtractOrgId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
        }
        catch
        {
            return null;
        }
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
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var direction = parseResult.GetValue(directionOption);

                var orgId = ExtractOrgId(token);
                if (string.IsNullOrEmpty(orgId))
                {
                    ConsoleHelper.WriteError("Could not determine organization ID from token.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateInvitationServiceClientAsync(profileName);
                var invitations = await client.ListInvitationsAsync(orgId, direction, $"Bearer {token}");

                // Filter by register ID client-side if specified
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

                Console.WriteLine($"{"ID",-34} {"Register",-34} {"Target DID",-35} {"Status",-10} {"Expires"}");
                Console.WriteLine(new string('-', 130));

                foreach (var inv in invitations)
                {
                    Console.WriteLine($"{inv.Id,-34} {inv.RegisterId,-34} {inv.TargetOrgDid,-35} {inv.Status,-10} {inv.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A"}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
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

    private static string? ExtractOrgId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
        }
        catch
        {
            return null;
        }
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
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var accessToken = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(accessToken))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var invitationToken = parseResult.GetValue(tokenOption)!;

                var orgId = ExtractOrgId(accessToken);
                if (string.IsNullOrEmpty(orgId))
                {
                    ConsoleHelper.WriteError("Could not determine organization ID from token.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateInvitationServiceClientAsync(profileName);

                var request = new AcceptInvitationRequest { Token = invitationToken };
                var result = await client.AcceptInvitationAsync(orgId, request, $"Bearer {accessToken}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Invitation accepted successfully.");
                Console.WriteLine($"  Register ID:     {result.RegisterId}");
                Console.WriteLine($"  Subscription ID: {result.SubscriptionId}");
                Console.WriteLine($"  Status:          {result.Status}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid invitation token: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
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

    private static string? ExtractOrgId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
        }
        catch
        {
            return null;
        }
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
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var invitationId = parseResult.GetValue(idOption)!;

                var orgId = ExtractOrgId(token);
                if (string.IsNullOrEmpty(orgId))
                {
                    ConsoleHelper.WriteError("Could not determine organization ID from token.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateInvitationServiceClientAsync(profileName);
                await client.RevokeInvitationAsync(orgId, invitationId, $"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Invitation '{invitationId}' has been revoked.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError("Invitation not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
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

    private static string? ExtractOrgId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
        }
        catch
        {
            return null;
        }
    }
}

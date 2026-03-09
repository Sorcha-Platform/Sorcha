// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// IDP configuration API endpoints.
/// Allows organization administrators to configure, discover, test, and toggle
/// external identity providers for OIDC-based single sign-on.
/// </summary>
public static class IdpConfigurationEndpoints
{
    /// <summary>
    /// Maps IDP configuration endpoints under /api/organizations/{orgId}/idp.
    /// </summary>
    public static IEndpointRouteBuilder MapIdpConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{orgId:guid}/idp")
            .WithTags("Identity Provider Configuration")
            .RequireAuthorization("RequireAdministrator");

        group.MapGet("/", GetConfiguration)
            .WithName("GetIdpConfiguration")
            .WithSummary("Get IDP configuration")
            .WithDescription("Returns the identity provider configuration for an organization, "
                + "including discovered endpoints and enabled status.")
            .Produces<IdpConfigurationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/", CreateOrUpdate)
            .WithName("CreateOrUpdateIdpConfiguration")
            .WithSummary("Create or update IDP configuration")
            .WithDescription("Creates a new IDP configuration or updates an existing one. "
                + "Automatically triggers OIDC discovery to populate endpoints. "
                + "Supports provider presets: MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc.")
            .Produces<IdpConfigurationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/", DeleteConfiguration)
            .WithName("DeleteIdpConfiguration")
            .WithSummary("Delete IDP configuration")
            .WithDescription("Removes the identity provider configuration for an organization. "
                + "Users will no longer be able to sign in via SSO.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/discover", Discover)
            .WithName("DiscoverIdpEndpoints")
            .WithSummary("Discover IDP endpoints")
            .WithDescription("Fetches the OIDC discovery document (.well-known/openid-configuration) "
                + "from the specified issuer URL and returns the discovered endpoints.")
            .Produces<DiscoveryResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/test", TestConnection)
            .WithName("TestIdpConnection")
            .WithSummary("Test IDP connection")
            .WithDescription("Tests the IDP connection by attempting a client_credentials grant "
                + "against the configured token endpoint. Verifies that the client ID and secret are valid.")
            .Produces<TestConnectionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/toggle", Toggle)
            .WithName("ToggleIdp")
            .WithSummary("Enable or disable IDP")
            .WithDescription("Enables or disables the identity provider for an organization. "
                + "When disabled, users cannot sign in via SSO but the configuration is preserved.")
            .Produces<IdpConfigurationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<IdpConfigurationResponse>, NotFound>> GetConfiguration(
        Guid orgId,
        IIdpConfigurationService idpService,
        CancellationToken cancellationToken)
    {
        var config = await idpService.GetConfigurationAsync(orgId, cancellationToken);
        return config is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(config);
    }

    private static async Task<Results<Ok<IdpConfigurationResponse>, ValidationProblem>> CreateOrUpdate(
        Guid orgId,
        IdpConfigurationRequest request,
        IIdpConfigurationService idpService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IssuerUrl))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["issuerUrl"] = ["Issuer URL is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientId"] = ["Client ID is required"]
            });
        }

        var result = await idpService.CreateOrUpdateAsync(orgId, request, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteConfiguration(
        Guid orgId,
        IIdpConfigurationService idpService,
        CancellationToken cancellationToken)
    {
        var deleted = await idpService.DeleteAsync(orgId, cancellationToken);
        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<DiscoveryResponse>, ValidationProblem>> Discover(
        Guid orgId,
        DiscoverIdpRequest request,
        IIdpConfigurationService idpService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IssuerUrl))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["issuerUrl"] = ["Issuer URL is required"]
            });
        }

        try
        {
            var result = await idpService.DiscoverAsync(orgId, request.IssuerUrl, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["issuerUrl"] = [$"Discovery failed: {ex.Message}"]
            });
        }
    }

    private static async Task<Results<Ok<TestConnectionResponse>, NotFound>> TestConnection(
        Guid orgId,
        IIdpConfigurationService idpService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await idpService.TestConnectionAsync(orgId, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException)
        {
            return TypedResults.NotFound();
        }
    }

    private static async Task<Results<Ok<IdpConfigurationResponse>, NotFound>> Toggle(
        Guid orgId,
        ToggleIdpRequest request,
        IIdpConfigurationService idpService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await idpService.ToggleAsync(orgId, request.Enabled, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException)
        {
            return TypedResults.NotFound();
        }
    }
}

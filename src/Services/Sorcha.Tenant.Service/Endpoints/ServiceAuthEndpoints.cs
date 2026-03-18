// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Service-to-Service authentication API endpoints.
/// </summary>
public static class ServiceAuthEndpoints
{
    /// <summary>
    /// Maps service authentication endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapServiceAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/service-auth")
            .WithTags("Service Authentication");

        // Unified OAuth2 token endpoint supporting multiple grant types
        // Note: We manually parse both form-urlencoded and JSON in the handler,
        // so we don't use .Accepts<T>() which would cause Content-Type validation issues
        group.MapPost("/token", GetOAuth2Token)
            .WithName("GetOAuth2Token")
            .WithSummary("OAuth2 token endpoint")
            .WithDescription("OAuth2 compliant token endpoint. Supports grant types: password, client_credentials, refresh_token. Accepts both application/x-www-form-urlencoded and application/json content types.")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Delegated authority token
        group.MapPost("/token/delegated", GetDelegatedToken)
            .WithName("GetDelegatedToken")
            .WithSummary("Get delegated authority token")
            .WithDescription("Gets a service token that acts on behalf of a user.")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Service principal management (admin only)
        var adminGroup = app.MapGroup("/api/service-principals")
            .WithTags("Service Principals")
            .RequireAuthorization("RequireAdministrator");

        adminGroup.MapPost("/", RegisterServicePrincipal)
            .WithName("RegisterServicePrincipal")
            .WithSummary("Register a new service principal")
            .WithDescription("Registers a new service for service-to-service authentication. Returns credentials only once.")
            .Produces<ServicePrincipalRegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        adminGroup.MapGet("/", ListServicePrincipals)
            .WithName("ListServicePrincipals")
            .WithSummary("List all service principals")
            .WithDescription("Lists all registered service principals.")
            .Produces<ServicePrincipalListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminGroup.MapGet("/{id:guid}", GetServicePrincipal)
            .WithName("GetServicePrincipal")
            .WithSummary("Get service principal details")
            .WithDescription("Gets details of a specific service principal.")
            .Produces<ServicePrincipalResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminGroup.MapGet("/by-client/{clientId}", GetServicePrincipalByClientId)
            .WithName("GetServicePrincipalByClientId")
            .WithSummary("Get service principal by client ID")
            .WithDescription("Gets a service principal by its client ID.")
            .Produces<ServicePrincipalResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminGroup.MapPut("/{id:guid}/scopes", UpdateServicePrincipalScopes)
            .WithName("UpdateServicePrincipalScopes")
            .WithSummary("Update service principal scopes")
            .WithDescription("Updates the allowed scopes for a service principal.")
            .Produces<ServicePrincipalResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminGroup.MapPost("/{id:guid}/suspend", SuspendServicePrincipal)
            .WithName("SuspendServicePrincipal")
            .WithSummary("Suspend service principal")
            .WithDescription("Temporarily suspends a service principal.")
            .Produces<SuccessResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminGroup.MapPost("/{id:guid}/reactivate", ReactivateServicePrincipal)
            .WithName("ReactivateServicePrincipal")
            .WithSummary("Reactivate service principal")
            .WithDescription("Reactivates a suspended service principal.")
            .Produces<SuccessResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminGroup.MapDelete("/{id:guid}", RevokeServicePrincipal)
            .WithName("RevokeServicePrincipal")
            .WithSummary("Revoke service principal")
            .WithDescription("Permanently revokes a service principal.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Secret rotation (requires current secret)
        group.MapPost("/rotate-secret", RotateSecret)
            .WithName("RotateServiceSecret")
            .WithSummary("Rotate service principal secret")
            .WithDescription("Rotates the client secret for a service principal. Requires current secret.")
            .AllowAnonymous()
            .Produces<RotateSecretResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> GetOAuth2Token(
        HttpContext context,
        IServiceAuthService serviceAuthService,
        ILoginService loginService,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Parse request data (support both form-urlencoded and JSON)
        string grantType, username, password, clientId, clientSecret, refreshToken, scope;

        var contentType = context.Request.ContentType?.ToLowerInvariant() ?? "";
        if (contentType.Contains("application/json"))
        {
            // Parse JSON body
            var request = await context.Request.ReadFromJsonAsync<OAuth2TokenRequest>(cancellationToken);
            if (request == null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Invalid JSON request body"]
                });
            }

            grantType = request.GrantType ?? "";
            username = request.Username ?? "";
            password = request.Password ?? "";
            clientId = request.ClientId ?? "";
            clientSecret = request.ClientSecret ?? "";
            refreshToken = request.RefreshToken ?? "";
            scope = request.Scope ?? "";
        }
        else
        {
            // Parse form-urlencoded data (OAuth2 standard)
            var form = await context.Request.ReadFormAsync(cancellationToken);
            grantType = form["grant_type"].ToString();
            username = form["username"].ToString();
            password = form["password"].ToString();
            clientId = form["client_id"].ToString();
            clientSecret = form["client_secret"].ToString();
            refreshToken = form["refresh_token"].ToString();
            scope = form["scope"].ToString();
        }

        if (string.IsNullOrWhiteSpace(grantType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["grant_type"] = ["Grant type is required"]
            });
        }

        return grantType switch
        {
            "password" => await HandlePasswordGrant(username, password, loginService, logger, cancellationToken),
            "client_credentials" => await HandleClientCredentialsGrant(clientId, clientSecret, scope, serviceAuthService, cancellationToken),
            "refresh_token" => await HandleRefreshTokenGrant(refreshToken, tokenService, cancellationToken),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["grant_type"] = [$"Unsupported grant type: {grantType}. Supported: password, client_credentials, refresh_token"]
            })
        };
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> HandlePasswordGrant(
        string username,
        string password,
        ILoginService loginService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["username"] = ["Username is required for password grant"]
            });
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password is required for password grant"]
            });
        }

        try
        {
            var result = await loginService.LoginAsync(username, password, cancellationToken);

            if (!result.Success || result.Tokens is null)
            {
                logger.LogWarning("OAuth2 password grant failed for {Email}", username);
                return TypedResults.Unauthorized();
            }

            return TypedResults.Ok(result.Tokens);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth2 password grant failed with exception - {Email}", username);
            return TypedResults.Unauthorized();
        }
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> HandleClientCredentialsGrant(
        string clientId,
        string clientSecret,
        string scope,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["client_id"] = ["Client ID is required for client_credentials grant"]
            });
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["client_secret"] = ["Client secret is required for client_credentials grant"]
            });
        }

        var response = await serviceAuthService.AuthenticateServiceAsync(
            clientId, clientSecret, string.IsNullOrWhiteSpace(scope) ? null : scope, cancellationToken);

        if (response == null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> HandleRefreshTokenGrant(
        string refreshToken,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["refresh_token"] = ["Refresh token is required for refresh_token grant"]
            });
        }

        var response = await tokenService.RefreshTokenAsync(refreshToken, cancellationToken);

        if (response == null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> GetDelegatedToken(
        DelegatedTokenRequest request,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientId"] = ["Client ID is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientSecret"] = ["Client secret is required"]
            });
        }

        if (request.DelegatedUserId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["delegatedUserId"] = ["Delegated user ID is required"]
            });
        }

        var response = await serviceAuthService.AuthenticateWithDelegationAsync(
            request.ClientId,
            request.ClientSecret,
            request.DelegatedUserId,
            request.DelegatedOrganizationId,
            request.Scope,
            cancellationToken);

        if (response == null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Created<ServicePrincipalRegistrationResponse>, ValidationProblem, Conflict<string>>> RegisterServicePrincipal(
        RegisterServicePrincipalRequest request,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["serviceName"] = ["Service name is required"]
            });
        }

        try
        {
            var response = await serviceAuthService.RegisterServicePrincipalAsync(
                request.ServiceName, request.Scopes, cancellationToken);

            return TypedResults.Created($"/api/service-principals/{response.Id}", response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return TypedResults.Conflict(ex.Message);
        }
    }

    private static async Task<Ok<ServicePrincipalListResponse>> ListServicePrincipals(
        IServiceAuthService serviceAuthService,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var response = await serviceAuthService.ListServicePrincipalsAsync(includeInactive, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<ServicePrincipalResponse>, NotFound>> GetServicePrincipal(
        Guid id,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        var response = await serviceAuthService.GetServicePrincipalAsync(id, cancellationToken);
        return response != null
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ServicePrincipalResponse>, NotFound>> GetServicePrincipalByClientId(
        string clientId,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        var response = await serviceAuthService.GetServicePrincipalByClientIdAsync(clientId, cancellationToken);
        return response != null
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ServicePrincipalResponse>, NotFound, ValidationProblem>> UpdateServicePrincipalScopes(
        Guid id,
        string[] scopes,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        if (scopes == null || scopes.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["scopes"] = ["At least one scope is required"]
            });
        }

        var response = await serviceAuthService.UpdateServicePrincipalScopesAsync(id, scopes, cancellationToken);
        return response != null
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<SuccessResponse>, NotFound>> SuspendServicePrincipal(
        Guid id,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        var success = await serviceAuthService.SuspendServicePrincipalAsync(id, cancellationToken);
        return success
            ? TypedResults.Ok(new SuccessResponse { Message = "Service principal suspended" })
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<SuccessResponse>, NotFound>> ReactivateServicePrincipal(
        Guid id,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        var success = await serviceAuthService.ReactivateServicePrincipalAsync(id, cancellationToken);
        return success
            ? TypedResults.Ok(new SuccessResponse { Message = "Service principal reactivated" })
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> RevokeServicePrincipal(
        Guid id,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        var success = await serviceAuthService.RevokeServicePrincipalAsync(id, cancellationToken);
        return success
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<RotateSecretResponse>, UnauthorizedHttpResult, ValidationProblem>> RotateSecret(
        RotateSecretRequest request,
        string clientId,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientId"] = ["Client ID is required (query parameter)"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.CurrentSecret))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["currentSecret"] = ["Current secret is required"]
            });
        }

        var response = await serviceAuthService.RotateSecretAsync(clientId, request.CurrentSecret, cancellationToken);

        if (response == null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(response);
    }
}

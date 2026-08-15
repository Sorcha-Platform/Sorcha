// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Models.Auth;

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

        // Public OAuth2 token endpoint — serves ONLY the browser/human grant types (password,
        // refresh_token). This route is proxied by the public API Gateway (/api/service-auth/{**catch-all}),
        // and client_credentials service-token minting was the #1397 vector: it accepted the
        // repo-committed dev service secrets from anyone on the internet and minted a signing-capable
        // service token. client_credentials now lives ONLY on the internal-only group below.
        // Note: We manually parse both form-urlencoded and JSON in the handler,
        // so we don't use .Accepts<T>() which would cause Content-Type validation issues
        group.MapPost("/token", GetOAuth2Token)
            .WithName("GetOAuth2Token")
            .WithSummary("OAuth2 token endpoint (password + refresh_token only)")
            .WithDescription("OAuth2 compliant token endpoint. Supports grant types: password, refresh_token. " +
                "client_credentials service-token minting has moved to POST /api/internal/service-auth/token " +
                "(not reachable through the public API Gateway) — see #1397. Accepts both " +
                "application/x-www-form-urlencoded and application/json content types.")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Service principal management (admin only)
        var adminGroup = app.MapGroup("/api/service-principals")
            .WithTags("Service Principals")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

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

        // Internal-only service-to-service auth surface (#1397). The API Gateway has NO route for
        // /api/internal/* — an external request falls through to the UI's `/{**catch-all}` route and
        // 404s, the same mechanism the rest of the /api/internal/* surface already relies on. These
        // endpoints still authenticate the CALLER by client_id/client_secret rather than a bearer token
        // (there is no bearer yet, so .AllowAnonymous() here is correct) — being unreachable from
        // outside the Docker network is what makes them "internal-only", not an auth policy.
        var internalGroup = app.MapGroup("/api/internal/service-auth")
            .WithTags("Service Authentication (internal)");

        internalGroup.MapPost("/token", GetInternalServiceToken)
            .WithName("GetInternalServiceToken")
            .WithSummary("Internal client_credentials token endpoint")
            .WithDescription("OAuth2 client_credentials grant only. NOT routed by the public API Gateway — " +
                "callers must reach the Tenant Service directly inside the internal network. Moved off the " +
                "public /api/service-auth/token route as part of the #1397 remediation. client_secret is " +
                "OPTIONAL when the request arrives over the workload mTLS listener with a valid workload " +
                "certificate — the chain-validated certificate's SPIFFE identity is the credential (F191/#1420).")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Delegated authority token — service acting on behalf of a user. Internal-only for the same
        // reason as client_credentials: it authenticates by client_id/client_secret, which is exactly
        // the credential class #1397 showed can leak, and there is no legitimate external caller (see
        // ServiceAuthClient / DelegationTokenClient below — both already target the Tenant Service
        // directly, never the gateway).
        internalGroup.MapPost("/token/delegated", GetDelegatedToken)
            .WithName("GetDelegatedToken")
            .WithSummary("Get delegated authority token (internal)")
            .WithDescription("Gets a service token that acts on behalf of a user. Internal-only — not routed by the " +
                "API Gateway. clientSecret is OPTIONAL when the request arrives over the workload mTLS listener with " +
                "a valid workload certificate (F191/#1420).")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Secret rotation (requires current secret) — internal-only for the same reason.
        internalGroup.MapPost("/rotate-secret", RotateSecret)
            .WithName("RotateServiceSecret")
            .WithSummary("Rotate service principal secret (internal)")
            .WithDescription("Rotates the client secret for a service principal. Requires current secret. Internal-only — not routed by the API Gateway.")
            .AllowAnonymous()
            .Produces<RotateSecretResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> GetOAuth2Token(
        HttpContext context,
        ILoginService loginService,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Parse request data (support both form-urlencoded and JSON).
        // Only password + refresh_token are served here — client_id/client_secret are deliberately NOT
        // read on this public path; client_credentials is handled exclusively by the internal-only
        // GetInternalServiceToken handler below (#1397).
        string grantType, username, password, refreshToken, scope;

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
            refreshToken = form["refresh_token"].ToString();
            scope = form["scope"].ToString();
        }

        _ = scope; // password/refresh_token grants on this endpoint don't take a caller-supplied scope

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
            "refresh_token" => await HandleRefreshTokenGrant(refreshToken, tokenService, cancellationToken),
            "client_credentials" => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["grant_type"] = [
                    "client_credentials is not available on the public token endpoint. Use " +
                    "POST /api/internal/service-auth/token from inside the internal network. See #1397."]
            }),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["grant_type"] = [$"Unsupported grant type: {grantType}. Supported: password, refresh_token"]
            })
        };
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> GetInternalServiceToken(
        HttpContext context,
        IServiceAuthService serviceAuthService,
        CancellationToken cancellationToken)
    {
        // Internal-only client_credentials endpoint. Not routed by the API Gateway — see the route
        // comment on MapServiceAuthEndpoints. Parsing mirrors GetOAuth2Token (form + JSON).
        string grantType, clientId, clientSecret, scope;

        var contentType = context.Request.ContentType?.ToLowerInvariant() ?? "";
        if (contentType.Contains("application/json"))
        {
            var request = await context.Request.ReadFromJsonAsync<OAuth2TokenRequest>(cancellationToken);
            if (request == null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Invalid JSON request body"]
                });
            }

            grantType = request.GrantType ?? "";
            clientId = request.ClientId ?? "";
            clientSecret = request.ClientSecret ?? "";
            scope = request.Scope ?? "";
        }
        else
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            grantType = form["grant_type"].ToString();
            clientId = form["client_id"].ToString();
            clientSecret = form["client_secret"].ToString();
            scope = form["scope"].ToString();
        }

        if (!string.IsNullOrWhiteSpace(grantType) && grantType != "client_credentials")
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["grant_type"] = [$"Unsupported grant type on the internal token endpoint: {grantType}. Supported: client_credentials"]
            });
        }

        // F191: a secretless request whose connection carries a client certificate uses the
        // certificate as the credential. The certificate is only ever non-null on the workload
        // mTLS listener, where Kestrel has already chain-validated it against the workload trust
        // bundle at handshake (RequireCertificate) — the plaintext internal listener never
        // negotiates one. Presence of client_secret always selects the legacy secret path.
        if (string.IsNullOrWhiteSpace(clientSecret) && context.Connection.ClientCertificate is { } clientCertificate)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["client_id"] = ["Client ID is required for client_credentials grant"]
                });
            }

            var certResponse = await serviceAuthService.AuthenticateServiceWithCertificateAsync(
                clientId, clientCertificate, string.IsNullOrWhiteSpace(scope) ? null : scope, cancellationToken);

            return certResponse == null
                ? TypedResults.Unauthorized()
                : TypedResults.Ok(certResponse);
        }

        return await HandleClientCredentialsGrant(clientId, clientSecret, scope, serviceAuthService, cancellationToken);
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
            var result = await loginService.LoginAsync(username, password, ct: cancellationToken);

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
        HttpContext context,
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

        if (request.DelegatedUserId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["delegatedUserId"] = ["Delegated user ID is required"]
            });
        }

        // F191: certificate-credential parity with the client_credentials endpoint — a secretless
        // request on the workload mTLS listener authenticates by its chain-validated certificate.
        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            if (context.Connection.ClientCertificate is { } clientCertificate)
            {
                var certResponse = await serviceAuthService.AuthenticateWithDelegationByCertificateAsync(
                    request.ClientId,
                    clientCertificate,
                    request.DelegatedUserId,
                    request.DelegatedOrganizationId,
                    request.Scope,
                    cancellationToken);

                return certResponse == null
                    ? TypedResults.Unauthorized()
                    : TypedResults.Ok(certResponse);
            }

            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientSecret"] = ["Client secret is required (or present a workload certificate over the mTLS listener)"]
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

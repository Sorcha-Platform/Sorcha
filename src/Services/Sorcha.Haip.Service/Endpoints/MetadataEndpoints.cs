// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// OpenID4VCI issuer metadata and OAuth AS metadata endpoints per HAIP 1.0.
/// </summary>
public static class MetadataEndpoints
{
    /// <summary>
    /// Maps HAIP discovery endpoints.
    /// </summary>
    public static void MapMetadataEndpoints(this WebApplication app)
    {
        app.MapGet("/.well-known/openid-credential-issuer", GetIssuerMetadata)
            .WithName("GetIssuerMetadata")
            .WithTags("HAIP Discovery")
            .WithSummary("OpenID4VCI Issuer Metadata")
            .WithDescription(
                "Returns the issuer metadata document per HAIP 1.0 Section 5. " +
                "HAIP wallets use this to discover supported credential types, endpoints, and algorithms.")
            .Produces<IssuerMetadata>(StatusCodes.Status200OK)
            .AllowAnonymous();

        app.MapGet("/.well-known/oauth-authorization-server", GetOAuthMetadata)
            .WithName("GetOAuthAuthorizationServerMetadata")
            .WithTags("HAIP Discovery")
            .WithSummary("OAuth 2.0 Authorization Server Metadata")
            .WithDescription(
                "Returns the OAuth 2.0 AS metadata document declaring the pre-authorized code grant type " +
                "and the token endpoint URL.")
            .Produces<object>(StatusCodes.Status200OK)
            .AllowAnonymous();
    }

    private static IResult GetIssuerMetadata(IConfiguration configuration)
    {
        var issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";

        var metadata = new IssuerMetadata
        {
            CredentialIssuer = issuerUrl,
            CredentialEndpoint = $"{issuerUrl}/credential",
            TokenEndpoint = $"{issuerUrl}/token",
            NonceEndpoint = $"{issuerUrl}/nonce",
            CredentialsSupported =
            [
                new CredentialSupported
                {
                    Id = "SorchaCredential",
                    Format = "vc+sd-jwt",
                    CryptographicBindingMethodsSupported = ["jwk"],
                    CredentialSigningAlgValuesSupported = ["ES256"],
                    Display =
                    [
                        new CredentialDisplay { Name = "Sorcha Credential", Locale = "en" }
                    ]
                }
            ],
            Display =
            [
                new IssuerDisplay { Name = "Sorcha Platform", Locale = "en" }
            ]
        };

        return Results.Json(metadata);
    }

    private static IResult GetOAuthMetadata(IConfiguration configuration)
    {
        var issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";

        var metadata = new
        {
            issuer = issuerUrl,
            token_endpoint = $"{issuerUrl}/token",
            grant_types_supported = new[]
            {
                "urn:ietf:params:oauth:grant-type:pre-authorized_code"
            },
            token_endpoint_auth_methods_supported = new[] { "none" },
            response_types_supported = Array.Empty<string>()
        };

        return Results.Json(metadata);
    }
}

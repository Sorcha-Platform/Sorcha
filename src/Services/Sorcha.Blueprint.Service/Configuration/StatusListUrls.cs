// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;

namespace Sorcha.Blueprint.Service.Configuration;

/// <summary>
/// The single resolver for the public base URLs credentials embed in their status claims
/// (issue #1447). A status-list URL is signed into every issued credential, so a wrong
/// value is permanently broken — the signature covers it. Before this resolver existed,
/// three call sites each defaulted to the unroutable placeholder
/// <c>https://sorcha.example</c> and nothing set the config anywhere, so every
/// credential on every deployment carried a dead revocation URL and
/// <c>FailClosed</c> revocation checks could never pass.
/// </summary>
/// <remarks>
/// Deployment wiring: docker-compose sets <c>StatusList__BaseUrl</c> /
/// <c>StatusList__IetfBaseUrl</c> from the deployment's public origin (n1 overrides them
/// alongside <c>Haip__IssuerUrl</c> in docker-compose.n1.yml). The non-production default
/// is deployment-local HTTPS — the Wallet Service refuses to sign a non-HTTPS status URL,
/// so an http default would fail every issuance.
/// </remarks>
public static class StatusListUrls
{
    /// <summary>Resolved pair of status-list base URLs (no trailing slash).</summary>
    public sealed record Resolved(string BaseUrl, string IetfBaseUrl);

    private const string PlaceholderHost = "sorcha.example";
    private const string DefaultBaseUrl = "https://localhost/api/v1/credentials/status-lists";
    private const string DefaultIetfBaseUrl = "https://localhost/api/v1/credentials/ietf-status-lists";

    /// <summary>
    /// Resolves the W3C and IETF status-list base URLs. Fails closed with
    /// <see cref="InvalidOperationException"/> when the value is unset in
    /// Production/Staging, or contains the <c>sorcha.example</c> placeholder in ANY
    /// environment (a committed placeholder that "works" in dev is how #1447 hid).
    /// </summary>
    public static Resolved Resolve(IConfiguration configuration, string environmentName)
    {
        var failClosed = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase);

        var baseUrl = ResolveOne(
            configuration.GetValue<string>("StatusList:BaseUrl"),
            "StatusList:BaseUrl", DefaultBaseUrl, failClosed);
        var ietfBaseUrl = ResolveOne(
            configuration.GetValue<string>("StatusList:IetfBaseUrl"),
            "StatusList:IetfBaseUrl", DefaultIetfBaseUrl, failClosed);

        return new Resolved(baseUrl, ietfBaseUrl);
    }

    private static string ResolveOne(string? configured, string key, string fallback, bool failClosed)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (failClosed)
            {
                throw new InvalidOperationException(
                    $"{key} is required in Production/Staging. The status-list URL is signed into " +
                    "every issued credential and cannot be fixed after the fact — set it to this " +
                    "deployment's public origin (e.g. https://n1.sorcha.dev/api/v1/credentials/status-lists). " +
                    "Refusing to start rather than issue credentials with unreachable revocation URLs (issue #1447).");
            }

            return fallback;
        }

        var value = configured.TrimEnd('/');

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Host.Equals(PlaceholderHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{key} points at the reserved placeholder host '{PlaceholderHost}', which can never " +
                "serve a status list. Set it to this deployment's public origin — a credential signed " +
                "with this URL is permanently broken (issue #1447).");
        }

        return value;
    }
}

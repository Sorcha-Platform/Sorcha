// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ApiGateway.Discoverability;
using Xunit;

namespace Sorcha.ApiGateway.Tests.Discoverability;

/// <summary>
/// MCP audit 2026-07-26 — the manifest advertised ONLY stdio while the Streamable HTTP
/// <c>/mcp</c> endpoint was live behind the same gateway.
/// <para>
/// <c>McpManifestOptions.HttpSseUrl</c> existed but shipped empty when spec 139 US3 brought the
/// HTTP transport up, and <c>BuildTransports</c> only added the entry when the option was set — so
/// a client reading <c>/.well-known/mcp.json</c> could never discover the live endpoint, and the
/// stdio command it DID get (<c>dotnet run --project …</c>) requires having the Sorcha repo. The
/// integration test asserting FR-014 (<c>Manifest_TransportsIncludeStdioAndHttpSse</c>) is
/// environment-gated and never runs in CI, which is exactly how the gap shipped. This test is NOT
/// gated.
/// </para>
/// <para>
/// The fix mirrors what the auth block already does (#826 / spec 136): when the option is unset,
/// DERIVE the URL from the request origin — which the forwarded-headers middleware has already
/// normalised — instead of advertising nothing.
/// </para>
/// </summary>
public sealed class McpManifestTransportTests
{
    [Fact]
    public void UnsetOption_DerivesTheHttpUrlFromTheRequestOrigin()
    {
        McpManifestEndpoint.ResolveHttpTransportUrl(configured: "", origin: "https://n1.sorcha.dev")
            .Should().Be("https://n1.sorcha.dev/mcp");
    }

    [Fact]
    public void NullOption_AlsoDerives()
    {
        McpManifestEndpoint.ResolveHttpTransportUrl(configured: null, origin: "http://localhost:8880")
            .Should().Be("http://localhost:8880/mcp");
    }

    [Fact]
    public void AnExplicitOption_StillWins()
    {
        // Same override contract as AuthIssuer/AuthAudience: config set => config wins verbatim.
        McpManifestEndpoint.ResolveHttpTransportUrl(
                configured: "https://mcp.example.test/custom", origin: "https://n1.sorcha.dev")
            .Should().Be("https://mcp.example.test/custom");
    }

    [Fact]
    public void ATrailingSlashOnTheOriginDoesNotDoubleTheSeparator()
    {
        McpManifestEndpoint.ResolveHttpTransportUrl(configured: "", origin: "https://n1.sorcha.dev/")
            .Should().Be("https://n1.sorcha.dev/mcp");
    }
}

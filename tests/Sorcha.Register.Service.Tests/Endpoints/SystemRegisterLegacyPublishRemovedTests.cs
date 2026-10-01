// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Feature 197 T018: the legacy <c>POST /api/system-register/publish</c> route (any holder of
/// <c>CanManageRegisters</c> could publish an arbitrary system blueprint) is removed — a clean break.
/// </summary>
[Collection("RegisterWebApp")]
public class SystemRegisterLegacyPublishRemovedTests : IClassFixture<SystemRegisterDriftWebApplicationFactory>
{
    private readonly SystemRegisterDriftWebApplicationFactory _factory;

    public SystemRegisterLegacyPublishRemovedTests(SystemRegisterDriftWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task LegacyPublish_SystemAdminOnPlatformTier_IsNotMapped()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DriftTestAuthHandler.PrincipalHeader, "sysadmin-platform");

        var response = await client.PostAsJsonAsync("/api/system-register/publish", new
        {
            blueprintId = "register-creation-v1",
            blueprint = new { title = "x", participants = Array.Empty<object>(), actions = Array.Empty<object>() }
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }
}

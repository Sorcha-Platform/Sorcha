// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Unit tests for the GET /api/operations list endpoint.
/// Uses a lightweight TestServer with mocked IEncryptionOperationStore.
/// </summary>
public class OperationsEndpointTests : IDisposable
{
    private readonly Mock<IEncryptionOperationStore> _mockStore;
    private readonly string _walletAddress = "sorcha1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
    private readonly HttpClient _client;
    private readonly WebApplication _app;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OperationsEndpointTests()
    {
        _mockStore = new Mock<IEncryptionOperationStore>();

        var walletAddress = _walletAddress;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(_mockStore.Object);

        builder.Services.AddAuthentication("TestScheme")
            .AddScheme<OperationsTestAuthOptions, OperationsTestAuthHandler>("TestScheme", opts =>
            {
                opts.UserId = Guid.NewGuid();
                opts.WalletAddress = walletAddress;
            });

        builder.Services.AddAuthorization();

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapOperationsEndpoints();

        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ListOperations_ValidWallet_ReturnsPagedResults()
    {
        // Arrange
        var activeOp = MakeOperation("op-1", EncryptionOperationStatus.Encrypting);

        _mockStore
            .Setup(s => s.GetByWalletAddressAsync(_walletAddress))
            .ReturnsAsync(activeOp);

        // Act
        var response = await _client.GetAsync($"/api/operations?wallet={_walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("totalCount").GetInt32().Should().Be(1);
        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(20);
        root.GetProperty("hasMore").GetBoolean().Should().BeFalse();

        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1);

        items[0].GetProperty("operationId").GetString().Should().Be("op-1");
        items[0].GetProperty("status").GetString().Should().Be("encrypting");
    }

    [Fact]
    public async Task ListOperations_WalletMismatch_Returns403()
    {
        // Arrange - request a wallet that differs from the JWT wallet_address claim
        var otherWallet = "sorcha1other_wallet_address";

        // Act
        var response = await _client.GetAsync($"/api/operations?wallet={otherWallet}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListOperations_EmptyResults_ReturnsEmptyPage()
    {
        // Arrange
        _mockStore
            .Setup(s => s.GetByWalletAddressAsync(_walletAddress))
            .ReturnsAsync((EncryptionOperation?)null);

        // Act
        var response = await _client.GetAsync($"/api/operations?wallet={_walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("totalCount").GetInt32().Should().Be(0);
        root.GetProperty("items").EnumerateArray().ToList().Should().BeEmpty();
        root.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ListOperations_DefaultPagination_ReturnsPage1Size20()
    {
        // Arrange
        _mockStore
            .Setup(s => s.GetByWalletAddressAsync(_walletAddress))
            .ReturnsAsync((EncryptionOperation?)null);

        // Act - no page/pageSize params
        var response = await _client.GetAsync($"/api/operations?wallet={_walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task ListOperations_CustomPagination_RespectsParameters()
    {
        // Arrange - with one active operation
        var activeOp = MakeOperation("op-1", EncryptionOperationStatus.Encrypting);
        _mockStore
            .Setup(s => s.GetByWalletAddressAsync(_walletAddress))
            .ReturnsAsync(activeOp);

        // Act - request page 1 with pageSize 3
        var response = await _client.GetAsync($"/api/operations?wallet={_walletAddress}&page=1&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(3);
        root.GetProperty("totalCount").GetInt32().Should().Be(1);
        root.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        root.GetProperty("items").EnumerateArray().ToList().Should().HaveCount(1);
    }

    #region Helpers

    private EncryptionOperation MakeOperation(string operationId, EncryptionOperationStatus status) => new()
    {
        OperationId = operationId,
        Status = status,
        BlueprintId = "bp-1",
        ActionId = "action-1",
        InstanceId = "instance-1",
        SubmittingWalletAddress = _walletAddress,
        TotalRecipients = 3,
        TotalGroups = 1,
        CurrentStep = 2,
        TotalSteps = 5,
        StepName = "Encrypting payloads",
        PercentComplete = 40
    };

    #endregion
}

#region Test Authentication Infrastructure

/// <summary>
/// Auth options for operations endpoint tests.
/// </summary>
public class OperationsTestAuthOptions : AuthenticationSchemeOptions
{
    public Guid UserId { get; set; }
    public string? WalletAddress { get; set; }
    public Claim[]? CustomClaims { get; set; }
}

/// <summary>
/// Test auth handler that provides userId and wallet_address claims.
/// </summary>
public class OperationsTestAuthHandler : AuthenticationHandler<OperationsTestAuthOptions>
{
    public OperationsTestAuthHandler(
        IOptionsMonitor<OperationsTestAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims;

        if (Options.CustomClaims is not null)
        {
            claims = Options.CustomClaims;
            if (claims.Length == 0)
            {
                claims = [new Claim(ClaimTypes.Name, "anonymous")];
            }
        }
        else
        {
            var claimsList = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Options.UserId.ToString()),
                new(ClaimTypes.Name, "Test User"),
                new(ClaimTypes.Role, "User")
            };

            if (!string.IsNullOrEmpty(Options.WalletAddress))
                claimsList.Add(new Claim("wallet_address", Options.WalletAddress));

            claims = claimsList.ToArray();
        }

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

#endregion

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using FluentAssertions;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Tests that GET /api/operations/{id} returns per-recipient status in the response.
/// Reuses the TestServer + auth pattern from <see cref="OperationsEndpointTests"/>.
/// </summary>
public class OperationRecipientStatusTests : IDisposable
{
    private readonly Mock<IEncryptionOperationStore> _mockStore;
    private readonly string _walletAddress = "sorcha1qrecipient_status_test_wallet";
    private readonly HttpClient _client;
    private readonly WebApplication _app;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OperationRecipientStatusTests()
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
    public async Task GetOperation_WithThreeRecipients_ReturnsRecipientsArrayWithStatus()
    {
        // Arrange
        var operation = new EncryptionOperation
        {
            OperationId = "op-recip-1",
            Status = EncryptionOperationStatus.Encrypting,
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = _walletAddress,
            TotalRecipients = 3,
            Recipients =
            [
                new RecipientOperationStatus
                {
                    Name = "Alice",
                    DisclosedFields = ["/name", "/email"],
                    Status = "secured"
                },
                new RecipientOperationStatus
                {
                    Name = "Bob",
                    DisclosedFields = ["/name", "/amount"],
                    Status = "secured"
                },
                new RecipientOperationStatus
                {
                    Name = "Carol",
                    DisclosedFields = ["/ssn"],
                    Status = "encrypting"
                }
            ]
        };

        _mockStore.Setup(s => s.GetByIdAsync("op-recip-1")).ReturnsAsync(operation);

        // Act
        var response = await _client.GetAsync("/api/operations/op-recip-1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var recipients = root.GetProperty("recipients").EnumerateArray().ToList();
        recipients.Should().HaveCount(3);

        // Verify each recipient has Name, DisclosedFields, Status
        var alice = recipients.First(r => r.GetProperty("name").GetString() == "Alice");
        alice.GetProperty("status").GetString().Should().Be("secured");
        alice.GetProperty("disclosedFields").EnumerateArray()
            .Select(f => f.GetString()).Should().BeEquivalentTo(["/name", "/email"]);

        var bob = recipients.First(r => r.GetProperty("name").GetString() == "Bob");
        bob.GetProperty("status").GetString().Should().Be("secured");

        var carol = recipients.First(r => r.GetProperty("name").GetString() == "Carol");
        carol.GetProperty("status").GetString().Should().Be("encrypting");
    }

    [Fact]
    public async Task GetOperation_WithoutRecipients_ReturnsEmptyRecipientsArray()
    {
        // Arrange — operation created but encryption not yet started (no recipients populated)
        var operation = new EncryptionOperation
        {
            OperationId = "op-no-recip",
            Status = EncryptionOperationStatus.ResolvingKeys,
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = _walletAddress,
            TotalRecipients = 0,
            Recipients = [] // empty — pre-encryption
        };

        _mockStore.Setup(s => s.GetByIdAsync("op-no-recip")).ReturnsAsync(operation);

        // Act
        var response = await _client.GetAsync("/api/operations/op-no-recip");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var recipients = root.GetProperty("recipients").EnumerateArray().ToList();
        recipients.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOperation_NotFound_Returns404()
    {
        // Arrange
        _mockStore.Setup(s => s.GetByIdAsync("op-missing")).ReturnsAsync((EncryptionOperation?)null);

        // Act
        var response = await _client.GetAsync("/api/operations/op-missing");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

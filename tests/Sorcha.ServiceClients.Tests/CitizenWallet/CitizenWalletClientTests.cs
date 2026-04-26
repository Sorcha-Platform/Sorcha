// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.CitizenWallet;
using Xunit;

namespace Sorcha.ServiceClients.Tests.CitizenWallet;

/// <summary>
/// Wire-shape tests for <see cref="CitizenWalletClient"/> (Feature 114, T035-T036).
/// Confirms the client posts to the right path, sends the request DTO, parses the
/// response, and surfaces non-2xx as <see cref="HttpRequestException"/>.
/// </summary>
public sealed class CitizenWalletClientTests
{
    [Fact]
    public async Task EnrolDeviceAsync_PostsToExpectedPath_AndParsesResponse()
    {
        HttpRequestMessage? captured = null;
        var deviceId = Guid.NewGuid();
        var responseBody = $$"""
            {
              "deviceId": "{{deviceId}}",
              "delegationCredential": "h.p.s",
              "holderPublicJwk": { "kty": "EC", "crv": "P-256", "x": "X", "y": "Y" },
              "statusListUri": "https://verify.test/api/v1/wallet/status/abc/citizen-devices/0.statuslist+jwt",
              "statusListIndex": 7,
              "delegationExpiresAt": "2027-04-26T00:00:00Z"
            }
            """;
        var client = Build(HttpStatusCode.OK, responseBody, req => captured = req);

        var result = await client.EnrolDeviceAsync(new DeviceEnrolmentRequest
        {
            DeviceLabel = "iPhone",
            DevicePublicJwk = new EcP256PublicJwk { X = "x", Y = "y" },
            Platform = "iOS",
            UserAgent = "UA"
        });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/v1/wallet/devices/enrol");

        var sent = await captured.Content!.ReadAsStringAsync();
        sent.Should().Contain("\"deviceLabel\":\"iPhone\"");
        sent.Should().Contain("\"platform\":\"iOS\"");

        result.DeviceId.Should().Be(deviceId);
        result.DelegationCredential.Should().Be("h.p.s");
        result.StatusListIndex.Should().Be(7);
    }

    [Fact]
    public async Task EnrolDeviceAsync_NonSuccessStatus_Throws()
    {
        var client = Build(HttpStatusCode.Conflict, "{\"detail\":\"already enrolled\"}");

        var act = () => client.EnrolDeviceAsync(new DeviceEnrolmentRequest
        {
            DeviceLabel = "x",
            DevicePublicJwk = new EcP256PublicJwk { X = "x", Y = "y" },
            Platform = "x",
            UserAgent = "x"
        });

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private static CitizenWalletClient Build(
        HttpStatusCode status,
        string body,
        Action<HttpRequestMessage>? onRequest = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                if (req.Content is not null)
                {
                    await req.Content.LoadIntoBufferAsync();
                }
                onRequest?.Invoke(req);
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            });

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://wallet.test/") };
        var config = new ConfigurationBuilder().Build();
        return new CitizenWalletClient(http, config, Mock.Of<ILogger<CitizenWalletClient>>());
    }
}

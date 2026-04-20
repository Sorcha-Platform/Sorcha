// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

using Sorcha.ServiceClients.Invitation;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Invitation;

/// <summary>
/// Wire-shape + error-mapping tests for <see cref="RegisterInvitationServiceClient"/>. The client
/// must round-trip snake_case DTOs with the Tenant Service endpoints and surface 4xx
/// responses as <see cref="InvitationApiException"/> carrying the server's error message.
/// </summary>
public class RegisterRegisterInvitationServiceClientTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string InvitationId = "inv-abc-123";

    [Fact]
    public async Task CreateAsync_SendsSnakeCaseBody_AndParsesResponse()
    {
        HttpRequestMessage? captured = null;
        var responseBody = """
            {
              "invitation_id": "inv-abc-123",
              "invitation_token": "base64url-token",
              "register_id": "aebf26362e079087571ac0932d4db973",
              "target_org_did": "did:sorcha:org:ws1qtarget",
              "expires_at": "2026-04-30T00:00:00Z",
              "created_at": "2026-04-20T00:00:00Z"
            }
            """;
        var client = Build(HttpStatusCode.Created, responseBody, req => captured = req);

        var result = await client.CreateAsync(OrgId, new CreateInvitationRequest
        {
            RegisterId = "aebf26362e079087571ac0932d4db973",
            TargetOrgDid = "did:sorcha:org:ws1qtarget",
            ExpiresInDays = 14,
        });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath
            .Should().Be($"/api/organizations/{OrgId}/register-invitations");

        var sentBody = await captured.Content!.ReadAsStringAsync();
        // Snake-case on the wire is what the Tenant endpoints expect.
        sentBody.Should().Contain("\"register_id\":\"aebf26362e079087571ac0932d4db973\"");
        sentBody.Should().Contain("\"target_org_did\":\"did:sorcha:org:ws1qtarget\"");
        sentBody.Should().Contain("\"expires_in_days\":14");

        result.InvitationId.Should().Be("inv-abc-123");
        result.InvitationToken.Should().Be("base64url-token");
    }

    [Fact]
    public async Task AcceptAsync_Returns409Conflict_AsInvitationApiException()
    {
        const string body = """{ "error": "Organization is already subscribed to this register." }""";
        var client = Build(HttpStatusCode.Conflict, body);

        var act = async () => await client.AcceptAsync(OrgId, new AcceptInvitationRequest
        {
            InvitationToken = "token",
        });

        var ex = (await act.Should().ThrowAsync<InvitationApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Message.Should().Contain("already subscribed");
    }

    [Fact]
    public async Task CreateAsync_Returns429_AsInvitationApiException()
    {
        const string body = """{ "error": "Rate limit exceeded. Maximum 10 invitations per hour." }""";
        var client = Build(HttpStatusCode.TooManyRequests, body);

        var act = async () => await client.CreateAsync(OrgId, new CreateInvitationRequest
        {
            RegisterId = "r".PadRight(32, 'e'),
            TargetOrgDid = "did:sorcha:org:ws1qtarget",
        });

        var ex = (await act.Should().ThrowAsync<InvitationApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        ex.Message.Should().Contain("Rate limit");
    }

    [Fact]
    public async Task ListAsync_BuildsDirectionQuery_AndParsesArrayResponse()
    {
        HttpRequestMessage? captured = null;
        const string body = """
            {
              "invitations": [
                {
                  "invitation_id": "inv-1",
                  "register_id": "aebf26362e079087571ac0932d4db973",
                  "register_name": "Procurement",
                  "source_org_did": "did:sorcha:org:ws1qsource",
                  "source_org_name": "Cairngorm",
                  "target_org_did": "did:sorcha:org:ws1qtarget",
                  "target_org_name": "Highland",
                  "direction": "sent",
                  "status": "Pending",
                  "expires_at": "2026-04-30T00:00:00Z",
                  "created_at": "2026-04-20T00:00:00Z"
                }
              ],
              "total_count": 1
            }
            """;
        var client = Build(HttpStatusCode.OK, body, req => captured = req);

        var result = await client.ListAsync(OrgId, "sent");

        captured!.RequestUri!.Query.Should().Contain("direction=sent");
        result.TotalCount.Should().Be(1);
        result.Invitations.Should().HaveCount(1);
        result.Invitations[0].InvitationId.Should().Be("inv-1");
        result.Invitations[0].Direction.Should().Be("sent");
    }

    [Fact]
    public async Task RevokeAsync_Returns204_NoBodyExpected()
    {
        HttpRequestMessage? captured = null;
        var client = Build(HttpStatusCode.NoContent, "", req => captured = req);

        await client.RevokeAsync(OrgId, InvitationId);

        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsolutePath
            .Should().Be($"/api/organizations/{OrgId}/register-invitations/{InvitationId}");
    }

    [Fact]
    public async Task RevokeAsync_404_AsInvitationApiException()
    {
        const string body = """{ "error": "Invitation not found" }""";
        var client = Build(HttpStatusCode.NotFound, body);

        var act = async () => await client.RevokeAsync(OrgId, InvitationId);

        var ex = (await act.Should().ThrowAsync<InvitationApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- test helper ----

    private static RegisterInvitationServiceClient Build(
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
                // Buffer body so tests can inspect it after the call completes.
                if (req.Content is not null)
                {
                    await req.Content.LoadIntoBufferAsync();
                }
                onRequest?.Invoke(req);
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            });

        var http = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("http://tenant.test/"),
        };
        var config = new ConfigurationBuilder().Build();
        return new RegisterInvitationServiceClient(http, config, Mock.Of<ILogger<RegisterInvitationServiceClient>>());
    }
}

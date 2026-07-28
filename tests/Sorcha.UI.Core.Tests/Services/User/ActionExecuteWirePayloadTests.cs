// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Models.Workflows;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User;

/// <summary>
/// Asserts that every field on <see cref="ActionExecuteRequest"/> actually reaches the wire.
/// </summary>
/// <remarks>
/// <para>
/// <c>WorkflowService.SubmitActionExecuteAsync</c> hand-writes an anonymous body rather than
/// serialising the request record. A field added to the record but forgotten in that literal is
/// dropped silently — no compiler error, no test failure, and on the server a
/// <c>required</c>-less property simply arrives null.
/// </para>
/// <para>
/// That is exactly what happened to <c>CredentialPresentations</c>: the citizen selected a
/// credential from their own wallet in <c>CredentialGatePanel</c> and consented, and the selection
/// was discarded here — so the server saw no presentations, decided the gate was unsatisfied, and
/// showed a QR code to scan with a second device. Found by clicking through on n1, 2026-07-28;
/// no unit test could see it because none of them looked at the payload.
/// </para>
/// </remarks>
public class ActionExecuteWirePayloadTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"transactionId":"tx","instanceId":"i","isComplete":false}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private static ActionExecuteRequest FullyPopulatedRequest() => new()
    {
        BlueprintId = "bp-1",
        ActionId = "0",
        InstanceId = "inst-1",
        SenderWallet = "ws11qtest",
        RegisterAddress = "reg-1",
        PayloadData = new Dictionary<string, object> { ["answer"] = "yes" },
        CredentialPresentations =
        [
            new CredentialPresentation
            {
                CredentialId = "did:sorcha:w:ws11qtest",
                RawPresentation = "eyJ.header~disclosure~",
                DisclosedClaims = new Dictionary<string, object> { ["givenName"] = "Stuart" }
            }
        ]
    };

    private static async Task<string> CaptureBodyAsync(ActionExecuteRequest request)
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://n1.test/") };
        var sut = new WorkflowService(http, NullLogger<WorkflowService>.Instance);

        await sut.SubmitActionExecuteAsync(request);

        handler.Body.Should().NotBeNull("the service must send a request body");
        return handler.Body!;
    }

    [Fact]
    public async Task EveryRequestPropertyAppearsInTheSentBody()
    {
        var body = await CaptureBodyAsync(FullyPopulatedRequest());

        using var doc = JsonDocument.Parse(body);
        var sent = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        var expected = typeof(ActionExecuteRequest)
            .GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name));

        sent.Should().Contain(expected,
            "a property on ActionExecuteRequest that the hand-written body omits is dropped "
            + "silently — add it to the anonymous object in SubmitActionExecuteAsync");
    }

    [Fact]
    public async Task TheConsentedCredentialSelectionSurvivesToTheWire()
    {
        var body = await CaptureBodyAsync(FullyPopulatedRequest());

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("credentialPresentations", out var presentations)
            .Should().BeTrue();
        presentations.ValueKind.Should().Be(JsonValueKind.Array);
        presentations.GetArrayLength().Should().Be(1);

        // camelCase, not PascalCase: the anonymous fields are hand-cased but this is a real type,
        // and default options would serialise it as "CredentialId".
        presentations[0].TryGetProperty("credentialId", out _).Should().BeTrue(
            "the server binds camelCase; PascalCase here would be a silent wire mismatch");
    }

    [Fact]
    public async Task NoCredentialSelectionSendsNullNotAnEmptyArray()
    {
        // The server branches on "no presentations submitted" to start the cross-device
        // lifecycle. An empty array must not read as a satisfied gate.
        var body = await CaptureBodyAsync(FullyPopulatedRequest() with { CredentialPresentations = null });

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("credentialPresentations", out var presentations))
        {
            presentations.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }
}

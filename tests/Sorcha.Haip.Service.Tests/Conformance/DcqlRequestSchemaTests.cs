// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using FluentAssertions;

using Json.Schema;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.AtomicCache;
using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Services;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Conformance;

/// <summary>
/// Feature 181 / T020 (SC-001) — conformance check: the Request Object payload the HAIP
/// verifier serves validates against a JSON Schema of the OpenID4VP 1.0 authorization
/// request shape. The schema requires <c>dcql_query</c> (with <c>credentials[]</c> each
/// carrying id/format/meta) and forbids the retired <c>presentation_definition</c>.
/// </summary>
public class DcqlRequestSchemaTests
{
    /// <summary>
    /// Minimal OpenID4VP 1.0 authorization-request schema for the direct_post profile
    /// Sorcha emits. Deliberately strict on the DCQL cutover invariants (FR-002/FR-004):
    /// response_type is the constant "vp_token", dcql_query is required, and
    /// presentation_definition is structurally forbidden.
    /// </summary>
    private const string RequestObjectSchema = /*lang=json,strict*/ """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["response_type", "response_mode", "response_uri", "client_id", "nonce", "state", "dcql_query"],
      "properties": {
        "response_type": { "const": "vp_token" },
        "response_mode": { "type": "string", "enum": ["direct_post", "direct_post.jwt"] },
        "response_uri": { "type": "string", "format": "uri" },
        "client_id": { "type": "string", "minLength": 1 },
        "nonce": { "type": "string", "minLength": 1 },
        "state": { "type": "string", "minLength": 1 },
        "dcql_query": {
          "type": "object",
          "required": ["credentials"],
          "properties": {
            "credentials": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "required": ["id", "format", "meta"],
                "properties": {
                  "id": { "type": "string", "pattern": "^[a-zA-Z0-9_-]+$" },
                  "format": { "type": "string", "enum": ["dc+sd-jwt", "mso_mdoc"] },
                  "meta": { "type": "object" },
                  "claims": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "required": ["path"],
                      "properties": {
                        "path": { "type": "array", "minItems": 1, "items": { "type": "string" } }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      },
      "not": { "required": ["presentation_definition"] }
    }
    """;

    /// <summary>
    /// Generates a Request Object payload exactly the way the endpoint does — by invoking
    /// the GetRequestObject handler and decoding the signed JWT it serves.
    /// </summary>
    private static async Task<JsonElement> GenerateRequestObjectPayloadAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PresentationRequestTtlSeconds"] = "600",
                ["Haip:IssuerSigningAlgorithm"] = "ES256"
            })
            .Build();

        var store = new PresentationRequestStore(
            Mock.Of<ILogger<PresentationRequestStore>>(),
            config,
            new InMemoryAtomicDistributedCache());
        var signer = new RequestObjectSigner(config, NullLogger<RequestObjectSigner>.Instance);

        var request = await store.CreateAsync(
            clientId: "https://test.example/haip",
            credentialType: "LicenseCredential",
            requiredClaims: ["licenseNumber", "locality"],
            acceptedIssuers: null,
            baseUrl: "https://test.example/haip");

        var handler = typeof(VerifierEndpoints).GetMethod(
            "GetRequestObject", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (Task<IResult>)handler.Invoke(
            null, [request.Id, store, signer, CancellationToken.None])!;

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var jwt = await new StreamReader(context.Response.Body).ReadToEndAsync();

        var payloadJson = Base64Url.DecodeFromChars(jwt.Split('.')[1]);
        // CLAUDE.md Pattern #4: JsonSchema.Net Evaluate() requires JsonElement, not JsonNode.
        return JsonDocument.Parse(payloadJson).RootElement.Clone();
    }

    [Fact]
    public async Task GeneratedRequestObject_ValidatesAgainstOpenId4Vp10Schema()
    {
        var schema = JsonSchema.FromText(RequestObjectSchema);
        var payload = await GenerateRequestObjectPayloadAsync();

        var result = schema.Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.Should().BeTrue(
            "the served Request Object must conform to the OpenID4VP 1.0 request shape: {0}",
            string.Join("; ", (result.Details ?? [])
                .Where(d => d.Errors is not null)
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))));
    }

    [Fact]
    public async Task RequestObjectWithPresentationDefinition_FailsSchema()
    {
        // Negative control — proves the schema actually forbids the retired dialect
        // rather than passing anything (FR-002).
        var schema = JsonSchema.FromText(RequestObjectSchema);
        var payload = await GenerateRequestObjectPayloadAsync();

        var mutated = JsonNode.Parse(payload.GetRawText())!.AsObject();
        mutated["presentation_definition"] = new JsonObject { ["id"] = "legacy", ["input_descriptors"] = new JsonArray() };
        var element = JsonDocument.Parse(mutated.ToJsonString()).RootElement;

        var result = schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.Should().BeFalse("presentation_definition is structurally forbidden");
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Helper methods for setting OpenAPI request/response examples on endpoint operations.
/// Used with <c>.WithOpenApi(operation => { ... })</c> to provide example payloads
/// that appear in Scalar UI documentation.
/// </summary>
public static class OpenApiExamples
{
    /// <summary>
    /// Sets a JSON example on the request body of an OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to modify.</param>
    /// <param name="json">A JSON string representing the example request body.</param>
    public static void SetRequestExample(OpenApiOperation operation, string json)
    {
        if (operation.RequestBody?.Content is null)
            return;

        if (operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Example = JsonNode.Parse(json);
        }
    }

    /// <summary>
    /// Sets a JSON example on a specific response status code of an OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to modify.</param>
    /// <param name="statusCode">The HTTP status code (e.g., "200", "201").</param>
    /// <param name="json">A JSON string representing the example response body.</param>
    public static void SetResponseExample(OpenApiOperation operation, string statusCode, string json)
    {
        if (operation.Responses is null)
            return;

        if (operation.Responses.TryGetValue(statusCode, out var response)
            && response.Content is not null
            && response.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Example = JsonNode.Parse(json);
        }
    }
}

/// <summary>
/// An OpenAPI operation transformer that applies JSON examples to endpoints based on their operation ID.
/// Register example entries via <see cref="AddExample"/> before adding this transformer to OpenAPI options.
/// </summary>
public sealed class EndpointExampleTransformer : IOpenApiOperationTransformer
{
    private readonly Dictionary<string, ExampleEntry> _examples = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a request and/or response example for a specific endpoint identified by its operation ID.
    /// The operation ID corresponds to the value set by <c>.WithName("...")</c> on the endpoint.
    /// </summary>
    /// <param name="operationId">The endpoint operation ID (from <c>.WithName()</c>).</param>
    /// <param name="requestJson">JSON string for the request body example, or null if the endpoint has no request body.</param>
    /// <param name="responseStatusCode">The HTTP status code for the response example (e.g., "200", "201").</param>
    /// <param name="responseJson">JSON string for the response body example, or null to skip.</param>
    /// <returns>This transformer instance for method chaining.</returns>
    public EndpointExampleTransformer AddExample(
        string operationId,
        string? requestJson,
        string responseStatusCode,
        string? responseJson)
    {
        _examples[operationId] = new ExampleEntry(requestJson, responseStatusCode, responseJson);
        return this;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var operationId = operation.OperationId;
        if (operationId is null || !_examples.TryGetValue(operationId, out var entry))
            return Task.CompletedTask;

        if (entry.RequestJson is not null)
        {
            OpenApiExamples.SetRequestExample(operation, entry.RequestJson);
        }

        if (entry.ResponseJson is not null)
        {
            OpenApiExamples.SetResponseExample(operation, entry.ResponseStatusCode, entry.ResponseJson);
        }

        return Task.CompletedTask;
    }

    private sealed record ExampleEntry(string? RequestJson, string ResponseStatusCode, string? ResponseJson);
}

using System.Net;
using System.Text.Json;

namespace Sorcha.Cli.Tests.Utilities;

/// <summary>
/// Test HTTP message handler for mocking HTTP responses.
/// </summary>
/// <remarks>
/// Supports two modes: a single fixed response via <see cref="SetResponse"/> (returned for every
/// request — the original behaviour), or a queue of responses via <see cref="EnqueueResponse"/> for
/// multi-step flows (e.g. login → org-selection). When the queue is non-empty it takes priority and
/// each request dequeues the next response; once drained, it falls back to the fixed response.
/// <see cref="Requests"/> records every request sent, so tests can assert on the URL/method used at
/// each step.
/// </remarks>
public class TestHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private object? _responseContent;
    private readonly Queue<(HttpStatusCode StatusCode, object? Content)> _queuedResponses = new();

    /// <summary>Every request this handler has seen, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    public void SetResponse(HttpStatusCode statusCode, object? content = null)
    {
        _statusCode = statusCode;
        _responseContent = content;
    }

    /// <summary>Queues a response to be returned for the next request, in FIFO order.</summary>
    public void EnqueueResponse(HttpStatusCode statusCode, object? content = null)
    {
        _queuedResponses.Enqueue((statusCode, content));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var (statusCode, content) = _queuedResponses.Count > 0
            ? _queuedResponses.Dequeue()
            : (_statusCode, _responseContent);

        var response = new HttpResponseMessage(statusCode);

        if (content != null)
        {
            var json = JsonSerializer.Serialize(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }
}

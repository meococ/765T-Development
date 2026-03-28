using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Memory;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class OllamaEmbeddingTests
{
    // ──────────────────────────────────────────────
    // OllamaEmbeddingClient unit tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EmbedAsync_ParsesOllamaEmbedResponse()
    {
        var embedding = new[] { 0.1f, 0.2f, 0.3f, -0.4f };
        var responseJson = JsonSerializer.Serialize(new
        {
            model = "nomic-embed-text",
            embeddings = new[] { embedding }
        });

        var handler = new FakeHttpHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var client = new OllamaEmbeddingClient(httpClient, "nomic-embed-text");

        var result = await client.EmbedAsync("test BIM query", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(embedding.Length, result.Values.Length);
        for (var i = 0; i < embedding.Length; i++)
        {
            Assert.Equal(embedding[i], result.Values[i], 6);
        }
    }

    [Fact]
    public void OllamaClient_IsSemantic_ReturnsTrue()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
        var client = new OllamaEmbeddingClient(httpClient, "nomic-embed-text");

        Assert.True(client.IsSemantic);
        Assert.Equal("ollama", client.ProviderId);
    }

    [Fact]
    public async Task EmbedAsync_EmptyEmbeddings_ReturnsEmptyVector()
    {
        var responseJson = JsonSerializer.Serialize(new { model = "nomic-embed-text", embeddings = Array.Empty<float[]>() });
        var handler = new FakeHttpHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var client = new OllamaEmbeddingClient(httpClient, "nomic-embed-text");

        var result = await client.EmbedAsync("test", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task EmbedAsync_HttpError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpHandler("Internal Server Error", HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var client = new OllamaEmbeddingClient(httpClient, "nomic-embed-text");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.EmbedAsync("test", CancellationToken.None));
    }

    [Fact]
    public async Task EmbedAsync_DefaultModel_IsNomicEmbedText()
    {
        string? capturedBody = null;
        var embedding = new[] { 0.5f };
        var responseJson = JsonSerializer.Serialize(new { model = "nomic-embed-text", embeddings = new[] { embedding } });
        var handler = new FakeHttpHandler(responseJson, HttpStatusCode.OK, body => capturedBody = body);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var client = new OllamaEmbeddingClient(httpClient, "");

        await client.EmbedAsync("hello", CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("nomic-embed-text", capturedBody);
    }

    // ──────────────────────────────────────────────
    // EmbeddingProviderFactory unit tests
    // ──────────────────────────────────────────────

    [Fact]
    public void HashEmbeddingClient_IsSemantic_ReturnsFalse()
    {
        var hash = new HashEmbeddingClient(64);
        Assert.False(hash.IsSemantic);
        Assert.Equal("hash_lexical_fallback", hash.ProviderId);
    }

    [Fact]
    public void WorkerHostSettings_OllamaDefaults_Correct()
    {
        var settings = new WorkerHostSettings();

        Assert.Equal("http://127.0.0.1:11434", settings.OllamaUrl);
        Assert.Equal("nomic-embed-text", settings.OllamaEmbeddingModel);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;
        private readonly Action<string>? _captureRequestBody;

        public FakeHttpHandler(string responseBody, HttpStatusCode statusCode, Action<string>? captureRequestBody = null)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _captureRequestBody = captureRequestBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_captureRequestBody != null && request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                _captureRequestBody(body);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}

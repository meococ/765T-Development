using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Copilot.Core.Brain;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

/// <summary>
/// IDE-based integration tests for the LLM client flow.
/// These tests run outside Revit and verify:
/// 1. AnthropicLlmClient HTTP communication
/// 2. OpenAI-compatible response parsing
/// 3. Timeout behavior
/// 4. Error handling and fallback
///
/// Run with: dotnet test BIM765T.Revit.WorkerHost.Tests -c Release --filter "FullyQualifiedName~LlmIntegrationTests"
/// </summary>
public sealed class LlmIntegrationTests : IDisposable
{
    public void Dispose()
    {
        // No persistent state to clean up.
    }

    [Fact]
    public void AnthropicLlmClient_ThrowsOnEmptyApiKey()
    {
        Assert.Throws<ArgumentException>(() => new AnthropicLlmClient(new HttpClient(), ""));
    }

    [Fact]
    public void AnthropicLlmClient_ThrowsOnWhitespaceApiKey()
    {
        Assert.Throws<ArgumentException>(() => new AnthropicLlmClient(new HttpClient(), "   "));
    }

    [Fact]
    public void AnthropicLlmClient_IsConfigured_WhenValidApiKey()
    {
        var client = new AnthropicLlmClient(new HttpClient(), "test-key-123");
        Assert.True(client.IsConfigured);
    }

    [Fact]
    public void AnthropicLlmClient_DetectsOpenAiFormat_FromUrl()
    {
        var client = new AnthropicLlmClient(
            new HttpClient(),
            "test-key",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        Assert.True(client.IsOpenAiCompatible);
        Assert.Equal("claude-sonnet-4.6", client.Model);
    }

    [Fact]
    public void AnthropicLlmClient_DetectsAnthropicFormat_FromUrl()
    {
        var client = new AnthropicLlmClient(
            new HttpClient(),
            "test-key",
            apiUrl: "https://api.anthropic.com/v1/messages");

        Assert.False(client.IsOpenAiCompatible);
        Assert.Equal("claude-sonnet-4-20250514", client.Model);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_WhenUserMessageIsEmpty()
    {
        var client = new AnthropicLlmClient(new HttpClient(), "test-key");

        var result = await client.CompleteAsync("system", "", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_WhenUserMessageIsWhitespace()
    {
        var client = new AnthropicLlmClient(new HttpClient(), "test-key");

        var result = await client.CompleteAsync("system", "   ", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_OnHttpRequestException()
    {
        var handler = new FailingHttpMessageHandler(
            new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        var result = await client.CompleteAsync("system", "hello", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_OnTimeout()
    {
        // Handler delays 25s but client CancelAfter is 20s → times out first.
        var handler = new SlowHttpMessageHandler(TimeSpan.FromSeconds(25));
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions",
            maxTokens: 100);

        var result = await client.CompleteAsync("system", "hello", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_ParsesOpenAiResponse_Correctly()
    {
        var jsonResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""Xin chao anh, em la 765T Assistant.""
                }
            }]
        }";

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            model: "test-model",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        var result = await client.CompleteAsync("system prompt", "xin chao", CancellationToken.None);

        Assert.Equal("Xin chao anh, em la 765T Assistant.", result);
    }

    [Fact]
    public async Task CompleteAsync_ParsesAnthropicResponse_Correctly()
    {
        var jsonResponse = @"{
            ""content"": [{
                ""type"": ""text"",
                ""text"": ""Em da hieu yeu cau cua anh.""
            }]
        }";

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            model: "claude-sonnet-4-20250514",
            apiUrl: "https://api.anthropic.com/v1/messages");

        var result = await client.CompleteAsync("system prompt", "hello", CancellationToken.None);

        Assert.Equal("Em da hieu yeu cau cua anh.", result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_WhenResponseHasNoContent()
    {
        var jsonResponse = @"{
            ""choices"": [{}]
        }";

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        var result = await client.CompleteAsync("system", "hello", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_WhenResponseIsMalformedJson()
    {
        var handler = new MockHttpMessageHandler("not valid json {{{", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        var result = await client.CompleteAsync("system", "hello", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsEmpty_WhenHttpStatusIsUnauthorized()
    {
        var handler = new MockHttpMessageHandler(
            @"{""error"": {""message"": ""Invalid API key""}}",
            HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "bad-key",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        var result = await client.CompleteAsync("system", "hello", CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_UsesCustomModel_WhenSpecified()
    {
        string? capturedBody = null;
        var jsonResponse = @"{""choices"": [{""message"": {""content"": ""ok""}}]}";
        var handler = new CapturingHttpMessageHandler(jsonResponse, HttpStatusCode.OK, body => capturedBody = body);
        var httpClient = new HttpClient(handler);
        var client = new AnthropicLlmClient(
            httpClient,
            "test-key",
            model: "custom-model-42",
            apiUrl: "https://api.minimaxi.chat/v1/chat/completions");

        await client.CompleteAsync("system", "hello", CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("custom-model-42", capturedBody);
    }

    [Fact]
    public void OpenAiCompatibleLlmClient_ThrowsOnNullHttpClient()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenAiCompatibleLlmClient(null!, "key", "model"));
    }

    [Fact]
    public void OpenAiCompatibleLlmClient_ThrowsOnNullApiKey()
    {
        Assert.Throws<ArgumentException>(() => new OpenAiCompatibleLlmClient(new HttpClient(), null!, "model"));
    }

    [Fact]
    public void OpenAiCompatibleLlmClient_ThrowsOnEmptyApiKey()
    {
        Assert.Throws<ArgumentException>(() => new OpenAiCompatibleLlmClient(new HttpClient(), "", "model"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseBody, HttpStatusCode statusCode)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public FailingHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class SlowHttpMessageHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public SlowHttpMessageHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""choices"": [{""message"": {""content"": ""slow""}}]}")
            };
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;
        private readonly Action<string> _onBody;

        public CapturingHttpMessageHandler(string responseBody, HttpStatusCode statusCode, Action<string> onBody)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _onBody = onBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _onBody(body);
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            };
            return response;
        }
    }
}
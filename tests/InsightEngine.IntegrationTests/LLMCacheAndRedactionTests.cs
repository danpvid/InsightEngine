using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.Data.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class LLMCacheAndRedactionTests
{
    [Fact]
    public async Task CachedClient_ReusesCachedResponse_OnRepeatedCalls()
    {
        var llmSettings = new LLMSettings
        {
            Provider = LLMProvider.LocalHttp,
            EnableCaching = true,
            LocalHttp = new LocalHttpSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama3"
            }
        };

        var runtimeSettings = new InsightEngineSettings
        {
            CacheTtlSeconds = 120
        };

        var handler = new StubHttpMessageHandler("""
{
  "model": "llama3",
  "response": "A concise answer.",
  "prompt_eval_count": 20,
  "eval_count": 10
}
""");
        var httpClient = new HttpClient(handler);

        var localClient = new LocalHttpLLMClient(
            httpClient,
            new TestOptionsMonitor<LLMSettings>(llmSettings),
            NullLogger<LocalHttpLLMClient>.Instance);
        var router = new LLMClientRouter(
            new TestOptionsMonitor<LLMSettings>(llmSettings),
            localClient,
            new NullLLMClient(NullLogger<NullLLMClient>.Instance),
            new OpenAiLLMClient());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cachedClient = new CachedLLMClient(
            router,
            cache,
            new TestOptionsMonitor<LLMSettings>(llmSettings),
            new TestOptionsMonitor<InsightEngineSettings>(runtimeSettings));

        var request = new LLMRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            QueryHash = "abc123",
            FeatureKind = "ai-summary",
            SystemPrompt = "You are an assistant.",
            UserPrompt = "Summarize this chart.",
            ContextObjects = new Dictionary<string, object?>
            {
                ["metrics"] = new { avg = 10.2, max = 15.6 }
            }
        };

        var first = await cachedClient.GenerateTextAsync(request);
        var second = await cachedClient.GenerateTextAsync(request);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Data!.CacheHit.Should().BeFalse();
        second.Data!.CacheHit.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public void RedactionService_RemovesSensitiveColumnsAndValues()
    {
        var settings = new LLMSettings
        {
            Redaction = new RedactionSettings
            {
                Enabled = true,
                ColumnNamePatterns = ["email", "ssn"]
            }
        };

        var redactionService = new LLMRedactionService(new TestOptionsMonitor<LLMSettings>(settings));
        var context = new Dictionary<string, object?>
        {
            ["schema"] = new[]
            {
                new Dictionary<string, object?> { ["name"] = "sales", ["type"] = "number" },
                new Dictionary<string, object?> { ["name"] = "customer_email", ["type"] = "string" }
            },
            ["rows"] = new[]
            {
                new Dictionary<string, object?> { ["region"] = "North", ["customer_email"] = "north@example.com", ["sales"] = 100 },
                new Dictionary<string, object?> { ["region"] = "South", ["customer_ssn"] = "123-44-5678", ["sales"] = 200 }
            }
        };

        var redacted = redactionService.RedactContext(context);
        var serialized = System.Text.Json.JsonSerializer.Serialize(redacted).ToLowerInvariant();

        serialized.Should().Contain("sales");
        serialized.Should().NotContain("customer_email");
        serialized.Should().NotContain("customer_ssn");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _payload;
        public int CallCount { get; private set; }

        public StubHttpMessageHandler(string payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

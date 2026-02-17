using FluentAssertions;
using InsightEngine.Application.Models.DataSet;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Queries.DataSet;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using InsightEngine.Infra.Data.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class LLMFeatureServiceTests
{
    [Fact]
    public async Task ContextBuilder_AppliesSizeBudget_AndRedactsSensitiveColumns()
    {
        var llmSettings = new LLMSettings
        {
            MaxContextBytes = 1_400,
            Redaction = new RedactionSettings
            {
                Enabled = true,
                ColumnNamePatterns = ["email"]
            }
        };

        var optionsMonitor = new TestOptionsMonitor<LLMSettings>(llmSettings);
        var builder = new LLMContextBuilder(
            new FakeDataSetApplicationService(),
            new LLMRedactionService(optionsMonitor),
            optionsMonitor,
            NullLogger<LLMContextBuilder>.Instance);

        var result = await builder.BuildChartContextAsync(new LLMChartContextRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001"
        });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Truncated.Should().BeTrue();

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Data.ContextObjects).ToLowerInvariant();
        serialized.Should().NotContain("customer_email");
    }

    [Fact]
    public void CacheKeyHelper_ChangesKey_WhenQueryHashOrFeatureKindChanges()
    {
        var baseRequest = new LLMRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            QueryHash = "abc",
            FeatureKind = "ai-summary",
            SystemPrompt = "system",
            UserPrompt = "user"
        };

        var keyA = LLMCacheKeyHelper.Build(baseRequest, LLMProvider.LocalHttp, "llama3");

        var changedQueryHash = new LLMRequest
        {
            DatasetId = baseRequest.DatasetId,
            RecommendationId = baseRequest.RecommendationId,
            QueryHash = "xyz",
            FeatureKind = baseRequest.FeatureKind,
            SystemPrompt = baseRequest.SystemPrompt,
            UserPrompt = baseRequest.UserPrompt
        };
        var keyB = LLMCacheKeyHelper.Build(changedQueryHash, LLMProvider.LocalHttp, "llama3");

        var changedFeature = new LLMRequest
        {
            DatasetId = baseRequest.DatasetId,
            RecommendationId = baseRequest.RecommendationId,
            QueryHash = baseRequest.QueryHash,
            FeatureKind = "explain-chart",
            SystemPrompt = baseRequest.SystemPrompt,
            UserPrompt = baseRequest.UserPrompt
        };
        var keyC = LLMCacheKeyHelper.Build(changedFeature, LLMProvider.LocalHttp, "llama3");

        keyA.Should().NotBe(keyB);
        keyA.Should().NotBe(keyC);
    }

    [Fact]
    public async Task AiSummary_WithInvalidJson_FallsBackToHeuristic()
    {
        var heuristic = new InsightSummary
        {
            Headline = "Heuristic headline",
            BulletPoints = ["one", "two"],
            Confidence = 0.55
        };

        var contextBuilder = new StubContextBuilder(new LLMContextPayload
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            QueryHash = "hash",
            HeuristicSummary = heuristic
        });

        var llmClient = new StubLLMClient(Result.Success(new LLMResponse
        {
            Provider = LLMProvider.LocalHttp,
            ModelId = "llama3",
            Json = """{"headline":"missing bullets"}"""
        }));

        var service = new AIInsightService(
            contextBuilder,
            new StubEvidencePackService(),
            llmClient,
            new TestOptionsMonitor<LLMSettings>(new LLMSettings()),
            NullLogger<AIInsightService>.Instance);

        var result = await service.GenerateAiSummaryAsync(new LLMChartContextRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001"
        });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Meta.FallbackUsed.Should().BeTrue();
        result.Data.InsightSummary.Headline.Should().Be("Heuristic headline");
    }

    [Fact]
    public async Task AiSummary_WhenLlmFails_FallsBackToHeuristic()
    {
        var contextBuilder = new StubContextBuilder(new LLMContextPayload
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            QueryHash = "hash",
            HeuristicSummary = new InsightSummary
            {
                Headline = "Fallback headline",
                BulletPoints = ["rule based"],
                Confidence = 0.4
            }
        });

        var llmClient = new StubLLMClient(Result.Failure<LLMResponse>("LLM provider is disabled."));
        var service = new AIInsightService(
            contextBuilder,
            new StubEvidencePackService(),
            llmClient,
            new TestOptionsMonitor<LLMSettings>(new LLMSettings()),
            NullLogger<AIInsightService>.Instance);

        var result = await service.GenerateAiSummaryAsync(new LLMChartContextRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001"
        });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Meta.FallbackUsed.Should().BeTrue();
        result.Data.InsightSummary.Headline.Should().Be("Fallback headline");
    }

    private sealed class StubContextBuilder : ILLMContextBuilder
    {
        private readonly LLMContextPayload _payload;

        public StubContextBuilder(LLMContextPayload payload)
        {
            _payload = payload;
        }

        public Task<Result<LLMContextPayload>> BuildChartContextAsync(
            LLMChartContextRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(_payload));
        }

        public Task<Result<LLMContextPayload>> BuildAskContextAsync(
            LLMAskContextRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(_payload));
        }
    }

    private sealed class StubLLMClient : ILLMClient
    {
        private readonly Result<LLMResponse> _result;

        public StubLLMClient(Result<LLMResponse> result)
        {
            _result = result;
        }

        public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class StubEvidencePackService : IEvidencePackService
    {
        public Task<Result<EvidencePackResult>> BuildEvidencePackAsync(
            DeepInsightsRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new EvidencePackResult
            {
                EvidencePack = new EvidencePack
                {
                    DatasetId = request.DatasetId,
                    RecommendationId = request.RecommendationId,
                    QueryHash = "stub",
                    EvidenceVersion = "v1",
                    Facts =
                    [
                        new EvidenceFact { EvidenceId = "TEST_1", ShortClaim = "stub", Value = "1" }
                    ]
                },
                CacheHit = false
            }));
        }
    }

    private sealed class FakeDataSetApplicationService : IDataSetApplicationService
    {
        public Task<Result<UploadDataSetResponse>> UploadAsync(IFormFile file, long? maxFileSizeBytes = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<UploadDataSetResponse>("Not used"));
        }

        public Task<Result<DatasetProfile>> GetProfileAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            var columns = new List<ColumnProfile>();
            for (var i = 0; i < 60; i++)
            {
                columns.Add(new ColumnProfile
                {
                    Name = $"metric_{i}",
                    InferredType = InferredType.Number,
                    NullRate = 0,
                    DistinctCount = 100,
                    Min = 1,
                    Max = 999,
                    TopValues = Enumerable.Range(0, 10).Select(x => $"{x}").ToList()
                });
            }

            columns.Add(new ColumnProfile
            {
                Name = "customer_email",
                InferredType = InferredType.Category,
                NullRate = 0,
                DistinctCount = 100,
                TopValues = ["north@example.com", "south@example.com"]
            });

            var profile = new DatasetProfile
            {
                DatasetId = datasetId,
                RowCount = 50_000,
                SampleSize = 5_000,
                Columns = columns
            };

            return Task.FromResult(Result.Success(profile));
        }

        public Task<Result<List<ChartRecommendation>>> GetRecommendationsAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new List<ChartRecommendation>
            {
                new()
                {
                    Id = "rec_001",
                    Title = "Revenue trend",
                    Reason = "Synthetic recommendation",
                    Chart = new ChartMeta { Type = ChartType.Line },
                    Query = new ChartQuery
                    {
                        X = new FieldSpec { Column = "date", Role = AxisRole.Time, Bin = TimeBin.Day },
                        Y = new FieldSpec { Column = "metric_1", Role = AxisRole.Measure, Aggregation = Aggregation.Sum }
                    }
                }
            }));
        }

        public Task<Result<List<DataSetSummary>>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<List<DataSetSummary>>("Not used"));
        }

        public Task<Result<ChartExecutionResponse>> GetChartAsync(
            Guid datasetId,
            string recommendationId,
            string? aggregation = null,
            string? timeBin = null,
            string? yColumn = null,
            string? groupBy = null,
            List<ChartFilter>? filters = null,
            ChartViewKind view = ChartViewKind.Base,
            PercentileMode percentileMode = PercentileMode.None,
            PercentileKind? percentileKind = null,
            string? percentileTarget = null,
            string? xColumn = null,
            CancellationToken cancellationToken = default)
        {
            var points = Enumerable.Range(0, 300)
                .Select(index => new object?[] { index, index * 1.2 })
                .ToList();

            var option = new EChartsOption
            {
                Series = new List<Dictionary<string, object>>
                {
                    new()
                    {
                        ["name"] = "metric_1",
                        ["type"] = "line",
                        ["data"] = points
                    }
                }
            };

            var response = new ChartExecutionResponse
            {
                DatasetId = datasetId,
                RecommendationId = recommendationId,
                QueryHash = "query-hash",
                ExecutionResult = new ChartExecutionResult
                {
                    Option = option,
                    RowCount = points.Count,
                    DuckDbMs = 12
                },
                InsightSummary = new InsightSummary
                {
                    Headline = "Fallback",
                    BulletPoints = ["Rule based"],
                    Confidence = 0.5
                }
            };

            return Task.FromResult(Result.Success(response));
        }

        public Task<Result<ScenarioSimulationResponse>> SimulateAsync(Guid datasetId, ScenarioRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<ScenarioSimulationResponse>("Not used"));
        }

        public Task<Result<BuildDataSetIndexResponse>> BuildIndexAsync(Guid datasetId, BuildIndexRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<BuildDataSetIndexResponse>("Not used"));
        }

        public Task<Result<DatasetIndex>> GetIndexAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<DatasetIndex>("Not used"));
        }

        public Task<Result<DatasetIndexStatus>> GetIndexStatusAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<DatasetIndexStatus>("Not used"));
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

using FluentAssertions;
using InsightEngine.Application.Models.DataSet;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
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

public class DeepInsightsTests
{
    [Fact]
    public async Task EvidencePackService_ShouldGenerateForecastAndFacts()
    {
        var llmOptions = new TestOptionsMonitor<LLMSettings>(new LLMSettings
        {
            MaxContextBytes = 32_000,
            DeepInsights = new DeepInsightsSettings
            {
                ForecastDefaultHorizon = 14,
                ForecastMaxHorizon = 60
            }
        });

        var runtimeOptions = new TestOptionsMonitor<InsightEngineSettings>(new InsightEngineSettings
        {
            CacheTtlSeconds = 300
        });

        var service = new EvidencePackService(
            new FakeDataSetApplicationService(),
            new LLMRedactionService(llmOptions),
            llmOptions,
            runtimeOptions,
            NullLogger<EvidencePackService>.Instance);

        var result = await service.BuildEvidencePackAsync(new DeepInsightsRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            TimeBin = "Day"
        });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.EvidencePack.Facts.Should().NotBeEmpty();
        result.Data.EvidencePack.ForecastPack.Methods.Should().HaveCount(3);
        result.Data.EvidencePack.Facts.Should().Contain(f => f.EvidenceId == "FORECAST_HORIZON");
    }

    [Fact]
    public async Task DeepInsights_WhenEvidenceIdsAreInvalid_ShouldFallback()
    {
        var aiService = new AIInsightService(
            new StubContextBuilder(),
            new StubEvidencePackService(),
            new StubLLMClient(Result.Success(new LLMResponse
            {
                Provider = LLMProvider.LocalHttp,
                ModelId = "llama3",
                Json = """
{
  "headline": "Bad output",
  "executiveSummary": "Uses unknown evidence.",
  "keyFindings": [{ "title": "Finding", "narrative": "Narrative", "evidenceIds": ["UNKNOWN_ID"], "severity": "high" }],
  "drivers": [],
  "risksAndCaveats": [],
  "projections": { "horizon": "14", "methods": [], "conclusion": "N/A" },
  "recommendedActions": [],
  "nextQuestions": [],
  "citations": [{ "evidenceId": "UNKNOWN_ID", "shortClaim": "bad" }],
  "meta": { "provider": "local", "model": "llama3", "promptVersion": "v1", "evidenceVersion": "v1" }
}
"""
            })),
            new TestOptionsMonitor<LLMSettings>(new LLMSettings()),
            NullLogger<AIInsightService>.Instance);

        var result = await aiService.GenerateDeepInsightsAsync(new DeepInsightsRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001"
        });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Meta.FallbackUsed.Should().BeTrue();
        result.Data.Report.Citations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeepInsights_WithValidEvidenceIds_ShouldReturnValidatedReport()
    {
        var aiService = new AIInsightService(
            new StubContextBuilder(),
            new StubEvidencePackService(),
            new StubLLMClient(Result.Success(new LLMResponse
            {
                Provider = LLMProvider.LocalHttp,
                ModelId = "llama3",
                Json = """
{
  "headline": "Validated report",
  "executiveSummary": "Evidence grounded summary.",
  "keyFindings": [{ "title": "Finding", "narrative": "Narrative", "evidenceIds": ["DIST_MEAN_METRIC"], "severity": "medium" }],
  "drivers": [{ "driver": "Trend", "whyItMatters": "Impact", "evidenceIds": ["TS_TREND_SLOPE"] }],
  "risksAndCaveats": [{ "risk": "Variance", "mitigation": "Monitor", "evidenceIds": ["DIST_STDDEV_METRIC"] }],
  "projections": {
    "horizon": "14",
    "methods": [{ "method": "naive", "narrative": "Baseline", "confidence": "medium", "evidenceIds": ["FORECAST_NAIVE_RMSE"] }],
    "conclusion": "Baseline only"
  },
  "recommendedActions": [{ "action": "Review segments", "expectedImpact": "Better focus", "effort": "low", "evidenceIds": ["SEG_SHARE_1"] }],
  "nextQuestions": ["What changed?"],
  "citations": [{ "evidenceId": "DIST_MEAN_METRIC", "shortClaim": "Mean captured" }],
  "meta": { "provider": "local", "model": "llama3", "promptVersion": "v1", "evidenceVersion": "v1" }
}
"""
            })),
            new TestOptionsMonitor<LLMSettings>(new LLMSettings()),
            NullLogger<AIInsightService>.Instance);

        var result = await aiService.GenerateDeepInsightsAsync(new DeepInsightsRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001"
        });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Meta.FallbackUsed.Should().BeFalse();
        result.Data.Report.Headline.Should().Be("Validated report");
        result.Data.Explainability.EvidenceUsedCount.Should().BeGreaterThan(0);
    }

    private sealed class StubContextBuilder : ILLMContextBuilder
    {
        public Task<Result<LLMContextPayload>> BuildChartContextAsync(LLMChartContextRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new LLMContextPayload()));
        }

        public Task<Result<LLMContextPayload>> BuildAskContextAsync(LLMAskContextRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new LLMContextPayload()));
        }
    }

    private sealed class StubEvidencePackService : IEvidencePackService
    {
        public Task<Result<EvidencePackResult>> BuildEvidencePackAsync(DeepInsightsRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new EvidencePackResult
            {
                EvidencePack = new EvidencePack
                {
                    DatasetId = request.DatasetId,
                    RecommendationId = request.RecommendationId,
                    QueryHash = "hash",
                    EvidenceVersion = "v1",
                    Facts =
                    [
                        new EvidenceFact { EvidenceId = "DIST_MEAN_METRIC", ShortClaim = "Mean", Value = "10" },
                        new EvidenceFact { EvidenceId = "DIST_STDDEV_METRIC", ShortClaim = "StdDev", Value = "2" },
                        new EvidenceFact { EvidenceId = "TS_TREND_SLOPE", ShortClaim = "Slope", Value = "0.2" },
                        new EvidenceFact { EvidenceId = "FORECAST_NAIVE_RMSE", ShortClaim = "Naive RMSE", Value = "1.2" },
                        new EvidenceFact { EvidenceId = "SEG_SHARE_1", ShortClaim = "Top segment share", Value = "44" }
                    ],
                    ForecastPack = new ForecastPack
                    {
                        Horizon = 14,
                        Methods =
                        [
                            new ForecastMethodEvidence { Method = "naive", Rmse = 1.2 },
                            new ForecastMethodEvidence { Method = "movingAverage", Rmse = 1.1 },
                            new ForecastMethodEvidence { Method = "linearRegression", Rmse = 1.0 }
                        ]
                    }
                },
                CacheHit = false
            }));
        }
    }

    private sealed class StubLLMClient : ILLMClient
    {
        private readonly Result<LLMResponse> _response;

        public StubLLMClient(Result<LLMResponse> response)
        {
            _response = response;
        }

        public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }

        public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class FakeDataSetApplicationService : IDataSetApplicationService
    {
        public Task<Result<UploadDataSetResponse>> UploadAsync(IFormFile file, long? maxFileSizeBytes = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<UploadDataSetResponse>("Not used."));
        }

        public Task<Result<DatasetProfile>> GetProfileAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new DatasetProfile
            {
                DatasetId = datasetId,
                RowCount = 10_000,
                SampleSize = 5_000,
                Columns =
                [
                    new ColumnProfile { Name = "date", InferredType = InferredType.Date, NullRate = 0.01, DistinctCount = 365 },
                    new ColumnProfile { Name = "metric", InferredType = InferredType.Number, NullRate = 0.0, DistinctCount = 8000, Min = 1, Max = 999 }
                ]
            }));
        }

        public Task<Result<List<ChartRecommendation>>> GetRecommendationsAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new List<ChartRecommendation>
            {
                new()
                {
                    Id = "rec_001",
                    Title = "Trend",
                    Reason = "Synthetic",
                    Chart = new ChartMeta { Type = ChartType.Line },
                    Query = new ChartQuery
                    {
                        X = new FieldSpec { Column = "date", Role = AxisRole.Time, Bin = TimeBin.Day },
                        Y = new FieldSpec { Column = "metric", Role = AxisRole.Measure, Aggregation = Aggregation.Sum }
                    }
                }
            }));
        }

        public Task<Result<List<DataSetSummary>>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new List<DataSetSummary>()));
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
            var series = Enumerable.Range(0, 90)
                .Select(i => new object?[] { DateTime.UtcNow.Date.AddDays(i).ToString("yyyy-MM-dd"), 100 + (i * 0.5) })
                .ToList();

            return Task.FromResult(Result.Success(new ChartExecutionResponse
            {
                DatasetId = datasetId,
                RecommendationId = recommendationId,
                QueryHash = "hash",
                ExecutionResult = new ChartExecutionResult
                {
                    Option = new EChartsOption
                    {
                        Series = new List<Dictionary<string, object>>
                        {
                            new()
                            {
                                ["name"] = "metric",
                                ["type"] = "line",
                                ["data"] = series
                            }
                        }
                    },
                    RowCount = series.Count,
                    DuckDbMs = 12
                }
            }));
        }

        public Task<Result<ScenarioSimulationResponse>> SimulateAsync(Guid datasetId, ScenarioRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new ScenarioSimulationResponse
            {
                DatasetId = datasetId,
                TargetMetric = request.TargetMetric,
                TargetDimension = request.TargetDimension,
                QueryHash = "scenario-hash",
                BaselineSeries =
                [
                    new ScenarioSeriesPoint { Dimension = "A", Value = 100 },
                    new ScenarioSeriesPoint { Dimension = "B", Value = 120 }
                ],
                SimulatedSeries =
                [
                    new ScenarioSeriesPoint { Dimension = "A", Value = 110 },
                    new ScenarioSeriesPoint { Dimension = "B", Value = 130 }
                ],
                DeltaSeries =
                [
                    new ScenarioDeltaPoint { Dimension = "A", Baseline = 100, Simulated = 110, Delta = 10, DeltaPercent = 10 },
                    new ScenarioDeltaPoint { Dimension = "B", Baseline = 120, Simulated = 130, Delta = 10, DeltaPercent = 8.3 }
                ],
                DeltaSummary = new ScenarioDeltaSummary
                {
                    AverageDeltaPercent = 9.15,
                    MaxDeltaPercent = 10,
                    MinDeltaPercent = 8.3,
                    ChangedPoints = 2
                }
            }));
        }

        public Task<Result<BuildDataSetIndexResponse>> BuildIndexAsync(Guid datasetId, BuildIndexRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<BuildDataSetIndexResponse>("Not used."));
        }

        public Task<Result<DatasetIndex>> GetIndexAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<DatasetIndex>("Not used."));
        }

        public Task<Result<DatasetIndexStatus>> GetIndexStatusAsync(Guid datasetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure<DatasetIndexStatus>("Not used."));
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

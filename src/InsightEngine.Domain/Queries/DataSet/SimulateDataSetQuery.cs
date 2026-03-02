using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using MediatR;

namespace InsightEngine.Domain.Queries.DataSet;

public class SimulateDataSetQuery : Query<ScenarioSimulationResponse>
{
    public Guid DatasetId { get; set; }
    public ScenarioRequest Request { get; set; } = new();
}

public class SimulateDataSetQueryHandler : IRequestHandler<SimulateDataSetQuery, Result<ScenarioSimulationResponse>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly IScenarioSimulationService _scenarioSimulationService;
    private readonly ICurrentUser _currentUser;
    private readonly InsightEngineFeatures _features;

    public SimulateDataSetQueryHandler(
        IDataSetRepository dataSetRepository,
        ICsvProfiler csvProfiler,
        IDataSetSchemaStore schemaStore,
        IScenarioSimulationService scenarioSimulationService,
        ICurrentUser currentUser,
        InsightEngineFeatures features)
    {
        _dataSetRepository = dataSetRepository;
        _csvProfiler = csvProfiler;
        _schemaStore = schemaStore;
        _scenarioSimulationService = scenarioSimulationService;
        _currentUser = currentUser;
        _features = features;
    }

    public async Task<Result<ScenarioSimulationResponse>> Handle(SimulateDataSetQuery request, CancellationToken cancellationToken)
    {
        if (_features.AuthRequiredForDatasets && (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue))
        {
            return Result.Failure<ScenarioSimulationResponse>("Unauthorized");
        }

        var dataSet = _currentUser.IsAuthenticated && _currentUser.UserId.HasValue
            ? await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken)
            : await _dataSetRepository.GetByIdAsync(request.DatasetId);
        if (dataSet is null)
        {
            return Result.Failure<ScenarioSimulationResponse>($"Dataset not found: {request.DatasetId}");
        }

        if (!File.Exists(dataSet.StoredPath))
        {
            return Result.Failure<ScenarioSimulationResponse>($"Dataset file not found: {request.DatasetId}");
        }

        var profile = await _csvProfiler.ProfileAsync(request.DatasetId, dataSet.StoredPath, cancellationToken);
        var schema = await _schemaStore.LoadAsync(request.DatasetId, cancellationToken);
        profile = DatasetSchemaProfileMapper.ApplySchema(profile, schema);

        return await _scenarioSimulationService.SimulateAsync(
            request.DatasetId,
            profile,
            request.Request,
            cancellationToken);
    }
}

using FluentValidation;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Behaviors;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.Data.Configuration;
using InsightEngine.Infra.Data.Context;
using InsightEngine.Infra.Data.Repositories;
using InsightEngine.Infra.Data.Services;
using InsightEngine.Infra.Data.UoW;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace InsightEngine.CrossCutting.IoC;

public static class NativeInjectorBootStrapper
{
    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var runtimeSettings = configuration
            .GetSection(InsightEngineSettings.SectionName)
            .Get<InsightEngineSettings>() ?? new InsightEngineSettings();

        // Domain - Notifications
        services.AddScoped<IDomainNotificationHandler, DomainNotificationHandler>();

        // Configuration - Settings
        services.Configure<ChartExecutionSettings>(configuration.GetSection("ChartExecution"));
        services.Configure<ChartCacheSettings>(configuration.GetSection("ChartCache"));
        services.Configure<ScenarioSimulationSettings>(configuration.GetSection("ScenarioSimulation"));
        services.PostConfigure<ChartExecutionSettings>(options =>
        {
            options.ScatterMaxPoints = runtimeSettings.ScatterMaxPoints;
            options.HistogramMinBins = runtimeSettings.HistogramBinsMin;
            options.HistogramMaxBins = runtimeSettings.HistogramBinsMax;
            options.TimeSeriesMaxPoints = runtimeSettings.QueryResultMaxRows;
            if (options.HistogramBins < options.HistogramMinBins || options.HistogramBins > options.HistogramMaxBins)
            {
                options.HistogramBins = Math.Clamp(options.HistogramBins, options.HistogramMinBins, options.HistogramMaxBins);
            }
        });
        services.PostConfigure<ChartCacheSettings>(options =>
        {
            options.TtlSeconds = runtimeSettings.CacheTtlSeconds;
        });
        services.PostConfigure<ScenarioSimulationSettings>(options =>
        {
            options.MaxRowsReturned = runtimeSettings.QueryResultMaxRows;
        });

        // Infra - Data
        services.AddDbContext<InsightEngineContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // Repositories específicos
        services.AddScoped<IDataSetRepository, DataSetRepository>();

        // Services
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<ICsvProfiler, CsvProfiler>();
        services.AddScoped<IChartExecutionService, ChartExecutionService>();
        services.AddScoped<IScenarioSimulationService, ScenarioSimulationService>();
        services.AddScoped<InsightEngine.Domain.Services.RecommendationEngine>();

        services.AddMemoryCache();
        services.AddSingleton<IChartQueryCache, ChartQueryCacheService>();
        
        // Task 6.4: Metadata cache service
        services.AddSingleton<IMetadataCacheService, MetadataCacheService>();

        // Application Services (thin orchestration layer)
        services.AddScoped<IDataSetApplicationService, DataSetApplicationService>();

        // Application - AutoMapper
        services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

        // Domain - MediatR with Domain Commands/Queries
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(InsightEngine.Domain.Commands.Command).Assembly);
        });

        // Domain - FluentValidation
        services.AddValidatorsFromAssembly(typeof(InsightEngine.Domain.Commands.Command).Assembly);

        // Domain - Pipeline Behaviors (order matters: Logging -> Performance -> Validation -> Handler)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Infra - External Services
        services.AddHttpClient();
    }
}

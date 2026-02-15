using FluentValidation;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Behaviors;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Infra.Data.Configuration;
using InsightEngine.Infra.Data.Context;
using InsightEngine.Infra.Data.Repositories;
using InsightEngine.Infra.Data.Services;
using InsightEngine.Infra.Data.UoW;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InsightEngine.CrossCutting.IoC;

public static class NativeInjectorBootStrapper
{
    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Domain - Notifications
        services.AddScoped<IDomainNotificationHandler, DomainNotificationHandler>();

        // Configuration - Settings
        services.Configure<ChartExecutionSettings>(configuration.GetSection("ChartExecution"));

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
        services.AddScoped<InsightEngine.Domain.Services.RecommendationEngine>();
        
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

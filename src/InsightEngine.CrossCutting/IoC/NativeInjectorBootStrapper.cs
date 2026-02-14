using FluentValidation;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Behaviors;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
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
        services.AddScoped<InsightEngine.Domain.Services.RecommendationEngine>();

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

        // Domain - Pipeline Behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Infra - External Services
        services.AddHttpClient();
    }
}

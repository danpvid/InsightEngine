using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Infra.Data.Context;
using InsightEngine.Infra.Data.Repositories;
using InsightEngine.Infra.Data.Services;
using InsightEngine.Infra.Data.UoW;
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

        // Application - AutoMapper
        services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

        // Application - MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(InsightEngine.Application.Commands.Command).Assembly));

        // Infra - External Services
        services.AddHttpClient();
    }
}

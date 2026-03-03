using InsightEngine.API.Configuration;
using InsightEngine.API.Middleware;
using InsightEngine.API.Models;
using InsightEngine.API.Services;
using InsightEngine.API.Validators;
using InsightEngine.CrossCutting.IoC;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.Data.Context;
using InsightEngine.Infra.Data.Identity;
using DuckDB.NET.Data;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MediatR;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var runtimeSettingsSection = builder.Configuration.GetSection(InsightEngineSettings.SectionName);
var runtimeSettings = runtimeSettingsSection.Get<InsightEngineSettings>() ?? new InsightEngineSettings();
builder.Services.Configure<InsightEngineSettings>(runtimeSettingsSection);
builder.Services.Configure<LLMSettings>(builder.Configuration.GetSection(LLMSettings.SectionName));
builder.Services.Configure<InsightEngineFeatures>(builder.Configuration.GetSection(InsightEngineFeatures.SectionName));
builder.Services.Configure<AdminSeedSettings>(builder.Configuration.GetSection(AdminSeedSettings.SectionName));

// Configurar Kestrel para suportar uploads com limite centralizado
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = runtimeSettings.UploadMaxBytes;
});

// Configurar Form Options para uploads com limite centralizado
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = runtimeSettings.UploadMaxBytes;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Add services to the container.
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Task 6.3: Standardize JSON serialization
        
        // Converter enums para string no JSON (não números)
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        
        // Usar camelCase para nomes de propriedades
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        
        // Ignorar propriedades null no JSON
        options.JsonSerializerOptions.DefaultIgnoreCondition = 
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        
        // Permitir trailing commas em JSON (flexibilidade)
        options.JsonSerializerOptions.AllowTrailingCommas = true;
        
        // Permitir comentários em JSON (útil para debugging)
        options.JsonSerializerOptions.ReadCommentHandling = 
            System.Text.Json.JsonCommentHandling.Skip;
        
        // Case-insensitive property names ao fazer desserialização
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        
        // Configurar números para permitir NaN e Infinity (dados científicos)
        options.JsonSerializerOptions.NumberHandling = 
            System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage)
                    .ToList());

        var response = ApiErrorResponse.FromValidationErrors(errors, traceId, StatusCodes.Status400BadRequest);
        return new BadRequestObjectResult(response);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddValidatorsFromAssemblyContaining<AiChartRequestValidator>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(InsightEngine.API.CQRS.Auth.RegisterCommand).Assembly);
});

// Configure API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = Microsoft.AspNetCore.Mvc.Versioning.ApiVersionReader.Combine(
        new Microsoft.AspNetCore.Mvc.Versioning.UrlSegmentApiVersionReader(),
        new Microsoft.AspNetCore.Mvc.Versioning.HeaderApiVersionReader("x-api-version"),
        new Microsoft.AspNetCore.Mvc.Versioning.QueryStringApiVersionReader("api-version")
    );
});

// Configure API Version Explorer
builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Configure JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

// Configure Upload Settings
builder.Services.Configure<UploadSettings>(options =>
{
    builder.Configuration.GetSection("UploadSettings").Bind(options);
    options.MaxFileSizeBytes = runtimeSettings.UploadMaxBytes;
});

// Configure JWT Authentication
builder.Services.AddJwtAuthentication(builder.Configuration);

// Configure Swagger with JWT and Versioning
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerConfiguration();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        corsBuilder =>
        {
            corsBuilder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
    
    // Policy específica para o frontend Angular
    options.AddPolicy("AllowAngular",
        corsBuilder =>
        {
            corsBuilder.WithOrigins("http://localhost:4200", "https://localhost:4200")
                   .AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials()
                   .WithExposedHeaders("Content-Disposition");
        });
});

// Register Token Service
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<InsightEngine.Domain.Interfaces.ICurrentUser, CurrentUser>();
builder.Services.AddHostedService<DataSetRetentionBackgroundService>();

// Register application services
NativeInjectorBootStrapper.RegisterServices(builder.Services, builder.Configuration);
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<InsightEngineContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var metadataSettings = scope.ServiceProvider
        .GetRequiredService<IOptions<MetadataPersistenceSettings>>()
        .Value;

    if (metadataSettings.Enabled)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<InsightEngineContext>();
        dbContext.Database.EnsureCreated();
        EnsureAuthSchema(dbContext, logger);
        logger.LogInformation("Metadata store initialized using SQLite.");
    }

    await SeedDefaultAdminAsync(scope.ServiceProvider, logger);
}

// Get API Version Description Provider
var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerConfiguration(apiVersionDescriptionProvider);
}

app.UseMiddleware<RequestTimingLoggingMiddleware>();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error != null)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(feature.Error, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var response = ApiErrorResponse.FromMessage(
            "Unexpected server error.",
            traceId,
            "internal_error",
            StatusCodes.Status500InternalServerError);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(response);
    });
});

app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    var statusCode = context.Response.StatusCode;
    if (statusCode < 400 || context.Response.HasStarted)
    {
        return;
    }

    var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
    var code = statusCode switch
    {
        StatusCodes.Status400BadRequest => "validation_error",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        _ => "http_error"
    };

    var message = statusCode switch
    {
        StatusCodes.Status400BadRequest => "Invalid request.",
        StatusCodes.Status404NotFound => "Resource not found.",
        StatusCodes.Status413PayloadTooLarge => "Payload too large.",
        _ => "Request failed."
    };

    var response = ApiErrorResponse.FromMessage(message, traceId, code, statusCode);
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(response);
});

app.UseHttpsRedirection();

// Use política mais permissiva em desenvolvimento, específica em produção
app.UseCors(app.Environment.IsDevelopment() ? "AllowAll" : "AllowAngular");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        service = "InsightEngine.API",
        timestampUtc = DateTime.UtcNow
    });
});

app.MapGet("/health/ready", (ILogger<Program> logger) =>
{
    try
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        var result = Convert.ToInt32(command.ExecuteScalar() ?? 0);
        if (result != 1)
        {
            throw new InvalidOperationException("DuckDB did not return expected value.");
        }

        return Results.Ok(new
        {
            status = "ready",
            duckDb = "ok",
            timestampUtc = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health readiness check failed.");
        return Results.Json(new
        {
            status = "unhealthy",
            duckDb = "error",
            timestampUtc = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static void EnsureAuthSchema(InsightEngineContext dbContext, ILogger logger)
{
    try
    {
        using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using (var users = connection.CreateCommand())
        {
            users.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id TEXT NOT NULL PRIMARY KEY,
                    UserName TEXT NULL,
                    NormalizedUserName TEXT NULL,
                    Email TEXT NULL,
                    NormalizedEmail TEXT NULL,
                    EmailConfirmed INTEGER NOT NULL DEFAULT 0,
                    PasswordHash TEXT NULL,
                    SecurityStamp TEXT NULL,
                    ConcurrencyStamp TEXT NULL,
                    PhoneNumber TEXT NULL,
                    PhoneNumberConfirmed INTEGER NOT NULL DEFAULT 0,
                    TwoFactorEnabled INTEGER NOT NULL DEFAULT 0,
                    LockoutEnd TEXT NULL,
                    LockoutEnabled INTEGER NOT NULL DEFAULT 0,
                    AccessFailedCount INTEGER NOT NULL DEFAULT 0,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    AvatarUrl TEXT NULL,
                    Plan TEXT NOT NULL DEFAULT 'Free',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL
                );";
            users.ExecuteNonQuery();
        }

        EnsureColumn(connection, "Users", "UserName", "TEXT NULL");
        EnsureColumn(connection, "Users", "NormalizedUserName", "TEXT NULL");
        EnsureColumn(connection, "Users", "NormalizedEmail", "TEXT NULL");
        EnsureColumn(connection, "Users", "EmailConfirmed", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Users", "SecurityStamp", "TEXT NULL");
        EnsureColumn(connection, "Users", "ConcurrencyStamp", "TEXT NULL");
        EnsureColumn(connection, "Users", "PhoneNumber", "TEXT NULL");
        EnsureColumn(connection, "Users", "PhoneNumberConfirmed", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Users", "TwoFactorEnabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Users", "LockoutEnd", "TEXT NULL");
        EnsureColumn(connection, "Users", "LockoutEnabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Users", "AccessFailedCount", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Users", "DisplayName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "Users", "AvatarUrl", "TEXT NULL");
        EnsureColumn(connection, "Users", "Plan", "TEXT NOT NULL DEFAULT 'Free'");
        EnsureColumn(connection, "Users", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "Users", "CreatedAt", "TEXT NOT NULL DEFAULT (datetime('now'))");
        EnsureColumn(connection, "Users", "UpdatedAt", "TEXT NULL");

        using (var userIndexes = connection.CreateCommand())
        {
            userIndexes.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_NormalizedUserName ON Users (NormalizedUserName);
                CREATE INDEX IF NOT EXISTS IX_Users_NormalizedEmail ON Users (NormalizedEmail);";
            userIndexes.ExecuteNonQuery();
        }

        using (var refreshTokens = connection.CreateCommand())
        {
            refreshTokens.CommandText = @"
                CREATE TABLE IF NOT EXISTS RefreshTokens (
                    Id TEXT NOT NULL PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    Token TEXT NOT NULL,
                    ExpiresAtUtc TEXT NOT NULL,
                    RevokedAtUtc TEXT NULL,
                    ReplacedByToken TEXT NULL,
                    CreatedByIp TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_RefreshTokens_Token ON RefreshTokens (Token);
                CREATE INDEX IF NOT EXISTS IX_RefreshTokens_UserId_ExpiresAtUtc ON RefreshTokens (UserId, ExpiresAtUtc);";
            refreshTokens.ExecuteNonQuery();
        }

        EnsureColumn(connection, "RefreshTokens", "CreatedByIp", "TEXT NULL");

        using (var dashboardCache = connection.CreateCommand())
        {
            dashboardCache.CommandText = @"
                CREATE TABLE IF NOT EXISTS DashboardCache (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OwnerUserId TEXT NOT NULL,
                    DatasetId TEXT NOT NULL,
                    Version TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    SourceDatasetUpdatedAt TEXT NOT NULL,
                    SourceFingerprint TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_DashboardCache_OwnerUserId_DatasetId_Version 
                    ON DashboardCache (OwnerUserId, DatasetId, Version);
                CREATE INDEX IF NOT EXISTS IX_DashboardCache_OwnerUserId_DatasetId 
                    ON DashboardCache (OwnerUserId, DatasetId);";
            dashboardCache.ExecuteNonQuery();
        }

        EnsureColumn(connection, "DashboardCache", "SourceFingerprint", "TEXT NOT NULL DEFAULT ''");

        using var checkColumn = connection.CreateCommand();
        checkColumn.CommandText = "PRAGMA table_info(DataSets);";
        using var reader = checkColumn.ExecuteReader();
        var hasOwnerUserId = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "OwnerUserId", StringComparison.OrdinalIgnoreCase))
            {
                hasOwnerUserId = true;
                break;
            }
        }

        if (!hasOwnerUserId)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE DataSets ADD COLUMN OwnerUserId TEXT NULL;";
            alter.ExecuteNonQuery();

            using var index = connection.CreateCommand();
            index.CommandText = "CREATE INDEX IF NOT EXISTS IX_DataSets_OwnerUserId_Id ON DataSets (OwnerUserId, Id);";
            index.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to ensure auth schema bootstrap.");
    }
}

static void EnsureColumn(System.Data.Common.DbConnection connection, string tableName, string columnName, string sqlDefinition)
{
    using var pragma = connection.CreateCommand();
    pragma.CommandText = $"PRAGMA table_info({tableName});";
    using var reader = pragma.ExecuteReader();
    while (reader.Read())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    using var alter = connection.CreateCommand();
    alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlDefinition};";
    alter.ExecuteNonQuery();
}

static async Task SeedDefaultAdminAsync(IServiceProvider serviceProvider, ILogger logger)
{
    try
    {
        var settings = serviceProvider.GetRequiredService<IOptions<AdminSeedSettings>>().Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password))
        {
            logger.LogWarning("Admin seed enabled but Email/Password are empty. Skipping admin seed.");
            return;
        }

        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var normalizedEmail = settings.Email.Trim();
        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(settings.DisplayName) ? "Admin" : settings.DisplayName.Trim(),
            Plan = string.IsNullOrWhiteSpace(settings.Plan) ? "Enterprise" : settings.Plan.Trim(),
            AvatarUrl = settings.AvatarUrl?.Trim() ?? string.Empty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(admin, settings.Password);
        if (!createResult.Succeeded)
        {
            logger.LogWarning("Failed to seed default admin user: {Errors}", string.Join(" | ", createResult.Errors.Select(error => error.Description)));
            return;
        }

        logger.LogInformation("Default admin user seeded successfully: {Email}", normalizedEmail);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to seed default admin user.");
    }
}

public partial class Program { }

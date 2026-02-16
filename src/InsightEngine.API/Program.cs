using InsightEngine.API.Configuration;
using InsightEngine.API.Middleware;
using InsightEngine.API.Models;
using InsightEngine.API.Services;
using InsightEngine.CrossCutting.IoC;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.Data.Context;
using DuckDB.NET.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var runtimeSettingsSection = builder.Configuration.GetSection(InsightEngineSettings.SectionName);
var runtimeSettings = runtimeSettingsSection.Get<InsightEngineSettings>() ?? new InsightEngineSettings();
builder.Services.Configure<InsightEngineSettings>(runtimeSettingsSection);

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
builder.Services.AddHostedService<DataSetRetentionBackgroundService>();

// Register application services
NativeInjectorBootStrapper.RegisterServices(builder.Services, builder.Configuration);

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
        logger.LogInformation("Metadata store initialized using SQLite.");
    }
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

public partial class Program { }

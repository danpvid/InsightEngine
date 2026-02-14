using InsightEngine.API.Configuration;
using InsightEngine.API.Services;
using InsightEngine.CrossCutting.IoC;
using InsightEngine.Domain.Settings;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// Configurar Kestrel para suportar arquivos grandes
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024; // 20MB (MVP)
});

// Configurar Form Options para uploads grandes
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024; // 20MB (MVP)
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
builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection("UploadSettings"));

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
});

// Register Token Service
builder.Services.AddScoped<ITokenService, TokenService>();

// Register application services
NativeInjectorBootStrapper.RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Get API Version Description Provider
var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerConfiguration(apiVersionDescriptionProvider);
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }

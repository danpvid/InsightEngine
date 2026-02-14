using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace InsightEngine.API.Configuration;

public static class SwaggerConfiguration
{
    public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            // Configuração de autenticação JWT no Swagger
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Insira o token JWT desta forma: Bearer {seu token}"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Configuração para versionamento
            c.OperationFilter<RemoveVersionParameterFilter>();
            c.DocumentFilter<ReplaceVersionWithExactValueInPathFilter>();

            // Incluir comentários XML se disponível
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    public static IApplicationBuilder UseSwaggerConfiguration(this IApplicationBuilder app, IApiVersionDescriptionProvider provider)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            // Criar um documento Swagger para cada versão descoberta
            foreach (var description in provider.ApiVersionDescriptions)
            {
                c.SwaggerEndpoint(
                    $"/swagger/{description.GroupName}/swagger.json",
                    $"InsightEngine API {description.GroupName.ToUpperInvariant()}");
            }
            
            c.RoutePrefix = "swagger";
        });

        return app;
    }
}

/// <summary>
/// Remove o parâmetro de versão dos endpoints no Swagger
/// </summary>
public class RemoveVersionParameterFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters == null) return;

        var versionParameter = operation.Parameters
            .FirstOrDefault(p => p.Name == "version");

        if (versionParameter != null)
        {
            operation.Parameters.Remove(versionParameter);
        }
    }
}

/// <summary>
/// Substitui o placeholder de versão pelo valor real no path
/// </summary>
public class ReplaceVersionWithExactValueInPathFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var paths = new OpenApiPaths();

        foreach (var (key, value) in swaggerDoc.Paths)
        {
            var newKey = key.Replace("v{version}", swaggerDoc.Info.Version);
            paths.Add(newKey, value);
        }

        swaggerDoc.Paths = paths;
    }
}

/// <summary>
/// Configuração do Swagger para versionamento
/// </summary>
public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(
                description.GroupName,
                new OpenApiInfo
                {
                    Title = "InsightEngine API",
                    Version = description.ApiVersion.ToString(),
                    Description = description.IsDeprecated 
                        ? "Esta versão da API foi descontinuada." 
                        : "API do InsightEngine com arquitetura limpa, CQRS e Domain Notifications",
                    Contact = new OpenApiContact
                    {
                        Name = "InsightEngine Team",
                        Email = "contato@insightengine.com"
                    }
                });
        }
    }
}

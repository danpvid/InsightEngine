namespace InsightEngine.API.Models;

/// <summary>
/// Envelope padrão para respostas de erro da API
/// </summary>
public class ApiErrorResponse
{
    /// <summary>
    /// Sempre false em respostas de erro
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Sempre null em respostas de erro
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Dicionário de erros agrupados por campo/chave
    /// Formato: { "fieldName": ["erro1", "erro2"], "general": ["erro geral"] }
    /// </summary>
    public Dictionary<string, List<string>> Errors { get; set; } = new();

    /// <summary>
    /// ID de rastreamento para correlação de logs
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    public ApiErrorResponse()
    {
    }

    public ApiErrorResponse(string traceId)
    {
        TraceId = traceId;
    }

    public ApiErrorResponse(Dictionary<string, List<string>> errors, string traceId)
    {
        Errors = errors;
        TraceId = traceId;
    }

    /// <summary>
    /// Cria resposta de erro com mensagem única sem campo específico
    /// </summary>
    public static ApiErrorResponse FromMessage(string message, string traceId)
    {
        return new ApiErrorResponse
        {
            Errors = new Dictionary<string, List<string>>
            {
                ["general"] = new List<string> { message }
            },
            TraceId = traceId
        };
    }

    /// <summary>
    /// Cria resposta de erro a partir de lista de strings (Result Pattern)
    /// </summary>
    public static ApiErrorResponse FromList(List<string> errors, string traceId)
    {
        return new ApiErrorResponse
        {
            Errors = new Dictionary<string, List<string>>
            {
                ["general"] = errors
            },
            TraceId = traceId
        };
    }

    /// <summary>
    /// Adiciona erro a um campo específico
    /// </summary>
    public void AddError(string field, string message)
    {
        if (!Errors.ContainsKey(field))
            Errors[field] = new List<string>();

        Errors[field].Add(message);
    }
}

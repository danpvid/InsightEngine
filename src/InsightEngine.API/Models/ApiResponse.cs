namespace InsightEngine.API.Models;

/// <summary>
/// Envelope padrão para respostas bem-sucedidas da API
/// </summary>
/// <typeparam name="T">Tipo de dados retornados</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indica se a operação foi bem-sucedida
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Dados retornados pela operação
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Sempre null em respostas de sucesso
    /// </summary>
    public object? Errors { get; set; }

    /// <summary>
    /// ID de rastreamento para correlação de logs
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    public ApiResponse()
    {
    }

    public ApiResponse(T data, string traceId)
    {
        Success = true;
        Data = data;
        Errors = null;
        TraceId = traceId;
    }
}

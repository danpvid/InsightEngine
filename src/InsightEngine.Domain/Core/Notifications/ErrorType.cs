namespace InsightEngine.Domain.Core.Notifications;

/// <summary>
/// Tipos de erro para mapeamento de HTTP status codes
/// </summary>
public enum ErrorType
{
    /// <summary>
    /// Erro de validação (400 Bad Request)
    /// </summary>
    Validation = 400,

    /// <summary>
    /// Recurso não encontrado (404 Not Found)
    /// </summary>
    NotFound = 404,

    /// <summary>
    /// Conflito de negócio (409 Conflict)
    /// </summary>
    Conflict = 409,

    /// <summary>
    /// Erro de processamento de entidade (422 Unprocessable Entity)
    /// </summary>
    UnprocessableEntity = 422,

    /// <summary>
    /// Erro interno do servidor (500 Internal Server Error)
    /// </summary>
    InternalError = 500
}

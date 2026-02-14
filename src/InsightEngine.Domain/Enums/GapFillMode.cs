namespace InsightEngine.Domain.Enums;

/// <summary>
/// Modos de preenchimento de lacunas (gaps) em séries temporais
/// </summary>
public enum GapFillMode
{
    /// <summary>
    /// Não preenche lacunas - retorna apenas pontos existentes
    /// </summary>
    None = 0,

    /// <summary>
    /// Preenche lacunas com valores null
    /// </summary>
    Nulls = 1,

    /// <summary>
    /// Preenche lacunas com o último valor não-null (forward fill)
    /// </summary>
    ForwardFill = 2,

    /// <summary>
    /// Preenche lacunas com zeros
    /// </summary>
    Zeros = 3
}

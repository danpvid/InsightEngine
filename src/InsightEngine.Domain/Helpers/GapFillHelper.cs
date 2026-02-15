using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Helpers;

/// <summary>
/// Helper para preencher lacunas (gaps) em séries temporais
/// </summary>
public static class GapFillHelper
{
    /// <summary>
    /// Preenche lacunas em uma série temporal baseado no modo configurado
    /// </summary>
    /// <param name="points">Lista de pontos (timestampMs, value)</param>
    /// <param name="mode">Modo de preenchimento</param>
    /// <param name="timeBin">Granularidade temporal (Day, Month, Year)</param>
    /// <returns>Lista de pontos com lacunas preenchidas</returns>
    public static List<(long TimestampMs, double? Value)> FillGaps(
        List<(long TimestampMs, double Value)> points,
        GapFillMode mode,
        TimeBin timeBin)
    {
        if (mode == GapFillMode.None || points.Count == 0)
        {
            return points.Select(p => (p.TimestampMs, (double?)p.Value)).ToList();
        }

        // Ordenar por timestamp
        var sortedPoints = points.OrderBy(p => p.TimestampMs).ToList();

        // Descobrir min e max dates
        var minDate = DateTimeOffset.FromUnixTimeMilliseconds(sortedPoints.First().TimestampMs).UtcDateTime;
        var maxDate = DateTimeOffset.FromUnixTimeMilliseconds(sortedPoints.Last().TimestampMs).UtcDateTime;

        // Criar dicionário para lookup rápido
        var pointDict = sortedPoints.ToDictionary(
            p => DateTimeOffset.FromUnixTimeMilliseconds(p.TimestampMs).UtcDateTime.Date,
            p => p.Value
        );

        var result = new List<(long TimestampMs, double? Value)>();
        var currentDate = minDate.Date;
        double? lastNonNullValue = null;

        // Iterar por cada período baseado no TimeBin
        while (currentDate <= maxDate.Date)
        {
            var timestampMs = new DateTimeOffset(currentDate, TimeSpan.Zero).ToUnixTimeMilliseconds();

            if (pointDict.TryGetValue(currentDate, out var value))
            {
                // Ponto existe
                result.Add((timestampMs, value));
                lastNonNullValue = value;
            }
            else
            {
                // Lacuna - aplicar modo de preenchimento
                double? fillValue = mode switch
                {
                    GapFillMode.Nulls => null,
                    GapFillMode.ForwardFill => lastNonNullValue,
                    GapFillMode.Zeros => 0.0,
                    _ => null
                };

                result.Add((timestampMs, fillValue));
            }

            // Avançar para próximo período
            currentDate = timeBin switch
            {
                TimeBin.Day => currentDate.AddDays(1),
                TimeBin.Week => currentDate.AddDays(7),
                TimeBin.Month => currentDate.AddMonths(1),
                TimeBin.Quarter => currentDate.AddMonths(3),
                TimeBin.Year => currentDate.AddYears(1),
                _ => currentDate.AddDays(1)
            };
        }

        return result;
    }
}

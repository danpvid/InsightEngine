using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Infra.Data.Services;

public class CsvProfiler : ICsvProfiler
{
    private const int MaxSampleRows = 5000;
    private const double TypeInferenceThreshold = 0.9; // 90%
    private const int TopValuesCount = 3;
    private const int MaxDistinctTracking = 10000; // Limite para não explodir memória

    public async Task<DatasetProfile> ProfileAsync(Guid datasetId, string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"CSV file not found: {filePath}");

        var profile = new DatasetProfile 
        { 
            DatasetId = datasetId,
            SampleSize = 0
        };

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        // Ler cabeçalho
        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? throw new InvalidOperationException("CSV file has no header");

        // Inicializar estatísticas por coluna
        var columnStats = headers.Select(h => new ColumnStats(h)).ToList();
        var rowCount = 0;
        var totalRowCount = 0;

        // Processar linhas (amostra de MaxSampleRows)
        while (await csv.ReadAsync() && rowCount < MaxSampleRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (int i = 0; i < columnStats.Count; i++)
            {
                var value = csv.GetField(i)?.Trim() ?? string.Empty;
                columnStats[i].ProcessValue(value);
            }

            rowCount++;
        }

        // Contar linhas restantes sem carregar em memória
        while (await csv.ReadAsync())
        {
            totalRowCount++;
        }

        totalRowCount += rowCount; // Total = amostra + resto

        profile.RowCount = totalRowCount;
        profile.SampleSize = rowCount;
        profile.Columns = columnStats.Select(s => s.ToColumnProfile(rowCount)).ToList();

        return profile;
    }

    /// <summary>
    /// Classe auxiliar para acumular estatísticas de uma coluna
    /// </summary>
    private class ColumnStats
    {
        public string Name { get; }
        private int _nullCount;
        private int _numberOk;
        private int _dateOk;
        private int _boolOk;
        private readonly HashSet<string> _distinctValues;
        private readonly Dictionary<string, int> _valueFrequency;
        private bool _distinctTrackingActive = true;

        public ColumnStats(string name)
        {
            Name = name;
            _distinctValues = new HashSet<string>();
            _valueFrequency = new Dictionary<string, int>();
        }

        public void ProcessValue(string value)
        {
            // Null check
            if (string.IsNullOrWhiteSpace(value))
            {
                _nullCount++;
                return;
            }

            // Distinct tracking (com limite)
            if (_distinctTrackingActive)
            {
                _distinctValues.Add(value);
                if (_distinctValues.Count > MaxDistinctTracking)
                {
                    _distinctTrackingActive = false;
                    _distinctValues.Clear(); // Liberar memória
                }
            }

            // Frequency tracking (top values)
            if (_valueFrequency.Count < 1000) // Limite para não explodir memória
            {
                _valueFrequency[value] = _valueFrequency.GetValueOrDefault(value, 0) + 1;
            }

            // Type inference counters
            if (IsBooleanValue(value)) _boolOk++;
            if (IsDateValue(value)) _dateOk++;
            if (IsNumericValue(value)) _numberOk++;
        }

        public ColumnProfile ToColumnProfile(int totalRows)
        {
            var nonNull = totalRows - _nullCount;
            
            return new ColumnProfile
            {
                Name = Name,
                NullRate = totalRows > 0 ? (double)_nullCount / totalRows : 0,
                DistinctCount = _distinctTrackingActive ? _distinctValues.Count : MaxDistinctTracking,
                TopValues = _valueFrequency
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(TopValuesCount)
                    .Select(kvp => kvp.Key)
                    .ToList(),
                InferredType = InferType(nonNull, totalRows)
            };
        }

        private InferredType InferType(int nonNull, int totalRows)
        {
            if (nonNull == 0)
                return InferredType.String;

            var boolRatio = (double)_boolOk / nonNull;
            var dateRatio = (double)_dateOk / nonNull;
            var numberRatio = (double)_numberOk / nonNull;

            // Heurística de inferência
            if (boolRatio >= TypeInferenceThreshold)
                return InferredType.Boolean;

            if (dateRatio >= TypeInferenceThreshold)
                return InferredType.Date;

            if (numberRatio >= TypeInferenceThreshold)
                return InferredType.Number;

            // Category: baixa cardinalidade
            if (_distinctTrackingActive)
            {
                var categoryThreshold = Math.Max(20, totalRows * 0.05);
                if (_distinctValues.Count <= categoryThreshold)
                    return InferredType.Category;
            }

            return InferredType.String;
        }

        private static bool IsBooleanValue(string value)
        {
            var normalized = value.ToLowerInvariant();
            return normalized is "true" or "false" or "yes" or "no" or "1" or "0"
                or "t" or "f" or "y" or "n" or "sim" or "não" or "nao";
        }

        private static bool IsDateValue(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                   DateTime.TryParseExact(value,
                       new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd",
                              "dd-MM-yyyy", "MM-dd-yyyy", "yyyyMMdd" },
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out _);
        }

        private static bool IsNumericValue(string value)
        {
            var cleaned = value.Replace(",", "").Replace(" ", "");
            return decimal.TryParse(cleaned,
                NumberStyles.Number | NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _);
        }
    }
}

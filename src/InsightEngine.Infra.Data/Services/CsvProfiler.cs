using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.ImportPreview;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class CsvProfiler : ICsvProfiler
{
    private const double TypeInferenceThreshold = 0.9; // 90%
    private const int MaxDistinctTracking = 10000;
    private const int MaxFrequencyTracking = 1000;
    private static readonly string[] MoneyNameHints =
    [
        "price", "amount", "total", "cost", "revenue", "saldo", "balance", "valor", "pagamento",
        "fee", "charge", "faturamento", "lucro", "profit", "receita", "custo"
    ];
    private static readonly string[] PercentageNameHints =
    [
        "percent", "pct", "rate", "ratio", "taxa", "share", "margem", "margin"
    ];
    private static readonly string[] IgnoredNameHints = ["id", "uuid", "guid", "hash", "token", "key"];

    private readonly int _maxSampleRows;
    private readonly int _topValuesCount;

    public CsvProfiler(IOptions<UploadSettings> uploadSettings)
    {
        var settings = uploadSettings.Value ?? new UploadSettings();
        _maxSampleRows = Math.Clamp(settings.ProfileSampleSize, 100, 50000);
        _topValuesCount = Math.Clamp(settings.ProfileTopValuesCount, 3, 50);
    }

    public async Task<DatasetProfile> ProfileAsync(Guid datasetId, string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"CSV file not found: {filePath}");
        }

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

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? throw new InvalidOperationException("CSV file has no header");

        var columnStats = headers.Select(h => new ColumnStats(h)).ToList();
        var rowCount = 0;
        var totalRowCount = 0;

        while (await csv.ReadAsync() && rowCount < _maxSampleRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var i = 0; i < columnStats.Count; i++)
            {
                var value = csv.GetField(i)?.Trim() ?? string.Empty;
                columnStats[i].ProcessValue(value);
            }

            rowCount++;
        }

        while (await csv.ReadAsync())
        {
            totalRowCount++;
        }

        totalRowCount += rowCount;

        profile.RowCount = totalRowCount;
        profile.SampleSize = rowCount;
        profile.Columns = columnStats.Select(s => s.ToColumnProfile(rowCount, _topValuesCount)).ToList();

        return profile;
    }

    public async Task<ImportPreviewResponse> AnalyzeSampleAsync(
        Guid datasetId,
        string filePath,
        int sampleSize = 200,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"CSV file not found: {filePath}");
        }

        var effectiveSampleSize = Math.Clamp(sampleSize, 20, _maxSampleRows);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? throw new InvalidOperationException("CSV file has no header");
        var columnStats = headers.Select(name => new ColumnStats(name)).ToList();
        var sampleRows = new List<Dictionary<string, string>>();
        var rowCount = 0;

        while (await csv.ReadAsync() && rowCount < effectiveSampleSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columnStats.Count; i++)
            {
                var value = csv.GetField(i)?.Trim() ?? string.Empty;
                row[headers[i]] = value;
                columnStats[i].ProcessValue(value);
            }

            sampleRows.Add(row);
            rowCount++;
        }

        var previewColumns = columnStats
            .Select(stats => stats.ToPreviewColumn(rowCount))
            .ToList();

        var targetCandidates = columnStats
            .Where(stats => stats.IsTargetCandidate())
            .Select(stats => stats.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ignoredCandidates = columnStats
            .Where(stats => stats.IsIgnoredCandidate(rowCount))
            .Select(stats => stats.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ImportPreviewResponse
        {
            TempUploadId = datasetId.ToString(),
            SampleSize = rowCount,
            Columns = previewColumns,
            SampleRows = sampleRows,
            SuggestedTargetCandidates = targetCandidates,
            SuggestedIgnoredCandidates = ignoredCandidates
        };
    }

    private sealed class ColumnStats
    {
        public string Name { get; }

        private int _nullCount;
        private int _numberOk;
        private int _integerOk;
        private int _dateOk;
        private int _boolOk;
        private int _currencySignCount;
        private int _percentSignCount;
        private int _twoDecimalCount;
        private int _betweenZeroAndOneCount;
        private int _betweenZeroAndHundredCount;
        private readonly HashSet<string> _distinctValues;
        private readonly Dictionary<string, int> _valueFrequency;
        private bool _distinctTrackingActive = true;
        private double? _min;
        private double? _max;

        public ColumnStats(string name)
        {
            Name = name;
            _distinctValues = new HashSet<string>();
            _valueFrequency = new Dictionary<string, int>();
        }

        public void ProcessValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _nullCount++;
                return;
            }

            if (_distinctTrackingActive)
            {
                _distinctValues.Add(value);
                if (_distinctValues.Count > MaxDistinctTracking)
                {
                    _distinctTrackingActive = false;
                    _distinctValues.Clear();
                }
            }

            if (_valueFrequency.Count < MaxFrequencyTracking)
            {
                _valueFrequency[value] = _valueFrequency.GetValueOrDefault(value, 0) + 1;
            }

            if (IsBooleanValue(value))
            {
                _boolOk++;
            }

            if (IsDateValue(value))
            {
                _dateOk++;
            }

            if (IsNumericValue(value, out var numericValue))
            {
                _numberOk++;

                if (IsIntegerLike(numericValue))
                {
                    _integerOk++;
                }

                if (Math.Abs(numericValue) <= 1)
                {
                    _betweenZeroAndOneCount++;
                }

                if (numericValue >= 0 && numericValue <= 100)
                {
                    _betweenZeroAndHundredCount++;
                }

                if (HasTwoDecimalPlaces(numericValue))
                {
                    _twoDecimalCount++;
                }

                _min = _min.HasValue ? Math.Min(_min.Value, numericValue) : numericValue;
                _max = _max.HasValue ? Math.Max(_max.Value, numericValue) : numericValue;
            }

            if (ContainsCurrencySymbol(value))
            {
                _currencySignCount++;
            }

            if (ContainsPercentSign(value))
            {
                _percentSignCount++;
            }
        }

        public ColumnProfile ToColumnProfile(int totalRows, int topValuesCount)
        {
            var nonNull = totalRows - _nullCount;
            var orderedTopValues = _valueFrequency
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(topValuesCount)
                .ToList();

            return new ColumnProfile
            {
                Name = Name,
                NullRate = totalRows > 0 ? (double)_nullCount / totalRows : 0,
                DistinctCount = _distinctTrackingActive ? _distinctValues.Count : MaxDistinctTracking,
                TopValues = orderedTopValues.Select(kvp => kvp.Key).ToList(),
                TopValueStats = orderedTopValues
                    .Select(kvp => new TopValueStat
                    {
                        Value = kvp.Key,
                        Count = kvp.Value
                    })
                    .ToList(),
                InferredType = InferType(nonNull, totalRows),
                ConfirmedType = null,
                IsIgnored = false,
                IsTarget = false,
                CurrencyCode = "BRL",
                HasPercentSign = nonNull > 0 && (double)_percentSignCount / nonNull >= 0.1,
                Min = _min,
                Max = _max
            };
        }

        public ImportPreviewColumn ToPreviewColumn(int totalRows)
        {
            var nonNull = Math.Max(0, totalRows - _nullCount);
            var reasons = new List<string>();
            var inferredType = InferTypeWithHints(nonNull, totalRows, reasons, out var confidence, out var hints);

            return new ImportPreviewColumn
            {
                Name = Name,
                InferredType = inferredType,
                Confidence = Math.Round(confidence, 3),
                Reasons = reasons,
                Hints = hints
            };
        }

        public bool IsTargetCandidate()
        {
            var nonNull = Math.Max(0, _numberOk);
            if (nonNull == 0)
            {
                return false;
            }

            var inferred = InferType(Math.Max(1, _numberOk), Math.Max(1, _numberOk));
            if (!inferred.IsNumericLike())
            {
                return false;
            }

            return _min.HasValue && _max.HasValue && Math.Abs(_max.Value - _min.Value) > double.Epsilon;
        }

        public bool IsIgnoredCandidate(int totalRows)
        {
            var nonNull = Math.Max(0, totalRows - _nullCount);
            if (nonNull == 0)
            {
                return false;
            }

            var distinct = _distinctTrackingActive ? _distinctValues.Count : MaxDistinctTracking;
            var nearUnique = distinct >= Math.Max(1, (int)Math.Round(nonNull * 0.95));
            var almostConstant = distinct <= 1;
            var nameHint = ContainsHint(Name, IgnoredNameHints);

            return almostConstant || (nearUnique && nameHint);
        }

        private InferredType InferType(int nonNull, int totalRows)
        {
            if (nonNull == 0)
            {
                return InferredType.String;
            }

            var boolRatio = (double)_boolOk / nonNull;
            var dateRatio = (double)_dateOk / nonNull;
            var numberRatio = (double)_numberOk / nonNull;

            if (boolRatio >= TypeInferenceThreshold)
            {
                return InferredType.Boolean;
            }

            if (dateRatio >= TypeInferenceThreshold)
            {
                return InferredType.Date;
            }

            if (numberRatio >= TypeInferenceThreshold)
            {
                if (_numberOk > 0 && (double)_integerOk / _numberOk >= TypeInferenceThreshold)
                {
                    return InferredType.Integer;
                }

                return InferredType.Decimal;
            }

            if (_distinctTrackingActive)
            {
                var categoryThreshold = Math.Max(20, totalRows * 0.05);
                if (_distinctValues.Count <= categoryThreshold)
                {
                    return InferredType.Category;
                }
            }

            return InferredType.String;
        }

        private InferredType InferTypeWithHints(
            int nonNull,
            int totalRows,
            List<string> reasons,
            out double confidence,
            out ImportPreviewHints hints)
        {
            var inferred = InferType(nonNull, totalRows).NormalizeLegacy();
            var numberRatio = nonNull == 0 ? 0 : (double)_numberOk / nonNull;
            var currencyRatio = nonNull == 0 ? 0 : (double)_currencySignCount / nonNull;
            var percentRatio = nonNull == 0 ? 0 : (double)_percentSignCount / nonNull;
            var integerRatio = _numberOk == 0 ? 0 : (double)_integerOk / _numberOk;
            var zeroToOneRatio = _numberOk == 0 ? 0 : (double)_betweenZeroAndOneCount / _numberOk;
            var zeroToHundredRatio = _numberOk == 0 ? 0 : (double)_betweenZeroAndHundredCount / _numberOk;
            var twoDecimalRatio = _numberOk == 0 ? 0 : (double)_twoDecimalCount / _numberOk;

            hints = new ImportPreviewHints
            {
                HasPercentSign = percentRatio >= 0.1,
                HasCurrencySymbol = currencyRatio >= 0.1,
                MostlyInteger = integerRatio >= 0.9,
                MostlyZeroToOne = zeroToOneRatio >= 0.75,
                MostlyZeroToHundred = zeroToHundredRatio >= 0.75,
                ConsistentTwoDecimalPlaces = twoDecimalRatio >= 0.75,
                CurrencyCode = "BRL"
            };

            if (numberRatio >= TypeInferenceThreshold)
            {
                if (percentRatio >= 0.3)
                {
                    inferred = InferredType.Percentage;
                    reasons.Add("Detected '%' symbol in a significant part of sampled values.");
                }
                else if (currencyRatio >= 0.2)
                {
                    inferred = InferredType.Money;
                    reasons.Add("Detected currency symbols (e.g., R$, $, €, £) in sampled values.");
                }
                else if (ContainsHint(Name, PercentageNameHints) && (zeroToOneRatio >= 0.7 || zeroToHundredRatio >= 0.7))
                {
                    inferred = InferredType.Percentage;
                    reasons.Add("Column name and value range indicate a percentage/rate pattern.");
                }
                else if (ContainsHint(Name, MoneyNameHints) && twoDecimalRatio >= 0.6)
                {
                    inferred = InferredType.Money;
                    reasons.Add("Column name and decimal precision indicate a money-like metric.");
                }
                else if (integerRatio >= TypeInferenceThreshold)
                {
                    inferred = InferredType.Integer;
                    reasons.Add("All numeric sampled values are integer-like.");
                }
                else
                {
                    inferred = InferredType.Decimal;
                    reasons.Add("Numeric values are present with decimal behavior.");
                }
            }

            if (reasons.Count == 0)
            {
                reasons.Add($"Inferred as {inferred} based on sampled values and cardinality.");
            }

            confidence = Math.Clamp(numberRatio, 0.2, 1.0);
            if (inferred == InferredType.Percentage && (percentRatio >= 0.3 || ContainsHint(Name, PercentageNameHints)))
            {
                confidence = Math.Max(confidence, 0.92);
            }
            else if (inferred == InferredType.Money && (currencyRatio >= 0.2 || ContainsHint(Name, MoneyNameHints)))
            {
                confidence = Math.Max(confidence, 0.9);
            }
            else if (inferred == InferredType.Integer && integerRatio >= TypeInferenceThreshold)
            {
                confidence = Math.Max(confidence, 0.9);
            }
            else if (inferred == InferredType.Decimal)
            {
                confidence = Math.Max(confidence, 0.8);
            }

            return inferred;
        }

        private static bool IsBooleanValue(string value)
        {
            var normalized = value.ToLowerInvariant();
            return normalized is "true" or "false" or "yes" or "no" or "1" or "0"
                or "t" or "f" or "y" or "n" or "sim" or "não" or "nao";
        }

        private static bool IsDateValue(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                   || DateTime.TryParseExact(
                       value,
                       new[]
                       {
                           "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd",
                           "dd-MM-yyyy", "MM-dd-yyyy", "yyyyMMdd"
                       },
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out _);
        }

        private static bool IsNumericValue(string value)
        {
            var cleaned = NormalizeNumericToken(value);
            return decimal.TryParse(cleaned,
                NumberStyles.Number | NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _);
        }

        private static bool IsNumericValue(string value, out double numericValue)
        {
            var cleaned = NormalizeNumericToken(value);
            if (decimal.TryParse(cleaned,
                NumberStyles.Number | NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var decimalValue))
            {
                numericValue = (double)decimalValue;
                return true;
            }

            numericValue = 0;
            return false;
        }

        private static string NormalizeNumericToken(string value)
        {
            var cleaned = value
                .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("$", string.Empty)
                .Replace("€", string.Empty)
                .Replace("£", string.Empty)
                .Replace("%", string.Empty)
                .Trim();

            if (cleaned.Contains(',') && cleaned.Contains('.'))
            {
                cleaned = cleaned.Replace(",", string.Empty);
            }
            else if (cleaned.Contains(',') && !cleaned.Contains('.'))
            {
                cleaned = cleaned.Replace(',', '.');
            }

            return cleaned.Replace(" ", string.Empty);
        }

        private static bool IsIntegerLike(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.0000001;
        }

        private static bool HasTwoDecimalPlaces(double value)
        {
            var rounded2 = Math.Round(value, 2);
            return Math.Abs(value - rounded2) < 0.0000001;
        }

        private static bool ContainsCurrencySymbol(string value)
        {
            return value.Contains("R$", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("$")
                   || value.Contains("€")
                   || value.Contains("£");
        }

        private static bool ContainsPercentSign(string value)
        {
            return value.Contains('%');
        }

        private static bool ContainsHint(string columnName, IEnumerable<string> hints)
        {
            var normalized = columnName.Trim().ToLowerInvariant();
            return hints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase));
        }
    }
}

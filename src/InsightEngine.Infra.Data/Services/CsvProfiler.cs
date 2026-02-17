using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class CsvProfiler : ICsvProfiler
{
    private const double TypeInferenceThreshold = 0.9; // 90%
    private const int MaxDistinctTracking = 10000;
    private const int MaxFrequencyTracking = 1000;

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

    private sealed class ColumnStats
    {
        public string Name { get; }

        private int _nullCount;
        private int _numberOk;
        private int _dateOk;
        private int _boolOk;
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
                _min = _min.HasValue ? Math.Min(_min.Value, numericValue) : numericValue;
                _max = _max.HasValue ? Math.Max(_max.Value, numericValue) : numericValue;
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
                Min = _min,
                Max = _max
            };
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
                return InferredType.Number;
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
            var cleaned = value.Replace(",", string.Empty).Replace(" ", string.Empty);
            return decimal.TryParse(cleaned,
                NumberStyles.Number | NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _);
        }

        private static bool IsNumericValue(string value, out double numericValue)
        {
            var cleaned = value.Replace(",", string.Empty).Replace(" ", string.Empty);
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
    }
}

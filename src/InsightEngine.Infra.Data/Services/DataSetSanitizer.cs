using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InsightEngine.Domain.Interfaces;

namespace InsightEngine.Infra.Data.Services;

public class DataSetSanitizer : IDataSetSanitizer
{
    public async Task<long> RewriteWithoutColumnsAsync(
        string csvPath,
        IReadOnlyCollection<string> ignoredColumns,
        CancellationToken cancellationToken = default)
    {
        if (ignoredColumns.Count == 0)
        {
            return new FileInfo(csvPath).Length;
        }

        var tempPath = $"{csvPath}.clean.tmp";
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        {
            using var reader = new StreamReader(csvPath);
            using var csvReader = new CsvReader(reader, config);

            await csvReader.ReadAsync();
            csvReader.ReadHeader();
            var headers = csvReader.HeaderRecord ?? throw new InvalidOperationException("CSV file has no header");

            var includedIndexes = headers
                .Select((name, index) => new { name, index })
                .Where(item => !ignoredColumns.Contains(item.name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            await using var writer = new StreamWriter(tempPath);
            await using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);

            foreach (var item in includedIndexes)
            {
                csvWriter.WriteField(item.name);
            }

            await csvWriter.NextRecordAsync();

            while (await csvReader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var item in includedIndexes)
                {
                    var value = csvReader.GetField(item.index) ?? string.Empty;
                    csvWriter.WriteField(value);
                }

                await csvWriter.NextRecordAsync();
            }

            await writer.FlushAsync(cancellationToken);
        }

        if (File.Exists(csvPath))
        {
            File.Delete(csvPath);
        }

        File.Move(tempPath, csvPath);
        return new FileInfo(csvPath).Length;
    }

    public async Task<long> AddSequentialKeyColumnAsync(
        string csvPath,
        string keyColumnName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyColumnName))
        {
            throw new ArgumentException("Key column name is required.", nameof(keyColumnName));
        }

        var tempPath = $"{csvPath}.key.tmp";
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        {
            using var reader = new StreamReader(csvPath);
            using var csvReader = new CsvReader(reader, config);

            await csvReader.ReadAsync();
            csvReader.ReadHeader();
            var headers = csvReader.HeaderRecord ?? throw new InvalidOperationException("CSV file has no header");

            if (headers.Any(header => string.Equals(header, keyColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                return new FileInfo(csvPath).Length;
            }

            await using var writer = new StreamWriter(tempPath);
            await using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);

            csvWriter.WriteField(keyColumnName);
            foreach (var header in headers)
            {
                csvWriter.WriteField(header);
            }

            await csvWriter.NextRecordAsync();

            long sequence = 1;
            while (await csvReader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                csvWriter.WriteField(sequence);
                for (var index = 0; index < headers.Length; index++)
                {
                    csvWriter.WriteField(csvReader.GetField(index) ?? string.Empty);
                }

                await csvWriter.NextRecordAsync();
                sequence++;
            }

            await writer.FlushAsync(cancellationToken);
        }

        if (File.Exists(csvPath))
        {
            File.Delete(csvPath);
        }

        File.Move(tempPath, csvPath);
        return new FileInfo(csvPath).Length;
    }
}

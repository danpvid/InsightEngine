using CsvHelper;
using CsvHelper.Configuration;
using InsightEngine.DataGenerator.Models;
using System.Globalization;

namespace InsightEngine.DataGenerator.Generators;

public class CsvGenerator
{
    private readonly Random _random = new();

    public async Task GenerateAsync(DatasetTemplate template, string outputPath)
    {
        Console.WriteLine($"Generating dataset: {template.Name}");
        Console.WriteLine($"Rows: {template.RowCount:N0}, Columns: {template.Columns.Count}");
        
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        await using var writer = new StreamWriter(outputPath);
        await using var csv = new CsvWriter(writer, config);

        // Write headers
        foreach (var column in template.Columns)
        {
            csv.WriteField(column.Name);
        }
        await csv.NextRecordAsync();

        // Write data rows
        for (int i = 0; i < template.RowCount; i++)
        {
            foreach (var column in template.Columns)
            {
                var value = GenerateValue(column, i);
                csv.WriteField(value);
            }
            await csv.NextRecordAsync();

            // Progress feedback
            if ((i + 1) % 1000 == 0)
            {
                Console.WriteLine($"  Generated {i + 1:N0} / {template.RowCount:N0} rows");
            }
        }

        Console.WriteLine($"✅ Dataset saved to: {outputPath}");
        Console.WriteLine();
    }

    private string? GenerateValue(ColumnDefinition column, int rowIndex)
    {
        // Apply null rate
        if (_random.NextDouble() < column.NullRate)
        {
            return string.Empty;
        }

        return column.Type switch
        {
            ColumnType.Number => GenerateNumber(column),
            ColumnType.Date => GenerateDate(column),
            ColumnType.Boolean => GenerateBoolean(),
            ColumnType.Category => GenerateCategory(column),
            ColumnType.String => GenerateString(column, rowIndex),
            _ => string.Empty
        };
    }

    private string GenerateNumber(ColumnDefinition column)
    {
        var (min, max) = column.NumberRange ?? (0, 10000);
        var value = (decimal)(_random.NextDouble() * (double)(max - min)) + min;
        
        // Sometimes add thousand separator for testing
        if (_random.NextDouble() < 0.3)
        {
            return value.ToString("N2", CultureInfo.InvariantCulture);
        }
        
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private string GenerateDate(ColumnDefinition column)
    {
        var (min, max) = column.DateRange ?? (DateTime.Now.AddYears(-2), DateTime.Now);
        var range = (max - min).TotalDays;
        var randomDays = _random.Next(0, (int)range);
        var date = min.AddDays(randomDays);

        // Vary date formats for testing
        var format = _random.Next(0, 5);
        return format switch
        {
            0 => date.ToString("yyyy-MM-dd"),      // ISO
            1 => date.ToString("dd/MM/yyyy"),      // BR
            2 => date.ToString("MM/dd/yyyy"),      // US
            3 => date.ToString("yyyyMMdd"),        // Compact
            _ => date.ToString("yyyy/MM/dd")       // Alternative
        };
    }

    private string GenerateBoolean()
    {
        var format = _random.Next(0, 8);
        var value = _random.Next(0, 2) == 1;

        return format switch
        {
            0 => value.ToString().ToLower(),       // true/false
            1 => value ? "yes" : "no",             // yes/no
            2 => value ? "1" : "0",                // 1/0
            3 => value ? "Y" : "N",                // Y/N
            4 => value ? "T" : "F",                // T/F
            5 => value ? "sim" : "não",            // sim/não
            6 => value ? "True" : "False",         // True/False (capitalized)
            _ => value ? "YES" : "NO"              // YES/NO
        };
    }

    private string GenerateCategory(ColumnDefinition column)
    {
        if (column.PossibleValues == null || column.PossibleValues.Count == 0)
        {
            throw new InvalidOperationException($"Category column '{column.Name}' must have PossibleValues defined");
        }

        var index = _random.Next(0, column.PossibleValues.Count);
        return column.PossibleValues[index];
    }

    private string GenerateString(ColumnDefinition column, int rowIndex)
    {
        // If possible values are provided, use them with high variance
        if (column.PossibleValues != null && column.PossibleValues.Count > 0)
        {
            // High cardinality: combine base value with variations
            var baseValue = column.PossibleValues[_random.Next(column.PossibleValues.Count)];
            var suffix = _random.Next(0, 1000);
            return $"{baseValue} {suffix}";
        }

        // Generate generic text
        var words = new[] { "Product", "Service", "Item", "Document", "Record", "Entry", "Asset", "Resource" };
        var adjectives = new[] { "Premium", "Standard", "Basic", "Advanced", "Professional", "Enterprise", "Custom" };
        
        var word = words[_random.Next(words.Length)];
        var adj = adjectives[_random.Next(adjectives.Length)];
        
        return $"{adj} {word} #{rowIndex + 1}";
    }
}

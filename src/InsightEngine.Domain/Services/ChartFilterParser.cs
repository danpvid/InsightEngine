using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Services;

public class ChartFilterParser : IChartFilterParser
{
    public Result<List<ChartFilter>> Parse(string[]? filters)
    {
        var errors = new List<string>();
        var parsed = new List<ChartFilter>();

        if (filters == null || filters.Length == 0)
        {
            return Result.Success(parsed);
        }

        if (filters.Length > 3)
        {
            errors.Add("No more than 3 filters are allowed.");
        }

        foreach (var raw in filters.Take(3))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var parts = raw.Split('|', StringSplitOptions.None);
            if (parts.Length < 3)
            {
                errors.Add($"Invalid filter format '{raw}'. Use column|operator|value.");
                continue;
            }

            var column = parts[0].Trim();
            var opRaw = parts[1].Trim();
            var logicalOperator = FilterLogicalOperator.And;
            var valueParts = parts.Skip(2).ToList();
            if (valueParts.Count > 1 &&
                TryParseFilterLogicalOperator(valueParts[^1], out var parsedLogicalOperator))
            {
                logicalOperator = parsedLogicalOperator;
                valueParts.RemoveAt(valueParts.Count - 1);
            }

            var valueRaw = string.Join("|", valueParts).Trim();

            if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(opRaw))
            {
                errors.Add($"Invalid filter '{raw}'. Column and operator are required.");
                continue;
            }

            if (!TryParseFilterOperator(opRaw, out var op))
            {
                errors.Add($"Invalid filter operator '{opRaw}'.");
                continue;
            }

            var values = valueRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .ToList();

            if (values.Count == 0)
            {
                errors.Add($"Filter '{raw}' must include at least one value.");
                continue;
            }

            if (op == FilterOperator.Between)
            {
                if (values.Count < 2 || values.Count % 2 != 0)
                {
                    errors.Add($"Filter '{raw}' must provide an even number of values (2, 4, 6...) for 'between'.");
                    continue;
                }
            }

            if ((op == FilterOperator.Gt ||
                 op == FilterOperator.Gte ||
                 op == FilterOperator.Lt ||
                 op == FilterOperator.Lte) && values.Count != 1)
            {
                errors.Add($"Filter '{raw}' must provide a single value for '{opRaw}'.");
                continue;
            }

            if (op == FilterOperator.Contains && values.Count != 1)
            {
                errors.Add($"Filter '{raw}' must provide a single value for 'contains'.");
                continue;
            }

            if ((op == FilterOperator.Eq || op == FilterOperator.NotEq) && values.Count != 1)
            {
                errors.Add($"Filter '{raw}' must provide a single value for '{opRaw}'.");
                continue;
            }

            parsed.Add(new ChartFilter
            {
                Column = column,
                Operator = op,
                Values = values,
                LogicalOperator = logicalOperator
            });
        }

        return errors.Count > 0
            ? Result.Failure<List<ChartFilter>>(errors)
            : Result.Success(parsed);
    }

    private static bool TryParseFilterOperator(string input, out FilterOperator op)
    {
        var normalized = input?.Trim() ?? string.Empty;

        normalized = normalized switch
        {
            "==" => "Eq",
            "!=" => "NotEq",
            ">" => "Gt",
            ">=" => "Gte",
            "<" => "Lt",
            "<=" => "Lte",
            "Ln" => "In",
            _ => normalized
        };

        return Enum.TryParse(normalized, true, out op);
    }

    private static bool TryParseFilterLogicalOperator(string input, out FilterLogicalOperator logicalOperator)
    {
        return Enum.TryParse(input, true, out logicalOperator);
    }
}

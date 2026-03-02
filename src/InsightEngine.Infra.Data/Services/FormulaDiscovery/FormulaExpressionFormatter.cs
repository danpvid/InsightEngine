using System.Globalization;
using System.Text.RegularExpressions;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Models.FormulaDiscovery;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FormulaExpressionFormatter
{
    public string BuildPrettyFormula(string target, double intercept, IReadOnlyCollection<Term> terms)
    {
        var normalizedTarget = NormalizeIdentifier(target);
        var orderedTerms = terms
            .Where(term => !ColumnRoleHeuristics.IsRowIdLike(term.FeatureName))
            .Where(term => Math.Abs(term.Coefficient) > 1e-12d)
            .OrderByDescending(term => Math.Abs(term.Coefficient))
            .ThenBy(term => term.FeatureName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string> { FormatNumber(intercept) };
        foreach (var term in orderedTerms)
        {
            var sign = term.Coefficient >= 0d ? "+" : "-";
            var absCoefficient = Math.Abs(term.Coefficient);
            var feature = NormalizeFeatureExpression(term.FeatureName);
            if (string.IsNullOrWhiteSpace(feature))
            {
                continue;
            }

            if (Math.Abs(absCoefficient - 1d) <= 1e-12d)
            {
                lines.Add($"{sign} {feature}");
                continue;
            }

            lines.Add($"{sign} {FormatNumber(absCoefficient)}*{feature}");
        }

        return $"{normalizedTarget} ≈ {string.Join(Environment.NewLine, lines)}";
    }

    private static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Trim();
    }

    private static string NormalizeFeatureExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = Regex.Replace(normalized, @"(?<![\d.])1\*", string.Empty);
        normalized = Regex.Replace(normalized, @"\*1(?![\d.])", string.Empty);
        normalized = Regex.Replace(normalized, @"\s+", string.Empty);
        return normalized;
    }

    private static string FormatNumber(double value)
    {
        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) < 1e-12d)
        {
            return "0";
        }

        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }
}

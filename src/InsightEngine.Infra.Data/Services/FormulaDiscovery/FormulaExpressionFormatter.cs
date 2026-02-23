using InsightEngine.Domain.Models.FormulaDiscovery;
using System.Globalization;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FormulaExpressionFormatter
{
    public string BuildPrettyFormula(string target, double intercept, IReadOnlyCollection<Term> terms)
    {
        var normalizedTarget = NormalizeIdentifier(target);
        var orderedTerms = terms
            .OrderByDescending(term => Math.Abs(term.Coefficient))
            .ThenBy(term => term.FeatureName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parts = new List<string>
        {
            FormatNumber(intercept)
        };

        foreach (var term in orderedTerms)
        {
            var sign = term.Coefficient >= 0d ? "+" : "-";
            var coefficient = FormatNumber(Math.Abs(term.Coefficient));
            var feature = NormalizeIdentifier(term.FeatureName);
            parts.Add($"{sign} {coefficient}*{feature}");
        }

        return $"{normalizedTarget} ≈ {string.Join(" ", parts)}";
    }

    private static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Trim();
    }

    private static string FormatNumber(double value)
    {
        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) < 1e-12)
        {
            return "0";
        }

        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
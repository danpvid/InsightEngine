namespace InsightEngine.Domain.Enums;

public static class InferredTypeExtensions
{
    public static InferredType NormalizeLegacy(this InferredType inferredType)
    {
        return inferredType == InferredType.Number
            ? InferredType.Decimal
            : inferredType;
    }

    public static bool IsNumericLike(this InferredType inferredType)
    {
        var normalized = inferredType.NormalizeLegacy();
        return normalized == InferredType.Integer
            || normalized == InferredType.Decimal
            || normalized == InferredType.Percentage
            || normalized == InferredType.Money;
    }
}

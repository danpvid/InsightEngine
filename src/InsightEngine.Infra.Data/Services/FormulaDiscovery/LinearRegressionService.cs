using MathNet.Numerics.LinearAlgebra;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class LinearRegressionService
{
    public LinearRegressionResult FitRidge(
        double[][] x,
        double[] y,
        double lambda = 1e-6d,
        int maxRetries = 1)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
        {
            throw new ArgumentException("X and y must be non-empty and have the same number of rows.");
        }

        var featureCount = x[0].Length;
        if (featureCount == 0)
        {
            throw new ArgumentException("X must contain at least one feature.");
        }

        var currentLambda = Math.Max(lambda, 1e-10d);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= Math.Max(0, maxRetries); attempt++)
        {
            try
            {
                var result = FitInternal(x, y, currentLambda);
                if (HasExplodedCoefficients(result.Coefficients))
                {
                    throw new InvalidOperationException("Regression coefficients became numerically unstable.");
                }

                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
                currentLambda *= 1000d;
            }
        }

        throw new InvalidOperationException("Failed to fit ridge regression after retries.", lastException);
    }

    private static LinearRegressionResult FitInternal(double[][] x, double[] y, double lambda)
    {
        var rowCount = x.Length;
        var featureCount = x[0].Length;
        var designColumns = featureCount + 1;

        var design = Matrix<double>.Build.Dense(rowCount, designColumns);
        for (var row = 0; row < rowCount; row++)
        {
            design[row, 0] = 1d;
            for (var col = 0; col < featureCount; col++)
            {
                var value = x[row][col];
                design[row, col + 1] = double.IsFinite(value) ? value : 0d;
            }
        }

        var yVector = Vector<double>.Build.DenseOfArray(y.Select(value => double.IsFinite(value) ? value : 0d).ToArray());
        var xt = design.Transpose();
        var xtx = xt * design;

        // Apply ridge stabilization only to feature terms; keep intercept unconstrained.
        for (var diag = 1; diag < designColumns; diag++)
        {
            xtx[diag, diag] += lambda;
        }

        var xty = xt * yVector;
        var beta = xtx.Solve(xty);
        var predictionsVector = design * beta;
        var predictions = predictionsVector.ToArray();
        var residuals = y.Select((actual, index) => actual - predictions[index]).ToArray();

        return new LinearRegressionResult
        {
            Intercept = beta[0],
            Coefficients = beta.Skip(1).ToArray(),
            Predictions = predictions,
            Residuals = residuals,
            Metrics = BuildMetrics(y, predictions, residuals),
            RidgeLambdaUsed = lambda
        };
    }

    private static RegressionMetrics BuildMetrics(double[] actual, double[] predicted, double[] residuals)
    {
        var n = actual.Length;
        if (n == 0)
        {
            return new RegressionMetrics
            {
                SampleSize = 0,
                R2 = 0d,
                Mae = 0d,
                Rmse = 0d,
                ResidualP95Abs = 0d,
                ResidualMeanAbs = 0d
            };
        }

        var absResiduals = residuals.Select(value => Math.Abs(value)).ToArray();
        var meanActual = actual.Average();
        var ssTot = actual.Select(value => (value - meanActual) * (value - meanActual)).Sum();
        var ssRes = residuals.Select(value => value * value).Sum();
        var r2 = ssTot <= 1e-12d ? 0d : 1d - (ssRes / ssTot);

        return new RegressionMetrics
        {
            SampleSize = n,
            R2 = double.IsFinite(r2) ? r2 : 0d,
            Mae = absResiduals.Average(),
            Rmse = Math.Sqrt(ssRes / n),
            ResidualP95Abs = Percentile(absResiduals, 0.95d),
            ResidualMeanAbs = absResiduals.Average()
        };
    }

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 0d;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var clamped = Math.Clamp(percentile, 0d, 1d);
        var position = (ordered.Length - 1) * clamped;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return ordered[lowerIndex];
        }

        var weight = position - lowerIndex;
        return ordered[lowerIndex] + ((ordered[upperIndex] - ordered[lowerIndex]) * weight);
    }

    private static bool HasExplodedCoefficients(double[] coefficients)
    {
        if (coefficients.Length == 0)
        {
            return false;
        }

        return coefficients.Any(value => !double.IsFinite(value) || Math.Abs(value) > 1e9d);
    }
}

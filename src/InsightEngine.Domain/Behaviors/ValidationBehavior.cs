using FluentValidation;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace InsightEngine.Domain.Behaviors;

/// <summary>
/// Pipeline behavior that validates commands/queries using FluentValidation,
/// integrates with DomainNotification system, and provides structured logging
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly IDomainNotificationHandler _notificationHandler;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        IDomainNotificationHandler notificationHandler,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _notificationHandler = notificationHandler;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Validating {RequestName} with {ValidatorCount} validators",
            requestName,
            _validators.Count());

        if (!_validators.Any())
        {
            _logger.LogDebug(
                "No validators registered for {RequestName}, proceeding to handler",
                requestName);
            
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        stopwatch.Stop();

        if (failures.Any())
        {
            _logger.LogWarning(
                "Validation failed for {RequestName} with {ErrorCount} error(s) in {ElapsedMs}ms: {Errors}",
                requestName,
                failures.Count,
                stopwatch.ElapsedMilliseconds,
                string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

            // Add failures to DomainNotification
            foreach (var failure in failures)
            {
                _notificationHandler.AddNotification(failure.PropertyName, failure.ErrorMessage);
            }

            // Return failure result
            var errors = failures.Select(f => f.ErrorMessage).ToList();
            
            // Criar Result<T> failure usando reflexão para obter o tipo genérico
            var responseType = typeof(TResponse);
            if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
            {
                var dataType = responseType.GetGenericArguments()[0];
                // Obter o método genérico Failure<T>(List<string>) especificamente
                var failureMethod = typeof(Result)
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => 
                        m.Name == nameof(Result.Failure) && 
                        m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(List<string>))
                    ?.MakeGenericMethod(dataType);
                
                var result = failureMethod?.Invoke(null, new object[] { errors });
                return (TResponse)result!;
            }
            
            return (TResponse)(object)Result.Failure(errors);
        }

        _logger.LogDebug(
            "Validation succeeded for {RequestName} in {ElapsedMs}ms",
            requestName,
            stopwatch.ElapsedMilliseconds);

        return await next();
    }
}

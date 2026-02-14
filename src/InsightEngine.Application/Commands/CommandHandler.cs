using FluentValidation.Results;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using MediatR;

namespace InsightEngine.Application.Commands;

public abstract class CommandHandler
{
    private readonly IDomainNotificationHandler _notificationHandler;
    private readonly IUnitOfWork _unitOfWork;

    protected CommandHandler(IDomainNotificationHandler notificationHandler, IUnitOfWork unitOfWork)
    {
        _notificationHandler = notificationHandler;
        _unitOfWork = unitOfWork;
    }

    protected void NotifyValidationErrors(ValidationResult validationResult)
    {
        foreach (var error in validationResult.Errors)
        {
            _notificationHandler.AddNotification(error.PropertyName, error.ErrorMessage);
        }
    }

    protected void NotifyError(string key, string message)
    {
        _notificationHandler.AddNotification(key, message);
    }

    protected async Task<bool> CommitAsync()
    {
        if (_notificationHandler.HasNotifications())
        {
            return false;
        }

        var success = await _unitOfWork.CommitAsync();
        
        if (!success)
        {
            NotifyError("Commit", "Erro ao salvar os dados no banco de dados");
            return false;
        }

        return true;
    }
}

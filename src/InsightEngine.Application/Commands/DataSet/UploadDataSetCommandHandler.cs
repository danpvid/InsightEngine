using InsightEngine.Application.Commands;
using InsightEngine.Application.Commands.DataSet;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using MediatR;

namespace InsightEngine.Application.Commands.DataSet;

public class UploadDataSetCommandHandler : CommandHandler, IRequestHandler<UploadDataSetCommand, bool>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IFileStorageService _fileStorageService;

    public UploadDataSetCommandHandler(
        IDomainNotificationHandler notificationHandler,
        IUnitOfWork unitOfWork,
        IDataSetRepository dataSetRepository,
        IFileStorageService fileStorageService) : base(notificationHandler, unitOfWork)
    {
        _dataSetRepository = dataSetRepository;
        _fileStorageService = fileStorageService;
    }

    public async Task<bool> Handle(UploadDataSetCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsValid())
        {
            NotifyError("UploadDataSet", "Arquivo inválido. Apenas arquivos CSV são permitidos.");
            return false;
        }

        try
        {
            // Gera um novo GUID para o dataset
            var dataSetId = Guid.NewGuid();
            var storedFileName = $"{dataSetId}.csv";

            // Salva o arquivo usando streaming (não carrega na memória)
            var (storedPath, fileSize) = await _fileStorageService.SaveFileAsync(
                request.FileStream,
                storedFileName,
                cancellationToken);

            // Cria a entidade DataSet
            var dataSet = new Domain.Entities.DataSet(
                originalFileName: request.FileName,
                storedFileName: storedFileName,
                storedPath: storedPath,
                fileSizeInBytes: fileSize,
                contentType: request.ContentType
            );

            // Salva no banco de dados
            await _dataSetRepository.AddAsync(dataSet);

            // Commit da transação
            var success = await CommitAsync();

            if (!success)
            {
                // Se falhar o commit, tenta deletar o arquivo
                await _fileStorageService.DeleteFileAsync(storedFileName);
            }

            return success;
        }
        catch (Exception ex)
        {
            NotifyError("UploadDataSet", $"Erro ao fazer upload do arquivo: {ex.Message}");
            return false;
        }
    }
}

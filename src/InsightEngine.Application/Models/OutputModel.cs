namespace InsightEngine.Application.Models;

public abstract class OutputModel
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();

    protected OutputModel()
    {
        Success = true;
    }

    protected OutputModel(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public void AddError(string error)
    {
        Success = false;
        Errors.Add(error);
    }

    public void AddErrors(IEnumerable<string> errors)
    {
        Success = false;
        Errors.AddRange(errors);
    }
}

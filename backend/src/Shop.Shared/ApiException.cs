namespace Shop.Shared;

public sealed class ApiException(int status, string code, string? detail = null,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail ?? code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;
}

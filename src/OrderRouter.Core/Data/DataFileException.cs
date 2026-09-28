namespace OrderRouter.Core.Data;

/// <summary>A file-level problem that makes a reference data file unusable. Stops startup.</summary>
public sealed class DataFileException(string file, string message)
    : Exception($"{file}: {message}")
{
    public string File { get; } = file;
}

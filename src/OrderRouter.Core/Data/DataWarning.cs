namespace OrderRouter.Core.Data;

/// <summary>A non-fatal problem found while loading reference data.</summary>
public sealed record DataWarning(string File, int Line, string Message)
{
    public override string ToString() => $"{File} line {Line}: {Message}";
}

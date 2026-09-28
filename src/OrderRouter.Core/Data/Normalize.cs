using System.Text;

namespace OrderRouter.Core.Data;

/// <summary>Shared text normalization rules for reference data and requests.</summary>
public static class Normalize
{
    /// <summary>Lowercase ASCII letters and digits only: "Can Mail Order?" becomes "canmailorder".</summary>
    public static string HeaderKey(string? header)
    {
        var builder = new StringBuilder();
        foreach (var c in header ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Trims, collapses inner whitespace and lowercases: " CPM  Machine " becomes "cpm machine".</summary>
    public static string CategoryKey(string? category) =>
        CollapseWhitespace(category).ToLowerInvariant();

    /// <summary>Trims and uppercases: " wc-std-001 " becomes "WC-STD-001".</summary>
    public static string ProductCode(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Key used to detect repeated supplier IDs, ignoring case and surrounding whitespace.</summary>
    public static string SupplierIdKey(string? id) =>
        (id ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Trims and replaces each run of whitespace (including non-breaking spaces) with one space.</summary>
    public static string CollapseWhitespace(string? value)
    {
        var builder = new StringBuilder();
        var pendingSpace = false;

        foreach (var c in value ?? string.Empty)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }
}

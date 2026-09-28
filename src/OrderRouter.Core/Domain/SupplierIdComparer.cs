namespace OrderRouter.Core.Domain;

/// <summary>
/// Orders supplier IDs by their numeric part (the last run of digits), lowest first, then by
/// the full ID compared as text. So SUP-021 comes before SUP-0199, and SUP-002 before SUP-T002.
/// IDs with no digits come after all numbered IDs. Numbers of any length are compared without
/// overflow.
/// </summary>
public sealed class SupplierIdComparer : IComparer<string>
{
    public static SupplierIdComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var numberX = NumberPart(x);
        var numberY = NumberPart(y);

        if (numberX is null && numberY is not null) return 1;
        if (numberX is not null && numberY is null) return -1;

        if (numberX is not null && numberY is not null)
        {
            // Leading zeros are stripped, so a longer digit string is a larger number.
            var byLength = numberX.Length.CompareTo(numberY.Length);
            if (byLength != 0) return byLength;

            var byDigits = string.CompareOrdinal(numberX, numberY);
            if (byDigits != 0) return byDigits;
        }

        return string.CompareOrdinal(x, y);
    }

    /// <summary>The last run of ASCII digits with leading zeros removed ("0" if all zeros), or null.</summary>
    private static string? NumberPart(string id)
    {
        var end = id.Length - 1;
        while (end >= 0 && !char.IsAsciiDigit(id[end])) end--;
        if (end < 0) return null;

        var start = end;
        while (start > 0 && char.IsAsciiDigit(id[start - 1])) start--;

        var digits = id[start..(end + 1)].TrimStart('0');
        return digits.Length == 0 ? "0" : digits;
    }
}

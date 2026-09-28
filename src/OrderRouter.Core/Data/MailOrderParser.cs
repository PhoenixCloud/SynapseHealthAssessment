namespace OrderRouter.Core.Data;

/// <param name="CanMailOrder">The parsed flag.</param>
/// <param name="Problem">Set when the value was unrecognized and treated as "n".</param>
public sealed record MailOrderParseResult(bool CanMailOrder, string? Problem);

/// <summary>
/// Parses can_mail_order?. Accepts y/yes/true/1 and n/no/false/0 in any case. Anything else is
/// treated as "n" with a problem message, which never adds mail-order eligibility.
/// </summary>
public static class MailOrderParser
{
    private static readonly HashSet<string> Yes = new(StringComparer.OrdinalIgnoreCase) { "y", "yes", "true", "1" };
    private static readonly HashSet<string> No = new(StringComparer.OrdinalIgnoreCase) { "n", "no", "false", "0" };

    public static MailOrderParseResult Parse(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();

        if (Yes.Contains(value)) return new MailOrderParseResult(true, null);
        if (No.Contains(value)) return new MailOrderParseResult(false, null);

        var shown = value.Length == 0 ? "blank" : $"'{value}'";
        return new MailOrderParseResult(false,
            $"can_mail_order? is {shown}, expected y or n; treated as n.");
    }
}

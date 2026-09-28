using System.Globalization;
using System.Text.RegularExpressions;
using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Data;

/// <param name="Coverage">The valid ZIPs and ranges, merged.</param>
/// <param name="Problems">One message per entry that was dropped.</param>
public sealed record ZipParseResult(ZipCoverage Coverage, IReadOnlyList<string> Problems);

/// <summary>
/// Parses a service_zips value such as "10001, 10002", "10001-10100" or a mix of both.
/// Entries of 1–5 digits are read as numbers, which restores leading zeros lost by
/// spreadsheets ("2130" is 02130). Invalid entries are dropped and reported; they never
/// widen coverage.
/// </summary>
public static partial class ZipCoverageParser
{
    public static ZipParseResult Parse(string? raw)
    {
        var ranges = new List<ZipRange>();
        var problems = new List<string>();

        var text = DashWithSpaces().Replace(raw ?? string.Empty, "-");

        foreach (var token in Separators().Split(text))
        {
            if (token.Length == 0) continue;

            var single = SingleZip().Match(token);
            if (single.Success)
            {
                var zip = ToZip(single.Groups[1].Value);
                ranges.Add(new ZipRange(zip, zip));
                continue;
            }

            var range = ZipRangePattern().Match(token);
            if (!range.Success)
            {
                problems.Add($"Ignored ZIP entry '{token}': not a ZIP code (up to 5 digits) or a ZIP range.");
                continue;
            }

            var startText = range.Groups[1].Value;
            var endText = range.Groups[2].Value;

            if (endText.Length < startText.Length)
            {
                problems.Add($"Ignored ZIP entry '{token}': the end has fewer digits than the start, " +
                             "which looks like a ZIP+4 code rather than a range.");
                continue;
            }

            int start = ToZip(startText), end = ToZip(endText);
            if (start > end)
            {
                problems.Add($"Ignored ZIP range '{token}': the start is after the end.");
                continue;
            }

            ranges.Add(new ZipRange(start, end));
        }

        return new ZipParseResult(ZipCoverage.FromRanges(ranges), problems);
    }

    private static int ToZip(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    // Hyphen plus Unicode dash variants (hyphen, non-breaking hyphen, figure dash, en dash,
    // em dash, minus sign), with any surrounding whitespace.
    [GeneratedRegex(@"\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]\s*")]
    private static partial Regex DashWithSpaces();

    [GeneratedRegex(@"[,;\s]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^([0-9]{1,5})$")]
    private static partial Regex SingleZip();

    [GeneratedRegex(@"^([0-9]{1,5})-([0-9]{1,5})$")]
    private static partial Regex ZipRangePattern();
}

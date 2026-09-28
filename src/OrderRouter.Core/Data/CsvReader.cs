using System.Text;

namespace OrderRouter.Core.Data;

/// <summary>One CSV record with the line number it starts on. Fields are raw (not trimmed).</summary>
public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>
/// Minimal RFC 4180 reader: quoted fields, "" escapes, line breaks inside quotes,
/// CRLF/LF/CR line endings and a leading byte-order mark. Blank lines are skipped.
/// A quote preceded only by spaces or tabs still opens a quoted field (common in
/// hand-edited files: <c>a, "b, c"</c>); the leading spaces are discarded.
/// Any other quote inside an unquoted field is kept as a literal character.
/// </summary>
public static class CsvReader
{
    public static IReadOnlyList<CsvRow> Parse(string text, string fileName)
    {
        var rows = new List<CsvRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true; // true until the field has anything other than spaces or tabs
        var line = 1;
        var rowStartLine = 1;
        var quoteStartLine = 1;

        var start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (IsLineBreak(text, i)) line++;
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when atFieldStart:
                    field.Clear();
                    inQuotes = true;
                    atFieldStart = false;
                    quoteStartLine = line;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    atFieldStart = true;
                    break;

                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow();
                    line++;
                    rowStartLine = line;
                    break;

                default:
                    field.Append(c);
                    if (c != ' ' && c != '\t') atFieldStart = false;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new DataFileException(fileName, $"Unclosed quote starting on line {quoteStartLine}.");
        }

        if (fields.Count > 0 || field.Length > 0 || !atFieldStart)
        {
            EndRow();
        }

        return rows;

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();
            atFieldStart = true;

            var isBlankLine = fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0]);
            if (!isBlankLine) rows.Add(new CsvRow(rowStartLine, fields.ToArray()));

            fields.Clear();
        }
    }

    /// <summary>True for LF, and for a CR that isn't the first half of CRLF.</summary>
    private static bool IsLineBreak(string text, int i) =>
        text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'));
}

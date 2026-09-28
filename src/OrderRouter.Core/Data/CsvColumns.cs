namespace OrderRouter.Core.Data;

/// <summary>A logical column and the normalized header names accepted for it.</summary>
public sealed record ColumnSpec(string Field, bool Required, params string[] Aliases);

/// <summary>Maps logical fields to column positions using normalized header names.</summary>
public sealed class CsvColumns
{
    private readonly Dictionary<string, int> _indexes;

    private CsvColumns(Dictionary<string, int> indexes, int headerCount)
    {
        _indexes = indexes;
        HeaderCount = headerCount;
    }

    public int HeaderCount { get; }

    /// <summary>
    /// Resolves each spec against the header row. Unknown columns are ignored.
    /// Throws if a required column is missing or any field matches more than one column.
    /// </summary>
    public static CsvColumns Resolve(CsvRow header, string fileName, IReadOnlyList<ColumnSpec> specs)
    {
        var indexes = new Dictionary<string, int>();

        foreach (var spec in specs)
        {
            var matches = header.Fields
                .Select((name, index) => (Key: Normalize.HeaderKey(name), Index: index))
                .Where(h => spec.Aliases.Contains(h.Key))
                .ToList();

            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(m => $"'{header.Fields[m.Index].Trim()}'"));
                throw new DataFileException(fileName,
                    $"Ambiguous header: columns {names} all match field '{spec.Field}'.");
            }

            if (matches.Count == 1)
            {
                indexes[spec.Field] = matches[0].Index;
            }
            else if (spec.Required)
            {
                throw new DataFileException(fileName,
                    $"Missing required column for '{spec.Field}'. Accepted headers: {string.Join(", ", spec.Aliases)}.");
            }
        }

        return new CsvColumns(indexes, header.Fields.Count);
    }

    public bool Has(string field) => _indexes.ContainsKey(field);

    /// <summary>The trimmed value of a field, or an empty string for an optional column that is absent.</summary>
    public string Get(CsvRow row, string field) =>
        _indexes.TryGetValue(field, out var index) && index < row.Fields.Count
            ? row.Fields[index].Trim()
            : string.Empty;

    /// <summary>
    /// Null when the row has the header's column count. Extra trailing columns are allowed
    /// only if they are all blank.
    /// </summary>
    public string? CheckRowShape(CsvRow row)
    {
        if (row.Fields.Count < HeaderCount)
        {
            return $"Row skipped: expected {HeaderCount} columns but found {row.Fields.Count}.";
        }

        if (row.Fields.Skip(HeaderCount).Any(f => !string.IsNullOrWhiteSpace(f)))
        {
            return $"Row skipped: expected {HeaderCount} columns but found {row.Fields.Count} with extra values.";
        }

        return null;
    }
}

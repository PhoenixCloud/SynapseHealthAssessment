using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Data;

public sealed record SupplierParseResult(IReadOnlyList<Supplier> Suppliers, IReadOnlyList<DataWarning> Warnings);

/// <summary>Parses suppliers.csv. All rules are documented in README section 4.</summary>
public static class SupplierCsvParser
{
    public const string DefaultFileName = "suppliers.csv";

    private const string Id = "supplier_id";
    private const string Name = "supplier_name";
    private const string Zips = "service_zips";
    private const string Categories = "product_categories";
    private const string Score = "customer_satisfaction_score";
    private const string MailOrder = "can_mail_order?";

    // Normalized header names. The misspellings are the ones found in the delivered file;
    // the correct spellings are accepted too so a corrected file keeps working.
    private static readonly ColumnSpec[] Columns =
    [
        new(Id, true, "supplierid", "suplierid"),
        new(Name, true, "suppliername", "supliername"),
        new(Zips, true, "servicezips"),
        new(Categories, true, "productcategories"),
        new(Score, true, "customersatisfactionscore"),
        new(MailOrder, true, "canmailorder"),
    ];

    public static SupplierParseResult Parse(string csvText, string fileName = DefaultFileName)
    {
        var rows = CsvReader.Parse(csvText, fileName);
        if (rows.Count == 0)
        {
            throw new DataFileException(fileName, "File is empty; expected a header row.");
        }

        var columns = CsvColumns.Resolve(rows[0], fileName, Columns);
        var warnings = new List<DataWarning>();
        var parsed = new List<(Supplier Supplier, int Line)>();

        foreach (var row in rows.Skip(1))
        {
            var supplier = ParseRow(row, columns, fileName, warnings);
            if (supplier is not null) parsed.Add((supplier, row.LineNumber));
        }

        return new SupplierParseResult(RemoveDuplicates(parsed, fileName, warnings), warnings);
    }

    private static Supplier? ParseRow(CsvRow row, CsvColumns columns, string fileName, List<DataWarning> warnings)
    {
        void Warn(string message) => warnings.Add(new DataWarning(fileName, row.LineNumber, message));

        var shapeProblem = columns.CheckRowShape(row);
        if (shapeProblem is not null)
        {
            Warn(shapeProblem);
            return null;
        }

        var id = columns.Get(row, Id);
        if (id.Length == 0)
        {
            Warn("Row skipped: blank supplier_id.");
            return null;
        }

        var name = columns.Get(row, Name);
        if (name.Length == 0)
        {
            Warn($"Row skipped: supplier '{id}' has a blank supplier_name.");
            return null;
        }

        var categories = columns.Get(row, Categories)
            .Split(',')
            .Select(Normalize.CategoryKey)
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (categories.Count == 0)
        {
            Warn($"Row skipped: supplier '{id}' has no product categories.");
            return null;
        }

        var zips = ZipCoverageParser.Parse(columns.Get(row, Zips));
        foreach (var problem in zips.Problems) Warn($"Supplier '{id}': {problem}");
        if (zips.Coverage.IsEmpty)
        {
            Warn($"Supplier '{id}' has no valid service ZIPs; it can only be used for mail order.");
        }

        var rating = RatingParser.Parse(columns.Get(row, Score));
        if (rating.Problem is not null) Warn($"Supplier '{id}': {rating.Problem}");

        var mailOrder = MailOrderParser.Parse(columns.Get(row, MailOrder));
        if (mailOrder.Problem is not null) Warn($"Supplier '{id}': {mailOrder.Problem}");

        return new Supplier(id, name, zips.Coverage, categories, rating.Rating, mailOrder.CanMailOrder);
    }

    /// <summary>
    /// Identical repeats (same ID ignoring case) keep the first row. Repeats with different
    /// data drop every row for that ID, since it isn't safe to guess which is right.
    /// </summary>
    private static List<Supplier> RemoveDuplicates(
        List<(Supplier Supplier, int Line)> parsed, string fileName, List<DataWarning> warnings)
    {
        var result = new List<Supplier>();

        foreach (var group in parsed.GroupBy(p => Normalize.SupplierIdKey(p.Supplier.Id)))
        {
            var entries = group.ToList();
            var first = entries[0];

            if (entries.Count == 1)
            {
                result.Add(first.Supplier);
                continue;
            }

            if (entries.Skip(1).All(e => HaveSameData(e.Supplier, first.Supplier)))
            {
                result.Add(first.Supplier);
                foreach (var repeat in entries.Skip(1))
                {
                    warnings.Add(new DataWarning(fileName, repeat.Line,
                        $"Duplicate of supplier '{first.Supplier.Id}' on line {first.Line}; ignored."));
                }

                continue;
            }

            var lines = string.Join(", ", entries.Select(e => e.Line));
            foreach (var entry in entries)
            {
                warnings.Add(new DataWarning(fileName, entry.Line,
                    $"supplier_id '{entry.Supplier.Id}' appears on lines {lines} with different data; " +
                    "all of these rows were dropped."));
            }
        }

        return result;
    }

    private static bool HaveSameData(Supplier a, Supplier b) =>
        a.Name == b.Name
        && a.ServiceArea.Equals(b.ServiceArea)
        && a.CategoryKeys.SetEquals(b.CategoryKeys)
        && a.Rating == b.Rating
        && a.CanMailOrder == b.CanMailOrder;
}

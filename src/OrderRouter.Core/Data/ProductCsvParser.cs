using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Data;

public sealed record ProductParseResult(IReadOnlyList<Product> Products, IReadOnlyList<DataWarning> Warnings);

/// <summary>Parses products.csv. All rules are documented in README section 4.</summary>
public static class ProductCsvParser
{
    public const string DefaultFileName = "products.csv";

    private const string Code = "product_code";
    private const string Name = "product_name";
    private const string Category = "category";

    private static readonly ColumnSpec[] Columns =
    [
        new(Code, true, "productcode"),
        new(Name, false, "productname"),
        new(Category, true, "category"),
    ];

    public static ProductParseResult Parse(string csvText, string fileName = DefaultFileName)
    {
        var rows = CsvReader.Parse(csvText, fileName);
        if (rows.Count == 0)
        {
            throw new DataFileException(fileName, "File is empty; expected a header row.");
        }

        var columns = CsvColumns.Resolve(rows[0], fileName, Columns);
        var warnings = new List<DataWarning>();
        var parsed = new List<(Product Product, int Line)>();

        foreach (var row in rows.Skip(1))
        {
            var product = ParseRow(row, columns, fileName, warnings);
            if (product is not null) parsed.Add((product, row.LineNumber));
        }

        return new ProductParseResult(RemoveDuplicates(parsed, fileName, warnings), warnings);
    }

    private static Product? ParseRow(CsvRow row, CsvColumns columns, string fileName, List<DataWarning> warnings)
    {
        void Warn(string message) => warnings.Add(new DataWarning(fileName, row.LineNumber, message));

        var shapeProblem = columns.CheckRowShape(row);
        if (shapeProblem is not null)
        {
            Warn(shapeProblem);
            return null;
        }

        var code = Normalize.ProductCode(columns.Get(row, Code));
        if (code.Length == 0)
        {
            Warn("Row skipped: blank product_code.");
            return null;
        }

        var category = Normalize.CollapseWhitespace(columns.Get(row, Category));
        if (category.Length == 0)
        {
            Warn($"Row skipped: product '{code}' has a blank category.");
            return null;
        }

        return new Product(code, columns.Get(row, Name), category, Normalize.CategoryKey(category));
    }

    /// <summary>
    /// Repeats with the same category keep the first row (the name doesn't affect routing).
    /// Repeats with a different category drop every row for that code: an unsafe mismatch.
    /// </summary>
    private static List<Product> RemoveDuplicates(
        List<(Product Product, int Line)> parsed, string fileName, List<DataWarning> warnings)
    {
        var result = new List<Product>();

        foreach (var group in parsed.GroupBy(p => p.Product.Code))
        {
            var entries = group.ToList();
            var first = entries[0];

            if (entries.Count == 1)
            {
                result.Add(first.Product);
                continue;
            }

            if (entries.Any(e => e.Product.CategoryKey != first.Product.CategoryKey))
            {
                var lines = string.Join(", ", entries.Select(e => e.Line));
                foreach (var entry in entries)
                {
                    warnings.Add(new DataWarning(fileName, entry.Line,
                        $"product_code '{entry.Product.Code}' appears on lines {lines} with different categories; " +
                        "all of these rows were dropped."));
                }

                continue;
            }

            result.Add(first.Product);
            foreach (var repeat in entries.Skip(1))
            {
                var message = repeat.Product == first.Product
                    ? $"Duplicate of product '{first.Product.Code}' on line {first.Line}; ignored."
                    : $"Product '{first.Product.Code}' repeats line {first.Line} with a different name or " +
                      $"category spelling; kept line {first.Line}.";
                warnings.Add(new DataWarning(fileName, repeat.Line, message));
            }
        }

        return result;
    }
}

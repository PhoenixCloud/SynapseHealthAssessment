using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Data;

public sealed record ReferenceDataLoadResult(ReferenceData Data, IReadOnlyList<DataWarning> Warnings);

/// <summary>Loads suppliers.csv and products.csv from a directory.</summary>
public static class ReferenceDataLoader
{
    public static ReferenceDataLoadResult Load(string directory)
    {
        var suppliers = SupplierCsvParser.Parse(
            ReadFile(directory, SupplierCsvParser.DefaultFileName), SupplierCsvParser.DefaultFileName);
        var products = ProductCsvParser.Parse(
            ReadFile(directory, ProductCsvParser.DefaultFileName), ProductCsvParser.DefaultFileName);

        var data = new ReferenceData(products.Products, suppliers.Suppliers);
        return new ReferenceDataLoadResult(data, [.. suppliers.Warnings, .. products.Warnings]);
    }

    private static string ReadFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new DataFileException(fileName, $"File not found at '{Path.GetFullPath(path)}'.");
        }

        return File.ReadAllText(path);
    }
}

using OrderRouter.Core.Data;
using OrderRouter.Core.Domain;

namespace OrderRouter.Tests.Data;

/// <summary>Loads the real, unmodified CSV files copied to the test output folder.</summary>
public class ReferenceDataLoaderTests
{
    private static readonly string DataDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, "Data");

    private ReferenceDataLoadResult _result = null!;

    [OneTimeSetUp]
    public void LoadRealData() => _result = ReferenceDataLoader.Load(DataDirectory);

    private Supplier Supplier(string id) => _result.Data.Suppliers.Single(s => s.Id == id);

    private Product Product(string code)
    {
        Assert.That(_result.Data.TryGetProduct(code, out var product), Is.True, $"Product {code} not found");
        return product;
    }

    [Test]
    public void LoadsEverySupplier() => Assert.That(_result.Data.Suppliers, Has.Count.EqualTo(1100));

    [Test]
    public void LoadsEveryProductMinusFiveDuplicates() =>
        Assert.That(_result.Data.Products, Has.Count.EqualTo(1195));

    [Test]
    public void OnlyWarningsAreTheFiveIdenticalProductDuplicates()
    {
        Assert.That(_result.Warnings, Has.Count.EqualTo(5));
        Assert.That(_result.Warnings, Has.All.Property("File").EqualTo("products.csv"));
        Assert.That(_result.Warnings, Has.All.Property("Message").StartsWith("Duplicate"));
    }

    [Test]
    public void MisspelledSupplierNameColumnIsRead() =>
        Assert.That(Supplier("SUP-005").Name, Is.EqualTo("Respiratory Care Co Co"));

    [Test]
    public void MailOrderColumnIsRead()
    {
        Assert.That(Supplier("SUP-001").CanMailOrder, Is.True);
        Assert.That(Supplier("SUP-005").CanMailOrder, Is.False);
    }

    [Test]
    public void NoRatingsYetIsUnrated()
    {
        Assert.That(Supplier("SUP-003").IsRated, Is.False);
        Assert.That(Supplier("SUP-003").EffectiveRating, Is.EqualTo(5.5m));
        Assert.That(_result.Data.Suppliers.Count(s => !s.IsRated), Is.EqualTo(184));
    }

    [Test]
    public void DecimalRatingsAreRead() => Assert.That(Supplier("SUP-0115").Rating, Is.EqualTo(5.5m));

    [Test]
    public void FourDigitZipsAreReadAsZipsWithALeadingZero()
    {
        // SUP-016: "2164-2213, 2143-2193, 2154-2171" means 02143-02213.
        Assert.That(Supplier("SUP-016").Serves(2143), Is.True);
        Assert.That(Supplier("SUP-016").Serves(2213), Is.True);
        Assert.That(Supplier("SUP-016").Serves(2142), Is.False, "02142 is just below the merged range");
        // SUP-020 lists "2131" as a single ZIP, meaning 02131.
        Assert.That(Supplier("SUP-020").Serves(2131), Is.True);
    }

    [Test]
    public void MixedRangeAndSingleZipsAreRead()
    {
        // SUP-0121: "10059-10103, 90117, 60699, 10229"
        var supplier = Supplier("SUP-0121");
        Assert.That(supplier.Serves(10080) && supplier.Serves(90117) && supplier.Serves(10229), Is.True);
        Assert.That(supplier.Serves(10104), Is.False);
    }

    [Test]
    public void EveryZipRangeExcludesTestZips()
    {
        Assert.That(Supplier("SUP-T013").Serves(40), Is.False);
        Assert.That(Supplier("SUP-T013").Serves(10015), Is.True);
    }

    [Test]
    public void TestSuppliersServeOnlyTheirOwnZip()
    {
        Assert.That(Supplier("SUP-T001").Serves(1), Is.True);
        Assert.That(Supplier("SUP-T014").Serves(98), Is.True);
        Assert.That(Supplier("SUP-T014").Serves(99), Is.False);
    }

    [Test]
    public void SupplierCategoriesInDifferentCaseMatchTheSameKey()
    {
        Assert.That(Supplier("SUP-002").Handles("cpap"), Is.True);   // listed as "CPAP"
        Assert.That(Supplier("SUP-0119").Handles("cpap"), Is.True);  // listed as "cpap"
        Assert.That(Supplier("SUP-013").Handles("cpm machine"), Is.True); // listed as "CPM machine"
    }

    [Test]
    public void ProductCategoriesAreKeptAsWrittenWithNormalizedKeys()
    {
        Assert.That(Product("CP-STD-031").Category, Is.EqualTo("CPAP"));
        Assert.That(Product("CP-STD-031").CategoryKey, Is.EqualTo("cpap"));
        Assert.That(Product("CP-BAR-493").CategoryKey, Is.EqualTo("cpap"));
    }

    [Test]
    public void ProductWithMisleadingPrefixUsesItsCategory() =>
        Assert.That(Product("CM-SENT-001").CategoryKey, Is.EqualTo("cpm machine"));

    [Test]
    public void ProductLookupIgnoresCaseAndWhitespace() =>
        Assert.That(Product(" wc-std-001 ").Code, Is.EqualTo("WC-STD-001"));

    [Test]
    public void EveryProductCategoryIsHandledBySomeSupplier()
    {
        var supplierCategories = _result.Data.Suppliers.SelectMany(s => s.CategoryKeys).ToHashSet();

        Assert.That(_result.Data.Products.Values.Select(p => p.CategoryKey).Distinct(),
            Is.SubsetOf(supplierCategories));
    }

    [Test]
    public void MissingFileThrows()
    {
        var emptyDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var ex = Assert.Throws<DataFileException>(() => ReferenceDataLoader.Load(emptyDirectory.FullName));
            Assert.That(ex!.File, Is.EqualTo("suppliers.csv"));
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }
}

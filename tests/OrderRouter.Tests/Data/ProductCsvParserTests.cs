using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class ProductCsvParserTests
{
    private const string Header = "product_code,product_name,category";

    private static ProductParseResult Parse(params string[] rows) =>
        ProductCsvParser.Parse(string.Join("\n", [Header, .. rows]));

    [Test]
    public void ParsesRowFromTheRealFileFormat()
    {
        var result = Parse("WC-STD-001,Standard Wheelchair,wheelchair");

        var product = result.Products.Single();
        Assert.Multiple(() =>
        {
            Assert.That(product.Code, Is.EqualTo("WC-STD-001"));
            Assert.That(product.Name, Is.EqualTo("Standard Wheelchair"));
            Assert.That(product.Category, Is.EqualTo("wheelchair"));
            Assert.That(product.CategoryKey, Is.EqualTo("wheelchair"));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public void KeepsCategoryAsWrittenButNormalizesTheKey()
    {
        var product = Parse("CP-STD-031,CPAP Machine,CPAP").Products.Single();

        Assert.That(product.Category, Is.EqualTo("CPAP"));
        Assert.That(product.CategoryKey, Is.EqualTo("cpap"));
    }

    [Test]
    public void CategoryComesFromTheColumnNotTheCodePrefix()
    {
        var product = Parse("CM-SENT-001,Sentinel CPM Machine,cpm machine").Products.Single();

        Assert.That(product.CategoryKey, Is.EqualTo("cpm machine"));
    }

    [Test]
    public void CodeIsTrimmedAndUppercased()
    {
        Assert.That(Parse(" wc-std-001 ,Chair,wheelchair").Products.Single().Code, Is.EqualTo("WC-STD-001"));
    }

    [Test]
    public void NameColumnIsOptional()
    {
        var product = ProductCsvParser.Parse("category,product_code\nwheelchair,WC-1").Products.Single();

        Assert.That(product.Code, Is.EqualTo("WC-1"));
        Assert.That(product.Name, Is.Empty);
    }

    [Test]
    public void BlankNameIsAllowed()
    {
        var result = Parse("WC-1,,wheelchair");

        Assert.That(result.Products, Has.Count.EqualTo(1));
        Assert.That(result.Warnings, Is.Empty);
    }

    [TestCase("category\nwheelchair", "product_code", TestName = "Missing code column")]
    [TestCase("product_code,product_name\nWC-1,Chair", "category", TestName = "Missing category column")]
    public void MissingRequiredColumnFailsTheFile(string csv, string missingField)
    {
        var ex = Assert.Throws<DataFileException>(() => ProductCsvParser.Parse(csv));

        Assert.That(ex!.Message, Does.Contain(missingField));
    }

    [TestCase(",Chair,wheelchair", "blank product_code")]
    [TestCase("WC-1,Chair,", "blank category")]
    [TestCase("WC-1,Chair", "expected 3 columns")]
    public void UnusableRowsAreSkippedWithWarning(string row, string expected)
    {
        var result = Parse("OK-1,Fine,cane", row);

        Assert.That(result.Products.Select(p => p.Code), Is.EqualTo(new[] { "OK-1" }));
        Assert.That(result.Warnings.Single().Message, Does.Contain(expected));
        Assert.That(result.Warnings.Single().Line, Is.EqualTo(3));
    }

    [Test]
    public void IdenticalDuplicateKeepsFirstWithWarning()
    {
        var result = Parse(
            "HB-MAN-019-BAS,Manual Hospital Bed - Basic,hospital bed",
            "WC-1,Chair,wheelchair",
            "HB-MAN-019-BAS,Manual Hospital Bed - Basic,hospital bed");

        Assert.That(result.Products.Select(p => p.Code), Is.EqualTo(new[] { "HB-MAN-019-BAS", "WC-1" }));
        var warning = result.Warnings.Single();
        Assert.That(warning.Line, Is.EqualTo(4));
        Assert.That(warning.Message, Does.Contain("Duplicate").And.Contain("line 2"));
    }

    [Test]
    public void DuplicateDifferingOnlyInCodeCaseIsTheSameProduct()
    {
        var result = Parse("WC-1,Chair,wheelchair", "wc-1,Chair,wheelchair");

        Assert.That(result.Products, Has.Count.EqualTo(1));
        Assert.That(result.Warnings.Single().Message, Does.Contain("Duplicate"));
    }

    [Test]
    public void DuplicateWithDifferentNameKeepsFirstWithWarning()
    {
        var result = Parse("WC-1,Chair,wheelchair", "WC-1,Chair Deluxe,wheelchair");

        Assert.That(result.Products.Single().Name, Is.EqualTo("Chair"));
        Assert.That(result.Warnings.Single().Message, Does.Contain("different name").And.Contain("kept line 2"));
    }

    [Test]
    public void DuplicateWithCategoryInDifferentCaseKeepsFirstSpelling()
    {
        var result = Parse("CP-1,Machine,CPAP", "CP-1,Machine,cpap");

        Assert.That(result.Products.Single().Category, Is.EqualTo("CPAP"));
        Assert.That(result.Warnings.Single().Message, Does.Contain("kept line 2"));
    }

    [Test]
    public void DuplicateWithDifferentCategoryDropsAllRowsForThatCode()
    {
        var result = Parse("XX-1,Mystery,commode", "OK-1,Fine,cane", "XX-1,Mystery,cpm machine");

        Assert.That(result.Products.Select(p => p.Code), Is.EqualTo(new[] { "OK-1" }));
        Assert.That(result.Warnings.Select(w => w.Line), Is.EquivalentTo(new[] { 2, 4 }));
        Assert.That(result.Warnings, Has.All.Property("Message").Contains("different categories"));
    }
}

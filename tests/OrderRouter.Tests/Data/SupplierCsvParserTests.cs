using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class SupplierCsvParserTests
{
    // The real header from suppliers.csv, including its misspellings.
    private const string Header =
        "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?";

    private static SupplierParseResult Parse(params string[] rows) =>
        SupplierCsvParser.Parse(string.Join("\n", [Header, .. rows]));

    [Test]
    public void ParsesRowFromTheRealFileFormat()
    {
        var result = Parse("SUP-005,Respiratory Care Co Co,\"10014, 10018\",\"oxygen, CPAP, hospital bed\",7,n");

        var supplier = result.Suppliers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(supplier.Id, Is.EqualTo("SUP-005"));
            Assert.That(supplier.Name, Is.EqualTo("Respiratory Care Co Co"));
            Assert.That(supplier.Serves(10014), Is.True);
            Assert.That(supplier.Serves(10015), Is.False);
            Assert.That(supplier.CategoryKeys, Is.EquivalentTo(new[] { "oxygen", "cpap", "hospital bed" }));
            Assert.That(supplier.Rating, Is.EqualTo(7m));
            Assert.That(supplier.CanMailOrder, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public void CorrectlySpelledHeadersAlsoWork()
    {
        var csv = "supplier_id,supplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order\n" +
                  "SUP-1,Acme,10001,cane,8,y";

        Assert.That(SupplierCsvParser.Parse(csv).Suppliers.Single().Name, Is.EqualTo("Acme"));
    }

    [Test]
    public void ColumnsInDifferentOrderWithExtraColumn()
    {
        var csv = "can_mail_order?,notes,supplier_id,customer_satisfaction_score,product_categories,suplier_name,service_zips\n" +
                  "y,hello,SUP-1,9,cane,Acme,10001";

        var supplier = SupplierCsvParser.Parse(csv).Suppliers.Single();
        Assert.That(supplier.Id, Is.EqualTo("SUP-1"));
        Assert.That(supplier.CanMailOrder, Is.True);
        Assert.That(supplier.Rating, Is.EqualTo(9m));
    }

    [Test]
    public void MissingColumnFailsTheFile()
    {
        var csv = "supplier_id,suplier_name,service_zips,product_categories,can_mail_order?\nSUP-1,Acme,10001,cane,y";

        var ex = Assert.Throws<DataFileException>(() => SupplierCsvParser.Parse(csv));
        Assert.That(ex!.Message, Does.Contain("customer_satisfaction_score"));
    }

    [Test]
    public void EmptyFileFails()
    {
        Assert.Throws<DataFileException>(() => SupplierCsvParser.Parse(""));
    }

    [Test]
    public void HeaderOnlyGivesNoSuppliers()
    {
        var result = Parse();

        Assert.That(result.Suppliers, Is.Empty);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void NoRatingsYetIsUnratedWithMiddleEffectiveScore()
    {
        var supplier = Parse("SUP-003,HealthEquip,77059,cane,no ratings yet,y").Suppliers.Single();

        Assert.That(supplier.IsRated, Is.False);
        Assert.That(supplier.EffectiveRating, Is.EqualTo(5.5m));
    }

    [Test]
    public void FourDigitZipsAndMixedRangesAreParsed()
    {
        var supplier = Parse("SUP-016,Premier,\"2164-2213, 2143-2193, 2130\",nebulizer,10,y").Suppliers.Single();

        Assert.Multiple(() =>
        {
            Assert.That(supplier.Serves(2130), Is.True);
            Assert.That(supplier.Serves(2143), Is.True);
            Assert.That(supplier.Serves(2213), Is.True);
            Assert.That(supplier.Serves(2214), Is.False);
        });
    }

    [Test]
    public void CategoriesAreNormalizedAndDeduplicated()
    {
        var supplier = Parse("SUP-1,Acme,10001,\" CPAP, cpap , CPM  Machine,, \",8,y").Suppliers.Single();

        Assert.That(supplier.CategoryKeys, Is.EquivalentTo(new[] { "cpap", "cpm machine" }));
    }

    [Test]
    public void ValuesAreTrimmed()
    {
        var supplier = Parse("  SUP-1  ,  Acme  , 10001 , cane , 8 , y ").Suppliers.Single();

        Assert.That(supplier.Id, Is.EqualTo("SUP-1"));
        Assert.That(supplier.Name, Is.EqualTo("Acme"));
    }

    [Test]
    public void SpaceBeforeQuotedZipListDoesNotBreakTheRow()
    {
        var result = Parse("SUP-1, Acme, \"10001, 10002\", \"cane, walker\", 8, y");

        var supplier = result.Suppliers.Single();
        Assert.That(supplier.Serves(10002), Is.True);
        Assert.That(supplier.CategoryKeys, Is.EquivalentTo(new[] { "cane", "walker" }));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void TrailingBlankColumnIsAllowed()
    {
        Assert.That(Parse("SUP-1,Acme,10001,cane,8,y,").Suppliers, Has.Count.EqualTo(1));
    }

    [TestCase("SUP-1,Acme,10001,cane,8", "expected 6 columns but found 5")]
    [TestCase("SUP-1,Acme,10001,cane,8,y,unexpected", "extra values")]
    [TestCase(",Acme,10001,cane,8,y", "blank supplier_id")]
    [TestCase("SUP-1,,10001,cane,8,y", "blank supplier_name")]
    [TestCase("SUP-1,Acme,10001,\" , \",8,y", "no product categories")]
    public void UnusableRowsAreSkippedWithWarning(string row, string expected)
    {
        var result = Parse("SUP-OK,Good,10001,cane,8,y", row);

        Assert.That(result.Suppliers.Select(s => s.Id), Is.EqualTo(new[] { "SUP-OK" }));
        var warning = result.Warnings.Single();
        Assert.That(warning.Message, Does.Contain(expected));
        Assert.That(warning.Line, Is.EqualTo(3));
        Assert.That(warning.File, Is.EqualTo("suppliers.csv"));
    }

    [Test]
    public void BadZipEntryIsDroppedButSupplierIsKept()
    {
        var result = Parse("SUP-1,Acme,\"10001, ABC, 10003\",cane,8,y");

        var supplier = result.Suppliers.Single();
        Assert.That(supplier.Serves(10001) && supplier.Serves(10003), Is.True);
        Assert.That(result.Warnings.Single().Message, Does.Contain("SUP-1").And.Contain("'ABC'"));
    }

    [Test]
    public void NoValidZipsKeepsSupplierForMailOrderWithWarning()
    {
        var result = Parse("SUP-1,Acme,,cane,8,y");

        var supplier = result.Suppliers.Single();
        Assert.That(supplier.ServiceArea.IsEmpty, Is.True);
        Assert.That(supplier.CanMailOrder, Is.True);
        Assert.That(result.Warnings.Single().Message, Does.Contain("only be used for mail order"));
    }

    [Test]
    public void BadRatingIsUnratedWithWarning()
    {
        var result = Parse("SUP-1,Acme,10001,cane,11,y");

        Assert.That(result.Suppliers.Single().Rating, Is.Null);
        Assert.That(result.Warnings.Single().Message, Does.Contain("outside"));
    }

    [Test]
    public void UnrecognizedMailOrderIsNoWithWarning()
    {
        var result = Parse("SUP-1,Acme,10001,cane,8,maybe");

        Assert.That(result.Suppliers.Single().CanMailOrder, Is.False);
        Assert.That(result.Warnings.Single().Message, Does.Contain("treated as n"));
    }

    [Test]
    public void OneRowCanProduceSeveralWarnings()
    {
        var result = Parse("SUP-1,Acme,\"ABC\",cane,N/A,maybe");

        Assert.That(result.Suppliers, Has.Count.EqualTo(1));
        Assert.That(result.Warnings, Has.Count.EqualTo(4)); // bad ZIP, no ZIPs, rating, mail order
        Assert.That(result.Warnings.Select(w => w.Line), Is.All.EqualTo(2));
    }

    [Test]
    public void SameBusinessNameWithDifferentIdsIsAllowed()
    {
        var result = Parse("SUP-1,Beacon Medical Inc,10001,cane,8,y", "SUP-2,Beacon Medical Inc,10002,cane,7,n");

        Assert.That(result.Suppliers, Has.Count.EqualTo(2));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void IdenticalDuplicateKeepsFirstWithWarning()
    {
        var result = Parse(
            "SUP-1,Acme,\"10001, 10002\",\"cane, walker\",8,y",
            "sup-1,Acme,\"10002,10001\",\"Walker,CANE\",8.0,Y");

        Assert.That(result.Suppliers.Single().Id, Is.EqualTo("SUP-1"));
        var warning = result.Warnings.Single();
        Assert.That(warning.Line, Is.EqualTo(3));
        Assert.That(warning.Message, Does.Contain("Duplicate").And.Contain("line 2"));
    }

    [TestCase("SUP-1,Acme,10001,cane,9,y", TestName = "Different rating")]
    [TestCase("SUP-1,Acme,10001,cane,8,n", TestName = "Different mail order flag")]
    [TestCase("SUP-1,Acme,10002,cane,8,y", TestName = "Different ZIPs")]
    [TestCase("SUP-1,Acme,10001,walker,8,y", TestName = "Different categories")]
    [TestCase("SUP-1,Acme Two,10001,cane,8,y", TestName = "Different name")]
    public void ConflictingDuplicateDropsAllRowsForThatId(string conflictingRow)
    {
        var result = Parse("SUP-1,Acme,10001,cane,8,y", "SUP-2,Other,10001,cane,8,y", conflictingRow);

        Assert.That(result.Suppliers.Select(s => s.Id), Is.EqualTo(new[] { "SUP-2" }));
        Assert.That(result.Warnings.Select(w => w.Line), Is.EquivalentTo(new[] { 2, 4 }));
        Assert.That(result.Warnings, Has.All.Property("Message").Contains("different data"));
    }

    [Test]
    public void PreservesFileOrder()
    {
        var result = Parse("SUP-B,B,10001,cane,8,y", "SUP-A,A,10001,cane,8,y", "SUP-C,C,10001,cane,8,y");

        Assert.That(result.Suppliers.Select(s => s.Id), Is.EqualTo(new[] { "SUP-B", "SUP-A", "SUP-C" }));
    }

    [Test]
    public void UsesProvidedFileNameInWarnings()
    {
        var result = SupplierCsvParser.Parse(Header + "\nSUP-1,,10001,cane,8,y", "custom.csv");

        Assert.That(result.Warnings.Single().File, Is.EqualTo("custom.csv"));
    }
}

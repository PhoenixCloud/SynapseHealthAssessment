using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class CsvColumnsTests
{
    private static readonly ColumnSpec[] Specs =
    [
        new("name", true, "suppliername", "supliername"),
        new("mail", true, "canmailorder"),
        new("notes", false, "notes"),
    ];

    private static CsvRow Header(params string[] names) => new(1, names);

    [Test]
    public void MatchesMisspelledHeaderFromTheDataFile()
    {
        var columns = CsvColumns.Resolve(Header("suplier_name", "can_mail_order?"), "f.csv", Specs);
        var row = new CsvRow(2, ["Acme", "y"]);

        Assert.That(columns.Get(row, "name"), Is.EqualTo("Acme"));
        Assert.That(columns.Get(row, "mail"), Is.EqualTo("y"));
    }

    [TestCase("supplier_name")]
    [TestCase("Supplier Name")]
    [TestCase(" SUPPLIER-NAME ")]
    [TestCase("SupplierName")]
    public void IgnoresCaseSpacingAndPunctuationInHeaders(string header)
    {
        var columns = CsvColumns.Resolve(Header(header, "can_mail_order"), "f.csv", Specs);

        Assert.That(columns.Has("name"), Is.True);
    }

    [Test]
    public void ColumnOrderDoesNotMatterAndUnknownColumnsAreIgnored()
    {
        var columns = CsvColumns.Resolve(Header("extra", "can_mail_order?", "suplier_name"), "f.csv", Specs);
        var row = new CsvRow(2, ["ignored", "n", "Acme"]);

        Assert.That(columns.Get(row, "name"), Is.EqualTo("Acme"));
        Assert.That(columns.Get(row, "mail"), Is.EqualTo("n"));
    }

    [Test]
    public void GetTrimsValues()
    {
        var columns = CsvColumns.Resolve(Header("suplier_name", "can_mail_order?"), "f.csv", Specs);

        Assert.That(columns.Get(new CsvRow(2, ["  Acme  ", " y "]), "name"), Is.EqualTo("Acme"));
    }

    [Test]
    public void MissingOptionalColumnReturnsEmpty()
    {
        var columns = CsvColumns.Resolve(Header("suplier_name", "can_mail_order?"), "f.csv", Specs);

        Assert.That(columns.Has("notes"), Is.False);
        Assert.That(columns.Get(new CsvRow(2, ["Acme", "y"]), "notes"), Is.Empty);
    }

    [Test]
    public void MissingRequiredColumnThrows()
    {
        var ex = Assert.Throws<DataFileException>(() =>
            CsvColumns.Resolve(Header("suplier_name"), "f.csv", Specs));

        Assert.That(ex!.Message, Does.Contain("Missing required column for 'mail'"));
    }

    [Test]
    public void TwoColumnsMatchingOneFieldThrowsAsAmbiguous()
    {
        var ex = Assert.Throws<DataFileException>(() =>
            CsvColumns.Resolve(Header("supplier_name", "suplier_name", "can_mail_order?"), "f.csv", Specs));

        Assert.That(ex!.Message, Does.Contain("Ambiguous header"));
    }

    [Test]
    public void RowShapeAcceptsExactColumnCountAndBlankExtras()
    {
        var columns = CsvColumns.Resolve(Header("suplier_name", "can_mail_order?"), "f.csv", Specs);

        Assert.That(columns.CheckRowShape(new CsvRow(2, ["Acme", "y"])), Is.Null);
        Assert.That(columns.CheckRowShape(new CsvRow(2, ["Acme", "y", "", "  "])), Is.Null);
    }

    [Test]
    public void RowShapeRejectsTooFewColumnsAndNonBlankExtras()
    {
        var columns = CsvColumns.Resolve(Header("suplier_name", "can_mail_order?"), "f.csv", Specs);

        Assert.That(columns.CheckRowShape(new CsvRow(2, ["Acme"])), Does.Contain("expected 2 columns but found 1"));
        Assert.That(columns.CheckRowShape(new CsvRow(2, ["Acme", "y", "surprise"])), Does.Contain("extra values"));
    }
}

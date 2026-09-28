using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class CsvReaderTests
{
    private static IReadOnlyList<CsvRow> Parse(string text) => CsvReader.Parse(text, "test.csv");

    [Test]
    public void SplitsSimpleRowsAndFields()
    {
        var rows = Parse("a,b,c\n1,2,3\n");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Fields, Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(rows[1].Fields, Is.EqualTo(new[] { "1", "2", "3" }));
    }

    [Test]
    public void QuotedFieldKeepsCommas()
    {
        var rows = Parse("id,zips\nSUP-1,\"10001, 10002, 10003\"");

        Assert.That(rows[1].Fields, Is.EqualTo(new[] { "SUP-1", "10001, 10002, 10003" }));
    }

    [Test]
    public void DoubledQuoteInsideQuotedFieldIsOneQuote()
    {
        var rows = Parse("name\n\"The \"\"Best\"\" DME\"");

        Assert.That(rows[1].Fields[0], Is.EqualTo("The \"Best\" DME"));
    }

    [TestCase("SUP-1, \"10001, 10002\",cane", TestName = "Space before quote")]
    [TestCase("SUP-1,\t\"10001, 10002\",cane", TestName = "Tab before quote")]
    [TestCase("SUP-1,   \"10001, 10002\" ,cane", TestName = "Spaces before and after quotes")]
    public void QuotePrecededBySpacesStillOpensQuotedField(string row)
    {
        var rows = Parse("id,zips,categories\n" + row);

        Assert.That(rows[1].Fields, Has.Count.EqualTo(3));
        Assert.That(rows[1].Fields[1].Trim(), Is.EqualTo("10001, 10002"));
    }

    [Test]
    public void SpacesOnlyFieldIsKept()
    {
        var rows = Parse("a,b\n   ,x");

        Assert.That(rows[1].Fields, Is.EqualTo(new[] { "   ", "x" }));
    }

    [Test]
    public void QuoteInsideUnquotedFieldIsLiteral()
    {
        var rows = Parse("name\n5\" wheel");

        Assert.That(rows[1].Fields[0], Is.EqualTo("5\" wheel"));
    }

    [Test]
    public void LineBreakInsideQuotesStaysInFieldAndLineNumbersStayCorrect()
    {
        var rows = Parse("a,b\n\"line1\nline2\",x\nnext,row");

        Assert.Multiple(() =>
        {
            Assert.That(rows[1].Fields[0], Is.EqualTo("line1\nline2"));
            Assert.That(rows[1].LineNumber, Is.EqualTo(2));
            Assert.That(rows[2].LineNumber, Is.EqualTo(4));
        });
    }

    [TestCase("a,b\r\n1,2\r\n3,4", TestName = "CRLF")]
    [TestCase("a,b\n1,2\n3,4", TestName = "LF")]
    [TestCase("a,b\r1,2\r3,4", TestName = "CR")]
    public void HandlesAllLineEndings(string text)
    {
        var rows = Parse(text);

        Assert.That(rows.Select(r => r.LineNumber), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(rows[2].Fields, Is.EqualTo(new[] { "3", "4" }));
    }

    [Test]
    public void StripsByteOrderMark()
    {
        var rows = Parse("\uFEFFsupplier_id,name\n1,x");

        Assert.That(rows[0].Fields[0], Is.EqualTo("supplier_id"));
    }

    [Test]
    public void SkipsBlankAndWhitespaceOnlyLinesButKeepsLineNumbers()
    {
        var rows = Parse("a,b\n\n   \n1,2\n\n");

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[1].LineNumber, Is.EqualTo(4));
    }

    [Test]
    public void KeepsEmptyFieldsIncludingTrailingOne()
    {
        var rows = Parse("a,b,c\n,,\n1,,");

        Assert.That(rows[1].Fields, Is.EqualTo(new[] { "", "", "" }));
        Assert.That(rows[2].Fields, Is.EqualTo(new[] { "1", "", "" }));
    }

    [Test]
    public void DoesNotTrimValues()
    {
        var rows = Parse("a\n  padded  ");

        Assert.That(rows[1].Fields[0], Is.EqualTo("  padded  "));
    }

    [Test]
    public void LastRowWithoutTrailingNewlineIsRead()
    {
        var rows = Parse("a\n\"quoted\"");

        Assert.That(rows[1].Fields[0], Is.EqualTo("quoted"));
    }

    [Test]
    public void EmptyTextHasNoRows()
    {
        Assert.That(Parse(""), Is.Empty);
    }

    [Test]
    public void UnclosedQuoteThrowsWithStartLine()
    {
        var ex = Assert.Throws<DataFileException>(() => Parse("a,b\n1,\"open\n2,3"));

        Assert.That(ex!.Message, Does.Contain("Unclosed quote starting on line 2"));
        Assert.That(ex.File, Is.EqualTo("test.csv"));
    }
}

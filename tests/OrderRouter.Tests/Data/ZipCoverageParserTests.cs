using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class ZipCoverageParserTests
{
    private static string Ranges(ZipParseResult result) => result.Coverage.ToString();

    [Test]
    public void ExplicitList()
    {
        var result = ZipCoverageParser.Parse("11410, 11419, 11438");

        Assert.That(Ranges(result), Is.EqualTo("11410, 11419, 11438"));
        Assert.That(result.Problems, Is.Empty);
    }

    [Test]
    public void SingleRange()
    {
        var result = ZipCoverageParser.Parse("10455-10502");

        Assert.Multiple(() =>
        {
            Assert.That(result.Coverage.Covers(10455), Is.True);
            Assert.That(result.Coverage.Covers(10480), Is.True);
            Assert.That(result.Coverage.Covers(10502), Is.True);
            Assert.That(result.Coverage.Covers(10454), Is.False);
            Assert.That(result.Coverage.Covers(10503), Is.False);
        });
    }

    [Test]
    public void MixedRangesAndSingleZips()
    {
        var result = ZipCoverageParser.Parse("10059-10103, 90117, 60699, 10229");

        Assert.That(Ranges(result), Is.EqualTo("10059-10103, 10229, 60699, 90117"));
        Assert.That(result.Problems, Is.Empty);
    }

    [TestCase("2130", 2130, TestName = "Single 4-digit ZIP means 02130")]
    [TestCase("501", 501, TestName = "Single 3-digit ZIP means 00501")]
    [TestCase("1", 1, TestName = "Single 1-digit ZIP means 00001")]
    [TestCase("00001", 1, TestName = "Leading zeros kept")]
    [TestCase("02130", 2130, TestName = "5-digit with leading zero")]
    public void RestoresLostLeadingZeros(string raw, int zip)
    {
        var result = ZipCoverageParser.Parse(raw);

        Assert.That(result.Coverage.Covers(zip), Is.True);
        Assert.That(result.Problems, Is.Empty);
    }

    [Test]
    public void FourDigitRangeMeansLeadingZeroRange()
    {
        var result = ZipCoverageParser.Parse("2164-2213");

        Assert.That(Ranges(result), Is.EqualTo("02164-02213"));
    }

    [Test]
    public void RangeStartMayHaveLostZerosWhenEndIsLonger()
    {
        var result = ZipCoverageParser.Parse("100-99999");

        Assert.That(Ranges(result), Is.EqualTo("00100-99999"));
        Assert.That(result.Problems, Is.Empty);
    }

    [Test]
    public void EveryZipRangeDoesNotCoverTestZipsBelow100()
    {
        var coverage = ZipCoverageParser.Parse("00100-99999").Coverage;

        Assert.Multiple(() =>
        {
            Assert.That(coverage.Covers(98), Is.False);
            Assert.That(coverage.Covers(99), Is.False);
            Assert.That(coverage.Covers(100), Is.True);
            Assert.That(coverage.Covers(99999), Is.True);
        });
    }

    [Test]
    public void OverlappingAndAdjacentRangesAreMerged()
    {
        var result = ZipCoverageParser.Parse("11210-11236, 11211-11237, 11227-11274, 11275");

        Assert.That(Ranges(result), Is.EqualTo("11210-11275"));
    }

    [Test]
    public void RepeatedZipsAreMerged()
    {
        var result = ZipCoverageParser.Parse("10001, 10001, 10001");

        Assert.That(result.Coverage.Ranges, Has.Count.EqualTo(1));
    }

    [TestCase("10001;10002", TestName = "Semicolons")]
    [TestCase("10001 10002", TestName = "Spaces")]
    [TestCase("10001,10002", TestName = "Commas without spaces")]
    [TestCase(" 10001 ,\t10002 , ", TestName = "Extra whitespace and trailing comma")]
    [TestCase("10001,,10002", TestName = "Empty entry")]
    public void AcceptsSeparatorVariants(string raw)
    {
        var result = ZipCoverageParser.Parse(raw);

        Assert.That(Ranges(result), Is.EqualTo("10001-10002"));
        Assert.That(result.Problems, Is.Empty);
    }

    [TestCase("10001 - 10100", TestName = "Spaces around hyphen")]
    [TestCase("10001\u201310100", TestName = "En dash")]
    [TestCase("10001\u201410100", TestName = "Em dash")]
    [TestCase("10001\u221210100", TestName = "Minus sign")]
    public void AcceptsDashVariantsInRanges(string raw)
    {
        var result = ZipCoverageParser.Parse(raw);

        Assert.That(Ranges(result), Is.EqualTo("10001-10100"));
        Assert.That(result.Problems, Is.Empty);
    }

    [TestCase("", TestName = "Empty")]
    [TestCase("   ", TestName = "Whitespace")]
    [TestCase(null, TestName = "Null")]
    public void BlankValueGivesEmptyCoverageWithoutProblems(string? raw)
    {
        var result = ZipCoverageParser.Parse(raw);

        Assert.That(result.Coverage.IsEmpty, Is.True);
        Assert.That(result.Problems, Is.Empty);
    }

    [TestCase("ABC")]
    [TestCase("123456")]
    [TestCase("1000A")]
    [TestCase("10001-")]
    [TestCase("-10001")]
    [TestCase("10001--10100")]
    [TestCase("10001-10100-10200")]
    [TestCase("１０００１", TestName = "Full-width digits")]
    public void InvalidEntryIsDroppedButValidEntriesRemain(string bad)
    {
        var result = ZipCoverageParser.Parse($"10001, {bad}, 10002");

        Assert.That(Ranges(result), Is.EqualTo("10001-10002"));
        Assert.That(result.Problems, Has.Count.EqualTo(1));
        Assert.That(result.Problems[0], Does.Contain("not a ZIP code"));
    }

    [Test]
    public void ReversedRangeIsDropped()
    {
        var result = ZipCoverageParser.Parse("10100-10001, 20000");

        Assert.That(Ranges(result), Is.EqualTo("20000"));
        Assert.That(result.Problems.Single(), Does.Contain("start is after the end"));
    }

    [TestCase("12345-6789", TestName = "ZIP+4 whose end is below the start")]
    [TestCase("01234-5678", TestName = "ZIP+4 whose end is above the start")]
    public void RangeWithShorterEndIsRejectedAsZipPlus4(string raw)
    {
        var result = ZipCoverageParser.Parse(raw);

        Assert.That(result.Coverage.IsEmpty, Is.True);
        Assert.That(result.Problems.Single(), Does.Contain("ZIP+4"));
    }

    [Test]
    public void AllEntriesInvalidGivesEmptyCoverageAndOneProblemEach()
    {
        var result = ZipCoverageParser.Parse("N/A, unknown");

        Assert.That(result.Coverage.IsEmpty, Is.True);
        Assert.That(result.Problems, Has.Count.EqualTo(2));
    }
}

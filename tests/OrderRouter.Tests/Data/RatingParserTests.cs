using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class RatingParserTests
{
    [TestCase("10", 10.0)]
    [TestCase("10.0", 10.0)]
    [TestCase("9", 9.0)]
    [TestCase("5.5", 5.5)]
    [TestCase("2.6", 2.6)]
    [TestCase("1", 1.0)]
    [TestCase(" 7.25 ", 7.25)]
    [TestCase("8.", 8.0)]
    public void ValidScores(string raw, double expected)
    {
        var result = RatingParser.Parse(raw);

        Assert.That(result.Rating, Is.EqualTo((decimal)expected));
        Assert.That(result.Problem, Is.Null);
    }

    [Test]
    public void ScoresAreExactDecimals()
    {
        Assert.That(RatingParser.Parse("10.0").Rating - RatingParser.Parse("9.0").Rating, Is.EqualTo(1.0m));
    }

    [TestCase("no ratings yet")]
    [TestCase("No Ratings Yet")]
    [TestCase("NO RATINGS YET")]
    [TestCase("  no   ratings  yet  ")]
    public void NoRatingsYetIsUnratedWithoutProblem(string raw)
    {
        var result = RatingParser.Parse(raw);

        Assert.That(result.Rating, Is.Null);
        Assert.That(result.Problem, Is.Null);
    }

    [TestCase("", "Blank")]
    [TestCase("   ", "Blank")]
    [TestCase(null, "Blank")]
    [TestCase("N/A", "not a number")]
    [TestCase("8,5", "not a number")]
    [TestCase("1e1", "not a number")]
    [TestCase("9/10", "not a number")]
    [TestCase("1,000", "not a number")]
    [TestCase("no ratings", "not a number")]
    [TestCase("0", "outside")]
    [TestCase("0.9", "outside")]
    [TestCase("10.1", "outside")]
    [TestCase("11", "outside")]
    [TestCase("85", "outside")]
    [TestCase("-5", "outside")]
    public void UnusableScoresAreUnratedWithProblem(string? raw, string expectedProblem)
    {
        var result = RatingParser.Parse(raw);

        Assert.That(result.Rating, Is.Null);
        Assert.That(result.Problem, Does.Contain(expectedProblem));
    }
}

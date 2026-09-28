using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class MailOrderParserTests
{
    [TestCase("y")]
    [TestCase("Y")]
    [TestCase(" y ")]
    [TestCase("yes")]
    [TestCase("YES")]
    [TestCase("true")]
    [TestCase("True")]
    [TestCase("1")]
    public void YesValues(string raw)
    {
        var result = MailOrderParser.Parse(raw);

        Assert.That(result.CanMailOrder, Is.True);
        Assert.That(result.Problem, Is.Null);
    }

    [TestCase("n")]
    [TestCase("N")]
    [TestCase("no")]
    [TestCase("false")]
    [TestCase("FALSE")]
    [TestCase("0")]
    public void NoValues(string raw)
    {
        var result = MailOrderParser.Parse(raw);

        Assert.That(result.CanMailOrder, Is.False);
        Assert.That(result.Problem, Is.Null);
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("maybe")]
    [TestCase("ye")]
    [TestCase("2")]
    [TestCase("y/n")]
    public void UnrecognizedValuesAreTreatedAsNoWithProblem(string? raw)
    {
        var result = MailOrderParser.Parse(raw);

        Assert.That(result.CanMailOrder, Is.False);
        Assert.That(result.Problem, Does.Contain("treated as n"));
    }
}

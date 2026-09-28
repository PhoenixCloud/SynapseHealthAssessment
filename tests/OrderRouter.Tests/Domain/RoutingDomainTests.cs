using OrderRouter.Core.Domain;

namespace OrderRouter.Tests.Domain;

public class SupplierIdComparerTests
{
    private static int Compare(string? x, string? y) => SupplierIdComparer.Instance.Compare(x, y);

    [TestCase("SUP-021", "SUP-0199", TestName = "21 before 199 despite text order")]
    [TestCase("SUP-001", "SUP-002")]
    [TestCase("SUP-002", "SUP-T002", TestName = "Same number: full ID as text")]
    [TestCase("SUP-T003", "SUP-T004")]
    [TestCase("SUP-T014", "SUP-015")]
    [TestCase("SUP-100", "SUP-0115")]
    [TestCase("SUP-9", "SUP-10")]
    [TestCase("SUP-99999999999999999999", "SUP-100000000000000000000", TestName = "Huge numbers do not overflow")]
    [TestCase("SUP-1", "SUPPLIER", TestName = "Numbered before unnumbered")]
    [TestCase("ALPHA", "BETA", TestName = "Unnumbered by text")]
    [TestCase(null, "SUP-1", TestName = "Null first")]
    public void OrdersFirstBeforeSecond(string? first, string? second)
    {
        Assert.That(Compare(first, second), Is.LessThan(0));
        Assert.That(Compare(second, first), Is.GreaterThan(0));
    }

    [TestCase("SUP-001")]
    [TestCase("SUP-T001")]
    public void IdEqualsItself(string id) => Assert.That(Compare(id, id), Is.Zero);

    [Test]
    public void LeadingZerosAloneDoNotMakeIdsEqual() =>
        Assert.That(Compare("SUP-01", "SUP-001"), Is.Not.Zero);

    [Test]
    public void SortsRealIdFormats()
    {
        string[] ids = ["SUP-0199", "SUP-T002", "SUP-021", "SUP-002", "SUP-1100", "SUP-0115", "SUP-T013"];

        Assert.That(ids.Order(SupplierIdComparer.Instance), Is.EqualTo(new[]
        {
            "SUP-002", "SUP-T002", "SUP-T013", "SUP-021", "SUP-0115", "SUP-0199", "SUP-1100",
        }));
    }
}

public class OrderTests
{
    private static readonly OrderLine Line = new(1, "WC-1", 1);

    [TestCase("10015")]
    [TestCase("00001")]
    public void ValidZip(string zip) => Assert.That(new Order(null, zip, false, [Line]).CustomerZip, Is.EqualTo(zip));

    [TestCase("")]
    [TestCase("2130")]
    [TestCase("100150")]
    [TestCase("1001A")]
    [TestCase("１００１５")]
    public void InvalidZipIsRejected(string zip) =>
        Assert.Throws<ArgumentException>(() => _ = new Order(null, zip, false, [Line]));

    [Test]
    public void ZipIsReadAsANumber() => Assert.That(new Order(null, "02130", false, [Line]).Zip, Is.EqualTo(2130));

    [Test]
    public void NoLinesIsRejected() =>
        Assert.Throws<ArgumentException>(() => _ = new Order(null, "10015", false, []));

    [TestCase(0)]
    [TestCase(-1)]
    public void QuantityBelowOneIsRejected(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new OrderLine(1, "WC-1", quantity));

    [Test]
    public void PositionBelowOneIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new OrderLine(0, "WC-1", 1));

    [TestCase("")]
    [TestCase("  ")]
    public void BlankProductCodeIsRejected(string code) =>
        Assert.Throws<ArgumentException>(() => _ = new OrderLine(1, code, 1));
}

public class RoutingDecisionTests
{
    private static readonly Shipment AShipment =
        new("SUP-1", "Acme", [new RoutedItem("WC-1", 1, "wheelchair", FulfillmentMode.Local)]);

    [Test]
    public void SuccessHasShipmentsAndNoErrors()
    {
        var decision = RoutingDecision.Success([AShipment]);

        Assert.That(decision.Feasible, Is.True);
        Assert.That(decision.Shipments, Has.Count.EqualTo(1));
        Assert.That(decision.Errors, Is.Empty);
    }

    [Test]
    public void FailureHasErrorsAndNoShipments()
    {
        var decision = RoutingDecision.Failure(["Something failed."]);

        Assert.That(decision.Feasible, Is.False);
        Assert.That(decision.Shipments, Is.Empty);
        Assert.That(decision.Errors, Is.EqualTo(new[] { "Something failed." }));
    }

    [Test]
    public void SuccessNeedsAShipment() =>
        Assert.Throws<ArgumentException>(() => RoutingDecision.Success([]));

    [Test]
    public void FailureNeedsAnError() =>
        Assert.Throws<ArgumentException>(() => RoutingDecision.Failure([]));
}

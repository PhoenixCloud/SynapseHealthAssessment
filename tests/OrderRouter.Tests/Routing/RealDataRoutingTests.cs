using OrderRouter.Core.Data;
using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;

namespace OrderRouter.Tests.Routing;

/// <summary>Every expected result in README section 6, using the real, unmodified CSV files.</summary>
public class RealDataRoutingTests
{
    private RoutingService _service = null!;

    [OneTimeSetUp]
    public void LoadRealData()
    {
        var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "Data");
        _service = new RoutingService(ReferenceDataLoader.Load(directory).Data);
    }

    private RoutingDecision Route(string body)
    {
        var parsed = OrderRequestParser.Parse(body);
        Assert.That(parsed.Errors, Is.Empty, "The test request itself should be valid.");
        return _service.Route(parsed.Order!);
    }

    private RoutingDecision Route(string zip, bool mailOrder, params string[] codes)
    {
        var items = string.Join(", ", codes.Select(c => $$"""{ "product_code": "{{c}}", "quantity": 1 }"""));
        return Route($$"""{ "customer_zip": "{{zip}}", "mail_order": {{(mailOrder ? "true" : "false")}}, "items": [{{items}}] }""");
    }

    private static void AssertSingleShipment(RoutingDecision decision, string supplierId, FulfillmentMode mode)
    {
        Assert.That(decision.Feasible, Is.True, string.Join(" | ", decision.Errors));
        var shipment = decision.Shipments.Single();
        Assert.That(shipment.SupplierId, Is.EqualTo(supplierId));
        Assert.That(shipment.Items.Select(i => i.FulfillmentMode), Is.All.EqualTo(mode));
    }

    // ---- sample_orders.json, read from the real file ----

    private static IEnumerable<TestCaseData> SampleOrdersFromFile()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", "sample_orders.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var expected = new Dictionary<string, (string Supplier, FulfillmentMode Mode)>
        {
            ["ORD-001"] = ("SUP-0636", FulfillmentMode.Local),
            ["ORD-002"] = ("SUP-0928", FulfillmentMode.Local),
            ["ORD-003"] = ("SUP-023", FulfillmentMode.MailOrder),
        };

        foreach (var order in document.RootElement.EnumerateArray())
        {
            var id = order.GetProperty("order_id").GetString()!;
            yield return new TestCaseData(order.GetRawText(), expected[id].Supplier, expected[id].Mode)
                .SetName($"sample_orders.json {id}");
        }
    }

    /// <summary>
    /// Each order object in the delivered file, sent exactly as written, including its
    /// property names and the extra "priority" and "notes" fields.
    /// </summary>
    [TestCaseSource(nameof(SampleOrdersFromFile))]
    public void SampleOrderFileRoutesAsExpected(string orderJson, string supplierId, FulfillmentMode mode)
    {
        var decision = Route(orderJson);

        AssertSingleShipment(decision, supplierId, mode);
        var requestedLines = System.Text.Json.JsonDocument.Parse(orderJson).RootElement.GetProperty("items").GetArrayLength();
        Assert.That(decision.Shipments[0].Items, Has.Count.EqualTo(requestedLines));
    }

    [Test]
    public void SampleOrdersFileHasExactlyThreeOrders() =>
        Assert.That(SampleOrdersFromFile().Count(), Is.EqualTo(3));

    // ---- sample_orders.json (copies of the orders, inline) ----

    [Test]
    public void Ord001() => AssertSingleShipment(Route("""
        { "order_id": "ORD-001", "customer_zip": "10015", "mail_order": false,
          "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "OX-PORT-024", "quantity": 1 } ],
          "priority": "standard", "notes": "Simple order" }
        """), "SUP-0636", FulfillmentMode.Local);

    [Test]
    public void Ord002() => AssertSingleShipment(Route("""
        { "order_id": "ORD-002", "customer_zip": "77059", "mail_order": false,
          "items": [ { "product_code": "HB-FUL-018", "quantity": 1 }, { "product_code": "PL-ELEC-043", "quantity": 1 },
                     { "product_code": "CM-BED-048", "quantity": 1 }, { "product_code": "BP-AUTO-077", "quantity": 1 } ],
          "priority": "rush", "notes": "Larger order" }
        """), "SUP-0928", FulfillmentMode.Local);

    [Test]
    public void Ord003()
    {
        var decision = Route("""
            { "order_id": "ORD-003", "customer_zip": "02130", "mail_order": true,
              "items": [ { "product_code": "CP-STD-031", "quantity": 1 }, { "product_code": "CP-MSK-FF-035", "quantity": 2 },
                         { "product_code": "NB-COMP-039", "quantity": 1 } ],
              "priority": "standard", "notes": "Respiratory-focused order" }
            """);

        AssertSingleShipment(decision, "SUP-023", FulfillmentMode.MailOrder);
        Assert.That(decision.Shipments[0].Items.Select(i => (i.ProductCode, i.Quantity, i.Category)), Is.EqualTo(new[]
        {
            ("CP-STD-031", 1, "CPAP"), ("CP-MSK-FF-035", 2, "CPAP"), ("NB-COMP-039", 1, "nebulizer"),
        }));
    }

    // ---- Test fixtures (SUP-T0xx suppliers, *-SENT-001 products) ----

    [Test]
    public void MailOrderSuppliersExcludedWhenMailOrderIsOff() =>
        AssertSingleShipment(Route("00001", false, "WC-SENT-001"), "SUP-T001", FulfillmentMode.Local);

    [Test]
    public void LocalWinsWhenRatingsAreSimilar() =>
        AssertSingleShipment(Route("00001", true, "WC-SENT-001"), "SUP-T001", FulfillmentMode.Local);

    [Test]
    public void ExactTieGoesToLowestId() =>
        AssertSingleShipment(Route("00003", false, "CN-SENT-001"), "SUP-T003", FulfillmentMode.Local);

    [Test]
    public void LocalPreferredWhenRatingsAreEqual() =>
        AssertSingleShipment(Route("00005", true, "WK-SENT-001"), "SUP-T005", FulfillmentMode.Local);

    [Test]
    public void FewestShipmentsBeatsRating()
    {
        var decision = Route("00007", false, "WC-SENT-001", "CN-SENT-001", "WK-SENT-001");

        AssertSingleShipment(decision, "SUP-T007", FulfillmentMode.Local);
        Assert.That(decision.Shipments[0].Items, Has.Count.EqualTo(3));
    }

    [Test]
    public void UnratedSuppliersTie() =>
        AssertSingleShipment(Route("00040", false, "RL-SENT-001"), "SUP-T011", FulfillmentMode.Local);

    [Test]
    public void RatedMailOrderBeatsUnratedLocalOutsideTheBand() =>
        AssertSingleShipment(Route("00040", true, "RL-SENT-001"), "SUP-021", FulfillmentMode.MailOrder);

    [Test]
    public void CategoryComesFromTheColumnNotTheCodePrefix()
    {
        var decision = Route("00040", true, "CM-SENT-001");

        AssertSingleShipment(decision, "SUP-021", FulfillmentMode.MailOrder);
        Assert.That(decision.Shipments[0].Items[0].Category, Is.EqualTo("cpm machine"));
    }

    [Test]
    public void NoSupplierCoversTheZip()
    {
        var decision = Route("00040", false, "CM-SENT-001");

        Assert.That(decision.Feasible, Is.False);
        Assert.That(decision.Errors, Is.EqualTo(new[]
        {
            "No eligible supplier for product_code CM-SENT-001 (category: cpm machine).",
        }));
    }

    [Test]
    public void ExactZipMatch() =>
        AssertSingleShipment(Route("00098", false, "WC-SENT-001"), "SUP-T014", FulfillmentMode.Local);

    [Test]
    public void EveryZipRangeDoesNotCover00099() =>
        Assert.That(Route("00099", false, "WC-SENT-001").Feasible, Is.False);

    // ---- Other checks on real data ----

    [Test]
    public void UnknownProductOnRealData()
    {
        var decision = Route("10015", false, "WC-STD-001", "DOES-NOT-EXIST");

        Assert.That(decision.Errors, Is.EqualTo(new[] { "Unknown product_code: DOES-NOT-EXIST." }));
    }

    [Test]
    public void OrderForEveryCategoryIsStillOneShipment()
    {
        string[] oneProductPerCategory =
        [
            "WC-STD-001", "WK-STD-009", "HB-SEM-017", "OX-PORT-024", "CP-STD-031", "NB-COMP-039", "PL-ELEC-043",
            "CM-BED-048", "SC-STD-053", "RL-STD-058", "CN-STD-063", "CR-AXL-068", "KS-STD-073", "BP-AUTO-077",
            "GL-STD-081", "CS-KH-15-085", "BB-LUM-090", "AB-LACE-094", "CC-SOFT-098", "TU-DUAL-102",
            "HP-ELEC-106", "IM-KNEE-110", "CPM-KNEE-114", "TD-CERV-117",
        ];

        var decision = Route("10015", true, oneProductPerCategory);

        Assert.That(decision.Feasible, Is.True);
        Assert.That(decision.Shipments, Has.Count.EqualTo(1));
    }
}

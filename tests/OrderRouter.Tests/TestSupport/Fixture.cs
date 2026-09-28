using OrderRouter.Core.Data;
using OrderRouter.Core.Domain;

namespace OrderRouter.Tests.TestSupport;

/// <summary>Small builders for in-memory routing tests.</summary>
internal static class Fixture
{
    public const string Zip = "10015";
    public const string OtherZip = "90210";

    /// <summary>A supplier serving <paramref name="zips"/> ("" for none).</summary>
    public static Supplier Supplier(string id, decimal? rating, string zips, bool canMail, params string[] categories) =>
        new(id, $"{id} Name", ZipCoverageParser.Parse(zips).Coverage,
            categories.Select(Normalize.CategoryKey).ToHashSet(), rating, canMail);

    /// <summary>Serves <see cref="Zip"/>.</summary>
    public static Supplier Local(string id, decimal? rating, params string[] categories) =>
        Supplier(id, rating, Zip, canMail: false, categories);

    /// <summary>Serves <see cref="Zip"/> and can also mail.</summary>
    public static Supplier LocalMailer(string id, decimal? rating, params string[] categories) =>
        Supplier(id, rating, Zip, canMail: true, categories);

    /// <summary>Doesn't serve <see cref="Zip"/>, but can mail.</summary>
    public static Supplier Mailer(string id, decimal? rating, params string[] categories) =>
        Supplier(id, rating, OtherZip, canMail: true, categories);

    /// <summary>Doesn't serve <see cref="Zip"/> and can't mail.</summary>
    public static Supplier Elsewhere(string id, decimal? rating, params string[] categories) =>
        Supplier(id, rating, OtherZip, canMail: false, categories);

    /// <summary>Products "A-1", "B-1", ... with the given categories.</summary>
    public static readonly Product[] Products =
    [
        Product("WC-1", "wheelchair"),
        Product("WC-2", "wheelchair"),
        Product("CN-1", "cane"),
        Product("WK-1", "walker"),
        Product("OX-1", "oxygen"),
        Product("CP-1", "CPAP"),
        Product("NB-1", "nebulizer"),
    ];

    public static Product Product(string code, string category) =>
        new(code, $"{code} name", category, Normalize.CategoryKey(category));

    public static ReferenceData DataWith(params Supplier[] suppliers) => new(Products, suppliers);

    public static Order Order(bool mailOrder, params string[] productCodes) =>
        OrderAt(Zip, mailOrder, productCodes);

    public static Order OrderAt(string zip, bool mailOrder, params string[] productCodes) =>
        new(null, zip, mailOrder, productCodes.Select((code, i) => new OrderLine(i + 1, code, 1)).ToList());

    public static RoutingDecision Route(Order order, params Supplier[] suppliers) =>
        new Core.Routing.RoutingService(DataWith(suppliers)).Route(order);

    /// <summary>"SUP-1[WC-1:local,CN-1:local] SUP-2[...]" for compact assertions.</summary>
    public static string Describe(RoutingDecision decision) =>
        decision.Feasible
            ? string.Join(" ", decision.Shipments.Select(s =>
                $"{s.SupplierId}[{string.Join(",", s.Items.Select(i => $"{i.ProductCode}:{Mode(i.FulfillmentMode)}"))}]"))
            : "INFEASIBLE: " + string.Join(" | ", decision.Errors);

    private static string Mode(FulfillmentMode mode) => mode == FulfillmentMode.Local ? "local" : "mail";
}

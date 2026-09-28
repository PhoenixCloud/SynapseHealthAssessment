using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;
using static OrderRouter.Tests.TestSupport.Fixture;

namespace OrderRouter.Tests.Routing;

/// <summary>
/// Checks that removing dominated candidates never changes the result, by comparing against a
/// search over every candidate on many random data sets. Ratings come from a small set so ties
/// are common, which is where pruning mistakes would show up.
/// </summary>
public class RoutingPruningTests
{
    private static readonly string[] Categories = ["wheelchair", "cane", "walker", "oxygen"];
    private static readonly string[] Codes = ["WC-1", "CN-1", "WK-1", "OX-1"];
    private static readonly decimal?[] Ratings = [null, 5m, 5.5m, 6m, 6.5m, 7m, 8m, 9m, 9.5m, 10m];

    [Test]
    public void PruningNeverChangesTheResult([Range(0, 499)] int seed)
    {
        var random = new Random(seed);
        var suppliers = Enumerable.Range(1, random.Next(3, 10)).Select(i => RandomSupplier(random, i)).ToArray();
        var codes = Enumerable.Range(0, random.Next(1, 5)).Select(_ => Codes[random.Next(Codes.Length)]).ToArray();
        var order = Order(random.Next(2) == 0, codes);
        var data = DataWith(suppliers);

        var pruned = new RoutingService(data, pruneCandidates: true).Route(order);
        var exhaustive = new RoutingService(data, pruneCandidates: false).Route(order);

        Assert.That(Describe(pruned), Is.EqualTo(Describe(exhaustive)));
    }

    private static Supplier RandomSupplier(Random random, int number)
    {
        var categories = Categories.Where(_ => random.Next(3) == 0).ToArray();
        if (categories.Length == 0) categories = [Categories[random.Next(Categories.Length)]];

        var id = random.Next(4) == 0 ? $"SUP-T{number:D3}" : $"SUP-{number:D3}";
        var rating = Ratings[random.Next(Ratings.Length)];
        var zips = random.Next(2) == 0 ? Zip : OtherZip;

        return Supplier(id, rating, zips, canMail: random.Next(2) == 0, categories);
    }
}

using OrderRouter.Core.Data;
using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;

namespace OrderRouter.Tests.Routing;

/// <summary>
/// An independent oracle: re-derives the spec's rules by brute force, without using any routing
/// code, and checks the service against it on random orders (in-memory and real data).
/// Checks, for every feasible order:
/// every requested line appears exactly once with its quantity; every item goes to an eligible
/// supplier that handles its category, with the right fulfillment_mode and category text;
/// no supplier appears twice; the shipment count is the true minimum; and the chosen plan is
/// best under the rating band, then local count, then rating.
/// </summary>
public class RoutingOracleTests
{
    private static readonly string[] Categories = ["a", "b", "c", "d", "e"];
    private static readonly string[] ZipSpecs =
        ["10015", "2130", "02130", "10000-10100", "00100-99999", "10015, 2130", "2100-2200", "", "98"];
    private static readonly string[] OrderZips = ["10015", "02130", "00099", "00098", "02150"];
    private static readonly decimal?[] Ratings =
        [null, 1m, 4.5m, 5m, 5.5m, 6m, 6.5m, 7m, 8m, 8.9m, 9m, 9.5m, 10m];

    private static ReferenceData? _real;

    [Test]
    public void RandomInMemoryOrders([Range(0, 1999)] int seed)
    {
        var random = new Random(seed);
        var products = Categories.Select(c => new Product($"P-{c.ToUpperInvariant()}", "n",
            random.Next(2) == 0 ? c.ToUpperInvariant() : c, c)).ToList();
        var suppliers = Enumerable.Range(1, random.Next(1, 11)).Select(i =>
        {
            var categories = Categories.Where(_ => random.Next(3) == 0).ToArray();
            if (categories.Length == 0) categories = [Categories[random.Next(Categories.Length)]];
            var zips = string.Join(", ", Enumerable.Range(0, random.Next(1, 3)).Select(_ => ZipSpecs[random.Next(ZipSpecs.Length)]));
            return new Supplier(random.Next(5) == 0 ? $"SUP-T{i:D3}" : $"SUP-{i:D3}", $"S{i}",
                ZipCoverageParser.Parse(zips).Coverage, categories.ToHashSet(), Ratings[random.Next(Ratings.Length)],
                random.Next(2) == 0);
        }).ToList();

        var lines = Enumerable.Range(1, random.Next(1, 6)).Select(position =>
        {
            var code = random.Next(12) == 0 ? "UNKNOWN-1" : products[random.Next(products.Count)].Code;
            if (random.Next(3) == 0) code = code.ToLowerInvariant();
            return new OrderLine(position, code, random.Next(1, 6));
        }).ToList();

        var order = new Order(null, OrderZips[random.Next(OrderZips.Length)], random.Next(2) == 0, lines);
        Check(new ReferenceData(products, suppliers), order, bruteForce: true);
    }

    [Test]
    public void RandomOrdersOnRealData([Range(0, 499)] int seed)
    {
        _real ??= ReferenceDataLoader.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Data")).Data;
        var random = new Random(100_000 + seed);

        var zips = _real.Suppliers.SelectMany(s => s.ServiceArea.Ranges)
            .SelectMany(r => new[] { r.Start, r.End }).Distinct().OrderBy(z => z)
            .Select(z => z.ToString("D5"))
            .Concat(["00001", "00003", "00005", "00007", "00040", "00098", "00099", "00000", "99999"])
            .ToArray();
        var codes = _real.Products.Keys.Order().ToArray();

        var lines = Enumerable.Range(1, random.Next(1, 7))
            .Select(position => new OrderLine(position, codes[random.Next(codes.Length)], random.Next(1, 4))).ToList();
        var order = new Order(null, zips[random.Next(zips.Length)], random.Next(2) == 0, lines);

        Check(_real, order, bruteForce: false);
    }

    private static void Check(ReferenceData data, Order order, bool bruteForce)
    {
        var decision = new RoutingService(data).Route(order);
        var zip = order.Zip;
        bool Eligible(Supplier s) => s.Serves(zip) || (order.MailOrder && s.CanMailOrder);

        var resolved = order.Lines.Select(l => data.TryGetProduct(l.ProductCode, out var p) ? p : null).ToList();
        var orderCategories = resolved.OfType<Product>().Select(p => p.CategoryKey).Distinct().ToList();
        var expectedFeasible = resolved.All(p => p is not null)
                               && orderCategories.All(c => data.Suppliers.Any(s => s.Handles(c) && Eligible(s)));

        Assert.That(decision.Feasible, Is.EqualTo(expectedFeasible), "feasibility");
        if (!decision.Feasible)
        {
            Assert.That(decision.Errors, Is.Not.Empty);
            Assert.That(decision.Shipments, Is.Empty);
            return;
        }

        // Every requested line exactly once, with its quantity.
        var routed = decision.Shipments.SelectMany(s => s.Items.Select(i => (s.SupplierId, Item: i))).ToList();
        Assert.That(routed.Select(r => (r.Item.ProductCode, r.Item.Quantity)).Order(),
            Is.EqualTo(order.Lines.Select(l => (Normalize.ProductCode(l.ProductCode), l.Quantity)).Order()),
            "each requested line routed exactly once");
        Assert.That(decision.Shipments.Select(s => s.SupplierId), Is.Unique, "no supplier twice");

        foreach (var (supplierId, item) in routed)
        {
            var supplier = data.Suppliers.Single(s => s.Id == supplierId);
            var product = data.Products[item.ProductCode];
            Assert.That(supplier.Handles(product.CategoryKey), Is.True, $"{supplierId} handles {product.CategoryKey}");
            Assert.That(Eligible(supplier), Is.True, $"{supplierId} is eligible");
            Assert.That(item.FulfillmentMode,
                Is.EqualTo(supplier.Serves(zip) ? FulfillmentMode.Local : FulfillmentMode.MailOrder), "fulfillment mode");
            Assert.That(item.Category, Is.EqualTo(product.Category), "category as written");
        }

        var pool = data.Suppliers.Where(s => Eligible(s) && orderCategories.Any(s.Handles)).ToList();
        var chosen = decision.Shipments.Select(sh => data.Suppliers.Single(s => s.Id == sh.SupplierId)).ToList();

        List<List<Supplier>>? covers = null;
        if (bruteForce)
        {
            for (var size = 1; size <= pool.Count && covers is null; size++)
            {
                var found = Subsets(pool, size).Where(set => orderCategories.All(c => set.Any(s => s.Handles(c)))).ToList();
                if (found.Count > 0) covers = found;
            }
        }
        else
        {
            var singles = pool.Where(s => orderCategories.All(s.Handles)).Select(s => new List<Supplier> { s }).ToList();
            if (singles.Count > 0) covers = singles;
        }

        if (covers is null) return; // real data needing several suppliers: minimum not brute-forced

        Assert.That(chosen, Has.Count.EqualTo(covers[0].Count), "fewest shipments");

        decimal Average(List<Supplier> plan) => plan.Average(s => s.EffectiveRating);
        int LocalCount(List<Supplier> plan) => plan.Count(s => s.Serves(zip));

        var best = covers.Max(Average);
        var inBand = covers.Where(p => best - Average(p) <= 1.0m).ToList();
        var mostLocal = inBand.Max(LocalCount);
        var top = inBand.Where(p => LocalCount(p) == mostLocal).ToList();
        var topRating = top.Max(Average);
        var chosenIds = chosen.Select(s => s.Id).ToHashSet();

        Assert.That(top.Any(p => Average(p) == topRating && p.Select(s => s.Id).ToHashSet().SetEquals(chosenIds)), Is.True,
            $"chosen {string.Join("+", chosenIds)} (rating {Average(chosen)}, local {LocalCount(chosen)}) " +
            $"should be a best plan (rating {topRating}, local {mostLocal})");
    }

    private static IEnumerable<List<Supplier>> Subsets(List<Supplier> items, int size, int start = 0)
    {
        if (size == 0)
        {
            yield return [];
            yield break;
        }

        for (var i = start; i <= items.Count - size; i++)
        {
            foreach (var rest in Subsets(items, size - 1, i + 1))
            {
                yield return [items[i], .. rest];
            }
        }
    }
}

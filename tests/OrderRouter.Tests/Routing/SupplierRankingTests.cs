using OrderRouter.Core.Routing;

namespace OrderRouter.Tests.Routing;

public class SupplierRankingTests
{
    private static RankedOption<string> Option(string name, decimal rating, int local, params string[] ids) =>
        new(name, rating, local, ids.Length == 0 ? [name] : ids);

    [Test]
    public void SingleOptionWins() =>
        Assert.That(SupplierRanking.PickBest([Option("SUP-1", 5m, 0)]), Is.EqualTo("SUP-1"));

    [Test]
    public void EmptyOptionsThrow() =>
        Assert.Throws<ArgumentException>(() => SupplierRanking.PickBest(Array.Empty<RankedOption<string>>()));

    [Test]
    public void OptionsOutsideTheBandLoseEvenIfLocal() =>
        Assert.That(SupplierRanking.PickBest([Option("SUP-1", 10m, 0), Option("SUP-2", 8.99m, 1)]), Is.EqualTo("SUP-1"));

    [Test]
    public void LocalWinsInsideTheBandIncludingTheEdge() =>
        Assert.That(SupplierRanking.PickBest([Option("SUP-1", 10m, 0), Option("SUP-2", 9m, 1)]), Is.EqualTo("SUP-2"));

    [Test]
    public void MoreLocalSuppliersWinInsideTheBand() =>
        Assert.That(SupplierRanking.PickBest([Option("P1", 9.5m, 1, "SUP-1", "SUP-2"), Option("P2", 9m, 2, "SUP-3", "SUP-4")]),
            Is.EqualTo("P2"));

    [Test]
    public void HigherRatingWinsWhenLocalityIsEqual() =>
        Assert.That(SupplierRanking.PickBest([Option("SUP-1", 9m, 1), Option("SUP-2", 9.5m, 1)]), Is.EqualTo("SUP-2"));

    [Test]
    public void IdsDecideAFullTie() =>
        Assert.That(SupplierRanking.PickBest([Option("SUP-0199", 9m, 1), Option("SUP-021", 9m, 1)]), Is.EqualTo("SUP-021"));

    [Test]
    public void PlanIdsAreSortedBeforeComparing()
    {
        // Sorted: P1 = [SUP-2, SUP-9], P2 = [SUP-3, SUP-4]. First difference: SUP-2 < SUP-3.
        var best = SupplierRanking.PickBest([Option("P2", 8m, 1, "SUP-4", "SUP-3"), Option("P1", 8m, 1, "SUP-9", "SUP-2")]);

        Assert.That(best, Is.EqualTo("P1"));
    }

    [Test]
    public void ResultDoesNotDependOnInputOrder()
    {
        RankedOption<string>[] options = [Option("SUP-3", 9m, 1), Option("SUP-1", 9m, 1), Option("SUP-2", 9m, 1)];

        Assert.That(SupplierRanking.PickBest(options), Is.EqualTo("SUP-1"));
        Assert.That(SupplierRanking.PickBest(options.Reverse()), Is.EqualTo("SUP-1"));
    }
}

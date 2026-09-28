using OrderRouter.Core.Domain;

namespace OrderRouter.Tests.Domain;

public class ZipCoverageTests
{
    [Test]
    public void EmptyCoversNothing() => Assert.That(ZipCoverage.Empty.Covers(10001), Is.False);

    [Test]
    public void MergesUnsortedOverlappingAndAdjacentRanges()
    {
        var coverage = ZipCoverage.FromRanges([new(300, 400), new(100, 200), new(201, 250), new(150, 160)]);

        Assert.That(coverage.ToString(), Is.EqualTo("00100-00250, 00300-00400"));
    }

    [TestCase(99, false)]
    [TestCase(100, true)]
    [TestCase(250, true)]
    [TestCase(251, false)]
    [TestCase(300, true)]
    [TestCase(401, false)]
    public void CoversChecksEveryRange(int zip, bool expected)
    {
        var coverage = ZipCoverage.FromRanges([new(100, 250), new(300, 400)]);

        Assert.That(coverage.Covers(zip), Is.EqualTo(expected));
    }

    [Test]
    public void EqualityIgnoresInputOrder()
    {
        var a = ZipCoverage.FromRanges([new(1, 1), new(5, 9)]);
        var b = ZipCoverage.FromRanges([new(5, 9), new(1, 1)]);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [TestCase(-1, 5)]
    [TestCase(5, 100000)]
    [TestCase(10, 5)]
    public void InvalidRangeThrows(int start, int end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ZipRange(start, end));
}

public class SupplierTests
{
    private static Supplier Make(
        decimal? rating = 8m, string id = "SUP-1", string name = "Acme", params string[] categories) =>
        new(id, name, ZipCoverage.Empty,
            new HashSet<string>(categories.Length == 0 ? ["cane"] : categories), rating, false);

    [Test]
    public void RatedSupplierUsesItsScore() => Assert.That(Make(9.2m).EffectiveRating, Is.EqualTo(9.2m));

    [Test]
    public void UnratedSupplierUsesMiddleScore()
    {
        Assert.That(Make(null).IsRated, Is.False);
        Assert.That(Make(null).EffectiveRating, Is.EqualTo(5.5m));
    }

    [TestCase(1.0)]
    [TestCase(10.0)]
    public void RatingAtEitherEndOfTheScaleIsAllowed(double rating) =>
        Assert.That(Make((decimal)rating).Rating, Is.EqualTo((decimal)rating));

    [TestCase(0.9)]
    [TestCase(10.1)]
    [TestCase(42.0)]
    [TestCase(-1.0)]
    public void RatingOutsideTheScaleIsRejected(double rating) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Make((decimal)rating));

    [TestCase("")]
    [TestCase("   ")]
    public void BlankIdIsRejected(string id) =>
        Assert.Throws<ArgumentException>(() => Make(id: id));

    [TestCase("")]
    [TestCase("   ")]
    public void BlankNameIsRejected(string name) =>
        Assert.Throws<ArgumentException>(() => Make(name: name));

    [Test]
    public void NoCategoriesIsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            _ = new Supplier("SUP-1", "Acme", ZipCoverage.Empty, new HashSet<string>(), 8m, false));

    [Test]
    public void BlankCategoryIsRejected() =>
        Assert.Throws<ArgumentException>(() => Make(categories: ["cane", " "]));

    [Test]
    public void MissingServiceAreaIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            _ = new Supplier("SUP-1", "Acme", null!, new HashSet<string> { "cane" }, 8m, false));

    [Test]
    public void EmptyServiceAreaIsAllowedForMailOrderOnlySuppliers() =>
        Assert.That(Make().ServiceArea.IsEmpty, Is.True);
}

public class ProductTests
{
    [Test]
    public void ValidProductKeepsItsValues()
    {
        var product = new Product("WC-1", "", "Wheelchair", "wheelchair");

        Assert.That(product.Code, Is.EqualTo("WC-1"));
        Assert.That(product.Name, Is.Empty);
    }

    [TestCase("", "wheelchair", "wheelchair", TestName = "Blank code")]
    [TestCase("WC-1", " ", "wheelchair", TestName = "Blank category")]
    [TestCase("WC-1", "wheelchair", "", TestName = "Blank category key")]
    public void BlankRequiredValueIsRejected(string code, string category, string categoryKey) =>
        Assert.Throws<ArgumentException>(() => _ = new Product(code, "Name", category, categoryKey));

    [Test]
    public void NullNameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => _ = new Product("WC-1", null!, "wheelchair", "wheelchair"));
}

public class ReferenceDataTests
{
    [Test]
    public void DuplicateProductCodesAreRejected()
    {
        Product[] products = [new("WC-1", "A", "wheelchair", "wheelchair"), new("wc-1", "B", "wheelchair", "wheelchair")];

        Assert.Throws<ArgumentException>(() => _ = new ReferenceData(products, []));
    }

    [Test]
    public void UnknownProductIsNotFound()
    {
        var data = new ReferenceData([new Product("WC-1", "A", "wheelchair", "wheelchair")], []);

        Assert.That(data.TryGetProduct("NOPE", out _), Is.False);
        Assert.That(data.TryGetProduct("", out _), Is.False);
    }
}

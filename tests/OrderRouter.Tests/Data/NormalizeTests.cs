using OrderRouter.Core.Data;

namespace OrderRouter.Tests.Data;

public class NormalizeTests
{
    [TestCase("can_mail_order?", "canmailorder")]
    [TestCase("suplier_name", "supliername")]
    [TestCase("  Customer Satisfaction Score ", "customersatisfactionscore")]
    [TestCase("SUPPLIER-ID", "supplierid")]
    [TestCase(null, "")]
    public void HeaderKey(string? input, string expected) =>
        Assert.That(Normalize.HeaderKey(input), Is.EqualTo(expected));

    [TestCase("CPAP", "cpap")]
    [TestCase("cpap", "cpap")]
    [TestCase("CPM machine", "cpm machine")]
    [TestCase("  CPM   Machine  ", "cpm machine")]
    [TestCase("cpm\tmachine", "cpm machine")]
    [TestCase("cpm\u00A0machine", "cpm machine")]
    [TestCase("", "")]
    [TestCase(null, "")]
    public void CategoryKey(string? input, string expected) =>
        Assert.That(Normalize.CategoryKey(input), Is.EqualTo(expected));

    [TestCase("WC-STD-001", "WC-STD-001")]
    [TestCase(" wc-std-001 ", "WC-STD-001")]
    [TestCase("Wc-Std-001", "WC-STD-001")]
    [TestCase(null, "")]
    public void ProductCode(string? input, string expected) =>
        Assert.That(Normalize.ProductCode(input), Is.EqualTo(expected));

    [Test]
    public void SupplierIdKeyIgnoresCaseAndSurroundingWhitespace() =>
        Assert.That(Normalize.SupplierIdKey(" sup-t001 "), Is.EqualTo(Normalize.SupplierIdKey("SUP-T001")));

    [TestCase("  a   b  ", "a b")]
    [TestCase("a\r\nb", "a b")]
    [TestCase("   ", "")]
    public void CollapseWhitespace(string input, string expected) =>
        Assert.That(Normalize.CollapseWhitespace(input), Is.EqualTo(expected));
}

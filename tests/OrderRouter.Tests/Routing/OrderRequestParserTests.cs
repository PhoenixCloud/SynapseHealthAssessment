using OrderRouter.Core.Routing;

namespace OrderRouter.Tests.Routing;

/// <summary>Good, bad and missing values for every request field (README section 2).</summary>
public class OrderRequestParserTests
{
    private const string ValidItems = """[{ "product_code": "WC-STD-001", "quantity": 1 }]""";

    private static OrderParseResult Parse(string body) => OrderRequestParser.Parse(body);

    /// <summary>A valid order with one field's raw JSON replaced, or removed when null.</summary>
    private static string Body(
        string? orderId = "\"ORD-1\"", string? zip = "\"10015\"", string? mailOrder = "false", string? items = ValidItems)
    {
        var fields = new List<string>();
        if (orderId is not null) fields.Add($"\"order_id\": {orderId}");
        if (zip is not null) fields.Add($"\"customer_zip\": {zip}");
        if (mailOrder is not null) fields.Add($"\"mail_order\": {mailOrder}");
        if (items is not null) fields.Add($"\"items\": {items}");
        return "{" + string.Join(", ", fields) + "}";
    }

    private static string Item(string? code = "\"WC-STD-001\"", string? quantity = "1")
    {
        var fields = new List<string>();
        if (code is not null) fields.Add($"\"product_code\": {code}");
        if (quantity is not null) fields.Add($"\"quantity\": {quantity}");
        return "{" + string.Join(", ", fields) + "}";
    }

    private static void AssertOnlyError(OrderParseResult result, string expected)
    {
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Is.EqualTo(new[] { expected }));
    }

    // ---- Whole body ----

    [Test]
    public void ValidOrderFromTheSpecExample()
    {
        var result = Parse("""
            {
              "order_id": "ORD-EXAMPLE",
              "customer_zip": "10015",
              "mail_order": false,
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1 },
                { "product_code": "OX-PORT-024", "quantity": 1 }
              ]
            }
            """);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
        var order = result.Order!;
        Assert.Multiple(() =>
        {
            Assert.That(order.OrderId, Is.EqualTo("ORD-EXAMPLE"));
            Assert.That(order.CustomerZip, Is.EqualTo("10015"));
            Assert.That(order.MailOrder, Is.False);
            Assert.That(order.Lines.Select(l => (l.Position, l.ProductCode, l.Quantity)),
                Is.EqualTo(new[] { (1, "WC-STD-001", 1), (2, "OX-PORT-024", 1) }));
        });
    }

    [Test]
    public void SampleOrderWithExtraFieldsIsValid()
    {
        var result = Parse("""
            { "order_id": "ORD-003", "customer_zip": "02130", "mail_order": true,
              "items": [ { "product_code": "CP-STD-031", "quantity": 1, "note": "x" } ],
              "priority": "standard", "notes": "Respiratory-focused order" }
            """);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Order!.CustomerZip, Is.EqualTo("02130"));
        Assert.That(result.Order.MailOrder, Is.True);
    }

    [TestCase("", TestName = "Empty body")]
    [TestCase("   ", TestName = "Whitespace body")]
    [TestCase("{", TestName = "Truncated JSON")]
    [TestCase("{ customer_zip: '10015' }", TestName = "Not JSON (unquoted names, single quotes)")]
    [TestCase("{ \"customer_zip\": \"10015\", }", TestName = "Trailing comma")]
    [TestCase("[]", TestName = "Array")]
    [TestCase("""[{ "customer_zip": "10015" }]""", TestName = "Array of orders (sample_orders.json format)")]
    [TestCase("\"order\"", TestName = "String")]
    [TestCase("42", TestName = "Number")]
    [TestCase("null", TestName = "JSON null")]
    [TestCase("true", TestName = "JSON boolean")]
    [TestCase("""{ "customer_zip": "10015", "customer_zip": "10016" }""", TestName = "Duplicate property")]
    public void BodyThatIsNotAJsonObjectIsRejected(string body) =>
        AssertOnlyError(Parse(body), "Request body must be a JSON object.");

    [Test]
    public void NullBodyIsRejected() =>
        AssertOnlyError(OrderRequestParser.Parse(null), "Request body must be a JSON object.");

    [Test]
    public void EmptyObjectReportsBothSpecErrorsInTheSpecOrder()
    {
        var result = Parse("{}");

        Assert.That(result.Errors, Is.EqualTo(new[]
        {
            "Order must include at least one line item.",
            "Order must include a valid customer_zip.",
        }));
    }

    [Test]
    public void AllErrorsAreCollectedTogether()
    {
        var result = Parse(Body(zip: "\"123\"", mailOrder: "\"yes\"",
            items: $"[{Item(code: null)}, 5, {Item(quantity: "0")}]"));

        Assert.That(result.Errors, Is.EqualTo(new[]
        {
            "Item 1 must include a valid product_code.",
            "Item 2 must be an object.",
            "Item 3 must include a quantity that is a positive integer.",
            "Order must include a valid customer_zip.",
            "Order must include a valid mail_order flag.",
        }));
    }

    [Test]
    public void PropertyNamesAreMatchedExactly()
    {
        var result = Parse("""{ "Customer_Zip": "10015", "Items": [] }""");

        Assert.That(result.Errors, Is.EqualTo(new[]
        {
            "Order must include at least one line item.",
            "Order must include a valid customer_zip.",
        }));
    }

    // ---- order_id (optional, not validated) ----

    [TestCase("\"ORD-1\"", "ORD-1", TestName = "String")]
    [TestCase("\"\"", "", TestName = "Empty string")]
    [TestCase("123", null, TestName = "Number is ignored")]
    [TestCase("null", null, TestName = "Null is ignored")]
    [TestCase("{}", null, TestName = "Object is ignored")]
    public void OrderIdNeverCausesAnError(string raw, string? expected)
    {
        var result = Parse(Body(orderId: raw));

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Order!.OrderId, Is.EqualTo(expected));
    }

    [Test]
    public void MissingOrderIdIsAllowed() =>
        Assert.That(Parse(Body(orderId: null)).Order!.OrderId, Is.Null);

    // ---- customer_zip ----

    [TestCase("10015")]
    [TestCase("02130")]
    [TestCase("00001", TestName = "Test ZIP 00001")]
    [TestCase("00000")]
    [TestCase("99999")]
    public void ValidZips(string zip) =>
        Assert.That(Parse(Body(zip: $"\"{zip}\"")).Order!.CustomerZip, Is.EqualTo(zip));

    [TestCase(null, TestName = "Missing")]
    [TestCase("null", TestName = "Null")]
    [TestCase("\"\"", TestName = "Empty string")]
    [TestCase("\"2130\"", TestName = "4 digits")]
    [TestCase("\"100150\"", TestName = "6 digits")]
    [TestCase("\"1001A\"", TestName = "Letter")]
    [TestCase("\"02130-1234\"", TestName = "ZIP+4")]
    [TestCase("\" 10015\"", TestName = "Leading space")]
    [TestCase("\"10015 \"", TestName = "Trailing space")]
    [TestCase("\"１００１５\"", TestName = "Full-width digits")]
    [TestCase("\"١٠٠١٥\"", TestName = "Arabic-Indic digits")]
    [TestCase("true", TestName = "Boolean")]
    [TestCase("[\"10015\"]", TestName = "Array")]
    [TestCase("{}", TestName = "Object")]
    [TestCase("100000", TestName = "Number with 6 digits")]
    [TestCase("123456789012", TestName = "Number too large for an int")]
    [TestCase("-1", TestName = "Negative number")]
    [TestCase("-0", TestName = "Negative zero")]
    [TestCase("10015.0", TestName = "Decimal number")]
    [TestCase("1.0015e4", TestName = "Exponent")]
    [TestCase("1e4", TestName = "Whole-number exponent")]
    public void InvalidZips(string? raw) =>
        AssertOnlyError(Parse(Body(zip: raw)), "Order must include a valid customer_zip.");

    [TestCase("10015", "10015", TestName = "Number with 5 digits")]
    [TestCase("2130", "02130", TestName = "Number that lost its leading zero is padded")]
    [TestCase("501", "00501", TestName = "Number that lost two leading zeros is padded")]
    [TestCase("1", "00001", TestName = "Test ZIP 00001 as a number")]
    [TestCase("0", "00000", TestName = "Zero")]
    [TestCase("99999", "99999", TestName = "Largest ZIP")]
    public void NumericZipsArePaddedToFiveDigits(string raw, string expected)
    {
        var result = Parse(Body(zip: raw));

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Order!.CustomerZip, Is.EqualTo(expected));
    }

    [Test]
    public void StringZipIsNotPadded() =>
        AssertOnlyError(Parse(Body(zip: "\"2130\"")), "Order must include a valid customer_zip.");

    // ---- mail_order ----

    [TestCase("true", true)]
    [TestCase("false", false)]
    public void ValidMailOrder(string raw, bool expected) =>
        Assert.That(Parse(Body(mailOrder: raw)).Order!.MailOrder, Is.EqualTo(expected));

    [TestCase(null, TestName = "Missing")]
    [TestCase("null", TestName = "Null")]
    public void MissingOrNullMailOrderDefaultsToFalse(string? raw)
    {
        var result = Parse(Body(mailOrder: raw));

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Order!.MailOrder, Is.False);
    }

    [TestCase("\"true\"")]
    [TestCase("\"false\"")]
    [TestCase("\"yes\"")]
    [TestCase("\"y\"")]
    [TestCase("1")]
    [TestCase("0")]
    [TestCase("{}")]
    public void InvalidMailOrder(string raw) =>
        AssertOnlyError(Parse(Body(mailOrder: raw)), "Order must include a valid mail_order flag.");

    // ---- items ----

    [TestCase(null, TestName = "Missing")]
    [TestCase("null", TestName = "Null")]
    [TestCase("[]", TestName = "Empty array")]
    [TestCase("{}", TestName = "Object")]
    [TestCase("\"WC-STD-001\"", TestName = "String")]
    [TestCase("1", TestName = "Number")]
    public void MissingOrEmptyItems(string? raw) =>
        AssertOnlyError(Parse(Body(items: raw)), "Order must include at least one line item.");

    [TestCase("5")]
    [TestCase("\"WC-STD-001\"")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("true")]
    public void ItemThatIsNotAnObject(string raw) =>
        AssertOnlyError(Parse(Body(items: $"[{Item()}, {raw}]")), "Item 2 must be an object.");

    [Test]
    public void DuplicateLinesAreKeptAsSeparateLines()
    {
        var result = Parse(Body(items: $"[{Item()}, {Item(quantity: "2")}]"));

        Assert.That(result.Order!.Lines.Select(l => (l.Position, l.Quantity)), Is.EqualTo(new[] { (1, 1), (2, 2) }));
    }

    // ---- items[].product_code ----

    [TestCase("\"WC-STD-001\"", "WC-STD-001")]
    [TestCase("\"  WC-STD-001  \"", "WC-STD-001", TestName = "Surrounding spaces are trimmed")]
    [TestCase("\"wc-std-001\"", "wc-std-001", TestName = "Case is kept; lookup ignores case")]
    [TestCase("\"NOT-A-REAL-CODE\"", "NOT-A-REAL-CODE", TestName = "Unknown code is checked during routing")]
    public void ValidProductCodes(string raw, string expected) =>
        Assert.That(Parse(Body(items: $"[{Item(code: raw)}]")).Order!.Lines[0].ProductCode, Is.EqualTo(expected));

    [TestCase(null, TestName = "Missing")]
    [TestCase("null", TestName = "Null")]
    [TestCase("\"\"", TestName = "Empty")]
    [TestCase("\"   \"", TestName = "Whitespace")]
    [TestCase("123", TestName = "Number")]
    [TestCase("true", TestName = "Boolean")]
    [TestCase("[\"WC-STD-001\"]", TestName = "Array")]
    public void InvalidProductCodes(string? raw) =>
        AssertOnlyError(Parse(Body(items: $"[{Item(code: raw)}]")), "Item 1 must include a valid product_code.");

    // ---- items[].quantity ----

    [TestCase("1", 1)]
    [TestCase("2", 2)]
    [TestCase("1000", 1000)]
    [TestCase("2147483647", int.MaxValue)]
    public void ValidQuantities(string raw, int expected) =>
        Assert.That(Parse(Body(items: $"[{Item(quantity: raw)}]")).Order!.Lines[0].Quantity, Is.EqualTo(expected));

    [TestCase(null, TestName = "Missing")]
    [TestCase("null", TestName = "Null")]
    [TestCase("0", TestName = "Zero")]
    [TestCase("-1", TestName = "Negative")]
    [TestCase("-0", TestName = "Negative zero")]
    [TestCase("1.0", TestName = "Decimal that is whole")]
    [TestCase("1.5", TestName = "Fraction")]
    [TestCase("1e0", TestName = "Exponent")]
    [TestCase("1E2", TestName = "Uppercase exponent")]
    [TestCase("2147483648", TestName = "Larger than int.MaxValue")]
    [TestCase("\"1\"", TestName = "String")]
    [TestCase("true", TestName = "Boolean true is not 1")]
    [TestCase("false", TestName = "Boolean false")]
    [TestCase("[1]", TestName = "Array")]
    public void InvalidQuantities(string? raw) =>
        AssertOnlyError(Parse(Body(items: $"[{Item(quantity: raw)}]")),
            "Item 1 must include a quantity that is a positive integer.");

    [Test]
    public void ItemWithBadCodeAndQuantityReportsBoth()
    {
        var result = Parse(Body(items: $"[{Item(code: "\"\"", quantity: "0")}]"));

        Assert.That(result.Errors, Is.EqualTo(new[]
        {
            "Item 1 must include a valid product_code.",
            "Item 1 must include a quantity that is a positive integer.",
        }));
    }
}

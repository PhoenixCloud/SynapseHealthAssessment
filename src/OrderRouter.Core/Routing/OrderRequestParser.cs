using System.Text.Json;
using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Routing;

/// <param name="Order">The validated order, or null when there are errors.</param>
/// <param name="Errors">Every validation error found; empty when valid.</param>
public sealed record OrderParseResult(Order? Order, IReadOnlyList<string> Errors)
{
    public bool IsValid => Order is not null;
}

/// <summary>
/// Validates a raw request body and builds an <see cref="Order"/>. Fields are checked by hand
/// from a <see cref="JsonDocument"/> so that type mismatches become collected errors instead of
/// exceptions. All rules are in README section 2. Property names are matched exactly as the spec
/// writes them; unknown properties are ignored.
/// </summary>
public static class OrderRequestParser
{
    private static readonly JsonDocumentOptions Options = new()
    {
        // A repeated property would force a guess about which value to use, so reject it.
        AllowDuplicateProperties = false,
    };

    public static OrderParseResult Parse(string? body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body ?? string.Empty, Options);
        }
        catch (JsonException)
        {
            return Invalid(RoutingErrors.InvalidBody);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid(RoutingErrors.InvalidBody);
            }

            var errors = new List<string>();

            // Order of checks matches the spec's example: items first, then customer_zip.
            var lines = ParseItems(root, errors);
            var zip = ParseCustomerZip(root, errors);
            var mailOrder = ParseMailOrder(root, errors);
            var orderId = root.TryGetProperty("order_id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;

            if (errors.Count > 0) return new OrderParseResult(null, errors);

            return new OrderParseResult(new Order(orderId, zip!, mailOrder, lines!), []);
        }
    }

    private static OrderParseResult Invalid(string error) => new(null, [error]);

    private static List<OrderLine>? ParseItems(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() == 0)
        {
            errors.Add(RoutingErrors.NoLineItems);
            return null;
        }

        var lines = new List<OrderLine>();
        var position = 0;
        var itemErrors = false;

        foreach (var item in items.EnumerateArray())
        {
            position++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(RoutingErrors.ItemNotObject(position));
                itemErrors = true;
                continue;
            }

            var code = item.TryGetProperty("product_code", out var codeElement)
                       && codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()!.Trim()
                : string.Empty;

            if (code.Length == 0)
            {
                errors.Add(RoutingErrors.InvalidProductCode(position));
                itemErrors = true;
            }

            if (!item.TryGetProperty("quantity", out var quantityElement)
                || !TryGetQuantity(quantityElement, out var quantity))
            {
                errors.Add(RoutingErrors.InvalidQuantity(position));
                itemErrors = true;
                continue;
            }

            if (code.Length > 0) lines.Add(new OrderLine(position, code, quantity));
        }

        return itemErrors ? null : lines;
    }

    /// <summary>
    /// A JSON integer literal from 1 to int.MaxValue. Rejects booleans, strings, null, decimals
    /// (1.0, 1.5) and exponents (1e0), even when the value is a whole number.
    /// </summary>
    private static bool TryGetQuantity(JsonElement element, out int quantity)
    {
        quantity = 0;
        if (element.ValueKind != JsonValueKind.Number) return false;

        var raw = element.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0) return false;

        return element.TryGetInt32(out quantity) && quantity >= 1;
    }

    /// <summary>
    /// A string of exactly 5 ASCII digits, or a JSON integer from 0 to 99999. A number is padded
    /// to 5 digits (2130 becomes "02130"), because a number can't keep a leading zero. This is
    /// the same rule used for supplier ZIPs. Strings are not padded: a string can keep its
    /// leading zero, so "2130" is more likely a mistake.
    /// </summary>
    private static string? ParseCustomerZip(JsonElement root, List<string> errors)
    {
        if (root.TryGetProperty("customer_zip", out var zip))
        {
            if (zip.ValueKind == JsonValueKind.String && Order.IsValidZip(zip.GetString()))
            {
                return zip.GetString();
            }

            if (zip.ValueKind == JsonValueKind.Number
                && zip.GetRawText().All(char.IsAsciiDigit)
                && zip.TryGetInt32(out var number)
                && number is >= 0 and <= 99999)
            {
                return number.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        errors.Add(RoutingErrors.InvalidCustomerZip);
        return null;
    }

    /// <summary>Missing or null means false (README A4). Other values must be a JSON boolean.</summary>
    private static bool ParseMailOrder(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("mail_order", out var mailOrder)) return false;

        switch (mailOrder.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return false;
            default:
                errors.Add(RoutingErrors.InvalidMailOrder);
                return false;
        }
    }
}

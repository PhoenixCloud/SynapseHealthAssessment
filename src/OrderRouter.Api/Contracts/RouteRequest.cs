using System.Text.Json.Serialization;

namespace OrderRouter.Api.Contracts;

/// <summary>
/// Documents the request body in Swagger only. The endpoint never binds to this type: it reads
/// the raw body and validates it by hand, so malformed input becomes an error message in a 200
/// response instead of a framework 400 or 415.
/// </summary>
public sealed class RouteRequest
{
    /// <summary>Optional. Not used for routing.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>Required. Exactly 5 digits, as a string (e.g. "02130").</summary>
    [JsonPropertyName("customer_zip")]
    public required string CustomerZip { get; init; }

    /// <summary>Optional, defaults to false. When true, mail-order suppliers may be used.</summary>
    [JsonPropertyName("mail_order")]
    public bool? MailOrder { get; init; }

    /// <summary>Required. At least one item.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<RouteRequestItem> Items { get; init; }
}

public sealed class RouteRequestItem
{
    /// <summary>Required. A code from products.csv (case-insensitive).</summary>
    [JsonPropertyName("product_code")]
    public required string ProductCode { get; init; }

    /// <summary>Required. Whole number of at least 1.</summary>
    [JsonPropertyName("quantity")]
    public required int Quantity { get; init; }
}

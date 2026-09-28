using System.Text.Json.Serialization;
using OrderRouter.Core.Domain;

namespace OrderRouter.Api.Contracts;

/// <summary>
/// The response body of POST /api/route, exactly as the spec's examples define it.
/// A success has "feasible" and "routing"; a failure has "feasible" and "errors".
/// The HTTP status is always 200.
/// </summary>
public sealed class RoutingResult
{
    [JsonPropertyName("feasible")]
    [JsonPropertyOrder(0)]
    public required bool Feasible { get; init; }

    [JsonPropertyName("routing")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SupplierRouting>? Routing { get; init; }

    [JsonPropertyName("errors")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Errors { get; init; }

    public static RoutingResult Failure(IEnumerable<string> errors) => new()
    {
        Feasible = false,
        Errors = errors.ToList(),
    };

    public static RoutingResult From(RoutingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!decision.Feasible) return Failure(decision.Errors);

        return new RoutingResult
        {
            Feasible = true,
            Routing = decision.Shipments.Select(shipment => new SupplierRouting
            {
                SupplierId = shipment.SupplierId,
                SupplierName = shipment.SupplierName,
                Items = shipment.Items.Select(item => new RoutedItemResult
                {
                    ProductCode = item.ProductCode,
                    Quantity = item.Quantity,
                    Category = item.Category,
                    FulfillmentMode = FulfillmentModes.ToContract(item.FulfillmentMode),
                }).ToList(),
            }).ToList(),
        };
    }
}

/// <summary>One entry in "routing": a supplier and the items it ships.</summary>
public sealed class SupplierRouting
{
    [JsonPropertyName("supplier_id")]
    public required string SupplierId { get; init; }

    [JsonPropertyName("supplier_name")]
    public required string SupplierName { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<RoutedItemResult> Items { get; init; }
}

/// <summary>One routed line item.</summary>
public sealed class RoutedItemResult
{
    [JsonPropertyName("product_code")]
    public required string ProductCode { get; init; }

    [JsonPropertyName("quantity")]
    public required int Quantity { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>"local" or "mail_order".</summary>
    [JsonPropertyName("fulfillment_mode")]
    public required string FulfillmentMode { get; init; }
}

public static class FulfillmentModes
{
    public const string Local = "local";
    public const string MailOrder = "mail_order";

    public static string ToContract(FulfillmentMode mode) => mode switch
    {
        Core.Domain.FulfillmentMode.Local => Local,
        Core.Domain.FulfillmentMode.MailOrder => MailOrder,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}

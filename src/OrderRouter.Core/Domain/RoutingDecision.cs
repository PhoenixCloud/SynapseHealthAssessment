namespace OrderRouter.Core.Domain;

public enum FulfillmentMode
{
    Local,
    MailOrder,
}

/// <param name="ProductCode">The catalog's product code.</param>
/// <param name="Quantity">Quantity as requested.</param>
/// <param name="Category">Category as written in products.csv.</param>
/// <param name="FulfillmentMode">Local when the supplier serves the customer's ZIP, otherwise mail order.</param>
public sealed record RoutedItem(string ProductCode, int Quantity, string Category, FulfillmentMode FulfillmentMode);

/// <summary>Everything one supplier ships for the order.</summary>
public sealed record Shipment(string SupplierId, string SupplierName, IReadOnlyList<RoutedItem> Items);

/// <summary>
/// The routing outcome in domain terms. Either feasible with shipments, or not feasible with
/// errors, never both.
/// </summary>
public sealed class RoutingDecision
{
    private RoutingDecision(bool feasible, IReadOnlyList<Shipment> shipments, IReadOnlyList<string> errors)
    {
        Feasible = feasible;
        Shipments = shipments;
        Errors = errors;
    }

    public bool Feasible { get; }

    /// <summary>Empty when not feasible.</summary>
    public IReadOnlyList<Shipment> Shipments { get; }

    /// <summary>Empty when feasible.</summary>
    public IReadOnlyList<string> Errors { get; }

    public static RoutingDecision Success(IEnumerable<Shipment> shipments)
    {
        var list = shipments.ToList();
        if (list.Count == 0) throw new ArgumentException("A successful decision needs at least one shipment.", nameof(shipments));
        return new RoutingDecision(true, list.AsReadOnly(), []);
    }

    public static RoutingDecision Failure(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0) throw new ArgumentException("A failed decision needs at least one error.", nameof(errors));
        return new RoutingDecision(false, [], list.AsReadOnly());
    }
}

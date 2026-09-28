namespace OrderRouter.Core.Routing;

/// <summary>Every error message the service returns. See README section 2.</summary>
public static class RoutingErrors
{
    /// <summary>Word for word from the spec.</summary>
    public const string NoLineItems = "Order must include at least one line item.";

    /// <summary>Word for word from the spec.</summary>
    public const string InvalidCustomerZip = "Order must include a valid customer_zip.";

    public const string InvalidBody = "Request body must be a JSON object.";
    public const string UnreadableBody = "Request body could not be read.";
    public const string BodyTooLarge = "Request body is too large.";
    public const string InvalidMailOrder = "Order must include a valid mail_order flag.";
    public const string InternalError = "Internal error while routing order.";
    public const string UsePost = "Use POST to route an order.";

    public static string ItemNotObject(int position) => $"Item {position} must be an object.";

    public static string InvalidProductCode(int position) => $"Item {position} must include a valid product_code.";

    public static string InvalidQuantity(int position) =>
        $"Item {position} must include a quantity that is a positive integer.";

    public static string UnknownProduct(string code) => $"Unknown product_code: {code}.";

    public static string NoEligibleSupplier(string code, string category) =>
        $"No eligible supplier for product_code {code} (category: {category}).";
}

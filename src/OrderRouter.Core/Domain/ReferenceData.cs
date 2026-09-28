using OrderRouter.Core.Data;

namespace OrderRouter.Core.Domain;

/// <summary>Suppliers and products loaded once at startup and shared read-only.</summary>
public sealed class ReferenceData
{
    private readonly Dictionary<string, Product> _products;

    public ReferenceData(IEnumerable<Product> products, IEnumerable<Supplier> suppliers)
    {
        _products = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            if (!_products.TryAdd(product.Code, product))
            {
                throw new ArgumentException($"Duplicate product code '{product.Code}'.", nameof(products));
            }
        }

        Suppliers = suppliers.ToList().AsReadOnly();
    }

    public IReadOnlyDictionary<string, Product> Products => _products;

    public IReadOnlyList<Supplier> Suppliers { get; }

    /// <summary>Looks up a product, ignoring case and surrounding whitespace.</summary>
    public bool TryGetProduct(string code, out Product product)
    {
        if (_products.TryGetValue(Normalize.ProductCode(code), out var found))
        {
            product = found;
            return true;
        }

        product = null!;
        return false;
    }
}

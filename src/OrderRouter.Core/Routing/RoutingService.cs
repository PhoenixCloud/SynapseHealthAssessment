using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Routing;

/// <summary>
/// Routes a validated order to suppliers, applying the spec's priorities in order:
/// feasibility, fewest shipments, quality, then geographic preference (README section 3 and
/// the design's routing steps). Stateless and thread-safe.
/// </summary>
public class RoutingService
{
    /// <summary>Categories are tracked as bits in a 64-bit mask.</summary>
    public const int MaxDistinctCategoriesPerOrder = 64;

    private readonly ReferenceData _data;
    private readonly bool _pruneCandidates;

    public RoutingService(ReferenceData data) : this(data, pruneCandidates: true)
    {
    }

    /// <summary>Tests turn pruning off to check it never changes the result.</summary>
    internal RoutingService(ReferenceData data, bool pruneCandidates)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _pruneCandidates = pruneCandidates;
    }

    /// <summary>Virtual so API tests can substitute a failing service.</summary>
    public virtual RoutingDecision Route(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Step 1: look up products.
        var products = order.Lines
            .Select(line => _data.TryGetProduct(line.ProductCode, out var product) ? product : null)
            .ToList();

        var categoryBits = BuildCategoryBits(products);

        // Step 2: find every supplier that can fulfill at least one category (feasibility).
        var candidates = FindCandidates(order, categoryBits);
        var covered = candidates.Aggregate(0UL, (mask, c) => mask | c.Mask);

        // Step 3: stop on errors, reported in line order, each distinct message once.
        // A set tracks what's already listed, so huge orders full of errors stay linear.
        var errors = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < order.Lines.Count; i++)
        {
            var product = products[i];
            var error = product is null
                ? RoutingErrors.UnknownProduct(order.Lines[i].ProductCode)
                : (covered & categoryBits[product.CategoryKey]) == 0
                    ? RoutingErrors.NoEligibleSupplier(product.Code, product.Category)
                    : null;

            if (error is not null && listed.Add(error)) errors.Add(error);
        }

        if (errors.Count > 0) return RoutingDecision.Failure(errors);

        // Steps 4-6: narrow the candidates, find the fewest shipments, pick the best plan.
        var pool = _pruneCandidates ? RemoveDominated(candidates) : candidates;
        var plan = FindBestPlan(pool, covered, categoryBits.Count);

        // Step 7: assign lines and build shipments.
        return RoutingDecision.Success(BuildShipments(order, products!, plan, categoryBits));
    }

    private sealed record Candidate(Supplier Supplier, ulong Mask, bool IsLocal)
    {
        public decimal Rating => Supplier.EffectiveRating;
        public string Id => Supplier.Id;
    }

    /// <summary>One bit per distinct category in the order, in order of first appearance.</summary>
    private static Dictionary<string, ulong> BuildCategoryBits(List<Product?> products)
    {
        var keys = products.OfType<Product>().Select(p => p.CategoryKey).Distinct().ToList();
        if (keys.Count > MaxDistinctCategoriesPerOrder)
        {
            throw new InvalidOperationException(
                $"Orders with more than {MaxDistinctCategoriesPerOrder} distinct categories are not supported.");
        }

        return keys.Select((key, index) => (key, bit: 1UL << index)).ToDictionary(k => k.key, k => k.bit);
    }

    /// <summary>
    /// A supplier is a candidate when it handles at least one of the order's categories and can
    /// reach the customer: it serves the ZIP (local), or the order allows mail order and the
    /// supplier can mail.
    /// </summary>
    private List<Candidate> FindCandidates(Order order, Dictionary<string, ulong> categoryBits)
    {
        var candidates = new List<Candidate>();
        if (categoryBits.Count == 0) return candidates;

        var zip = order.Zip;
        foreach (var supplier in _data.Suppliers)
        {
            var isLocal = supplier.Serves(zip);
            if (!isLocal && !(order.MailOrder && supplier.CanMailOrder)) continue;

            var mask = 0UL;
            foreach (var (key, bit) in categoryBits)
            {
                if (supplier.Handles(key)) mask |= bit;
            }

            if (mask != 0) candidates.Add(new Candidate(supplier, mask, isLocal));
        }

        return candidates;
    }

    /// <summary>
    /// Removes every candidate that another candidate dominates. Swapping a dominated supplier
    /// for its dominator never makes a smallest plan worse, so the winner is unchanged.
    /// </summary>
    private static List<Candidate> RemoveDominated(List<Candidate> candidates) =>
        candidates.Where(a => !candidates.Any(b => Dominates(b, a))).ToList();

    private static bool Dominates(Candidate b, Candidate a)
    {
        if (ReferenceEquals(a, b)) return false;

        var coversAtLeast = (a.Mask & ~b.Mask) == 0;
        var ratedAtLeast = b.Rating >= a.Rating;
        var localAtLeast = b.IsLocal || !a.IsLocal;
        if (!coversAtLeast || !ratedAtLeast || !localAtLeast) return false;

        var strictlyBetter = b.Rating > a.Rating || (b.IsLocal && !a.IsLocal);
        return strictlyBetter || SupplierIdComparer.Instance.Compare(b.Id, a.Id) < 0;
    }

    /// <summary>
    /// Tries plan sizes 1, 2, 3, ... and stops at the first size with any covering plan
    /// (fewest shipments). Among those plans, applies the ranking rule (quality, then local).
    /// </summary>
    private static Candidate[] FindBestPlan(List<Candidate> pool, ulong fullMask, int categoryCount)
    {
        var maxSize = Math.Min(pool.Count, categoryCount);

        for (var size = 1; size <= maxSize; size++)
        {
            var plans = new List<Candidate[]>();
            CollectCoveringPlans(pool, size, fullMask, 0, 0UL, new Candidate[size], 0, plans);

            if (plans.Count > 0)
            {
                return SupplierRanking.PickBest(plans.Select(plan => new RankedOption<Candidate[]>(
                    plan,
                    plan.Sum(c => c.Rating) / plan.Length,
                    plan.Count(c => c.IsLocal),
                    plan.Select(c => c.Id).ToList())));
            }
        }

        // Unreachable: every category was checked as covered before searching.
        throw new InvalidOperationException("No covering plan found for a feasible order.");
    }

    private static void CollectCoveringPlans(
        List<Candidate> pool, int size, ulong fullMask,
        int start, ulong mask, Candidate[] current, int depth, List<Candidate[]> plans)
    {
        if (depth == size)
        {
            if (mask == fullMask) plans.Add((Candidate[])current.Clone());
            return;
        }

        var remainingSlots = size - depth;
        for (var i = start; i <= pool.Count - remainingSlots; i++)
        {
            current[depth] = pool[i];
            CollectCoveringPlans(pool, size, fullMask, i + 1, mask | pool[i].Mask, current, depth + 1, plans);
        }
    }

    /// <summary>
    /// Each line goes to the best supplier in the plan that handles its category. Shipments are
    /// ordered by each supplier's first line, and lines keep request order.
    /// </summary>
    private static List<Shipment> BuildShipments(
        Order order, List<Product> products, Candidate[] plan, Dictionary<string, ulong> categoryBits)
    {
        var bySupplier = new Dictionary<string, (Candidate Candidate, List<RoutedItem> Items)>();
        var supplierOrder = new List<string>();

        for (var i = 0; i < order.Lines.Count; i++)
        {
            var product = products[i];
            var bit = categoryBits[product.CategoryKey];

            var chosen = SupplierRanking.PickBest(plan
                .Where(c => (c.Mask & bit) != 0)
                .Select(c => new RankedOption<Candidate>(c, c.Rating, c.IsLocal ? 1 : 0, [c.Id])));

            if (!bySupplier.TryGetValue(chosen.Id, out var entry))
            {
                entry = (chosen, []);
                bySupplier[chosen.Id] = entry;
                supplierOrder.Add(chosen.Id);
            }

            var mode = chosen.IsLocal ? FulfillmentMode.Local : FulfillmentMode.MailOrder;
            entry.Items.Add(new RoutedItem(product.Code, order.Lines[i].Quantity, product.Category, mode));
        }

        return supplierOrder
            .Select(id => new Shipment(id, bySupplier[id].Candidate.Supplier.Name, bySupplier[id].Items))
            .ToList();
    }
}

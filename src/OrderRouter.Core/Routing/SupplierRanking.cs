using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Routing;

/// <summary>An option being ranked: a single supplier, or a whole plan of suppliers.</summary>
/// <param name="Value">The supplier or plan.</param>
/// <param name="Rating">The supplier's rating, or the plan's average rating per supplier.</param>
/// <param name="LocalCount">Number of local suppliers (0 or 1 for a single supplier).</param>
/// <param name="SupplierIds">IDs of the suppliers involved, used only as the final tie-breaker.</param>
public sealed record RankedOption<T>(T Value, decimal Rating, int LocalCount, IReadOnlyList<string> SupplierIds);

/// <summary>
/// The single ranking rule used for both plans and suppliers (README section 3):
/// keep options within <see cref="RoutingRules.SimilarityBand"/> of the best rating,
/// then more local suppliers, then higher rating, then supplier IDs.
/// </summary>
public static class SupplierRanking
{
    public static T PickBest<T>(IEnumerable<RankedOption<T>> options)
    {
        var list = options.ToList();
        if (list.Count == 0) throw new ArgumentException("There must be at least one option.", nameof(options));

        var best = list.Max(o => o.Rating);

        return list
            .Where(o => best - o.Rating <= RoutingRules.SimilarityBand)
            .OrderByDescending(o => o.LocalCount)
            .ThenByDescending(o => o.Rating)
            .ThenBy(o => o.SupplierIds, SupplierIdListComparer.Instance)
            .First()
            .Value;
    }

    /// <summary>Sorts each list by <see cref="SupplierIdComparer"/>, then compares position by position.</summary>
    private sealed class SupplierIdListComparer : IComparer<IReadOnlyList<string>>
    {
        public static SupplierIdListComparer Instance { get; } = new();

        public int Compare(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            var left = (x ?? []).Order(SupplierIdComparer.Instance).ToList();
            var right = (y ?? []).Order(SupplierIdComparer.Instance).ToList();

            for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
            {
                var result = SupplierIdComparer.Instance.Compare(left[i], right[i]);
                if (result != 0) return result;
            }

            return left.Count.CompareTo(right.Count);
        }
    }
}

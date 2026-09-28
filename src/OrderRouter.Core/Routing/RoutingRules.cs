namespace OrderRouter.Core.Routing;

/// <summary>Fixed routing constants. See README "Implementation Assumptions" A2.</summary>
public static class RoutingRules
{
    /// <summary>Ratings within this distance of the best rating count as similar.</summary>
    public const decimal SimilarityBand = 1.0m;
}

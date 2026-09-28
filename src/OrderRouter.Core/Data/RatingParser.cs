using System.Globalization;
using OrderRouter.Core.Domain;

namespace OrderRouter.Core.Data;

/// <param name="Rating">The score, or null when unrated.</param>
/// <param name="Problem">Set when the value was unusable and treated as unrated.</param>
public sealed record RatingParseResult(decimal? Rating, string? Problem);

/// <summary>
/// Parses customer_satisfaction_score. "no ratings yet" (any case or spacing) means unrated.
/// Blank, non-numeric or out-of-range values are treated as unrated with a problem message,
/// because a bad score should only affect ranking, never whether an order can be routed.
/// </summary>
public static class RatingParser
{
    public const decimal MinScore = Supplier.MinRating;
    public const decimal MaxScore = Supplier.MaxRating;
    public const string NoRatingsYet = "no ratings yet";

    public static RatingParseResult Parse(string? raw)
    {
        var value = Normalize.CollapseWhitespace(raw);

        if (value.Equals(NoRatingsYet, StringComparison.OrdinalIgnoreCase))
        {
            return new RatingParseResult(null, null);
        }

        if (value.Length == 0)
        {
            return new RatingParseResult(null, "Blank customer_satisfaction_score; treated as unrated.");
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var score))
        {
            return new RatingParseResult(null,
                $"customer_satisfaction_score '{value}' is not a number; treated as unrated.");
        }

        if (score < MinScore || score > MaxScore)
        {
            return new RatingParseResult(null,
                $"customer_satisfaction_score '{value}' is outside {MinScore}-{MaxScore}; treated as unrated.");
        }

        return new RatingParseResult(score, null);
    }
}

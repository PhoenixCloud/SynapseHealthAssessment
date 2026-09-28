namespace OrderRouter.Core.Domain;

/// <param name="Id">Supplier ID as written (trimmed). Must not be blank.</param>
/// <param name="Name">Supplier name as written (trimmed). Must not be blank.</param>
/// <param name="ServiceArea">ZIP codes served locally. May be empty (mail order only).</param>
/// <param name="CategoryKeys">Normalized categories the supplier handles. At least one.</param>
/// <param name="Rating">Customer satisfaction score from 1 to 10, or null when unrated.</param>
/// <param name="CanMailOrder">Whether the supplier ships nationally.</param>
public sealed record Supplier(
    string Id,
    string Name,
    ZipCoverage ServiceArea,
    IReadOnlySet<string> CategoryKeys,
    decimal? Rating,
    bool CanMailOrder)
{
    public const decimal MinRating = 1m;
    public const decimal MaxRating = 10m;

    /// <summary>Score used for suppliers with "no ratings yet": the middle of the 1–10 scale.</summary>
    public const decimal UnratedScore = 5.5m;

    public string Id { get; } = Guard.NotBlank(Id, nameof(Id));

    public string Name { get; } = Guard.NotBlank(Name, nameof(Name));

    public ZipCoverage ServiceArea { get; } =
        ServiceArea ?? throw new ArgumentNullException(nameof(ServiceArea));

    public IReadOnlySet<string> CategoryKeys { get; } = ValidCategories(CategoryKeys);

    public decimal? Rating { get; } = ValidRating(Rating);

    public bool IsRated => Rating.HasValue;

    /// <summary>The rating used for ranking: the score, or the middle score when unrated.</summary>
    public decimal EffectiveRating => Rating ?? UnratedScore;

    public bool Serves(int zip) => ServiceArea.Covers(zip);

    public bool Handles(string categoryKey) => CategoryKeys.Contains(categoryKey);

    private static IReadOnlySet<string> ValidCategories(IReadOnlySet<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories, nameof(CategoryKeys));
        if (categories.Count == 0 || categories.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A supplier needs at least one non-blank category.", nameof(CategoryKeys));
        }

        return categories;
    }

    private static decimal? ValidRating(decimal? rating)
    {
        if (rating is < MinRating or > MaxRating)
        {
            throw new ArgumentOutOfRangeException(nameof(Rating), rating,
                $"Rating must be between {MinRating} and {MaxRating}, or null when unrated.");
        }

        return rating;
    }
}

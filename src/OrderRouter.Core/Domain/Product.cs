namespace OrderRouter.Core.Domain;

/// <param name="Code">Normalized product code (trimmed, uppercase). Must not be blank.</param>
/// <param name="Name">Product name as written; may be empty.</param>
/// <param name="Category">Category as written in products.csv (trimmed, spaces collapsed); returned in responses.</param>
/// <param name="CategoryKey">Normalized category used for matching suppliers.</param>
public sealed record Product(string Code, string Name, string Category, string CategoryKey)
{
    public string Code { get; } = Guard.NotBlank(Code, nameof(Code));

    public string Name { get; } = Name ?? throw new ArgumentNullException(nameof(Name));

    public string Category { get; } = Guard.NotBlank(Category, nameof(Category));

    public string CategoryKey { get; } = Guard.NotBlank(CategoryKey, nameof(CategoryKey));
}

namespace OrderRouter.Core.Domain;

/// <param name="Position">1-based position of the line in the request's items array.</param>
/// <param name="ProductCode">Product code as sent (trimmed). Must not be blank.</param>
/// <param name="Quantity">Whole number of at least 1. Echoed back only.</param>
public sealed record OrderLine(int Position, string ProductCode, int Quantity)
{
    public int Position { get; } = Position >= 1
        ? Position
        : throw new ArgumentOutOfRangeException(nameof(Position), Position, "Position must be at least 1.");

    public string ProductCode { get; } = Guard.NotBlank(ProductCode, nameof(ProductCode));

    public int Quantity { get; } = Quantity >= 1
        ? Quantity
        : throw new ArgumentOutOfRangeException(nameof(Quantity), Quantity, "Quantity must be at least 1.");
}

/// <summary>A validated order. Only created by the request parser, or directly in tests.</summary>
/// <param name="OrderId">Optional; not used for routing.</param>
/// <param name="CustomerZip">Exactly 5 ASCII digits.</param>
/// <param name="MailOrder">Whether mail-order suppliers may be used.</param>
/// <param name="Lines">At least one line.</param>
public sealed record Order(string? OrderId, string CustomerZip, bool MailOrder, IReadOnlyList<OrderLine> Lines)
{
    public string CustomerZip { get; } = IsValidZip(CustomerZip)
        ? CustomerZip
        : throw new ArgumentException("CustomerZip must be exactly 5 digits.", nameof(CustomerZip));

    public IReadOnlyList<OrderLine> Lines { get; } = Lines is { Count: > 0 }
        ? Lines
        : throw new ArgumentException("An order needs at least one line.", nameof(Lines));

    /// <summary>The ZIP as a number, e.g. "02130" is 2130.</summary>
    public int Zip => int.Parse(CustomerZip, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True for exactly 5 ASCII digits. <c>\d</c> is avoided because it also matches non-ASCII digits.</summary>
    public static bool IsValidZip(string? zip) => zip is { Length: 5 } && zip.All(char.IsAsciiDigit);
}

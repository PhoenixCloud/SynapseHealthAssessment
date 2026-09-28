namespace OrderRouter.Core.Domain;

/// <summary>An inclusive range of 5-digit ZIP codes, stored as numbers (e.g. 02130 is 2130).</summary>
public readonly record struct ZipRange
{
    public const int MinZip = 0;
    public const int MaxZip = 99999;

    public ZipRange(int start, int end)
    {
        if (start < MinZip || end > MaxZip || start > end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start), $"Invalid ZIP range {start}-{end}.");
        }

        Start = start;
        End = end;
    }

    public int Start { get; }
    public int End { get; }

    public bool Contains(int zip) => zip >= Start && zip <= End;

    public override string ToString() =>
        Start == End ? $"{Start:D5}" : $"{Start:D5}-{End:D5}";
}

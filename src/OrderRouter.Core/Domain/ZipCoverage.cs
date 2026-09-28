namespace OrderRouter.Core.Domain;

/// <summary>
/// The set of ZIP codes a supplier serves locally, held as sorted, non-overlapping ranges.
/// </summary>
public sealed class ZipCoverage : IEquatable<ZipCoverage>
{
    private readonly ZipRange[] _ranges;

    private ZipCoverage(ZipRange[] mergedRanges) => _ranges = mergedRanges;

    public static ZipCoverage Empty { get; } = new([]);

    public IReadOnlyList<ZipRange> Ranges => _ranges;

    public bool IsEmpty => _ranges.Length == 0;

    /// <summary>Builds coverage from any ranges; overlapping and adjacent ranges are merged.</summary>
    public static ZipCoverage FromRanges(IEnumerable<ZipRange> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        var merged = new List<ZipRange>(sorted.Count);

        foreach (var range in sorted)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
            {
                var last = merged[^1];
                merged[^1] = new ZipRange(last.Start, Math.Max(last.End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged.Count == 0 ? Empty : new ZipCoverage([.. merged]);
    }

    public bool Covers(int zip)
    {
        int low = 0, high = _ranges.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            var range = _ranges[mid];
            if (zip < range.Start) high = mid - 1;
            else if (zip > range.End) low = mid + 1;
            else return true;
        }

        return false;
    }

    public bool Equals(ZipCoverage? other) =>
        other is not null && _ranges.AsSpan().SequenceEqual(other._ranges);

    public override bool Equals(object? obj) => Equals(obj as ZipCoverage);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var range in _ranges) hash.Add(range);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(", ", _ranges);
}

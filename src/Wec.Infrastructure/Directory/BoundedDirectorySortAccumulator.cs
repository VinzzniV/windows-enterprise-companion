using System.Globalization;
using Wec.Core.Abstractions;

namespace Wec.Infrastructure.Directory;

internal sealed class BoundedDirectorySortAccumulator
{
    private readonly int _offset;
    private readonly int _limit;
    private readonly int _capacity;
    private readonly Comparer<DirectoryEntryData> _order;
    private readonly PriorityQueue<DirectoryEntryData, DirectoryEntryData> _entries;
    private int _totalCount;

    internal int RetainedCount => _entries.Count;

    public BoundedDirectorySortAccumulator(DirectorySearchQuery query, int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _offset = offset;
        _limit = limit;
        _capacity = checked(offset + limit);
        if (query.MaximumSortedPageEntries is not > 0 || _capacity > query.MaximumSortedPageEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        _order = Comparer<DirectoryEntryData>.Create((left, right) => Compare(query, left, right));
        _entries = new(Comparer<DirectoryEntryData>.Create((left, right) => _order.Compare(right, left)));
    }

    public void Add(DirectoryEntryData entry)
    {
        _totalCount = checked(_totalCount + 1);
        if (_capacity == 0) { return; }
        if (_entries.Count < _capacity)
        {
            _entries.Enqueue(entry, entry);
        }
        else if (_order.Compare(entry, _entries.Peek()) < 0)
        {
            _entries.DequeueEnqueue(entry, entry);
        }
    }

    public BoundedDirectorySearchResult Build() => new(_totalCount,
        _entries.UnorderedItems.Select(item => item.Element).Order(_order).Skip(_offset).Take(_limit).ToArray());

    private static int Compare(DirectorySearchQuery query, DirectoryEntryData left, DirectoryEntryData right)
    {
        int primary = CompareValue(left.GetFirstValue(query.SortAttribute!), right.GetFirstValue(query.SortAttribute!),
            string.Equals(query.SortAttribute, "lastLogonTimestamp", StringComparison.OrdinalIgnoreCase));
        int secondary = primary != 0 ? primary : CompareValue(
            left.GetFirstValue(query.SortTieBreakerAttribute!), right.GetFirstValue(query.SortTieBreakerAttribute!), false);
        int identity = secondary != 0 ? secondary : StringComparer.OrdinalIgnoreCase.Compare(left.DistinguishedName, right.DistinguishedName);
        int result = identity != 0 ? identity : StringComparer.Ordinal.Compare(left.DistinguishedName, right.DistinguishedName);
        return query.SortDescending ? -Math.Sign(result) : result;
    }

    private static int CompareValue(string? left, string? right, bool numeric)
    {
        if (left is null) { return right is null ? 0 : 1; }
        if (right is null) { return -1; }
        if (numeric)
        {
            bool leftValid = long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out long leftNumber);
            bool rightValid = long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out long rightNumber);
            if (leftValid && rightValid) { return leftNumber.CompareTo(rightNumber); }
            if (leftValid != rightValid) { return leftValid ? -1 : 1; }
        }
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }
}

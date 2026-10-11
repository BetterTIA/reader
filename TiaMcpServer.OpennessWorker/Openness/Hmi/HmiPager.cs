using Siemens.Engineering.HmiUnified;
using TiaMcpServer.Contracts.Hmi;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>The page a paged reader returns; <see cref="Snapshot"/> is set on a continuation and replaces the scan.</summary>
public readonly record struct HmiPaging(int Offset, int Limit, string Operation, HmiSnapshot? Snapshot);

public static class HmiPager
{
    /// <summary>Case-insensitive ordinal order with a case-sensitive ordinal tie-break, so the order is total and stable.</summary>
    public static IOrderedEnumerable<T> InNameOrder<T>(IEnumerable<T> source, Func<T, string> name)
        => source.OrderBy(name, StringComparer.OrdinalIgnoreCase).ThenBy(name, StringComparer.Ordinal);

    /// <summary>
    /// One page in name order; an offset past the total is an empty page. A continuation pages its held rows and
    /// never calls <paramref name="scan"/>; a fresh scan with more rows than the page is held in
    /// <see cref="HmiSnapshotStore.Shared"/>. <see cref="HmiPage.SnapshotId"/> is set exactly when <see cref="HmiPage.NextOffset"/> is.
    /// </summary>
    public static (IReadOnlyList<T> Items, HmiPage Page) Page<T>(
        Func<IEnumerable<T>> scan, Func<T, string> name, HmiPaging paging, object project, HmiSoftware software)
    {
        if (paging.Snapshot is { } live)
        {
            // A dead Portal or a deleted HMI fails the item, as on a fresh scan; only a deleted row object fails just its row.
            HmiReadLog.Guard(() => live.Software.Name, "The HMI software of the page snapshot");
        }

        var ordered = paging.Snapshot is { } held ? held.Ordered.Cast<T>().ToList() : InNameOrder(scan(), name).ToList();
        var items = ordered.Skip(paging.Offset).Take(paging.Limit).ToList();
        var end = (long)paging.Offset + paging.Limit; // long: offset is not bounded above, so int addition could wrap negative
        var page = new HmiPage(paging.Offset, paging.Limit, ordered.Count, null);
        if (end >= ordered.Count)
        {
            return (items, page);
        }

        var snapshotId = paging.Snapshot?.Id
            ?? HmiSnapshotStore.Shared.Put(project, software, paging.Operation, ordered.Cast<object>().ToList());
        return (items, page with { NextOffset = (int)end, SnapshotId = snapshotId });
    }
}

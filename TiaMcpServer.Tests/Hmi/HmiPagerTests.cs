using Siemens.Engineering.HmiUnified;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiPagerTests
{
    private const string Op = "hmi_list_tags";
    private static readonly object Project = new();
    private static readonly HmiSoftware Software = new();

    private static (IReadOnlyList<string> Items, TiaMcpServer.Contracts.Hmi.HmiPage Page) Fresh(
        IEnumerable<string> source, int offset, int limit)
        => HmiPager.Page(() => source, n => n, new HmiPaging(offset, limit, Op, null), Project, Software);

    [Fact]
    public void CaseOnlyDifferentNamesKeepStableOrderAcrossPages()
    {
        var shuffled = new[] { "b", "a", "B", "A" };
        var ordered = new List<string>();
        int? offset = 0;

        while (offset is { } o)
        {
            var (items, page) = Fresh(shuffled, o, limit: 1);
            ordered.AddRange(items);
            offset = page.NextOffset;
        }

        Assert.Equal(new[] { "A", "a", "B", "b" }, ordered);
    }

    [Fact]
    public void OffsetPastTotalIsEmptyPageWithNullNextOffset()
    {
        var (items, page) = Fresh(new[] { "a", "b", "c" }, offset: 10, limit: 5);

        Assert.Empty(items);
        Assert.Equal(10, page.Offset);
        Assert.Equal(5, page.Limit);
        Assert.Equal(3, page.Total);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void OffsetNearIntMaxValueDoesNotOverflowIntoANegativeNextOffset()
    {
        var (items, page) = Fresh(new[] { "a", "b", "c" }, offset: int.MaxValue - 1, limit: 2000);

        Assert.Empty(items);
        Assert.Equal(int.MaxValue - 1, page.Offset);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void LastPageHasNullNextOffset()
    {
        var names = new[] { "a", "b", "c" };

        var first = Fresh(names, 0, 2);
        var last = Fresh(names, 2, 2);

        Assert.Equal(2, first.Page.NextOffset);
        Assert.Equal(new[] { "c" }, last.Items);
        Assert.Null(last.Page.NextOffset);
    }

    [Fact]
    public void Page_SinglePage_StoresNoSnapshot()
    {
        var (items, page) = Fresh(new[] { "b", "a" }, 0, 2);

        Assert.Equal(new[] { "a", "b" }, items);
        Assert.Null(page.NextOffset);
        Assert.Null(page.SnapshotId);
    }

    [Fact]
    public void Page_LastPage_HasNoSnapshotId()
    {
        var first = Fresh(new[] { "a", "b", "c" }, 0, 2);
        var snapshot = HmiSnapshotStore.Shared.Take(Project, first.Page.SnapshotId!, Op);

        var last = HmiPager.Page(() => new[] { "a", "b", "c" }, n => n, new HmiPaging(2, 2, Op, snapshot), Project, Software);

        Assert.NotNull(first.Page.SnapshotId);
        Assert.Equal(new[] { "c" }, last.Items);
        Assert.Null(last.Page.NextOffset);
        Assert.Null(last.Page.SnapshotId);
    }

    [Fact]
    public void Page_WithSnapshot_DoesNotScan()
    {
        var snapshot = new HmiSnapshot(Software, new object[] { "a", "b", "c" }, "held");
        IEnumerable<string> Scan() => throw new InvalidOperationException("A continuation must not scan.");

        var (items, page) = HmiPager.Page(Scan, n => n, new HmiPaging(0, 2, Op, snapshot), Project, Software);

        Assert.Equal(new[] { "a", "b" }, items);
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.NextOffset);
        Assert.Equal("held", page.SnapshotId);
    }
}

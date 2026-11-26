using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Reader;

/// <summary>ANet: export_to_folder never reports success for an export that did not happen.</summary>
public class ProjectFolderExportCheckTests
{
    private static readonly string[] Paths =
    {
        "PLC_1/Blocks/Main",
        "PLC_1/Blocks/Line/Motor",
        "PLC_1/Tags/Default tag table",
    };

    [Theory]
    [InlineData("PLC_1/Blocks/Line", "PLC_1/Blocks/Line/Motor", true)]
    [InlineData("plc_1/blocks/line/", "PLC_1/Blocks/Line/Motor", true)]
    [InlineData("PLC_1/Types", "PLC_1/Blocks/Line/Motor", false)]
    [InlineData("  ", "PLC_1/Blocks/Line/Motor", false)]
    public void MatchesPathFilter_ComparesTheStartOfThePath(string prefix, string path, bool expected)
        => Assert.Equal(expected, ProjectFolderExportCheck.MatchesPathFilter(path, new[] { prefix }));

    [Fact]
    public void MatchesPathFilter_NoPrefixesMatchesEverything()
    {
        Assert.True(ProjectFolderExportCheck.MatchesPathFilter("PLC_1/Blocks/Main", null));
        Assert.True(ProjectFolderExportCheck.MatchesPathFilter("PLC_1/Blocks/Main", new string[0]));
    }

    [Fact]
    public void UnmatchedPrefixes_NoneGivenOrAllMatch_NoWarning()
    {
        Assert.Null(ProjectFolderExportCheck.UnmatchedPrefixesWarning(null, Paths));
        Assert.Null(ProjectFolderExportCheck.UnmatchedPrefixesWarning(new[] { "PLC_1/Blocks/Line", "PLC_1/Tags" }, Paths));
    }

    [Fact]
    public void UnmatchedPrefixes_NamesEachPrefixThatMatchedNothing()
    {
        var warning = ProjectFolderExportCheck.UnmatchedPrefixesWarning(new[] { "PLC_1/Blocks/Line", "PLC_1/Blocks/Lien", "PLC_2" }, Paths);

        Assert.NotNull(warning);
        Assert.Contains("prefix matched nothing", warning);
        Assert.Contains("'PLC_1/Blocks/Lien'", warning);
        Assert.Contains("'PLC_2'", warning);
        Assert.DoesNotContain("'PLC_1/Blocks/Line'", warning);
        Assert.Contains("e.g. 'PLC_1/Blocks/Main'", warning);
    }

    [Fact]
    public void UnmatchedPrefixes_EmptyPlc_StillWarns()
        => Assert.Contains("prefix matched nothing", ProjectFolderExportCheck.UnmatchedPrefixesWarning(new[] { "PLC_1/Blocks" }, new string[0]));

    [Theory]
    [InlineData("documents: Export is not supported in online mode.", true)]
    [InlineData("documents: x | xml: The operation is not permitted while ONLINE.", true)]
    [InlineData("documents: document export state 'Failed'", false)]
    [InlineData(null, false)]
    public void IsOnlineModeError_LooksForOnline(string? error, bool expected)
        => Assert.Equal(expected, ProjectFolderExportCheck.IsOnlineModeError(error));

    [Fact]
    public void Outcome_NothingExportedWithOnlineErrors_IsAnErrorThatSaysGoOffline()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectFolderExportCheck.CheckOutcome(3, 0, 3, null, "PLC_1", "PLC_1/Blocks/Main: documents: not supported in online mode"));

        Assert.Contains("Nothing was exported", exception.Message);
        Assert.Contains("Go offline", exception.Message);
        Assert.Contains("First error: PLC_1/Blocks/Main", exception.Message);
    }

    [Fact]
    public void Outcome_NothingExportedWhilePlcOnline_IsAnErrorEvenWithLocalizedMessages()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectFolderExportCheck.CheckOutcome(2, 0, 0, true, "PLC_1", "PLC_1/Blocks/Main: documents: Export wird nicht unterstuetzt"));

        Assert.Contains("PLC 'PLC_1' is online", exception.Message);
    }

    [Fact]
    public void Outcome_SomeRefusedInOnlineMode_Warns()
        => Assert.Contains("2 of 5 items were not exported because TIA Portal is in online mode",
            ProjectFolderExportCheck.CheckOutcome(5, 3, 2, true, "PLC_1", "x"));

    [Fact]
    public void Outcome_AllFailedForAnotherReasonOffline_WarnsNothingExported()
        => Assert.Contains("Nothing was exported: all 2 items failed",
            ProjectFolderExportCheck.CheckOutcome(2, 0, 0, false, "PLC_1", "x"));

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(4, 4, 0, true)]
    [InlineData(4, 4, 0, null)]
    public void Outcome_NothingToReport_IsNull(int processed, int succeeded, int onlineRefused, bool? plcOnline)
        => Assert.Null(ProjectFolderExportCheck.CheckOutcome(processed, succeeded, onlineRefused, plcOnline, "PLC_1", null));
}

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Reader;

/// <summary>ANet anet.9: Siemens-free logic of cross_references_for, manifest dedup, skipped log, includeXml check, README counts.</summary>
public class ReaderTechnicalDebtTests
{
    private static ObjectCrossReferenceElement Element(string name, params (string By, string Type, string Access, string RefType)[] uses)
    {
        var element = new ObjectCrossReferenceElement { Name = name };
        foreach (var group in uses.GroupBy(use => (use.By, use.Type)))
        {
            var usage = new ObjectCrossReferenceUsage { Name = group.Key.By, TypeName = group.Key.Type };
            foreach (var use in group)
            {
                usage.Locations.Add(new ObjectCrossReferenceLocation { Access = use.Access, ReferenceType = use.RefType });
            }

            element.UsedBy.Add(usage);
        }

        return element;
    }

    // ---- cross_references_for: incomplete reads ----

    [Fact]
    public void Incomplete_NothingUnreadable_LeavesResultUntouched()
    {
        var result = new ObjectCrossReferenceResult();
        ObjectCrossReferenceLogic.ApplyIncompleteness(result, 0);
        Assert.Null(result.Incomplete);
        Assert.Null(result.UnreadableMembers);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Incomplete_Unreadable_WarnsThatMissingReferenceIsNoProof()
    {
        var result = new ObjectCrossReferenceResult();
        ObjectCrossReferenceLogic.ApplyIncompleteness(result, 3);
        Assert.True(result.Incomplete);
        Assert.Equal(3, result.UnreadableMembers);
        Assert.Contains("NOT proof", result.Warning);
        Assert.Contains("3 member(s)", result.Warning);
    }

    [Fact]
    public void Incomplete_SerializesAsCamelCaseFlags()
    {
        var result = new ObjectCrossReferenceResult();
        ObjectCrossReferenceLogic.ApplyIncompleteness(result, 1);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        Assert.Contains("\"incomplete\":true", json);
        Assert.Contains("\"unreadableMembers\":1", json);
    }

    // ---- cross_references_for: software units and bare-name lookup ----

    [Fact]
    public void TakeUnitName_UnitPath_StripsUnitPrefix()
    {
        var segments = new List<string> { "Units", "Line1", "Blocks", "Grp", "FB10" };
        Assert.Equal("Line1", ObjectCrossReferenceLogic.TakeUnitName(segments));
        Assert.Equal(new[] { "Blocks", "Grp", "FB10" }, segments);
    }

    [Theory]
    [InlineData("Units", "Line1")]
    [InlineData("Blocks", "Grp", "FB10")]
    [InlineData("units")]
    public void TakeUnitName_NotAUnitPath_LeavesSegments(params string[] input)
    {
        var segments = input.ToList();
        Assert.Null(ObjectCrossReferenceLogic.TakeUnitName(segments));
        Assert.Equal(input, segments);
    }

    [Fact]
    public void TakeUnitName_IsCaseInsensitiveOnKeyword()
    {
        var segments = new List<string> { "UNITS", "U", "Tags", "T" };
        Assert.Equal("U", ObjectCrossReferenceLogic.TakeUnitName(segments));
    }

    [Fact]
    public void NotResolvedMessage_NoCandidates_SaysNoneFound()
        => Assert.Equal("No block, tag or type named 'X' in PLC_1.", ObjectCrossReferenceLogic.NotResolvedMessage("PLC_1", "X", Array.Empty<string>()));

    [Fact]
    public void NotResolvedMessage_Ambiguous_ListsFullPathsWithUnitsAndSystemBlocks()
    {
        var message = ObjectCrossReferenceLogic.NotResolvedMessage("PLC_1", "FB1",
            new[] { "Blocks/FB1", "Units/U/Blocks/FB1", "SystemBlocks/Sys/FB1" });
        Assert.Contains("ambiguous", message);
        Assert.Contains("PLC_1/Units/U/Blocks/FB1", message);
        Assert.Contains("PLC_1/SystemBlocks/Sys/FB1", message);
    }

    [Fact]
    public void NotResolvedMessage_ListsAtMostTenCandidates()
    {
        var message = ObjectCrossReferenceLogic.NotResolvedMessage("P", "N", Enumerable.Range(0, 25).Select(i => "Blocks/B" + i).ToList());
        Assert.Contains("P/Blocks/B9", message);
        Assert.DoesNotContain("P/Blocks/B10", message);
    }

    // ---- cross_references_for: summary ----

    [Fact]
    public void BuildSummary_CountsAccessKindsPerReferencingObject_SortedByLocations()
    {
        var summary = ObjectCrossReferenceLogic.BuildSummary(new[]
        {
            Element("DB1", ("FC1", "FC", "Read", "UsedBy"), ("FC1", "FC", "Write", "UsedBy"), ("OB1", "OB", "Call", "Uses")),
            Element("DB1.M", ("FC1", "FC", "Read", "UsedBy")),
        });

        Assert.Equal(new[] { "FC1", "OB1" }, summary.Select(row => row.ReferencedBy));
        var fc = summary[0];
        Assert.Equal(2, fc.Elements);
        Assert.Equal(2, fc.Reads);
        Assert.Equal(1, fc.Writes);
        Assert.Equal(3, fc.Locations);
        Assert.Equal(3, fc.UsedBy);
        Assert.Equal(1, summary[1].Calls);
        Assert.Equal(1, summary[1].Uses);
    }

    [Fact]
    public void BuildSummary_MoreThanThreeHmiTags_CollapseIntoOneRow()
    {
        var uses = Enumerable.Range(1, 4).Select(i => ("Tag" + i, "HMI_Tag", "Read", "UsedBy")).ToArray();
        var row = Assert.Single(ObjectCrossReferenceLogic.BuildSummary(new[] { Element("DB1", uses) }));
        Assert.StartsWith("4 HMI tags", row.ReferencedBy);
        Assert.Equal(4, row.Locations);
    }

    [Fact]
    public void BuildSummary_ThreeHmiTags_StayListed()
    {
        var uses = Enumerable.Range(1, 3).Select(i => ("Tag" + i, "HMI_Tag", "Read", "UsedBy")).ToArray();
        Assert.Equal(3, ObjectCrossReferenceLogic.BuildSummary(new[] { Element("DB1", uses) }).Count);
    }

    // ---- manifest dedup ----

    private static string TempManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "manifest.jsonl");
    }

    [Fact]
    public void Manifest_RepeatedPath_KeepsOneLineWithLatestContentAtFirstPosition()
    {
        var path = TempManifest();
        var store = ManifestStore.Load(path);
        store.Upsert("PLC/Blocks/A", "{\"path\":\"PLC/Blocks/A\",\"error\":\"first\"}");
        store.Upsert("PLC/Blocks/B", "{\"path\":\"PLC/Blocks/B\"}");
        store.Upsert("PLC/Blocks/A", "{\"path\":\"PLC/Blocks/A\"}");
        store.Save(path);

        Assert.Equal(new[] { "{\"path\":\"PLC/Blocks/A\"}", "{\"path\":\"PLC/Blocks/B\"}" }, File.ReadAllLines(path));
    }

    [Fact]
    public void Manifest_SecondChunkOfSameRun_AppendsNewAndReplacesRepeated()
    {
        var path = TempManifest();
        var first = ManifestStore.Load(path);
        first.Upsert("a", "{\"path\":\"a\",\"n\":1}");
        first.Upsert("b", "{\"path\":\"b\",\"n\":1}");
        first.Save(path);

        var second = ManifestStore.Load(path);
        second.Upsert("b", "{\"path\":\"b\",\"n\":2}");
        second.Upsert("c", "{\"path\":\"c\",\"n\":2}");
        second.Save(path);

        Assert.Equal(new[] { "{\"path\":\"a\",\"n\":1}", "{\"path\":\"b\",\"n\":2}", "{\"path\":\"c\",\"n\":2}" }, File.ReadAllLines(path));
    }

    [Fact]
    public void Manifest_Load_CollapsesDuplicatesAlreadyOnDisk_AndKeepsForeignLines()
    {
        var path = TempManifest();
        File.WriteAllLines(path, new[] { "{\"path\":\"a\",\"n\":1}", "not json", "{\"path\":\"a\",\"n\":2}", "" });

        var store = ManifestStore.Load(path);
        Assert.Equal(2, store.Count);
        store.Save(path);
        Assert.Equal(new[] { "{\"path\":\"a\",\"n\":2}", "not json" }, File.ReadAllLines(path));
    }

    [Fact]
    public void Manifest_MissingFile_StartsEmpty()
        => Assert.Equal(0, ManifestStore.Load(TempManifest()).Count);

    // ---- skipped log ----

    [Fact]
    public void SkippedLog_Empty_HasNoWarning()
    {
        var log = new SkippedLog();
        Assert.Null(log.Warning);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void SkippedLog_RecordsPathWhatAndReason()
    {
        var log = new SkippedLog();
        log.Add("Screens/Main", "attribute Foo", new InvalidOperationException("boom"));
        var entry = Assert.Single(log.Entries);
        Assert.Equal("Screens/Main", entry.Path);
        Assert.Equal("attribute Foo", entry.What);
        Assert.Equal("boom", entry.Reason);
        Assert.Contains("1 object(s)", log.Warning);
    }

    [Fact]
    public void SkippedLog_CapsListButNotCount()
    {
        var log = new SkippedLog();
        for (var i = 0; i < SkippedLog.MaxEntries + 5; i++)
        {
            log.Add(null, "x", "r");
        }

        Assert.Equal(SkippedLog.MaxEntries + 5, log.Total);
        Assert.Equal(SkippedLog.MaxEntries, log.Entries.Count);
        Assert.Contains($"first {SkippedLog.MaxEntries} listed", log.Warning);
        Assert.Equal(string.Empty, log.Entries[0].Path);
    }

    // ---- includeXml ----

    [Theory]
    [InlineData(null, "PID_1", null)]
    [InlineData(false, "PID_1", null)]
    [InlineData(false, null, "")]
    public void IncludeXml_NotRequested_PassesWithoutRoot(bool? includeXml, string? name, string? root)
        => Assert.Null(IncludeXmlCheck.Validate(includeXml, name, root));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\folder")]
    public void IncludeXml_WithoutAbsoluteExportRoot_IsAClearError(string? root)
    {
        var exception = Assert.Throws<WorkerOperationException>(() => IncludeXmlCheck.Validate(true, "PID_1", root));
        Assert.Contains("includeXml", exception.Message);
        Assert.Contains("export root", exception.Message);
    }

    [Fact]
    public void IncludeXml_WithRootAndName_IsFine()
        => Assert.Null(IncludeXmlCheck.Validate(true, "PID_1", Path.GetTempPath()));

    [Fact]
    public void IncludeXml_WithoutName_WarnsThatItWasIgnored()
        => Assert.Contains("ignored", IncludeXmlCheck.Validate(true, null, Path.GetTempPath()));

    // ---- README keeps the tool counts honest ----

    private static string RepoFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, name)))
        {
            directory = directory.Parent;
        }

        return directory is null ? throw new FileNotFoundException(name) : Path.Combine(directory.FullName, name);
    }

    [Fact]
    public void Readme_ToolCounts_MatchTheRegisteredTools()
    {
        var names = typeof(ProjectLifecycleTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name!)
            .ToArray();
        // The tools that are not [ReadOnly = true]: lifecycle, write batches, network_write, compile_check.
        var writeOrCompile = new[]
        {
            "compile_check", "open_project", "create_project", "save_project", "save_project_as", "archive_project",
            "close_project", "preview_write_batch", "apply_write_batch", "network_write",
        };

        var readme = File.ReadAllText(RepoFile("README.md"));
        var match = Regex.Match(readme, @"exposes (\d+) tools in read-write mode and (\d+) tools in read-only mode");
        Assert.True(match.Success, "README.md must keep the sentence 'exposes N tools in read-write mode and M tools in read-only mode'.");
        Assert.Equal(names.Length, int.Parse(match.Groups[1].Value));
        Assert.Equal(names.Count(name => !writeOrCompile.Contains(name)), int.Parse(match.Groups[2].Value));
    }
}

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>ANet fork: Siemens-free parts of cross_references_for, kept apart so they can be unit tested without TIA.</summary>
internal static class ObjectCrossReferenceLogic
{
    /// <summary>Marks the result incomplete when Openness refused to give some members (they come back as empty values).</summary>
    public static void ApplyIncompleteness(ObjectCrossReferenceResult result, int unreadable)
    {
        if (unreadable <= 0)
        {
            return;
        }

        result.Incomplete = true;
        result.UnreadableMembers = unreadable;
        result.Warning = $"{unreadable} member(s) could not be read from TIA and appear empty, so a missing reference is NOT proof that nothing uses this object. Re-run, or check the object in TIA.";
    }

    /// <summary>
    /// For <c>Units/&lt;unit&gt;/Blocks|Types|Tags/...</c> removes the first two segments and returns the unit name;
    /// otherwise leaves the segments alone and returns null (the PLC itself).
    /// </summary>
    public static string? TakeUnitName(List<string> segments)
    {
        if (segments.Count < 3 || !segments[0].Equals("Units", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var unit = segments[1];
        segments.RemoveRange(0, 2);
        return unit;
    }

    public static string NotResolvedMessage(string plcName, string name, IReadOnlyList<string> candidatePaths)
        => candidatePaths.Count == 0
            ? $"No block, tag or type named '{name}' in {plcName}."
            : $"'{name}' is ambiguous; use a full path: {string.Join("; ", candidatePaths.Take(10).Select(path => plcName + "/" + path))}.";

    /// <summary>One row per referencing object; more than three HMI tags collapse into one row.</summary>
    public static List<ObjectCrossReferenceSummary> BuildSummary(IEnumerable<ObjectCrossReferenceElement> elements)
    {
        var summary = elements
            .SelectMany(element => element.UsedBy.Select(usage => (element, usage)))
            .GroupBy(pair => pair.usage.Name, StringComparer.Ordinal)
            .Select(group => new ObjectCrossReferenceSummary
            {
                ReferencedBy = group.Key,
                TypeName = group.First().usage.TypeName,
                Elements = group.Select(pair => pair.element.Name).Distinct(StringComparer.Ordinal).Count(),
                Reads = group.Sum(pair => pair.usage.Locations.Count(location => location.Access.IndexOf("Read", StringComparison.OrdinalIgnoreCase) >= 0)),
                Writes = group.Sum(pair => pair.usage.Locations.Count(location => location.Access.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0)),
                Calls = group.Sum(pair => pair.usage.Locations.Count(location => location.Access.IndexOf("Call", StringComparison.OrdinalIgnoreCase) >= 0)),
                Locations = group.Sum(pair => pair.usage.Locations.Count),
                UsedBy = group.Sum(pair => pair.usage.Locations.Count(location => location.ReferenceType == "UsedBy")),
                Uses = group.Sum(pair => pair.usage.Locations.Count(location => location.ReferenceType == "Uses")),
            })
            .OrderByDescending(row => row.Locations)
            .ToList();
        var hmiTags = summary.Where(row => row.TypeName == "HMI_Tag").ToList();
        if (hmiTags.Count > 3)
        {
            summary.RemoveAll(row => row.TypeName == "HMI_Tag");
            summary.Add(new ObjectCrossReferenceSummary
            {
                ReferencedBy = $"{hmiTags.Count} HMI tags (e.g. {hmiTags[0].ReferencedBy})",
                TypeName = "HMI_Tag",
                Elements = hmiTags.Sum(row => row.Elements),
                Locations = hmiTags.Sum(row => row.Locations),
                UsedBy = hmiTags.Sum(row => row.UsedBy),
                Uses = hmiTags.Sum(row => row.Uses),
            });
        }

        return summary;
    }
}

internal sealed class ObjectCrossReferenceResult
{
    public string ObjectPath { get; set; } = string.Empty;
    public string ObjectKind { get; set; } = string.Empty;
    public string PlcName { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public int ElementCount { get; set; }
    public int ReferencingObjectCount { get; set; }
    public int LocationCount { get; set; }
    public bool Truncated { get; set; }
    public bool? Incomplete { get; set; }
    public int? UnreadableMembers { get; set; }
    public string? Warning { get; set; }
    public string? Note { get; set; }
    public string? File { get; set; }
    public List<ObjectCrossReferenceSummary> Summary { get; set; } = new();
    public List<ObjectCrossReferenceCaller>? CalledVia { get; set; }
    public List<ObjectCrossReferenceElement> Elements { get; set; } = new();
}

internal sealed class ObjectCrossReferenceCaller
{
    public string InstanceDb { get; set; } = string.Empty;
    public List<string> UsedBy { get; set; } = new();
}

internal sealed class ObjectCrossReferenceSummary
{
    public string ReferencedBy { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int Elements { get; set; }
    public int Reads { get; set; }
    public int Writes { get; set; }
    public int Calls { get; set; }
    public int Locations { get; set; }
    public int UsedBy { get; set; }
    public int Uses { get; set; }
}

internal sealed class ObjectCrossReferenceElement
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public List<ObjectCrossReferenceUsage> UsedBy { get; set; } = new();
}

internal sealed class ObjectCrossReferenceUsage
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Path { get; set; }
    public List<ObjectCrossReferenceLocation> Locations { get; set; } = new();
}

internal sealed class ObjectCrossReferenceLocation
{
    public string Name { get; set; } = string.Empty;
    public string Access { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public string? ReferenceLocation { get; set; }
    public string? ReferencedAs { get; set; }
    public string? Address { get; set; }
}

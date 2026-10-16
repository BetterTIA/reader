using System.Collections;
using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.TechnologicalObjects;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// ANet fork: offline configuration of technology objects (PID_Temp, PID_Compact, axes...). Technology
/// objects are instance DBs (V21: TechnologicalInstanceDB : InstanceDB) kept in the "Technology objects"
/// folder (PlcSoftware.TechnologicalObjectGroup), so the block tools never see them.
///
/// <para>
/// Without a name the reader lists every technology object of the PLC. With a name it returns the
/// object's parameters (TechnologicalParameter Name / Value, e.g. Config.InputUpperLimit,
/// Retain.CtrlParams.Heat.Gain), optionally narrowed to path prefixes, and on request writes the
/// SimaticML export of the object under the export root. Nothing is written to the project.
/// </para>
/// </summary>
internal static class TechnologicalObjectReader
{
    private const int DefaultMaxParameters = 1_000;

    public static TechnologicalObjectResult Read(Project project, WorkerRequest request)
    {
        var plcSoftware = PlcSoftwareLocator.Find(project, request.PlcName);
        var all = Enumerate(plcSoftware.TechnologicalObjectGroup, string.Empty).ToList();

        var result = new TechnologicalObjectResult
        {
            PlcName = plcSoftware.Name,
            Count = all.Count,
        };

        if (string.IsNullOrWhiteSpace(request.ObjectPath))
        {
            result.Objects = all.Select(entry => Describe(entry.Object, entry.Group)).ToList();
            result.Note = all.Count == 0
                ? "The PLC has no technology objects."
                : "Pass name to read the parameters of one technology object.";
            return result;
        }

        var name = request.ObjectPath!.Trim();
        var found = all.Where(entry => string.Equals(entry.Object.Name, name, StringComparison.Ordinal)).ToList();
        if (found.Count == 0)
        {
            found = all.Where(entry => string.Equals(entry.Object.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (found.Count != 1)
        {
            var known = all.Count == 0 ? "none" : string.Join(", ", all.Select(entry => entry.Object.Name).Take(50));
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"Technology object '{name}' not found (or not unique) in '{plcSoftware.Name}'. Technology objects: {known}.");
        }

        var technologyObject = found[0].Object;
        var described = Describe(technologyObject, found[0].Group);
        result.Objects = new List<TechnologicalObjectInfo> { described };

        var prefixes = (request.ExportPathPrefixes ?? new List<string>())
            .Select(prefix => prefix.Trim().TrimEnd('.'))
            .Where(prefix => prefix.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var maxParameters = request.MaxResults is > 0 ? request.MaxResults.Value : DefaultMaxParameters;

        var parameters = new List<TechnologicalParameterValue>();
        var total = 0;
        var unreadable = 0;
        foreach (TechnologicalParameter parameter in technologyObject.Parameters)
        {
            string parameterName;
            try
            {
                parameterName = parameter.Name;
            }
            catch (Exception)
            {
                unreadable++;
                continue;
            }

            total++;
            if (prefixes.Count > 0 && !prefixes.Any(prefix => IsAtOrBelow(parameterName, prefix)))
            {
                continue;
            }
            if (parameters.Count >= maxParameters)
            {
                result.Truncated = true;
                continue;
            }

            try
            {
                parameters.Add(new TechnologicalParameterValue { Name = parameterName, Value = Format(parameter.Value) });
            }
            catch (Exception exception)
            {
                unreadable++;
                parameters.Add(new TechnologicalParameterValue { Name = parameterName, Error = exception.Message });
            }
        }

        described.Parameters = parameters;
        described.ParameterCount = total;
        if (prefixes.Count > 0)
        {
            described.Filter = string.Join(", ", prefixes);
            if (parameters.Count == 0 && total > 0)
            {
                result.Note = "No parameter matches the filter. Top-level parameters: "
                    + string.Join(", ", TopLevelNames(technologyObject).Take(50));
            }
        }
        if (result.Truncated)
        {
            result.Note = $"Only the first {maxParameters} matching parameters are returned; narrow parameterPrefixes or raise maxParameters.";
        }
        if (unreadable > 0)
        {
            result.Incomplete = true;
            result.UnreadableParameters = unreadable;
            result.Warning = $"{unreadable} parameter(s) could not be read from TIA; their values are missing or carry an error.";
        }

        if (request.IncludeXmlExport == true)
        {
            described.XmlFile = ExportXml(project, request, technologyObject);
        }

        return result;
    }

    /// <summary>True when name is the prefix itself or lies below it ('Config' matches 'Config.InputUpperLimit', not 'ConfigX').</summary>
    internal static bool IsAtOrBelow(string name, string prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return name.Length == prefix.Length || name[prefix.Length] == '.' || name[prefix.Length] == '[';
    }

    /// <summary>Invariant text of a parameter value; arrays and lists as "[a, b, c]".</summary>
    internal static string Format(object? value) => value switch
    {
        null => "null",
        string text => text,
        bool flag => flag ? "TRUE" : "FALSE",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Format)) + "]",
        _ => value.ToString() ?? string.Empty,
    };

    private static IEnumerable<string> TopLevelNames(TechnologicalInstanceDB technologyObject)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TechnologicalParameter parameter in technologyObject.Parameters)
        {
            string name;
            try
            {
                name = parameter.Name;
            }
            catch (Exception)
            {
                continue;
            }
            var end = name.IndexOfAny(new[] { '.', '[' });
            var top = end < 0 ? name : name.Substring(0, end);
            if (seen.Add(top))
            {
                yield return top;
            }
        }
    }

    private static TechnologicalObjectInfo Describe(TechnologicalInstanceDB technologyObject, string group)
    {
        var info = new TechnologicalObjectInfo
        {
            Name = technologyObject.Name,
            Group = group.Length == 0 ? null : group,
        };
        try { info.Type = technologyObject.OfSystemLibElement; } catch (Exception) { }
        try { info.Version = technologyObject.OfSystemLibVersion?.ToString(); } catch (Exception) { }
        try { info.Number = technologyObject.Number; } catch (Exception) { }
        try { info.IsConsistent = technologyObject.IsConsistent; } catch (Exception) { }
        return info;
    }

    private static string ExportXml(Project project, WorkerRequest request, TechnologicalInstanceDB technologyObject)
    {
        if (string.IsNullOrWhiteSpace(request.ExportRoot) || !Path.IsPathRooted(request.ExportRoot))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "includeXml needs the server's export root (--export-root or TIA_MCP_EXPORT_ROOT).");
        }

        var directory = Path.Combine(Path.GetFullPath(request.ExportRoot!),
            ProjectFolderExporter.SafeName(project.Name), "technology-objects");
        Directory.CreateDirectory(directory);
        var file = new FileInfo(Path.Combine(directory,
            ProjectFolderExporter.SafeName(technologyObject.Name) + "-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".xml"));
        // Same call export_to_folder uses for blocks; it only writes the file. Not yet tried on a technology object.
        technologyObject.Export(file, ExportOptions.None);
        return file.FullName;
    }

    private static IEnumerable<(TechnologicalInstanceDB Object, string Group)> Enumerate(TechnologicalInstanceDBGroup group, string path)
    {
        foreach (TechnologicalInstanceDB technologyObject in group.TechnologicalObjects)
        {
            yield return (technologyObject, path);
        }
        foreach (TechnologicalInstanceDBUserGroup child in group.Groups)
        {
            var childPath = path.Length == 0 ? child.Name : path + "/" + child.Name;
            foreach (var entry in Enumerate(child, childPath))
            {
                yield return entry;
            }
        }
    }
}

internal sealed class TechnologicalObjectResult
{
    public string PlcName { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool Truncated { get; set; }
    public bool? Incomplete { get; set; }
    public int? UnreadableParameters { get; set; }
    public string? Warning { get; set; }
    public string? Note { get; set; }
    public List<TechnologicalObjectInfo> Objects { get; set; } = new();
}

internal sealed class TechnologicalObjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Version { get; set; }
    public int? Number { get; set; }
    public bool? IsConsistent { get; set; }
    public string? Group { get; set; }
    public int? ParameterCount { get; set; }
    public string? Filter { get; set; }
    public string? XmlFile { get; set; }
    public List<TechnologicalParameterValue>? Parameters { get; set; }
}

internal sealed class TechnologicalParameterValue
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Error { get; set; }
}

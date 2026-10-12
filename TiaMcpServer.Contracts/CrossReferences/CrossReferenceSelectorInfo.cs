using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.CrossReferences;

/// <summary>
/// Cross-reference target: a <c>browse_project_tree</c> selector path, optionally narrowed to a
/// member the tree does not model (a tag or constant under a <c>TagTable</c> path), or, provisionally,
/// one Unified HMI tag (<see cref="Hmi"/>, with an empty <see cref="Path"/>).
/// </summary>
public class CrossReferenceSelectorInfo
{
    public List<ProjectTreeSelectorSegment> Path { get; set; } = new List<ProjectTreeSelectorSegment>();

    public CrossReferenceMemberSelectorInfo? Member { get; set; }

    /// <summary>Present only on a provisional HMI tag selector; path selectors omit it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CrossReferenceHmiSelectorInfo? Hmi { get; set; }
}

public class CrossReferenceMemberSelectorInfo
{
    /// <summary>One of <see cref="CrossReferenceMemberKinds.All"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>One top-level tag of one Unified HMI; UDT member paths are not supported.</summary>
public class CrossReferenceHmiSelectorInfo
{
    public string HmiName { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;
}

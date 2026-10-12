using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Hmi;

/// <summary>Page position of a paged operation; <see cref="NextOffset"/> is null on the last page.</summary>
public sealed record HmiPage(int Offset, int Limit, int Total, int? NextOffset)
{
    /// <summary>Worker to host only: the snapshot the worker holds for the next page; present exactly when <see cref="NextOffset"/> is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotId { get; init; }

    /// <summary>Host to caller only: the cursor for the next page; present when more rows exist.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

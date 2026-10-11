using Xunit;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>Classes that Put/Take on the static HmiSnapshotStore.Shared run serially so LRU eviction cannot race them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HmiSnapshotStoreCollection
{
    public const string Name = "HmiSnapshotStore";
}

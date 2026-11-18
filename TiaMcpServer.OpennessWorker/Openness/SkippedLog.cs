namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// ANet fork: collects what an export could not read, so the response lists it instead of the
/// reader swallowing the exception. The list is capped (the count is not).
/// </summary>
internal sealed class SkippedLog
{
    internal const int MaxEntries = 100;

    public List<SkippedEntry> Entries { get; } = new();
    public int Total { get; private set; }

    public void Add(string? path, string what, Exception exception)
        => Add(path, what, exception.Message);

    public void Add(string? path, string what, string reason)
    {
        Total++;
        if (Entries.Count < MaxEntries)
        {
            Entries.Add(new SkippedEntry { Path = path ?? string.Empty, What = what, Reason = reason });
        }
    }

    public string? Warning => Total == 0
        ? null
        : $"{Total} object(s) or attribute(s) could not be read and are missing or incomplete in the export; see skipped"
            + (Total > Entries.Count ? $" (first {Entries.Count} listed)." : ".");
}

internal sealed class SkippedEntry
{
    public string Path { get; set; } = string.Empty;
    public string What { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// ANet fork: export_to_folder must never look like a success when it exported nothing
/// (TIA Portal in online mode, or path prefixes that match no item).
/// </summary>
internal static class ProjectFolderExportCheck
{
    /// <summary>True when no prefix is given or the path starts with one of them (case-insensitive).</summary>
    public static bool MatchesPathFilter(string path, IReadOnlyCollection<string>? prefixes)
        => prefixes is null || prefixes.Count == 0 || prefixes.Any(prefix => Matches(path, prefix));

    /// <summary>A warning naming every given prefix that matches none of <paramref name="paths"/>; null when all match or none is given.</summary>
    public static string? UnmatchedPrefixesWarning(IReadOnlyCollection<string>? prefixes, IReadOnlyCollection<string> paths)
    {
        var unmatched = (prefixes ?? new List<string>())
            .Where(prefix => !paths.Any(path => Matches(path, prefix)))
            .ToList();
        if (unmatched.Count == 0)
        {
            return null;
        }

        var example = paths.FirstOrDefault();
        return $"pathPrefixes: prefix matched nothing: {string.Join(", ", unmatched.Select(prefix => $"'{prefix}'"))}. "
            + "Prefixes are compared with the start of item paths such as PLC_1/Blocks/<folder>/<name>, PLC_1/Types/... or PLC_1/Tags/<table>"
            + (example is null ? string.Empty : $" (e.g. '{example}')") + ".";
    }

    /// <summary>Whether an item error is TIA Portal refusing the export in online mode ("not supported in online mode").</summary>
    public static bool IsOnlineModeError(string? error)
        => error is not null && error.IndexOf("online", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Throws when a call processed items but exported none of them while TIA Portal is in online mode
    /// (an item error says so, or the PLC reads as online). Returns a warning when only some items were
    /// refused in online mode, or when every processed item failed for another reason; null otherwise.
    /// </summary>
    public static string? CheckOutcome(int processed, int succeeded, int onlineRefused, bool? plcOnline, string plcName, string? firstError)
    {
        if (processed == 0)
        {
            return null;
        }

        if (succeeded == 0 && (onlineRefused > 0 || plcOnline == true))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Nothing was exported: TIA Portal does not export in online mode (all {processed} items failed"
                + (plcOnline == true ? $"; PLC '{plcName}' is online" : string.Empty) + "). "
                + "Go offline in TIA Portal (or watch_online goOffline) and repeat the call; files from an earlier export may be outdated."
                + (firstError is null ? string.Empty : $" First error: {firstError}"));
        }

        if (onlineRefused > 0)
        {
            return $"{onlineRefused} of {processed} items were not exported because TIA Portal is in online mode; go offline and repeat the call for them (see failures).";
        }

        return succeeded == 0
            ? $"Nothing was exported: all {processed} items failed (see failures)."
            : null;
    }

    private static bool Matches(string path, string? prefix)
        => !string.IsNullOrWhiteSpace(prefix) &&
           path.StartsWith(prefix!.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

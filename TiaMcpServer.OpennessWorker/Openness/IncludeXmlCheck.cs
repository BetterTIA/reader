using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>ANet fork: includeXml must never be dropped without the caller noticing.</summary>
internal static class IncludeXmlCheck
{
    /// <summary>
    /// Throws when includeXml is requested but no absolute export root is configured. Returns a warning when
    /// includeXml cannot apply (no technology object named, so only the list is returned); null otherwise.
    /// </summary>
    public static string? Validate(bool? includeXml, string? objectName, string? exportRoot)
    {
        if (includeXml != true)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(exportRoot) || !Path.IsPathRooted(exportRoot))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "includeXml needs the server's export root (--export-root or TIA_MCP_EXPORT_ROOT); none is configured, so nothing was read.");
        }

        return string.IsNullOrWhiteSpace(objectName)
            ? "includeXml was ignored: no technology object name was given, so only the list is returned. Pass name to export its XML."
            : null;
    }
}

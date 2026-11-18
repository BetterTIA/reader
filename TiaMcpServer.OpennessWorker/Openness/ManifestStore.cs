using System.Text;
using System.Text.Json;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// ANet fork: manifest.jsonl with one line per object path. Re-running a chunk (same runId and offset)
/// used to append the same objects again; here the latest line for a path replaces the earlier one,
/// keeping the position of its first occurrence. Load collapses duplicates already on disk.
/// </summary>
internal sealed class ManifestStore
{
    private readonly List<string> order = new();
    private readonly Dictionary<string, string> lines = new(StringComparer.Ordinal);

    public int Count => order.Count;

    public static ManifestStore Load(string path)
    {
        var store = new ManifestStore();
        if (!File.Exists(path))
        {
            return store;
        }

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("path", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    store.Upsert(value.GetString()!, line);
                    continue;
                }
            }
            catch (JsonException)
            {
                // Not ours; kept verbatim below rather than dropped.
            }

            var rawKey = "0000raw" + store.order.Count;
            store.order.Add(rawKey);
            store.lines[rawKey] = line;
        }

        return store;
    }

    public void Upsert(string objectPath, string jsonLine)
    {
        if (!lines.ContainsKey(objectPath))
        {
            order.Add(objectPath);
        }

        lines[objectPath] = jsonLine;
    }

    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllLines(temporary, order.Select(key => lines[key]), new UTF8Encoding(false));
        // Replace keeps the old manifest until the new one is complete (Delete + Move left a gap).
        if (File.Exists(path))
        {
            File.Replace(temporary, path, null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}

using System.IO;
using System.Text.Json;

namespace Blindfire.Results;

public sealed record ResultHistoryEntry(
    DateTimeOffset Timestamp,
    double FovDegrees,
    double MouseDpi,
    int TrialCount,
    bool IncludePerpendicularAxisData,
    string RecommendationSummary,
    string DetailsText,
    double? CombinedSensitivity,
    double? AdsMultiplier);

public static class ResultHistoryStore
{
    private const int MaxEntries = 3;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Blindfire", "history.json");

    // Most recent first.
    public static IReadOnlyList<ResultHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Array.Empty<ResultHistoryEntry>();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ResultHistoryEntry>>(json) ?? new List<ResultHistoryEntry>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<ResultHistoryEntry>();
        }
    }

    public static void Add(ResultHistoryEntry entry)
    {
        try
        {
            var entries = Load().ToList();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistence is a convenience, not a requirement - a failed save
            // just means this run won't show up in history later.
        }
    }
}

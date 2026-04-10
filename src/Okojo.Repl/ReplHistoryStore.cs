using System.Text.Json;

namespace Okojo.Repl;

public sealed class ReplHistoryStore : IDisposable
{
    private const int MaxEntries = 1000;
    private readonly List<string> entries;
    private readonly string path;
    private int browseIndex = -1;
    private string draft = string.Empty;

    private ReplHistoryStore(string path, List<string> entries)
    {
        this.path = path;
        this.entries = entries;
    }

    public void Dispose()
    {
        Save();
    }

    public static ReplHistoryStore Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), ReplJsonContext.Default.ListString) ??
                             [];
                return new(path, loaded);
            }
        }
        catch
        {
        }

        return new(path, []);
    }

    public static ReplHistoryStore CreateEphemeral()
    {
        return new(string.Empty, []);
    }

    public void Record(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            ResetNavigation();
            return;
        }

        if (entries.Count == 0 || !string.Equals(entries[^1], entry, StringComparison.Ordinal))
            entries.Add(entry);
        if (entries.Count > MaxEntries)
            entries.RemoveRange(0, entries.Count - MaxEntries);
        ResetNavigation();
    }

    public bool TryMovePrevious(string currentText, out string entry)
    {
        if (entries.Count == 0)
        {
            entry = currentText;
            return false;
        }

        if (browseIndex < 0)
        {
            draft = currentText;
            browseIndex = entries.Count - 1;
        }
        else if (browseIndex > 0)
        {
            browseIndex--;
        }

        entry = entries[browseIndex];
        return true;
    }

    public bool TryMoveNext(out string entry)
    {
        if (browseIndex < 0)
        {
            entry = draft;
            return false;
        }

        if (browseIndex < entries.Count - 1)
        {
            browseIndex++;
            entry = entries[browseIndex];
            return true;
        }

        browseIndex = -1;
        entry = draft;
        draft = string.Empty;
        return true;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, ReplJsonContext.Default.ListString));
        }
        catch
        {
        }
    }

    private void ResetNavigation()
    {
        browseIndex = -1;
        draft = string.Empty;
    }
}

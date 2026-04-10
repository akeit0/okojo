using System.Text;

namespace Okojo.Node.Cli;

internal static class NodeCliEnvironmentFileLoader
{
    public static void LoadIntoProcess(string envFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envFilePath);

        var fullPath = Path.GetFullPath(envFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Environment file not found: {fullPath}", fullPath);

        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(fullPath, Encoding.UTF8))
        {
            lineNumber++;
            var line = rawLine.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#')
                continue;

            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                throw new FormatException($"Invalid environment entry at {fullPath}:{lineNumber}.");

            var key = line[..equalsIndex].Trim().ToString();
            var value = ParseValue(line[(equalsIndex + 1)..]);
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string ParseValue(ReadOnlySpan<char> rawValue)
    {
        var value = rawValue.Trim();
        if (value.IsEmpty)
            return string.Empty;

        if ((value[0] == '"' || value[0] == '\'') && value.Length >= 2 && value[^1] == value[0])
        {
            var inner = value[1..^1];
            return value[0] == '"' ? UnescapeDoubleQuoted(inner) : inner.ToString();
        }

        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        if (commentIndex >= 0)
            value = value[..commentIndex];

        return value.TrimEnd().ToString();
    }

    private static string UnescapeDoubleQuoted(ReadOnlySpan<char> value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => value[i]
                });
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}

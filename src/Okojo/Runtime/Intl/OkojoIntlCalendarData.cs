using System.Collections.Frozen;
using System.Globalization;

namespace Okojo.Runtime.Intl;

internal static class OkojoIntlCalendarData
{
    private static readonly Lock Gate = new();
    private static FrozenSet<string>? supportedCalendars;
    private static string[]? supportedCalendarsSorted;
    private static FrozenSet<string>? eraSuppressedCalendars;
    private static FrozenDictionary<string, ThresholdEraRule>? thresholdEraRules;
    private static FrozenDictionary<string, EraText>? fixedEraRules;
    private static JapaneseEraRule[]? japaneseEraRules;
    private static volatile bool loaded;

    internal static string[] GetSupportedCalendars()
    {
        EnsureLoaded();
        return supportedCalendarsSorted!;
    }

    internal static bool IsSupportedCalendar(string calendar)
    {
        EnsureLoaded();
        return supportedCalendars!.Contains(calendar);
    }

    internal static string? FormatEra(string calendar, int isoYear, string eraWidth)
    {
        EnsureLoaded();

        if (eraSuppressedCalendars!.Contains(calendar))
            return null;

        if (thresholdEraRules!.TryGetValue(calendar, out var thresholdRule))
            return FormatEraText(isoYear <= thresholdRule.BeforeEndYear ? thresholdRule.Before : thresholdRule.After,
                eraWidth);

        if (fixedEraRules!.TryGetValue(calendar, out var fixedRule))
            return FormatEraText(fixedRule, eraWidth);

        if (string.Equals(calendar, "japanese", StringComparison.OrdinalIgnoreCase))
            foreach (var rule in japaneseEraRules!)
                if (isoYear >= rule.StartYear)
                    return FormatEraText(rule.Text, eraWidth);

        return FormatEraText(new("AD", "Anno Domini"), eraWidth);
    }

    private static string FormatEraText(EraText text, string eraWidth)
    {
        return eraWidth switch
        {
            "short" or "narrow" => text.ShortText,
            _ => text.LongText
        };
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        lock (Gate)
        {
            if (loaded)
                return;

            var supportedCalendarsBuilder = new HashSet<string>(StringComparer.Ordinal);
            var eraSuppressedCalendarsBuilder = new HashSet<string>(StringComparer.Ordinal);
            var thresholdEraRulesBuilder = new Dictionary<string, ThresholdEraRule>(StringComparer.Ordinal);
            var fixedEraRulesBuilder = new Dictionary<string, EraText>(StringComparer.Ordinal);
            var japaneseEraList = new List<JapaneseEraRule>();

            var assembly = typeof(OkojoIntlCalendarData).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                                   .FirstOrDefault(static n => n.EndsWith("CalendarData.txt", StringComparison.Ordinal))
                               ?? throw new InvalidOperationException("Intl calendar data resource not found.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException("Intl calendar data resource stream not found.");
            using var reader = new StreamReader(stream);

            string? currentSection = null;
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                if (line.Length > 2 && line[0] == '[' && line[^1] == ']')
                {
                    currentSection = line[1..^1];
                    continue;
                }

                switch (currentSection)
                {
                    case "SUPPORTED_CALENDARS":
                        supportedCalendarsBuilder.Add(line);
                        break;
                    case "ERA_SUPPRESSED":
                        eraSuppressedCalendarsBuilder.Add(line);
                        break;
                    case "THRESHOLD_ERAS":
                        ParseThresholdEraRule(line, thresholdEraRulesBuilder);
                        break;
                    case "FIXED_ERAS":
                        ParseFixedEraRule(line, fixedEraRulesBuilder);
                        break;
                    case "JAPANESE_ERAS":
                        japaneseEraList.Add(ParseJapaneseEraRule(line));
                        break;
                }
            }

            supportedCalendars = supportedCalendarsBuilder.ToFrozenSet(StringComparer.Ordinal);
            eraSuppressedCalendars = eraSuppressedCalendarsBuilder.ToFrozenSet(StringComparer.Ordinal);
            thresholdEraRules = thresholdEraRulesBuilder.ToFrozenDictionary(StringComparer.Ordinal);
            fixedEraRules = fixedEraRulesBuilder.ToFrozenDictionary(StringComparer.Ordinal);
            supportedCalendarsSorted = [.. supportedCalendars.OrderBy(static value => value, StringComparer.Ordinal)];
            japaneseEraRules = [.. japaneseEraList.OrderByDescending(static value => value.StartYear)];
            loaded = true;
        }
    }

    private static void ParseThresholdEraRule(string line, Dictionary<string, ThresholdEraRule> builder)
    {
        var parts = line.Split('|');
        if (parts.Length != 6)
            throw new InvalidOperationException($"Invalid threshold era rule: {line}");

        builder[parts[0]] = new(
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            new(parts[2], parts[3]),
            new(parts[4], parts[5]));
    }

    private static void ParseFixedEraRule(string line, Dictionary<string, EraText> builder)
    {
        var parts = line.Split('|');
        if (parts.Length != 3)
            throw new InvalidOperationException($"Invalid fixed era rule: {line}");

        builder[parts[0]] = new(parts[1], parts[2]);
    }

    private static JapaneseEraRule ParseJapaneseEraRule(string line)
    {
        var parts = line.Split('|');
        if (parts.Length != 3)
            throw new InvalidOperationException($"Invalid Japanese era rule: {line}");

        return new(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            new(parts[1], parts[2]));
    }

    private readonly record struct EraText(string ShortText, string LongText);

    private readonly record struct ThresholdEraRule(int BeforeEndYear, EraText Before, EraText After);

    private readonly record struct JapaneseEraRule(int StartYear, EraText Text);
}

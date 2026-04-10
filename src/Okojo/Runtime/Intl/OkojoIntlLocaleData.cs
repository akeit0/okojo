namespace Okojo.Runtime.Intl;

internal static class OkojoIntlLocaleData
{
    private static readonly Lock Gate = new();
    private static Dictionary<string, string>? tagMappings;
    private static Dictionary<string, string>? languageMappings;
    private static Dictionary<string, ComplexLanguageMapping>? complexLanguageMappings;
    private static Dictionary<string, string>? regionMappings;
    private static Dictionary<string, VariantMapping>? variantMappings;
    private static Dictionary<string, string>? languageVariantMappings;
    private static Dictionary<string, string>? scriptRegionMappings;
    private static Dictionary<string, Dictionary<string, string>>? unicodeMappings;
    private static volatile bool loaded;

    public static Dictionary<string, string> TagMappings
    {
        get
        {
            EnsureLoaded();
            return tagMappings!;
        }
    }

    public static Dictionary<string, string> LanguageMappings
    {
        get
        {
            EnsureLoaded();
            return languageMappings!;
        }
    }

    public static Dictionary<string, ComplexLanguageMapping> ComplexLanguageMappings
    {
        get
        {
            EnsureLoaded();
            return complexLanguageMappings!;
        }
    }

    public static Dictionary<string, string> RegionMappings
    {
        get
        {
            EnsureLoaded();
            return regionMappings!;
        }
    }

    public static Dictionary<string, VariantMapping> VariantMappings
    {
        get
        {
            EnsureLoaded();
            return variantMappings!;
        }
    }

    public static Dictionary<string, string> LanguageVariantMappings
    {
        get
        {
            EnsureLoaded();
            return languageVariantMappings!;
        }
    }

    public static Dictionary<string, string> ScriptRegionMappings
    {
        get
        {
            EnsureLoaded();
            return scriptRegionMappings!;
        }
    }

    public static Dictionary<string, Dictionary<string, string>> UnicodeMappings
    {
        get
        {
            EnsureLoaded();
            return unicodeMappings!;
        }
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        lock (Gate)
        {
            if (loaded)
                return;

            tagMappings = new(StringComparer.OrdinalIgnoreCase);
            languageMappings = new(StringComparer.OrdinalIgnoreCase);
            complexLanguageMappings = new(StringComparer.OrdinalIgnoreCase);
            regionMappings = new(StringComparer.OrdinalIgnoreCase);
            variantMappings = new(StringComparer.OrdinalIgnoreCase);
            languageVariantMappings = new(StringComparer.OrdinalIgnoreCase);
            scriptRegionMappings = new(StringComparer.OrdinalIgnoreCase);
            unicodeMappings = new(StringComparer.OrdinalIgnoreCase);

            var assembly = typeof(OkojoIntlLocaleData).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(static n => n.EndsWith("LocaleData.txt", StringComparison.Ordinal));
            if (resourceName is null)
            {
                loaded = true;
                return;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                loaded = true;
                return;
            }

            using var reader = new StreamReader(stream);
            string? currentSection = null;
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.Length > 2 && line[0] == '[' && line[^1] == ']')
                {
                    currentSection = line.Substring(1, line.Length - 2);
                    continue;
                }

                var eqIndex = line.IndexOf('=');
                if (eqIndex < 0)
                    continue;

                var key = line.Substring(0, eqIndex);
                var value = line.Substring(eqIndex + 1);
                switch (currentSection)
                {
                    case "TAG_MAPPINGS":
                        tagMappings[key] = value;
                        break;
                    case "LANGUAGE_MAPPINGS":
                        languageMappings[key] = value;
                        break;
                    case "COMPLEX_LANGUAGE_MAPPINGS":
                        ParseComplexLanguageMapping(key, value);
                        break;
                    case "REGION_MAPPINGS":
                        regionMappings[key] = value;
                        break;
                    case "VARIANT_MAPPINGS":
                        ParseVariantMapping(key, value);
                        break;
                    case "LANGUAGE_VARIANT_MAPPINGS":
                        languageVariantMappings[key] = value;
                        break;
                    case "SCRIPT_REGION_MAPPINGS":
                        scriptRegionMappings[key] = value;
                        break;
                    case "UNICODE_MAPPINGS":
                        ParseUnicodeMapping(key, value);
                        break;
                }
            }

            loaded = true;
        }
    }

    private static void ParseComplexLanguageMapping(string key, string value)
    {
        var parts = value.Split(',');
        string? script = null;
        string? region = null;
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.StartsWith("script:", StringComparison.Ordinal))
                script = part.Substring(7);
            else if (part.StartsWith("region:", StringComparison.Ordinal))
                region = part.Substring(7);
        }

        complexLanguageMappings![key] = new(parts[0], script, region);
    }

    private static void ParseVariantMapping(string key, string value)
    {
        var parts = value.Split(',');
        if (parts.Length < 2)
            return;

        string? prefix = null;
        for (var i = 2; i < parts.Length; i++)
            if (parts[i].StartsWith("prefix:", StringComparison.Ordinal))
                prefix = parts[i].Substring(7);

        variantMappings![key] = new(parts[0], parts[1], prefix);
    }

    private static void ParseUnicodeMapping(string key, string value)
    {
        var colonIndex = key.IndexOf(':');
        if (colonIndex <= 0)
            return;

        var keyType = key.Substring(0, colonIndex);
        var oldValue = key.Substring(colonIndex + 1);
        if (!unicodeMappings!.TryGetValue(keyType, out var typeDict))
        {
            typeDict = new(StringComparer.OrdinalIgnoreCase);
            unicodeMappings[keyType] = typeDict;
        }

        typeDict[oldValue] = value;
    }

    internal readonly record struct ComplexLanguageMapping(string Language, string? Script, string? Region);

    internal readonly record struct VariantMapping(string Type, string Replacement, string? Prefix = null);
}

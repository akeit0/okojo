namespace Okojo.Globalization;

public static class LocaleData
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

    /// <summary>Whole-tag alias mappings (grandfathered tags etc.).</summary>
    public static Dictionary<string, string> TagMappings
    {
        get
        {
            EnsureLoaded();
            return tagMappings!;
        }
    }

    /// <summary>Language subtag alias mappings.</summary>
    public static Dictionary<string, string> LanguageMappings
    {
        get
        {
            EnsureLoaded();
            return languageMappings!;
        }
    }

    /// <summary>Complex language alias mappings that also replace script/region.</summary>
    public static Dictionary<string, ComplexLanguageMapping> ComplexLanguageMappings
    {
        get
        {
            EnsureLoaded();
            return complexLanguageMappings!;
        }
    }

    /// <summary>Region subtag alias mappings.</summary>
    public static Dictionary<string, string> RegionMappings
    {
        get
        {
            EnsureLoaded();
            return regionMappings!;
        }
    }

    /// <summary>Variant subtag alias mappings.</summary>
    public static Dictionary<string, VariantMapping> VariantMappings
    {
        get
        {
            EnsureLoaded();
            return variantMappings!;
        }
    }

    /// <summary>Language+variant composite alias mappings.</summary>
    public static Dictionary<string, string> LanguageVariantMappings
    {
        get
        {
            EnsureLoaded();
            return languageVariantMappings!;
        }
    }

    /// <summary>Script+region composite alias mappings.</summary>
    public static Dictionary<string, string> ScriptRegionMappings
    {
        get
        {
            EnsureLoaded();
            return scriptRegionMappings!;
        }
    }

    /// <summary>Unicode extension key/value alias mappings.</summary>
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

            var assembly = typeof(LocaleData).Assembly;
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

    /// <summary>A complex language alias: replacement language plus optional script/region.</summary>
    public readonly record struct ComplexLanguageMapping(string Language, string? Script, string? Region);

    /// <summary>A variant alias: replacement value, replacement type, and optional required prefix.</summary>
    public readonly record struct VariantMapping(string Type, string Replacement, string? Prefix = null);
}

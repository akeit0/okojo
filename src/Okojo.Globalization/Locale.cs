using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Okojo.Globalization;

/// <summary>Portable ECMA-402 locale tag parsing, validation, and canonicalization.</summary>
public static partial class Locale
{
    private static readonly ConcurrentDictionary<string, string?> ValidatedCanonicalLocaleCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string> CanonicalLocaleCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> GrandfatheredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        { "art-lojban", "jbo" },
        { "cel-gaulish", "xtg" },
        { "zh-guoyu", "zh" },
        { "zh-hakka", "hak" },
        { "zh-xiang", "hsn" },
        { "sgn-BR", "bzs" },
        { "sgn-CO", "csn" },
        { "sgn-DE", "gsg" },
        { "sgn-DK", "dsl" },
        { "sgn-ES", "ssp" },
        { "sgn-FR", "fsl" },
        { "sgn-GB", "bfi" },
        { "sgn-GR", "gss" },
        { "sgn-IE", "isg" },
        { "sgn-IT", "ise" },
        { "sgn-JP", "jsl" },
        { "sgn-MX", "mfs" },
        { "sgn-NI", "ncs" },
        { "sgn-NL", "dse" },
        { "sgn-NO", "nsl" },
        { "sgn-PT", "psr" },
        { "sgn-SE", "swl" },
        { "sgn-US", "ase" },
        { "sgn-ZA", "sfs" }
    };

    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "cmn", "zh" }, { "arb", "ar" }, { "swh", "sw" }, { "zsm", "ms" },
        { "ji", "yi" }, { "iw", "he" }, { "in", "id" }, { "jw", "jv" },
        { "mo", "ro" }, { "tl", "fil" }, { "sh", "sr-Latn" }
    };

    private static readonly Dictionary<string, string> RegionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "DD", "DE" }, { "YD", "YE" }, { "AN", "CW" }, { "CS", "RS" },
        { "YU", "RS" }, { "TP", "TL" }, { "ZR", "CD" }, { "BU", "MM" },
        { "SU", "RU" }, { "FX", "FR" }
    };

    private static readonly Dictionary<string, string> LikelyScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        { "aa", "Latn" }, { "ab", "Cyrl" }, { "af", "Latn" }, { "am", "Ethi" }, { "ar", "Arab" },
        { "as", "Beng" }, { "az", "Latn" }, { "be", "Cyrl" }, { "bg", "Cyrl" }, { "bn", "Beng" },
        { "bs", "Latn" }, { "ca", "Latn" }, { "cs", "Latn" }, { "cy", "Latn" }, { "da", "Latn" },
        { "de", "Latn" }, { "el", "Grek" }, { "en", "Latn" }, { "es", "Latn" }, { "et", "Latn" },
        { "eu", "Latn" }, { "fa", "Arab" }, { "fi", "Latn" }, { "fr", "Latn" }, { "ga", "Latn" },
        { "gl", "Latn" }, { "gu", "Gujr" }, { "he", "Hebr" }, { "hi", "Deva" }, { "hr", "Latn" },
        { "hu", "Latn" }, { "hy", "Armn" }, { "id", "Latn" }, { "is", "Latn" }, { "it", "Latn" },
        { "ja", "Jpan" }, { "ka", "Geor" }, { "kk", "Cyrl" }, { "km", "Khmr" }, { "kn", "Knda" },
        { "ko", "Kore" }, { "ky", "Cyrl" }, { "lo", "Laoo" }, { "lt", "Latn" }, { "lv", "Latn" },
        { "mk", "Cyrl" }, { "ml", "Mlym" }, { "mn", "Cyrl" }, { "mr", "Deva" }, { "ms", "Latn" },
        { "my", "Mymr" }, { "nb", "Latn" }, { "ne", "Deva" }, { "nl", "Latn" }, { "nn", "Latn" },
        { "no", "Latn" }, { "or", "Orya" }, { "pa", "Guru" }, { "pl", "Latn" }, { "ps", "Arab" },
        { "pt", "Latn" }, { "ro", "Latn" }, { "ru", "Cyrl" }, { "si", "Sinh" }, { "sk", "Latn" },
        { "sl", "Latn" }, { "sq", "Latn" }, { "sr", "Cyrl" }, { "sv", "Latn" }, { "sw", "Latn" },
        { "ta", "Taml" }, { "te", "Telu" }, { "tg", "Cyrl" }, { "th", "Thai" }, { "tk", "Latn" },
        { "tr", "Latn" }, { "uk", "Cyrl" }, { "und", "Latn" }, { "ur", "Arab" }, { "uz", "Latn" },
        { "vi", "Latn" }, { "zh", "Hans" }
    };

    private static readonly Dictionary<string, string> TValueAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "names", "prprname" }
    };

    [GeneratedRegex("^[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8})*$", RegexOptions.CultureInvariant,
        100)]
    private static partial Regex LanguageTagRegex();

    public static string RemoveUnicodeExtensions(string locale)
    {
        var extensionIndex = locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (extensionIndex == -1)
            return locale;

        var endIndex = locale.Length;
        for (var i = extensionIndex + 3; i < locale.Length - 1; i++)
            if (locale[i] == '-' && i + 2 < locale.Length && locale[i + 2] == '-')
            {
                endIndex = i;
                break;
            }

        return endIndex < locale.Length
            ? string.Concat(locale.AsSpan(0, extensionIndex), locale.AsSpan(endIndex))
            : locale.Substring(0, extensionIndex);
    }

    public static bool ContainsUnicodeExtension(string locale)
    {
        return locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool TryGetValidatedCanonicalLocale(string locale, out string canonicalized)
    {
        if (ValidatedCanonicalLocaleCache.TryGetValue(locale, out var cachedCanonicalized))
        {
            canonicalized = cachedCanonicalized ?? string.Empty;
            return cachedCanonicalized is not null;
        }

        if (!IsStructurallyValidLanguageTag(locale))
        {
            ValidatedCanonicalLocaleCache[locale] = null;
            canonicalized = string.Empty;
            return false;
        }

        canonicalized = CanonicalizeUnicodeLocaleId(locale);
        ValidatedCanonicalLocaleCache[locale] = canonicalized;
        return true;
    }

    public static bool IsStructurallyValidLanguageTag(string locale)
    {
        if (string.IsNullOrEmpty(locale))
            return false;

        foreach (var c in locale)
            if (c > 127 || c == '\0' || char.IsWhiteSpace(c) || c == '_')
                return false;

        if (!LanguageTagRegex().IsMatch(locale))
            return false;

        var parts = locale.Split('-');
        if (parts.Length == 0 || parts[0].Length == 0)
            return false;

        var firstPart = parts[0];
        if (string.Equals(firstPart, "x", StringComparison.OrdinalIgnoreCase))
            return false;
        if (firstPart.Length == 1)
            return false;
        if (firstPart.Length == 3 && char.IsDigit(firstPart[0]))
            return false;
        foreach (var c in firstPart)
            if (!char.IsLetter(c))
                return false;

        if (firstPart.Length == 4 || firstPart.Length > 8)
            return false;

        var seenSingletons = new HashSet<char>();
        var seenVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inExtension = false;
        var extensionType = '\0';
        var extensionHasSubtag = false;
        var hasScript = false;
        var hasRegion = false;

        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                return false;

            if (part.Length == 1)
            {
                var singleton = char.ToLowerInvariant(part[0]);
                if (inExtension && extensionType == 'x')
                {
                    extensionHasSubtag = true;
                    continue;
                }

                if (inExtension && !extensionHasSubtag)
                    return false;
                if (seenSingletons.Contains(singleton))
                    return false;

                seenSingletons.Add(singleton);
                inExtension = true;
                extensionType = singleton;
                extensionHasSubtag = false;
                continue;
            }

            if (inExtension)
            {
                extensionHasSubtag = true;
                if (extensionType == 'x')
                    continue;
                if (extensionType == 'u' && part.Length == 2 &&
                    (!char.IsLetterOrDigit(part[0]) || !char.IsLetter(part[1])))
                    return false;
            }
            else
            {
                if (part.Length == 4 && char.IsLetter(part[0]))
                {
                    var isAllLetters = IsAllLetters(part);
                    if (isAllLetters)
                    {
                        if (hasScript || hasRegion || seenVariants.Count > 0)
                            return false;
                        hasScript = true;
                    }
                    else if (char.IsDigit(part[0]))
                    {
                        if (!seenVariants.Add(part.ToLowerInvariant()))
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if ((part.Length == 2 && char.IsLetter(part[0])) ||
                         (part.Length == 3 && char.IsDigit(part[0])))
                {
                    if (hasRegion)
                        return false;
                    hasRegion = true;
                }
                else if (part.Length == 4 && char.IsDigit(part[0]))
                {
                    if (!seenVariants.Add(part.ToLowerInvariant()))
                        return false;
                }
                else if (part.Length >= 5 && part.Length <= 8)
                {
                    if (!seenVariants.Add(part.ToLowerInvariant()))
                        return false;
                }
                else
                {
                    return false;
                }
            }
        }

        if (inExtension && !extensionHasSubtag)
            return false;

        return ValidateTransformedExtension(locale);
    }

    public static string CanonicalizeUnicodeLocaleId(string locale)
    {
        if (CanonicalLocaleCache.TryGetValue(locale, out var cached))
            return cached;

        string canonical;
        if (OkojoIntlLocaleData.TagMappings.TryGetValue(locale, out var replacement))
        {
            canonical = replacement;
        }
        else if (GrandfatheredTags.TryGetValue(locale, out replacement))
        {
            canonical = replacement;
        }
        else
        {
            var parsed = ParseLanguageTag(locale);
            if (parsed.Language is not null)
            {
                if (OkojoIntlLocaleData.ComplexLanguageMappings.TryGetValue(parsed.Language, out var complex))
                {
                    parsed.Language = complex.Language;
                    if (parsed.Script is null && complex.Script is not null)
                        parsed.Script = complex.Script;
                    if (parsed.Region is null && complex.Region is not null)
                        parsed.Region = complex.Region;
                }
                else if (OkojoIntlLocaleData.LanguageMappings.TryGetValue(parsed.Language, out replacement) ||
                         LanguageAliases.TryGetValue(parsed.Language, out replacement))
                {
                    if (replacement.Contains('-'))
                    {
                        var replacementParts = replacement.Split('-');
                        parsed.Language = replacementParts[0];
                        if (replacementParts.Length > 1 && parsed.Script is null)
                            parsed.Script = replacementParts[1];
                    }
                    else
                    {
                        parsed.Language = replacement;
                    }
                }
            }

            if (parsed.Region is not null)
            {
                var script = parsed.Script;
                if (script is null && parsed.Language is not null)
                    LikelyScripts.TryGetValue(parsed.Language, out script);

                if (script is not null)
                {
                    var scriptRegionKey = script + "+" + parsed.Region;
                    if (OkojoIntlLocaleData.ScriptRegionMappings.TryGetValue(scriptRegionKey, out replacement))
                        parsed.Region = replacement;
                    else if (OkojoIntlLocaleData.RegionMappings.TryGetValue(parsed.Region, out replacement) ||
                             RegionAliases.TryGetValue(parsed.Region, out replacement))
                        parsed.Region = replacement;
                }
                else if (OkojoIntlLocaleData.RegionMappings.TryGetValue(parsed.Region, out replacement) ||
                         RegionAliases.TryGetValue(parsed.Region, out replacement))
                {
                    parsed.Region = replacement;
                }
            }

            if (parsed.Variants is not null && parsed.Variants.Count > 0)
                for (var i = parsed.Variants.Count - 1; i >= 0; i--)
                {
                    if (!OkojoIntlLocaleData.VariantMappings.TryGetValue(parsed.Variants[i], out var variantMapping))
                        continue;

                    if (string.Equals(variantMapping.Type, "language", StringComparison.Ordinal))
                    {
                        parsed.Language = variantMapping.Replacement;
                        parsed.Variants.RemoveAt(i);
                    }
                    else if (string.Equals(variantMapping.Type, "region", StringComparison.Ordinal))
                    {
                        if (parsed.Region is null)
                            parsed.Region = variantMapping.Replacement;
                        parsed.Variants.RemoveAt(i);
                    }
                    else
                    {
                        parsed.Variants[i] = variantMapping.Replacement;
                        if (variantMapping.Prefix is null)
                            continue;

                        for (var j = parsed.Variants.Count - 1; j >= 0; j--)
                            if (j != i && string.Equals(parsed.Variants[j], variantMapping.Prefix,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                parsed.Variants.RemoveAt(j);
                                if (j < i)
                                    i--;
                            }
                    }
                }

            if (parsed.Language is not null && parsed.Variants is not null && parsed.Variants.Count > 0)
                for (var i = parsed.Variants.Count - 1; i >= 0; i--)
                {
                    var key = parsed.Language + "+" + parsed.Variants[i].ToLowerInvariant();
                    if (!OkojoIntlLocaleData.LanguageVariantMappings.TryGetValue(key, out var newLanguage))
                        continue;
                    parsed.Language = newLanguage;
                    parsed.Variants.RemoveAt(i);
                }

            if (parsed.Variants is not null && parsed.Variants.Count > 1)
                parsed.Variants.Sort(StringComparer.OrdinalIgnoreCase);

            CanonicalizeExtensions(parsed);
            canonical = BuildCanonicalTag(parsed);
        }

        CanonicalLocaleCache[locale] = canonical;
        return canonical;
    }

    public static ParsedLanguageTag ParseLanguageTag(string tag)
    {
        var result = new ParsedLanguageTag();
        var parts = tag.Split('-');
        var index = 0;
        if (parts.Length == 0)
            return result;

        result.Language = parts[index++].ToLowerInvariant();
        while (index < parts.Length)
        {
            var part = parts[index];
            var partLower = part.ToLowerInvariant();
            if (part.Length == 1)
            {
                var extensionType = partLower[0];
                var extensionParts = new List<string> { partLower };
                index++;
                if (extensionType == 'x')
                    while (index < parts.Length)
                        extensionParts.Add(parts[index++].ToLowerInvariant());
                else
                    while (index < parts.Length && parts[index].Length != 1)
                        extensionParts.Add(parts[index++].ToLowerInvariant());

                result.Extensions ??= [];
                result.Extensions.Add(new() { Type = extensionType, Parts = extensionParts });
            }
            else if (part.Length == 4 && char.IsLetter(part[0]) && result.Script is null && result.Region is null &&
                     (result.Variants is null || result.Variants.Count == 0))
            {
                result.Script = char.ToUpperInvariant(part[0]) + partLower.Substring(1);
                index++;
            }
            else if ((part.Length == 2 && char.IsLetter(part[0])) || (part.Length == 3 && char.IsDigit(part[0])))
            {
                if (result.Region is null && (result.Variants is null || result.Variants.Count == 0))
                {
                    result.Region = part.Length == 2 ? part.ToUpperInvariant() : part;
                }
                else
                {
                    result.Variants ??= [];
                    result.Variants.Add(partLower);
                }

                index++;
            }
            else
            {
                result.Variants ??= [];
                result.Variants.Add(partLower);
                index++;
            }
        }

        return result;
    }

    private static void CanonicalizeExtensions(ParsedLanguageTag parsed)
    {
        if (parsed.Extensions is null)
            return;

        for (var i = 0; i < parsed.Extensions.Count; i++)
        {
            var ext = parsed.Extensions[i];
            var type = ext.Type;
            var parts = ext.Parts;
            if (type == 't' && parts.Count > 1)
            {
                var newParts = new List<string> { "t" };
                var tfields = new List<KeyValueParts>();
                string? currentKey = null;
                var currentValues = new List<string>();
                var tlangParts = new List<string>();
                var inTlang = true;

                for (var j = 1; j < parts.Count; j++)
                {
                    var part = parts[j];
                    if (part.Length == 2 && char.IsLetter(part[0]) && char.IsDigit(part[1]))
                    {
                        inTlang = false;
                        if (currentKey is not null)
                        {
                            tfields.Add(new() { Key = currentKey, Values = currentValues });
                            currentValues = [];
                        }

                        currentKey = part;
                    }
                    else if (inTlang)
                    {
                        tlangParts.Add(part);
                    }
                    else
                    {
                        currentValues.Add(TValueAliases.TryGetValue(part, out var alias) ? alias : part);
                    }
                }

                if (currentKey is not null)
                    tfields.Add(new() { Key = currentKey, Values = currentValues });

                if (tlangParts.Count > 0)
                {
                    if (OkojoIntlLocaleData.LanguageMappings.TryGetValue(tlangParts[0], out var tlangReplacement) ||
                        LanguageAliases.TryGetValue(tlangParts[0], out tlangReplacement))
                        tlangParts[0] = tlangReplacement;

                    var tlangPrefix = new List<string>();
                    var tlangVariants = new List<string>();
                    for (var k = 0; k < tlangParts.Count; k++)
                    {
                        var part = tlangParts[k];
                        if (k == 0)
                        {
                            tlangPrefix.Add(part);
                        }
                        else if (part.Length == 4 && char.IsLetter(part[0]) && tlangVariants.Count == 0)
                        {
                            tlangPrefix.Add(part);
                        }
                        else if ((part.Length == 2 && char.IsLetter(part[0])) ||
                                 (part.Length == 3 && char.IsDigit(part[0])))
                        {
                            if (tlangVariants.Count == 0)
                                tlangPrefix.Add(part);
                            else
                                tlangVariants.Add(part);
                        }
                        else
                        {
                            tlangVariants.Add(part);
                        }
                    }

                    tlangVariants.Sort(StringComparer.Ordinal);
                    newParts.AddRange(tlangPrefix);
                    newParts.AddRange(tlangVariants);
                }

                tfields.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
                foreach (var kv in tfields)
                {
                    newParts.Add(kv.Key);
                    newParts.AddRange(kv.Values);
                }

                parsed.Extensions[i] = new() { Type = type, Parts = newParts };
            }
            else if (type == 'u')
            {
                var newParts = new List<string> { "u" };
                var attributes = new List<string>();
                var keywords = new List<KeyValueParts>();
                string? currentKey = null;
                var currentValues = new List<string>();

                for (var j = 1; j < parts.Count; j++)
                {
                    var part = parts[j];
                    if (part.Length == 2 && char.IsLetter(part[0]) && char.IsLetter(part[1]) &&
                        currentKey is null && keywords.Count == 0 && attributes.Count == 0 && j == 1)
                    {
                        currentKey = part;
                    }
                    else if (part.Length == 2 && char.IsLetter(part[0]) && char.IsLetter(part[1]))
                    {
                        if (currentKey is not null)
                        {
                            keywords.Add(new() { Key = currentKey, Values = currentValues });
                            currentValues = [];
                        }

                        currentKey = part;
                    }
                    else if (currentKey is null)
                    {
                        attributes.Add(part);
                    }
                    else
                    {
                        currentValues.Add(part);
                    }
                }

                if (currentKey is not null)
                    keywords.Add(new() { Key = currentKey, Values = currentValues });

                if (keywords.Count > 1)
                {
                    var deduped = new List<KeyValueParts>(keywords.Count);
                    var seenKeywords = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var keyword in keywords)
                        if (seenKeywords.Add(keyword.Key))
                            deduped.Add(keyword);

                    keywords = deduped;
                }

                foreach (var kw in keywords)
                {
                    if (OkojoIntlLocaleData.UnicodeMappings.TryGetValue(kw.Key, out var valueAliases))
                    {
                        var fullValue = string.Join("-", kw.Values);
                        if (valueAliases.TryGetValue(fullValue, out var aliasedValue))
                        {
                            kw.Values.Clear();
                            foreach (var part in aliasedValue.Split('-'))
                                kw.Values.Add(part);
                        }
                        else
                        {
                            for (var k = 0; k < kw.Values.Count; k++)
                                if (valueAliases.TryGetValue(kw.Values[k], out aliasedValue))
                                    kw.Values[k] = aliasedValue;
                        }
                    }

                    kw.Values.RemoveAll(static v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
                }

                attributes.Sort(StringComparer.Ordinal);
                newParts.AddRange(attributes);
                keywords.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
                foreach (var kv in keywords)
                {
                    newParts.Add(kv.Key);
                    newParts.AddRange(kv.Values);
                }

                parsed.Extensions[i] = new() { Type = type, Parts = newParts };
            }
        }

        parsed.Extensions.Sort((a, b) => a.Type.CompareTo(b.Type));
    }

    private static string BuildCanonicalTag(ParsedLanguageTag parsed)
    {
        var result = new List<string>();
        if (parsed.Language is not null)
            result.Add(parsed.Language);
        if (parsed.Script is not null)
            result.Add(parsed.Script);
        if (parsed.Region is not null)
            result.Add(parsed.Region);
        if (parsed.Variants is not null)
            result.AddRange(parsed.Variants);
        if (parsed.Extensions is not null)
            foreach (var ext in parsed.Extensions)
                result.AddRange(ext.Parts);

        return string.Join("-", result);
    }

    private static bool ValidateTransformedExtension(string locale)
    {
        var tIndex = locale.IndexOf("-t-", StringComparison.OrdinalIgnoreCase);
        if (tIndex < 0)
            return true;

        var endIndex = locale.Length;
        for (var i = tIndex + 3; i < locale.Length - 1; i++)
        {
            if (locale[i] != '-' || i + 2 >= locale.Length || locale[i + 2] != '-' ||
                !char.IsLetterOrDigit(locale[i + 1]))
                continue;

            var nextChar = locale[i + 1];
            if (char.IsLetter(nextChar) && nextChar != 'x' && nextChar != 'X')
            {
                endIndex = i;
                break;
            }

            if (nextChar == 'x' || nextChar == 'X')
            {
                endIndex = i;
                break;
            }
        }

        var tExtension = locale.Substring(tIndex + 3, endIndex - tIndex - 3);
        if (string.IsNullOrEmpty(tExtension))
            return false;

        var parts = tExtension.Split('-');
        if (parts.Length == 0 || parts[0].Length == 0)
            return false;

        var index = 0;
        var inTlang = true;
        var tlangHasLanguage = false;
        var tlangHasScript = false;
        var tlangHasRegion = false;
        var currentTKeyHasValue = true;
        HashSet<string>? tlangSeenVariants = null;

        while (index < parts.Length)
        {
            var part = parts[index];
            if (part.Length == 2 && char.IsLetter(part[0]) && char.IsDigit(part[1]))
            {
                if (!currentTKeyHasValue)
                    return false;
                inTlang = false;
                currentTKeyHasValue = false;
                index++;
                continue;
            }

            if (inTlang)
            {
                if (!tlangHasLanguage)
                {
                    if (!IsValidTLangLanguage(part))
                        return false;
                    tlangHasLanguage = true;
                }
                else if (!tlangHasScript && part.Length == 4 && IsAllLetters(part))
                {
                    tlangHasScript = true;
                }
                else if (part.Length == 4 && char.IsDigit(part[0]))
                {
                    tlangSeenVariants ??= new(StringComparer.OrdinalIgnoreCase);
                    if (!tlangSeenVariants.Add(part))
                        return false;
                }
                else if (!tlangHasRegion &&
                         ((part.Length == 2 && IsAllLetters(part)) || (part.Length == 3 && IsAllDigits(part))))
                {
                    tlangHasRegion = true;
                }
                else if (IsValidVariant(part))
                {
                    tlangSeenVariants ??= new(StringComparer.OrdinalIgnoreCase);
                    if (!tlangSeenVariants.Add(part))
                        return false;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (part.Length < 3 || part.Length > 8)
                    return false;
                foreach (var c in part)
                    if (!char.IsLetterOrDigit(c))
                        return false;

                currentTKeyHasValue = true;
            }

            index++;
        }

        return inTlang || currentTKeyHasValue;
    }

    private static bool IsValidTLangLanguage(string part)
    {
        if (part.Length < 2 || part.Length == 4 || part.Length > 8)
            return false;
        return IsAllLetters(part);
    }

    public static bool IsValidVariant(string part)
    {
        if (part.Length >= 5 && part.Length <= 8)
            return part.All(char.IsLetterOrDigit);
        if (part.Length == 4 && char.IsDigit(part[0]))
            return part.All(char.IsLetterOrDigit);
        return false;
    }

    private static bool IsAllLetters(string part)
    {
        foreach (var c in part)
            if (!char.IsLetter(c))
                return false;

        return true;
    }

    private static bool IsAllDigits(string part)
    {
        foreach (var c in part)
            if (!char.IsDigit(c))
                return false;

        return true;
    }

    public static string GetLanguageSubtag(string locale)
    {
        var dash = locale.IndexOf('-');
        return dash >= 0 ? locale[..dash].ToLowerInvariant() : locale.ToLowerInvariant();
    }
}

public sealed class ParsedLanguageTag
{
    public string? Language { get; set; }
    public string? Script { get; set; }
    public string? Region { get; set; }
    public List<string>? Variants { get; set; }
    public List<ExtensionSubtag>? Extensions { get; set; }
}

public sealed class ExtensionSubtag
{
    public char Type { get; set; }
    public List<string> Parts { get; set; } = [];
}

public sealed class KeyValueParts
{
    public string Key { get; set; } = string.Empty;
    public List<string> Values { get; set; } = [];
}

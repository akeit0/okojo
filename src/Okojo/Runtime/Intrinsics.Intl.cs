using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Okojo.Runtime.Intl;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private static readonly ConcurrentDictionary<string, CultureInfo?> IntlCultureInfoCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> IntlSupportedLocaleCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> IntlSupportedLocaleBaseCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string> IntlCanonicalLocaleCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string> IntlResolvedLocaleCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string?> IntlValidatedCanonicalLocaleCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> IntlAvailableCultureNames =
        BuildIntlAvailableCultureNames();

    private static string[]? intlSupportedValuesOfCalendarsCache;
    private static string[]? intlSupportedValuesOfCollationsCache;
    private static string[]? intlSupportedValuesOfCurrenciesCache;
    private static string[]? intlSupportedValuesOfNumberingSystemsCache;
    private static string[]? intlSupportedValuesOfTimeZonesCache;
    private static string[]? intlSupportedValuesOfUnitsCache;

    private static readonly Dictionary<string, string> IntlGrandfatheredTags = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly Dictionary<string, string> IntlLanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "cmn", "zh" }, { "arb", "ar" }, { "swh", "sw" }, { "zsm", "ms" },
        { "ji", "yi" }, { "iw", "he" }, { "in", "id" }, { "jw", "jv" },
        { "mo", "ro" }, { "tl", "fil" }, { "sh", "sr-Latn" }
    };

    private static readonly Dictionary<string, string> IntlRegionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "DD", "DE" }, { "YD", "YE" }, { "AN", "CW" }, { "CS", "RS" },
        { "YU", "RS" }, { "TP", "TL" }, { "ZR", "CD" }, { "BU", "MM" },
        { "SU", "RU" }, { "FX", "FR" }
    };

    private static readonly Dictionary<string, string> IntlLikelyScripts = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly Dictionary<string, string> IntlTValueAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "names", "prprname" }
    };

    private static readonly string[] IntlLocaleMatcherValues = ["lookup", "best fit"];
    private static readonly string[] IntlCollatorUsageValues = ["sort", "search"];
    private static readonly string[] IntlCollatorSensitivityValues = ["base", "accent", "case", "variant"];
    private static readonly string[] IntlCollatorCaseFirstValues = ["upper", "lower", "false"];

    private static readonly Dictionary<string, HashSet<string>> IntlCollatorLocaleCollationSupport =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = new(StringComparer.Ordinal) { "default", "phonebk", "eor" },
            ["en"] = new(StringComparer.Ordinal) { "default", "ducet", "emoji", "eor" }
        };

    private static readonly string[] IntlSegmenterGranularityValues = ["grapheme", "word", "sentence"];
    private static readonly string[] IntlDurationFormatStyleValues = ["long", "short", "narrow", "digital"];
    private static readonly string[] IntlDurationFormatUnitStyleValues = ["long", "short", "narrow"];

    private static readonly string[] IntlDurationFormatUnitStyleWithNumericValues =
        ["long", "short", "narrow", "numeric", "2-digit"];

    private static readonly string[] IntlDurationFormatSubSecondStyleValues = ["long", "short", "narrow", "numeric"];
    private static readonly string[] IntlDurationFormatDisplayValues = ["auto", "always"];
    private static readonly string[] IntlNumberFormatStyleValues = ["decimal", "percent", "currency", "unit"];
    private static readonly string[] IntlNumberFormatCurrencyDisplayValues = ["code", "symbol", "narrowSymbol", "name"];
    private static readonly string[] IntlNumberFormatCurrencySignValues = ["standard", "accounting"];

    private static readonly string[] IntlNumberFormatSignDisplayValues =
        ["auto", "never", "always", "exceptZero", "negative"];

    private static readonly string[] IntlNumberFormatUseGroupingStringValues = ["auto", "always", "min2"];
    private static readonly string[] IntlNumberFormatUnitDisplayValues = ["short", "narrow", "long"];

    private static readonly string[] IntlNumberFormatNotationValues =
        ["standard", "scientific", "engineering", "compact"];

    private static readonly string[] IntlNumberFormatCompactDisplayValues = ["short", "long"];

    private static readonly string[] IntlNumberFormatRoundingModeValues =
        ["ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc", "halfEven"];

    private static readonly string[] IntlNumberFormatRoundingPriorityValues =
        ["auto", "morePrecision", "lessPrecision"];

    private static readonly string[] IntlNumberFormatTrailingZeroDisplayValues = ["auto", "stripIfInteger"];
    private static readonly string[] IntlDateTimeFormatHourCycleValues = ["h11", "h12", "h23", "h24"];
    private static readonly string[] IntlDateTimeFormatWeekdayValues = ["narrow", "short", "long"];
    private static readonly string[] IntlDateTimeFormatEraValues = ["narrow", "short", "long"];
    private static readonly string[] IntlDateTimeFormatYearValues = ["2-digit", "numeric"];
    private static readonly string[] IntlDateTimeFormatMonthValues = ["2-digit", "numeric", "narrow", "short", "long"];
    private static readonly string[] IntlDateTimeFormatDayValues = ["2-digit", "numeric"];
    private static readonly string[] IntlDateTimeFormatDayPeriodValues = ["narrow", "short", "long"];
    private static readonly string[] IntlDateTimeFormatHourValues = ["2-digit", "numeric"];
    private static readonly string[] IntlDateTimeFormatMinuteValues = ["2-digit", "numeric"];
    private static readonly string[] IntlDateTimeFormatSecondValues = ["2-digit", "numeric"];

    private static readonly string[] IntlDateTimeFormatTimeZoneNameValues =
        ["short", "long", "shortOffset", "longOffset", "shortGeneric", "longGeneric"];

    private static readonly string[] IntlDateTimeFormatFormatMatcherValues = ["basic", "best fit"];
    private static readonly string[] IntlDateTimeFormatStyleValues = ["full", "long", "medium", "short"];
    private static readonly string[] IntlPluralRulesTypeValues = ["cardinal", "ordinal"];

    private static readonly string[] IntlPluralRulesNotationValues =
        ["standard", "scientific", "engineering", "compact"];

    private static readonly string[] IntlRelativeTimeFormatStyleValues = ["long", "short", "narrow"];
    private static readonly string[] IntlRelativeTimeFormatNumericValues = ["always", "auto"];

    private static readonly string[] IntlDisplayNamesTypeValues =
        ["language", "region", "script", "currency", "calendar", "dateTimeField"];

    private static readonly string[] IntlDisplayNamesStyleValues = ["long", "short", "narrow"];
    private static readonly string[] IntlDisplayNamesFallbackValues = ["code", "none"];
    private static readonly string[] IntlDisplayNamesLanguageDisplayValues = ["dialect", "standard"];

    private static readonly string[] IntlSupportedValueTimeZones =
    [
        "Etc/GMT+1", "Etc/GMT+10", "Etc/GMT+11", "Etc/GMT+12",
        "Etc/GMT+2", "Etc/GMT+3", "Etc/GMT+4", "Etc/GMT+5", "Etc/GMT+6",
        "Etc/GMT+7", "Etc/GMT+8", "Etc/GMT+9", "Etc/GMT-1", "Etc/GMT-10",
        "Etc/GMT-11", "Etc/GMT-12", "Etc/GMT-13", "Etc/GMT-14", "Etc/GMT-2",
        "Etc/GMT-3", "Etc/GMT-4", "Etc/GMT-5", "Etc/GMT-6", "Etc/GMT-7",
        "Etc/GMT-8", "Etc/GMT-9", "UTC"
    ];

    private static readonly string[] IntlDurationRecordProperties =
    [
        "years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds",
        "nanoseconds"
    ];

    [GeneratedRegex("^[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8})*$", RegexOptions.CultureInvariant,
        100)]
    private static partial Regex IntlLanguageTagRegex();

    private void InstallIntlBuiltins()
    {
        // Intl is installed as a plain object on globalThis.
    }

    private JsPlainObject CreateIntlObject()
    {
        var intl = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };

        var localePrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var localeConstructor = CreateLocaleConstructor(localePrototype);
        InstallLocalePrototypeBuiltins(localePrototype, localeConstructor);

        var segmenterPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var segmentsPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var segmentIteratorPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = IteratorPrototype
        };
        var segmenterConstructor = CreateSegmenterConstructor(segmenterPrototype);
        InstallSegmentIteratorPrototypeBuiltins(segmentIteratorPrototype);
        InstallSegmentsPrototypeBuiltins(segmentsPrototype, segmentIteratorPrototype);
        InstallSegmenterPrototypeBuiltins(segmenterPrototype, segmenterConstructor, segmentsPrototype,
            segmentIteratorPrototype);

        var relativeTimeFormatPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var relativeTimeFormatConstructor = CreateRelativeTimeFormatConstructor(relativeTimeFormatPrototype);
        InstallRelativeTimeFormatPrototypeBuiltins(relativeTimeFormatPrototype, relativeTimeFormatConstructor);

        var durationFormatPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var durationFormatConstructor = CreateDurationFormatConstructor(durationFormatPrototype);
        InstallDurationFormatPrototypeBuiltins(durationFormatPrototype, durationFormatConstructor);

        var displayNamesPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var displayNamesConstructor = CreateDisplayNamesConstructor(displayNamesPrototype);
        InstallDisplayNamesPrototypeBuiltins(displayNamesPrototype, displayNamesConstructor);

        var listFormatPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var listFormatConstructor = CreateListFormatConstructor(listFormatPrototype);
        InstallListFormatPrototypeBuiltins(listFormatPrototype, listFormatConstructor);

        var collatorPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var collatorConstructor = CreateCollatorConstructor(collatorPrototype);
        InstallCollatorPrototypeBuiltins(collatorPrototype, collatorConstructor);

        var dateTimeFormatPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var dateTimeFormatConstructor = CreateDateTimeFormatConstructor(dateTimeFormatPrototype);
        InstallDateTimeFormatPrototypeBuiltins(dateTimeFormatPrototype, dateTimeFormatConstructor);

        var numberFormatPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var numberFormatConstructor = CreateNumberFormatConstructor(numberFormatPrototype);
        InstallNumberFormatPrototypeBuiltins(numberFormatPrototype, numberFormatConstructor);

        var pluralRulesPrototype = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };
        var pluralRulesConstructor = CreatePluralRulesConstructor(pluralRulesPrototype);
        InstallPluralRulesPrototypeBuiltins(pluralRulesPrototype, pluralRulesConstructor);

        var getCanonicalLocalesFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var locales = args.Length == 0 ? JsValue.Undefined : args[0];
            var canonicalized = CanonicalizeLocaleList(realm, locales);
            return CreateStringArray(realm, canonicalized);
        }, "getCanonicalLocales", 1);
        var supportedValuesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var key = realm.ToJsStringSlowPath(args.Length == 0 ? JsValue.Undefined : args[0]);
            var values = key switch
            {
                "calendar" => GetSupportedValuesOfCalendars(),
                "collation" => GetSupportedValuesOfCollations(),
                "currency" => GetSupportedValuesOfCurrencies(),
                "numberingSystem" => GetSupportedValuesOfNumberingSystems(),
                "timeZone" => GetSupportedValuesOfTimeZones(),
                "unit" => GetSupportedValuesOfUnits(),
                _ => throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid key: {key}")
            };

            return CreateStringArray(realm, values);
        }, "supportedValuesOf", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdGetCanonicalLocales, JsValue.FromObject(getCanonicalLocalesFn)),
            PropertyDefinition.Mutable(IdSupportedValuesOf, JsValue.FromObject(supportedValuesOfFn)),
            PropertyDefinition.Mutable(IdLocale, JsValue.FromObject(localeConstructor)),
            PropertyDefinition.Mutable(IdSegmenter, JsValue.FromObject(segmenterConstructor)),
            PropertyDefinition.Mutable(IdRelativeTimeFormat,
                JsValue.FromObject(relativeTimeFormatConstructor)),
            PropertyDefinition.Mutable(IdDurationFormat, JsValue.FromObject(durationFormatConstructor)),
            PropertyDefinition.Mutable(IdDisplayNames, JsValue.FromObject(displayNamesConstructor)),
            PropertyDefinition.Mutable(IdListFormat, JsValue.FromObject(listFormatConstructor)),
            PropertyDefinition.Mutable(IdCollator, JsValue.FromObject(collatorConstructor)),
            PropertyDefinition.Mutable(IdDateTimeFormat, JsValue.FromObject(dateTimeFormatConstructor)),
            PropertyDefinition.Mutable(IdNumberFormat, JsValue.FromObject(numberFormatConstructor)),
            PropertyDefinition.Mutable(IdPluralRules, JsValue.FromObject(pluralRulesConstructor)),
            PropertyDefinition.Data(IdSymbolToStringTag, JsValue.FromString("Intl"), configurable: true)
        ];
        intl.DefineNewPropertiesNoCollision(Realm, defs);
        return intl;
    }

    private static List<string> CanonicalizeLocaleList(JsRealm realm, in JsValue locales)
    {
        if (locales.IsUndefined)
            return [];

        var seen = new List<string>();
        var seenSet = new HashSet<string>(StringComparer.Ordinal);
        if (locales.IsNull)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Locales argument must not be null");

        if (locales.IsString)
        {
            AddCanonicalizedLocale(realm, seen, seenSet, locales);
            return seen;
        }

        if (!realm.TryToObject(locales, out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Locale list must be an object");

        if (obj is JsLocaleObject localeObject)
        {
            seen.Add(localeObject.Locale);
            seenSet.Add(localeObject.Locale);
            return seen;
        }

        if (obj is JsArray array)
        {
            var arrayLength = array.Length;
            for (uint k = 0; k < arrayLength; k++)
            {
                if (!array.TryGetElement(k, out var kValue))
                    continue;

                AddCanonicalizedLocale(realm, seen, seenSet, kValue);
            }

            return seen;
        }

        var len = GetArrayLikeLengthLong(realm, obj);
        for (long k = 0; k < len; k++)
        {
            var pk = k.ToString(CultureInfo.InvariantCulture);
            if (!JsRealm.HasPropertySlowPath(realm, obj, JsValue.FromString(pk)))
                continue;

            JsValue kValue;
            if ((ulong)k <= uint.MaxValue && obj.TryGetElement((uint)k, out var indexedValue))
                kValue = indexedValue;
            else
                _ = obj.TryGetProperty(pk, out kValue);

            AddCanonicalizedLocale(realm, seen, seenSet, kValue);
        }

        return seen;
    }

    private static string[] GetSupportedValuesOfCalendars()
    {
        return intlSupportedValuesOfCalendarsCache ??= OkojoIntlCalendarData.GetSupportedCalendars();
    }

    private static string[] GetSupportedValuesOfCollations()
    {
        return intlSupportedValuesOfCollationsCache ??=
        [
            .. IntlCollatorLocaleCollationSupport.Values
                .SelectMany(static values => values)
                .Where(static value => !string.Equals(value, "default", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
        ];
    }

    private static string[] GetSupportedValuesOfCurrencies()
    {
        return intlSupportedValuesOfCurrenciesCache ??= JsDisplayNamesObject.GetSupportedCurrencyCodes();
    }

    private static string[] GetSupportedValuesOfNumberingSystems()
    {
        return intlSupportedValuesOfNumberingSystemsCache ??=
            OkojoIntlNumberingSystemData.GetSupportedNumberingSystems();
    }

    private static string[] GetSupportedValuesOfTimeZones()
    {
        return intlSupportedValuesOfTimeZonesCache ??=
            [.. IntlSupportedValueTimeZones.OrderBy(static value => value, StringComparer.Ordinal)];
    }

    private static string[] GetSupportedValuesOfUnits()
    {
        return intlSupportedValuesOfUnitsCache ??= OkojoIntlUnitData.GetSupportedValues();
    }

    internal static CultureInfo ResolveRequestedLocaleCulture(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0)
            return CultureInfo.InvariantCulture;

        var requestedLocales = CanonicalizeLocaleList(realm, args[0]);
        if (requestedLocales.Count == 0)
            return CultureInfo.InvariantCulture;

        return GetCultureInfo(requestedLocales[0]) ?? CultureInfo.InvariantCulture;
    }

    internal static CultureInfo? GetCultureInfo(string locale)
    {
        if (string.IsNullOrEmpty(locale))
            return null;

        if (IntlCultureInfoCache.TryGetValue(locale, out var cached))
            return cached;

        var candidate = RemoveUnicodeExtensions(locale).Replace('_', '-');
        CultureInfo? culture = null;
        while (!string.IsNullOrEmpty(candidate))
        {
            if (IntlAvailableCultureNames.ContainsKey(candidate))
            {
                culture = new(candidate, false);
                break;
            }

            try
            {
                culture = new(candidate, false);
                IntlAvailableCultureNames.TryAdd(candidate, 0);
                break;
            }
            catch (CultureNotFoundException)
            {
            }

            var hyphenIndex = candidate.LastIndexOf('-');
            if (hyphenIndex <= 0)
                break;
            candidate = candidate[..hyphenIndex];
        }

        IntlCultureInfoCache[locale] = culture;
        return culture;
    }

    private static ConcurrentDictionary<string, byte> BuildIntlAvailableCultureNames()
    {
        var names = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures))
        {
            if (string.IsNullOrEmpty(culture.Name))
                continue;

            names.TryAdd(culture.Name.Replace('_', '-'), 0);
        }

        return names;
    }

    private static string RemoveUnicodeExtensions(string locale)
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

    private static bool ContainsUnicodeExtension(string locale)
    {
        return locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? GetRequestedLocaleNumberingSystem(IReadOnlyList<string> requestedLocales)
    {
        for (var i = 0; i < requestedLocales.Count; i++)
        {
            var locale = requestedLocales[i];
            if (!ContainsUnicodeExtension(locale))
                continue;

            var numberingSystem = ExtractNumberingSystemFromLocale(locale);
            if (numberingSystem is not null)
                return numberingSystem;
        }

        return null;
    }

    private static string? GetLocaleLanguageSubtag(string locale)
    {
        if (string.IsNullOrEmpty(locale))
            return null;

        var separatorIndex = locale.IndexOf('-');
        return separatorIndex < 0 ? locale : locale[..separatorIndex];
    }

    private static void AddCanonicalizedLocale(JsRealm realm, List<string> seen, HashSet<string> seenSet,
        in JsValue value)
    {
        if (!value.IsString && !value.IsObject)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Locale should be a string or object");
        if (value.IsNull)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Locale should be a string or object");

        if (value.TryGetObject(out var localeObj) && localeObj is JsLocaleObject locale)
        {
            if (seenSet.Add(locale.Locale))
                seen.Add(locale.Locale);
            return;
        }

        var tag = realm.ToJsStringSlowPath(value);
        if (!TryGetValidatedCanonicalLocale(tag, out var canonicalized))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid language tag: {tag}");
        if (seenSet.Add(canonicalized))
            seen.Add(canonicalized);
    }

    private static bool TryGetValidatedCanonicalLocale(string locale, out string canonicalized)
    {
        if (IntlValidatedCanonicalLocaleCache.TryGetValue(locale, out var cachedCanonicalized))
        {
            canonicalized = cachedCanonicalized ?? string.Empty;
            return cachedCanonicalized is not null;
        }

        if (!IsStructurallyValidLanguageTag(locale))
        {
            IntlValidatedCanonicalLocaleCache[locale] = null;
            canonicalized = string.Empty;
            return false;
        }

        canonicalized = CanonicalizeUnicodeLocaleId(locale);
        IntlValidatedCanonicalLocaleCache[locale] = canonicalized;
        return true;
    }

    private static bool IsStructurallyValidLanguageTag(string locale)
    {
        if (string.IsNullOrEmpty(locale))
            return false;

        foreach (var c in locale)
            if (c > 127 || c == '\0' || char.IsWhiteSpace(c) || c == '_')
                return false;

        if (!IntlLanguageTagRegex().IsMatch(locale))
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

    private static string CanonicalizeUnicodeLocaleId(string locale)
    {
        if (IntlCanonicalLocaleCache.TryGetValue(locale, out var cached))
            return cached;

        string canonical;
        if (OkojoIntlLocaleData.TagMappings.TryGetValue(locale, out var replacement))
        {
            canonical = replacement;
        }
        else if (IntlGrandfatheredTags.TryGetValue(locale, out replacement))
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
                         IntlLanguageAliases.TryGetValue(parsed.Language, out replacement))
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
                    IntlLikelyScripts.TryGetValue(parsed.Language, out script);

                if (script is not null)
                {
                    var scriptRegionKey = script + "+" + parsed.Region;
                    if (OkojoIntlLocaleData.ScriptRegionMappings.TryGetValue(scriptRegionKey, out replacement))
                        parsed.Region = replacement;
                    else if (OkojoIntlLocaleData.RegionMappings.TryGetValue(parsed.Region, out replacement) ||
                             IntlRegionAliases.TryGetValue(parsed.Region, out replacement))
                        parsed.Region = replacement;
                }
                else if (OkojoIntlLocaleData.RegionMappings.TryGetValue(parsed.Region, out replacement) ||
                         IntlRegionAliases.TryGetValue(parsed.Region, out replacement))
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

        IntlCanonicalLocaleCache[locale] = canonical;
        return canonical;
    }

    private static ParsedLanguageTag ParseLanguageTag(string tag)
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
                        currentValues.Add(IntlTValueAliases.TryGetValue(part, out var alias) ? alias : part);
                    }
                }

                if (currentKey is not null)
                    tfields.Add(new() { Key = currentKey, Values = currentValues });

                if (tlangParts.Count > 0)
                {
                    if (OkojoIntlLocaleData.LanguageMappings.TryGetValue(tlangParts[0], out var tlangReplacement) ||
                        IntlLanguageAliases.TryGetValue(tlangParts[0], out tlangReplacement))
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

    private static bool IsValidVariant(string part)
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

    private JsHostFunction CreateLocaleConstructor(JsPlainObject localePrototype)
    {
        JsHostFunction localeConstructor = null!;
        localeConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.Locale must be called with new");

            var tagValue = args.Length == 0 ? JsValue.Undefined : args[0];
            if (!tagValue.IsString && !tagValue.IsObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "First argument to Intl.Locale must be a string or Locale object");

            var tag = tagValue.TryGetObject(out var tagObject) && tagObject is JsLocaleObject localeArg
                ? localeArg.Locale
                : realm.ToJsStringSlowPath(tagValue);

            if (!IsStructurallyValidLanguageTag(tag))
                throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid language tag: {tag}");

            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, localeConstructor,
                localePrototype, "Locale");
            return CreateLocaleObject(realm, prototype, CanonicalizeUnicodeLocaleId(tag),
                args.Length > 1 ? args[1] : JsValue.Undefined);
        }, "Locale", 1, true);

        localeConstructor.InitializePrototypeProperty(localePrototype);
        return localeConstructor;
    }

    private JsHostFunction CreateSegmenterConstructor(JsPlainObject segmenterPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.Segmenter.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction segmenterConstructor = null!;
        segmenterConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.Segmenter must be called with new");

            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.Segmenter options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");
            var granularity =
                GetIntlStringOption(realm, options, "granularity", IntlSegmenterGranularityValues, "grapheme");

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var cultureInfo = GetCultureInfo(resolvedLocale) ?? CultureInfo.InvariantCulture;
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, segmenterConstructor,
                segmenterPrototype, "Segmenter");
            return new JsSegmenterObject(realm, prototype, resolvedLocale, granularity, cultureInfo);
        }, "Segmenter", 0, true);

        segmenterConstructor.InitializePrototypeProperty(segmenterPrototype);
        segmenterConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return segmenterConstructor;
    }

    private JsHostFunction CreateRelativeTimeFormatConstructor(JsPlainObject relativeTimeFormatPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.RelativeTimeFormat.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction relativeTimeFormatConstructor = null!;
        relativeTimeFormatConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Intl.RelativeTimeFormat must be called with new");

            var options = GetIntlConstructorOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.RelativeTimeFormat options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            string? numberingSystemOption = null;
            if (options.TryGetProperty("numberingSystem", out var numberingSystemValue) &&
                !numberingSystemValue.IsUndefined)
            {
                numberingSystemOption = realm.ToJsStringSlowPath(numberingSystemValue);
                if (!IsWellFormedNumberingSystem(numberingSystemOption))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid numberingSystem: {numberingSystemOption}");
            }

            var style = GetIntlStringOption(realm, options, "style", IntlRelativeTimeFormatStyleValues, "long");
            var numeric =
                GetIntlStringOption(realm, options, "numeric", IntlRelativeTimeFormatNumericValues, "always");

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var localeNumberingSystem = GetRequestedLocaleNumberingSystem(requestedLocales);

            string resolvedNumberingSystem;
            if (numberingSystemOption is not null && OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption))
                resolvedNumberingSystem = numberingSystemOption.ToLowerInvariant();
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
                resolvedNumberingSystem = localeNumberingSystem.ToLowerInvariant();
            else
                resolvedNumberingSystem = "latn";

            var finalResolvedLocale = resolvedLocale;
            var numberingSystemFromOptions = numberingSystemOption is not null &&
                                             OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption);
            if (numberingSystemFromOptions)
            {
                if (localeNumberingSystem is not null &&
                    string.Equals(numberingSystemOption, localeNumberingSystem, StringComparison.OrdinalIgnoreCase))
                    finalResolvedLocale = EnsureNumberingSystemInLocale(resolvedLocale, resolvedNumberingSystem);
                else
                    finalResolvedLocale = RemoveNumberingSystemFromLocale(resolvedLocale);
            }
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
            {
                finalResolvedLocale = EnsureNumberingSystemInLocale(resolvedLocale, resolvedNumberingSystem);
            }
            else
            {
                finalResolvedLocale = RemoveNumberingSystemFromLocale(resolvedLocale);
            }

            var cultureInfo = GetCultureInfo(finalResolvedLocale) ?? CultureInfo.InvariantCulture;
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, relativeTimeFormatConstructor,
                relativeTimeFormatPrototype, "RelativeTimeFormat");
            return new JsRelativeTimeFormatObject(realm, prototype, finalResolvedLocale, resolvedNumberingSystem,
                style, numeric, cultureInfo);
        }, "RelativeTimeFormat", 0, true);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdSupportedLocalesOf, JsValue.FromObject(supportedLocalesOfFn))
        ];
        relativeTimeFormatConstructor.InitializePrototypeProperty(relativeTimeFormatPrototype);
        relativeTimeFormatConstructor.DefineNewPropertiesNoCollision(Realm, defs);
        return relativeTimeFormatConstructor;
    }

    private JsHostFunction CreateDurationFormatConstructor(JsPlainObject durationFormatPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.DurationFormat.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction durationFormatConstructor = null!;
        durationFormatConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.DurationFormat must be called with new");

            var options = GetIntlConstructorOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.DurationFormat options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            string? numberingSystemOption = null;
            if (options.TryGetProperty("numberingSystem", out var numberingSystemValue) &&
                !numberingSystemValue.IsUndefined)
            {
                numberingSystemOption = realm.ToJsStringSlowPath(numberingSystemValue);
                if (!IsWellFormedNumberingSystem(numberingSystemOption))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid numberingSystem: {numberingSystemOption}");
            }

            var style = GetIntlStringOption(realm, options, "style", IntlDurationFormatStyleValues, "short");
            var baseStyle = string.Equals(style, "digital", StringComparison.Ordinal) ? "short" : style;
            var baseTimeStyle = style switch
            {
                "long" => "long",
                "short" => "short",
                "narrow" => "narrow",
                "digital" => "numeric",
                _ => "short"
            };

            static bool IsNumericLike(string value)
            {
                return value is "numeric" or "2-digit";
            }

            static string GetDurationUnitStyleOption(JsRealm realm, JsObject options, string name,
                string[] allowedValues,
                string fallback, out bool wasDefined)
            {
                if (!options.TryGetProperty(name, out var value) || value.IsUndefined)
                {
                    wasDefined = false;
                    return fallback;
                }

                wasDefined = true;
                var stringValue = realm.ToJsStringSlowPath(value);
                if (!allowedValues.Contains(stringValue, StringComparer.Ordinal))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid value for {name}: {stringValue}");
                return stringValue;
            }

            static string GetDurationDisplayFallback(bool isExplicitStyle)
            {
                return isExplicitStyle ? "always" : "auto";
            }

            var yearsStyle = GetDurationUnitStyleOption(realm, options, "years", IntlDurationFormatUnitStyleValues,
                baseStyle,
                out var yearsStyleDefined);
            var yearsDisplay = GetIntlStringOption(realm, options, "yearsDisplay", IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(yearsStyleDefined));
            var monthsStyle = GetDurationUnitStyleOption(realm, options, "months", IntlDurationFormatUnitStyleValues,
                baseStyle,
                out var monthsStyleDefined);
            var monthsDisplay = GetIntlStringOption(realm, options, "monthsDisplay", IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(monthsStyleDefined));
            var weeksStyle = GetDurationUnitStyleOption(realm, options, "weeks", IntlDurationFormatUnitStyleValues,
                baseStyle,
                out var weeksStyleDefined);
            var weeksDisplay = GetIntlStringOption(realm, options, "weeksDisplay", IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(weeksStyleDefined));
            var daysStyle = GetDurationUnitStyleOption(realm, options, "days", IntlDurationFormatUnitStyleValues,
                baseStyle,
                out var daysStyleDefined);
            var daysDisplay = GetIntlStringOption(realm, options, "daysDisplay", IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(daysStyleDefined));

            var hoursDefault = string.Equals(style, "digital", StringComparison.Ordinal) ? "numeric" : baseTimeStyle;
            var hoursStyle = GetDurationUnitStyleOption(realm, options, "hours",
                IntlDurationFormatUnitStyleWithNumericValues,
                hoursDefault, out var hoursStyleDefined);
            var hoursDisplay = GetIntlStringOption(realm, options, "hoursDisplay", IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(hoursStyleDefined));

            var minutesDefault = IsNumericLike(hoursStyle) ? "2-digit" : baseTimeStyle;
            var minutesStyle = GetDurationUnitStyleOption(realm, options, "minutes",
                IntlDurationFormatUnitStyleWithNumericValues,
                minutesDefault, out var minutesStyleDefined);
            var minutesDisplay = GetIntlStringOption(realm, options, "minutesDisplay",
                IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(minutesStyleDefined));
            if (IsNumericLike(hoursStyle) && !IsNumericLike(minutesStyle))
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "minutes style must be numeric or 2-digit when hours uses numeric or 2-digit");

            var secondsDefault = IsNumericLike(minutesStyle) ? "2-digit" : baseTimeStyle;
            var secondsStyle = GetDurationUnitStyleOption(realm, options, "seconds",
                IntlDurationFormatUnitStyleWithNumericValues,
                secondsDefault, out var secondsStyleDefined);
            var secondsDisplay = GetIntlStringOption(realm, options, "secondsDisplay",
                IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(secondsStyleDefined));
            if (IsNumericLike(minutesStyle) && !IsNumericLike(secondsStyle))
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "seconds style must be numeric or 2-digit when minutes uses numeric or 2-digit");

            var millisecondsDefault = IsNumericLike(secondsStyle) ? "numeric" : baseTimeStyle;
            var millisecondsStyle = GetDurationUnitStyleOption(realm, options, "milliseconds",
                IntlDurationFormatSubSecondStyleValues,
                millisecondsDefault, out var millisecondsStyleDefined);
            var millisecondsDisplay = GetIntlStringOption(realm, options, "millisecondsDisplay",
                IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(millisecondsStyleDefined));
            if (IsNumericLike(secondsStyle) && !string.Equals(millisecondsStyle, "numeric", StringComparison.Ordinal))
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "milliseconds style must be numeric when seconds uses numeric or 2-digit");

            var microsecondsDefault = string.Equals(millisecondsStyle, "numeric", StringComparison.Ordinal)
                ? "numeric"
                : baseTimeStyle;
            var microsecondsStyle = GetDurationUnitStyleOption(realm, options, "microseconds",
                IntlDurationFormatSubSecondStyleValues,
                microsecondsDefault, out var microsecondsStyleDefined);
            var microsecondsDisplay = GetIntlStringOption(realm, options, "microsecondsDisplay",
                IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(microsecondsStyleDefined));
            if (string.Equals(millisecondsStyle, "numeric", StringComparison.Ordinal) &&
                !string.Equals(microsecondsStyle, "numeric", StringComparison.Ordinal))
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "microseconds style must be numeric when milliseconds uses numeric");

            var nanosecondsDefault = string.Equals(microsecondsStyle, "numeric", StringComparison.Ordinal)
                ? "numeric"
                : baseTimeStyle;
            var nanosecondsStyle = GetDurationUnitStyleOption(realm, options, "nanoseconds",
                IntlDurationFormatSubSecondStyleValues,
                nanosecondsDefault, out var nanosecondsStyleDefined);
            var nanosecondsDisplay = GetIntlStringOption(realm, options, "nanosecondsDisplay",
                IntlDurationFormatDisplayValues,
                GetDurationDisplayFallback(nanosecondsStyleDefined));
            if (string.Equals(microsecondsStyle, "numeric", StringComparison.Ordinal) &&
                !string.Equals(nanosecondsStyle, "numeric", StringComparison.Ordinal))
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "nanoseconds style must be numeric when microseconds uses numeric");

            int? fractionalDigits = null;
            if (options.TryGetProperty("fractionalDigits", out var fractionalDigitsValue) &&
                !fractionalDigitsValue.IsUndefined)
            {
                var digits = realm.ToNumberSlowPath(fractionalDigitsValue);
                if (double.IsNaN(digits) || digits < 0 || digits > 9)
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        "fractionalDigits must be between 0 and 9");
                fractionalDigits = (int)Math.Floor(digits);
            }

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var localeNumberingSystem = GetRequestedLocaleNumberingSystem(requestedLocales);

            string resolvedNumberingSystem;
            if (numberingSystemOption is not null && OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption))
                resolvedNumberingSystem = numberingSystemOption.ToLowerInvariant();
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
                resolvedNumberingSystem = localeNumberingSystem.ToLowerInvariant();
            else
                resolvedNumberingSystem = "latn";

            var finalResolvedLocale = resolvedLocale;
            if (numberingSystemOption is not null && OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption))
                finalResolvedLocale = localeNumberingSystem is not null &&
                                      string.Equals(localeNumberingSystem, numberingSystemOption,
                                          StringComparison.OrdinalIgnoreCase)
                    ? EnsureNumberingSystemInLocale(resolvedLocale, resolvedNumberingSystem)
                    : RemoveNumberingSystemFromLocale(resolvedLocale);
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
                finalResolvedLocale = EnsureNumberingSystemInLocale(resolvedLocale, resolvedNumberingSystem);
            else if (localeNumberingSystem is not null)
                finalResolvedLocale = RemoveNumberingSystemFromLocale(resolvedLocale);

            var cultureInfo = GetCultureInfo(finalResolvedLocale) ?? CultureInfo.InvariantCulture;
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, durationFormatConstructor,
                durationFormatPrototype, "DurationFormat");
            return new JsDurationFormatObject(realm, prototype, finalResolvedLocale, style, resolvedNumberingSystem,
                cultureInfo, yearsStyle, monthsStyle, weeksStyle, daysStyle, hoursStyle, minutesStyle, secondsStyle,
                millisecondsStyle, microsecondsStyle, nanosecondsStyle, yearsDisplay, monthsDisplay, weeksDisplay,
                daysDisplay, hoursDisplay, minutesDisplay, secondsDisplay, millisecondsDisplay, microsecondsDisplay,
                nanosecondsDisplay, fractionalDigits);
        }, "DurationFormat", 0, true);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdLength, JsValue.FromInt32(0), configurable: true),
            PropertyDefinition.Mutable(IdSupportedLocalesOf, JsValue.FromObject(supportedLocalesOfFn))
        ];
        durationFormatConstructor.InitializePrototypeProperty(durationFormatPrototype);
        durationFormatConstructor.DefineNewPropertiesNoCollision(Realm, defs);
        return durationFormatConstructor;
    }

    private JsHostFunction CreateNumberFormatConstructor(JsPlainObject numberFormatPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.NumberFormat.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction numberFormatConstructor = null!;
        numberFormatConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;

            var options = GetIntlConstructorOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.NumberFormat options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            string? numberingSystemOption = null;
            if (options.TryGetProperty("numberingSystem", out var numberingSystemValue) &&
                !numberingSystemValue.IsUndefined)
            {
                numberingSystemOption = realm.ToJsStringSlowPath(numberingSystemValue);
                if (!IsWellFormedNumberingSystem(numberingSystemOption))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid numberingSystem: {numberingSystemOption}");
            }

            var style = GetIntlStringOption(realm, options, "style", IntlNumberFormatStyleValues, "decimal");

            string? currencyOption = null;
            if (options.TryGetProperty("currency", out var currencyValue) && !currencyValue.IsUndefined)
            {
                currencyOption = realm.ToJsStringSlowPath(currencyValue).ToUpperInvariant();
                if (!IsWellFormedCurrencyCode(currencyOption))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid currency code: {currencyOption}");
            }

            var currencyDisplay = GetIntlStringOption(realm, options, "currencyDisplay",
                IntlNumberFormatCurrencyDisplayValues, "symbol");
            var currencySign = GetIntlStringOption(realm, options, "currencySign",
                IntlNumberFormatCurrencySignValues, "standard");

            string? unitOption = null;
            if (options.TryGetProperty("unit", out var unitValue) && !unitValue.IsUndefined)
                unitOption = realm.ToJsStringSlowPath(unitValue);

            var unitDisplay =
                GetIntlStringOption(realm, options, "unitDisplay", IntlNumberFormatUnitDisplayValues, "short");
            var notation =
                GetIntlStringOption(realm, options, "notation", IntlNumberFormatNotationValues, "standard");

            var isCurrency = string.Equals(style, "currency", StringComparison.Ordinal);
            var isUnit = string.Equals(style, "unit", StringComparison.Ordinal);
            var isPercent = string.Equals(style, "percent", StringComparison.Ordinal);
            var isCompact = string.Equals(notation, "compact", StringComparison.Ordinal);
            var isStandardNotation = string.Equals(notation, "standard", StringComparison.Ordinal);

            if (isCurrency && currencyOption is null)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Currency code is required with currency style");
            if (unitOption is not null && !IsWellFormedUnitIdentifier(unitOption))
                throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid unit identifier: {unitOption}");
            if (isUnit && unitOption is null)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Unit is required with unit style");

            var currency = isCurrency ? currencyOption : null;
            var unit = isUnit ? unitOption : null;

            var minimumIntegerDigits = GetIntlNumberOption(realm, options, "minimumIntegerDigits", 1, 21, 1);
            var defaultCurrencyDigits = isCurrency && currency is not null && isStandardNotation
                ? GetCurrencyDigits(currency)
                : 2;
            var defaultMinFractionDigits = isCurrency && isStandardNotation ? defaultCurrencyDigits : 0;
            var defaultMaxFractionDigits = isCurrency && isStandardNotation ? defaultCurrencyDigits :
                isPercent ? 0 :
                isCompact ? 0 : 3;

            var minFracValue = options.TryGetProperty("minimumFractionDigits", out var minFracRaw)
                ? minFracRaw
                : JsValue.Undefined;
            var maxFracValue = options.TryGetProperty("maximumFractionDigits", out var maxFracRaw)
                ? maxFracRaw
                : JsValue.Undefined;
            var minSigValue = options.TryGetProperty("minimumSignificantDigits", out var minSigRaw)
                ? minSigRaw
                : JsValue.Undefined;
            var maxSigValue = options.TryGetProperty("maximumSignificantDigits", out var maxSigRaw)
                ? maxSigRaw
                : JsValue.Undefined;

            var minimumFractionDigits = minFracValue.IsUndefined
                ? defaultMinFractionDigits
                : GetIntlNumberOptionValue(realm, minFracValue, "minimumFractionDigits", 0, 100);
            var maxFractionMinimum = minFracValue.IsUndefined ? 0 : minimumFractionDigits;
            var maximumFractionDigits = maxFracValue.IsUndefined
                ? Math.Max(minimumFractionDigits, defaultMaxFractionDigits)
                : GetIntlNumberOptionValue(realm, maxFracValue, "maximumFractionDigits", maxFractionMinimum, 100);
            if (minimumFractionDigits > maximumFractionDigits)
                minimumFractionDigits = maximumFractionDigits;

            int? minimumSignificantDigits = null;
            int? maximumSignificantDigits = null;
            var minimumSignificantDigitsExplicit = !minSigValue.IsUndefined;
            var maximumSignificantDigitsExplicit = !maxSigValue.IsUndefined;
            if (minimumSignificantDigitsExplicit)
                minimumSignificantDigits =
                    GetIntlNumberOptionValue(realm, minSigValue, "minimumSignificantDigits", 1, 21);
            if (maximumSignificantDigitsExplicit)
                maximumSignificantDigits = GetIntlNumberOptionValue(realm, maxSigValue, "maximumSignificantDigits",
                    minimumSignificantDigits ?? 1, 21);
            if (!minimumSignificantDigitsExplicit && maximumSignificantDigitsExplicit)
                minimumSignificantDigits = 1;
            if (minimumSignificantDigits is not null && maximumSignificantDigits is not null &&
                minimumSignificantDigits > maximumSignificantDigits)
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "minimumSignificantDigits must not be greater than maximumSignificantDigits");

            var roundingIncrement = GetIntlRoundingIncrementOption(realm, options);
            var roundingMode = GetIntlStringOption(realm, options, "roundingMode",
                IntlNumberFormatRoundingModeValues, "halfExpand");
            var roundingPriority = GetIntlStringOption(realm, options, "roundingPriority",
                IntlNumberFormatRoundingPriorityValues, "auto");
            var trailingZeroDisplay = GetIntlStringOption(realm, options, "trailingZeroDisplay",
                IntlNumberFormatTrailingZeroDisplayValues, "auto");
            if (roundingIncrement != 1)
            {
                if (!string.Equals(roundingPriority, "auto", StringComparison.Ordinal) ||
                    minimumSignificantDigitsExplicit || maximumSignificantDigitsExplicit)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "roundingIncrement is incompatible with significant-digits or roundingPriority options");

                if (minimumFractionDigits != maximumFractionDigits)
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        "roundingIncrement requires matching fraction digit bounds");
            }

            var compactDisplay = GetIntlStringOption(realm, options, "compactDisplay",
                IntlNumberFormatCompactDisplayValues, "short");
            var useGrouping = GetIntlUseGroupingOption(realm, options, notation);
            var signDisplay =
                GetIntlStringOption(realm, options, "signDisplay", IntlNumberFormatSignDisplayValues, "auto");

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = RemoveUnsupportedNumberFormatLocaleExtensions(ResolveIntlLocale(requestedLocales));
            var localeNumberingSystem = GetRequestedLocaleNumberingSystem(requestedLocales);

            string resolvedNumberingSystem;
            if (numberingSystemOption is not null && OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption))
                resolvedNumberingSystem = numberingSystemOption.ToLowerInvariant();
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
                resolvedNumberingSystem = localeNumberingSystem.ToLowerInvariant();
            else
                resolvedNumberingSystem = "latn";

            var finalResolvedLocale = resolvedLocale;
            if (numberingSystemOption is not null && OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption))
                finalResolvedLocale = localeNumberingSystem is not null &&
                                      string.Equals(localeNumberingSystem, numberingSystemOption,
                                          StringComparison.OrdinalIgnoreCase)
                    ? EnsureNumberingSystemInLocale(resolvedLocale, resolvedNumberingSystem)
                    : RemoveNumberingSystemFromLocale(resolvedLocale);
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
                finalResolvedLocale = EnsureNumberingSystemInLocale(resolvedLocale, resolvedNumberingSystem);
            else if (localeNumberingSystem is not null)
                finalResolvedLocale = RemoveNumberingSystemFromLocale(resolvedLocale);

            var cultureInfo = GetCultureInfo(finalResolvedLocale) ?? CultureInfo.InvariantCulture;
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, numberFormatConstructor,
                numberFormatPrototype, "NumberFormat");
            return new JsNumberFormatObject(realm, prototype, finalResolvedLocale, resolvedNumberingSystem, style,
                currency, currencyDisplay, currencySign, unit, unitDisplay, notation, compactDisplay,
                minimumIntegerDigits, minimumFractionDigits, maximumFractionDigits,
                minimumSignificantDigits, maximumSignificantDigits,
                minimumSignificantDigitsExplicit, maximumSignificantDigitsExplicit,
                useGrouping, signDisplay, roundingMode, roundingPriority, roundingIncrement, trailingZeroDisplay,
                cultureInfo);
        }, "NumberFormat", 0, true);

        numberFormatConstructor.InitializePrototypeProperty(numberFormatPrototype);
        numberFormatConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return numberFormatConstructor;
    }

    private JsHostFunction CreateListFormatConstructor(JsPlainObject listFormatPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.ListFormat.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction listFormatConstructor = null!;
        listFormatConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.ListFormat must be called with new");

            var options = GetIntlObjectOnlyOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.ListFormat options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");
            var type = GetIntlStringOption(realm, options, "type", ["conjunction", "disjunction", "unit"],
                "conjunction");
            var style = GetIntlStringOption(realm, options, "style", ["long", "short", "narrow"], "long");

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, listFormatConstructor,
                listFormatPrototype, "ListFormat");
            return new JsListFormatObject(realm, prototype, resolvedLocale, type, style);
        }, "ListFormat", 0, true);

        listFormatConstructor.InitializePrototypeProperty(listFormatPrototype);
        listFormatConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return listFormatConstructor;
    }

    private JsHostFunction CreateDisplayNamesConstructor(JsPlainObject displayNamesPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.DisplayNames.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction displayNamesConstructor = null!;
        displayNamesConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.DisplayNames must be called with new");

            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, displayNamesConstructor,
                displayNamesPrototype, "DisplayNames");
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);

            var optionsValue = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (!optionsValue.TryGetObject(out var options))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.DisplayNames options must be an object");

            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");
            var style = GetIntlStringOption(realm, options, "style", IntlDisplayNamesStyleValues, "long");
            if (!options.TryGetProperty("type", out var typeValue) || typeValue.IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.DisplayNames requires a type option");

            var type = GetIntlStringOptionValue(realm, typeValue, "type", IntlDisplayNamesTypeValues);
            var fallback = GetIntlStringOption(realm, options, "fallback", IntlDisplayNamesFallbackValues, "code");
            var languageDisplay = GetIntlStringOption(realm, options, "languageDisplay",
                IntlDisplayNamesLanguageDisplayValues, "dialect");

            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var finalResolvedLocale = CanonicalizeUnicodeLocaleId(RemoveUnicodeExtensions(resolvedLocale));
            var cultureInfo = GetCultureInfo(finalResolvedLocale) ?? CultureInfo.InvariantCulture;
            return new JsDisplayNamesObject(realm, prototype, finalResolvedLocale, type, style, fallback,
                string.Equals(type, "language", StringComparison.Ordinal) ? languageDisplay : null, cultureInfo);
        }, "DisplayNames", 2, true);

        displayNamesConstructor.InitializePrototypeProperty(displayNamesPrototype);
        displayNamesConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return displayNamesConstructor;
    }

    private JsHostFunction CreateDateTimeFormatConstructor(JsPlainObject dateTimeFormatPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.DateTimeFormat.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction dateTimeFormatConstructor = null!;
        dateTimeFormatConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var options = GetIntlConstructorOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.DateTimeFormat options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            string? calendarOption = null;
            if (options.TryGetProperty("calendar", out var calendarValue) && !calendarValue.IsUndefined)
            {
                calendarOption = realm.ToJsStringSlowPath(calendarValue).ToLowerInvariant();
                if (!IsAsciiLowerAlphaNumericTypeSequence(calendarOption))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid calendar option: {calendarOption}");
                if (OkojoIntlLocaleData.UnicodeMappings.TryGetValue("ca", out var calendarMappings) &&
                    calendarMappings.TryGetValue(calendarOption, out var calendarAlias))
                    calendarOption = calendarAlias;
            }

            string? numberingSystemOption = null;
            if (options.TryGetProperty("numberingSystem", out var numberingSystemValue) &&
                !numberingSystemValue.IsUndefined)
            {
                numberingSystemOption = realm.ToJsStringSlowPath(numberingSystemValue);
                if (!IsWellFormedNumberingSystem(numberingSystemOption))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid numberingSystem: {numberingSystemOption}");
            }

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var baseResolvedLocale = CanonicalizeUnicodeLocaleId(RemoveUnicodeExtensions(resolvedLocale));
            var cultureInfo = GetCultureInfo(resolvedLocale) ?? CultureInfo.InvariantCulture;
            string? localeCalendar = null;
            string? localeHourCycle = null;
            string? localeNumberingSystem = null;
            Dictionary<string, string?> localeOtherUnicodeKeywords = new(StringComparer.Ordinal);
            List<string> localeUnicodeAttributes = [];
            List<ExtensionSubtag> localeOtherExtensions = [];
            if (requestedLocales.Count > 0 && ContainsUnicodeExtension(requestedLocales[0]))
            {
                var parsed = ParseLanguageTag(requestedLocales[0]);
                ExtractLocaleComponents(parsed, out _, out _, out _, out _,
                    out localeCalendar, out _, out _, out localeHourCycle, out localeNumberingSystem,
                    out _, out _, out localeUnicodeAttributes, out localeOtherUnicodeKeywords,
                    out localeOtherExtensions);
            }

            string resolvedNumberingSystem;
            if (numberingSystemOption is not null && OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption))
                resolvedNumberingSystem = numberingSystemOption.ToLowerInvariant();
            else if (localeNumberingSystem is not null &&
                     OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem))
                resolvedNumberingSystem = localeNumberingSystem.ToLowerInvariant();
            else
                resolvedNumberingSystem = "latn";

            var calendar = ResolveDateTimeFormatCalendar(localeCalendar, calendarOption);
            bool? hour12 = options.TryGetProperty("hour12", out var hour12Value) && !hour12Value.IsUndefined
                ? JsRealm.ToBoolean(hour12Value)
                : null;
            var hourCycleExplicit = options.TryGetProperty("hourCycle", out var explicitHourCycleValue) &&
                                    !explicitHourCycleValue.IsUndefined;
            var hourCycle = hourCycleExplicit
                ? GetIntlStringOptionValue(realm, explicitHourCycleValue, "hourCycle",
                    IntlDateTimeFormatHourCycleValues)
                : localeHourCycle ?? GetDefaultHourCycle(cultureInfo);
            if (hour12.HasValue)
                hourCycle = hour12.Value
                    ? resolvedLocale.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "h11" : "h12"
                    : "h23";

            var hasTimeZoneOption =
                options.TryGetProperty("timeZone", out var timeZoneValue) && !timeZoneValue.IsUndefined;
            var timeZone =
                ValidateAndCanonicalizeDateTimeFormatTimeZone(realm,
                    hasTimeZoneOption ? timeZoneValue : JsValue.Undefined);
            var weekday = GetIntlStringOption(realm, options, "weekday", IntlDateTimeFormatWeekdayValues, null!);
            var era = GetIntlStringOption(realm, options, "era", IntlDateTimeFormatEraValues, null!);
            var year = GetIntlStringOption(realm, options, "year", IntlDateTimeFormatYearValues, null!);
            var month = GetIntlStringOption(realm, options, "month", IntlDateTimeFormatMonthValues, null!);
            var day = GetIntlStringOption(realm, options, "day", IntlDateTimeFormatDayValues, null!);
            var dayPeriod =
                GetIntlStringOption(realm, options, "dayPeriod", IntlDateTimeFormatDayPeriodValues, null!);
            var hour = GetIntlStringOption(realm, options, "hour", IntlDateTimeFormatHourValues, null!);
            var minute = GetIntlStringOption(realm, options, "minute", IntlDateTimeFormatMinuteValues, null!);
            var second = GetIntlStringOption(realm, options, "second", IntlDateTimeFormatSecondValues, null!);
            int? fractionalSecondDigits = null;
            if (options.TryGetProperty("fractionalSecondDigits", out var fractionalSecondDigitsRaw) &&
                !fractionalSecondDigitsRaw.IsUndefined)
            {
                var number = realm.ToNumberSlowPath(fractionalSecondDigitsRaw);
                var integer = realm.ToIntegerOrInfinity(new(number));
                if (double.IsNaN(integer) || double.IsInfinity(integer) || number < 1 || number > 3 || integer < 1 ||
                    integer > 3)
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid value '{number}' for option 'fractionalSecondDigits'");
                fractionalSecondDigits = (int)integer;
            }

            var timeZoneName = GetIntlStringOption(realm, options, "timeZoneName",
                IntlDateTimeFormatTimeZoneNameValues, null!);
            var formatMatcher = GetIntlStringOption(realm, options, "formatMatcher",
                IntlDateTimeFormatFormatMatcherValues, "best fit");
            var dateStyle = GetIntlStringOption(realm, options, "dateStyle", IntlDateTimeFormatStyleValues, null!);
            var timeStyle = GetIntlStringOption(realm, options, "timeStyle", IntlDateTimeFormatStyleValues, null!);

            var hasExplicitFormatComponents = weekday is not null || era is not null || year is not null ||
                                              month is not null ||
                                              day is not null || dayPeriod is not null || hour is not null ||
                                              minute is not null ||
                                              second is not null || fractionalSecondDigits is not null ||
                                              timeZoneName is not null;
            if ((dateStyle is not null || timeStyle is not null) && hasExplicitFormatComponents)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "dateStyle/timeStyle cannot be mixed with explicit format components");

            if (dateStyle is null && timeStyle is null &&
                weekday is null && era is null && year is null && month is null && day is null &&
                dayPeriod is null && hour is null && minute is null && second is null &&
                fractionalSecondDigits is null && timeZoneName is null)
            {
                year = "numeric";
                month = "numeric";
                day = "numeric";
            }

            var finalResolvedLocale = BuildResolvedDateTimeFormatLocale(
                baseResolvedLocale,
                localeHourCycle,
                localeNumberingSystem,
                localeUnicodeAttributes,
                localeOtherUnicodeKeywords,
                localeOtherExtensions,
                localeCalendar,
                hourCycle,
                resolvedNumberingSystem,
                calendar,
                hour is not null || timeStyle is not null,
                hour12.HasValue,
                hourCycleExplicit,
                numberingSystemOption);

            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, dateTimeFormatConstructor,
                dateTimeFormatPrototype, "DateTimeFormat");

            return new JsDateTimeFormatObject(realm, prototype, finalResolvedLocale, calendar,
                resolvedNumberingSystem,
                timeZone, !hasTimeZoneOption, hourCycle, hour12, weekday, era, year, month, day, dayPeriod, hour,
                minute, second,
                fractionalSecondDigits, timeZoneName, formatMatcher, dateStyle, timeStyle, cultureInfo);
        }, "DateTimeFormat", 0, true);

        dateTimeFormatConstructor.InitializePrototypeProperty(dateTimeFormatPrototype);
        dateTimeFormatConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return dateTimeFormatConstructor;
    }

    private JsHostFunction CreateCollatorConstructor(JsPlainObject collatorPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.Collator.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction collatorConstructor = null!;
        collatorConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;

            var options = GetIntlConstructorOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.Collator options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            var usage = GetIntlStringOption(realm, options, "usage", IntlCollatorUsageValues, "sort");
            var sensitivity =
                GetIntlStringOption(realm, options, "sensitivity", IntlCollatorSensitivityValues, "variant");
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var baseResolvedLocale = CanonicalizeUnicodeLocaleId(RemoveUnicodeExtensions(resolvedLocale));
            string? localeCaseFirst = null;
            string? localeCollation = null;
            bool? localeNumeric = null;
            if (requestedLocales.Count > 0 && ContainsUnicodeExtension(requestedLocales[0]))
            {
                var parsed = ParseLanguageTag(requestedLocales[0]);
                ExtractLocaleComponents(parsed, out _, out _, out _, out _,
                    out _, out localeCaseFirst, out localeCollation, out _, out _,
                    out localeNumeric, out _, out _, out _, out _);
            }

            if (localeCaseFirst is not null &&
                !IntlCollatorCaseFirstValues.Contains(localeCaseFirst, StringComparer.Ordinal))
                localeCaseFirst = null;

            var caseFirst = GetIntlStringOption(realm, options, "caseFirst", IntlCollatorCaseFirstValues,
                localeCaseFirst ?? "false");
            var numeric = GetNumericOption(realm, options, localeNumeric) ?? false;
            var ignorePunctuation = options.TryGetProperty("ignorePunctuation", out var ignorePunctuationValue) &&
                                    !ignorePunctuationValue.IsUndefined
                ? JsRealm.ToBoolean(ignorePunctuationValue)
                : baseResolvedLocale.StartsWith("th", StringComparison.OrdinalIgnoreCase);
            var collationOption = GetUnicodeKeywordOption(realm, options, "collation", "co", null);
            var collation = ResolveSupportedCollation(baseResolvedLocale, usage, collationOption, localeCollation);
            var finalResolvedLocale = BuildResolvedCollatorLocale(baseResolvedLocale,
                localeCaseFirst, localeCollation, localeNumeric, caseFirst, collation, numeric);

            var cultureInfo = GetCultureInfo(resolvedLocale) ?? CultureInfo.InvariantCulture;
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, collatorConstructor,
                collatorPrototype, "Collator");

            var compareOptions = MapCollatorCompareOptions(sensitivity, ignorePunctuation);
            if (string.Equals(usage, "search", StringComparison.Ordinal) &&
                baseResolvedLocale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
                compareOptions |= CompareOptions.IgnoreCase;

            return new JsCollatorObject(realm, prototype, finalResolvedLocale, usage, sensitivity, ignorePunctuation,
                collation, numeric, caseFirst, cultureInfo.CompareInfo, compareOptions);
        }, "Collator", 0, true);

        collatorConstructor.InitializePrototypeProperty(collatorPrototype);
        collatorConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return collatorConstructor;
    }

    private JsHostFunction CreatePluralRulesConstructor(JsPlainObject pluralRulesPrototype)
    {
        var supportedLocalesOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var options = GetIntlOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.PluralRules.supportedLocalesOf options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            return CreateSupportedLocalesArray(realm, requestedLocales);
        }, "supportedLocalesOf", 1);

        JsHostFunction pluralRulesConstructor = null!;
        pluralRulesConstructor = new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.PluralRules must be called with new");

            var options = GetIntlConstructorOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined,
                "Intl.PluralRules options must be an object");
            _ = GetIntlStringOption(realm, options, "localeMatcher", IntlLocaleMatcherValues, "best fit");

            var type = GetIntlStringOption(realm, options, "type", IntlPluralRulesTypeValues, "cardinal");
            var notation =
                GetIntlStringOption(realm, options, "notation", IntlPluralRulesNotationValues, "standard");
            var minimumIntegerDigits = GetIntlNumberOption(realm, options, "minimumIntegerDigits", 1, 21, 1);

            int? minimumFractionDigits = null;
            int? maximumFractionDigits = null;
            int? minimumSignificantDigits = null;
            int? maximumSignificantDigits = null;

            var minFracValue = options.TryGetProperty("minimumFractionDigits", out var minFracRaw)
                ? minFracRaw
                : JsValue.Undefined;
            var maxFracValue = options.TryGetProperty("maximumFractionDigits", out var maxFracRaw)
                ? maxFracRaw
                : JsValue.Undefined;
            var minSigValue = options.TryGetProperty("minimumSignificantDigits", out var minSigRaw)
                ? minSigRaw
                : JsValue.Undefined;
            var maxSigValue = options.TryGetProperty("maximumSignificantDigits", out var maxSigRaw)
                ? maxSigRaw
                : JsValue.Undefined;

            var minimumSignificantDigitsExplicit = !minSigValue.IsUndefined;
            var maximumSignificantDigitsExplicit = !maxSigValue.IsUndefined;
            if (minimumSignificantDigitsExplicit || maximumSignificantDigitsExplicit)
            {
                minimumSignificantDigits = minimumSignificantDigitsExplicit
                    ? GetIntlNumberOptionValue(realm, minSigValue, "minimumSignificantDigits", 1, 21)
                    : 1;
                maximumSignificantDigits = maximumSignificantDigitsExplicit
                    ? GetIntlNumberOptionValue(realm, maxSigValue, "maximumSignificantDigits",
                        minimumSignificantDigits.Value, 21)
                    : 21;
            }
            else
            {
                minimumFractionDigits = minFracValue.IsUndefined
                    ? 0
                    : GetIntlNumberOptionValue(realm, minFracValue, "minimumFractionDigits", 0, 100);
                var maxFractionFallback = Math.Max(minimumFractionDigits.Value, 3);
                maximumFractionDigits = maxFracValue.IsUndefined
                    ? maxFractionFallback
                    : GetIntlNumberOptionValue(realm, maxFracValue, "maximumFractionDigits",
                        minimumFractionDigits.Value, 100);
                if (minimumFractionDigits > maximumFractionDigits)
                    minimumFractionDigits = maximumFractionDigits;
            }

            var roundingIncrement = GetIntlRoundingIncrementOption(realm, options);
            var roundingMode = GetIntlStringOption(realm, options, "roundingMode",
                IntlNumberFormatRoundingModeValues, "halfExpand");
            var roundingPriority = GetIntlStringOption(realm, options, "roundingPriority",
                IntlNumberFormatRoundingPriorityValues, "auto");
            var trailingZeroDisplay = GetIntlStringOption(realm, options, "trailingZeroDisplay",
                IntlNumberFormatTrailingZeroDisplayValues, "auto");

            var requestedLocales = CanonicalizeLocaleList(realm, args.Length == 0 ? JsValue.Undefined : args[0]);
            var resolvedLocale = ResolveIntlLocale(requestedLocales);
            var cultureInfo = GetCultureInfo(resolvedLocale) ?? CultureInfo.InvariantCulture;
            var prototype = GetIntlPrototypeFromConstructor(realm, info.NewTarget, pluralRulesConstructor,
                pluralRulesPrototype, "PluralRules");
            return new JsPluralRulesObject(realm, prototype, resolvedLocale, type, notation, minimumIntegerDigits,
                minimumFractionDigits, maximumFractionDigits, minimumSignificantDigits, maximumSignificantDigits,
                roundingMode, roundingPriority, roundingIncrement, trailingZeroDisplay, cultureInfo);
        }, "PluralRules", 0, true);


        pluralRulesConstructor.InitializePrototypeProperty(pluralRulesPrototype);
        pluralRulesConstructor.DefineDataPropertyAtom(Realm, IdSupportedLocalesOf, supportedLocalesOfFn,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        return pluralRulesConstructor;
    }

    private void InstallLocalePrototypeBuiltins(JsPlainObject localePrototype, JsHostFunction localeConstructor)
    {
        var maximizeFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.maximize");
            var maximized = LikelySubtags.AddLikelySubtags(locale.Locale);
            return CreateLocaleObject(info.Realm, localePrototype, maximized, JsValue.Undefined);
        }, "maximize", 0);

        var minimizeFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.minimize");
            var minimized = LikelySubtags.RemoveLikelySubtags(locale.Locale);
            return CreateLocaleObject(info.Realm, localePrototype, minimized, JsValue.Undefined);
        }, "minimize", 0);

        var toStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                return JsValue.FromString(ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.toString")
                    .Locale);
            }, "toString", 0);

        var getCalendarsFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getCalendars");
            return CreateStringArray(info.Realm, [locale.Calendar ?? "gregory"]);
        }, "getCalendars", 0);

        var getCollationsFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getCollations");
            var result = info.Realm.CreateArrayObject();
            var value = locale.Collation switch
            {
                null or "" or "search" or "standard" => "default",
                _ => locale.Collation
            };
            return CreateStringArray(info.Realm, [value]);
        }, "getCollations", 0);

        var getHourCyclesFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getHourCycles");
            var hourCycle = locale.HourCycle ?? GetDefaultHourCycle(locale.CultureInfo);
            return CreateStringArray(info.Realm, [hourCycle]);
        }, "getHourCycles", 0);

        var getNumberingSystemsFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getNumberingSystems");
            return CreateStringArray(info.Realm, [locale.NumberingSystem ?? "latn"]);
        }, "getNumberingSystems", 0);

        var getTimeZonesFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getTimeZones");
            if (string.IsNullOrEmpty(locale.Region))
                return JsValue.Undefined;

            var timeZones = GetTimeZonesForRegion(locale.Region);
            return CreateStringArray(info.Realm, timeZones);
        }, "getTimeZones", 0);

        var getTextInfoFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getTextInfo");
            var result = new JsPlainObject(info.Realm);
            result.DefineDataPropertyAtom(info.Realm, IdDirection,
                JsValue.FromString(locale.CultureInfo.TextInfo.IsRightToLeft ? "rtl" : "ltr"),
                JsShapePropertyFlags.Open);
            return result;
        }, "getTextInfo", 0);

        var getWeekInfoFn = new JsHostFunction(Realm, (in info) =>
        {
            var locale = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.getWeekInfo");
            var result = new JsPlainObject(info.Realm);
            result.DefineDataPropertyAtom(info.Realm, IdFirstDay,
                JsValue.FromInt32(GetWeekdayNumber(locale.FirstDayOfWeek ?? GetDefaultFirstDayOfWeek(locale.Region))),
                JsShapePropertyFlags.Open);
            var weekend = info.Realm.CreateArrayObject();
            FreshArrayOperations.DefineElement(weekend, 0, JsValue.FromInt32(6));
            FreshArrayOperations.DefineElement(weekend, 1, JsValue.FromInt32(7));
            result.DefineDataPropertyAtom(info.Realm, IdWeekend, JsValue.FromObject(weekend),
                JsShapePropertyFlags.Open);
            return result;
        }, "getWeekInfo", 0);

        var baseNameGetter = new JsHostFunction(Realm,
            static (in info) =>
            {
                return JsValue.FromString(ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.baseName")
                    .BaseName);
            }, "get baseName", 0);
        var calendarGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.calendar").Calendar;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get calendar", 0);
        var caseFirstGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.caseFirst").CaseFirst;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get caseFirst", 0);
        var collationGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.collation").Collation;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get collation", 0);
        var hourCycleGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.hourCycle").HourCycle;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get hourCycle", 0);
        var languageGetter = new JsHostFunction(Realm,
            static (in info) =>
            {
                return JsValue.FromString(ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.language")
                    .Language);
            }, "get language", 0);
        var numberingSystemGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.numberingSystem")
                .NumberingSystem;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get numberingSystem", 0);
        var numericGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.numeric").Numeric;
            return value.HasValue ? value.Value ? JsValue.True : JsValue.False : JsValue.False;
        }, "get numeric", 0);
        var regionGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.region").Region;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get region", 0);
        var scriptGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.script").Script;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get script", 0);
        var firstDayOfWeekGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.firstDayOfWeek")
                .FirstDayOfWeek;
            return value is null ? JsValue.Undefined : JsValue.FromString(value);
        }, "get firstDayOfWeek", 0);
        var variantsGetter = new JsHostFunction(Realm, (in info) =>
        {
            var value = ThisLocaleValue(info.Realm, info.ThisValue, "Intl.Locale.prototype.variants").Variants;
            return value.Length == 0 ? JsValue.Undefined : JsValue.FromString(string.Join("-", value));
        }, "get variants", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(localeConstructor), true,
                configurable: true),
            PropertyDefinition.Mutable(IdMaximize, JsValue.FromObject(maximizeFn)),
            PropertyDefinition.Mutable(IdMinimize, JsValue.FromObject(minimizeFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(IdGetCalendars, JsValue.FromObject(getCalendarsFn)),
            PropertyDefinition.Mutable(IdGetCollations, JsValue.FromObject(getCollationsFn)),
            PropertyDefinition.Mutable(IdGetHourCycles, JsValue.FromObject(getHourCyclesFn)),
            PropertyDefinition.Mutable(IdGetNumberingSystems, JsValue.FromObject(getNumberingSystemsFn)),
            PropertyDefinition.Mutable(IdGetTimeZones, JsValue.FromObject(getTimeZonesFn)),
            PropertyDefinition.Mutable(IdGetTextInfo, JsValue.FromObject(getTextInfoFn)),
            PropertyDefinition.Mutable(IdGetWeekInfo, JsValue.FromObject(getWeekInfoFn)),
            PropertyDefinition.GetterData(IdBaseName, baseNameGetter, configurable: true),
            PropertyDefinition.GetterData(IdCalendar, calendarGetter, configurable: true),
            PropertyDefinition.GetterData(IdCaseFirst, caseFirstGetter, configurable: true),
            PropertyDefinition.GetterData(IdCollation, collationGetter, configurable: true),
            PropertyDefinition.GetterData(IdHourCycle, hourCycleGetter, configurable: true),
            PropertyDefinition.GetterData(IdLanguage, languageGetter, configurable: true),
            PropertyDefinition.GetterData(IdNumberingSystem, numberingSystemGetter, configurable: true),
            PropertyDefinition.GetterData(IdNumeric, numericGetter, configurable: true),
            PropertyDefinition.GetterData(IdRegion, regionGetter, configurable: true),
            PropertyDefinition.GetterData(IdScript, scriptGetter, configurable: true),
            PropertyDefinition.GetterData(IdFirstDayOfWeek, firstDayOfWeekGetter, configurable: true),
            PropertyDefinition.GetterData(IdVariants, variantsGetter, configurable: true)
        ];
        localePrototype.DefineNewPropertiesNoCollision(Realm, defs);
        localePrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag, JsValue.FromString("Intl.Locale"),
            JsShapePropertyFlags.Configurable);
    }

    private void InstallSegmenterPrototypeBuiltins(
        JsPlainObject segmenterPrototype,
        JsHostFunction segmenterConstructor,
        JsPlainObject segmentsPrototype,
        JsPlainObject segmentIteratorPrototype)
    {
        var segmentFn = new JsHostFunction(Realm, (in info) =>
        {
            var segmenter = ThisSegmenterValue(info.Realm, info.ThisValue, "Intl.Segmenter.prototype.segment");
            var input = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            var text = info.Realm.ToJsStringSlowPath(input);
            return new JsSegmentsObject(info.Realm, segmentsPrototype, segmentIteratorPrototype, segmenter, text);
        }, "segment", 1);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var segmenter = ThisSegmenterValue(info.Realm, info.ThisValue, "Intl.Segmenter.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(segmenter.Locale),
                JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdGranularity,
                JsValue.FromString(segmenter.Granularity),
                JsShapePropertyFlags.Open);
            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(segmenterConstructor), true,
                configurable: true),
            PropertyDefinition.Mutable(IdSegment, JsValue.FromObject(segmentFn)),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn))
        ];
        segmenterPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        segmenterPrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag,
            JsValue.FromString("Intl.Segmenter"),
            JsShapePropertyFlags.Configurable);
    }

    private void InstallSegmentsPrototypeBuiltins(JsPlainObject segmentsPrototype,
        JsPlainObject segmentIteratorPrototype)
    {
        var containingFn = new JsHostFunction(Realm, (in info) =>
        {
            var segments =
                ThisSegmentsValue(info.Realm, info.ThisValue, "Intl.Segmenter.prototype.segment().containing");
            var index = info.Arguments.Length == 0 ? 0d : info.Realm.ToIntegerOrInfinity(info.Arguments[0]);
            return segments.Containing(index);
        }, "containing", 1);

        var iteratorFn = new JsHostFunction(Realm, (in info) =>
        {
            var segments = ThisSegmentsValue(info.Realm, info.ThisValue,
                "Intl.Segmenter.prototype.segment()[Symbol.iterator]");
            return segments.CreateIterator();
        }, "[Symbol.iterator]", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdContaining, JsValue.FromObject(containingFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorFn))
        ];
        segmentsPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        segmentsPrototype.DefineDataPropertyAtom(Realm, IdConstructor, JsValue.Undefined,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
    }

    private void InstallSegmentIteratorPrototypeBuiltins(JsPlainObject segmentIteratorPrototype)
    {
        var nextFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                return ThisSegmentIteratorValue(info.Realm, info.ThisValue, "Segment Iterator.prototype.next").Next();
            }, "next", 0);

        var iteratorSelfFn = new JsHostFunction(Realm, (in info) =>
        {
            ThisSegmentIteratorValue(info.Realm, info.ThisValue, "Segment Iterator.prototype[Symbol.iterator]");
            return info.ThisValue;
        }, "[Symbol.iterator]", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdNext, JsValue.FromObject(nextFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorSelfFn))
        ];
        segmentIteratorPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        segmentIteratorPrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag,
            JsValue.FromString("Segment Iterator"), JsShapePropertyFlags.Configurable);
    }

    private void InstallRelativeTimeFormatPrototypeBuiltins(
        JsPlainObject relativeTimeFormatPrototype,
        JsHostFunction relativeTimeFormatConstructor)
    {
        var formatFn = new JsHostFunction(Realm, (in info) =>
        {
            var relativeTimeFormat = ThisRelativeTimeFormatValue(info.Realm, info.ThisValue,
                "Intl.RelativeTimeFormat.prototype.format");
            var value = info.Realm.ToNumberSlowPath(
                info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0]);
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid value");
            var unit = NormalizeRelativeTimeFormatUnit(info.Realm,
                info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined);
            return JsValue.FromString(relativeTimeFormat.Format(value, unit));
        }, "format", 2);

        var formatToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var relativeTimeFormat = ThisRelativeTimeFormatValue(info.Realm, info.ThisValue,
                "Intl.RelativeTimeFormat.prototype.formatToParts");
            var value = info.Realm.ToNumberSlowPath(
                info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0]);
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid value");
            var unit = NormalizeRelativeTimeFormatUnit(info.Realm,
                info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined);
            return relativeTimeFormat.FormatToParts(value, unit);
        }, "formatToParts", 2);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var relativeTimeFormat = ThisRelativeTimeFormatValue(info.Realm, info.ThisValue,
                "Intl.RelativeTimeFormat.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(relativeTimeFormat.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdStyle,
                JsValue.FromString(relativeTimeFormat.Style), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNumeric,
                JsValue.FromString(relativeTimeFormat.Numeric), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNumberingSystem,
                JsValue.FromString(relativeTimeFormat.NumberingSystem), JsShapePropertyFlags.Open);
            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(relativeTimeFormatConstructor),
                true, configurable: true),
            PropertyDefinition.Mutable(IdFormat, JsValue.FromObject(formatFn)),
            PropertyDefinition.Mutable(IdFormatToParts, JsValue.FromObject(formatToPartsFn)),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn))
        ];
        relativeTimeFormatPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        relativeTimeFormatPrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag,
            JsValue.FromString("Intl.RelativeTimeFormat"), JsShapePropertyFlags.Configurable);
    }

    private void InstallDurationFormatPrototypeBuiltins(
        JsPlainObject durationFormatPrototype,
        JsHostFunction durationFormatConstructor)
    {
        var formatFn = new JsHostFunction(Realm, (in info) =>
        {
            var durationFormat =
                ThisDurationFormatValue(info.Realm, info.ThisValue, "Intl.DurationFormat.prototype.format");
            var duration = ToDurationFormatRecord(info.Realm,
                info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0]);
            return JsValue.FromString(durationFormat.Format(duration));
        }, "format", 1);

        var formatToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var durationFormat = ThisDurationFormatValue(info.Realm, info.ThisValue,
                "Intl.DurationFormat.prototype.formatToParts");
            var duration = ToDurationFormatRecord(info.Realm,
                info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0]);
            return durationFormat.FormatToParts(duration);
        }, "formatToParts", 1);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var durationFormat = ThisDurationFormatValue(info.Realm, info.ThisValue,
                "Intl.DurationFormat.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm, useDictionaryMode: true)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(durationFormat.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNumberingSystem,
                JsValue.FromString(durationFormat.NumberingSystem), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdStyle,
                JsValue.FromString(durationFormat.Style), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdYears,
                JsValue.FromString(durationFormat.YearsStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdYearsDisplay,
                JsValue.FromString(durationFormat.YearsDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMonths,
                JsValue.FromString(durationFormat.MonthsStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMonthsDisplay,
                JsValue.FromString(durationFormat.MonthsDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdWeeks,
                JsValue.FromString(durationFormat.WeeksStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdWeeksDisplay,
                JsValue.FromString(durationFormat.WeeksDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdDays,
                JsValue.FromString(durationFormat.DaysStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdDaysDisplay,
                JsValue.FromString(durationFormat.DaysDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdHours,
                JsValue.FromString(durationFormat.HoursStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdHoursDisplay,
                JsValue.FromString(durationFormat.HoursDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMinutes,
                JsValue.FromString(durationFormat.MinutesStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMinutesDisplay,
                JsValue.FromString(durationFormat.MinutesDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdSeconds,
                JsValue.FromString(durationFormat.SecondsStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdSecondsDisplay,
                JsValue.FromString(durationFormat.SecondsDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMilliseconds,
                JsValue.FromString(durationFormat.MillisecondsStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMillisecondsDisplay,
                JsValue.FromString(durationFormat.MillisecondsDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMicroseconds,
                JsValue.FromString(durationFormat.MicrosecondsStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMicrosecondsDisplay,
                JsValue.FromString(durationFormat.MicrosecondsDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNanoseconds,
                JsValue.FromString(durationFormat.NanosecondsStyle), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNanosecondsDisplay,
                JsValue.FromString(durationFormat.NanosecondsDisplay), JsShapePropertyFlags.Open);
            if (durationFormat.FractionalDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdFractionalDigits,
                    JsValue.FromInt32(durationFormat.FractionalDigits.Value), JsShapePropertyFlags.Open);

            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(durationFormatConstructor),
                true, configurable: true),
            PropertyDefinition.Mutable(IdFormat, JsValue.FromObject(formatFn)),
            PropertyDefinition.Mutable(IdFormatToParts, JsValue.FromObject(formatToPartsFn)),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn))
        ];
        durationFormatPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        durationFormatPrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag,
            JsValue.FromString("Intl.DurationFormat"), JsShapePropertyFlags.Configurable);
    }

    private void InstallNumberFormatPrototypeBuiltins(
        JsPlainObject numberFormatPrototype,
        JsHostFunction numberFormatConstructor)
    {
        var formatGetter = new JsHostFunction(Realm, (in info) =>
        {
            var numberFormat = ThisNumberFormatValue(info.Realm, info.ThisValue, "Intl.NumberFormat.prototype.format");
            var boundFormat = new JsHostFunction(info.Realm, static (in innerInfo) =>
            {
                var capture = (JsNumberFormatObject)((JsHostFunction)innerInfo.Function).UserData!;
                var value = innerInfo.Arguments.Length == 0 ? JsValue.Undefined : innerInfo.Arguments[0];
                if (capture.TryFormatExactValue(value, out var exact))
                    return JsValue.FromString(exact);
                var number = value.IsUndefined ? double.NaN : innerInfo.Realm.ToNumberSlowPath(value);
                return JsValue.FromString(capture.Format(number));
            }, string.Empty, 1)
            {
                UserData = numberFormat
            };
            return boundFormat;
        }, "get format", 0);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var numberFormat = ThisNumberFormatValue(info.Realm, info.ThisValue,
                "Intl.NumberFormat.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm, useDictionaryMode: true)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            var useGroupingValue = string.Equals(numberFormat.UseGrouping, "false", StringComparison.Ordinal)
                ? JsValue.False
                : JsValue.FromString(numberFormat.UseGrouping);
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(numberFormat.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNumberingSystem,
                JsValue.FromString(numberFormat.NumberingSystem), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdStyle,
                JsValue.FromString(numberFormat.Style), JsShapePropertyFlags.Open);
            if (numberFormat.Currency is not null)
            {
                result.DefineDataPropertyAtom(info.Realm, IdCurrency,
                    JsValue.FromString(numberFormat.Currency), JsShapePropertyFlags.Open);
                result.DefineDataPropertyAtom(info.Realm, IdCurrencyDisplay,
                    JsValue.FromString(numberFormat.CurrencyDisplay), JsShapePropertyFlags.Open);
                result.DefineDataPropertyAtom(info.Realm, IdCurrencySign,
                    JsValue.FromString(numberFormat.CurrencySign), JsShapePropertyFlags.Open);
            }

            if (numberFormat.Unit is not null)
            {
                result.DefineDataPropertyAtom(info.Realm, IdUnit,
                    JsValue.FromString(numberFormat.Unit), JsShapePropertyFlags.Open);
                result.DefineDataPropertyAtom(info.Realm, IdUnitDisplay,
                    JsValue.FromString(numberFormat.UnitDisplay), JsShapePropertyFlags.Open);
            }

            result.DefineDataPropertyAtom(info.Realm, IdMinimumIntegerDigits,
                JsValue.FromInt32(numberFormat.MinimumIntegerDigits), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMinimumFractionDigits,
                JsValue.FromInt32(numberFormat.MinimumFractionDigits), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMaximumFractionDigits,
                JsValue.FromInt32(numberFormat.MaximumFractionDigits), JsShapePropertyFlags.Open);
            if (numberFormat.MinimumSignificantDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdMinimumSignificantDigits,
                    JsValue.FromInt32(numberFormat.MinimumSignificantDigits.Value), JsShapePropertyFlags.Open);

            if (numberFormat.MaximumSignificantDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdMaximumSignificantDigits,
                    JsValue.FromInt32(numberFormat.MaximumSignificantDigits.Value), JsShapePropertyFlags.Open);

            result.DefineDataPropertyAtom(info.Realm, IdUseGrouping,
                useGroupingValue, JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNotation,
                JsValue.FromString(numberFormat.Notation), JsShapePropertyFlags.Open);
            if (string.Equals(numberFormat.Notation, "compact", StringComparison.Ordinal))
                result.DefineDataPropertyAtom(info.Realm, IdCompactDisplay,
                    JsValue.FromString(numberFormat.CompactDisplay), JsShapePropertyFlags.Open);

            result.DefineDataPropertyAtom(info.Realm, IdSignDisplay,
                JsValue.FromString(numberFormat.SignDisplay), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdRoundingIncrement,
                JsValue.FromInt32(numberFormat.RoundingIncrement), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdRoundingMode,
                JsValue.FromString(numberFormat.RoundingMode), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdRoundingPriority,
                JsValue.FromString(numberFormat.RoundingPriority), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdTrailingZeroDisplay,
                JsValue.FromString(numberFormat.TrailingZeroDisplay), JsShapePropertyFlags.Open);
            return result;
        }, "resolvedOptions", 0);

        var formatToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var numberFormat = ThisNumberFormatValue(info.Realm, info.ThisValue,
                "Intl.NumberFormat.prototype.formatToParts");
            var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            var number = value.IsUndefined ? double.NaN : info.Realm.ToNumberSlowPath(value);
            return numberFormat.FormatToParts(number);
        }, "formatToParts", 1);

        var formatRangeFn = new JsHostFunction(Realm, (in info) =>
        {
            var numberFormat = ThisNumberFormatValue(info.Realm, info.ThisValue,
                "Intl.NumberFormat.prototype.formatRange");
            var startValue = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var endValue = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            var startFormatted = FormatRangeValue(info.Realm, numberFormat, startValue);
            var endFormatted = FormatRangeValue(info.Realm, numberFormat, endValue);
            if (string.Equals(startFormatted, endFormatted, StringComparison.Ordinal))
                return JsValue.FromString("~" + startFormatted);

            var separator = GetRangeSeparator(numberFormat, startValue, endValue);
            if (TryCompressSharedSuffix(startFormatted, endFormatted, separator, out var suffixCompressed))
                return JsValue.FromString(suffixCompressed!);
            if (TryCompressSharedPrefix(startFormatted, endFormatted, separator, out var prefixCompressed))
                return JsValue.FromString(prefixCompressed!);
            return JsValue.FromString(startFormatted + separator + endFormatted);
        }, "formatRange", 2);

        var formatRangeToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var numberFormat = ThisNumberFormatValue(info.Realm, info.ThisValue,
                "Intl.NumberFormat.prototype.formatRangeToParts");
            var startValue = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var endValue = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            var start = ToRangeNumber(info.Realm, startValue);
            var end = ToRangeNumber(info.Realm, endValue);
            var startParts = numberFormat.FormatToParts(start);
            var endParts = numberFormat.FormatToParts(end);
            var startFormatted = numberFormat.Format(start);
            var endFormatted = numberFormat.Format(end);
            var result = info.Realm.CreateArrayObject();
            uint index = 0;

            if (string.Equals(startFormatted, endFormatted, StringComparison.Ordinal))
            {
                result.SetElement(index++,
                    JsValue.FromObject(CreateRangePartObject(info.Realm, "approximatelySign", "~", "shared")));
                AppendRangeParts(result, ref index, startParts, "shared");
                return result;
            }

            AppendRangeParts(result, ref index, startParts, "startRange");
            result.SetElement(index++,
                JsValue.FromObject(CreateRangePartObject(info.Realm, "literal",
                    GetRangeSeparator(numberFormat, startValue, endValue), "shared")));
            AppendRangeParts(result, ref index, endParts, "endRange");
            return result;
        }, "formatRangeToParts", 2);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(numberFormatConstructor),
                true, configurable: true),
            PropertyDefinition.GetterData(IdFormat, formatGetter, configurable: true),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn)),
            PropertyDefinition.Mutable(IdFormatToParts, JsValue.FromObject(formatToPartsFn)),
            PropertyDefinition.Mutable(IdFormatRange, JsValue.FromObject(formatRangeFn)),
            PropertyDefinition.Mutable(IdFormatRangeToParts, JsValue.FromObject(formatRangeToPartsFn)),
            PropertyDefinition.Data(IdSymbolToStringTag, JsValue.FromString("Intl.NumberFormat"),
                configurable: true)
        ];
        numberFormatPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private void InstallDateTimeFormatPrototypeBuiltins(
        JsPlainObject dateTimeFormatPrototype,
        JsHostFunction dateTimeFormatConstructor)
    {
        var formatGetter = new JsHostFunction(Realm, (in info) =>
        {
            var dateTimeFormat =
                ThisDateTimeFormatValue(info.Realm, info.ThisValue, "Intl.DateTimeFormat.prototype.format");
            return dateTimeFormat.GetOrCreateBoundFormat(info.Realm);
        }, "get format", 0);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var dateTimeFormat = ThisDateTimeFormatValue(info.Realm, info.ThisValue,
                "Intl.DateTimeFormat.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(dateTimeFormat.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdCalendar,
                JsValue.FromString(dateTimeFormat.Calendar), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNumberingSystem,
                JsValue.FromString(dateTimeFormat.NumberingSystem), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdTimeZone,
                JsValue.FromString(dateTimeFormat.TimeZone), JsShapePropertyFlags.Open);
            var includeHourCycle = dateTimeFormat.Hour is not null || dateTimeFormat.TimeStyle is not null;
            if (includeHourCycle)
            {
                result.DefineDataPropertyAtom(info.Realm, IdHourCycle,
                    JsValue.FromString(dateTimeFormat.HourCycle), JsShapePropertyFlags.Open);
                result.DefineDataPropertyAtom(info.Realm, IdHour12,
                    dateTimeFormat.HourCycle is "h11" or "h12" ? JsValue.True : JsValue.False,
                    JsShapePropertyFlags.Open);
            }

            DefineDateTimeResolvedOption(info.Realm, result, IdWeekday, dateTimeFormat.Weekday);
            DefineDateTimeResolvedOption(info.Realm, result, IdEra, dateTimeFormat.Era);
            DefineDateTimeResolvedOption(info.Realm, result, IdYear, dateTimeFormat.Year);
            DefineDateTimeResolvedOption(info.Realm, result, IdMonth, dateTimeFormat.Month);
            DefineDateTimeResolvedOption(info.Realm, result, IdDay, dateTimeFormat.Day);
            DefineDateTimeResolvedOption(info.Realm, result, IdDayPeriod, dateTimeFormat.DayPeriod);
            DefineDateTimeResolvedOption(info.Realm, result, IdHour, dateTimeFormat.Hour);
            DefineDateTimeResolvedOption(info.Realm, result, IdMinute, dateTimeFormat.Minute);
            DefineDateTimeResolvedOption(info.Realm, result, IdSecond, dateTimeFormat.Second);
            if (dateTimeFormat.FractionalSecondDigits is not null)
                result.DefineDataPropertyAtom(info.Realm, IdFractionalSecondDigits,
                    JsValue.FromInt32(dateTimeFormat.FractionalSecondDigits.Value), JsShapePropertyFlags.Open);

            DefineDateTimeResolvedOption(info.Realm, result, IdTimeZoneName, dateTimeFormat.TimeZoneName);
            DefineDateTimeResolvedOption(info.Realm, result, IdDateStyle, dateTimeFormat.DateStyle);
            DefineDateTimeResolvedOption(info.Realm, result, IdTimeStyle, dateTimeFormat.TimeStyle);
            return result;
        }, "resolvedOptions", 0);

        var formatToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var dateTimeFormat = ThisDateTimeFormatValue(info.Realm, info.ThisValue,
                "Intl.DateTimeFormat.prototype.formatToParts");
            var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            var number = value.IsUndefined
                ? DateTimeOffset.Now.ToUnixTimeMilliseconds()
                : info.Realm.ToNumberSlowPath(value);
            return dateTimeFormat.FormatToParts(number);
        }, "formatToParts", 1);

        var formatRangeFn = new JsHostFunction(Realm, (in info) =>
        {
            var dateTimeFormat =
                ThisDateTimeFormatValue(info.Realm, info.ThisValue, "Intl.DateTimeFormat.prototype.formatRange");
            if (info.Arguments.Length < 2)
                throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");
            if (info.Arguments[0].IsUndefined || info.Arguments[1].IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");
            var start = ToRangeDateTimeFormatNumber(info.Realm, info.Arguments[0]);
            var end = ToRangeDateTimeFormatNumber(info.Realm, info.Arguments[1]);
            return JsValue.FromString(dateTimeFormat.FormatRange(start, end));
        }, "formatRange", 2);

        var formatRangeToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var dateTimeFormat = ThisDateTimeFormatValue(info.Realm, info.ThisValue,
                "Intl.DateTimeFormat.prototype.formatRangeToParts");
            if (info.Arguments.Length < 2)
                throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");
            if (info.Arguments[0].IsUndefined || info.Arguments[1].IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");
            var start = ToRangeDateTimeFormatNumber(info.Realm, info.Arguments[0]);
            var end = ToRangeDateTimeFormatNumber(info.Realm, info.Arguments[1]);
            return dateTimeFormat.FormatRangeToParts(start, end);
        }, "formatRangeToParts", 2);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(dateTimeFormatConstructor),
                true, configurable: true),
            PropertyDefinition.GetterData(IdFormat, formatGetter, configurable: true),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn)),
            PropertyDefinition.Mutable(IdFormatToParts, JsValue.FromObject(formatToPartsFn)),
            PropertyDefinition.Mutable(IdFormatRange, JsValue.FromObject(formatRangeFn)),
            PropertyDefinition.Mutable(IdFormatRangeToParts, JsValue.FromObject(formatRangeToPartsFn)),
            PropertyDefinition.Data(IdSymbolToStringTag, JsValue.FromString("Intl.DateTimeFormat"),
                configurable: true)
        ];
        dateTimeFormatPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private void InstallListFormatPrototypeBuiltins(
        JsPlainObject listFormatPrototype,
        JsHostFunction listFormatConstructor)
    {
        var formatFn = new JsHostFunction(Realm, (in info) =>
        {
            var listFormat = ThisListFormatValue(info.Realm, info.ThisValue, "Intl.ListFormat.prototype.format");
            return JsValue.FromString(listFormat.Format(StringListFromIterable(info.Realm,
                info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0])));
        }, "format", 1);

        var formatToPartsFn = new JsHostFunction(Realm, (in info) =>
        {
            var listFormat = ThisListFormatValue(info.Realm, info.ThisValue, "Intl.ListFormat.prototype.formatToParts");
            return listFormat.FormatToParts(StringListFromIterable(info.Realm,
                info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0]));
        }, "formatToParts", 1);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var listFormat =
                ThisListFormatValue(info.Realm, info.ThisValue, "Intl.ListFormat.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(listFormat.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdType,
                JsValue.FromString(listFormat.Type), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdStyle,
                JsValue.FromString(listFormat.Style), JsShapePropertyFlags.Open);
            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(listFormatConstructor), true,
                configurable: true),
            PropertyDefinition.Mutable(IdFormat, JsValue.FromObject(formatFn)),
            PropertyDefinition.Mutable(IdFormatToParts, JsValue.FromObject(formatToPartsFn)),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn)),
            PropertyDefinition.Data(IdSymbolToStringTag, JsValue.FromString("Intl.ListFormat"),
                configurable: true)
        ];
        listFormatPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private void InstallDisplayNamesPrototypeBuiltins(
        JsPlainObject displayNamesPrototype,
        JsHostFunction displayNamesConstructor)
    {
        var ofFn = new JsHostFunction(Realm, (in info) =>
        {
            var displayNames = ThisDisplayNamesValue(info.Realm, info.ThisValue, "Intl.DisplayNames.prototype.of");
            var codeValue = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            if (codeValue.IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Intl.DisplayNames.prototype.of requires a code");

            var code = info.Realm.ToJsStringSlowPath(codeValue);
            var canonicalCode = CanonicalizeDisplayNamesCode(displayNames.DisplayType, code);
            var result = displayNames.Of(canonicalCode);
            return result is null ? JsValue.Undefined : JsValue.FromString(result);
        }, "of", 1);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var displayNames = ThisDisplayNamesValue(info.Realm, info.ThisValue,
                "Intl.DisplayNames.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(displayNames.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdStyle,
                JsValue.FromString(displayNames.Style), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdType,
                JsValue.FromString(displayNames.DisplayType), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdFallback,
                JsValue.FromString(displayNames.Fallback), JsShapePropertyFlags.Open);
            if (displayNames.LanguageDisplay is not null)
                result.DefineDataPropertyAtom(info.Realm, IdLanguageDisplay,
                    JsValue.FromString(displayNames.LanguageDisplay), JsShapePropertyFlags.Open);

            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(displayNamesConstructor),
                true, configurable: true),
            PropertyDefinition.Mutable(IdOf, JsValue.FromObject(ofFn)),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn))
        ];
        displayNamesPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        displayNamesPrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag,
            JsValue.FromString("Intl.DisplayNames"), JsShapePropertyFlags.Configurable);
    }

    private void InstallCollatorPrototypeBuiltins(
        JsPlainObject collatorPrototype,
        JsHostFunction collatorConstructor)
    {
        var compareGetter = new JsHostFunction(Realm, (in info) =>
        {
            var collator = ThisCollatorValue(info.Realm, info.ThisValue, "Intl.Collator.prototype.compare");
            return collator.GetOrCreateBoundCompare(info.Realm);
        }, "get compare", 0);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var collator = ThisCollatorValue(info.Realm, info.ThisValue, "Intl.Collator.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(collator.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdUsage,
                JsValue.FromString(collator.Usage), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdSensitivity,
                JsValue.FromString(collator.Sensitivity), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdIgnorePunctuation,
                collator.IgnorePunctuation ? JsValue.True : JsValue.False, JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdCollation,
                JsValue.FromString(collator.Collation), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNumeric,
                collator.Numeric ? JsValue.True : JsValue.False, JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdCaseFirst,
                JsValue.FromString(collator.CaseFirst), JsShapePropertyFlags.Open);
            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(collatorConstructor), true,
                configurable: true),
            PropertyDefinition.GetterData(IdCompare, compareGetter, configurable: true),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn)),
            PropertyDefinition.Data(IdSymbolToStringTag, JsValue.FromString("Intl.Collator"),
                configurable: true)
        ];
        collatorPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private void InstallPluralRulesPrototypeBuiltins(
        JsPlainObject pluralRulesPrototype,
        JsHostFunction pluralRulesConstructor)
    {
        var selectFn = new JsHostFunction(Realm, (in info) =>
        {
            var pluralRules = ThisPluralRulesValue(info.Realm, info.ThisValue, "Intl.PluralRules.prototype.select");
            var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            var number = value.IsUndefined ? double.NaN : info.Realm.ToNumberSlowPath(value);
            return JsValue.FromString(pluralRules.Select(number));
        }, "select", 1);

        var selectRangeFn = new JsHostFunction(Realm, (in info) =>
        {
            var pluralRules =
                ThisPluralRulesValue(info.Realm, info.ThisValue, "Intl.PluralRules.prototype.selectRange");
            if (info.Arguments.Length < 2 || info.Arguments[0].IsUndefined || info.Arguments[1].IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");

            var start = info.Realm.ToNumberSlowPath(info.Arguments[0]);
            var end = info.Realm.ToNumberSlowPath(info.Arguments[1]);
            if (double.IsNaN(start) || double.IsNaN(end))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid value");

            return JsValue.FromString(pluralRules.Select(end));
        }, "selectRange", 2);

        var resolvedOptionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var pluralRules =
                ThisPluralRulesValue(info.Realm, info.ThisValue, "Intl.PluralRules.prototype.resolvedOptions");
            var result = new JsPlainObject(info.Realm, useDictionaryMode: true)
            {
                Prototype = info.Realm.ObjectPrototype
            };
            result.DefineDataPropertyAtom(info.Realm, IdLocaleLower,
                JsValue.FromString(pluralRules.Locale), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdType,
                JsValue.FromString(pluralRules.PluralRuleType), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdNotation,
                JsValue.FromString(pluralRules.Notation), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdMinimumIntegerDigits,
                JsValue.FromInt32(pluralRules.MinimumIntegerDigits), JsShapePropertyFlags.Open);
            if (pluralRules.MinimumFractionDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdMinimumFractionDigits,
                    JsValue.FromInt32(pluralRules.MinimumFractionDigits.Value), JsShapePropertyFlags.Open);

            if (pluralRules.MaximumFractionDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdMaximumFractionDigits,
                    JsValue.FromInt32(pluralRules.MaximumFractionDigits.Value), JsShapePropertyFlags.Open);

            if (pluralRules.MinimumSignificantDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdMinimumSignificantDigits,
                    JsValue.FromInt32(pluralRules.MinimumSignificantDigits.Value), JsShapePropertyFlags.Open);

            if (pluralRules.MaximumSignificantDigits.HasValue)
                result.DefineDataPropertyAtom(info.Realm, IdMaximumSignificantDigits,
                    JsValue.FromInt32(pluralRules.MaximumSignificantDigits.Value), JsShapePropertyFlags.Open);

            var categories = pluralRules.GetPluralCategories();
            var pluralCategories = CreateStringArray(info.Realm, categories);
            result.DefineDataPropertyAtom(info.Realm, IdPluralCategories,
                JsValue.FromObject(pluralCategories), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdRoundingIncrement,
                JsValue.FromInt32(pluralRules.RoundingIncrement), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdRoundingMode,
                JsValue.FromString(pluralRules.RoundingMode), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdRoundingPriority,
                JsValue.FromString(pluralRules.RoundingPriority), JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(info.Realm, IdTrailingZeroDisplay,
                JsValue.FromString(pluralRules.TrailingZeroDisplay), JsShapePropertyFlags.Open);
            return result;
        }, "resolvedOptions", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(pluralRulesConstructor),
                true, configurable: true),
            PropertyDefinition.Mutable(IdSelect, JsValue.FromObject(selectFn)),
            PropertyDefinition.Mutable(IdSelectRange, JsValue.FromObject(selectRangeFn)),
            PropertyDefinition.Mutable(IdResolvedOptions, JsValue.FromObject(resolvedOptionsFn))
        ];
        pluralRulesPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        pluralRulesPrototype.DefineDataPropertyAtom(Realm, IdSymbolToStringTag,
            JsValue.FromString("Intl.PluralRules"), JsShapePropertyFlags.Configurable);
    }

    private static JsLocaleObject ThisLocaleValue(JsRealm realm, in JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsLocaleObject locale)
            return locale;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.Locale receiver");
    }

    private static JsSegmenterObject ThisSegmenterValue(JsRealm realm, in JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsSegmenterObject segmenter)
            return segmenter;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.Segmenter receiver");
    }

    private static JsSegmentsObject ThisSegmentsValue(JsRealm realm, in JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsSegmentsObject segments)
            return segments;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires a Segments receiver");
    }

    private static JsSegmentIteratorObject ThisSegmentIteratorValue(JsRealm realm, in JsValue thisValue,
        string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsSegmentIteratorObject iterator)
            return iterator;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires a Segment Iterator receiver");
    }

    private static JsRelativeTimeFormatObject ThisRelativeTimeFormatValue(JsRealm realm, in JsValue thisValue,
        string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsRelativeTimeFormatObject relativeTimeFormat)
            return relativeTimeFormat;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"{methodName} requires Intl.RelativeTimeFormat receiver");
    }

    private static JsDurationFormatObject ThisDurationFormatValue(JsRealm realm, in JsValue thisValue,
        string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsDurationFormatObject durationFormat)
            return durationFormat;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.DurationFormat receiver");
    }

    private static JsNumberFormatObject ThisNumberFormatValue(JsRealm realm, in JsValue thisValue,
        string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsNumberFormatObject numberFormat)
            return numberFormat;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.NumberFormat receiver");
    }

    private static JsPluralRulesObject ThisPluralRulesValue(JsRealm realm, in JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsPluralRulesObject pluralRules)
            return pluralRules;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.PluralRules receiver");
    }

    private static JsListFormatObject ThisListFormatValue(JsRealm realm, in JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsListFormatObject listFormat)
            return listFormat;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.ListFormat receiver");
    }

    private static JsDisplayNamesObject ThisDisplayNamesValue(JsRealm realm, in JsValue thisValue,
        string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsDisplayNamesObject displayNames)
            return displayNames;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.DisplayNames receiver");
    }

    private static JsCollatorObject ThisCollatorValue(JsRealm realm, in JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsCollatorObject collator)
            return collator;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.Collator receiver");
    }

    private static JsDateTimeFormatObject ThisDateTimeFormatValue(JsRealm realm, in JsValue thisValue,
        string methodName)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsDateTimeFormatObject dateTimeFormat)
            return dateTimeFormat;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} requires Intl.DateTimeFormat receiver");
    }

    private static double ToRangeNumber(JsRealm realm, in JsValue value)
    {
        if (value.IsUndefined)
            throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");
        if (value.IsBigInt)
            return (double)value.AsBigInt().Value;

        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number))
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid number value");
        return number;
    }

    private static string FormatRangeValue(JsRealm realm, JsNumberFormatObject numberFormat, in JsValue value)
    {
        if (value.IsUndefined)
            throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");

        if (numberFormat.SupportsExactIntegralFormatting)
        {
            if (value.IsString && TryParseExactIntegralString(value.AsString(), out var rawIntegral))
                return numberFormat.FormatExactIntegralString(rawIntegral!);
            if (value.IsBigInt)
                return numberFormat.FormatExactIntegralString(value.AsBigInt().Value.ToString());
        }

        return numberFormat.Format(ToRangeNumber(realm, value));
    }

    private static bool TryParseExactIntegralString(string text, out string? rawIntegral)
    {
        rawIntegral = null;
        if (string.IsNullOrEmpty(text))
            return false;

        var index = text[0] is '+' or '-' ? 1 : 0;
        if (index == text.Length)
            return false;
        for (var i = index; i < text.Length; i++)
            if (!char.IsAsciiDigit(text[i]))
                return false;

        rawIntegral = text;
        return true;
    }

    private static string GetRangeSeparator(JsNumberFormatObject numberFormat, in JsValue startValue,
        in JsValue endValue)
    {
        if (numberFormat.Locale.StartsWith("pt-PT", StringComparison.OrdinalIgnoreCase))
            return " - ";
        if (numberFormat.SupportsExactIntegralFormatting && IsExactIntegralRangeOperand(startValue) &&
            IsExactIntegralRangeOperand(endValue))
            return "–";
        return " – ";
    }

    private static bool IsExactIntegralRangeOperand(in JsValue value)
    {
        if (value.IsBigInt)
            return true;
        return value.IsString && TryParseExactIntegralString(value.AsString(), out _);
    }

    private static bool TryCompressSharedPrefix(string startFormatted, string endFormatted, string separator,
        out string? compressed)
    {
        compressed = null;
        var prefixLength = 0;
        var max = Math.Min(startFormatted.Length, endFormatted.Length);
        while (prefixLength < max && startFormatted[prefixLength] == endFormatted[prefixLength])
            prefixLength++;
        if (prefixLength == 0)
            return false;

        var prefix = startFormatted[..prefixLength];
        if (!prefix.Contains('+') && !prefix.Contains('-') && !prefix.Contains('('))
            return false;

        var compressedSeparator = separator.Contains('–') ? "–" : separator;
        compressed = prefix + startFormatted[prefixLength..] + compressedSeparator + endFormatted[prefixLength..];
        return true;
    }

    private static bool TryCompressSharedSuffix(string startFormatted, string endFormatted, string separator,
        out string? compressed)
    {
        compressed = null;
        var suffixLength = 0;
        var max = Math.Min(startFormatted.Length, endFormatted.Length);
        while (suffixLength < max &&
               startFormatted[startFormatted.Length - 1 - suffixLength] ==
               endFormatted[endFormatted.Length - 1 - suffixLength])
            suffixLength++;
        if (suffixLength == 0)
            return false;

        var suffix = startFormatted[^suffixLength..];
        var affixStart = 0;
        while (affixStart < suffix.Length &&
               (char.IsAsciiDigit(suffix[affixStart]) || suffix[affixStart] is '.' or ',' or '+' or '-'))
            affixStart++;

        if (affixStart > 0 && affixStart < suffix.Length)
            suffix = suffix[affixStart..];
        if (!suffix.Contains('€') && !suffix.Contains('$') && !suffix.Contains('¥') && !suffix.Contains('£'))
            return false;

        var removeLength = suffix.Length;
        var startCore = startFormatted[..^removeLength];
        var endCore = endFormatted[..^removeLength];
        if (startCore.Length > 0 && endCore.Length > 0 &&
            startCore[0] == endCore[0] &&
            startCore[0] is '+' or '-')
        {
            compressed = startCore[0] + startCore[1..] + separator + endCore[1..] + suffix;
            return true;
        }

        compressed = startCore + separator + endCore + suffix;
        return true;
    }

    private static JsPlainObject CreateRangePartObject(JsRealm realm, string type, string value, string source)
    {
        var part = new JsPlainObject(realm.IntlRangePartObjectShape);
        part.SetNamedSlotUnchecked(JsRealm.IntlRangePartTypeSlot, JsValue.FromString(type));
        part.SetNamedSlotUnchecked(JsRealm.IntlRangePartValueSlot, JsValue.FromString(value));
        part.SetNamedSlotUnchecked(JsRealm.IntlRangePartSourceSlot, JsValue.FromString(source));
        return part;
    }

    private static List<string> StringListFromIterable(JsRealm realm, in JsValue iterable)
    {
        var list = new List<string>();
        if (iterable.IsUndefined)
            return list;

        if (iterable.IsString)
        {
            var text = iterable.AsString();
            foreach (var ch in text)
                list.Add(ch.ToString());
            return list;
        }

        if (!iterable.TryGetObject(out var iterableObject))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.ListFormat list is not iterable");

        if (!iterableObject.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethodValue, out _) ||
            iteratorMethodValue.IsUndefined || iteratorMethodValue.IsNull)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.ListFormat list is not iterable");

        if (!iteratorMethodValue.TryGetObject(out var iteratorMethodObject) ||
            iteratorMethodObject is not JsFunction iteratorMethod)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.ListFormat list is not iterable");

        var iteratorValue = realm.InvokeFunction(iteratorMethod, JsValue.FromObject(iterableObject),
            ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iterator))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.ListFormat iterator result must be object");

        while (true)
        {
            if (!iterator.TryGetPropertyAtom(realm, IdNext, out var nextMethodValue, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Intl.ListFormat iterator.next is not a function");
            if (!nextMethodValue.TryGetObject(out var nextMethodObject) ||
                nextMethodObject is not JsFunction nextMethod)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Intl.ListFormat iterator.next is not a function");

            var stepValue = realm.InvokeFunction(nextMethod, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
            if (!stepValue.TryGetObject(out var stepObject))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Intl.ListFormat iterator result must be object");

            _ = stepObject.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
            if (JsRealm.ToBoolean(doneValue))
                break;

            var nextValue = stepObject.TryGetPropertyAtom(realm, IdValue, out var value, out _)
                ? value
                : JsValue.Undefined;
            if (!nextValue.IsString)
            {
                realm.BestEffortIteratorCloseOnThrow(iterator);
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterable yielded a non-String value");
            }

            list.Add(nextValue.AsString());
        }

        return list;
    }

    private static void AppendRangeParts(JsArray result, ref uint index, JsArray parts, string source)
    {
        for (uint i = 0; i < parts.Length; i++)
        {
            if (!parts.TryGetElement(i, out var entry) || !entry.TryGetObject(out var entryObject))
                continue;
            if (!entryObject.TryGetProperty("type", out var typeValue) || !typeValue.IsString)
                continue;
            if (!entryObject.TryGetProperty("value", out var valueValue) || !valueValue.IsString)
                continue;
            result.SetElement(index++,
                JsValue.FromObject(CreateRangePartObject(result.Realm, typeValue.AsString(), valueValue.AsString(),
                    source)));
        }
    }

    private static JsObject GetIntlOptionsObject(JsRealm realm, in JsValue optionsValue, string errorMessage)
    {
        if (optionsValue.IsUndefined)
            return new JsPlainObject(realm, false);
        if (realm.TryToObject(optionsValue, out var options))
            return options;
        throw new JsRuntimeException(JsErrorKind.TypeError, errorMessage);
    }

    private static JsDurationFormatObject.DurationRecord ToDurationFormatRecord(JsRealm realm, in JsValue value)
    {
        if (value.IsString)
        {
            if (!JsDurationFormatObject.TryParseDurationString(value.AsString(), out var parsed))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid duration string");
            ValidateDurationFormatRecord(parsed);
            return parsed;
        }

        if (!value.TryGetObject(out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Duration must be an object");

        var hasDefinedProperty = false;
        foreach (var property in IntlDurationRecordProperties)
            if (obj.TryGetProperty(property, out var propertyValue) && !propertyValue.IsUndefined)
            {
                hasDefinedProperty = true;
                break;
            }

        if (!hasDefinedProperty)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Duration must have at least one duration property defined");

        ulong presentMask = 0;
        var record = new JsDurationFormatObject.DurationRecord(
            GetDurationFormatComponent(realm, obj, "years", ref presentMask, 0),
            GetDurationFormatComponent(realm, obj, "months", ref presentMask, 1),
            GetDurationFormatComponent(realm, obj, "weeks", ref presentMask, 2),
            GetDurationFormatComponent(realm, obj, "days", ref presentMask, 3),
            GetDurationFormatComponent(realm, obj, "hours", ref presentMask, 4),
            GetDurationFormatComponent(realm, obj, "minutes", ref presentMask, 5),
            GetDurationFormatComponent(realm, obj, "seconds", ref presentMask, 6),
            GetDurationFormatComponent(realm, obj, "milliseconds", ref presentMask, 7),
            GetDurationFormatComponent(realm, obj, "microseconds", ref presentMask, 8),
            GetDurationFormatComponent(realm, obj, "nanoseconds", ref presentMask, 9),
            presentMask);

        ValidateDurationFormatRecord(record);
        return record;
    }

    private static double GetDurationFormatComponent(JsRealm realm, JsObject obj, string property,
        ref ulong presentMask, int bit)
    {
        obj.TryGetProperty(property, out var value);
        if (value.IsUndefined)
            return 0d;
        presentMask |= 1UL << bit;

        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid value for {property}");

        var truncated = Math.Truncate(number);
        if (number != truncated)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Duration property {property} must be an integer");

        return JsDurationFormatObject.NoNegativeZero(truncated);
    }

    private static void ValidateDurationFormatRecord(JsDurationFormatObject.DurationRecord record)
    {
        const double maxYearsMonthsWeeks = 4294967296d;
        if (Math.Abs(record.Years) >= maxYearsMonthsWeeks)
            throw new JsRuntimeException(JsErrorKind.RangeError, "years value out of range");
        if (Math.Abs(record.Months) >= maxYearsMonthsWeeks)
            throw new JsRuntimeException(JsErrorKind.RangeError, "months value out of range");
        if (Math.Abs(record.Weeks) >= maxYearsMonthsWeeks)
            throw new JsRuntimeException(JsErrorKind.RangeError, "weeks value out of range");

        var totalNanoseconds =
            new BigInteger(record.Days) * 86_400_000_000_000 +
            new BigInteger(record.Hours) * 3_600_000_000_000 +
            new BigInteger(record.Minutes) * 60_000_000_000 +
            new BigInteger(record.Seconds) * 1_000_000_000 +
            new BigInteger(record.Milliseconds) * 1_000_000 +
            new BigInteger(record.Microseconds) * 1_000 +
            new BigInteger(record.Nanoseconds);

        var maxTimeDuration = ((BigInteger)1 << 53) * 1_000_000_000;
        if (BigInteger.Abs(totalNanoseconds) >= maxTimeDuration)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Duration time values out of range");

        var hasPositive = record.Years > 0 || record.Months > 0 || record.Weeks > 0 || record.Days > 0 ||
                          record.Hours > 0 || record.Minutes > 0 || record.Seconds > 0 ||
                          record.Milliseconds > 0 || record.Microseconds > 0 || record.Nanoseconds > 0;
        var hasNegative = record.Years < 0 || record.Months < 0 || record.Weeks < 0 || record.Days < 0 ||
                          record.Hours < 0 || record.Minutes < 0 || record.Seconds < 0 ||
                          record.Milliseconds < 0 || record.Microseconds < 0 || record.Nanoseconds < 0;
        if (hasPositive && hasNegative)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                "Duration cannot have mixed positive and negative values");
    }

    private static JsObject GetIntlConstructorOptionsObject(JsRealm realm, in JsValue optionsValue,
        string errorMessage)
    {
        if (optionsValue.IsUndefined)
            return new JsPlainObject(realm, false);
        if (realm.TryToObject(optionsValue, out var options))
            return options;
        throw new JsRuntimeException(JsErrorKind.TypeError, errorMessage);
    }

    private static JsObject GetIntlObjectOnlyOptionsObject(JsRealm realm, in JsValue optionsValue,
        string errorMessage)
    {
        if (optionsValue.IsUndefined)
            return new JsPlainObject(realm, false);
        if (optionsValue.TryGetObject(out var options))
            return options;
        throw new JsRuntimeException(JsErrorKind.TypeError, errorMessage);
    }

    private static void DefineDateTimeResolvedOption(JsRealm realm, JsPlainObject result, int atom, string? value)
    {
        if (value is null)
            return;
        result.DefineDataPropertyAtom(realm, atom, JsValue.FromString(value), JsShapePropertyFlags.Open);
    }

    private static double ToRangeDateTimeFormatNumber(JsRealm realm, in JsValue value)
    {
        if (value.IsUndefined)
            throw new JsRuntimeException(JsErrorKind.TypeError, "start and end are required");
        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid time value");
        return number;
    }

    private static string ValidateAndCanonicalizeDateTimeFormatTimeZone(JsRealm realm, in JsValue value)
    {
        if (value.IsUndefined)
            return "UTC";

        var text = realm.ToJsStringSlowPath(value);
        if (OkojoIntlTimeZoneData.TryGetCanonicalTimeZone(text, out var canonicalNamedTimeZone))
            return canonicalNamedTimeZone;
        if (string.Equals(text, "UTC", StringComparison.OrdinalIgnoreCase))
            return "UTC";
        if (string.Equals(text, "GMT", StringComparison.OrdinalIgnoreCase))
            return "GMT";
        if (string.Equals(text, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
            return "Etc/UTC";
        if (string.Equals(text, "Etc/GMT", StringComparison.OrdinalIgnoreCase))
            return "Etc/GMT";
        if (string.Equals(text, "Etc/UCT", StringComparison.OrdinalIgnoreCase))
            return "Etc/UCT";
        if (string.Equals(text, "Etc/GMT0", StringComparison.OrdinalIgnoreCase))
            return "Etc/GMT0";

        if (TryCanonicalizeDateTimeFormatOffset(text, out var offset))
            return offset;

        if (TryCanonicalizeEtcGmtOffsetTimeZone(text, out var canonicalEtcGmt))
            return canonicalEtcGmt;

        if (!TimeZoneRegex().IsMatch(text))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid timeZone: {text}");

        return NormalizeTimeZoneCasing(text);
    }

    private static bool TryCanonicalizeDateTimeFormatOffset(string text, out string canonical)
    {
        canonical = string.Empty;
        if (text.Length < 3 || text.Length > 6 || (text[0] != '+' && text[0] != '-'))
            return false;

        int hours;
        int minutes;
        if (text.Length == 3)
        {
            if (!char.IsAsciiDigit(text[1]) || !char.IsAsciiDigit(text[2]))
                throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid timeZone: {text}");
            hours = (text[1] - '0') * 10 + (text[2] - '0');
            minutes = 0;
        }
        else if (text.Length == 5)
        {
            if (!char.IsAsciiDigit(text[1]) || !char.IsAsciiDigit(text[2]) ||
                !char.IsAsciiDigit(text[3]) || !char.IsAsciiDigit(text[4]))
                throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid timeZone: {text}");

            hours = (text[1] - '0') * 10 + (text[2] - '0');
            minutes = (text[3] - '0') * 10 + (text[4] - '0');
        }
        else
        {
            if (text.Length != 6 || text[3] != ':' ||
                !char.IsAsciiDigit(text[1]) || !char.IsAsciiDigit(text[2]) ||
                !char.IsAsciiDigit(text[4]) || !char.IsAsciiDigit(text[5]))
                throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid timeZone: {text}");

            hours = (text[1] - '0') * 10 + (text[2] - '0');
            minutes = (text[4] - '0') * 10 + (text[5] - '0');
        }

        if (hours > 23 || minutes > 59)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid timeZone: {text}");

        var sign = hours == 0 && minutes == 0 ? '+' : text[0];
        canonical = $"{sign}{hours:00}:{minutes:00}";
        return true;
    }

    private static bool TryCanonicalizeEtcGmtOffsetTimeZone(string text, out string canonical)
    {
        canonical = string.Empty;
        if (!text.StartsWith("Etc/GMT", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = text["Etc/GMT".Length..];
        if (suffix.Length < 2 || (suffix[0] != '+' && suffix[0] != '-'))
            return false;

        if (!int.TryParse(suffix[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || hours > 23)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid timeZone: {text}");

        canonical = $"Etc/GMT{suffix[0]}{hours}";
        return true;
    }

    private static bool LooksCanonicalTimeZone(string text)
    {
        foreach (var segment in text.Split('/'))
        {
            if (segment.Length == 0)
                return false;
            if (!(char.IsUpper(segment[0]) || segment.Equals("UTC", StringComparison.Ordinal) ||
                  segment.Equals("GMT", StringComparison.Ordinal)))
                return false;
        }

        return true;
    }

    private static string NormalizeTimeZoneCasing(string text)
    {
        static string NormalizeSegment(string segment)
        {
            if (segment.Equals("utc", StringComparison.OrdinalIgnoreCase))
                return "UTC";
            if (segment.Equals("gmt", StringComparison.OrdinalIgnoreCase))
                return "GMT";

            var words = segment.Split('_');
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0)
                    continue;

                var allLower = words[i].All(static c => !char.IsLetter(c) || char.IsLower(c));
                var allUpper = words[i].All(static c => !char.IsLetter(c) || char.IsUpper(c));
                if (allLower || allUpper)
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..].ToLowerInvariant();
            }

            return string.Join("_", words);
        }

        return string.Join("/", text.Split('/').Select(NormalizeSegment));
    }

    private static string GetIntlStringOption(
        JsRealm realm,
        JsObject options,
        string property,
        IReadOnlyList<string> validValues,
        string fallback)
    {
        if (!options.TryGetProperty(property, out var value) || value.IsUndefined)
            return fallback;

        return GetIntlStringOptionValue(realm, value, property, validValues);
    }

    private static string GetIntlStringOptionValue(
        JsRealm realm,
        in JsValue value,
        string property,
        IReadOnlyList<string> validValues)
    {
        var text = realm.ToJsStringSlowPath(value);
        foreach (var validValue in validValues)
            if (string.Equals(text, validValue, StringComparison.Ordinal))
                return text;

        throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid value '{text}' for option '{property}'");
    }

    private static string CanonicalizeDisplayNamesCode(string type, string code)
    {
        switch (type)
        {
            case "language":
                if (!IsValidDisplayNamesLanguageCode(code))
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        $"Invalid code '{code}' for type '{type}'");
                return CanonicalizeUnicodeLocaleId(code);
            case "region":
                if (code.Length == 2 && char.IsLetter(code[0]) && char.IsLetter(code[1]))
                    return code.ToUpperInvariant();
                if (code.Length == 3 && char.IsDigit(code[0]) && char.IsDigit(code[1]) && char.IsDigit(code[2]))
                    return code;
                break;
            case "script":
                if (code.Length == 4 && IsAsciiLetters(code))
                    return char.ToUpperInvariant(code[0]) + code.Substring(1).ToLowerInvariant();
                break;
            case "currency":
                if (code.Length == 3 && IsAsciiLetters(code))
                    return code.ToUpperInvariant();
                break;
            case "calendar":
                if (IsValidDisplayNamesCalendarCode(code))
                    return code.ToLowerInvariant();
                break;
            case "dateTimeField":
                if (TryCanonicalizeDateTimeFieldCode(code, out var canonicalDateTimeField))
                    return canonicalDateTimeField!;
                break;
        }

        throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid code '{code}' for type '{type}'");
    }

    private static bool IsValidDisplayNamesLanguageCode(string code)
    {
        if (string.IsNullOrEmpty(code) ||
            string.Equals(code, "root", StringComparison.OrdinalIgnoreCase) ||
            code.Contains('_') ||
            code[0] == '-' ||
            code[^1] == '-' ||
            code.Contains("--", StringComparison.Ordinal))
            return false;

        foreach (var c in code)
            if ((c < 'A' || c > 'Z') &&
                (c < 'a' || c > 'z') &&
                (c < '0' || c > '9') &&
                c != '-')
                return false;

        var parts = code.Split('-');
        if (parts.Length == 0)
            return false;

        var firstPart = parts[0];
        if (firstPart.Length is 1 or 4 || firstPart.Length > 8 || !IsAsciiLetters(firstPart))
            return false;

        var hasScript = false;
        var hasRegion = false;
        var seenVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || part.Length == 1)
                return false;

            if (part.Length == 2 && char.IsLetter(part[0]))
            {
                if (hasRegion || seenVariants.Count > 0 || !char.IsLetter(part[1]))
                    return false;
                hasRegion = true;
            }
            else if (part.Length == 3 && char.IsDigit(part[0]) && char.IsDigit(part[1]) && char.IsDigit(part[2]))
            {
                if (hasRegion || seenVariants.Count > 0)
                    return false;
                hasRegion = true;
            }
            else if (part.Length == 3 && char.IsDigit(part[0]))
            {
                return false;
            }
            else if (part.Length == 4)
            {
                if (IsAsciiLetters(part))
                {
                    if (hasScript || hasRegion || seenVariants.Count > 0)
                        return false;
                    hasScript = true;
                }
                else if (char.IsDigit(part[0]))
                {
                    if (seenVariants.Contains(part))
                        return false;
                    seenVariants.Add(part);
                }
                else
                {
                    return false;
                }
            }
            else if (part.Length >= 5 && part.Length <= 8)
            {
                foreach (var c in part)
                    if (!char.IsLetterOrDigit(c))
                        return false;

                if (seenVariants.Contains(part))
                    return false;
                seenVariants.Add(part);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetters(string text)
    {
        foreach (var c in text)
            if ((c < 'A' || c > 'Z') && (c < 'a' || c > 'z'))
                return false;

        return true;
    }

    private static bool IsValidDisplayNamesCalendarCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code[0] is '-' or '_' || code[^1] is '-' or '_')
            return false;

        foreach (var c in code)
        {
            if (char.IsWhiteSpace(c) || c == '_')
                return false;
            if ((c < 'A' || c > 'Z') &&
                (c < 'a' || c > 'z') &&
                (c < '0' || c > '9') &&
                c != '-')
                return false;
        }

        var segments = code.Split('-');
        foreach (var segment in segments)
            if (segment.Length < 3 || segment.Length > 8)
                return false;

        return true;
    }

    private static bool TryCanonicalizeDateTimeFieldCode(string code, out string? canonicalCode)
    {
        canonicalCode = code switch
        {
            "era" => "era",
            "year" => "year",
            "quarter" => "quarter",
            "month" => "month",
            "weekOfYear" => "weekOfYear",
            "weekday" => "weekday",
            "day" => "day",
            "dayPeriod" => "dayPeriod",
            "hour" => "hour",
            "minute" => "minute",
            "second" => "second",
            "timeZoneName" => "timeZoneName",
            _ => null
        };
        return canonicalCode is not null;
    }

    private static string NormalizeRelativeTimeFormatUnit(JsRealm realm, in JsValue unitValue)
    {
        var unit = realm.ToJsStringSlowPath(unitValue);
        if (!OkojoIntlUnitData.IsRelativeTimeFormatUnit(unit))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid unit: {unit}");

        return unit switch
        {
            "seconds" => "second",
            "minutes" => "minute",
            "hours" => "hour",
            "days" => "day",
            "weeks" => "week",
            "months" => "month",
            "quarters" => "quarter",
            "years" => "year",
            _ => unit
        };
    }

    private static int GetIntlNumberOption(JsRealm realm, JsObject options, string property, int minimum,
        int maximum, int fallback)
    {
        if (!options.TryGetProperty(property, out var value) || value.IsUndefined)
            return fallback;

        return GetIntlNumberOptionValue(realm, value, property, minimum, maximum);
    }

    private static int GetIntlNumberOptionValue(JsRealm realm, in JsValue value, string property, int minimum,
        int maximum)
    {
        var number = realm.ToNumberSlowPath(value);
        var integer = realm.ToIntegerOrInfinity(new(number));
        if (double.IsNaN(integer) || integer < minimum || integer > maximum)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Invalid value '{number}' for option '{property}'");
        return (int)integer;
    }

    private static bool TryGetIntlNumberOption(JsRealm realm, JsObject options, string property, int minimum,
        int maximum, out int? result)
    {
        result = null;
        if (!options.TryGetProperty(property, out var value) || value.IsUndefined)
            return false;

        var number = realm.ToNumberSlowPath(value);
        var integer = realm.ToIntegerOrInfinity(new(number));
        if (double.IsNaN(integer) || integer < minimum || integer > maximum)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Invalid value '{number}' for option '{property}'");
        result = (int)integer;
        return true;
    }

    private static int GetIntlRoundingIncrementOption(JsRealm realm, JsObject options)
    {
        if (!options.TryGetProperty("roundingIncrement", out var value) || value.IsUndefined)
            return 1;

        var number = realm.ToNumberSlowPath(value);
        var integer = realm.ToIntegerOrInfinity(new(number));
        if (double.IsNaN(integer) || double.IsInfinity(integer) || number != integer)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid roundingIncrement");
        var intValue = (int)integer;
        ReadOnlySpan<int> valid = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000];
        foreach (var candidate in valid)
            if (candidate == intValue)
                return intValue;

        throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid roundingIncrement");
    }

    private static string GetIntlUseGroupingOption(JsRealm realm, JsObject options, string notation)
    {
        var fallback = string.Equals(notation, "compact", StringComparison.Ordinal) ? "min2" : "auto";
        if (!options.TryGetProperty("useGrouping", out var value) || value.IsUndefined)
            return fallback;
        if (value.IsBool)
            return value.IsTrue ? "always" : "false";
        if (value.IsNull)
            return "false";
        if (value.IsNumber)
        {
            var number = value.NumberValue;
            if (number == 0d)
                return "false";
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Invalid value '{number}' for option 'useGrouping'");
        }

        var text = realm.ToJsStringSlowPath(value);
        if (text.Length == 0)
            return "false";
        foreach (var valid in IntlNumberFormatUseGroupingStringValues)
            if (string.Equals(text, valid, StringComparison.Ordinal))
                return text;

        if (string.Equals(text, "true", StringComparison.Ordinal) ||
            string.Equals(text, "false", StringComparison.Ordinal))
            return fallback;
        throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid value '{text}' for option 'useGrouping'");
    }

    private static string ResolveIntlLocale(IReadOnlyList<string> requestedLocales)
    {
        if (requestedLocales.Count == 1)
        {
            var requestedLocale = requestedLocales[0];
            if (IntlResolvedLocaleCache.TryGetValue(requestedLocale, out var cachedResolvedLocale))
                return cachedResolvedLocale;

            var resolvedSingle = IsSupportedIntlLocale(requestedLocale)
                ? requestedLocale
                : GetDefaultResolvedIntlLocale();

            IntlResolvedLocaleCache[requestedLocale] = resolvedSingle;
            return resolvedSingle;
        }

        foreach (var locale in requestedLocales)
            if (IsSupportedIntlLocale(locale))
                return locale;

        return GetDefaultResolvedIntlLocale();
    }

    private static string GetDefaultResolvedIntlLocale()
    {
        var defaultLocale = CultureInfo.CurrentCulture.Name;
        if (string.IsNullOrEmpty(defaultLocale))
            return "en-US";

        var canonicalDefaultLocale = CanonicalizeUnicodeLocaleId(defaultLocale.Replace('_', '-'));
        return IsSupportedIntlLocale(canonicalDefaultLocale) ? canonicalDefaultLocale : "en";
    }

    private static JsArray CreateSupportedLocalesArray(JsRealm realm, IReadOnlyList<string> requestedLocales)
    {
        if (requestedLocales.Count == 0)
            return realm.CreateArrayObject();

        if (requestedLocales.Count == 1)
        {
            var result = realm.CreateArrayObject();
            if (IsSupportedIntlLocale(requestedLocales[0]))
                FreshArrayOperations.DefineElement(result, 0, JsValue.FromString(requestedLocales[0]));
            return result;
        }

        List<string> supported = [];
        foreach (var locale in requestedLocales)
            if (IsSupportedIntlLocale(locale))
                supported.Add(locale);

        return CreateStringArray(realm, supported);
    }

    private static JsArray CreateStringArray(JsRealm realm, IReadOnlyList<string> values)
    {
        var result = realm.CreateArrayObject();
        for (var i = 0; i < values.Count; i++)
            FreshArrayOperations.DefineElement(result, (uint)i, JsValue.FromString(values[i]));
        return result;
    }

    private static JsArray CreateStringArray(JsRealm realm, ReadOnlySpan<string> values)
    {
        var result = realm.CreateArrayObject();
        for (var i = 0; i < values.Length; i++)
            FreshArrayOperations.DefineElement(result, (uint)i, JsValue.FromString(values[i]));
        return result;
    }

    private static bool IsSupportedIntlLocale(string locale)
    {
        if (IntlSupportedLocaleCache.TryGetValue(locale, out var cached))
            return cached;

        var canonicalLocale = CanonicalizeUnicodeLocaleId(RemoveUnicodeExtensions(locale));
        if (IntlSupportedLocaleBaseCache.TryGetValue(canonicalLocale, out var cachedBase))
        {
            IntlSupportedLocaleCache[locale] = cachedBase;
            return cachedBase;
        }

        var culture = GetCultureInfo(canonicalLocale);
        bool supported;
        if (culture is null)
        {
            supported = false;
        }
        else
        {
            var language = GetLocaleLanguageSubtag(canonicalLocale);
            if (string.Equals(language, "zxx", StringComparison.OrdinalIgnoreCase))
                supported = false;
            else if (string.Equals(culture.EnglishName, culture.Name, StringComparison.OrdinalIgnoreCase) &&
                     culture.Name.Length >= 2)
                supported = false;
            else
                supported = true;
        }

        IntlSupportedLocaleBaseCache[canonicalLocale] = supported;
        IntlSupportedLocaleCache[locale] = supported;
        return supported;
    }

    private static bool IsWellFormedNumberingSystem(string numberingSystem)
    {
        if (string.IsNullOrEmpty(numberingSystem))
            return false;

        var parts = numberingSystem.Split('-');
        foreach (var part in parts)
        {
            if (part.Length < 3 || part.Length > 8)
                return false;
            foreach (var c in part)
                if (!char.IsAsciiLetterOrDigit(c))
                    return false;
        }

        return true;
    }

    private static bool IsWellFormedCurrencyCode(string currency)
    {
        if (currency.Length != 3)
            return false;
        foreach (var c in currency)
            if (!char.IsAsciiLetter(c))
                return false;

        return true;
    }

    private static bool IsWellFormedUnitIdentifier(string unit)
    {
        if (OkojoIntlUnitData.IsSimpleSanctionedUnit(unit))
            return true;

        var perIndex = unit.IndexOf("-per-", StringComparison.Ordinal);
        if (perIndex <= 0)
            return false;

        var numerator = unit[..perIndex];
        var denominator = unit[(perIndex + 5)..];
        return OkojoIntlUnitData.IsSimpleSanctionedUnit(numerator) &&
               OkojoIntlUnitData.IsSimpleSanctionedUnit(denominator);
    }

    private static int GetCurrencyDigits(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX" or "UYI"
                or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
            _ => 2
        };
    }

    private static string? ExtractNumberingSystemFromLocale(string locale)
    {
        var uIndex = locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (uIndex < 0)
            return null;
        var extensionPart = locale[(uIndex + 3)..];
        var nuIndex = extensionPart.IndexOf("nu-", StringComparison.OrdinalIgnoreCase);
        if (nuIndex < 0)
            return null;
        var valueStart = nuIndex + 3;
        var valueEnd = valueStart;
        while (valueEnd < extensionPart.Length && extensionPart[valueEnd] != '-')
            valueEnd++;
        return valueEnd > valueStart ? extensionPart[valueStart..valueEnd].ToLowerInvariant() : null;
    }

    private static string RemoveUnsupportedNumberFormatLocaleExtensions(string locale)
    {
        var uIndex = locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (uIndex < 0)
            return locale;

        var baseLocale = locale[..uIndex];
        var extensionPart = locale[(uIndex + 3)..];
        var kept = new List<string>();
        var parts = extensionPart.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length;)
        {
            var key = parts[i];
            if (key.Length != 2)
            {
                i++;
                continue;
            }

            i++;
            var values = new List<string>();
            while (i < parts.Length && parts[i].Length > 2)
                values.Add(parts[i++]);

            if (!string.Equals(key, "nu", StringComparison.OrdinalIgnoreCase) || values.Count != 1)
                continue;

            var numberingSystem = values[0].ToLowerInvariant();
            if (!IsWellFormedNumberingSystem(numberingSystem) ||
                !OkojoIntlNumberingSystemData.IsSupported(numberingSystem))
                continue;

            kept.Add("nu");
            kept.Add(numberingSystem);
        }

        return kept.Count == 0 ? baseLocale : baseLocale + "-u-" + string.Join("-", kept);
    }

    private static string RemoveNumberingSystemFromLocale(string locale)
    {
        var uIndex = locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (uIndex < 0)
            return locale;
        var nuIndex = locale.IndexOf("-nu-", StringComparison.OrdinalIgnoreCase);
        if (nuIndex < 0)
            return locale;

        var valueStart = nuIndex + 4;
        var valueEnd = valueStart;
        while (valueEnd < locale.Length && locale[valueEnd] != '-')
            valueEnd++;

        var hasOtherExtensions = valueEnd < locale.Length;
        var extensionPart = locale[(uIndex + 3)..];
        var hasExtensionsBefore = !extensionPart.StartsWith("nu-", StringComparison.OrdinalIgnoreCase);
        if (!hasOtherExtensions && !hasExtensionsBefore)
            return locale[..uIndex];

        return locale[..nuIndex] + locale[valueEnd..];
    }

    private static string BuildResolvedDateTimeFormatLocale(
        string baseResolvedLocale,
        string? localeHourCycle,
        string? localeNumberingSystem,
        List<string> localeUnicodeAttributes,
        Dictionary<string, string?> localeOtherUnicodeKeywords,
        List<ExtensionSubtag> localeOtherExtensions,
        string? localeCalendar,
        string resolvedHourCycle,
        string resolvedNumberingSystem,
        string resolvedCalendar,
        bool includesHourField,
        bool hour12Explicit,
        bool hourCycleExplicit,
        string? numberingSystemOption)
    {
        var parsedBaseLocale = ParseLanguageTag(baseResolvedLocale);
        ExtractLocaleComponents(parsedBaseLocale, out var language, out var script, out var region, out var variants,
            out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);

        localeOtherUnicodeKeywords.Clear();

        string? finalCalendar = null;
        var localeCalendarSupported = localeCalendar is not null &&
                                      IsSupportedDateTimeFormatCalendar(localeCalendar);
        var optionCalendarSupported = IsSupportedDateTimeFormatCalendar(resolvedCalendar);
        if (localeCalendarSupported &&
            optionCalendarSupported &&
            string.Equals(localeCalendar, resolvedCalendar, StringComparison.OrdinalIgnoreCase))
            finalCalendar = CanonicalizeDateTimeFormatCalendar(localeCalendar!);

        string? finalHourCycle = null;
        if (localeHourCycle is not null &&
            IntlDateTimeFormatHourCycleValues.Contains(localeHourCycle, StringComparer.Ordinal))
        {
            var keepLocaleHourCycle = !hour12Explicit &&
                                      ((!hourCycleExplicit && !includesHourField) ||
                                       string.Equals(localeHourCycle, resolvedHourCycle,
                                           StringComparison.OrdinalIgnoreCase));
            if (keepLocaleHourCycle)
                finalHourCycle = localeHourCycle.ToLowerInvariant();
        }

        string? finalNumberingSystem = null;
        var localeNumberingSystemSupported = localeNumberingSystem is not null &&
                                             IsWellFormedNumberingSystem(localeNumberingSystem) &&
                                             OkojoIntlNumberingSystemData.IsSupported(localeNumberingSystem);
        var numberingSystemOptionSupported = numberingSystemOption is not null &&
                                             OkojoIntlNumberingSystemData.IsSupported(numberingSystemOption);
        if (localeNumberingSystemSupported &&
            string.Equals(localeNumberingSystem, resolvedNumberingSystem, StringComparison.OrdinalIgnoreCase) &&
            (!numberingSystemOptionSupported ||
             string.Equals(localeNumberingSystem, numberingSystemOption, StringComparison.OrdinalIgnoreCase)))
            finalNumberingSystem = localeNumberingSystem!.ToLowerInvariant();

        return BuildLocaleWithExtensions(language, script, region, variants, finalCalendar, null, null, null,
            finalHourCycle,
            finalNumberingSystem, null, localeUnicodeAttributes, localeOtherUnicodeKeywords, localeOtherExtensions);
    }

    private static string ResolveDateTimeFormatCalendar(string? localeCalendar, string? calendarOption)
    {
        if (calendarOption is not null)
        {
            var canonicalOption = CanonicalizeDateTimeFormatCalendar(calendarOption);
            if (string.Equals(canonicalOption, "islamic", StringComparison.Ordinal) ||
                string.Equals(canonicalOption, "islamic-rgsa", StringComparison.Ordinal))
                return "islamic-civil";

            if (IsSupportedDateTimeFormatCalendar(canonicalOption))
                return canonicalOption;
        }

        if (localeCalendar is not null)
        {
            var canonicalLocale = CanonicalizeDateTimeFormatCalendar(localeCalendar);
            if (string.Equals(canonicalLocale, "islamic", StringComparison.Ordinal) ||
                string.Equals(canonicalLocale, "islamic-rgsa", StringComparison.Ordinal))
                return "islamic-civil";

            if (IsSupportedDateTimeFormatCalendar(canonicalLocale))
                return canonicalLocale;
        }

        return "gregory";
    }

    private static string CanonicalizeDateTimeFormatCalendar(string calendar)
    {
        var text = calendar.ToLowerInvariant();
        if (OkojoIntlLocaleData.UnicodeMappings.TryGetValue("ca", out var mappings) &&
            mappings.TryGetValue(text, out var alias))
            text = alias;

        return text;
    }

    private static bool IsSupportedDateTimeFormatCalendar(string? calendar)
    {
        if (string.IsNullOrEmpty(calendar))
            return false;
        var canonical = CanonicalizeDateTimeFormatCalendar(calendar);
        return OkojoIntlCalendarData.IsSupportedCalendar(canonical) ||
               string.Equals(canonical, "islamic", StringComparison.Ordinal) ||
               string.Equals(canonical, "islamic-rgsa", StringComparison.Ordinal);
    }

    private static string EnsureNumberingSystemInLocale(string locale, string numberingSystem)
    {
        var uIndex = locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (uIndex < 0)
            return locale + "-u-nu-" + numberingSystem;

        var nuPattern = "-nu-" + numberingSystem;
        if (locale.Contains(nuPattern, StringComparison.OrdinalIgnoreCase))
            return locale;

        var nuIndex = locale.IndexOf("-nu-", StringComparison.OrdinalIgnoreCase);
        if (nuIndex < 0)
            return locale.Insert(uIndex + 3, "nu-" + numberingSystem + "-");

        var valueStart = nuIndex + 4;
        var valueEnd = valueStart;
        while (valueEnd < locale.Length && locale[valueEnd] != '-')
            valueEnd++;
        return locale[..valueStart] + numberingSystem + locale[valueEnd..];
    }

    private static JsLocaleObject CreateLocaleObject(JsRealm realm, JsObject prototype, string canonicalTag,
        in JsValue optionsValue)
    {
        var parsed = ParseLanguageTag(canonicalTag);
        ExtractLocaleComponents(parsed, out var language, out var script, out var region, out var variants,
            out var calendar, out var caseFirst, out var collation, out var hourCycle, out var numberingSystem,
            out var numeric, out var firstDayOfWeek, out var unicodeAttributes, out var otherUnicodeKeywords,
            out var otherExtensions);

        JsObject? options = null;
        if (!optionsValue.IsUndefined)
            if (!realm.TryToObject(optionsValue, out options))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Intl.Locale options must be an object");

        language = GetLocaleLanguageOption(realm, options, language);
        script = GetLocaleScriptOption(realm, options, script);
        region = GetLocaleRegionOption(realm, options, region);
        variants = GetLocaleVariantsOption(realm, options, variants);
        calendar = GetUnicodeKeywordOption(realm, options, "calendar", "ca", calendar);
        collation = GetUnicodeKeywordOption(realm, options, "collation", "co", collation);
        firstDayOfWeek = GetFirstDayOfWeekOption(realm, options, firstDayOfWeek);
        hourCycle = GetHourCycleOption(realm, options, hourCycle);
        caseFirst = GetCaseFirstOption(realm, options, caseFirst);
        numeric = GetNumericOption(realm, options, numeric);
        numberingSystem = GetUnicodeKeywordOption(realm, options, "numberingSystem", "nu", numberingSystem);

        var baseName = BuildLocaleBaseName(language, script, region, variants);
        var canonicalBaseName = CanonicalizeUnicodeLocaleId(baseName);
        var baseParsed = ParseLanguageTag(canonicalBaseName);
        language = baseParsed.Language ?? "und";
        script = baseParsed.Script;
        region = baseParsed.Region;
        variants = baseParsed.Variants?.ToArray() ?? [];

        var locale = BuildLocaleWithExtensions(language, script, region, variants, calendar, caseFirst, collation,
            firstDayOfWeek, hourCycle, numberingSystem, numeric, unicodeAttributes, otherUnicodeKeywords,
            otherExtensions);
        var cultureBaseName = BuildLocaleBaseName(language, script, region, []);
        var cultureInfo = GetCultureInfo(cultureBaseName) ?? CultureInfo.InvariantCulture;

        return new(realm, prototype, locale, canonicalBaseName, language, script, region, variants,
            calendar, caseFirst, collation, hourCycle, numberingSystem, numeric, firstDayOfWeek, cultureInfo);
    }

    private static string BuildLocaleWithExtensions(
        string language,
        string? script,
        string? region,
        string[] variants,
        string? calendar,
        string? caseFirst,
        string? collation,
        string? firstDayOfWeek,
        string? hourCycle,
        string? numberingSystem,
        bool? numeric,
        List<string> unicodeAttributes,
        Dictionary<string, string?> otherUnicodeKeywords,
        List<ExtensionSubtag> otherExtensions)
    {
        var parts = new List<string>();
        parts.Add(language);
        if (!string.IsNullOrEmpty(script))
            parts.Add(script);
        if (!string.IsNullOrEmpty(region))
            parts.Add(region);
        parts.AddRange(variants);

        var unicodeParts = new List<string>();
        if (unicodeAttributes.Count > 0)
            unicodeParts.AddRange(unicodeAttributes);
        AddUnicodeKeyword(unicodeParts, "ca", calendar);
        AddUnicodeKeyword(unicodeParts, "co", collation);
        AddUnicodeKeyword(unicodeParts, "fw", firstDayOfWeek);
        AddUnicodeKeyword(unicodeParts, "hc", hourCycle);
        AddUnicodeKeyword(unicodeParts, "kf", caseFirst);
        if (numeric.HasValue)
            AddUnicodeKeyword(unicodeParts, "kn", numeric.Value ? string.Empty : "false");
        AddUnicodeKeyword(unicodeParts, "nu", numberingSystem);
        foreach (var kv in otherUnicodeKeywords.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
            AddUnicodeKeyword(unicodeParts, kv.Key, kv.Value);
        if (unicodeParts.Count > 0)
        {
            parts.Add("u");
            parts.AddRange(unicodeParts);
        }

        foreach (var ext in otherExtensions)
            parts.AddRange(ext.Parts);
        return CanonicalizeUnicodeLocaleId(string.Join("-", parts));
    }

    private static void AddUnicodeKeyword(List<string> target, string key, string? value)
    {
        if (value is null)
            return;
        target.Add(key);
        if (value.Length != 0)
            target.AddRange(value.Split('-'));
    }

    private static string BuildLocaleBaseName(string language, string? script, string? region,
        IReadOnlyList<string> variants)
    {
        var parts = new List<string> { language };
        if (!string.IsNullOrEmpty(script))
            parts.Add(script);
        if (!string.IsNullOrEmpty(region))
            parts.Add(region);
        for (var i = 0; i < variants.Count; i++)
            parts.Add(variants[i]);
        return string.Join("-", parts);
    }

    private static void ExtractLocaleComponents(
        ParsedLanguageTag parsed,
        out string language,
        out string? script,
        out string? region,
        out string[] variants,
        out string? calendar,
        out string? caseFirst,
        out string? collation,
        out string? hourCycle,
        out string? numberingSystem,
        out bool? numeric,
        out string? firstDayOfWeek,
        out List<string> unicodeAttributes,
        out Dictionary<string, string?> otherUnicodeKeywords,
        out List<ExtensionSubtag> otherExtensions)
    {
        language = parsed.Language ?? "und";
        script = parsed.Script;
        region = parsed.Region;
        variants = parsed.Variants?.ToArray() ?? [];
        calendar = null;
        caseFirst = null;
        collation = null;
        hourCycle = null;
        numberingSystem = null;
        numeric = null;
        firstDayOfWeek = null;
        unicodeAttributes = [];
        otherUnicodeKeywords = new(StringComparer.Ordinal);
        otherExtensions = [];

        if (parsed.Extensions is null)
            return;

        foreach (var extension in parsed.Extensions)
        {
            if (extension.Type != 'u')
            {
                otherExtensions.Add(new() { Type = extension.Type, Parts = [.. extension.Parts] });
                continue;
            }

            for (var i = 1; i < extension.Parts.Count;)
            {
                var part = extension.Parts[i];
                if (part.Length != 2)
                {
                    unicodeAttributes.Add(part);
                    i++;
                    continue;
                }

                var key = part;
                i++;
                var values = new List<string>();
                while (i < extension.Parts.Count && extension.Parts[i].Length != 2)
                    values.Add(extension.Parts[i++]);

                var joined = values.Count == 0 ? string.Empty : string.Join("-", values);
                switch (key)
                {
                    case "ca":
                        calendar = joined;
                        break;
                    case "co":
                        collation = joined;
                        break;
                    case "fw":
                        firstDayOfWeek = joined;
                        break;
                    case "hc":
                        hourCycle = joined;
                        break;
                    case "kf":
                        caseFirst = joined;
                        break;
                    case "kn":
                        numeric = joined.Length == 0 || !string.Equals(joined, "false", StringComparison.Ordinal);
                        break;
                    case "nu":
                        numberingSystem = joined;
                        break;
                    default:
                        otherUnicodeKeywords[key] = joined;
                        break;
                }
            }
        }
    }

    private static string GetLocaleLanguageOption(JsRealm realm, JsObject? options, string fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "language");
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value);
        if (!IsAsciiLanguageOption(text))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid language option: {text}");
        return text.ToLowerInvariant();
    }

    private static string? GetLocaleScriptOption(JsRealm realm, JsObject? options, string? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "script");
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value);
        if (!IsAsciiLettersOnly(text, 4, 4))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid script option: {text}");
        text = text.ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string? GetLocaleRegionOption(JsRealm realm, JsObject? options, string? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "region");
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value);
        if (!IsAsciiRegionOption(text))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid region option: {text}");
        return text.Length == 2 ? text.ToUpperInvariant() : text;
    }

    private static string[] GetLocaleVariantsOption(JsRealm realm, JsObject? options, string[] fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "variants");
        if (value.IsUndefined)
            return fallback;

        var text = realm.ToJsStringSlowPath(value);
        if (text.Length == 0 || text.StartsWith("-", StringComparison.Ordinal) ||
            text.EndsWith("-", StringComparison.Ordinal) || text.Contains("--", StringComparison.Ordinal))
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid variants option: ");
        var parts = text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variants = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (!IsValidVariant(part) || !seen.Add(part))
                throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid variants option: {text}");
            variants.Add(part.ToLowerInvariant());
        }

        variants.Sort(StringComparer.Ordinal);
        return variants.ToArray();
    }

    private static string? GetUnicodeKeywordOption(JsRealm realm, JsObject? options, string propertyName,
        string keyword, string? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, propertyName);
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value).ToLowerInvariant();
        if (!IsAsciiLowerAlphaNumericTypeSequence(text))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid {propertyName} option: {text}");

        if (OkojoIntlLocaleData.UnicodeMappings.TryGetValue(keyword, out var mappings) &&
            mappings.TryGetValue(text, out var alias))
            text = alias;

        return text;
    }

    private static string? GetHourCycleOption(JsRealm realm, JsObject? options, string? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "hourCycle");
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value);
        return text switch
        {
            "h11" or "h12" or "h23" or "h24" => text,
            _ => throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid hourCycle option: {text}")
        };
    }

    private static string? GetCaseFirstOption(JsRealm realm, JsObject? options, string? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "caseFirst");
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value);
        return text switch
        {
            "upper" or "lower" or "false" => text,
            _ => throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid caseFirst option: {text}")
        };
    }

    private static bool? GetNumericOption(JsRealm realm, JsObject? options, bool? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "numeric");
        return value.IsUndefined ? fallback : JsRealm.ToBoolean(value);
    }

    private static CompareOptions MapCollatorCompareOptions(string sensitivity, bool ignorePunctuation)
    {
        var options = sensitivity switch
        {
            "base" => CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace,
            "accent" => CompareOptions.IgnoreCase,
            "case" => CompareOptions.IgnoreNonSpace,
            _ => CompareOptions.None
        };

        if (ignorePunctuation)
            options |= CompareOptions.IgnoreSymbols;

        return options;
    }

    private static string ResolveSupportedCollation(string locale, string usage, string? optionCollation,
        string? localeCollation)
    {
        if (string.Equals(usage, "search", StringComparison.Ordinal))
            return "default";

        var localeLanguage = GetLanguageSubtag(locale);
        var supported = IntlCollatorLocaleCollationSupport.TryGetValue(localeLanguage, out var values)
            ? values
            : null;

        if (optionCollation is not null &&
            IsSupportedCollationValue(optionCollation) &&
            supported is not null &&
            supported.Contains(optionCollation))
            return optionCollation;

        if (localeCollation is not null &&
            IsSupportedCollationValue(localeCollation) &&
            supported is not null &&
            supported.Contains(localeCollation))
            return localeCollation;

        return "default";
    }

    private static bool IsSupportedCollationValue(string value)
    {
        return value switch
        {
            "default" or "phonebk" or "eor" or "ducet" or "emoji" => true,
            _ => false
        };
    }

    private static string BuildResolvedCollatorLocale(
        string baseLocale,
        string? localeCaseFirst,
        string? localeCollation,
        bool? localeNumeric,
        string caseFirst,
        string collation,
        bool numeric)
    {
        var extensions = new List<string>();
        if (localeCollation is not null &&
            string.Equals(localeCollation, collation, StringComparison.Ordinal) &&
            !string.Equals(collation, "default", StringComparison.Ordinal))
        {
            extensions.Add("co");
            extensions.Add(collation);
        }

        if (localeCaseFirst is not null &&
            string.Equals(localeCaseFirst, caseFirst, StringComparison.Ordinal) &&
            !string.Equals(caseFirst, "false", StringComparison.Ordinal))
        {
            extensions.Add("kf");
            extensions.Add(caseFirst);
        }

        if (localeNumeric.HasValue &&
            localeNumeric.Value == numeric)
        {
            extensions.Add("kn");
            if (!numeric)
                extensions.Add("false");
        }

        if (extensions.Count == 0)
            return baseLocale;

        return baseLocale + "-u-" + string.Join("-", extensions);
    }

    private static string GetLanguageSubtag(string locale)
    {
        var dash = locale.IndexOf('-');
        return dash >= 0 ? locale[..dash].ToLowerInvariant() : locale.ToLowerInvariant();
    }

    private static string? GetFirstDayOfWeekOption(JsRealm realm, JsObject? options, string? fallback)
    {
        var value = GetLocaleOptionValue(realm, options, "firstDayOfWeek");
        if (value.IsUndefined)
            return fallback;
        var text = realm.ToJsStringSlowPath(value);
        var normalized = NormalizeFirstDayOfWeek(text);
        if (normalized is not null)
            return normalized;
        if (!IsAsciiAlphaNumericTypeSequence(text))
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid firstDayOfWeek option: {text}");
        return text.ToLowerInvariant();
    }

    private static JsValue GetLocaleOptionValue(JsRealm realm, JsObject? options, string name)
    {
        if (options is null)
            return JsValue.Undefined;
        options.TryGetProperty(name, out var value);
        return value;
    }

    private static string? NormalizeFirstDayOfWeek(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "true" => string.Empty,
            "mon" or "1" => "mon",
            "tue" or "2" => "tue",
            "wed" or "3" => "wed",
            "thu" or "4" => "thu",
            "fri" or "5" => "fri",
            "sat" or "6" => "sat",
            "sun" or "0" or "7" => "sun",
            "false" => "false",
            _ => null
        };
    }

    private static bool IsAsciiLanguageOption(string text)
    {
        return IsAsciiLettersOnly(text, 2, 3) || IsAsciiLettersOnly(text, 5, 8);
    }

    private static bool IsAsciiRegionOption(string text)
    {
        return IsAsciiLettersOnly(text, 2, 2) || IsAsciiDigitsOnly(text, 3, 3);
    }

    private static bool IsAsciiAlphaNumericTypeSequence(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var segmentLength = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '-')
            {
                if (segmentLength is < 3 or > 8)
                    return false;
                segmentLength = 0;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(ch))
                return false;

            segmentLength++;
            if (segmentLength > 8)
                return false;
        }

        return segmentLength is >= 3 and <= 8;
    }

    private static bool IsAsciiLowerAlphaNumericTypeSequence(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var segmentLength = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '-')
            {
                if (segmentLength is < 3 or > 8)
                    return false;
                segmentLength = 0;
                continue;
            }

            if (!((ch >= 'a' && ch <= 'z') || char.IsAsciiDigit(ch)))
                return false;

            segmentLength++;
            if (segmentLength > 8)
                return false;
        }

        return segmentLength is >= 3 and <= 8;
    }

    private static bool IsAsciiLettersOnly(string text, int minLength, int maxLength)
    {
        if (text.Length < minLength || text.Length > maxLength)
            return false;

        for (var i = 0; i < text.Length; i++)
            if (!char.IsAsciiLetter(text[i]))
                return false;

        return true;
    }

    private static bool IsAsciiDigitsOnly(string text, int minLength, int maxLength)
    {
        if (text.Length < minLength || text.Length > maxLength)
            return false;

        for (var i = 0; i < text.Length; i++)
            if (!char.IsAsciiDigit(text[i]))
                return false;

        return true;
    }

    private static string GetDefaultHourCycle(CultureInfo cultureInfo)
    {
        return cultureInfo.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal) ? "h23" : "h12";
    }

    private static string GetDefaultFirstDayOfWeek(string? region)
    {
        return region?.ToUpperInvariant() switch
        {
            "US" or "CA" or "JP" => "sun",
            _ => "mon"
        };
    }

    private static int GetWeekdayNumber(string firstDayOfWeek)
    {
        return firstDayOfWeek switch
        {
            "mon" => 1,
            "tue" => 2,
            "wed" => 3,
            "thu" => 4,
            "fri" => 5,
            "sat" => 6,
            _ => 7
        };
    }

    private static string[] GetTimeZonesForRegion(string region)
    {
        return region.ToUpperInvariant() switch
        {
            "US" => ["America/Chicago", "America/Los_Angeles", "America/New_York"],
            "GB" => ["Europe/London"],
            "DE" => ["Europe/Berlin"],
            "FR" => ["Europe/Paris"],
            "JP" => ["Asia/Tokyo"],
            "CN" => ["Asia/Shanghai"],
            "CA" => ["America/Toronto"],
            _ => ["UTC"]
        };
    }

    private static JsObject GetIntlPrototypeFromConstructor(
        JsRealm realm,
        in JsValue newTarget,
        JsHostFunction activeFunction,
        JsObject intrinsicDefaultPrototype,
        string constructorName)
    {
        if (newTarget.TryGetObject(out var newTargetObj) && newTargetObj is JsFunction newTargetFunction)
        {
            if (newTargetObj.TryGetPropertyAtom(realm, IdPrototype, out var explicitPrototypeValue, out _) &&
                explicitPrototypeValue.TryGetObject(out var explicitPrototypeObj))
                return explicitPrototypeObj;

            var functionRealm = GetFunctionRealm(realm, newTargetFunction);
            if (functionRealm.Global.TryGetValue("Intl", out var intlValue) &&
                intlValue.TryGetObject(out var intlObj) &&
                intlObj.TryGetProperty(constructorName, out var ctorValue) &&
                ctorValue.TryGetObject(out var ctorObj) &&
                ctorObj.TryGetPropertyAtom(functionRealm, IdPrototype, out var realmPrototypeValue, out _) &&
                realmPrototypeValue.TryGetObject(out var realmPrototypeObj))
                return realmPrototypeObj;
        }

        return realm.Intrinsics.GetPrototypeFromConstructorOrIntrinsic(newTarget, activeFunction,
            intrinsicDefaultPrototype);
    }

    [GeneratedRegex(@"^[A-Za-z._+-]+(?:/[A-Za-z0-9._+-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZoneRegex();

    private sealed class ParsedLanguageTag
    {
        public string? Language { get; set; }
        public string? Script { get; set; }
        public string? Region { get; set; }
        public List<string>? Variants { get; set; }
        public List<ExtensionSubtag>? Extensions { get; set; }
    }

    private sealed class ExtensionSubtag
    {
        public char Type { get; set; }
        public List<string> Parts { get; set; } = [];
    }

    private sealed class KeyValueParts
    {
        public string Key { get; set; } = string.Empty;
        public List<string> Values { get; set; } = [];
    }
}

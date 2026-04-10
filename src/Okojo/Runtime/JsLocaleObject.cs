using System.Globalization;

namespace Okojo.Runtime;

internal sealed class JsLocaleObject : JsObject
{
    public JsLocaleObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string baseName,
        string language,
        string? script,
        string? region,
        string[] variants,
        string? calendar,
        string? caseFirst,
        string? collation,
        string? hourCycle,
        string? numberingSystem,
        bool? numeric,
        string? firstDayOfWeek,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        BaseName = baseName;
        Language = language;
        Script = script;
        Region = region;
        Variants = variants;
        Calendar = calendar;
        CaseFirst = caseFirst;
        Collation = collation;
        HourCycle = hourCycle;
        NumberingSystem = numberingSystem;
        Numeric = numeric;
        FirstDayOfWeek = firstDayOfWeek;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string BaseName { get; }
    internal string Language { get; }
    internal string? Script { get; }
    internal string? Region { get; }
    internal string[] Variants { get; }
    internal string? Calendar { get; }
    internal string? CaseFirst { get; }
    internal string? Collation { get; }
    internal string? HourCycle { get; }
    internal string? NumberingSystem { get; }
    internal bool? Numeric { get; }
    internal string? FirstDayOfWeek { get; }
    internal CultureInfo CultureInfo { get; }
}

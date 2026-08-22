using System.Globalization;
using Okojo.Globalization;

namespace Okojo.Runtime;

internal sealed class JsCollatorObject : JsObject
{
    private readonly Collator core;
    private JsHostFunction? boundCompare;

    internal JsCollatorObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string usage,
        string sensitivity,
        bool ignorePunctuation,
        string collation,
        bool numeric,
        string caseFirst,
        CompareInfo compareInfo,
        CompareOptions compareOptions
    )
        : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        Usage = usage;
        Sensitivity = sensitivity;
        IgnorePunctuation = ignorePunctuation;
        Collation = collation;
        Numeric = numeric;
        CaseFirst = caseFirst;
        CompareInfo = compareInfo;
        CompareOptions = compareOptions;
        core = new(
            locale,
            usage,
            sensitivity,
            ignorePunctuation,
            collation,
            numeric,
            caseFirst,
            compareInfo,
            compareOptions
        );
    }

    internal string Locale { get; }
    internal string Usage { get; }
    internal string Sensitivity { get; }
    internal bool IgnorePunctuation { get; }
    internal string Collation { get; }
    internal bool Numeric { get; }
    internal string CaseFirst { get; }
    internal CompareInfo CompareInfo { get; }
    internal CompareOptions CompareOptions { get; }

    internal JsHostFunction GetOrCreateBoundCompare(JsRealm realm)
    {
        if (boundCompare is not null)
            return boundCompare;

        boundCompare = new(
            realm,
            static (in info) =>
            {
                var collator = (JsCollatorObject)((JsHostFunction)info.Function).UserData!;
                var x =
                    info.Arguments.Length > 0
                        ? info.Realm.ToJsStringSlowPath(info.Arguments[0])
                        : "undefined";
                var y =
                    info.Arguments.Length > 1
                        ? info.Realm.ToJsStringSlowPath(info.Arguments[1])
                        : "undefined";
                return JsValue.FromInt32(collator.Compare(x, y));
            },
            string.Empty,
            2
        )
        {
            UserData = this,
        };
        return boundCompare;
    }

    internal int Compare(string x, string y)
    {
        return core.Compare(x, y);
    }
}

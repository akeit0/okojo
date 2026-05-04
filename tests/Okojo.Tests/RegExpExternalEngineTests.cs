using Okojo.Objects;
using Okojo.RegExp;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RegExpExternalEngineTests
{
    [Test]
    public void ExternalRegExpEngine_CompilesAndExecutes()
    {
        var engine = RegExpEngine.Default;
        var compiled = engine.Compile(@"(a)(b)?", "g");

        var match = engine.Exec(compiled, "zabz", 1);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Length, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("ab"));
        Assert.That(match.Groups[1], Is.EqualTo("a"));
        Assert.That(match.Groups[2], Is.EqualTo("b"));

        var named = engine.Compile(@"(?<name>a)", "");
        var namedMatch = engine.Exec(named, "za", 0);
        Assert.That(namedMatch, Is.Not.Null);
        Assert.That(namedMatch!.NamedGroups, Is.Not.Null);
        Assert.That(namedMatch.NamedGroups!["name"], Is.EqualTo("a"));
    }

    [Test]
    public void ExternalRegExpEngine_LeadingLiteralHintKeepsZeroWidthPrefixCorrect()
    {
        var engine = RegExpEngine.Default;
        var compiled = engine.Compile(@"\bfoo", "");

        var match = engine.Exec(compiled, " foo", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Length, Is.EqualTo(3));
        Assert.That(match.Groups[0], Is.EqualTo("foo"));
    }

    [Test]
    public void ExternalRegExpEngine_AllowsEmptyMatchesWithoutSkippingSearch()
    {
        var engine = RegExpEngine.Default;
        var compiled = engine.Compile(@"a*", "g");

        var match = engine.Exec(compiled, "", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(0));
        Assert.That(match.Length, Is.EqualTo(0));
        Assert.That(match.Groups[0], Is.EqualTo(string.Empty));
    }

    [Test]
    public void ExternalRegExpEngine_RejectsUnsupportedInlineModifiers()
    {
        var engine = RegExpEngine.Default;

        Assert.That(() => engine.Compile(@"(?I:a)", ""), Throws.ArgumentException);
    }

    [Test]
    public void ExternalRegExpEngine_CompilesUnicodeCaseFoldSample()
    {
        var engine = RegExpEngine.Default;

        var compiled = engine.Compile(@"[\u0390]", "ui");
        var match = engine.Exec(compiled, "\u1fd3", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Groups[0], Is.EqualTo("\u1fd3"));

        var ligature = engine.Compile(@"[\ufb05]", "ui");
        Assert.That(engine.Exec(ligature, "\ufb06", 0), Is.Not.Null);
    }

    [Test]
    public void ExternalRegExpEngine_CombinesEscapedSurrogatePairsInUnicodeMode()
    {
        var engine = RegExpEngine.Default;

        var compiled = engine.Compile(@"\ud834\udf06", "u");
        var match = engine.Exec(compiled, "𝌆", 0);
        var classCompiled = engine.Compile(@"[\ud834\udf06]", "u");
        var classMatch = engine.Exec(classCompiled, "𝌆", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Groups[0], Is.EqualTo("𝌆"));
        Assert.That(classMatch, Is.Not.Null);
        Assert.That(classMatch!.Groups[0], Is.EqualTo("𝌆"));
    }

    [Test]
    public void OkojoWrapsExternalRegExpCompileErrorsAsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               try {
                                   new RegExp("(?I:a)");
                                   false;
                               } catch (e) {
                                   e instanceof SyntaxError;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpConstructor_ReturnsInputWhenConstructorMatches()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const obj = { constructor: RegExp };
                               obj[Symbol.match] = true;
                               RegExp(obj) === obj;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpConstructor_UsesRegExpLikeSourceAndFlags()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const obj = { source: "source text", flags: "i" };
                               obj[Symbol.match] = true;
                               const re = new RegExp(obj);
                               re.source === "source text" && re.flags === "i";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoCanUseExternalRegExpEngineForJsSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const re = new RegExp("a+", "g");
                               const m1 = re.exec("baaa");
                               const li1 = re.lastIndex;
                               const m2 = re.exec("baaa");
                               const li2 = re.lastIndex;
                               m1[0] === "aaa" && li1 === 4 && m2 === null && li2 === 0;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoExternalRegExpEngineHonorsUnicodeCaseFoldEdges()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               /[\u0390]/ui.test("\u1fd3") && /[\ufb05]/ui.test("\ufb06") && /[\ufb06]/ui.test("\ufb05");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoExternalRegExpEngineMatchesLookaheadBackreferenceRegression()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const m = /(.*?)a(?!(a+)b\2c)\2(.*)/.exec("baaabaac");
                               m && m.index === 0 && m[1] === "ba" && m[2] === undefined && m[3] === "abaac";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngineDirectReproForLookaheadBackreference()
    {
        var engine = RegExpEngine.Default;
        var compiled = engine.Compile(@"(.*?)a(?!(a+)b\2c)\2(.*)", "");
        var match = engine.Exec(compiled, "baaabaac", 0);

        Assert.That(match, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(match!.Index, Is.EqualTo(0));
            Assert.That(match.Groups[0], Is.EqualTo("baaabaac"));
            Assert.That(match.Groups[1], Is.EqualTo("ba"));
            Assert.That(match.Groups[2], Is.Null);
            Assert.That(match.Groups[3], Is.EqualTo("abaac"));
        });
    }

    [Test]
    public void OkojoToStringThrowsWhenCoercingThrowingObjectProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(() => realm.Eval("""
                                     const v = { 0: { toString() { throw new Error("boom"); } } };
                                     "" + v[0];
                                     """), Throws.InstanceOf<JsRuntimeException>());
    }

    [Test]
    public void OkojoNumericObjectLiteralPropertyIsAccessible()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var value = realm.Eval("""
                               ({
                                 0: { toString() { return "ok"; } }
                               })
                               """);

        Assert.That(value.TryGetObject(out var obj), Is.True);
        Assert.That(obj!.TryGetProperty("0", out var named), Is.True);
        Assert.That(obj.TryGetElement(0, out var element), Is.True);
        Assert.That(realm.ToJsStringSlowPath(named), Is.EqualTo("ok"));
        Assert.That(realm.ToJsStringSlowPath(element), Is.EqualTo("ok"));

        Assert.That(realm.Atoms.TryGetInterned("0", out _), Is.False);
    }

    [Test]
    public void OkojoRegExpExecOverrideIsObserved()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = /./;
                               r.exec = function() {
                                 return { 0: { toString() { throw new Error("boom"); } } };
                               };
                               try {
                                 const m = r.exec("a");
                                 "" + m[0];
                                 false;
                               } catch (e) {
                                 e instanceof Error;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpReplaceExecResultStringCoercionIsObserved()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = /./;
                               r.exec = function() {
                                 return { 0: { toString() { throw new Error("boom"); } } };
                               };
                               try {
                                 "a".replace(r, "$&");
                                 false;
                               } catch (e) {
                                 e instanceof Error;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpMatchThrowsWhenLastIndexIsNotWritable()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = /./g;
                               Object.defineProperty(r, 'lastIndex', { writable: false });
                               try {
                                 r[Symbol.match]('');
                                 false;
                               } catch (e) {
                                 e instanceof TypeError;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpFlagsGetter_IsGenericAndObservesUnicodeSets()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const get = Object.getOwnPropertyDescriptor(RegExp.prototype, "flags").get;
                               const obj = {
                                 global: 1,
                                 ignoreCase: 0,
                                 multiline: "",
                                 dotAll: true,
                                 unicode: true,
                                 unicodeSets: true,
                                 sticky: false,
                                 hasIndices: "x"
                               };
                               get.call(obj) === "dgsv";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpCrossRealmGetterUsesGetterRealmTypeError()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        var getterValue = otherRealm.Eval("""Object.getOwnPropertyDescriptor(RegExp.prototype, "dotAll").get""");

        Assert.That(getterValue.TryGetObject(out var getterObj), Is.True);
        Assert.That(getterObj, Is.InstanceOf<JsFunction>());

        var ex = Assert.Throws<JsRuntimeException>(() =>
            otherRealm.InvokeFunction((JsFunction)getterObj!, realm.Eval("RegExp.prototype"),
                ReadOnlySpan<JsValue>.Empty));

        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.ErrorRealm, Is.SameAs(otherRealm));
    }

    [Test]
    public void RegExpExec_UsesUndefinedAndObservableLastIndex()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const a = /undefined/.exec()[0] === "undefined";
                               let gets = 0;
                               const counter = { valueOf() { gets++; return 0; } };
                               const re = /./;
                               re.lastIndex = counter;
                               const match = re.exec("abc");
                               a && match[0] === "a" && gets === 1 && re.lastIndex === counter;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpExec_StickyFailureThrowsOnLastIndexWrite()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const re = /c/y;
                               Object.defineProperty(re, "lastIndex", { writable: false });
                               try {
                                 re.exec("abc");
                                 false;
                               } catch (e) {
                                 e instanceof TypeError;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_SupportsXidPropertyFrontier()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               /^\p{XID_Start}+$/u.test("A\u03C0\u16A0") &&
                               /^\p{XIDS}+$/u.test("A\u03C0\u16A0") &&
                               /^\p{XID_Continue}+$/u.test("A0_") &&
                               /^\p{XIDC}+$/u.test("A0_");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_SupportsAdditionalBinaryUnicodeProperties()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               /^\p{White_Space}+$/u.test("\u3000") &&
                               /^\p{space}+$/u.test("\u00A0") &&
                               /^\p{Variation_Selector}+$/u.test("\uFE0F") &&
                               /^\p{VS}+$/u.test("\u180F") &&
                               /^\p{Uppercase}+$/u.test("A") &&
                               /^\p{Upper}+$/u.test("\u0130") &&
                               /^\p{Unified_Ideograph}+$/u.test("\u4E00") &&
                               /^\p{UIdeo}+$/u.test("\uFA11") &&
                               /^\p{Terminal_Punctuation}+$/u.test("!") &&
                               /^\p{Term}+$/u.test("\uFF0C") &&
                               /^\p{Soft_Dotted}+$/u.test("i") &&
                               /^\p{SD}+$/u.test("\u012F") &&
                               /^\p{Sentence_Terminal}+$/u.test("?") &&
                               /^\p{STerm}+$/u.test("\u3002");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_SupportsStringPropertiesAndUnicodeSetStringUnion()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               /^\p{Emoji_Keycap_Sequence}+$/v.test("0\uFE0F\u20E3") &&
                               /^\p{Basic_Emoji}+$/v.test("\u231A") &&
                               /^[\q{0|2|4|9\uFE0F\u20E3}\p{Emoji_Keycap_Sequence}]+$/v.test("0\uFE0F\u20E3") &&
                               /^[\q{0|2|4|9\uFE0F\u20E3}\p{Emoji_Keycap_Sequence}]+$/v.test("2");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_SupportsUnicodeSetClassOperations()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               /^[[0-9][0-9]]+$/v.test("7") &&
                               /^[[0-9]\q{9\uFE0F\u20E3}]+$/v.test("9\uFE0F\u20E3") &&
                               /^[[0-9]&&\d]+$/v.test("4") &&
                               !/^[[0-9]&&\d]+$/v.test("A") &&
                               /^[_--\q{0|2|4|9\uFE0F\u20E3}]+$/v.test("_") &&
                               !/^[_--\q{0|2|4|9\uFE0F\u20E3}]+$/v.test("2");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_RejectsNegatedStringProperties()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               try {
                                 /\P{Emoji_Keycap_Sequence}/v;
                                 false;
                               } catch (e) {
                                 e instanceof SyntaxError;
                               }
                               """).IsTrue, Is.True);

        Assert.That(realm.Eval("""
                               try {
                                 /[^\p{Emoji_Keycap_Sequence}]/v;
                                 false;
                               } catch (e) {
                                 e instanceof SyntaxError;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_ThrowsSyntaxErrorForOutOfOrderClassRange()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               try {
                                 new RegExp("[d-G\\a]").exec("a");
                                 false;
                               } catch (e) {
                                 e instanceof SyntaxError && e.message.includes("Range out of order in character class");
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_ThrowsSyntaxErrorForOutOfOrderClassRangeBeforeTrailingText()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               try {
                                 new RegExp("[d-G\\a]7").exec("a");
                                 false;
                               } catch (e) {
                                 e instanceof SyntaxError && e.message.includes("Range out of order in character class");
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpMatchThrowsWhenLastIndexBecomesNotWritableAfterExecLookup()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = /b/g;
                               Object.defineProperty(r, 'exec', {
                                 get() {
                                   Object.defineProperty(r, 'lastIndex', { writable: false });
                                 }
                               });
                               try {
                                 r[Symbol.match]('abc');
                                 false;
                               } catch (e) {
                                 e instanceof TypeError;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpSourceRoundTripsUnicodeCodePointPattern()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const source = /\u{1d306}/u.source;
                               source === "\\u{1d306}" && new RegExp(source, "u").test("𝌆");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpSourceRoundTripsUnicodeSurrogateEscapePattern()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const source = /\ud834\udf06/u.source;
                               source === "\\ud834\\udf06" && new RegExp(source, "u").test("\ud834\udf06") && new RegExp(source, "u").test("𝌆");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpSourcePreservesControlCharactersFromLiteralEval()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const c = String.fromCharCode(1);
                               const a = eval("/" + c + "/").source;
                               const b = eval("/" + "a\\" + c + "/").source;
                               a === c &&
                               a.length === 1 &&
                               a.charCodeAt(0) === 1 &&
                               b === ("a\\" + c) &&
                               b.length === 3 &&
                               b.charCodeAt(0) === 97 &&
                               b.charCodeAt(1) === 92 &&
                               b.charCodeAt(2) === 1;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpSourcePreservesRawLoneSurrogateFromLiteralEval()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const c = String.fromCharCode(0xD800);
                               const source = eval("/" + c + "/").source;
                               source === c && source.length === 1 && source.charCodeAt(0) === 0xD800;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpSourcePreservesLowControlCharactersAcrossEvalLoop()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var summary = realm.Eval("""
                                 const parts = [];
                                 for (let cu = 0; cu <= 3; ++cu) {
                                   const xx = String.fromCharCode(cu);
                                   const a = eval("/" + xx + "/").source;
                                   const b = eval("/" + "\\" + xx + "/").source;
                                   parts.push(
                                     cu + ":" +
                                     JSON.stringify(a) + ":" +
                                     (a === xx) + ":" +
                                     a.length + ":" +
                                     a.charCodeAt(0) + ":" +
                                     JSON.stringify(b) + ":" +
                                     (b === ("\\" + xx)) + ":" +
                                     b.length + ":" +
                                     b.charCodeAt(0) + ":" +
                                     b.charCodeAt(1)
                                   );
                                 }
                                 parts.join("|");
                                 """).AsString();

        Assert.That(summary, Is.EqualTo(
            "0:\"\\u0000\":true:1:0:\"\\\\\\u0000\":true:2:92:0|" +
            "1:\"\\u0001\":true:1:1:\"\\\\\\u0001\":true:2:92:1|" +
            "2:\"\\u0002\":true:1:2:\"\\\\\\u0002\":true:2:92:2|" +
            "3:\"\\u0003\":true:1:3:\"\\\\\\u0003\":true:2:92:3"));
    }

    [Test]
    public void OkojoRegExpEscapeExposesExpectedSurface()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               typeof RegExp.escape === "function"
                                 && RegExp.escape.name === "escape"
                                 && RegExp.escape.length === 1
                                 && (() => {
                                   const d = Object.getOwnPropertyDescriptor(RegExp, "escape");
                                   return d && d.writable === true && d.enumerable === false && d.configurable === true;
                                 })();
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpEscapeEncodesExpectedCharacters()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               RegExp.escape("abc") === "\\x61bc"
                                 && RegExp.escape(".-/ ") === "\\.\\x2d\\/\\x20"
                                 && RegExp.escape("\uFEFF\u2028") === "\\ufeff\\u2028"
                                 && RegExp.escape("\ud834") === "\\ud834"
                                 && RegExp.escape("你好!") === "你好\\x21";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoExternalRegExpEngineSupportsBasicLookbehindCaptures()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const m = "abcdef".match(/(?<=(\w{2}))def/);
                               m && m[0] === "def" && m[1] === "bc";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoExternalRegExpEngineSupportsNegativeLookbehind()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const m = "abcdef".match(/(?<!(^|[ab]))\w{2}/);
                               m && m[0] === "de" && m[1] === undefined;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpMatchIndicesExposeArraysAndNamedGroups()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const m = /(?<x>a)(b)?/d.exec("za");
                               Array.isArray(m.indices)
                                 && Array.isArray(m.indices[0])
                                 && m.indices[0][0] === 1
                                 && m.indices[0][1] === 2
                                 && Array.isArray(m.indices.groups.x)
                                 && m.indices.groups.x[0] === 1
                                 && m.indices.groups.x[1] === 2
                                 && m.indices[2] === undefined;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpMatchIndicesGroupsPropertyIsAlwaysPresent()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const plain = /./d.exec("a").indices;
                               const named = /(?<x>.)/d.exec("a").indices;
                               Object.prototype.hasOwnProperty.call(plain, "groups")
                                 && plain.groups === undefined
                                 && Object.prototype.hasOwnProperty.call(named, "groups")
                                 && Object.getPrototypeOf(named.groups) === null
                                 && named.groups.x[0] === 0
                                 && named.groups.x[1] === 1;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoRegExpFunctionalReplaceReceivesNamedGroups()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               "abcd".replace(/(?<fst>.)(?<snd>.)/g, (match, fst, snd, offset, str, groups) => groups.snd + groups.fst) === "badc";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoExternalRegExpEngineSupportsDuplicateNamedGroupsAcrossAlternatives()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const m = /(?<x>a)|(?<x>b)/.exec("bab");
                               m && m[0] === "b" && m[1] === undefined && m[2] === "b" && m.groups.x === "b";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void OkojoExternalRegExpEngineKeepsLastDuplicateNamedGroupAcrossIterations()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.ToJsStringSlowPath(realm.Eval("""
                                                        const m = /(?:(?:(?<x>a)|(?<x>b))\k<x>){2}/.exec("aabb");
                                                        m === null ? "null" : `${m[0]}|${String(m[1])}|${String(m[2])}|${String(m.groups.x)}`;
                                                        """)), Is.EqualTo("aabb|undefined|b|b"));
    }

    [Test]
    public void OkojoExternalRegExpEngineDuplicateNamedGroupExecSequenceMatchesReferenceCases()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.ToJsStringSlowPath(realm.Eval("""
                                                        (() => {
                                                          function enc(m) {
                                                            return m === null ? "null" : `${m.length}|${m[0]}|${String(m[1])}|${String(m[2])}|${String(m.groups?.x)}`;
                                                          }
                                                          if (enc(/(?<x>a)|(?<x>b)/.exec("bab")) !== "3|b|undefined|b|b") return "s1";
                                                          if (enc(/(?<x>b)|(?<x>a)/.exec("bab")) !== "3|b|b|undefined|b") return "s2";
                                                          if (enc(/(?:(?<x>a)|(?<x>b))\k<x>/.exec("aa")) !== "3|aa|a|undefined|a") return "s3";
                                                          if (enc(/(?:(?<x>a)|(?<x>b))\k<x>/.exec("bb")) !== "3|bb|undefined|b|b") return "s4";
                                                          if (enc(/(?:(?:(?<x>a)|(?<x>b))\k<x>){2}/.exec("aabb")) !== "3|aabb|undefined|b|b") return "s5";
                                                          if (enc(/(?:(?:(?<x>a)|(?<x>b))\k<x>){2}/.exec("abab")) !== "null") return "s6";
                                                          if (enc(/(?:(?<x>a)|(?<x>b))\k<x>/.exec("abab")) !== "null") return "s7";
                                                          if (enc(/(?:(?<x>a)|(?<x>b))\k<x>/.exec("cdef")) !== "null") return "s8";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z)\k<a>$/.exec("xx")) !== "3|xx|x|undefined|undefined") return "s9";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z)\k<a>$/.exec("z")) !== "3|z|undefined|undefined|undefined") return "s10";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z)\k<a>$/.exec("zz")) !== "null") return "s11";
                                                          if (enc(/(?<a>x)|(?:zy\k<a>)/.exec("zy")) !== "2|zy|undefined|undefined|undefined") return "s12";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("xz")) !== "3|xz|undefined|undefined|undefined") return "s13";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("yz")) !== "3|yz|undefined|undefined|undefined") return "s14";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("xzx")) !== "null") return "s15";
                                                          if (enc(/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("yzy")) !== "null") return "s16";
                                                          return "ok";
                                                        })();
                                                        """)), Is.EqualTo("ok"));
    }

    [Test]
    public void ExternalRegExpEngine_ScopedModifiersAffectEscapesAndPropertyClasses()
    {
        var engine = RegExpEngine.Default;

        Assert.That(engine.Exec(engine.Compile(@"(?i:\x61)b", ""), "Ab", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"(?i:\P{Lu})", "u"), "A", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"[\p{Hex}\P{Hex}]", "u"), "\uD834\uDF06", 0), Is.Not.Null);
    }

    [Test]
    public void ExternalRegExpEngine_SupportsCurrentUnicodePropertyFrontier()
    {
        var engine = RegExpEngine.Default;

        Assert.That(engine.Exec(engine.Compile(@"\p{AHex}+", "u"), "A9f", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Hex}+", "u"), "\uff11A", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{ASCII}+", "u"), "Az9", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Assigned}+", "u"), "\uDFFF", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Cased}+", "u"), "\u00AA", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Case_Ignorable}+", "u"), "\u00AD", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Bidi_M}+", "u"), "<>", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Dash}+", "u"), "-", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{CWCM}+", "u"), "\u00B5", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{CWCF}+", "u"), "\u00B5", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{CWL}+", "u"), "\u0130", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{CWT}+", "u"), "\u01C6", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{CWU}+", "u"), "\u00DF", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{CWKCF}+", "u"), "\u00DF", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Dia}+", "u"), "\u005E", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{EComp}+", "u"), "\u200D", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{EMod}+", "u"), "\ud83c\udffb", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{EBase}+", "u"), "\u261D", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Emoji}+", "u"), "\u00A9", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{EPres}+", "u"), "\u231A", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{DI}+", "u"), "\u00AD", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Bidi_C}+", "u"), "\u061C", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Ext}+", "u"), "\u00B7", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Any}+", "u"), "\ud800\udc00", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Alpha}+", "u"), "\u00AA", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Dep}+", "u"), "\u0149", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Gr_Base}+", "u"), "A", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Gr_Ext}+", "u"), "\u0301", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Ideo}+", "u"), "\u3006", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{IDS}+", "u"), "A", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{IDC}+", "u"), "0", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{IDSB}+", "u"), "\u2FF0", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{IDST}+", "u"), "\u2FF2", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Join_C}+", "u"), "\u200C", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{LOE}+", "u"), "\u0E40", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Lower}+", "u"), "\u00AA", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Math}+", "u"), "+", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{NChar}+", "u"), "\uFDD0", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Pat_Syn}+", "u"), "!", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Pat_WS}+", "u"), "\u2028", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{QMark}+", "u"), "\"'", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Radical}+", "u"), "\u2E80", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{RI}+", "u"), "\ud83c\udde6", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{punct}+", "u"), "!", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Combining_Mark}+", "u"), "\u0301", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Script=Adlm}+", "u"), "\ud83a\udd00", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{sc=Cham}+", "u"), "\uaa00", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{scx=Bopo}+", "u"), "\u3030", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{ExtPict}+", "u"), "\u00A9", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{gc=Format}+", "u"), "\u00AD", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Letter}+", "u"), "\u00AA", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Nl}+", "u"), "\u16EE", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Pi}+", "u"), "\u00AB", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\P{Bidi_M}+", "u"), "ABC", 0), Is.Not.Null);
    }

    [Test]
    public void RegExpMatchAllIterator_UsesGenericExecAndPropagatesElementAccess()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const regexp = /./g;
                               const iter = regexp[Symbol.matchAll]("abc");
                               let calls = 0;
                               let args0;
                               RegExp.prototype.exec = function(s) {
                                 calls++;
                                 args0 = s;
                                 return calls === 1 ? ["ab"] : null;
                               };
                               const first = iter.next();
                               const second = iter.next();
                               first.value[0] === "ab" &&
                               first.done === false &&
                               second.value === undefined &&
                               second.done === true &&
                               calls === 2 &&
                               args0 === "abc";
                               """).IsTrue, Is.True);

        Assert.That(() => realm.Eval("""
                                     const iter = /./g[Symbol.matchAll]("");
                                     RegExp.prototype.exec = function() {
                                       return {
                                         get '0'() { throw new Error("boom"); }
                                       };
                                     };
                                     iter.next();
                                     """), Throws.InstanceOf<JsRuntimeException>());

        Assert.That(realm.Eval("""
                               const iter = /./g[Symbol.matchAll]("");
                               let internalRegExp;
                               RegExp.prototype.exec = function() {
                                 internalRegExp = this;
                                 return {
                                   get '0'() {
                                     return { toString() { return ""; } };
                                   }
                                 };
                               };
                               iter.next();
                               const first = internalRegExp.lastIndex;
                               iter.next();
                               first === 1 && internalRegExp.lastIndex === 2;
                               """).IsTrue, Is.True);

        Assert.That(() => realm.Eval("""
                                     const iter = /./g[Symbol.matchAll]("");
                                     RegExp.prototype.exec = function() {
                                       this.lastIndex = { valueOf() { throw new Error("boom"); } };
                                       return [""];
                                     };
                                     iter.next();
                                     """), Throws.InstanceOf<JsRuntimeException>());

        var directMatchAllRealm = JsRuntime.Create().DefaultRealm;
        Assert.That(directMatchAllRealm.Eval("""
                                             const iter = /\w/[Symbol.matchAll]("*a*b");
                                             const first = iter.next();
                                             const second = iter.next();
                                             first.value[0] === "a" && first.done === false && second.value === undefined && second.done === true;
                                             """).IsTrue, Is.True);

        Assert.That(directMatchAllRealm.Eval("""
                                             const proto = Object.getPrototypeOf(/./[Symbol.matchAll](""));
                                             const desc = Object.getOwnPropertyDescriptor(proto, "next");
                                             typeof proto.next === "function" &&
                                             desc.writable === true &&
                                             desc.enumerable === false &&
                                             desc.configurable === true;
                                             """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpSearch_RestoresAbsentLastIndexAndThrowsWhenRestoreFails()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = {
                                 exec() {
                                   Object.defineProperty(this, "lastIndex", {
                                     value: 1,
                                     writable: false,
                                     configurable: true
                                   });
                                   return null;
                                 }
                               };
                               try {
                                 RegExp.prototype[Symbol.search].call(r, "");
                                 false;
                               } catch (e) {
                                 e instanceof TypeError;
                               }
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpReplace_UsesGenericLengthAndOmitsUndefinedGroupsArgument()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = /./g;
                               let calls = 0;
                               let seen;
                               r.exec = function() {
                                 calls++;
                                 if (calls > 1) return null;
                                 this.lastIndex = 1;
                                 return {
                                   0: "foo",
                                   1: "bar",
                                   2: "baz",
                                   length: { valueOf() { return 3.9; } },
                                   index: 0,
                                   groups: undefined
                                 };
                               };
                               const out = "foo".replace(r, function() {
                                 seen = Array.from(arguments);
                                 return "x";
                               });
                               out === "x" &&
                               seen.length === 5 &&
                               seen[0] === "foo" &&
                               seen[1] === "bar" &&
                               seen[2] === "baz" &&
                               seen[3] === 0 &&
                               seen[4] === "foo";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpMatchAll_UsesSpeciesAndGenericRegExpCreatePath()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const re = /./g;
                               const iter = re[Symbol.matchAll]("ab");
                               const first = iter.next();
                               first.value[0] === "a";
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpMatchAndReplace_AdvanceLastIndexAfterEmptyMatch()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const re = /./g;
                               re.exec = function() {
                                 this.lastIndex = { valueOf() { return Math.pow(2, 54); } };
                                 re.exec = function() { return null; };
                                 return { 0: "", length: 1, index: 0 };
                               };
                               re[Symbol.replace]("", "");
                               const replaceOk = re.lastIndex === Math.pow(2, 53);

                               const m = /./g;
                               m.exec = function() {
                                 Object.defineProperty(m, "lastIndex", { writable: false });
                                 return { get 0() { return ""; } };
                               };

                               let matchThrows = false;
                               try {
                                 m[Symbol.match]("");
                               } catch (e) {
                                 matchThrows = e instanceof TypeError;
                               }

                               replaceOk && matchThrows;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_SupportsClassicControlLetterEscapes()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               let ok = true;
                               for (let alpha = 0x41; alpha <= 0x5A; alpha++) {
                                 const str = String.fromCharCode(alpha % 32);
                                 const match = new RegExp("\\c" + String.fromCharCode(alpha)).exec(str);
                                 if (match === null || match[0] !== str) {
                                   ok = false;
                                   break;
                                 }
                               }
                               ok;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpPrototypeSourceAndCallBehaviorMatchSpecSurface()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const sourceGet = Object.getOwnPropertyDescriptor(RegExp.prototype, "source").get;
                               const sourceOk = sourceGet.call(RegExp.prototype) === "(?:)";

                               const re = /(?:)/;
                               re[Symbol.match] = false;
                               const callOk = RegExp(re) !== re;

                               sourceOk && callOk;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_RejectsInvalidRepeatedQuantifiersAndSupportsClassBackspace()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               let syntaxOk = true;
                               for (const pattern of ["a????", "*a", "x{1}{1,}", "??"]) {
                                 try {
                                   new RegExp(pattern);
                                   syntaxOk = false;
                                   break;
                                 } catch (e) {
                                   if (!(e instanceof SyntaxError)) {
                                     syntaxOk = false;
                                     break;
                                   }
                                 }
                               }

                               syntaxOk &&
                               /[\b]/u.test("\u0008") &&
                               /[\b-A]/u.test("A");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_TreatsHyphenAfterClassEscapeAsLiteral()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               /^[a-z$_][\w-$]*$/i.test("a-b$") &&
                               /[\w-$]/.test("-") &&
                               /[\w-$]/.test("$") &&
                               /[^\0-\x1f\x7f-\uFFFF\w-]/.test("!");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpReplace_ThrowsForNullGroupsAndExec_ResetsLargeLastIndex()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const r = /./;
                               r.exec = () => ({ length: 1, 0: "", index: 0, groups: null });

                               let replaceThrows = false;
                               try {
                                 r[Symbol.replace]("bar", "");
                               } catch (e) {
                                 replaceThrows = e instanceof TypeError;
                               }

                               const g = /./g;
                               g.lastIndex = 2 ** 32 + 4;
                               const execResult = g.exec("test");

                               replaceThrows && execResult === null && g.lastIndex === 0;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void ExternalRegExpEngine_RejectsTrailingBackslashAndHonorsIgnoreCaseRangeAndUndefinedMatch()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               let syntaxOk = false;
                               try {
                                 new RegExp("\\");
                               } catch (e) {
                                 syntaxOk = e instanceof SyntaxError;
                               }

                               const rangeOk = /[a-z]+/ig.exec("ABC def ghi").index === 0;

                               let args;
                               const r = /./;
                               r.exec = () => [];
                               r[Symbol.replace]("foo", function() { args = arguments; });
                               const replaceOk = args.length === 3 && args[0] === "undefined" && args[1] === 0 && args[2] === "foo";

                               syntaxOk && rangeOk && replaceOk;
                               """).IsTrue, Is.True);
    }
}

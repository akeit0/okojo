using Okojo.Runtime;

namespace Okojo.Tests;

[Parallelizable(ParallelScope.All)]
public class StringPrototypeTests
{
    [Test]
    public void String_Constructor_Follows_Symbol_Function_Vs_Construct_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let asFunction = String(Symbol("x"));
                   let ctorTypeError = false;
                   try {
                     new String(Symbol("x"));
                   } catch (e) {
                     ctorTypeError = e && e.name === "TypeError";
                   }
                   [asFunction, ctorTypeError].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("Symbol(x)|true"));
    }

    [Test]
    public void String_FromCodePoint_And_Raw_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let rangeError = false;
                   try { String.fromCodePoint(0x110000); } catch (e) { rangeError = e && e.name === "RangeError"; }
                   [
                     String.fromCodePoint(0x1F600),
                     String.raw({ raw: ["a", "b", "c"] }, 1, 2),
                     rangeError
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("😀|a1b2c|true"));
    }

    [Test]
    public void String_Basic_Generic_Methods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let regexRejects = false;
                   try { "abc".startsWith(/a/); } catch (e) { regexRejects = e && e.name === "TypeError"; }
                   [
                     String.prototype.charAt.call(42, 0),
                     String.prototype.charCodeAt.call(42, 1),
                     String.prototype.codePointAt.call("😀", 0),
                     "\uD800\uE000".codePointAt(0),
                     String.prototype.concat.call("a", "b", 3),
                     "abc".endsWith("", -1),
                     "abc".includes("b", 1),
                      "abcabc".lastIndexOf("b"),
                      "abc".substring(2, 1),
                      "abcdef".substr(2, 3),
                      "abcdef".substr(-2),
                      "abcdef".substr(10, 2),
                      "ABC".toLowerCase(),
                      "abc".toUpperCase(),
                      "  x  ".trim(),
                      "  x  ".trimStart(),
                      "  x  ".trimEnd(),
                     "abc".at(-1),
                     regexRejects
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("4|50|128512|55296|ab3|true|true|4|b|cde|ef||abc|ABC|x|x  |  x|c|true"));
    }

    [Test]
    public void String_WellFormed_Methods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let leading = "\uD83D";
                   let trailing = "\uDCA9";
                   let replacement = "\uFFFD";
                   [
                     ("a" + leading + "c").isWellFormed(),
                     ("a" + leading + "c").toWellFormed(),
                     ("a\uD83D\uDCA9c").isWellFormed(),
                     ("a" + trailing + "c").toWellFormed() === ("a" + replacement + "c")
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("false|a�c|true|true"));
    }

    [Test]
    public void String_Padding_And_Repeat_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let rangeError = false;
                   try { "a".repeat(-1); } catch (e) { rangeError = e && e.name === "RangeError"; }
                   [
                     "x".padStart(4, "ab"),
                     "x".padEnd(4, "ab"),
                     "x".padStart(1, "ab"),
                     String.prototype.padEnd.call(7, 3, 0),
                     "ab".repeat(3),
                     rangeError
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("abax|xaba|x|700|ababab|true"));
    }

    [Test]
    public void String_Replace_And_ReplaceAll_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let search = {};
                   search[Symbol.replace] = function(value, replacement) {
                     return String(value) + "|" + replacement;
                   };
                   [
                     "abc".replace("b", "$$-$&-$`-$'"),
                     "abc".replaceAll("b", "$$-$&-$`-$'"),
                     "xxx".replace("", "_"),
                     "xxx".replaceAll("", "_"),
                     "aba".replaceAll("a", function(match, index, text) { return index + ":" + text.length; }),
                     "abc".replace(search, "ok")
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("a$-b-a-cc|a$-b-a-cc|_xxx|_x_x_x_|0:3b2:3|abc|ok"));
    }

    [Test]
    public void String_LocaleCase_Methods_Coerce_Generically()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let obj = {
                     toString() { return {}; },
                     valueOf() { return 1; }
                   };
                   [
                     eval('"BJ"').toLocaleLowerCase(),
                     String.prototype.toLocaleUpperCase.call(obj)
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("bj|1"));
    }

    [Test]
    public void String_UpperCase_Uses_Unicode_Special_Casing_And_Supplementary_Mapping()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   [
                     "ß".toUpperCase(),
                     "ﬃ".toLocaleUpperCase(),
                     "\uD801\uDC28".toUpperCase()
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("SS|FFI|𐐀"));
    }

    [Test]
    public void String_LowerCase_Uses_Final_Sigma_Special_Casing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   [
                     "A\u180E\u03A3".toLocaleLowerCase(),
                     "A\u03A3\u180EB".toLocaleLowerCase(),
                     "\uD835\uDCA2\u03A3".toLowerCase(),
                     "\u0130".toLowerCase()
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a᠎ς|aσ᠎b|𝒢ς|i̇"));
    }

    [Test]
    public void String_ToLocaleCase_Uses_Turkic_And_Lithuanian_SpecialCasing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   [
                     "i".toLocaleUpperCase("tr"),
                     "\u0131".toLocaleUpperCase("az"),
                     "I\u0307".toLocaleLowerCase("tr"),
                     "I\u0323\u0307".toLocaleLowerCase("az"),
                     "i\u0307".toLocaleUpperCase("lt"),
                     "I\u0307".toLocaleUpperCase("lt"),
                     "\u00CC".toLocaleLowerCase("lt")
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("İ|I|i|i\u0323|I|I\u0307|i\u0307\u0300"));
    }

    [Test]
    public void String_LocaleCompare_Delegates_To_Intl_Collator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let localeTypeError = false;
                                let optionRangeError = false;
                                try { "".localeCompare("", null); } catch (e) { localeTypeError = e && e.name === "TypeError"; }
                                try { "".localeCompare("", [], { usage: "invalid" }); } catch (e) { optionRangeError = e && e.name === "RangeError"; }
                                const collatorSorted = ["d", "oe", "ö", "of"].slice().sort(new Intl.Collator(["de-u-co-phonebk"], { usage: "search" }).compare).join(",");
                                const localeSorted = ["d", "oe", "ö", "of"].slice().sort((a, b) => a.localeCompare(b, ["de-u-co-phonebk"], { usage: "search" })).join(",");
                                [localeTypeError, optionRangeError, collatorSorted === localeSorted].join("|");
                                """);
        Assert.That(result.AsString(), Is.EqualTo("true|true|true"));
    }

    [Test]
    public void String_ToLocaleCase_Validates_Locale_Identifiers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let lowerThrows = false;
                   let upperThrows = false;
                   try { "".toLocaleLowerCase("x-private"); } catch (e) { lowerThrows = e && e.name === "RangeError"; }
                   try { "".toLocaleUpperCase(["en", 1]); } catch (e) { upperThrows = e && e.name === "TypeError"; }
                   [lowerThrows, upperThrows].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true"));
    }

    [Test]
    public void String_Trim_Uses_Ecma_Whitespace_Table()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   [
                     "\uFEFFabc".trim(),
                     "abc\uFEFF".trim(),
                     "\u180Eabc\u180E".trim()
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("abc|abc|᠎abc᠎"));
    }

    [Test]
    public void String_Split_Basic_And_Argument_Order_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let limitError = false;
                   let sepError = false;
                   let limitBeforeSep = false;
                   try {
                     "".split("", { toString() {}, valueOf() { throw new Error("limit"); } });
                   } catch (e) {
                     limitError = e && e.message === "limit";
                   }
                   try {
                     "foo".split({ toString() { throw new Error("sep"); } }, 0);
                   } catch (e) {
                     sepError = e && e.message === "sep";
                   }
                   try {
                     String.prototype.split.call(new Number(10001.10001), {
                       toString() { throw new Error("sep2"); }
                     }, {
                       valueOf() { throw new Error("limit2"); }
                     });
                   } catch (e) {
                     limitBeforeSep = e && e.message === "limit2";
                   }
                   [
                     "abc".split("").join(","),
                     "abc".split("", 2).join(","),
                     "abc".split(undefined).join(","),
                     "abc".split("b").join(","),
                     "".split("").length,
                     limitError,
                     sepError,
                     limitBeforeSep
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a,b,c|a,b|abc|a,c|0|true|true|true"));
    }

    [Test]
    public void String_Normalize_Works_And_Validates_Form()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let rangeError = false;
                   try { "".normalize("BAD"); } catch (e) { rangeError = e && e.name === "RangeError"; }
                   [
                     "\u1E9B\u0323".normalize("NFC"),
                     "\u1E9B\u0323".normalize("NFD"),
                     "\u1E9B\u0323".normalize("NFKC"),
                     "\u1E9B\u0323".normalize("NFKD"),
                     String.prototype.normalize.call(5),
                     rangeError
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ẛ̣|ẛ̣|ṩ|ṩ|5|true"));
    }

    [Test]
    public void String_LocaleCompare_Is_Generic_And_Honors_Canonical_Equivalence()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let obj = {
                     toString() { return "\u212B"; }
                   };
                   let canonical = String.prototype.localeCompare.call(obj, "A\u030A");
                   let ordering = "b".localeCompare("a") > 0;
                   [canonical === 0, ordering].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true"));
    }

    [Test]
    public void String_StartsWith_Observes_IsRegExp_Abrupt_And_Split_Supports_Basic_RegExp_Separator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let abrupt = false;
                   let obj = {};
                   Object.defineProperty(obj, Symbol.match, {
                     get() { throw new Error("match"); }
                   });
                   try {
                     "".startsWith(obj);
                   } catch (e) {
                     abrupt = e && e.message === "match";
                   }

                   let split1 = new String("one-1 two-2 three-3").split(new RegExp);
                   let split2 = new String("hello").split(/l/, 2);
                   [
                     abrupt,
                     split1.length,
                     split1[0],
                     split1[18],
                     split2.length,
                     split2[0],
                     split2[1]
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|19|o|3|2|he|"));
    }

    [Test]
    public void String_Match_Global_And_MatchAll_Primitive_Path_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let s = "Boston, MA 02134";
                   let r = /([\d]{5})([-\ ]?[\d]{4})?$/g;
                   r.lastIndex = s.lastIndexOf("0");
                   let matched = s.match(r);

                   Object.defineProperty(Number.prototype, Symbol.matchAll, {
                     get() { throw new Error("nope"); }
                   });
                   let fromAll = Array.from("a1b1c".matchAll(1));

                   [
                     matched.length,
                     matched[0],
                     fromAll.length,
                     fromAll[0][0],
                     fromAll[0].index,
                     fromAll[1][0],
                     fromAll[1].index
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|02134|2|1|1|1|3"));
    }

    [Test]
    public void
        String_Match_Uses_BuiltIn_SymbolMatch_On_Internally_Created_RegExp_And_MatchAll_Undefined_Uses_Empty_RegExp()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let original = RegExp.prototype[Symbol.match];
                   let thisVal;
                   let arg;
                   let ret = {};
                   let out1;
                   RegExp.prototype[Symbol.match] = function(value) {
                     thisVal = this;
                     arg = value;
                     return ret;
                   };
                   try {
                     let result = 'target'.match('string source');
                     out1 = [result === ret, thisVal instanceof RegExp, thisVal.source, thisVal.flags, thisVal.lastIndex, arg].join("|");
                   } finally {
                     RegExp.prototype[Symbol.match] = original;
                   }

                   let matches = Array.from('a'.matchAll(undefined));
                   out1 + "|" + matches.length + "|" + matches[0][0] + "|" + matches[0].index + "|" + matches[1].index;
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|string source||0|target|2||0|1"));
    }

    [Test]
    public void String_Split_Uses_SymbolSplit_Before_Receiver_ToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let receiver = {
                     toString() { throw new Error("receiver"); }
                   };
                   let splitter = {
                     [Symbol.split](value, limit) { return [typeof value, typeof limit]; }
                   };
                   let result = String.prototype.split.call(receiver, splitter, Symbol());
                   [result[0], result[1]].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("object|symbol"));
    }

    [Test]
    public void String_MatchAll_Fallback_Stringifies_Receiver_Before_RegExpMatchAll()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let count = 0;
                   let arg;
                   let receiver = {
                     [Symbol.toPrimitive]() {
                       count++;
                       return "abc";
                     }
                   };
                   RegExp.prototype[Symbol.matchAll] = function(string) {
                     arg = string;
                     return [][Symbol.iterator]();
                   };
                   String.prototype.matchAll.call(receiver, null);
                   String.prototype.matchAll.call(receiver, undefined);
                   [count, arg].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2|abc"));
    }

    [Test]
    public void String_MatchAll_Checks_RegExp_Global_Before_MatchAll_And_Uses_Custom_Object_Matcher()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let nonGlobalThrows = false;
                                let objectMatcherOk = false;
                                let regexp = /./;
                                Object.defineProperty(regexp, Symbol.matchAll, {
                                  get() { throw new Error("matchAll-get"); }
                                });
                                try { "".matchAll(regexp); } catch (e) { nonGlobalThrows = e instanceof TypeError; }

                                let received;
                                let custom = {
                                  flags: "",
                                  [Symbol.matchAll](value) {
                                    received = value;
                                    return [][Symbol.iterator]();
                                  }
                                };
                                String.prototype.matchAll.call({ [Symbol.toPrimitive]() { return "abc"; } }, custom);
                                objectMatcherOk = received === "abc";

                                nonGlobalThrows && objectMatcherOk;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void String_ToString_And_ValueOf_Throw_TypeError_From_Current_Function_Realm()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherStringPrototype"] = JsValue.FromObject(otherRealm.StringPrototype);
        realm.Global["OtherTypeError"] = JsValue.FromObject(otherRealm.TypeErrorConstructor);

        var result = realm.Eval("""
                                var otherToString = OtherStringPrototype.toString;
                                var otherValueOf = OtherStringPrototype.valueOf;
                                var ok = false;
                                try { otherToString.call(true); } catch (e) { ok = e && e.constructor === OtherTypeError; }
                                try { otherValueOf.call({}); } catch (e) { ok = ok && e && e.constructor === OtherTypeError; }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void String_Search_Uses_SymbolSearch_And_Split_Handles_Regex_Start_Anchor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let abrupt = false;
                   let poisoned = {};
                   Object.defineProperty(poisoned, Symbol.search, {
                     get() { throw new Error("search"); }
                   });
                   try {
                     "".search(poisoned);
                   } catch (e) {
                     abrupt = e && e.message === "search";
                   }

                   let split = "x".split(/^/);
                   [abrupt, split.length, split[0]].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|1|x"));
    }

    [Test]
    public void String_Split_Allows_NonUnicode_IdentityEscapes_That_Look_Special_In_DotNet()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let splitK = "x".split(/\k<x>/);
                   let splitMalformedX = "x".split(/\x/);
                   let splitX = "x".split(/\X/);
                   let splitXA0 = "x".split(/\XA0/);
                   [
                     splitK.length, splitK[0],
                     splitMalformedX.length, splitMalformedX[0], splitMalformedX[1],
                     splitX.length, splitX[0],
                     splitXA0.length, splitXA0[0]
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|x|2|||1|x|1|x"));
    }

    [Test]
    public void String_Search_Invokes_BuiltIn_SymbolSearch_On_Internally_Created_RegExp()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let target = new String("target");
                   target[Symbol.search] = undefined;
                   let original = RegExp.prototype[Symbol.search];
                   let thisVal;
                   let arg;
                   let ret = {};
                   let output;
                   RegExp.prototype[Symbol.search] = function(value) {
                     thisVal = this;
                     arg = value;
                     return ret;
                   };
                   try {
                     let result = target.search("string source");
                     output = [result === ret, thisVal instanceof RegExp, thisVal.source, thisVal.flags, arg].join("|");
                   } finally {
                     RegExp.prototype[Symbol.search] = original;
                   }
                   output;
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|string source||target"));
    }

    [Test]
    public void String_Replace_Uses_RegExp_Replace_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   'She sells seashells by the seashore.'.replace(/sh/, 'sch');
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("She sells seaschells by the seashore."));
    }

    [Test]
    public void String_Replace_RegExp_Functional_Replacer_Receives_Captures()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let text = "abc12 def34";
                   let pattern = /([a-z]+)([0-9]+)/g;
                   text.replace(pattern, function() {
                     return arguments[2] + arguments[1];
                   });
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("12abc 34def"));
    }

    [Test]
    public void String_Replace_RegExp_String_Replacer_Uses_Numeric_Capture_Substitution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let str = 'foo-x-bar';
                   [
                     str.replace(/(x)/, '|$1|'),
                     str.replace(/(x)/, '|$01|'),
                     str.replace(/(x)/, '|$10|'),
                     str.replace(/(x)($^)?/, '|$2|'),
                     'uid=31'.replace(/(uid=)(\d+)/, '$11' + '15')
                   ].join('|');
                   """);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("foo-|x|-bar|foo-|x|-bar|foo-|x0|-bar|foo-||-bar|uid=115"));
    }

    [Test]
    public void String_Replace_RegExp_String_Replacer_Uses_Named_Capture_Substitution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   'abcba'.replace(/(?<named>b)/g, '($<named>)');
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a(b)c(b)a"));
    }

    [Test]
    public void String_Trim_On_RegExp_Object_Uses_RegExp_Source_Not_Slashed_Pattern_Stringification()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   String.prototype.trim.call(new RegExp(/test/));
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("/test/"));
    }

    [Test]
    public void String_Match_Exposes_Duplicate_Named_Groups_In_Source_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   const matcher = /(?:(?<x>a)|(?<y>a)(?<x>b))(?:(?<z>c)|(?<z>d))/;
                   const three = "abc".match(matcher);
                   const two = "ad".match(matcher);
                   [
                     String(!!three),
                     three && three.groups ? String(three.groups.x) : "missing",
                     three && three.groups ? String(three.groups.y) : "missing",
                     three && three.groups ? String(three.groups.z) : "missing",
                     three && three.groups ? Object.keys(three.groups).join(",") : "missing",
                     String(!!two),
                     two && two.groups ? String(two.groups.x) : "missing",
                     two && two.groups ? String(two.groups.y) : "missing",
                     two && two.groups ? String(two.groups.z) : "missing",
                     two && two.groups ? Object.keys(two.groups).join(",") : "missing"
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|b|a|c|x,y,z|true|a|undefined|d|x,y,z"));
    }

    [Test]
    public void RegExp_Flags_Property_Can_Be_Redefined_On_Instance()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let rx = /a/g;
                   let before = Object.getOwnPropertyDescriptor(rx, "flags");
                   let outcome;
                   try {
                     Object.defineProperty(rx, "flags", { value: undefined });
                     let after = Object.getOwnPropertyDescriptor(rx, "flags");
                     outcome = [
                       String(before && before.configurable),
                       String(before && before.writable),
                       String(after && after.configurable),
                       String(after && after.writable),
                       String(after && after.value)
                     ].join("|");
                   } catch (e) {
                     outcome = "throw|" + e.name + "|" + e.message;
                   }
                   outcome;
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("undefined|undefined|false|false|undefined"));
    }

    [Test]
    public void RegExp_Stringification_Uses_Canonical_Flag_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   [
                     String(/./iyg),
                     /./iyg.flags
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("/./giy|giy"));
    }

    [Test]
    public void RegExp_ToString_Uses_Overridden_Instance_Source_And_Flags()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let rx = /a/g;
                   Object.defineProperty(rx, "source", { value: "b", configurable: true });
                   Object.defineProperty(rx, "flags", { value: undefined, configurable: true });
                   String(rx);
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("/b/undefined"));
    }
}

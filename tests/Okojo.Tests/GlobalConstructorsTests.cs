using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class GlobalConstructorsTests
{
    [Test]
    public void Global_Array_And_Function_Constructors_AreDefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof Array === "function" && typeof Function === "function";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectConstructor_Has_Own_Length_And_Name_In_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var names = Object.getOwnPropertyNames(Object);
                                                                   Object.prototype.hasOwnProperty.call(Object, "length") &&
                                                                   Object.length === 1 &&
                                                                   names.indexOf("length") >= 0 &&
                                                                   names.indexOf("name") === names.indexOf("length") + 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayConstructor_CallAndConstruct_BasicSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const a = Array(1, 2, 3);
                                                                   const b = new Array(4);
                                                                   (a.length === 3) &&
                                                                   (a[0] === 1) &&
                                                                   (a[2] === 3) &&
                                                                   (b.length === 4) &&
                                                                   (b[0] === undefined);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayFrom_Consumes_MapIterator_And_Preserves_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const map = Map.groupBy([1, 2, 3], function (i) {
                                                                     return i % 2 === 0 ? "even" : "odd";
                                                                   });
                                                                   const keys = Array.from(map.keys());
                                                                   keys.length === 2 && keys[0] === "odd" && keys[1] === "even";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayFrom_Supports_Optional_MapFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const values = Array.from("ab", function (ch, index) {
                                                                     return ch + index;
                                                                   });
                                                                   values.length === 2 && values[0] === "a0" && values[1] === "b1";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Reviver_Receives_Source_Context_For_BigInt_RoundTrip()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const tooBigForNumber = BigInt(Number.MAX_SAFE_INTEGER) + 2n;
                                                                   const intToBigInt = (key, val, { source }) =>
                                                                     typeof val === 'number' && val % 1 === 0 ? BigInt(source) : val;
                                                                   const roundTripped = JSON.parse(String(tooBigForNumber), intToBigInt);
                                                                   roundTripped === tooBigForNumber;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Replacer_Can_Return_JsonRawJson_For_BigInt()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const tooBigForNumber = BigInt(Number.MAX_SAFE_INTEGER) + 2n;
                                                                   const bigIntToRawJSON = (key, val) =>
                                                                     typeof val === 'bigint' ? JSON.rawJSON(val) : val;
                                                                   JSON.stringify({ tooBigForNumber }, bigIntToRawJSON) ===
                                                                     '{"tooBigForNumber":9007199254740993}';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Functions_Are_Omitted_Like_JavaScript()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   JSON.stringify(function(){}) === undefined &&
                                                                   JSON.stringify([function(){}]) === "[null]" &&
                                                                   JSON.stringify({ key: function(){} }) === "{}";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Replacer_Wrong_Object_Type_Is_Ignored()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = { key: [1] };
                                                                   var json = '{"key":[1]}';
                                                                   JSON.stringify(obj, {}) === json &&
                                                                   JSON.stringify(obj, new String('str')) === json &&
                                                                   JSON.stringify(obj, new Number(6.1)) === json;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_NonCallable_ToJson_Does_Not_Alter_Ordinary_Object_Serialization()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   JSON.stringify({toJSON: null}) === '{"toJSON":null}' &&
                                                                   JSON.stringify({toJSON: false}) === '{"toJSON":false}' &&
                                                                   JSON.stringify({toJSON: []}) === '{"toJSON":[]}' &&
                                                                   JSON.stringify({toJSON: /re/}) === '{"toJSON":{}}';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ParseInt_Applies_ToInt32_To_Radix()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   parseInt("11", 4294967298) === parseInt("11", 2) &&
                                                                   parseInt("11", 4294967296) === parseInt("11", 10) &&
                                                                   Number.isNaN(parseInt("11", -2147483650)) &&
                                                                   parseInt("11", -4294967294) === parseInt("11", 2);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ParseFloat_Uses_Ascii_Decimal_Scan()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   parseFloat("0.1e1\u0000") === 1 &&
                                                                   parseFloat("0.1e1\u0660") === 1 &&
                                                                   parseFloat("0.1e1\u0669") === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Space_StringObject_Uses_ToString_And_Gap()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = { a1: { b1: [1, 2] }, a2: 'a2' };
                                                                   var str = new String('xxx');
                                                                   str.toString = function() { return '--'; };
                                                                   str.valueOf = function() { throw new Error('should not call valueOf'); };
                                                                   JSON.stringify(obj, null, str) === [
                                                                     '{',
                                                                     '--"a1": {',
                                                                     '----"b1": [',
                                                                     '------1,',
                                                                     '------2',
                                                                     '----]',
                                                                     '--},',
                                                                     '--"a2": "a2"',
                                                                     '}'
                                                                   ].join('\n');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Handles_Proxy_Array_And_Object_As_Ordinary_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var arrayProxy = new Proxy([], {
                                                                     get: function(_target, key) {
                                                                       if (key === 'length') return 2;
                                                                       return Number(key);
                                                                     }
                                                                   });
                                                                   var objectProxy = new Proxy({}, {
                                                                     getOwnPropertyDescriptor: function() {
                                                                       return { value: 1, writable: true, enumerable: true, configurable: true };
                                                                     },
                                                                     get: function() {
                                                                       return 1;
                                                                     },
                                                                     ownKeys: function() {
                                                                       return ['a', 'b'];
                                                                     }
                                                                   });
                                                                   JSON.stringify(arrayProxy) === '[0,1]' &&
                                                                   JSON.stringify(objectProxy) === '{"a":1,"b":1}';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Handles_Proxy_Of_Proxy_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var objectProxy = new Proxy({}, {
                                                                     getOwnPropertyDescriptor: function() {
                                                                       return { value: 1, writable: true, enumerable: true, configurable: true };
                                                                     },
                                                                     get: function() { return 1; },
                                                                     ownKeys: function() { return ['a', 'b']; }
                                                                   });
                                                                   var objectProxyProxy = new Proxy(objectProxy, {});
                                                                   JSON.stringify({ l1: { l2: objectProxyProxy } }) === '{"l1":{"l2":{"a":1,"b":1}}}';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonBuiltins_Have_Expected_Descriptors_And_Lengths()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var stringify = Object.getOwnPropertyDescriptor(JSON, 'stringify');
                                                                   var parse = Object.getOwnPropertyDescriptor(JSON, 'parse');
                                                                   var rawJSON = Object.getOwnPropertyDescriptor(JSON, 'rawJSON');
                                                                   var isRawJSON = Object.getOwnPropertyDescriptor(JSON, 'isRawJSON');
                                                                   stringify.value.length === 3 &&
                                                                   stringify.writable === true &&
                                                                   stringify.enumerable === false &&
                                                                   stringify.configurable === true &&
                                                                   parse.value.length === 2 &&
                                                                   parse.writable === true &&
                                                                   parse.enumerable === false &&
                                                                   parse.configurable === true &&
                                                                   rawJSON.value.length === 1 &&
                                                                   rawJSON.writable === true &&
                                                                   rawJSON.enumerable === false &&
                                                                   rawJSON.configurable === true &&
                                                                   isRawJSON.value.length === 1 &&
                                                                   isRawJSON.writable === true &&
                                                                   isRawJSON.enumerable === false &&
                                                                   isRawJSON.configurable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonRawJson_Validates_Input_And_Exposes_RawJson_Brand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function kind(fn) {
                                                                     try { fn(); return 'ok'; } catch (e) { return e.name; }
                                                                   }
                                                                   var raw = JSON.rawJSON(1);
                                                                   Object.getPrototypeOf(raw) === null &&
                                                                   Object.getOwnPropertyNames(raw).join(',') === 'rawJSON' &&
                                                                   raw.rawJSON === '1' &&
                                                                   JSON.isRawJSON(raw) === true &&
                                                                   JSON.isRawJSON({ rawJSON: '1' }) === false &&
                                                                   kind(function() { JSON.rawJSON(Symbol('x')); }) === 'TypeError' &&
                                                                   kind(function() { JSON.rawJSON(''); }) === 'SyntaxError' &&
                                                                   kind(function() { JSON.rawJSON(' 1'); }) === 'SyntaxError' &&
                                                                   kind(function() { JSON.rawJSON('1 '); }) === 'SyntaxError' &&
                                                                   kind(function() { JSON.rawJSON('{}'); }) === 'SyntaxError' &&
                                                                   kind(function() { JSON.rawJSON('[]'); }) === 'SyntaxError';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Replacer_Array_Can_Filter_All_Object_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   JSON.stringify({ a: 1, b: 2 }, []) === '{}' &&
                                                                   JSON.stringify({ undefined: 1 }, [undefined]) === '{}' &&
                                                                   JSON.stringify({ key: 1, undefined: 2 }, [,,,]) === '{}' &&
                                                                   JSON.stringify([1, { a: 2 }], []) === '[1,{}]';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Escapes_Lone_Surrogates_But_Preserves_Pairs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   JSON.stringify("\uD834") === '"\\ud834"' &&
                                                                   JSON.stringify("\uDF06") === '"\\udf06"' &&
                                                                   JSON.stringify("\uD834\uDF06") === '"𝌆"' &&
                                                                   JSON.stringify("\uD834\uD834\uDF06\uDF06") === '"\\ud834𝌆\\udf06"';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Replacer_Array_Uses_Observable_ToString_For_Boxed_Strings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var str = new String('str');
                                                                   str.toString = function() { return 'toString'; };
                                                                   str.valueOf = function() { throw new Error('should not call valueOf'); };
                                                                   var value = { str: 1, toString: 2, valueOf: 3 };
                                                                   JSON.stringify(value, [str]) === '{"toString":2}';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Replacer_Function_Sees_Deleted_Property_As_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = {
                                                                     get a() {
                                                                       delete this.b;
                                                                       return 1;
                                                                     },
                                                                     b: 2,
                                                                   };
                                                                   var replacer = function(key, value) {
                                                                     if (key === 'b') {
                                                                       return value === undefined ? '<replaced>' : '<wrong>';
                                                                     }
                                                                     return value;
                                                                   };
                                                                   JSON.stringify(obj, replacer) === '{"a":1,"b":"<replaced>"}';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Invalid_Text_Throws_SyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function errorName(fn) {
                                                                     try { fn(); return 'ok'; } catch (e) { return e.name; }
                                                                   }
                                                                   errorName(function() { JSON.parse(); }) === 'SyntaxError' &&
                                                                   errorName(function() { JSON.parse('01'); }) === 'SyntaxError' &&
                                                                   errorName(function() { JSON.parse(undefined); }) === 'SyntaxError';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Preserves_Negative_Zero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Object.is(JSON.parse('-0'), -0) &&
                                                                   Object.is(JSON.parse(' \n-0'), -0) &&
                                                                   Object.is(JSON.parse('-0  \t'), -0) &&
                                                                   Object.is(JSON.parse(-0), 0);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGetOwnPropertySymbols_EmptyObject_Returns_Empty_Array()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Object.getOwnPropertySymbols({}).length === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGetOwnPropertySymbols_Returns_Own_Symbol_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const s = Symbol("x");
                                                                   const o = {};
                                                                   o[s] = 1;
                                                                   const keys = Object.getOwnPropertySymbols(o);
                                                                   keys.length === 1 && keys[0] === s;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGroupBy_Groups_By_Callback_Key_And_Returns_NullPrototype_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const sym = Symbol("s");
                                                                   const values = [1, 2, 3, 4];
                                                                   const seen = [];
                                                                   const grouped = Object.groupBy(values, function (value, index) {
                                                                     seen.push(String(value) + ":" + String(index));
                                                                     if (value === 4) return sym;
                                                                     return value % 2 === 0 ? "even" : "odd";
                                                                   });

                                                                   Object.getPrototypeOf(grouped) === null &&
                                                                   Object.keys(grouped).join("|") === "odd|even" &&
                                                                   grouped.odd.length === 2 &&
                                                                   grouped.odd[0] === 1 &&
                                                                   grouped.odd[1] === 3 &&
                                                                   grouped.even.length === 1 &&
                                                                   grouped.even[0] === 2 &&
                                                                   Object.getOwnPropertySymbols(grouped).length === 1 &&
                                                                   Object.getOwnPropertySymbols(grouped)[0] === sym &&
                                                                   grouped[sym].length === 1 &&
                                                                   grouped[sym][0] === 4 &&
                                                                   seen.join("|") === "1:0|2:1|3:2|4:3";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGroupBy_String_Uses_Whole_CodePoints()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const grouped = Object.groupBy("🥰💩🙏😈", function (ch) {
                                                                     return ch < "🙏" ? "before" : "after";
                                                                   });

                                                                   Object.getPrototypeOf(grouped) === null &&
                                                                   Object.keys(grouped).join("|") === "after|before" &&
                                                                   grouped.before.length === 2 &&
                                                                   grouped.before[0] === "💩" &&
                                                                   grouped.before[1] === "😈" &&
                                                                   grouped.after.length === 2 &&
                                                                   grouped.after[0] === "🥰" &&
                                                                   grouped.after[1] === "🙏";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayLiteral_Uses_ArrayPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Object.getPrototypeOf([1]) === Array.prototype;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Array_ToString_AndPlusEmptyString_UseJoinLikeNode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const a = new Array(2, 4, 8, 16, 32);
                                                                   (a.toString() === "2,4,8,16,32") && ((a + "") === "2,4,8,16,32");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionConstructor_CallAndConstruct_ReturnCallable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const f = Function("x", "return x + 1;");
                                                                   const g = new Function("return 7;");
                                                                   (f(2) === 3) &&
                                                                   (g() === 7) &&
                                                                   (Function.prototype.constructor === Function);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionConstructor_ReturnThis_BindsToGlobalObjectInSloppyMode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Function("return this;")() === globalThis;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionConstructor_InvalidBody_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                try {
                                  Function.call(this, "var #x = 1;");
                                } catch (e) {
                                  ok = e instanceof SyntaxError;
                                }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionConstructor_PrivateIdentifierBody_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                try {
                                  Function("o.#f");
                                } catch (e) {
                                  ok = e instanceof SyntaxError;
                                }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void CrossRealmDynamicFunction_Call_UsesCalleeRealmForSloppyThis()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherFunction"] = JsValue.FromObject(otherRealm.FunctionConstructor);
        realm.Global["OtherGlobalThis"] = otherRealm.Eval("globalThis");
        realm.Global["OtherBoolean"] = JsValue.FromObject(otherRealm.BooleanConstructor);
        realm.Global["OtherNumber"] = JsValue.FromObject(otherRealm.NumberConstructor);
        realm.Global["OtherString"] = JsValue.FromObject(otherRealm.StringConstructor);
        realm.Eval("""
                   var func = new OtherFunction("return this;");
                   var implicitThis = func();
                   var explicitUndef = func.call(undefined);
                   var explicitNull = func.call(null);
                   var boxedBool = func.call(true);
                   var boxedNumber = func.call(1);
                   var boxedString = func.call("");
                   implicitThis === OtherGlobalThis &&
                   explicitUndef === OtherGlobalThis &&
                   explicitNull === OtherGlobalThis &&
                   boxedBool.constructor === OtherBoolean &&
                   boxedNumber.constructor === OtherNumber &&
                   boxedString.constructor === OtherString;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void String_Construct_Uses_NewTarget_Realm_StringPrototype_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        otherRealm.Eval("new Function();");
        realm.Global["OtherCtor"] = otherRealm.Accumulator;
        realm.Global["OtherStringPrototype"] = JsValue.FromObject(otherRealm.StringPrototype);
        realm.Eval("""
                   OtherCtor.prototype = null;
                   var o = Reflect.construct(String, [], OtherCtor);
                   Object.getPrototypeOf(o) === OtherStringPrototype;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DynamicFunction_Uses_NewTarget_Realm_FunctionPrototype_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var realmA = realm.Agent.CreateRealm();
        var realmB = realm.Agent.CreateRealm();
        realm.Global["RealmAFunction"] = JsValue.FromObject(realmA.FunctionConstructor);
        realm.Global["RealmBFunction"] = JsValue.FromObject(realmB.FunctionConstructor);
        realm.Eval("""
                   var newTarget = new RealmBFunction();
                   newTarget.prototype = null;
                   var fn = Reflect.construct(RealmAFunction, ["return 1;"], newTarget);
                   Object.getPrototypeOf(fn) === RealmBFunction.prototype;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectConstruct_Uses_Recursive_BoundNewTarget_Realm_ObjectPrototype_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var realmA = realm.Agent.CreateRealm();
        var realmB = realm.Agent.CreateRealm();
        var realmC = realm.Agent.CreateRealm();
        var realmD = realm.Agent.CreateRealm();
        realm.Global["RealmAFunction"] = JsValue.FromObject(realmA.FunctionConstructor);
        realm.Global["RealmAObjectPrototype"] = JsValue.FromObject(realmA.ObjectPrototype);
        realm.Global["RealmBFunction"] = JsValue.FromObject(realmB.FunctionConstructor);
        realm.Global["RealmCFunction"] = JsValue.FromObject(realmC.FunctionConstructor);
        realm.Global["RealmDObject"] = JsValue.FromObject(realmD.ObjectConstructor);
        realm.Eval("""
                   var newTarget = new RealmAFunction();
                   newTarget.prototype = 1;
                   var bound = RealmBFunction.prototype.bind.call(newTarget);
                   var boundBound = RealmCFunction.prototype.bind.call(bound);
                   var o = Reflect.construct(RealmDObject, [], boundBound);
                   Object.getPrototypeOf(o) === RealmAObjectPrototype;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DynamicFunction_PrototypeObject_Uses_Constructor_Realm_ObjectPrototype()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var realmA = realm.Agent.CreateRealm();
        var realmB = realm.Agent.CreateRealm();
        realm.Global["RealmAFunction"] = JsValue.FromObject(realmA.FunctionConstructor);
        realm.Global["RealmAObjectPrototype"] = JsValue.FromObject(realmA.ObjectPrototype);
        realm.Global["RealmBFunction"] = JsValue.FromObject(realmB.FunctionConstructor);
        realm.Eval("""
                   var newTarget = new RealmBFunction();
                   newTarget.prototype = null;
                   var fn = Reflect.construct(RealmAFunction, ["calls += 1;"], newTarget);
                   Object.getPrototypeOf(fn.prototype) === RealmAObjectPrototype;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DynamicFunction_CrossRealm_Construct_Executes_In_Constructor_Realm()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var realmA = realm.Agent.CreateRealm();
        var realmB = realm.Agent.CreateRealm();
        realmA.Global["calls"] = 0;
        realm.Global["RealmAFunction"] = JsValue.FromObject(realmA.FunctionConstructor);
        realm.Global["RealmAObject"] = JsValue.FromObject(realmA.ObjectConstructor);
        realm.Global["RealmBFunction"] = JsValue.FromObject(realmB.FunctionConstructor);
        realm.Eval("""
                   var newTarget = new RealmBFunction();
                   newTarget.prototype = null;
                   var fn = Reflect.construct(RealmAFunction, ["calls += 1;"], newTarget);
                   var instance = new fn();
                   Object.getPrototypeOf(fn) === RealmBFunction.prototype &&
                   Object.getPrototypeOf(fn.prototype) === RealmAObject.prototype &&
                   instance instanceof RealmAObject;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
        Assert.That(realmA.Global["calls"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Global_Boolean_String_Number_RegExp_Json_Math_AreDefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof Boolean === "function" &&
                                                                   typeof String === "function" &&
                                                                   typeof Number === "function" &&
                                                                   typeof RegExp === "function" &&
                                                                   typeof JSON === "object" &&
                                                                   typeof Math === "object";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_Constructor_Is_Subclass_Of_Function()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                async function foo() {}
                                var AsyncFunction = foo.constructor;
                                Object.getPrototypeOf(AsyncFunction) === Function &&
                                Object.getPrototypeOf(AsyncFunction.prototype) === Function.prototype &&
                                foo instanceof Function;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Boolean_String_Number_Constructors_BasicSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const b = Boolean(0) === false && new Boolean(true).valueOf() === true;
                                                                   const s = String(12) === "12" && new String("x").toString() === "x";
                                                                   const n = Number("3.5") === 3.5 && new Number(4).valueOf() === 4;
                                                                   b && s && n;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Number_Constructor_Converts_BigInt_To_Number()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Number(0n) === 0 &&
                                                                   Number(2n ** 53n + 3n) === 9007199254740996;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExp_And_Json_BasicSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const rx = new RegExp("a+");
                                                                   const ok1 = rx.test("caaad");
                                                                   const m = rx.exec("zzzaaa");
                                                                   const ok2 = m[0] === "aaa" && m.index === 3;
                                                                   const text = JSON.stringify({ a: 1, b: [2, 3] });
                                                                   const obj = JSON.parse(text);
                                                                   ok1 && ok2 && obj.a === 1 && obj.b[1] === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Raw_BigInt_Throws_But_Replacer_Can_Convert_It()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let primitive = false;
            try { JSON.stringify(0n); } catch (e) { primitive = e.name === "TypeError"; }
            let boxed = false;
            try { JSON.stringify(Object(0n)); } catch (e) { boxed = e.name === "TypeError"; }
            let nested = false;
            try { JSON.stringify({ x: 0n }); } catch (e) { nested = e.name === "TypeError"; }

            function replacer(k, v) {
              return typeof v === "bigint" ? "bigint" : v;
            }

            primitive &&
            boxed &&
            nested &&
            JSON.stringify(0n, replacer) === '"bigint"' &&
            JSON.stringify({ x: 0n }, replacer) === '{"x":"bigint"}';
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_BigInt_Uses_ToJson_Before_Throwing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            BigInt.prototype.toJSON = function () { return this.toString(); };
            JSON.stringify(0n) === '"0"';
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_BigInt_ToJson_Is_Called_With_Primitive_Receiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            Object.defineProperty(BigInt.prototype, "toJSON", {
              get() {
                "use strict";
                return () => typeof this;
              }
            });

            JSON.stringify(1n) === '"bigint"';
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Global_IsNaN_IsDefined_AndCoercesLikeJs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof isNaN === "function" &&
                                                                   isNaN() === true &&
                                                                   isNaN("123") === false &&
                                                                   isNaN("x") === true &&
                                                                   isNaN(new Number(5)) === false &&
                                                                   isNaN(undefined) === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Global_IsFinite_IsDefined_AndCoercesLikeJs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof isFinite === "function" &&
                                                                   isFinite("0") === true &&
                                                                   isFinite("") === true &&
                                                                   isFinite("Infinity") === false &&
                                                                   isFinite(true) === true &&
                                                                   isFinite(undefined) === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Number_Static_Predicates_Are_Defined_And_Do_Not_Coerce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof Number.isFinite === "function" &&
                                                                   typeof Number.isNaN === "function" &&
                                                                   typeof Number.isInteger === "function" &&
                                                                   typeof Number.isSafeInteger === "function" &&
                                                                   Number.isFinite(1) === true &&
                                                                   Number.isFinite("1") === false &&
                                                                   Number.isNaN(NaN) === true &&
                                                                   Number.isNaN("NaN") === false &&
                                                                   Number.isInteger(1) === true &&
                                                                   Number.isInteger(1.5) === false &&
                                                                   Number.isSafeInteger(9007199254740991) === true &&
                                                                   Number.isSafeInteger(9007199254740992) === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Boxed_Number_Uses_Observable_ToNumber_And_ToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var toPrimitiveReplacer = function(_key, value) {
                                                                     if (value === 'str') {
                                                                       var num = new Number(42);
                                                                       num.toString = function() { throw new Error('should not be called'); };
                                                                       num.valueOf = function() { return 2; };
                                                                       return num;
                                                                     }
                                                                     return value;
                                                                   };

                                                                   var num = new Number(10);
                                                                   num.toString = function() { return 'toString'; };
                                                                   num.valueOf = function() { throw new Error('should not be called'); };

                                                                   [
                                                                     JSON.stringify(['str'], toPrimitiveReplacer),
                                                                     JSON.stringify({ 10: 1, toString: 2, valueOf: 3 }, [num])
                                                                   ].join('|');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("[2]|{\"toString\":2}"));
    }

    [Test]
    public void JsonStringify_Revoked_CrossRealm_Proxy_Replacer_Uses_CurrentRealm_TypeError()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        otherRealm.Eval("""
                        var handle = Proxy.revocable([], {});
                        handle.revoke();
                        handle.proxy;
                        """);
        realm.Global["OtherRevokedProxy"] = otherRealm.Accumulator;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var ok = false;
                                                                   try {
                                                                     JSON.stringify({}, OtherRevokedProxy);
                                                                   } catch (e) {
                                                                     ok = e.constructor === TypeError;
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayOf_Calls_Constructor_With_Argument_Count()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var len;
                                                                   var hits = 0;
                                                                   function C(length) {
                                                                     len = length;
                                                                     hits++;
                                                                   }

                                                                   Array.of.call(C);
                                                                   var a = len === 0 && hits === 1;
                                                                   Array.of.call(C, 'a', 'b');
                                                                   var b = len === 2 && hits === 2;
                                                                   Array.of.call(C, false, null, undefined);
                                                                   var c = len === 3 && hits === 3;
                                                                   a && b && c;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayOf_Uses_CreateDataProperty_Not_Set_For_Indices()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function A(_length) {
                                                                     Object.defineProperty(this, "0", {
                                                                       value: 1,
                                                                       writable: false,
                                                                       enumerable: false,
                                                                       configurable: true
                                                                     });
                                                                   }

                                                                   var res = Array.of.call(A, 2);
                                                                   var desc = Object.getOwnPropertyDescriptor(res, "0");
                                                                   desc.value === 2 &&
                                                                     desc.writable === true &&
                                                                     desc.enumerable === true &&
                                                                     desc.configurable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Global_NaN_Infinity_Undefined_AreDefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof NaN === "number" &&
                                                                   typeof Infinity === "number" &&
                                                                   (undefined === void 0);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}

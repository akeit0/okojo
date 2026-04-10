using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ForOfTests
{
    [Test]
    public void ForOf_Array_BasicSum()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let s = 0;
                                                                   for (let x of [1, 2, 3]) {
                                                                       s = s + x;
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void ArrayLiteral_IntegerElements_Preserve_Int32_Tag()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let a = [1, 2, 3];
                                                                   a[0];
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void ForOf_Array_BreakAndContinue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let s = 0;
                                                                   for (let x of [1, 2, 3, 4]) {
                                                                       if (x == 2) continue;
                                                                       if (x == 4) break;
                                                                       s = s + x;
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4)); // 1 + 3
    }

    [Test]
    public void ForOf_CustomIterable_UsesIteratorFallback()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const iter = {
                                                                       i: 0,
                                                                       next: function () {
                                                                           this.i = this.i + 1;
                                                                           if (this.i < 4) return { value: this.i, done: false };
                                                                           return { value: 0, done: true };
                                                                       }
                                                                   };
                                                                   iter[Symbol.iterator] = function () { return iter; };

                                                                   let s = 0;
                                                                   for (let x of iter) {
                                                                       s = s + x;
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void ForOf_Object_Destructuring_Allows_Fresh_Const_Bindings_Across_Loops()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const values = [
                                                                     { label: "a", args: [] },
                                                                     { label: "b", args: [1] }
                                                                   ];

                                                                   let labels = "";
                                                                   for (const { label, args } of values) {
                                                                     labels += label + ":" + args.length + "|";
                                                                   }

                                                                   let second = "";
                                                                   for (const { label, args, expectedArgs } of values) {
                                                                     second += label + ":" + args.length + ":" + String(expectedArgs) + "|";
                                                                   }

                                                                   labels + "#" + second;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a:0|b:1|#a:0:undefined|b:1:undefined|"));
    }

    [Test]
    public void ForOf_Array_Holes_And_Undefined_Yield_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var array = [0, 'a', true, false, null, , undefined, NaN];
                                                                   const seen = [];
                                                                   for (var value of array) {
                                                                     seen.push(value === null ? "null" : Number.isNaN(value) ? "number:NaN" : typeof value + ":" + String(value));
                                                                   }
                                                                   seen.join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo(
                "number:0|string:a|boolean:true|boolean:false|null|undefined:undefined|undefined:undefined|number:NaN"));
    }

    [Test]
    public void ForOf_Compiler_EmitsFastPathAndIteratorStepRuntimeCalls()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function t(a) {
                                                                       let s = 0;
                                                                       for (let x of a) s = s + 1;
                                                                       return s;
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var code = t.Script.Bytecode;

        var sawForOfFastPathLengthProbe = false;
        var sawGetIteratorMethod = false;
        for (var i = 0; i + 3 < code.Length; i++)
        {
            if ((JsOpCode)code[i] != JsOpCode.CallRuntime)
                continue;
            var runtimeId = (RuntimeId)code[i + 1];
            if (runtimeId == RuntimeId.ForOfFastPathLength)
            {
                sawForOfFastPathLengthProbe = true;
                continue;
            }

            if (runtimeId == RuntimeId.GetIteratorMethod)
                sawGetIteratorMethod = true;
        }

        Assert.That(sawForOfFastPathLengthProbe, Is.True);
        Assert.That(sawGetIteratorMethod, Is.True);
    }

    [Test]
    public void ForOf_LetCapture_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let f = [undefined, undefined, undefined];
                                                                   for (let x of [1, 2, 3]) {
                                                                     f[x - 1] = function() { return x; };
                                                                   }
                                                                   f[0]() === 1 && f[1]() === 2 && f[2]() === 3;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_ConstCapture_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let f = [undefined, undefined, undefined];
                                                                   for (const x of [1, 2, 3]) {
                                                                     f[x - 1] = function() { return x; };
                                                                   }
                                                                   f[0]() === 1 && f[1]() === 2 && f[2]() === 3;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Let_DirectRead_And_Capture_Share_Current_Iteration_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let s = 0;
                                                                   let f = [undefined, undefined, undefined];
                                                                   for (let x of [1, 2, 3]) {
                                                                     s += x;
                                                                     f[x - 1] = function() { return x; };
                                                                   }
                                                                   s === 6 && f[0]() === 1 && f[1]() === 2 && f[2]() === 3;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Destructured_Capture_Prefers_Current_Loop_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const testCases = [
                                                                     { label: "x", args: [], expectedArgs: [undefined, undefined] },
                                                                   ];
                                                                   let ok = true;
                                                                   for (const { label, args, expectedArgs } of testCases) {
                                                                     const spy = {
                                                                       toLocaleString(...receivedArgs) {
                                                                         ok = ok &&
                                                                              label === "x" &&
                                                                              expectedArgs.length === 2 &&
                                                                              receivedArgs.length === 2;
                                                                         return "ok";
                                                                       }
                                                                     };
                                                                     ok = ok && [spy].toLocaleString(...args) === "ok";
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_LetBodyClosure_Boundary_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let first, second;
                                                                   for (let x of ['first', 'second']) {
                                                                     if (!first)
                                                                       first = function() { return x; };
                                                                     else
                                                                       second = function() { return x; };
                                                                   }
                                                                   first() === 'first' && second() === 'second';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_LetBodyClosure_ShadowingOuterBinding_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let first, second;
                                                                   for (let x of ['first', 'second']) {
                                                                     if (!first)
                                                                       first = function() { return x; };
                                                                     else
                                                                       second = function() { return x; };
                                                                   }
                                                                   first() === 'first' && second() === 'second';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Continue_From_Catch_Does_Not_Close_Iterator_Early()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* values() {
                                                                     yield 1;
                                                                     yield 1;
                                                                   }

                                                                   let i = 0;
                                                                   for (var x of values()) {
                                                                     try {
                                                                       throw new Error();
                                                                     } catch (err) {
                                                                       i++;
                                                                       continue;
                                                                     }
                                                                   }

                                                                   i === 2;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Continue_From_Finally_Does_Not_Close_Iterator_Early()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* values() {
                                                                     yield 1;
                                                                     yield 1;
                                                                   }

                                                                   let i = 0;
                                                                   for (var x of values()) {
                                                                     try {
                                                                       throw new Error();
                                                                     } catch (err) {
                                                                     } finally {
                                                                       i++;
                                                                       continue;
                                                                     }
                                                                   }

                                                                   i === 2;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Throw_Closes_Iterator_Before_Outer_Catch()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let startedCount = 0;
                                                                   let returnCount = 0;
                                                                   let iterationCount = 0;

                                                                   const iterable = {
                                                                     [Symbol.iterator]() {
                                                                       return {
                                                                         next() {
                                                                           startedCount += 1;
                                                                           return { done: false, value: null };
                                                                         },
                                                                         return() {
                                                                           returnCount += 1;
                                                                           return {};
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   try {
                                                                     for (var x of iterable) {
                                                                       iterationCount += 1;
                                                                       throw 0;
                                                                     }
                                                                   } catch (err) {}

                                                                   startedCount === 1 && iterationCount === 1 && returnCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_GeneratorRange_SumsExpectedValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* range(start, end) {
                                                                       for (let i = start; i < end; i++) {
                                                                           yield i;
                                                                       }
                                                                   }

                                                                   function sumFunction() {
                                                                       let s = 0;
                                                                       for (let i of range(0, 100)) {
                                                                           s += i;
                                                                       }
                                                                       return s;
                                                                   }

                                                                   sumFunction;
                                                                   """));

        realm.Execute(script);

        var function = realm.Accumulator.AsObject() as JsBytecodeFunction;
        realm.Execute(function!);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4950));
    }

    [Test]
    public void ForOf_GeneratorInstance_Exposes_SymbolIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* range(start, end) {
                                                                       for (let i = start; i < end; i++) {
                                                                           yield i;
                                                                       }
                                                                   }

                                                                   typeof range(0, 1)[Symbol.iterator];
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function"));
    }

    [Test]
    public void ForOf_ConstArrayDestructuring_Head_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const arrays = [["a", "first"], ["b", "second"]];
                                                                   let s = "";
                                                                   for (const [arg, description] of arrays) {
                                                                       s = s + arg + ":" + description + "|";
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a:first|b:second|"));
    }

    [Test]
    public void ForOf_ConstNestedArrayDestructuring_Head_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const values = [[["a", "g"], "abc", "z"], [["b", "gy"], "def", "q"]];
                                                                   let s = "";
                                                                   for (const [[reStr, flags], thisValue, replaceValue] of values) {
                                                                       s = s + reStr + ":" + flags + ":" + thisValue + ":" + replaceValue + "|";
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a:g:abc:z|b:gy:def:q|"));
    }

    [Test]
    public void ForOf_ConstObjectDestructuring_Head_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const values = [{ label: "first", args: 1 }, { label: "second", args: 2 }];
                                                                   let s = "";
                                                                   for (const { label, args } of values) {
                                                                       s = s + label + ":" + args + "|";
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("first:1|second:2|"));
    }

    [Test]
    public void ForOf_LetObjectDestructuring_Head_With_Default_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const values = [{ era: "ce" }, { era: "bce", aliases: ["bc"] }];
                                                                   let s = "";
                                                                   for (let { era, aliases = [] } of values) {
                                                                       s = s + era + ":" + aliases.length + "|";
                                                                   }
                                                                   s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ce:0|bce:1|"));
    }

    [Test]
    public void ForOf_MemberExpression_Head_Assigns_To_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let target = {};
                                                                   let count = 0;
                                                                   for (target.value of [23]) {
                                                                       count = count + 1;
                                                                   }
                                                                   target.value === 23 && count === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_ConstArrayDestructuring_Head_Capture_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let f = [];
                                                                   for (const [name] of [['green'], ['bgWhiteBright']]) {
                                                                     f.push(function() { return name; });
                                                                   }
                                                                   f[0]() === 'green' && f[1]() === 'bgWhiteBright';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_ConstArrayDestructuring_MultipleHeadCaptures_UseFreshBindingsPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let f = [];
                                                                   for (const [name, style] of [['green', '<g>'], ['bgWhiteBright', '<b>']]) {
                                                                     f.push(function() { return name + '|' + style; });
                                                                   }
                                                                   f[0]() === 'green|<g>' && f[1]() === 'bgWhiteBright|<b>';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_ConstArrayDestructuring_GetterMethodCapture_UsesFreshBindingsPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const styles = Object.create(null);
                                                                   for (const [name, style] of [['green', '<g>'], ['bgWhiteBright', '<b>']]) {
                                                                     styles[name] = {
                                                                       get() {
                                                                         return () => name + '|' + style;
                                                                       }
                                                                     };
                                                                   }

                                                                   const proto = Object.defineProperties(() => {}, { ...styles });
                                                                   const value = Object.getOwnPropertyDescriptor(proto, 'green').get();
                                                                   value() === 'green|<g>';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_ConstArrayDestructuring_PrototypeGetterCapture_UsesFreshBindingsPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const styles = Object.create(null);
                                                                   for (const [name, style] of [['green', '<g>'], ['bgWhiteBright', '<b>']]) {
                                                                     styles[name] = {
                                                                       get() {
                                                                         const builder = () => name + '|' + style;
                                                                         Object.defineProperty(this, name, { value: builder });
                                                                         return builder;
                                                                       }
                                                                     };
                                                                   }

                                                                   const proto = Object.defineProperties(() => {}, { ...styles });
                                                                   const target = () => 'base';
                                                                   Object.setPrototypeOf(target, proto);

                                                                   const value = target.green();
                                                                   value === 'green|<g>' &&
                                                                     Object.getOwnPropertyDescriptor(target, 'green') !== undefined &&
                                                                     Object.getOwnPropertyDescriptor(target, 'bgWhiteBright') === undefined;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_MemberExpression_Head_Allows_Async_Identifier_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var async = { x: 0 };
                                                                   for (async.x of [1]) ;
                                                                   async.x === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Closes_On_Break()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var returnCount = 0;
                                                                   var iterable = {};
                                                                   iterable[Symbol.iterator] = function() {
                                                                       return {
                                                                           next: function() { return { done: false, value: 1 }; },
                                                                           return: function() { returnCount += 1; return {}; }
                                                                       };
                                                                   };
                                                                   for (var x of iterable) {
                                                                       break;
                                                                   }
                                                                   returnCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Closes_On_NonLocal_Continue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var returnCount = 0;
                                                                   var iterable = {};
                                                                   iterable[Symbol.iterator] = function() {
                                                                       return {
                                                                           next: function() { return { done: false, value: 1 }; },
                                                                           return: function() { returnCount += 1; return {}; }
                                                                       };
                                                                   };
                                                                   outer: do {
                                                                       for (var x of iterable) {
                                                                           continue outer;
                                                                       }
                                                                   } while (false);
                                                                   returnCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Closes_On_Return()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var returnCount = 0;
                                                                   var iterable = {};
                                                                   iterable[Symbol.iterator] = function() {
                                                                       return {
                                                                           next: function() { return { done: false, value: 1 }; },
                                                                           return: function() { returnCount += 1; return {}; }
                                                                       };
                                                                   };
                                                                   function run() {
                                                                       for (var x of iterable) {
                                                                           return returnCount;
                                                                       }
                                                                       return -1;
                                                                   }
                                                                   run() === 0 && returnCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Closes_On_Throw()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var returnCount = 0;
                                                                   var iterable = {};
                                                                   iterable[Symbol.iterator] = function() {
                                                                       return {
                                                                           next: function() { return { done: false, value: 1 }; },
                                                                           return: function() { returnCount += 1; return {}; }
                                                                       };
                                                                   };
                                                                   try {
                                                                       for (var x of iterable) {
                                                                           throw 1;
                                                                       }
                                                                   } catch (e) {
                                                                   }
                                                                   returnCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Closes_When_Member_Head_Assignment_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var callCount = 0;
                                                                   var iterable = {};
                                                                   var x = {
                                                                     set attr(_) {
                                                                       throw new Error('boom');
                                                                     }
                                                                   };
                                                                   iterable[Symbol.iterator] = function() {
                                                                     return {
                                                                       next: function() { return { done: false, value: 0 }; },
                                                                       return: function() { callCount += 1; }
                                                                     };
                                                                   };
                                                                   try {
                                                                     for (x.attr of iterable) { }
                                                                   } catch (e) {
                                                                   }
                                                                   callCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Closes_When_Destructuring_Head_Assignment_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var callCount = 0;
                                                                   var iterable = {};
                                                                   var x = {
                                                                     set attr(_) {
                                                                       throw new Error('boom');
                                                                     }
                                                                   };
                                                                   iterable[Symbol.iterator] = function() {
                                                                     return {
                                                                       next: function() { return { done: false, value: [0] }; },
                                                                       return: function() { callCount += 1; }
                                                                     };
                                                                   };
                                                                   try {
                                                                     for ([x.attr] of iterable) { }
                                                                   } catch (e) {
                                                                   }
                                                                   callCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Array_FastPath_Observes_Length_Growth()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var array = [0];
                                                                   var count = 0;
                                                                   for (var x of array) {
                                                                     if (count === 0) {
                                                                       array.push(1);
                                                                     }
                                                                     count += 1;
                                                                   }
                                                                   count === 2;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Array_FastPath_Observes_Length_Shrink()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var array = [0, 1];
                                                                   var count = 0;
                                                                   for (var x of array) {
                                                                     array.pop();
                                                                     count += 1;
                                                                   }
                                                                   count === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_IteratorFallback_Caches_Next_Method_Per_Iterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var iterable = {};
                                                                   var iterator = {};
                                                                   var iterationCount = 0;
                                                                   var loadNextCount = 0;

                                                                   iterable[Symbol.iterator] = function() { return iterator; };

                                                                   function next() {
                                                                     if (iterationCount) return { done: true };
                                                                     return { value: 45, done: false };
                                                                   }

                                                                   Object.defineProperty(iterator, 'next', {
                                                                     get() { loadNextCount++; return next; },
                                                                     configurable: true
                                                                   });

                                                                   for (var x of iterable) {
                                                                     Object.defineProperty(iterator, 'next', {
                                                                       get() { throw new Error('next refetched'); }
                                                                     });
                                                                     iterationCount += 1;
                                                                   }

                                                                   iterationCount === 1 && loadNextCount === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_StringIterator_Preserves_Unpaired_Surrogates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var string = 'a\ud801b\ud801';
                                                                   var values = [];
                                                                   for (var value of string) {
                                                                     values.push(value);
                                                                   }
                                                                   values.length === 4 &&
                                                                     values[0] === 'a' &&
                                                                     values[1] === '\ud801' &&
                                                                     values[2] === 'b' &&
                                                                     values[3] === '\ud801';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}

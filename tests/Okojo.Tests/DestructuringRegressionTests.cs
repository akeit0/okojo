using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class DestructuringRegressionTests
{
    [Test]
    public void ConstStatement_ArrayPatternEmpty_DoesNotIterate()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var iterations = 0;
            var iter = function*() {
              iterations += 1;
            }();

            const [] = iter;

            iterations;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void ConstStatement_ArrayPatternElementObjectDefault_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const [{ x, y, z } = { x: 44, y: 55, z: 66 }] = [];
            x * 10000 + y * 100 + z;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(445566));
    }

    [Test]
    public void ConstStatement_ArrayPatternRestArrayElision_ConsumesIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var first = 0;
            var second = 0;
            function* g() {
              first += 1;
              yield;
              second += 1;
            };

            const [...[,]] = g();

            first * 10 + second;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void ForStatement_HeadLetArrayDestructuring_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var value;
            for ( let[x] = [23]; ; ) {
              value = x;
              break;
            }
            typeof x === "undefined" && value === 23;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForInStatement_HeadLetArrayDestructuring_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var obj = Object.create(null);
            var value;
            obj.key = 1;
            for ( let[x] in obj ) {
              value = x;
            }
            typeof x === "undefined" && value === "k";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_HeadLetArrayDestructuring_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var value;
            for ( let[x] of [[34]] ) {
              value = x;
            }
            typeof x === "undefined" && value === 34;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_AssignmentHead_ArrayElement_ObjectLiteralMemberTarget_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var setValue = 0;

            for ([{
              get y() {
                throw new Error("getter should not run");
              },
              set y(value) {
                setValue = value;
              }
            }.y] of [[23]]) {
            }

            setValue;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(23));
    }

    [Test]
    public void ForOfStatement_AssignmentHead_ArrayRest_ComputedMemberAbrupt_ClosesIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var nextCount = 0;
            var returnCount = 0;
            var iterable = {};
            var iterator = {
              next: function() {
                nextCount += 1;
                return { done: true };
              },
              return: function() {
                returnCount += 1;
              }
            };

            function thrower() {
              throw new Error("boom");
            }

            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            var ok = false;
            try {
              for ([...{}[thrower()]] of [iterable]) {
              }
            } catch (e) {
              ok = e.message === "boom";
            }

            ok && nextCount === 0 && returnCount === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_AssignmentHead_ArrayElement_Respects_LetTdz()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var counter = 0;
            var threw = false;

            try {
              for ([x] of [[]]) {
                counter += 1;
              }
              counter += 1;
            } catch (e) {
              threw = e instanceof ReferenceError;
            }

            let x;
            threw && counter === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_AssignmentHead_ObjectShorthandDefaults_With_Sloppy_Eval_And_Arguments_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var eval, arguments;
            var counter = 0;

            for ({ eval = 3, arguments = 4 } of [{}]) {
              counter += eval + arguments;
            }

            counter === 7;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AssignmentExpression_ObjectShorthandDefaults_With_Sloppy_Eval_And_Arguments_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var eval, arguments;
            var vals = {};
            var result = ({ eval = 3, arguments = 4 } = vals);
            eval === 3 && arguments === 4 && result === vals;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AssignmentExpression_ObjectPattern_DuplicateKeys_DoNot_Use_ObjectLiteral_StrictChecks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            var xGen, gen;
            var vals = {};
            var result = ({ x: xGen = function* x() {}, x: gen = function*() {} } = vals);
            xGen.name !== "xGen" && gen.name === "gen" && result === vals;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_AssignmentHead_ArrayRest_YieldingMemberTarget_ClosesIteratorOnGeneratorReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var nextCount = 0;
            var returnCount = 0;
            var iterable = {};
            var iterator = {
              next() {
                nextCount += 1;
                throw new Error("next should not run before target evaluation");
              },
              return() {
                returnCount += 1;
                return {};
              }
            };

            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            function* g() {
              for ([...{}[yield]] of [iterable]) {
              }
            }

            var iter = g();
            iter.next();
            var result = iter.return(444);
            nextCount === 0 && returnCount === 1 && result.value === 444 && result.done;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_AssignmentHead_TrailingArrayRest_YieldingMemberTarget_DoesNotConsumeRestEarly()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var nextCount = 0;
            var returnCount = 0;
            var iterable = {};
            var iterator = {
              next() {
                nextCount += 1;
                return { done: nextCount > 10 };
              },
              return() {
                returnCount += 1;
                return {};
              }
            };

            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            function* g() {
              var x;
              for ([x, ...{}[yield]] of [iterable]) {
              }
            }

            var iter = g();
            iter.next();
            var result = iter.return(999);
            nextCount === 1 && returnCount === 1 && result.value === 999 && result.done;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOfStatement_AssignmentHead_ArrayElement_YieldingMemberTarget_PropagatesIteratorCloseError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var returnCount = 0;
            var iterable = {};
            var iterator = {
              return() {
                returnCount += 1;
                throw new Error("close");
              }
            };

            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            function* g() {
              for ([{}[yield]] of [iterable]) {
              }
            }

            var iter = g();
            iter.next();

            var ok = false;
            try {
              iter.return();
            } catch (e) {
              ok = e.message === "close";
            }

            ok && returnCount === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForStatement_VarEmptyObjectPattern_ThrowsBeforeTestEvaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var ok = false;
            try {
              for (var {} = null; missing < 1; ) {
              }
            } catch (e) {
              ok = e instanceof TypeError;
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForStatement_VarObjectPatternDefaultThrow_ThrowsBeforeTestEvaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function thrower() {
              throw new Error("boom");
            }

            var ok = false;
            try {
              for (var { x = thrower() } = {}; missing < 1; ) {
              }
            } catch (e) {
              ok = e.message === "boom";
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AssignmentDestructuring_DefaultThrow_Preserves_Original_Throw_And_Touches_Return_Getter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function MyError() {}
            function thrower() {
              throw new MyError();
            }

            var returnGetterCalled = 0;
            var iterator = {
              [Symbol.iterator]() { return this; },
              next() { return { done: false }; },
              get return() {
                returnGetterCalled += 1;
                throw "bad";
              }
            };

            var ok = false;
            try {
              var a;
              ([a = thrower()] = iterator);
            } catch (e) {
              ok = e instanceof MyError;
            }

            ok && returnGetterCalled === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectAssignmentDestructuring_KeyedMemberTarget_Preserves_Evaluation_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var log = [];

            function source() {
              log.push("source");
              return {
                get p() {
                  log.push("get");
                }
              };
            }

            function target() {
              log.push("target");
              return {
                set q(v) {
                  log.push("set");
                }
              };
            }

            function sourceKey() {
              log.push("source-key");
              return {
                toString() {
                  log.push("source-key-tostring");
                  return "p";
                }
              };
            }

            function targetKey() {
              log.push("target-key");
              return {
                toString() {
                  log.push("target-key-tostring");
                  return "q";
                }
              };
            }

            ({ [sourceKey()]: target()[targetKey()] } = source());
            log.join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(
            "source|source-key|source-key-tostring|target|target-key|get|target-key-tostring|set"));
    }

    [Test]
    public void ArrayAssignmentDestructuring_KeyedMemberTarget_Preserves_Evaluation_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var log = [];

            function source() {
              log.push("source");
              var iterator = {
                next() {
                  log.push("iterator-step");
                  return {
                    get done() {
                      log.push("iterator-done");
                      return true;
                    },
                    get value() {
                      log.push("iterator-value");
                    }
                  };
                }
              };
              var source = {};
              source[Symbol.iterator] = function() {
                log.push("iterator");
                return iterator;
              };
              return source;
            }

            function target() {
              log.push("target");
              return {
                set q(v) {
                  log.push("set");
                }
              };
            }

            function targetKey() {
              log.push("target-key");
              return {
                toString() {
                  log.push("target-key-tostring");
                  return "q";
                }
              };
            }

            ([target()[targetKey()]] = source());
            log.join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(
            "source|iterator|target|target-key|iterator-step|iterator-done|target-key-tostring|set"));
    }

    [Test]
    public void ArrayAssignmentDestructuring_TargetThrow_Preserves_Original_Throw_When_IteratorReturn_Getter_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function MyError() {}
            var target = {
              set a(v) {
                throw new MyError();
              }
            };

            var returnGetterCalled = 0;
            var iterator = {
              [Symbol.iterator]() { return this; },
              next() { return { done: false }; },
              get return() {
                returnGetterCalled += 1;
                throw "bad";
              }
            };

            var ok = false;
            try {
              ([target.a] = iterator);
            } catch (e) {
              ok = e instanceof MyError;
            }

            ok && returnGetterCalled === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayAssignmentDestructuring_DefaultThrow_Preserves_Original_Throw_When_IteratorReturn_Is_Not_Callable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function MyError() {}
            function thrower() { throw new MyError(); }

            var iterable = {
              [Symbol.iterator]() { return this; },
              next() { return { done: false }; },
              return: 0
            };

            var ok = false;
            try {
              var a;
              ([a = thrower()] = iterable);
            } catch (e) {
              ok = e instanceof MyError;
            }

            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayAssignmentDestructuring_DefaultThrow_Preserves_Original_Throw_For_All_NonCallable_Return_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function MyError() {}
            function thrower() { throw new MyError(); }

            var values = [0, 0n, true, "string", {}, Symbol("x")];
            var ok = true;

            for (var i = 0; i < values.length; i++) {
              var iterable = {
                [Symbol.iterator]() { return this; },
                next() { return { done: false }; },
                return: values[i]
              };

              try {
                var a;
                ([a = thrower()] = iterable);
                ok = false;
              } catch (e) {
                if (!(e instanceof MyError)) {
                  ok = false;
                }
              }
            }

            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayAssignmentDestructuring_DefaultThrow_WithNonCallableReturn_Preserves_Host_ThrownValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var iterable = {
              [Symbol.iterator]() { return this; },
              next() { return { done: false }; },
              return: 0
            };

            var a;
            ([a = (() => { throw 7; })()] = iterable);
            """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("JS_THROW_VALUE"));
        Assert.That(ex.ThrownValue.HasValue, Is.True);
        Assert.That(ex.ThrownValue!.Value.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void ArrayAssignmentDestructuring_MinimalReleaseProbe_NextPropertyStillCallable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var iterable = {
              [Symbol.iterator]() { return this; },
              next() { return { done: true, value: 1 }; },
              return: 0
            };

            var a;
            ([a] = iterable);
            a === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyIteratorMethod_Returns_Original_Object_Receiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var iterable = {
              [Symbol.iterator]() { return this; },
              next() { return { done: true, value: 1 }; }
            };

            iterable[Symbol.iterator]() === iterable;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void EmptyObjectBindingDeclaration_RequireObjectCoercible_Allows_BigInt()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let {} = 0n;
            Object.setPrototypeOf(0n, null) === 0n;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}

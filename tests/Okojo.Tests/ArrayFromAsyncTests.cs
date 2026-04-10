using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayFromAsyncTests
{
    [Test]
    public void ArrayFromAsync_Treats_Number_As_ArrayLike()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Number.prototype.length = 2;
            Number.prototype[0] = 1;
            Number.prototype[1] = 2;

            globalThis.ok = false;
            globalThis.p = (async function () {
              const result = await Array.fromAsync(1);
              globalThis.ok = result.length === 2 && result[0] === 1 && result[1] === 2;
            })();
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFromAsync_SyncIterable_Observes_Array_Mutation_After_First_Element()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.ok = false;
            const items = [1, 2, 3];
            const promise = Array.fromAsync(items);
            items[0] = 7;
            items[1] = 8;

            globalThis.p = (async function () {
              const result = await promise;
              globalThis.ok =
                result.length === 3 &&
                result[0] === 1 &&
                result[1] === 8 &&
                result[2] === 3;
            })();
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFromAsync_AsyncIterable_Does_Not_Await_Input_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.ok = false;
            const prom = Promise.resolve({});
            const input = {
              [Symbol.asyncIterator]() {
                let i = 0;
                return {
                  async next() {
                    if (i > 0) return { done: true };
                    i++;
                    return { value: prom, done: false };
                  }
                };
              }
            };

            globalThis.p = (async function () {
              const output = await Array.fromAsync(input);
              globalThis.ok = output.length === 1 && output[0] === prom;
            })();
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFromAsync_AsyncIterable_Rejects_When_Iteration_Fails()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.ok = false;

            async function* generateInput () {
              throw 7;
            }

            globalThis.p = Array.fromAsync(generateInput()).catch(function (e) {
              globalThis.ok = e === 7;
            });
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFromAsync_AsyncTest_Harness_Shape_Completes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["$DONE"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var args = info.Arguments;
            return args.Length == 0 ? JsValue.FromInt32(1) : args[0];
        }, "$DONE", 1));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function asyncTest(testFunc) {
              try {
                testFunc().then(
                  function () { globalThis.done = "ok"; },
                  function (error) { globalThis.done = error; }
                );
              } catch (syncError) {
                globalThis.done = syncError;
              }
            }

            globalThis.done = "pending";
            asyncTest(async function () {
              Number.prototype.length = 2;
              Number.prototype[0] = 1;
              Number.prototype[1] = 2;
              const result = await Array.fromAsync(1);
              if (result.length !== 2 || result[0] !== 1 || result[1] !== 2) {
                throw new Error("bad");
              }
            });
            """));

        realm.Execute(script);
        for (var i = 0; i < 1000 && realm.Global["done"].AsString() == "pending"; i++)
            realm.PumpJobs();
        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void ArrayFromAsync_ArrayLike_Promise_Observes_Length_ToPrimitive()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function formatPropertyName(propertyKey, objectName = "") {
              switch (typeof propertyKey) {
                case "string":
                  if (propertyKey !== String(Number(propertyKey)))
                    return objectName ? `${objectName}.${propertyKey}` : propertyKey;
                  return `${objectName}[${propertyKey}]`;
                case "symbol":
                  return `${objectName}[${String(propertyKey)}]`;
                default:
                  return `${objectName}[${propertyKey}]`;
              }
            }

            function toPrimitiveObserver(calls, primitiveValue, propertyName) {
              return {
                get valueOf() {
                  calls.push(`get ${propertyName}.valueOf`);
                  return function () {
                    calls.push(`call ${propertyName}.valueOf`);
                    return primitiveValue;
                  };
                },
                get toString() {
                  calls.push(`get ${propertyName}.toString`);
                  return function () {
                    calls.push(`call ${propertyName}.toString`);
                    return primitiveValue.toString();
                  };
                },
              };
            }

            function propertyBagObserver(calls, propertyBag, objectName, skipToPrimitive) {
              return new Proxy(propertyBag, {
                get(target, key, receiver) {
                  calls.push(`get ${formatPropertyName(key, objectName)}`);
                  const result = Reflect.get(target, key, receiver);
                  if (result === undefined) {
                    return undefined;
                  }
                  if ((result !== null && typeof result === "object") || typeof result === "function") {
                    return result;
                  }
                  if (skipToPrimitive && skipToPrimitive.indexOf(key) >= 0) {
                    return result;
                  }
                  return toPrimitiveObserver(calls, result, `${formatPropertyName(key, objectName)}`);
                }
              });
            }

            globalThis.ok = false;
            globalThis.p = (async function () {
              const actual = [];
              const items = propertyBagObserver(actual, {
                length: 2,
                0: Promise.resolve(2),
                1: Promise.resolve(1),
              }, "items");
              const result = await Array.fromAsync(items);
              globalThis.ok =
                result.length === 2 &&
                result[0] === 2 &&
                result[1] === 1 &&
                actual.indexOf("get items.length") >= 0 &&
                actual.indexOf("get items.length.valueOf") >= 0 &&
                actual.indexOf("call items.length.valueOf") >= 0 &&
                actual.indexOf("get items[0]") >= 0 &&
                actual.indexOf("get items[1]") >= 0;
            })();
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFromAsync_Uses_Zero_Args_For_Iterable_Constructor_And_Length_For_ArrayLike()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.ok = false;
            const constructorCalls = [];
            function MyArray(...args) {
              constructorCalls.push(args);
            }

            globalThis.p = (async function () {
              await Array.fromAsync.call(MyArray, [1, 2]);
              await Array.fromAsync.call(MyArray, { length: 2, 0: 1, 1: 2 });
              globalThis.ok =
                constructorCalls.length === 2 &&
                constructorCalls[0].length === 0 &&
                constructorCalls[1].length === 1 &&
                constructorCalls[1][0] === 2;
            })();
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    [Ignore(
        "TEMP_SKIP_FUTURE_FIX: proxy numeric defineProperty trap ordering for Array.fromAsync result elements is still incomplete.")]
    public void ArrayFromAsync_Custom_Constructor_Uses_DefineProperty_Then_Length_Set_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function formatPropertyName(propertyKey, objectName = "") {
              switch (typeof propertyKey) {
                case "string":
                  if (propertyKey !== String(Number(propertyKey)))
                    return objectName ? `${objectName}.${propertyKey}` : propertyKey;
                  return `${objectName}[${propertyKey}]`;
                default:
                  return `${objectName}[${propertyKey}]`;
              }
            }

            globalThis.out = "";
            function MyArray() {
              return new Proxy(Object.create(null), {
                set(target, key, value) {
                  globalThis.out += `set ${formatPropertyName(key, "A")}|`;
                  return Reflect.set(target, key, value);
                },
                defineProperty(target, key, descriptor) {
                  globalThis.out += `defineProperty ${formatPropertyName(key, "A")}|`;
                  return Reflect.defineProperty(target, key, descriptor);
                }
              });
            }

            globalThis.p = (async function () {
              await Array.fromAsync.call(MyArray, [1, 2]);
            })();
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["out"].AsString(),
            Is.EqualTo("defineProperty A[0]|defineProperty A[1]|set A.length|"));
    }

    [Test]
    public void ArrayFromAsync_Closes_Sync_Iterator_When_Defining_Element_Fails()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.closed = false;
            globalThis.rejected = false;

            function MyArray() {
              Object.defineProperty(this, 0, {
                enumerable: true,
                writable: true,
                configurable: false,
                value: 0
              });
            }

            const iterator = {
              next() { return { value: 1, done: false }; },
              return() {
                globalThis.closed = true;
                return { done: true };
              },
              [Symbol.iterator]() { return this; }
            };

            globalThis.p = Array.fromAsync.call(MyArray, iterator).then(
              function () { globalThis.rejected = false; },
              function (e) { globalThis.rejected = e instanceof TypeError; }
            );
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["closed"].IsTrue, Is.True);
        Assert.That(realm.Global["rejected"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFromAsync_Closes_Async_Iterator_When_Defining_Element_Fails()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.closed = false;
            globalThis.rejected = false;

            function MyArray() {
              Object.defineProperty(this, 0, {
                enumerable: true,
                writable: true,
                configurable: false,
                value: 0
              });
            }

            const iterator = {
              next() { return Promise.resolve({ value: 1, done: false }); },
              return() {
                globalThis.closed = true;
                return Promise.resolve({ done: true });
              },
              [Symbol.asyncIterator]() { return this; }
            };

            globalThis.p = Array.fromAsync.call(MyArray, iterator).then(
              function () { globalThis.rejected = false; },
              function (e) { globalThis.rejected = e instanceof TypeError; }
            );
            """));

        realm.Execute(script);
        PumpUntilSettled(realm, "p");
        Assert.That(realm.Global["closed"].IsTrue, Is.True);
        Assert.That(realm.Global["rejected"].IsTrue, Is.True);
    }

    private static void PumpUntilSettled(JsRealm realm, string globalPromiseName)
    {
        Assert.That(realm.Global[globalPromiseName].TryGetObject(out var promiseObj), Is.True);
        Assert.That(promiseObj, Is.TypeOf<JsPromiseObject>());
        var promise = (JsPromiseObject)promiseObj;
        for (var i = 0; i < 1000 && promise.IsPending; i++)
            realm.PumpJobs();
        Assert.That(promise.IsPending, Is.False);
    }
}

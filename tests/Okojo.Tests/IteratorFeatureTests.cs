using Okojo.Runtime;

namespace Okojo.Tests;

public class IteratorFeatureTests
{
    [Test]
    public void Iterator_Global_Exists()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("typeof Iterator + '|' + (globalThis.Iterator === Iterator)");
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|true"));
    }

    [Test]
    public void Iterator_Concat_Validates_In_Order_And_Returns_Fresh_Result()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let getIterator = 0;
                   let iterable1 = {
                     get [Symbol.iterator]() {
                       getIterator++;
                       return function() { throw new Error('should not call during validation'); };
                     }
                   };
                   let oldIterResult = { done: false, value: 123 };
                   let iterable = {
                     [Symbol.iterator]() {
                       return {
                         next() { return oldIterResult; }
                       };
                     }
                   };
                   let out = [];
                   try {
                     Iterator.concat(iterable1, null);
                   } catch (e) {
                     out.push(e.name);
                   }
                   let iterator = Iterator.concat(iterable);
                   let iterResult = iterator.next();
                   out.push(getIterator);
                   out.push(iterResult.done);
                   out.push(iterResult.value);
                   out.push(iterResult === oldIterResult);
                   out.join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError|1|false|123|false"));
    }

    [Test]
    public void Iterator_Concat_Accepts_Primitive_Wrapper_Iterables()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let desc = {
                     value: function() {
                       return {
                         next() {
                           return { done: true, value: undefined };
                         }
                       };
                     },
                     writable: false,
                     enumerable: false,
                     configurable: true,
                   };
                   Object.defineProperty(Boolean.prototype, Symbol.iterator, desc);
                   Object.defineProperty(Number.prototype, Symbol.iterator, desc);
                   Object.defineProperty(BigInt.prototype, Symbol.iterator, desc);
                   Object.defineProperty(Symbol.prototype, Symbol.iterator, desc);
                   let result = Iterator.concat(
                     Object(true),
                     Object(123),
                     Object(123n),
                     Object("test"),
                     Object(Symbol())
                   ).next();
                   result && typeof result === "object" && "done" in result && "value" in result;
                   """);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Iterator_From_Caches_Next_Once_And_ToArray_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   let nextCalls = 0;
                   class CountingIterator {
                     get next() {
                       ++nextGets;
                       let iter = (function* () {
                         for (let i = 1; i < 5; ++i) {
                           yield i;
                         }
                       })();
                       return function() {
                         ++nextCalls;
                         return iter.next();
                       };
                     }
                   }
                   let iterator = Iterator.from(new CountingIterator());
                   let values = iterator.toArray();
                   nextGets + "|" + nextCalls + "|" + values.join(",");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|5|1,2,3,4"));
    }

    [Test]
    public void Iterator_From_Wraps_Direct_Iterator_With_Missing_Next_Without_Throwing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let calls = [];
                   let iter = new Proxy({
                     return() {
                       return { value: 5, done: true };
                     }
                   }, {
                     get(target, key, receiver) {
                       calls.push("get:" + String(key));
                       return Reflect.get(target, key, receiver);
                     }
                   });
                   let wrapper = Iterator.from(iter);
                   calls.join("|") + "|" + typeof wrapper.return;
                   """);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("get:Symbol(Symbol.iterator)|get:next|function"));
    }

    [Test]
    public void Iterator_Prototype_Constructor_Is_Accessor_With_Weird_Setter_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let IteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf([][Symbol.iterator]()));
                   let sentinel = {};
                   let desc = Object.getOwnPropertyDescriptor(Iterator.prototype, "constructor");
                   let out = [];
                   out.push(typeof desc.get, typeof desc.set, desc.enumerable, desc.configurable);
                   out.push(Iterator.prototype.constructor === Iterator);
                   try { desc.set.call(IteratorPrototype, ""); } catch (e) { out.push(e.name); }
                   let fake = Object.create(IteratorPrototype);
                   Object.freeze(IteratorPrototype);
                   fake.constructor = sentinel;
                   let o = { constructor: "x" };
                   desc.set.call(o, sentinel);
                   out.push(fake.constructor === sentinel);
                   out.push(o.constructor === sentinel);
                   out.join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("function|function|false|true|true|TypeError|true|true"));
    }

    [Test]
    public void Iterator_Drop_Validates_And_Closes_In_Observed_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let closed = false;
                   let closable = {
                     __proto__: Iterator.prototype,
                     get next() {
                       throw new Error("next should not be read");
                     },
                     return() {
                       closed = true;
                       return {};
                     },
                   };
                   let effects = [];
                   let rangeError = false;
                   try { Iterator.prototype.drop.call(closable); } catch (e) { rangeError = e.name === "RangeError"; }
                   let helper = Iterator.prototype.drop.call(
                     {
                       get next() {
                         effects.push("get next");
                         return function() { return { done: true, value: undefined }; };
                       },
                     },
                     {
                       valueOf() {
                         effects.push("ToNumber limit");
                         return 0;
                       },
                     }
                   );
                   let badNext = Iterator.prototype.drop.call({ next: 0 }, 1);
                   let badNextTypeError = false;
                   try { badNext.next(); } catch (e) { badNextTypeError = e.name === "TypeError"; }
                   [
                     rangeError,
                     closed,
                     effects.join(","),
                     typeof helper.next,
                     badNextTypeError
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("true|true|ToNumber limit,get next|function|true"));
    }

    [Test]
    public void Iterator_Every_Caches_Next_And_Closes_On_False()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   let returnCalls = 0;
                   class TestIterator extends Iterator {
                     get next() {
                       ++nextGets;
                       let counter = 3;
                       return function () {
                         return counter-- > 0
                           ? { done: false, value: counter + 1 }
                           : { done: true, value: undefined };
                       };
                     }
                     return() {
                       ++returnCalls;
                       return {};
                     }
                   }
                   let iterator = new TestIterator();
                   let result = iterator.every(v => v > 1);
                   [result, nextGets, returnCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("false|1|1"));
    }

    [Test]
    public void Iterator_Filter_Caches_Next_Once_And_Forwards_Return()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   let nextCalls = 0;
                   let returnCalls = 0;
                   class TestIterator extends Iterator {
                     get next() {
                       ++nextGets;
                       let iter = (function* () {
                         yield 1;
                         yield 2;
                         yield 3;
                       })();
                       return function() {
                         ++nextCalls;
                         return iter.next();
                       };
                     }
                     return() {
                       ++returnCalls;
                       return {};
                     }
                   }
                   let helper = new TestIterator().filter(v => v > 1);
                   let first = helper.next();
                   helper.return();
                   [first.value, first.done, nextGets, nextCalls, returnCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2|false|1|2|1"));
    }

    [Test]
    public void Iterator_Filter_Defers_NonCallable_Next_Failure_To_Helper_Advance()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let iter = Iterator.prototype.filter.call({ next: 0 }, () => true);
                   try {
                     iter.next();
                   } catch (e) {
                     e.name;
                   }
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError"));
    }

    [Test]
    public void Iterator_Find_Caches_Next_Once_And_Closes_On_Match()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   let returnCalls = 0;
                   class TestIterator extends Iterator {
                     get next() {
                       ++nextGets;
                       let counter = 0;
                       return function() {
                         return counter < 4
                           ? { done: false, value: counter++ }
                           : { done: true, value: undefined };
                       };
                     }
                     return() {
                       ++returnCalls;
                       return {};
                     }
                   }
                   let result = new TestIterator().find(v => v > 1);
                   [result, nextGets, returnCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2|1|1"));
    }

    [Test]
    public void Iterator_FlatMap_Flattens_One_Level_And_Forwards_Return()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let returnCalls = 0;
                   class TestIterator extends Iterator {
                     next() {
                       if (!this.i) this.i = 0;
                       return this.i < 3
                         ? { done: false, value: this.i++ }
                         : { done: true, value: undefined };
                     }
                     return() {
                       ++returnCalls;
                       return {};
                     }
                   }
                   let helper = new TestIterator().flatMap(v => [v, [v + 10]]);
                   let first = helper.next();
                   let second = helper.next();
                   helper.return();
                   [first.value, Array.isArray(second.value), second.value[0], returnCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0|true|10|1"));
    }

    [Test]
    public void Iterator_FlatMap_Rejects_Primitive_String_But_Flattens_String_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   function* g() { yield 0; }
                   let threw = false;
                   try {
                     Array.from(g().flatMap(v => "string"));
                   } catch (e) {
                     threw = e.name === "TypeError";
                   }
                   let values = Array.from(g().flatMap(v => new String("ok")));
                   [threw, values.join(",")].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|o,k"));
    }

    [Test]
    public void Iterator_ForEach_Uses_Default_Call_This_And_Yields_Value_And_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let records = [];
                   let expectedThis = function() { return this; }.call(undefined);
                   function* g() {
                     yield "a";
                     yield "b";
                   }
                   g().forEach(function(v, i) {
                     records.push(this === expectedThis, v, i);
                   });
                   records.join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|a|0|true|b|1"));
    }

    [Test]
    public void Iterator_Map_Caches_Next_Once_And_Maps_Lazily()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   let nextCalls = 0;
                   class CountingIterator extends Iterator {
                     get next() {
                       ++nextGets;
                       let iter = (function* () {
                         yield 1;
                         yield 2;
                       })();
                       return function() {
                         ++nextCalls;
                         return iter.next();
                       };
                     }
                   }
                   let helper = new CountingIterator().map((v, i) => v + i);
                   let first = helper.next();
                   let second = helper.next();
                   let done = helper.next();
                   [first.value, second.value, done.done, nextGets, nextCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|3|true|1|3"));
    }

    [Test]
    public void Iterator_Reduce_Caches_Next_Once_And_Uses_Initial_Value_Rules()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   class TestIterator extends Iterator {
                     get next() {
                       ++nextGets;
                       let counter = 0;
                       return function() {
                         return counter < 3
                           ? { done: false, value: ++counter }
                           : { done: true, value: undefined };
                       };
                     }
                   }
                   let a = new TestIterator().reduce((m, v, i) => m + ":" + v + ":" + i);
                   let b = new TestIterator().reduce((m, v, i) => m + v + i, 10);
                   [a, b, nextGets].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1:2:1:3:2|19|2"));
    }

    [Test]
    public void Iterator_Some_Caches_Next_Once_And_Closes_On_True()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let nextGets = 0;
                   let returnCalls = 0;
                   class TestIterator extends Iterator {
                     get next() {
                       ++nextGets;
                       let counter = 0;
                       return function() {
                         return counter < 4
                           ? { done: false, value: counter++ }
                           : { done: true, value: undefined };
                       };
                     }
                     return() {
                       ++returnCalls;
                       return {};
                     }
                   }
                   let result = new TestIterator().some(v => v > 1);
                   [result, nextGets, returnCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|1|1"));
    }

    [Test]
    public void Iterator_Take_Uses_Limit_And_Closes_When_Exhausted_By_Limit()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let returnCalls = 0;
                   class TestIterator extends Iterator {
                     next() {
                       if (!this.i) this.i = 0;
                       return { done: false, value: this.i++ };
                     }
                     return() {
                       ++returnCalls;
                       return {};
                     }
                   }
                   let values = Array.from(new TestIterator().take(3));
                   [values.join(","), returnCalls].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0,1,2|1"));
    }

    [Test]
    public void Iterator_Take_Zero_Limit_Surfaces_Underlying_Return_Throw()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   class ReturnCalledError extends Error {}
                   class ThrowingIterator extends Iterator {
                     next() {
                       return { done: false, value: 1 };
                     }
                     return() {
                       throw new ReturnCalledError();
                     }
                   }
                   let name = "";
                   let ctor = "";
                   try {
                     new ThrowingIterator().take(0).next();
                   } catch (e) {
                     name = e.name;
                     ctor = e.constructor.name;
                   }
                   [name, ctor].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("Error|ReturnCalledError"));
    }

    [Test]
    public void Iterator_Dispose_Invokes_Return_And_Returns_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let IteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf([][Symbol.iterator]()));
                   let iter = Object.create(IteratorPrototype);
                   let returnCalled = false;
                   iter.return = function () {
                     returnCalled = true;
                     return { done: true };
                   };
                   let rv = iter[Symbol.dispose]();
                   [typeof IteratorPrototype[Symbol.dispose], returnCalled, rv === undefined].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|true|true"));
    }

    [Test]
    public void Iterator_SymbolIterator_Returns_This_Value_And_ToStringTag_Is_Accessor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let IteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf([][Symbol.iterator]()));
                   let getIterator = IteratorPrototype[Symbol.iterator];
                   let values = [{}, Symbol(), 4, 4n, true, undefined, null];
                   let same = values.map(v => getIterator.call(v) === v).every(Boolean);
                   let desc = Object.getOwnPropertyDescriptor(Iterator.prototype, Symbol.toStringTag);
                   let sentinel = "x";
                   let fake = Object.create(IteratorPrototype);
                   Object.freeze(IteratorPrototype);
                   fake[Symbol.toStringTag] = sentinel;
                   [
                     same,
                     typeof desc.get,
                     typeof desc.set,
                     Iterator.prototype[Symbol.toStringTag],
                     fake[Symbol.toStringTag]
                   ].join("|");
                   """);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|function|function|Iterator|x"));
    }
}

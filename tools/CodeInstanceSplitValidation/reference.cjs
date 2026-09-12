'use strict';
// Reference behavior only: these results do not execute or validate Okojo.
const assert = require('node:assert/strict');
const vm = require('node:vm');
const results = [];
function check(name, source, expected, globals = {}) {
    const actual = new vm.Script(source).runInNewContext(globals);
    assert.equal(actual, expected, name);
    results.push({ name, result: actual, status: 'passed' });
}
check('fresh closure identity and captures', `
    function make(x) { return function add(y) { return x + y; }; }
    var first = make(1), second = make(10);
    [first(2), second(2), first === second,
     first.prototype === second.prototype].join(',');`, '3,12,false,false');
check('private brands, super and generators', `
    class Base { value() { return seed; } }
    function make() { return class C extends Base {
        #x = 4; value() { return super.value() + this.#x; }
    }; }
    var C1 = make(), C2 = make(), rejected = false;
    try { C1.prototype.value.call(new C2()); }
    catch (e) { rejected = e instanceof TypeError; }
    function* values() { yield new C1().value(); return 99; }
    var iterator = values();
    [iterator.next().value, iterator.next().value, rejected].join(',');`,
    '5,99,true', { seed: 1 });
check('tagged site identity and freezing', `
    function make() { return function get() { return (x => x)\`same\`; }; }
    var a = make(), b = make(), t1 = a(), t2 = b();
    t1 === t2 && Object.isFrozen(t1) && Object.isFrozen(t1.raw);`, true);
check('delete uses global environment, not globalThis property', `
    var target = this;
    this.victim = 7;
    globalThis = { victim: 99 };
    var removed = delete victim;
    removed && !('victim' in target) && globalThis.victim === 99;`, true);
check('delete lexical binding is false', 'let victim = 1; delete victim;', false);
const context = vm.createContext({});
new vm.Script('let conflict = 2;').runInContext(context);
assert.throws(() => new vm.Script('this.sideEffect = true; let conflict = 1;')
    .runInContext(context), error => error.name === 'SyntaxError');
assert.equal(context.sideEffect, undefined);
results.push({ name: 'declaration rejection precedes side effects', status: 'passed' });
console.log(JSON.stringify({
    scope: 'Node/V8 reference behavior only; Okojo was not executed',
    node: process.version, v8: process.versions.v8, cases: results
}, null, 2));

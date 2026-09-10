// No transpiler or npm dependencies required. Use "Okojo: DAP inspection".
let getterCalls = 0;
function inner(value) {
  const object = {
    value,
    nested: { answer: 42 },
    items: [10, 20, 30],
    get danger() {
      getterCalls++;
      throw new Error('The debugger should not execute this getter.');
    }
  };
  object.self = object;
  debugger;
  // Inspect object, object.nested.answer, and object.items[1].
  // Expand self: it is cyclic. danger appears as <accessor>, without evaluation.
  return object.value;
}
function caller(value) {
  const callerOnly = 99;
  return inner(value + 1) + callerOnly;
}
console.log('result:', caller(7));
console.log('getterCalls:', getterCalls);

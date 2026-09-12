# Dynamic Named Host Objects

## Scope

This iteration adds an engine-owned, public extension seam for host objects whose own
non-index string properties are resolved dynamically. It covers reads, writes, existence checks,
own-key enumeration, data-property descriptors, and ordinary prototype/receiver behavior. It
does not add a Proxy-based implementation, CSS-specific policy, indexed-property behavior, or
UiEngine changes.

`JsIndexedObject` derives from the new seam. A later HTML `element.style` implementation can
therefore expose CSS declaration order through indexed properties and supported CSS names through
dynamic named properties in one object, without placing CSS knowledge in the engine API.

## Minimal JavaScript Repros

```js
host.color = "red";
host.color;
"color" in host;
Object.keys(host);
Object.getOwnPropertyDescriptor(host, "color");
```

```js
const child = Object.create(host);
child.value = 2; // creates an own property when host exposes writable `value`
host.prototypeAccessor = 3; // inherited setter observes host as receiver
```

## Planned Tests

- `tests/Okojo.Tests/DynamicNamedHostObjectTests.cs`
  - arbitrary and missing reads/writes
  - `in`, `Object.keys`, and own descriptors
  - prototype methods and accessors, including child receivers
  - strict rejected assignment
  - symbols, numeric indices, and object-to-string keys

## Reference Observations

Node ordinary-object behavior is the runtime reference for the shared semantics: inherited
methods and accessors receive the original receiver, assigning through a writable prototype data
property creates an own property on the receiver, symbols remain distinct property keys, and a
failed assignment throws `TypeError` in strict mode. Okojo's keyed-property bytecode routes reads,
writes, `in`, and `Object.keys` through the existing object-model operations; the new seam belongs
below those operations rather than in the compiler or VM.

## Copy Versus Intentional Difference

Prototype, receiver, key coercion, descriptor, enumeration, and strict-assignment behavior copy
ordinary ECMAScript observable behavior. The intentional host boundary is that only non-index
string keys reach the dynamic hooks. Symbols and array-index keys remain ordinary Okojo object
properties, and dynamic properties are data descriptors rather than arbitrary Proxy traps.

## Performance Plan

Ordinary `JsObject` instances keep their current property path and field count. Only instances of
the specialized public subclass execute dynamic host callbacks. Dynamic accesses do not expose
inline-cache slots; named-property enumeration allocates scratch collections on the already-slow
own-key path. No VM opcode or compiler change is required.

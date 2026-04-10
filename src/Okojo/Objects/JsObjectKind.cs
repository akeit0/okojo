namespace Okojo.Objects;

// WIP
internal enum JsObjectKind : byte
{
    Ordinary,
    Array,
    Uint8Array,
    Int8Array,
    Uint16Array,
    Int16Array,
    Uint32Array,
    Int32Array,
    Float32Array,
    Float64Array,
    BigInt64Array,
    BigUint64Array,
    NormalBytecodeFunction,
    ArrowFunction,
    AsyncFunction,
    GeneratorFunction,
    AsyncGeneratorFunction,
    BoundFunction,
    ProxyFunction,
    ProxyObject,
    Error,
    GeneratorObject,
    AsyncFunctionObject,
    AsyncGeneratorObject,
    RegExp,
    Map,
    Set,
    WeakMap,
    WeakSet,
    WeakRef,
    FinalizationRegistry,
    DataView,
    ArrayBuffer,
    SharedArrayBuffer,

    Promise
    // TODO: more kinds.
}

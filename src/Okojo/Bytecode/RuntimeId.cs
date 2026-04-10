namespace Okojo.Bytecode;

internal enum RuntimeId : byte
{
    // Generator state / control.
    ThrowConstAssignError = 0,
    GeneratorGetResumeMode = 1,
    GeneratorClearResumeState = 2,


    // Iteration helpers.
    // Generic indexed-iteration fast-path probe for `for...of`.
    // Returns non-negative length when fast indexed iteration is available;
    // returns -1 to fall back to iterator protocol path.
    ForOfFastPathLength,

    // Delete keyed property/element. Args: [target, key]. Returns boolean.
    DeleteKeyedProperty,

    // Class / private-field / super helpers.
    // Define class prototype method with class semantics flags (writable+configurable, non-enumerable).
    DefineClassMethod,

    // Derived constructor super() call. Args: [arg0, arg1, ...].
    CallSuperConstructor,

    // Wire class heritage. Args: [ctor, superCtorOrNull].
    SetClassHeritage,

    // super property set. Args: [thisValue, key, value].
    SuperSet,

    // Derived default constructor super(...args) forward-all path.
    CallSuperConstructorForwardAll,

    // Define class prototype accessor with class semantics flags (configurable, non-enumerable).
    DefineClassAccessor,

    // Define class field (constructor/prototype target own data property).
    DefineClassField,

    // Define object literal accessor by runtime key (enumerable+configurable).
    DefineObjectAccessor,

    // Module helpers.
    GetCurrentModuleSetFunctionName,
    GetCurrentModuleImportMeta,

    // Property-key / object coercion helpers.
    // Normalize property key once (ToPropertyKey semantics) and return canonical primitive key.
    NormalizePropertyKey,

    // Throw TypeError when value is null/undefined; otherwise return input value.
    RequireObjectCoercible,

    // super / method-environment helpers.
    // Load super[key] using current frame super base and provided receiver+key.
    LoadKeyedFromSuper,

    // Resolve current function super base (homeObject.[[Prototype]]).
    GetCurrentFunctionSuperBase,

    // Resolve prototype for method-environment home object (acc input).
    GetObjectPrototypeForSuper,

    // delete super.x / delete super[x] always throws ReferenceError.
    ThrowDeleteSuperPropertyReference,

    // Literal / template helpers.
    // Create RegExp literal object from [pattern, flags] without going through mutable global RegExp binding.
    CreateRegExpLiteral,

    // Resolve/cached template literal site object for tagged templates.
    GetTemplateObject,

    // Destructuring helpers.
    // Destructure array-assignment pattern from iterable source. Args: [source, elementFlags0, ...].
    DestructureArrayAssignment,

    // Destructure array-assignment pattern into prepared member targets.
    // Args: [source, targetObj0, targetKey0, isComputed0, defaultThunk0, ...].
    DestructureArrayAssignmentMemberTargets,
    ThrowParameterInitializerTdz,

    // Spread/call/construct helpers.
    // Call with explicit this and spread-aware argument expansion. Args: [callee, thisValue, flags, arg0, ...].
    CallWithSpread,

    // Construct with spread-aware argument expansion. Args: [callee, flags, arg0, ...].
    ConstructWithSpread,

    // Derived constructor super(...args) with spread-aware argument expansion. Args: [flags, arg0, ...].
    CallSuperConstructorWithSpread,

    // Copy enumerable own properties from source onto target using CopyDataProperties-style define semantics.
    CopyDataProperties,

    // Append spread iterable values into array literal target. Args: [target, source, nextIndex].
    AppendArraySpread,

    // Strict delete keyed property/element. Args: [target, key]. Throws TypeError when delete returns false.
    DeleteKeyedPropertyStrict,

    // Iterator protocol / dynamic import helpers.
    // Throw TypeError for iterator protocol violations where an iterator result must be an object.
    ThrowIteratorResultNotObject,

    // Dynamic import expression. Args: [specifier]. Returns a promise.
    DynamicImport,

    // Object rest / function-name / class computed key helpers.
    // Copy enumerable own properties from source onto target excluding later arguments as property keys.
    CopyDataPropertiesExcluding,

    // Define a function/class name property and return the target.
    SetFunctionName,

    // Cache a precomputed computed-name key for deferred public instance field initialization.
    SetFunctionInstanceFieldKey,

    // Load a precomputed computed-name key from the current function.
    LoadCurrentFunctionInstanceFieldKey,

    // Async iteration / delegated yield* helpers.
    // Wrap a sync iterator result into an internal async-from-sync delegate iterator.
    WrapSyncIteratorForAsyncDelegate,

    // Load @@asyncIterator from an object using the intrinsic well-known symbol.
    GetAsyncIteratorMethod,

    // Load @@iterator from an object using the intrinsic well-known symbol.
    GetIteratorMethod,

    // Function/class private brand helper cache.
    // Set a bytecode function closure's private brand token from a class prototype/constructor source object.
    SetFunctionPrivateBrandToken,

    // Cache a precomputed private method closure on a class constructor function.
    SetFunctionPrivateMethodValue,

    // Load a precomputed private method closure from the current constructor function.
    LoadCurrentFunctionPrivateMethodValue,

    // Set an explicit private brand mapping for one compiled private brand id on a closure.
    SetFunctionPrivateBrandMapping,

    // Set an explicit private brand mapping using the exact source object as the token.
    SetFunctionPrivateBrandMappingExact,

    CreateRestParameterFromArrayLike,

    // Close an async iterator for for-await loop abrupt completion.
    AsyncIteratorClose,

    // Create a sync iterator for array destructuring without consuming it.
    CreateArrayDestructureIterator,

    // Step a destructuring iterator and return next value or TheHole when done.
    DestructureIteratorStepValue,

    // Perform IteratorClose for sync destructuring iterators.
    DestructureIteratorClose,

    // Best-effort IteratorClose used while preserving an original throw completion.
    DestructureIteratorCloseBestEffort,

    // Drain remaining iterator values into a new array for array-rest destructuring.
    DestructureIteratorRestArray,

    // Close an async iterator while preserving an original throw completion.
    AsyncIteratorCloseBestEffort,


    // Attach a method-environment parent context (homeObject + optional class lexical binding) to a closure.
    SetFunctionMethodEnvironment,

    // Private name presence check used by `#x in value`.
    HasPrivateField,

    // Class helpers.
    ClassGetPrototypeAndSetConstructor,

    // Generator delegation helpers.
    GeneratorHasActiveDelegateIterator
}

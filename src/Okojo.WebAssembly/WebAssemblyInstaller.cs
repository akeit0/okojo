using System.Buffers;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebAssembly;

internal static class WebAssemblyInstaller
{
    private const JsShapePropertyFlags InternalPropertyFlags = JsShapePropertyFlags.None;

    private const JsShapePropertyFlags BuiltinDataPropertyFlags =
        JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable;

    public static void Install(JsRealm realm, IWasmBackend backend)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(backend);

        var bindings = CreateBindings(realm, backend);
        var webAssembly = CreateWebAssemblyObject(realm, bindings);
        if (!realm.Global.TryGetValue("WebAssembly", out _))
            realm.Global["WebAssembly"] = JsValue.FromObject(webAssembly);
    }

    private static JsPlainObject CreateWebAssemblyObject(JsRealm realm, WebAssemblyBindings bindings)
    {
        var obj = new JsPlainObject(realm);

        var validate = new JsHostFunction(realm, (scoped in info) =>
        {
            var wasm = ReadWasmBytes(info.Realm, info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined);
            return bindings.Backend.Validate(wasm) ? JsValue.True : JsValue.False;
        }, "validate", 1, false);

        var compile = new JsHostFunction(realm, (scoped in info) =>
        {
            try
            {
                var wasm = ReadWasmBytes(info.Realm, info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined);
                var compiled = bindings.Backend.Compile(wasm);
                var module = CreateModuleObject(info.Realm, bindings, bindings.Backend, compiled);
                return info.Realm.PromiseResolveValue(JsValue.FromObject(module));
            }
            catch (Exception ex)
            {
                return CreateRejectedPromise(info.Realm, bindings, ex);
            }
        }, "compile", 1);

        var instantiate = new JsHostFunction(realm, (scoped in info) =>
        {
            try
            {
                var args = info.Arguments;
                if (args.Length == 0)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "WebAssembly.instantiate requires a module or bytes");

                var imports = args.Length > 1 ? args[1] : JsValue.Undefined;
                if (TryGetModuleHandle(args[0], out var moduleHandle))
                {
                    var instance = moduleHandle.Backend.Instantiate(
                        moduleHandle.CompiledModule,
                        new JsObjectWasmImportResolver(info.Realm, moduleHandle.Backend, bindings, imports));
                    var instanceObject = CreateInstanceObject(info.Realm, bindings, instance);
                    return info.Realm.PromiseResolveValue(JsValue.FromObject(instanceObject));
                }

                var wasm = ReadWasmBytes(info.Realm, args[0]);
                var compiled = bindings.Backend.Compile(wasm);
                var moduleObject = CreateModuleObject(info.Realm, bindings, bindings.Backend, compiled);
                var moduleValue = JsValue.FromObject(moduleObject);
                var compiledInstance = bindings.Backend.Instantiate(
                    compiled,
                    new JsObjectWasmImportResolver(info.Realm, bindings.Backend, bindings, imports));
                var createdInstanceObject = CreateInstanceObject(info.Realm, bindings, compiledInstance);

                var result = new JsPlainObject(info.Realm);
                result.DefineDataProperty("module", moduleValue, JsShapePropertyFlags.Open);
                result.DefineDataProperty("instance", JsValue.FromObject(createdInstanceObject),
                    JsShapePropertyFlags.Open);
                return info.Realm.PromiseResolveValue(JsValue.FromObject(result));
            }
            catch (Exception ex)
            {
                return CreateRejectedPromise(info.Realm, bindings, ex);
            }
        }, "instantiate", 1);

        obj.DefineDataProperty("validate", JsValue.FromObject(validate), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("compile", JsValue.FromObject(compile), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("instantiate", JsValue.FromObject(instantiate), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("Module", JsValue.FromObject(bindings.ModuleConstructor), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("Instance", JsValue.FromObject(bindings.InstanceConstructor), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("Memory", JsValue.FromObject(bindings.MemoryConstructor), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("Table", JsValue.FromObject(bindings.TableConstructor), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("Global", JsValue.FromObject(bindings.GlobalConstructor), JsShapePropertyFlags.Open);
        obj.DefineDataProperty("RuntimeError", JsValue.FromObject(bindings.RuntimeErrorConstructor),
            JsShapePropertyFlags.Open);
        obj.DefineDataProperty("CompileError", JsValue.FromObject(bindings.CompileErrorConstructor),
            JsShapePropertyFlags.Open);
        obj.DefineDataProperty("LinkError", JsValue.FromObject(bindings.LinkErrorConstructor),
            JsShapePropertyFlags.Open);
        return obj;
    }

    private static WebAssemblyBindings CreateBindings(JsRealm realm, IWasmBackend backend)
    {
        var runtimeErrorPrototype = CreateNativeErrorPrototype(realm, "RuntimeError");
        var compileErrorPrototype = CreateNativeErrorPrototype(realm, "CompileError");
        var linkErrorPrototype = CreateNativeErrorPrototype(realm, "LinkError");

        var runtimeErrorConstructor =
            realm.Intrinsics.CreateNativeErrorConstructor("RuntimeError", runtimeErrorPrototype);
        var compileErrorConstructor =
            realm.Intrinsics.CreateNativeErrorConstructor("CompileError", compileErrorPrototype);
        var linkErrorConstructor = realm.Intrinsics.CreateNativeErrorConstructor("LinkError", linkErrorPrototype);

        InitializeNativeErrorConstructor(realm, runtimeErrorConstructor, runtimeErrorPrototype);
        InitializeNativeErrorConstructor(realm, compileErrorConstructor, compileErrorPrototype);
        InitializeNativeErrorConstructor(realm, linkErrorConstructor, linkErrorPrototype);

        var modulePrototype = CreatePrototypeObject(realm);
        var instancePrototype = CreatePrototypeObject(realm);
        var memoryPrototype = CreatePrototypeObject(realm);
        var tablePrototype = CreatePrototypeObject(realm);
        var globalPrototype = CreatePrototypeObject(realm);

        var moduleConstructor = new JsHostFunction(realm, (scoped in info) =>
        {
            var wasm = ReadWasmBytes(info.Realm, info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined);
            var compiled = backend.Compile(wasm);
            return JsValue.FromObject(CreateModuleObject(info.Realm, null, backend, compiled, modulePrototype));
        }, "Module", 1, true);

        var instanceConstructor = new JsHostFunction(realm, (scoped in info) =>
        {
            if (info.Arguments.Length == 0)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly.Instance requires a module");

            if (!TryGetModuleHandle(info.Arguments[0], out var moduleHandle))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "WebAssembly.Instance requires a WebAssembly.Module");

            var imports = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            var instance = moduleHandle.Backend.Instantiate(
                moduleHandle.CompiledModule,
                new JsObjectWasmImportResolver(info.Realm, moduleHandle.Backend, null, imports));
            return JsValue.FromObject(CreateInstanceObject(info.Realm, null, instance, instancePrototype));
        }, "Instance", 1, true);

        var memoryConstructor = new JsHostFunction(realm, (scoped in info) =>
        {
            var type = ReadMemoryType(info.Realm, info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined);
            var memory = backend.CreateMemory(type);
            return JsValue.FromObject(CreateMemoryObject(info.Realm, null, memory, memoryPrototype));
        }, "Memory", 1, true);

        var tableConstructor = new JsHostFunction(realm, (scoped in info) =>
        {
            var type = ReadTableType(info.Realm, info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined);
            var initialValue = info.Arguments.Length > 1 ? ConvertJsToExternRef(info.Arguments[1]) : null;
            var table = backend.CreateTable(type, initialValue);
            return JsValue.FromObject(CreateTableObject(info.Realm, null, table, tablePrototype));
        }, "Table", 1, true);

        var globalConstructor = new JsHostFunction(realm, (scoped in info) =>
        {
            var type = ReadGlobalType(info.Realm, info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined);
            var initialValue = info.Arguments.Length > 1
                ? ConvertJsToWasmValue(info.Realm, type.ValueKind, info.Arguments[1])
                : GetDefaultValue(type.ValueKind);
            var global = backend.CreateGlobal(type, initialValue);
            return JsValue.FromObject(CreateGlobalObject(info.Realm, null, global, globalPrototype));
        }, "Global", 1, true);

        var bindings = new WebAssemblyBindings(
            backend,
            runtimeErrorConstructor,
            compileErrorConstructor,
            linkErrorConstructor,
            moduleConstructor,
            instanceConstructor,
            memoryConstructor,
            tableConstructor,
            globalConstructor,
            modulePrototype,
            instancePrototype,
            memoryPrototype,
            tablePrototype,
            globalPrototype);

        moduleConstructor.UserData = bindings;
        instanceConstructor.UserData = bindings;
        memoryConstructor.UserData = bindings;
        tableConstructor.UserData = bindings;
        globalConstructor.UserData = bindings;

        InitializePrototypeConstructorPair(realm, moduleConstructor, modulePrototype, "Module");
        InitializePrototypeConstructorPair(realm, instanceConstructor, instancePrototype, "Instance");
        InitializePrototypeConstructorPair(realm, memoryConstructor, memoryPrototype, "Memory");
        InitializePrototypeConstructorPair(realm, tableConstructor, tablePrototype, "Table");
        InitializePrototypeConstructorPair(realm, globalConstructor, globalPrototype, "Global");

        InstallMemoryPrototypeMembers(realm, bindings);
        InstallTablePrototypeMembers(realm, bindings);
        InstallGlobalPrototypeMembers(realm, bindings);

        return bindings;
    }

    private static JsPlainObject CreateNativeErrorPrototype(JsRealm realm, string name)
    {
        var prototype = new JsPlainObject(realm, false)
        {
            Prototype = realm.Intrinsics.ErrorPrototype
        };
        prototype.DefineDataProperty("name", JsValue.FromString(name), BuiltinDataPropertyFlags);
        prototype.DefineDataProperty("message", JsValue.FromString(string.Empty), BuiltinDataPropertyFlags);
        return prototype;
    }

    private static void InitializeNativeErrorConstructor(
        JsRealm realm,
        JsHostFunction constructor,
        JsPlainObject prototype)
    {
        constructor.Prototype = realm.Intrinsics.ErrorConstructor;
        prototype.DefineDataProperty("constructor", JsValue.FromObject(constructor), BuiltinDataPropertyFlags);
        constructor.InitializePrototypeProperty(prototype);
    }

    private static JsPlainObject CreatePrototypeObject(JsRealm realm)
    {
        return new(realm, false)
        {
            Prototype = realm.ObjectPrototype
        };
    }

    private static void InitializePrototypeConstructorPair(
        JsRealm realm,
        JsHostFunction constructor,
        JsPlainObject prototype,
        string name)
    {
        prototype.DefineDataProperty("constructor", JsValue.FromObject(constructor), BuiltinDataPropertyFlags);
        prototype.DefineDataProperty("name", JsValue.FromString(name), BuiltinDataPropertyFlags);
        constructor.InitializePrototypeProperty(prototype);
    }

    private static void InstallMemoryPrototypeMembers(JsRealm realm, WebAssemblyBindings bindings)
    {
        var bufferGetter = new JsHostFunction(realm, (scoped in info) =>
        {
            var memory = RequireMemory(info.ThisValue);
            return JsValue.FromObject(memory.WrapArrayBuffer(info.Realm));
        }, "get buffer", 0);

        var grow = new JsHostFunction(realm, (scoped in info) =>
        {
            var memory = RequireMemory(info.ThisValue);
            var delta = info.Arguments.Length == 0
                ? 0
                : checked((long)info.Realm.ToIntegerOrInfinity(info.Arguments[0]));
            return new(memory.Grow(delta));
        }, "grow", 1);

        bindings.MemoryPrototype.DefineAccessorProperty("buffer", bufferGetter, null,
            JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.Configurable);
        bindings.MemoryPrototype.DefineDataProperty("grow", JsValue.FromObject(grow), BuiltinDataPropertyFlags);
    }

    private static void InstallTablePrototypeMembers(JsRealm realm, WebAssemblyBindings bindings)
    {
        var lengthGetter = new JsHostFunction(realm, (scoped in info) =>
        {
            var table = RequireTable(info.ThisValue);
            return new((double)table.Length);
        }, "get length", 0);

        var get = new JsHostFunction(realm, (scoped in info) =>
        {
            var table = RequireTable(info.ThisValue);
            var index = info.Arguments.Length == 0 ? 0u : info.Realm.ToUint32(info.Arguments[0]);
            return ConvertTableElement(info.Realm, table.GetElement(index));
        }, "get", 1);

        var set = new JsHostFunction(realm, (scoped in info) =>
        {
            var table = RequireTable(info.ThisValue);
            var index = info.Arguments.Length == 0 ? 0u : info.Realm.ToUint32(info.Arguments[0]);
            var value = info.Arguments.Length > 1 ? ConvertJsToExternRef(info.Arguments[1]) : null;
            table.SetElement(index, value);
            return JsValue.Undefined;
        }, "set", 2);

        var grow = new JsHostFunction(realm, (scoped in info) =>
        {
            var table = RequireTable(info.ThisValue);
            var delta = info.Arguments.Length == 0 ? 0u : info.Realm.ToUint32(info.Arguments[0]);
            var initialValue = info.Arguments.Length > 1 ? ConvertJsToExternRef(info.Arguments[1]) : null;
            return new((double)table.Grow(delta, initialValue));
        }, "grow", 1);

        bindings.TablePrototype.DefineAccessorProperty("length", lengthGetter, null,
            JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.Configurable);
        bindings.TablePrototype.DefineDataProperty("get", JsValue.FromObject(get), BuiltinDataPropertyFlags);
        bindings.TablePrototype.DefineDataProperty("set", JsValue.FromObject(set), BuiltinDataPropertyFlags);
        bindings.TablePrototype.DefineDataProperty("grow", JsValue.FromObject(grow), BuiltinDataPropertyFlags);
    }

    private static void InstallGlobalPrototypeMembers(JsRealm realm, WebAssemblyBindings bindings)
    {
        var valueGetter = new JsHostFunction(realm, (scoped in info) =>
        {
            var global = RequireGlobal(info.ThisValue);
            return ConvertValue(info.Realm, global.GetValue());
        }, "get value", 0);

        var valueSetter = new JsHostFunction(realm, (scoped in info) =>
        {
            var global = RequireGlobal(info.ThisValue);
            var arg = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            global.SetValue(ConvertJsToWasmValue(info.Realm, global.Type.ValueKind, arg));
            return JsValue.Undefined;
        }, "set value", 1);

        bindings.GlobalPrototype.DefineAccessorProperty("value", valueGetter, valueSetter,
            JsShapePropertyFlags.BothAccessor | JsShapePropertyFlags.Configurable);
    }

    private static JsPlainObject CreateModuleObject(
        JsRealm realm,
        WebAssemblyBindings? bindings,
        IWasmBackend backend,
        IWasmCompiledModule compiled,
        JsPlainObject? explicitPrototype = null)
    {
        var prototype = explicitPrototype ?? bindings!.ModulePrototype;
        var obj = new JsPlainObject(realm, false)
        {
            Prototype = prototype
        };
        obj.DefineDataProperty("__jsWasmBackend", JsValue.FromObject(new JsBoxObject(realm, backend)),
            InternalPropertyFlags);
        obj.DefineDataProperty("__jsWasmCompiledModule", JsValue.FromObject(new JsBoxObject(realm, compiled)),
            InternalPropertyFlags);
        return obj;
    }

    private static JsPlainObject CreateInstanceObject(
        JsRealm realm,
        WebAssemblyBindings? bindings,
        IWasmInstance instance,
        JsPlainObject? explicitPrototype = null)
    {
        var prototype = explicitPrototype ?? bindings!.InstancePrototype;
        var obj = new JsPlainObject(realm, false)
        {
            Prototype = prototype
        };
        obj.DefineDataProperty("__jsWasmInstance", JsValue.FromObject(new JsBoxObject(realm, instance)),
            InternalPropertyFlags);

        var exports = new JsPlainObject(realm);
        foreach (var pair in instance.Exports)
            exports.DefineDataProperty(pair.Key, ConvertExtern(realm, bindings, pair.Value), JsShapePropertyFlags.Open);

        obj.DefineDataProperty("exports", JsValue.FromObject(exports), JsShapePropertyFlags.Open);
        return obj;
    }

    private static JsPlainObject CreateMemoryObject(
        JsRealm realm,
        WebAssemblyBindings? bindings,
        IWasmMemory memory,
        JsPlainObject? explicitPrototype = null)
    {
        var prototype = explicitPrototype ?? bindings!.MemoryPrototype;
        var obj = new JsPlainObject(realm, false)
        {
            Prototype = prototype
        };
        obj.DefineDataProperty("__jsWasmMemory", JsValue.FromObject(new JsBoxObject(realm, memory)),
            InternalPropertyFlags);
        return obj;
    }

    private static JsPlainObject CreateTableObject(
        JsRealm realm,
        WebAssemblyBindings? bindings,
        IWasmTable table,
        JsPlainObject? explicitPrototype = null)
    {
        var prototype = explicitPrototype ?? bindings!.TablePrototype;
        var obj = new JsPlainObject(realm, false)
        {
            Prototype = prototype
        };
        obj.DefineDataProperty("__jsWasmTable", JsValue.FromObject(new JsBoxObject(realm, table)),
            InternalPropertyFlags);
        return obj;
    }

    private static JsPlainObject CreateGlobalObject(
        JsRealm realm,
        WebAssemblyBindings? bindings,
        IWasmGlobal global,
        JsPlainObject? explicitPrototype = null)
    {
        var prototype = explicitPrototype ?? bindings!.GlobalPrototype;
        var obj = new JsPlainObject(realm, false)
        {
            Prototype = prototype
        };
        obj.DefineDataProperty("__jsWasmGlobal", JsValue.FromObject(new JsBoxObject(realm, global)),
            InternalPropertyFlags);
        return obj;
    }

    private static JsValue ConvertExtern(JsRealm realm, WebAssemblyBindings? bindings, IWasmExtern wasmExtern)
    {
        return wasmExtern switch
        {
            IWasmMemory memory => JsValue.FromObject(CreateMemoryObject(realm, bindings, memory)),
            IWasmGlobal global => JsValue.FromObject(CreateGlobalObject(realm, bindings, global)),
            IWasmFunction function => JsValue.FromObject(CreateExportedFunction(realm, function)),
            IWasmTable table => JsValue.FromObject(CreateTableObject(realm, bindings, table)),
            _ => throw new NotSupportedException($"Unsupported wasm extern type: {wasmExtern.GetType().FullName}")
        };
    }

    private static JsHostFunction CreateExportedFunction(JsRealm realm, IWasmFunction function)
    {
        return new(realm, (scoped in info) =>
        {
            WasmValue[]? rentedArgs = null;
            WasmValue[]? rentedResults = null;
            var wasmArgs = function.Type.ParameterCount == 0
                ? Array.Empty<WasmValue>()
                : rentedArgs = ArrayPool<WasmValue>.Shared.Rent(function.Type.ParameterCount);
            var wasmResults = function.Type.ResultCount == 0
                ? Array.Empty<WasmValue>()
                : rentedResults = ArrayPool<WasmValue>.Shared.Rent(function.Type.ResultCount);

            try
            {
                for (var i = 0; i < function.Type.ParameterCount; i++)
                    wasmArgs[i] = ConvertJsToWasmValue(info.Realm, function.Type.Parameters[i],
                        i < info.Arguments.Length ? info.Arguments[i] : JsValue.Undefined);

                function.Invoke(
                    wasmArgs.AsSpan(0, function.Type.ParameterCount),
                    wasmResults.AsSpan(0, function.Type.ResultCount));

                return function.Type.ResultCount switch
                {
                    0 => JsValue.Undefined,
                    1 => ConvertValue(info.Realm, wasmResults[0]),
                    _ => JsValue.FromObject(CreateMultiValueResult(info.Realm,
                        wasmResults.AsSpan(0, function.Type.ResultCount)))
                };
            }
            finally
            {
                if (rentedArgs is not null)
                    ArrayPool<WasmValue>.Shared.Return(rentedArgs, true);
                if (rentedResults is not null)
                    ArrayPool<WasmValue>.Shared.Return(rentedResults, true);
            }
        }, "wasm", function.Type.Parameters.Count);
    }

    private static JsArray CreateMultiValueResult(JsRealm realm, ReadOnlySpan<WasmValue> values)
    {
        var result = new JsArray(realm);
        for (uint i = 0; i < values.Length; i++)
            result.SetElement(i, ConvertValue(realm, values[(int)i]));
        return result;
    }

    private static JsValue ConvertValue(JsRealm realm, in WasmValue value)
    {
        return value.Kind switch
        {
            WasmValueKind.Int32 => JsValue.FromInt32(value.Int32Value),
            WasmValueKind.Int64 => new(value.Int64Value),
            WasmValueKind.Float32 => new(value.Float32Value),
            WasmValueKind.Float64 => new(value.Float64Value),
            WasmValueKind.ExternRef => ConvertExternRef(realm, value.ObjectValue),
            WasmValueKind.FuncRef => value.ObjectValue is IWasmFunction fn
                ? JsValue.FromObject(CreateExportedFunction(realm, fn))
                : JsValue.Null,
            WasmValueKind.V128 => JsValue.FromString(value.ObjectValue?.ToString() ?? string.Empty),
            _ => throw new NotSupportedException($"Unsupported wasm value kind: {value.Kind}")
        };
    }

    private static JsValue ConvertExternRef(JsRealm realm, object? value)
    {
        return value switch
        {
            null => JsValue.Null,
            JsValue jsValue => jsValue,
            JsObject jsObject => JsValue.FromObject(jsObject),
            string str => JsValue.FromString(str),
            bool b => b ? JsValue.True : JsValue.False,
            int i => JsValue.FromInt32(i),
            long l => new(l),
            float f => new(f),
            double d => new(d),
            _ => JsValue.FromObject(new JsBoxObject(realm, value))
        };
    }

    private static object? ConvertJsToExternRef(in JsValue value)
    {
        if (value.IsNullOrUndefined)
            return null;
        return value.IsObject ? value.AsObject() : value.Obj ?? value.ToString();
    }

    private static WasmValue ConvertJsToWasmValue(JsRealm realm, WasmValueKind kind, in JsValue value)
    {
        return kind switch
        {
            WasmValueKind.Int32 => WasmValue.FromInt32(unchecked((int)realm.ToIntegerOrInfinity(value))),
            WasmValueKind.Int64 => WasmValue.FromInt64(unchecked((long)realm.ToIntegerOrInfinity(value))),
            WasmValueKind.Float32 => WasmValue.FromFloat32((float)realm.ToNumber(value)),
            WasmValueKind.Float64 => WasmValue.FromFloat64(realm.ToNumber(value)),
            WasmValueKind.ExternRef => WasmValue.FromExternRef(ConvertJsToExternRef(value)),
            WasmValueKind.FuncRef => value.TryGetObject(out var obj) && obj is JsFunction fn
                ? new(WasmValueKind.FuncRef, fn)
                : new WasmValue(WasmValueKind.FuncRef, null),
            WasmValueKind.V128 => throw new JsRuntimeException(JsErrorKind.TypeError,
                "WebAssembly V128 interop is not implemented"),
            _ => throw new NotSupportedException($"Unsupported wasm value kind: {kind}")
        };
    }

    private static WasmValue GetDefaultValue(WasmValueKind kind)
    {
        return kind switch
        {
            WasmValueKind.Int32 => WasmValue.FromInt32(0),
            WasmValueKind.Int64 => WasmValue.FromInt64(0),
            WasmValueKind.Float32 => WasmValue.FromFloat32(0),
            WasmValueKind.Float64 => WasmValue.FromFloat64(0),
            WasmValueKind.FuncRef => new(WasmValueKind.FuncRef, null),
            WasmValueKind.ExternRef => WasmValue.FromExternRef(null),
            WasmValueKind.V128 => throw new JsRuntimeException(JsErrorKind.TypeError,
                "WebAssembly V128 globals are not implemented"),
            _ => throw new NotSupportedException($"Unsupported wasm value kind: {kind}")
        };
    }

    private static byte[] ReadWasmBytes(JsRealm realm, in JsValue value)
    {
        if (!value.TryGetObject(out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "WebAssembly bytes must be an ArrayBuffer or ArrayBufferView");

        return obj switch
        {
            JsArrayBufferObject buffer => CopyBufferBytes(buffer, 0, buffer.ByteLength),
            JsTypedArrayObject view => CopyBufferBytes(view.Buffer, view.ByteOffset, view.ByteLength),
            JsDataViewObject dataView => CopyBufferBytes(dataView.Buffer, dataView.ByteOffset, dataView.ByteLength),
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                "WebAssembly bytes must be an ArrayBuffer or ArrayBufferView")
        };
    }

    private static byte[] CopyBufferBytes(JsArrayBufferObject buffer, uint byteOffset, uint byteLength)
    {
        var copy = new byte[byteLength];
        for (uint i = 0; i < byteLength; i++)
            copy[i] = buffer.GetByte(byteOffset + i);
        return copy;
    }

    private static WasmMemoryType ReadMemoryType(JsRealm realm, in JsValue descriptorValue)
    {
        if (!descriptorValue.TryGetObject(out var descriptor))
            throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly.Memory descriptor must be an object");

        var initial = ReadRequiredLongProperty(realm, descriptor, "initial",
            "WebAssembly.Memory descriptor.initial is required");
        var maximum = ReadOptionalLongProperty(realm, descriptor, "maximum");
        var shared = ReadOptionalBooleanProperty(descriptor, "shared");
        return new(initial, maximum, shared);
    }

    private static WasmTableType ReadTableType(JsRealm realm, in JsValue descriptorValue)
    {
        if (!descriptorValue.TryGetObject(out var descriptor))
            throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly.Table descriptor must be an object");

        if (!descriptor.TryGetProperty("element", out var elementValue))
            throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly.Table descriptor.element is required");

        var element = realm.ToJsStringSlowPath(elementValue);
        var kind = element switch
        {
            "anyfunc" or "funcref" => WasmValueKind.FuncRef,
            "externref" => WasmValueKind.ExternRef,
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Unsupported WebAssembly.Table element type '{element}'")
        };

        var initial = checked((uint)ReadRequiredLongProperty(realm, descriptor, "initial",
            "WebAssembly.Table descriptor.initial is required"));
        var maximumLong = ReadOptionalLongProperty(realm, descriptor, "maximum");
        uint? maximum = maximumLong.HasValue ? checked((uint)maximumLong.Value) : null;
        return new(kind, initial, maximum);
    }

    private static WasmGlobalType ReadGlobalType(JsRealm realm, in JsValue descriptorValue)
    {
        if (!descriptorValue.TryGetObject(out var descriptor))
            throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly.Global descriptor must be an object");

        if (!descriptor.TryGetProperty("value", out var valueType))
            throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly.Global descriptor.value is required");

        var valueKind = realm.ToJsStringSlowPath(valueType);
        var mutable = ReadOptionalBooleanProperty(descriptor, "mutable");
        return new(ParseValueKind(valueKind), mutable ? WasmMutability.Var : WasmMutability.Const);
    }

    private static WasmValueKind ParseValueKind(string valueKind)
    {
        return valueKind switch
        {
            "i32" => WasmValueKind.Int32,
            "i64" => WasmValueKind.Int64,
            "f32" => WasmValueKind.Float32,
            "f64" => WasmValueKind.Float64,
            "externref" => WasmValueKind.ExternRef,
            "funcref" => WasmValueKind.FuncRef,
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Unsupported WebAssembly value type '{valueKind}'")
        };
    }

    private static long ReadRequiredLongProperty(JsRealm realm, JsObject obj, string name, string error)
    {
        if (!obj.TryGetProperty(name, out var value))
            throw new JsRuntimeException(JsErrorKind.TypeError, error);

        var result = checked((long)realm.ToIntegerOrInfinity(value));
        if (result < 0)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"{name} must be non-negative");
        return result;
    }

    private static long? ReadOptionalLongProperty(JsRealm realm, JsObject obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.IsUndefined)
            return null;

        var result = checked((long)realm.ToIntegerOrInfinity(value));
        if (result < 0)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"{name} must be non-negative");
        return result;
    }

    private static bool ReadOptionalBooleanProperty(JsObject obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.IsTrue;
    }

    private static bool TryGetModuleHandle(in JsValue value, out WasmModuleHandle handle)
    {
        handle = default;
        if (!value.TryGetObject(out var obj))
            return false;
        if (!TryGetBoxedValue<IWasmBackend>(obj, "__jsWasmBackend", out var backend) ||
            !TryGetBoxedValue<IWasmCompiledModule>(obj, "__jsWasmCompiledModule", out var compiled))
            return false;

        handle = new(backend, compiled);
        return true;
    }

    private static bool TryGetMemory(in JsValue value, out IWasmMemory memory)
    {
        return TryGetBoxedExtern(value, "__jsWasmMemory", out memory);
    }

    private static bool TryGetTable(in JsValue value, out IWasmTable table)
    {
        return TryGetBoxedExtern(value, "__jsWasmTable", out table);
    }

    private static bool TryGetGlobal(in JsValue value, out IWasmGlobal global)
    {
        return TryGetBoxedExtern(value, "__jsWasmGlobal", out global);
    }

    private static IWasmMemory RequireMemory(in JsValue value)
    {
        if (!TryGetMemory(value, out var memory))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Receiver is not a WebAssembly.Memory");
        return memory;
    }

    private static IWasmTable RequireTable(in JsValue value)
    {
        if (!TryGetTable(value, out var table))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Receiver is not a WebAssembly.Table");
        return table;
    }

    private static IWasmGlobal RequireGlobal(in JsValue value)
    {
        if (!TryGetGlobal(value, out var global))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Receiver is not a WebAssembly.Global");
        return global;
    }

    private static bool TryGetBoxedExtern<T>(in JsValue value, string propertyName, out T wasmExtern)
        where T : class, IWasmExtern
    {
        wasmExtern = null!;
        if (!value.TryGetObject(out var obj))
            return false;
        return TryGetBoxedValue(obj, propertyName, out wasmExtern);
    }

    private static bool TryGetBoxedValue<T>(JsObject obj, string propertyName, out T value)
        where T : class
    {
        value = null!;
        if (!obj.TryGetProperty(propertyName, out var boxedValue) ||
            !boxedValue.TryGetObject(out var boxedObject) ||
            boxedObject is not JsBoxObject box ||
            box.Value is not T typed)
            return false;

        value = typed;
        return true;
    }

    private static JsValue ConvertTableElement(JsRealm realm, object? value)
    {
        return value switch
        {
            null => JsValue.Null,
            JsValue jsValue => jsValue,
            JsObject jsObject => JsValue.FromObject(jsObject),
            IWasmFunction function => JsValue.FromObject(CreateExportedFunction(realm, function)),
            _ => JsValue.FromObject(new JsBoxObject(realm, value))
        };
    }

    private static JsValue CreateRejectedPromise(JsRealm realm, WebAssemblyBindings bindings, Exception ex)
    {
        var reason = MapExceptionToReason(realm, bindings, ex);
        return realm.PromiseRejectByConstructor(realm.Intrinsics.PromiseConstructor, reason);
    }

    private static JsValue MapExceptionToReason(JsRealm realm, WebAssemblyBindings bindings, Exception ex)
    {
        if (ex is JsRuntimeException jsRuntimeException)
            return jsRuntimeException.ThrownValue ?? realm.CreateErrorObjectFromException(jsRuntimeException);

        if (ex is WasmCompileException compileException)
            return CreateErrorValue(realm, bindings.CompileErrorConstructor, compileException.Message);

        if (ex is WasmLinkException linkException)
            return CreateErrorValue(realm, bindings.LinkErrorConstructor, linkException.Message);

        if (ex is WasmRuntimeTrapException trapException)
            return CreateErrorValue(realm, bindings.RuntimeErrorConstructor, trapException.Message);

        return CreateErrorValue(realm, bindings.RuntimeErrorConstructor, ex.Message);
    }

    private static JsValue CreateErrorValue(JsRealm realm, JsFunction constructor, string message)
    {
        var messageValue = JsValue.FromString(message);
        return realm.InvokeFunction(constructor, JsValue.Undefined, [messageValue]);
    }

    private readonly record struct WasmModuleHandle(IWasmBackend Backend, IWasmCompiledModule CompiledModule);

    private sealed record WebAssemblyBindings(
        IWasmBackend Backend,
        JsHostFunction RuntimeErrorConstructor,
        JsHostFunction CompileErrorConstructor,
        JsHostFunction LinkErrorConstructor,
        JsHostFunction ModuleConstructor,
        JsHostFunction InstanceConstructor,
        JsHostFunction MemoryConstructor,
        JsHostFunction TableConstructor,
        JsHostFunction GlobalConstructor,
        JsPlainObject ModulePrototype,
        JsPlainObject InstancePrototype,
        JsPlainObject MemoryPrototype,
        JsPlainObject TablePrototype,
        JsPlainObject GlobalPrototype);

    private sealed class JsObjectWasmImportResolver(
        JsRealm realm,
        IWasmBackend backend,
        WebAssemblyBindings? bindings,
        in JsValue importsValue)
        : IWasmImportResolver
    {
        private readonly WebAssemblyBindings? bindings = bindings;
        private readonly JsValue importsValue = importsValue;

        public bool TryResolveImport(WasmImportDescriptor descriptor, out IWasmExtern wasmExtern)
        {
            wasmExtern = null!;
            if (importsValue.IsUndefined || importsValue.IsNull)
                return false;
            if (!importsValue.TryGetObject(out var importsObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly imports must be an object");
            if (!importsObj.TryGetProperty(descriptor.ModuleName, out var moduleValue) ||
                !moduleValue.TryGetObject(out var moduleObj))
                return false;
            if (!moduleObj.TryGetProperty(descriptor.Name, out var value))
                return false;

            wasmExtern = descriptor.Kind switch
            {
                WasmExternalKind.Function => ResolveFunctionImport((WasmFunctionType)descriptor.Type, value),
                WasmExternalKind.Memory => ResolveMemoryImport((WasmMemoryType)descriptor.Type, value),
                WasmExternalKind.Table => ResolveTableImport((WasmTableType)descriptor.Type, value),
                WasmExternalKind.Global => ResolveGlobalImport((WasmGlobalType)descriptor.Type, value),
                _ => throw new NotSupportedException($"Unsupported wasm import kind: {descriptor.Kind}")
            };

            return true;
        }

        private IWasmExtern ResolveFunctionImport(WasmFunctionType type, in JsValue value)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsFunction function)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly function import must be callable");

            return backend.CreateFunction(type, (args, returnValues) =>
            {
                JsValue[]? rentedArgs = null;
                var jsArgs = args.Length == 0
                    ? Array.Empty<JsValue>()
                    : rentedArgs = ArrayPool<JsValue>.Shared.Rent(args.Length);

                try
                {
                    for (var i = 0; i < args.Length; i++)
                        jsArgs[i] = ConvertValue(realm, args[i]);

                    var result = realm.InvokeFunction(function, JsValue.Undefined, jsArgs.AsSpan(0, args.Length));
                    if (type.ResultCount == 0)
                        return;

                    if (type.ResultCount == 1)
                    {
                        returnValues[0] = ConvertJsToWasmValue(realm, type.Results[0], result);
                        return;
                    }

                    if (!result.TryGetObject(out var resultObj) || resultObj is not JsArray resultArray)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Multi-value WebAssembly import result must be an Array");

                    for (var i = 0; i < type.ResultCount; i++)
                        returnValues[i] = ConvertJsToWasmValue(realm, type.Results[i], resultArray[(uint)i]);
                }
                finally
                {
                    if (rentedArgs is not null)
                        ArrayPool<JsValue>.Shared.Return(rentedArgs, true);
                }
            });
        }

        private IWasmExtern ResolveMemoryImport(WasmMemoryType type, in JsValue value)
        {
            if (TryGetMemory(value, out var memory))
                return memory;

            throw new JsRuntimeException(JsErrorKind.TypeError,
                "WebAssembly memory import must be a WebAssembly.Memory");
        }

        private IWasmExtern ResolveTableImport(WasmTableType type, in JsValue value)
        {
            if (TryGetTable(value, out var table))
                return table;

            throw new JsRuntimeException(JsErrorKind.TypeError, "WebAssembly table import must be a WebAssembly.Table");
        }

        private IWasmExtern ResolveGlobalImport(WasmGlobalType type, in JsValue value)
        {
            if (TryGetGlobal(value, out var global))
                return global;

            return backend.CreateGlobal(type, ConvertJsToWasmValue(realm, type.ValueKind, value));
        }
    }

    private sealed class JsBoxObject(JsRealm realm, object value) : JsObject(realm)
    {
        public object Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
    }
}

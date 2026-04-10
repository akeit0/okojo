using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo.WebAssembly;
using Okojo.WebAssembly.Wasmtime;
using Wasmtime;

namespace Okojo.Tests;

public class WebAssemblyFeatureTests
{
    [Test]
    public void WasmtimeBackend_Validate_And_Compile_Wat_Module()
    {
        var backend = new WasmtimeBackend();
        var wasm = Module.ConvertText("""
                                      (module
                                        (func (export "run") (result i32)
                                          i32.const 42))
                                      """);

        Assert.That(backend.Validate(wasm), Is.True);

        using var compiled = backend.Compile(wasm);
        Assert.That(compiled.Exports.Any(x => x.Name == "run" && x.Kind == WasmExternalKind.Function), Is.True);
    }

    [Test]
    public void External_ArrayBuffer_Wrapper_Reflects_Backing_Bytes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        byte[] bytes = [1, 2, 3, 4];
        var backing = new JsArrayBufferObject.DelegateExternalBufferBackingStore(
            () => bytes.AsSpan(),
            () => IntPtr.Zero,
            new());

        var buffer = JsArrayBufferObject.CreateExternal(realm, backing);
        realm.GlobalObject["ext"] = buffer;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const view = new Uint8Array(ext);
            view[1] = 99;
            view[0] === 1 && view[1] === 99 && ext.byteLength === 4;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
        Assert.That(bytes[1], Is.EqualTo(99));
    }

    [Test]
    public void External_SharedArrayBuffer_Wrapper_Supports_Atomics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var bytes = new byte[4];
        var backing = new JsArrayBufferObject.DelegateExternalBufferBackingStore(
            () => bytes.AsSpan(),
            () => IntPtr.Zero,
            new());

        var buffer = JsArrayBufferObject.CreateExternalShared(realm, backing);
        realm.GlobalObject["shared"] = buffer;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const view = new Int32Array(shared);
            Atomics.store(view, 0, 7);
            Atomics.add(view, 0, 5) === 7 && Atomics.load(view, 0) === 12;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UseWebAssembly_Installer_Exposes_WebAssembly_Validate_Compile_And_Instantiate()
    {
        var wasm = Module.ConvertText("""
                                      (module
                                        (func (export "run") (result i32)
                                          i32.const 42))
                                      """);

        using var runtime = JsRuntime.CreateBuilder()
            .UseWebAssembly(wasmBuilder => wasmBuilder
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .Build();

        var realm = runtime.DefaultRealm;
        realm.GlobalObject["wasmBytes"] = JsArrayBufferObject.CreateExternal(
            realm,
            new JsArrayBufferObject.DelegateExternalBufferBackingStore(
                () => wasm.AsSpan(),
                () => IntPtr.Zero,
                new()));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const valid = WebAssembly.validate(wasmBytes);
            globalThis.ok = false;
            WebAssembly.compile(wasmBytes)
              .then(mod => WebAssembly.instantiate(mod))
              .then(instance => {
                globalThis.ok = valid && instance.exports.run() === 42;
              });
            globalThis.ok;
            """));

        realm.Execute(script);
        runtime.DefaultRealm.PumpJobs();

        Assert.That(realm.GlobalObject["ok"].IsTrue, Is.True);
    }

    [Test]
    public void UseWebAssembly_Installer_Exposes_Memory_Wrapper_And_Error_Constructors()
    {
        using var runtime = JsRuntime.CreateBuilder()
            .UseWebAssembly(wasmBuilder => wasmBuilder
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .Build();

        var realm = runtime.DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const mem = new WebAssembly.Memory({ initial: 1, maximum: 2 });
            const view = new Uint8Array(mem.buffer);
            view[0] = 17;
            const err = new WebAssembly.RuntimeError("boom");
            mem.buffer instanceof ArrayBuffer &&
              view[0] === 17 &&
              err.name === "RuntimeError" &&
              err.message === "boom";
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UseWebAssembly_Installer_Supports_MultiArgument_Function_Imports()
    {
        var wasm = Module.ConvertText("""
                                      (module
                                        (import "env" "fn" (func $fn (param i32 i32 i32 i32 i32 i32 i32 i32)))
                                        (func (export "run")
                                          i32.const 1
                                          i32.const 2
                                          i32.const 3
                                          i32.const 4
                                          i32.const 5
                                          i32.const 6
                                          i32.const 7
                                          i32.const 8
                                          call $fn))
                                      """);

        using var runtime = JsRuntime.CreateBuilder()
            .UseWebAssembly(wasmBuilder => wasmBuilder
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .Build();

        var realm = runtime.DefaultRealm;
        realm.GlobalObject["wasmBytes"] = JsArrayBufferObject.CreateExternal(
            realm,
            new JsArrayBufferObject.DelegateExternalBufferBackingStore(
                () => wasm.AsSpan(),
                () => IntPtr.Zero,
                new()));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.argCount = -1;
            globalThis.argSum = -1;
            WebAssembly.instantiate(wasmBytes, {
              env: {
                fn(a, b, c, d, e, f, g, h) {
                  globalThis.argCount = arguments.length;
                  globalThis.argSum = a + b + c + d + e + f + g + h;
                }
              }
            }).then(result => {
              result.instance ? result.instance.exports.run() : result.exports.run();
            });
            0;
            """));

        realm.Execute(script);
        runtime.DefaultRealm.PumpJobs();

        Assert.That((int)realm.ToNumber(realm.GlobalObject["argCount"]), Is.EqualTo(8));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["argSum"]), Is.EqualTo(36));
    }

    [Test]
    public void UseWebAssembly_Import_Callback_Can_Spread_Arguments_Into_Inner_Js_Call()
    {
        var wasm = Module.ConvertText("""
                                      (module
                                        (import "env" "fn" (func $fn (param i32 i32 i32 i32)))
                                        (func (export "run")
                                          i32.const 1
                                          i32.const 2
                                          i32.const 3
                                          i32.const 4
                                          call $fn))
                                      """);

        using var runtime = JsRuntime.CreateBuilder()
            .UseWebAssembly(wasmBuilder => wasmBuilder
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .Build();

        var realm = runtime.DefaultRealm;
        realm.GlobalObject["wasmBytes"] = JsArrayBufferObject.CreateExternal(
            realm,
            new JsArrayBufferObject.DelegateExternalBufferBackingStore(
                () => wasm.AsSpan(),
                () => IntPtr.Zero,
                new()));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.outerCount = -1;
            globalThis.innerCount = -1;
            globalThis.innerSum = -1;

            function target(a, b, c, d) {
              globalThis.innerCount = arguments.length;
              globalThis.innerSum = a + b + c + d;
            }

            WebAssembly.instantiate(wasmBytes, {
              env: {
                fn: function() {
                  globalThis.outerCount = arguments.length;
                  target(...arguments);
                }
              }
            }).then(result => {
              result.instance ? result.instance.exports.run() : result.exports.run();
            });

            0;
            """));

        realm.Execute(script);
        runtime.DefaultRealm.PumpJobs();

        Assert.That((int)realm.ToNumber(realm.GlobalObject["outerCount"]), Is.EqualTo(4));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["innerCount"]), Is.EqualTo(4));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["innerSum"]), Is.EqualTo(10));
    }

    [Test]
    public void UseWebAssembly_Import_Callback_Can_Apply_Arguments_Into_Inner_Js_Call()
    {
        var wasm = Module.ConvertText("""
                                      (module
                                        (import "env" "fn" (func $fn (param i32 i32 i32 i32)))
                                        (func (export "run")
                                          i32.const 1
                                          i32.const 2
                                          i32.const 3
                                          i32.const 4
                                          call $fn))
                                      """);

        using var runtime = JsRuntime.CreateBuilder()
            .UseWebAssembly(wasmBuilder => wasmBuilder
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .Build();

        var realm = runtime.DefaultRealm;
        realm.GlobalObject["wasmBytes"] = JsArrayBufferObject.CreateExternal(
            realm,
            new JsArrayBufferObject.DelegateExternalBufferBackingStore(
                () => wasm.AsSpan(),
                () => IntPtr.Zero,
                new()));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.outerCount = -1;
            globalThis.innerCount = -1;
            globalThis.innerSum = -1;

            function target(a, b, c, d) {
              globalThis.innerCount = arguments.length;
              globalThis.innerSum = a + b + c + d;
            }

            WebAssembly.instantiate(wasmBytes, {
              env: {
                fn: function() {
                  globalThis.outerCount = arguments.length;
                  target.apply(this, arguments);
                }
              }
            }).then(result => {
              result.instance ? result.instance.exports.run() : result.exports.run();
            });

            0;
            """));

        realm.Execute(script);
        runtime.DefaultRealm.PumpJobs();

        Assert.That((int)realm.ToNumber(realm.GlobalObject["outerCount"]), Is.EqualTo(4));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["innerCount"]), Is.EqualTo(4));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["innerSum"]), Is.EqualTo(10));
    }

    [Test]
    public void WasmtimeBackend_Exported_Function_Writes_Into_Caller_Provided_Result_Buffer()
    {
        var backend = new WasmtimeBackend();
        var wasm = Module.ConvertText("""
                                      (module
                                        (func (export "pair") (result i32 i32)
                                          i32.const 4
                                          i32.const 7))
                                      """);

        using var compiled = backend.Compile(wasm);
        using var instance = backend.Instantiate(
            compiled,
            new DictionaryImportResolver(new Dictionary<(string ModuleName, string Name), IWasmExtern>()));

        Assert.That(instance.TryGetExport("pair", out var wasmExtern), Is.True);
        Assert.That(wasmExtern, Is.InstanceOf<IWasmFunction>());

        var function = (IWasmFunction)wasmExtern;
        var returnValues = new WasmValue[2];
        function.Invoke(ReadOnlySpan<WasmValue>.Empty, returnValues);

        Assert.That(returnValues[0].Int32Value, Is.EqualTo(4));
        Assert.That(returnValues[1].Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void WasmtimeBackend_Host_Function_Can_Write_Multi_Value_Results_Into_Result_Buffer()
    {
        var backend = new WasmtimeBackend();
        var wasm = Module.ConvertText("""
                                      (module
                                        (import "env" "pair" (func $pair (param i32) (result i32 i32)))
                                        (func (export "run") (param i32) (result i32 i32)
                                          local.get 0
                                          call $pair))
                                      """);

        using var compiled = backend.Compile(wasm);
        var importFunction = backend.CreateFunction(
            new([WasmValueKind.Int32], [WasmValueKind.Int32, WasmValueKind.Int32]),
            static (arguments, returnValues) =>
            {
                returnValues[0] = WasmValue.FromInt32(arguments[0].Int32Value + 1);
                returnValues[1] = WasmValue.FromInt32(arguments[0].Int32Value + 2);
            });

        using var instance = backend.Instantiate(
            compiled,
            new DictionaryImportResolver(new Dictionary<(string ModuleName, string Name), IWasmExtern>
            {
                [("env", "pair")] = importFunction
            }));

        Assert.That(instance.TryGetExport("run", out var wasmExtern), Is.True);
        Assert.That(wasmExtern, Is.InstanceOf<IWasmFunction>());

        var function = (IWasmFunction)wasmExtern;
        var arguments = new WasmValue[1];
        arguments[0] = WasmValue.FromInt32(10);
        var returnValues = new WasmValue[2];
        function.Invoke(arguments, returnValues);

        Assert.That(returnValues[0].Int32Value, Is.EqualTo(11));
        Assert.That(returnValues[1].Int32Value, Is.EqualTo(12));
    }

    [Test]
    public void FunctionPrototypeApply_Can_Forward_Arguments_Object_Into_Host_Function()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.GlobalObject["hostCapture"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            info.Realm.GlobalObject["hostArgCount"] = JsValue.FromInt32(info.Arguments.Length);
            info.Realm.GlobalObject["hostArg0"] = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            info.Realm.GlobalObject["hostArg1"] = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            return JsValue.Undefined;
        }, "hostCapture", 0));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            globalThis.hostArgCount = -1;
            globalThis.hostArg0 = undefined;
            globalThis.hostArg1 = undefined;

            function outer() {
              return hostCapture.apply(undefined, arguments);
            }

            outer(11, 22);
            0;
            """));

        realm.Execute(script);

        Assert.That((int)realm.ToNumber(realm.GlobalObject["hostArgCount"]), Is.EqualTo(2));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["hostArg0"]), Is.EqualTo(11));
        Assert.That((int)realm.ToNumber(realm.GlobalObject["hostArg1"]), Is.EqualTo(22));
    }

    [Test]
    public void WasmValue_Numeric_Kinds_Use_Typed_Fields()
    {
        var i32 = WasmValue.FromInt32(7);
        var i64 = WasmValue.FromInt64(9);
        var f32 = WasmValue.FromFloat32(1.5f);
        var f64 = WasmValue.FromFloat64(2.5);

        Assert.Multiple(() =>
        {
            Assert.That(i32.Int32Value, Is.EqualTo(7));
            Assert.That(i32.ObjectValue, Is.Null);
            Assert.That(i64.Int64Value, Is.EqualTo(9));
            Assert.That(i64.ObjectValue, Is.Null);
            Assert.That(f32.Float32Value, Is.EqualTo(1.5f));
            Assert.That(f32.ObjectValue, Is.Null);
            Assert.That(f64.Float64Value, Is.EqualTo(2.5));
            Assert.That(f64.ObjectValue, Is.Null);
        });
    }

    private sealed class DictionaryImportResolver(
        IReadOnlyDictionary<(string ModuleName, string Name), IWasmExtern> imports)
        : IWasmImportResolver
    {
        public bool TryResolveImport(WasmImportDescriptor descriptor, out IWasmExtern wasmExtern)
        {
            return imports.TryGetValue((descriptor.ModuleName, descriptor.Name), out wasmExtern!);
        }
    }
}

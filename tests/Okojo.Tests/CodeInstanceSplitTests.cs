using System.Runtime.CompilerServices;
using Okojo.Diagnostics;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Values;

namespace Okojo.Tests;

public class CodeInstanceSplitTests
{
    [Test]
    public void Compilation_ProducesPortableDescriptors_NotLanguageObjects()
    {
        var unit = JsCompiler.CompileUnit(
            "function make(x) { return y => x + y; } class C { #x = 1; get() { return this.#x; } }",
            "portable.js"
        );

        Assert.That(unit.Functions.Length, Is.GreaterThan(3));
        foreach (var function in unit.Functions)
        {
            Assert.That(function.Code.SourceCode?.Path, Is.EqualTo("portable.js"));
            foreach (var constant in function.Code.ConstantDescriptors)
                Assert.That(
                    constant is JsObject or JsScript or JsRealm or StaticNamedPropertyLayout,
                    Is.False,
                    $"Non-portable constant in {function.Name}"
                );
        }
    }

    [Test]
    public void Linking_SharesCode_ButSeparatesRealmFeedback_AndAgentAtoms()
    {
        var unit = JsCompiler.CompileUnit(
            "function read(o) { return o.value + portableSentinel; } read({ value: 2 });"
        );
        using var first = JsRuntime.Create();
        using var second = JsRuntime.Create();
        var a = first.DefaultRealm;
        var sibling = first.CreateRealm();
        var b = second.DefaultRealm;
        for (var i = 0; i < 17; i++)
            b.Atoms.InternNoCheck($"target-only-{i}");
        var aScript = unit.Link(a);
        var siblingScript = unit.Link(sibling);
        var bScript = unit.Link(b);
        var aRead = FindFunction(aScript, "read");
        var siblingRead = FindFunction(siblingScript, "read");
        var bRead = FindFunction(bScript, "read");

        Assert.That(unit.Link(a), Is.SameAs(aScript));
        Assert.That(aScript.Code, Is.SameAs(bScript.Code));
        Assert.That(aRead.BytecodeArray, Is.SameAs(bRead.BytecodeArray));
        Assert.That(aRead.ExecutionBytecode, Is.SameAs(aRead.BytecodeArray));
        Assert.That(aRead, Is.Not.SameAs(siblingRead));
        Assert.That(aRead.NamedPropertyIcEntries, Is.Not.Null);
        Assert.That(aRead.GlobalBindingIcEntries, Is.Not.Null);
        Assert.That(
            aRead.NamedPropertyIcEntries,
            Is.Not.SameAs(siblingRead.NamedPropertyIcEntries)
        );
        Assert.That(aRead.NamedPropertyIcEntries, Is.Not.SameAs(bRead.NamedPropertyIcEntries));
        Assert.That(aRead.GlobalBindingIcEntries, Is.Not.SameAs(bRead.GlobalBindingIcEntries));
        Assert.That(aRead.AtomizedStringConstants, Is.Not.SameAs(bRead.AtomizedStringConstants));
        Assert.That(aRead.Agent, Is.SameAs(first.MainAgent));
        Assert.That(bRead.Agent, Is.SameAs(second.MainAgent));

        a.Global["portableSentinel"] = 5;
        b.Global["portableSentinel"] = 40;
        a.Execute(aScript);
        Assert.That(a.Accumulator.NumberValue, Is.EqualTo(7));
        Assert.That(aRead.NamedPropertyIcEntries!.Any(entry => entry.Shape is not null), Is.True);
        Assert.That(bRead.NamedPropertyIcEntries!.All(entry => entry.Shape is null), Is.True);
        Assert.That(siblingRead.NamedPropertyIcEntries!.All(entry => entry.Shape is null), Is.True);
        b.Execute(bScript);
        Assert.That(b.Accumulator.NumberValue, Is.EqualTo(42));
        Assert.That(aRead.Agent, Is.SameAs(first.MainAgent));
    }

    [Test]
    public void LiteralLayouts_AreResolvedAgainstTheTargetRealm()
    {
        var unit = JsCompiler.CompileUnit(
            "var result = { alpha: 11, beta: 22 }; result.alpha + result.beta;"
        );
        using var first = JsRuntime.Create();
        using var second = JsRuntime.Create();
        second.DefaultRealm.Atoms.InternNoCheck("beta");
        second.DefaultRealm.Atoms.InternNoCheck("alpha");
        var a = unit.Link(first.DefaultRealm);
        var b = unit.Link(second.DefaultRealm);
        var aLayout = a.ObjectConstants.OfType<StaticNamedPropertyLayout>().Single();
        var bLayout = b.ObjectConstants.OfType<StaticNamedPropertyLayout>().Single();

        Assert.That(aLayout, Is.Not.SameAs(bLayout));
        first.DefaultRealm.Execute(a);
        second.DefaultRealm.Execute(b);
        Assert.That(first.DefaultRealm.Accumulator.NumberValue, Is.EqualTo(33));
        Assert.That(second.DefaultRealm.Accumulator.NumberValue, Is.EqualTo(33));
    }

    [Test]
    public void Closures_HaveFreshIdentityAndCaptures_ButShareTheirLinkedInstance()
    {
        var unit = JsCompiler.CompileUnit(
            "function make(x) { return function add(y) { return x + y; }; }"
                + "var first = make(1); var second = make(10);"
        );
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(unit.Link(realm));
        var first = (JsBytecodeFunction)realm.Global["first"].AsObject();
        var second = (JsBytecodeFunction)realm.Global["second"].AsObject();

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first.Script, Is.SameAs(second.Script));
        Assert.That(first.Descriptor, Is.SameAs(second.Descriptor));
        Assert.That(first.BoundParentContext, Is.Not.SameAs(second.BoundParentContext));
        Assert.That(realm.InvokeFunction(first, JsValue.Undefined, [2]).NumberValue, Is.EqualTo(3));
        Assert.That(
            realm.InvokeFunction(second, JsValue.Undefined, [2]).NumberValue,
            Is.EqualTo(12)
        );
        Assert.That(realm.Evaluate("first.prototype !== second.prototype").IsTrue, Is.True);
    }

    [Test]
    public void PrivateBrands_Super_AndGenerators_RemainRealmLocal()
    {
        var unit = JsCompiler.CompileUnit(
            """
            class Base { value() { return seed; } }
            function make() {
                return class C extends Base {
                    #x = 4;
                    value() { return super.value() + this.#x; }
                };
            }
            var C1 = make(), C2 = make(), foreign = new C2();
            var rejected = false;
            try { C1.prototype.value.call(foreign); }
            catch (e) { rejected = e instanceof TypeError; }
            function* values() { yield new C1().value(); return 99; }
            var iterator = values();
            [iterator.next().value, iterator.next().value, rejected].join(",");
            """
        );
        using var a = JsRuntime.Create();
        using var b = JsRuntime.Create();
        a.DefaultRealm.Global["seed"] = 1;
        b.DefaultRealm.Global["seed"] = 10;
        a.DefaultRealm.Execute(unit.Link(a.DefaultRealm));
        b.DefaultRealm.Execute(unit.Link(b.DefaultRealm));

        Assert.That(a.DefaultRealm.Accumulator.AsString(), Is.EqualTo("5,99,true"));
        Assert.That(b.DefaultRealm.Accumulator.AsString(), Is.EqualTo("14,99,true"));
    }

    [Test]
    public void TemplateObjects_ShareASiteAcrossClosures_NotAcrossRealms()
    {
        var unit = JsCompiler.CompileUnit(
            """
            function make() { return function get() { return (x => x)`same`; }; }
            var a = make(), b = make();
            var t1 = a(), t2 = b();
            t1 === t2 && Object.isFrozen(t1) && Object.isFrozen(t1.raw);
            """
        );
        using var first = JsRuntime.Create();
        using var second = JsRuntime.Create();
        first.DefaultRealm.Execute(unit.Link(first.DefaultRealm));
        second.DefaultRealm.Execute(unit.Link(second.DefaultRealm));

        Assert.That(first.DefaultRealm.Accumulator.IsTrue, Is.True);
        Assert.That(second.DefaultRealm.Accumulator.IsTrue, Is.True);
        Assert.That(
            first.DefaultRealm.Global["t1"].AsObject(),
            Is.Not.SameAs(second.DefaultRealm.Global["t1"].AsObject())
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Declarations_AreCheckedAtLink_AndRecheckedBeforeExecution(bool linkFirst)
    {
        var unit = JsCompiler.CompileUnit("globalThis.sideEffect = true; let conflict = 1;");
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var script = linkFirst ? unit.Link(realm) : null;
        realm.Execute(JsCompiler.Compile(realm, "let conflict = 2;"));

        var error = Assert.Throws<JsRuntimeException>(() =>
        {
            if (script is null)
                unit.Link(realm);
            else
                realm.Execute(script);
        });
        Assert.That(error!.Kind, Is.EqualTo(JsErrorKind.SyntaxError));
        Assert.That(realm.Global.TryGetValue("sideEffect", out _), Is.False);
        Assert.Throws<JsRuntimeException>(() => unit.Link(realm));
    }

    [TestCase("var unavailable;")]
    [TestCase("function unavailable() {}")]
    public void Declarations_RespectANonExtensibleTargetGlobal(string source)
    {
        var unit = JsCompiler.CompileUnit(source);
        using var runtime = JsRuntime.Create();
        runtime.DefaultRealm.Evaluate("Object.preventExtensions(globalThis)");
        var error = Assert.Throws<JsRuntimeException>(() => unit.Link(runtime.DefaultRealm));
        Assert.That(error!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void DeleteIdentifier_UsesTargetLexicals_AndTheRealGlobalObject()
    {
        var unit = JsCompiler.CompileUnit("delete victim;");
        using var first = JsRuntime.Create();
        using var second = JsRuntime.Create();
        var a = first.DefaultRealm;
        var b = second.DefaultRealm;
        a.Execute(JsCompiler.Compile(a, "let victim = 1;"));
        b.Global["victim"] = 7;
        b.Evaluate("globalThis = { victim: 99 }");
        a.Execute(unit.Link(a));
        b.Execute(unit.Link(b));

        Assert.That(a.Accumulator.IsFalse, Is.True);
        Assert.That(b.Accumulator.IsTrue, Is.True);
        Assert.That(b.Global.TryGetValue("victim", out _), Is.False);
        Assert.That(b.Evaluate("globalThis.victim").NumberValue, Is.EqualTo(99));
    }

    [Test]
    public void Breakpoints_CopyOnlyTheTargetExecutionView_AndKeepDisassemblyPristine()
    {
        var debugger = new Recorder();
        using var first = CreateDebugRuntime(debugger);
        using var second = JsRuntime.Create();
        var unit = JsCompiler.CompileUnit("function hot(x) { return x + 1; } hot(4);", "cow.js");
        var a = unit.Link(first.DefaultRealm);
        var b = unit.Link(second.DefaultRealm);
        var aHot = FindFunction(a, "hot");
        var bHot = FindFunction(b, "hot");
        var original = aHot.Bytecode.ToArray();
        var disassembly = Disassembler.Dump(aHot);
        using var breakpoint = first.MainAgent.AddBreakpoint(aHot, 0);

        Assert.That(aHot.ExecutionBytecode, Is.Not.SameAs(aHot.BytecodeArray));
        Assert.That(bHot.ExecutionBytecode, Is.SameAs(aHot.BytecodeArray));
        Assert.That(aHot.ExecutionBytecode[0], Is.EqualTo((byte)JsOpCode.Debugger));
        Assert.That(aHot.Bytecode.ToArray(), Is.EqualTo(original));
        Assert.That(Disassembler.Dump(aHot), Is.EqualTo(disassembly));
        Assert.Throws<ArgumentException>(() => first.MainAgent.AddBreakpoint(bHot, 0));
        first.DefaultRealm.Execute(a);
        second.DefaultRealm.Execute(b);
        Assert.That(debugger.BreakpointCount, Is.EqualTo(1));
        Assert.That(first.DefaultRealm.Accumulator.NumberValue, Is.EqualTo(5));
        Assert.That(second.DefaultRealm.Accumulator.NumberValue, Is.EqualTo(5));
        breakpoint.Dispose();
        Assert.That(aHot.ExecutionBytecode, Is.EqualTo(original));
        Assert.That(aHot.ExecutionBytecode, Is.Not.SameAs(aHot.BytecodeArray));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void FirstBreakpointInsideHostCode_RebasesLiveAndNestedCursors(bool nested, bool throws)
    {
        var debugger = new Recorder();
        using var runtime = CreateDebugRuntime(debugger);
        var realm = runtime.DefaultRealm;
        var firstLine = throws ? "try { enter(); } catch (e) { }" : "enter();";
        var unit = JsCompiler.CompileUnit(
            firstLine + "\nglobalThis.answer = 42;\nanswer;",
            "late.js"
        );
        var script = unit.Link(realm);
        var original = script.Bytecode.ToArray();
        JsBreakpointHandle? breakpoint = null;
        realm.Global["arm"] = new JsHostFunction(
            realm,
            "testHost",
            0,
            (in CallInfo info) =>
            {
                breakpoint = runtime.MainAgent.AddBreakpoint("late.js", 2);
                if (throws)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "host failure",
                        "HOST_TEST"
                    );
                return JsValue.Undefined;
            }
        );
        var inner = (JsBytecodeFunction)
            realm
                .Evaluate(
                    "(function inner() { arm(); var n = 0; for (var i = 0; i < 10; i++) n += i; return n; })"
                )
                .AsObject();
        realm.Global["enter"] = new JsHostFunction(
            realm,
            "testHost",
            0,
            (in CallInfo info) =>
            {
                var callee = nested
                    ? (JsFunction)inner
                    : (JsFunction)realm.Global["arm"].AsObject();
                return realm.InvokeFunction(callee, JsValue.Undefined, []);
            }
        );
        try
        {
            realm.Execute(script);
            Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(42));
            Assert.That(debugger.BreakpointCount, Is.EqualTo(1));
            Assert.That(script.Bytecode.ToArray(), Is.EqualTo(original));
        }
        finally
        {
            breakpoint?.Dispose();
        }
    }

    [Test]
    public void FirstCopyDuringADebuggerStatement_RebasesBeforeContinuing()
    {
        var debugger = new Recorder();
        using var runtime = CreateDebugRuntime(debugger);
        runtime.MainAgent.EnableDebuggerStatementHook();
        var unit = JsCompiler.CompileUnit("debugger;\nglobalThis.answer = 42;\nanswer;", "stop.js");
        var script = unit.Link(runtime.DefaultRealm);
        var original = script.Bytecode.ToArray();
        JsBreakpointHandle? breakpoint = null;
        debugger.OnHit = checkpoint =>
        {
            if (checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement)
                breakpoint = runtime.MainAgent.AddBreakpoint("stop.js", 2);
        };
        try
        {
            runtime.DefaultRealm.Execute(script);
            Assert.That(runtime.DefaultRealm.Accumulator.NumberValue, Is.EqualTo(42));
            Assert.That(debugger.BreakpointCount, Is.EqualTo(1));
            Assert.That(script.Bytecode.ToArray(), Is.EqualTo(original));
        }
        finally
        {
            breakpoint?.Dispose();
        }
    }

    [Test]
    public void UnpatchedCursorRefresh_DoesNotConsumeAnExtraPolicyInterval()
    {
        var unit = JsCompiler.CompileUnit(
            "arm(); var n = 0; for (var i = 0; i < 40; i++) n += i; n;"
        );
        var baseline = Run(false);
        var copied = Run(true);
        Assert.That(baseline, Is.Not.Empty);
        Assert.That(copied, Is.EqualTo(baseline));

        ulong[] Run(bool copy)
        {
            var recorder = new Recorder();
            using var runtime = JsRuntime.Create(builder =>
                builder.UseAgent(agent =>
                {
                    agent.SetCheckInterval(5);
                    agent.AddConstraint(recorder);
                })
            );
            var realm = runtime.DefaultRealm;
            var script = unit.Link(realm);
            realm.Global["arm"] = new JsHostFunction(
                realm,
                "testHost",
                0,
                (in CallInfo info) =>
                {
                    if (copy)
                        script.GetOrCreateDebugBytecode();
                    return JsValue.Undefined;
                }
            );
            realm.Execute(script);
            Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(780));
            return recorder
                .Checkpoints.Where(checkpoint =>
                    checkpoint.Kind == ExecutionCheckpointKind.Periodic
                )
                .Select(checkpoint => checkpoint.ExecutedInstructions)
                .ToArray();
        }
    }

    [Test]
    public void ASharedUnit_CanExecuteConcurrentlyOnIndependentAgents()
    {
        var unit = JsCompiler.CompileUnit(
            "function run(x) { return { value: x }.value + seed; } run(40);",
            "parallel.js"
        );
        var results = new double[8];
        Parallel.For(
            0,
            results.Length,
            i =>
            {
                using var runtime = JsRuntime.Create();
                var realm = runtime.DefaultRealm;
                realm.Global["seed"] = i;
                var script = unit.Link(realm);
                _ = unit.Functions.ToArray();
                script.TryGetSourceLocationAtPc(0, out _, out _);
                realm.Execute(script);
                results[i] = realm.Accumulator.NumberValue;
            }
        );
        Assert.That(
            results,
            Is.EqualTo(Enumerable.Range(40, results.Length).Select(i => (double)i))
        );
    }

    [Test]
    public void KeepingAUnit_DoesNotKeepATemplateObjectRealmAlive()
    {
        var (unit, reference) = LinkAndDiscardRealm();
        Collect(reference);
        Assert.That(IsAlive(reference), Is.False);
        GC.KeepAlive(unit);
    }

    [Test]
    public void RealmLinkCache_DoesNotRootDiscardedCode()
    {
        using var runtime = JsRuntime.Create();
        var reference = LinkAndDiscardUnit(runtime.DefaultRealm);
        Collect(reference);
        Assert.That(IsAlive(reference), Is.False);
        GC.KeepAlive(runtime);
    }

    [Test]
    public void Builder_SnapshotsMutableLeaves_AndRejectsRealmObjects()
    {
        using var builder = new BytecodeBuilder();
        var flags = new[] { 1, 0 };
        var index = builder.AddObjectConstant(flags);
        builder.Emit(JsOpCode.Return);
        var unit = builder.ToCompilationUnit();
        flags[0] = 99;
        Assert.That((int[])unit.Entry.Code.ConstantDescriptors[index], Is.EqualTo(new[] { 1, 0 }));
        using var runtime = JsRuntime.Create();
        using var invalidBuilder = new BytecodeBuilder();
        invalidBuilder.AddObjectConstant(new JsPlainObject(runtime.DefaultRealm));
        invalidBuilder.Emit(JsOpCode.Return);
        Assert.Throws<ArgumentException>(() => invalidBuilder.ToCompilationUnit());
    }

    [Test]
    public void FeedbackAndDebuggerStorage_AreAbsentWhenNotNeeded()
    {
        using var runtime = JsRuntime.Create();
        var script = JsCompiler.CompileUnit("1 + 2;").Link(runtime.DefaultRealm);
        Assert.That(script.NamedPropertyIcEntries, Is.Null);
        Assert.That(script.GlobalBindingIcEntries, Is.Null);
        Assert.That(script.PrototypeNamedPropertyIcEntries, Is.Null);
        Assert.That(script.ExecutionBytecode, Is.SameAs(script.BytecodeArray));
    }

    private static JsScript FindFunction(JsScript script, string name) =>
        script
            .ObjectConstants.OfType<JsScript>()
            .Single(instance => instance.Function.Name == name);

    private static JsRuntime CreateDebugRuntime(Recorder recorder) =>
        JsRuntime.Create(builder =>
            builder.UseAgent(agent =>
            {
                agent.DebuggerSession = recorder;
                agent.EnableBreakpointHook();
            })
        );

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (JsCompilationUnit Unit, WeakReference<JsRealm> Realm) LinkAndDiscardRealm()
    {
        using var runtime = JsRuntime.Create();
        var unit = JsCompiler.CompileUnit("var template = (x => x)`retention`; template;");
        var realm = runtime.DefaultRealm;
        realm.Execute(unit.Link(realm));
        return (unit, new WeakReference<JsRealm>(realm));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<JsScript> LinkAndDiscardUnit(JsRealm realm)
    {
        var unit = JsCompiler.CompileUnit("function unused(x) { return x + 1; } 42;");
        return new WeakReference<JsScript>(unit.Link(realm));
    }

    private static void Collect<T>(WeakReference<T> reference)
        where T : class
    {
        for (var i = 0; i < 8 && IsAlive(reference); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive<T>(WeakReference<T> reference)
        where T : class => reference.TryGetTarget(out _);

    private sealed class Recorder : IDebuggerSession, IExecutionConstraint
    {
        internal readonly List<ExecutionCheckpoint> Checkpoints = [];
        internal Action<ExecutionCheckpoint>? OnHit;
        internal int BreakpointCount =>
            Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint);

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            Checkpoints.Add(checkpoint);
            OnHit?.Invoke(checkpoint);
        }
    }
}

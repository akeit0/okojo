using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Tests;

public class CompilerStoragePlannerTests
{
    [Test]
    public void Plan_ClassifiesRootBindings_IntoRegisterStorageKinds()
    {
        var program = JavaScriptParser.ParseModule(
            """
            import foo from "pkg";
            var a = 1;
            let b = 2;
            const c = 3;
            function f() {}
            class K {}
            """
        );

        using var collected = CompilerBindingCollector.Collect(program);
        using var plan = CompilerStoragePlanner.Plan(collected);
        var bindings = plan
            .Bindings.ToArray()
            .Where(static binding => binding.ScopeId == 0)
            .OrderBy(static binding => binding.Position)
            .ToArray();

        Assert.That(
            bindings.Select(static binding => (binding.Name, binding.StorageKind)).ToArray(),
            Is.EqualTo(
                new[]
                {
                    ("foo", CompilerPlannedStorageKind.ImportBinding),
                    ("a", CompilerPlannedStorageKind.GlobalBinding),
                    ("b", CompilerPlannedStorageKind.ContextSlot),
                    ("c", CompilerPlannedStorageKind.ContextSlot),
                    ("f", CompilerPlannedStorageKind.GlobalBinding),
                    ("K", CompilerPlannedStorageKind.ContextSlot),
                }
            )
        );
    }

    [Test]
    public void Plan_MarksBindingsCapturedAcrossFunctionBoundaries_AsContextSlots()
    {
        var program = JavaScriptParser.ParseScript(
            """
            let outer = 1;
            function f() {
                return outer;
            }
            """
        );

        using var collected = CompilerBindingCollector.Collect(program);
        using var plan = CompilerStoragePlanner.Plan(collected);
        var bindings = plan.Bindings.ToArray();

        var outerBinding = bindings.Single(static binding => binding.Name == "outer");
        Assert.That(outerBinding.IsCaptured, Is.True);
        Assert.That(outerBinding.StorageKind, Is.EqualTo(CompilerPlannedStorageKind.ContextSlot));
    }

    [Test]
    public void Plan_MarksCapturedLoopHeadAlias_AsContextSlot()
    {
        var program = JavaScriptParser.ParseScript(
            """
            function captureLoop() {
                for (let uncaptured = 0, i = 0; i < 3; i++) {
                    function read() {
                        return i;
                    }
                }
            }
            function ordinaryLoop() {
                for (let j = 0; j < 3; j++) {}
            }
            """
        );

        using var collected = CompilerBindingCollector.Collect(program);
        using var plan = CompilerStoragePlanner.Plan(collected);
        var bindings = plan.Bindings.ToArray();
        var binding = bindings.Single(static binding => binding.Name == "i");
        var uncaptured = bindings.Single(static binding => binding.Name == "uncaptured");
        var ordinary = bindings.Single(static binding => binding.Name == "j");

        Assert.That(binding.Kind, Is.EqualTo(CompilerCollectedBindingKind.LoopHeadAlias));
        Assert.That(binding.IsCaptured, Is.True);
        Assert.That(binding.StorageKind, Is.EqualTo(CompilerPlannedStorageKind.ContextSlot));
        Assert.That(binding.StorageIndex, Is.Zero);
        Assert.That(uncaptured.StorageKind, Is.EqualTo(CompilerPlannedStorageKind.LexicalRegister));
        Assert.That(uncaptured.StorageIndex, Is.EqualTo(-1));
        Assert.That(ordinary.IsCaptured, Is.False);
        Assert.That(ordinary.StorageKind, Is.EqualTo(CompilerPlannedStorageKind.LexicalRegister));
    }

    [Test]
    public void Plan_UsesFinalizedFlatModuleCells()
    {
        using var ast = FlatJavaScriptParser.ParseModule(
            """
            import { source as imported } from "dependency";
            import * as namespaceValue from "namespace";
            export const value = imported;
            const hidden = 1;
            """
        );
        using var collected = CompilerBindingCollector.Collect(ast);
        using var plan = CompilerStoragePlanner.Plan(collected, ast);

        var bindings = plan.Bindings.ToArray().ToDictionary(binding => binding.Name);
        Assert.Multiple(() =>
        {
            Assert.That(
                bindings["imported"].StorageKind,
                Is.EqualTo(CompilerPlannedStorageKind.ModuleBinding)
            );
            Assert.That(bindings["imported"].StorageIndex, Is.EqualTo(-1));
            Assert.That(
                bindings["value"].StorageKind,
                Is.EqualTo(CompilerPlannedStorageKind.ModuleBinding)
            );
            Assert.That(bindings["value"].StorageIndex, Is.EqualTo(1));
            Assert.That(
                bindings["namespaceValue"].StorageKind,
                Is.EqualTo(CompilerPlannedStorageKind.LexicalRegister)
            );
            Assert.That(
                bindings["hidden"].StorageKind,
                Is.EqualTo(CompilerPlannedStorageKind.ContextSlot),
                "non-exported module top-levels must live in the module context so nested functions can capture them"
            );
        });
    }
}

using Okojo.Compiler.Experimental;
using Okojo.Parsing;

namespace Okojo.Compiler.Tests;

public class CompilerStoragePlannerTests
{
    [Test]
    public void Plan_ClassifiesRootBindings_IntoRegisterStorageKinds()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   import foo from "pkg";
                                                   var a = 1;
                                                   let b = 2;
                                                   const c = 3;
                                                   function f() {}
                                                   class K {}
                                                   """);

        using var collected = CompilerBindingCollector.Collect(program);
        using var plan = CompilerStoragePlanner.Plan(collected);
        var bindings = plan.Bindings.ToArray().Where(static binding => binding.ScopeId == 0)
            .OrderBy(static binding => binding.Position)
            .ToArray();

        Assert.That(bindings.Select(static binding => (binding.Name, binding.StorageKind)).ToArray(), Is.EqualTo(new[]
        {
            ("foo", CompilerPlannedStorageKind.ImportBinding),
            ("a", CompilerPlannedStorageKind.LocalRegister),
            ("b", CompilerPlannedStorageKind.LexicalRegister),
            ("c", CompilerPlannedStorageKind.LexicalRegister),
            ("f", CompilerPlannedStorageKind.LocalRegister),
            ("K", CompilerPlannedStorageKind.LexicalRegister)
        }));
    }

    [Test]
    public void Plan_MarksBindingsCapturedAcrossFunctionBoundaries_AsContextSlots()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   let outer = 1;
                                                   function f() {
                                                       return outer;
                                                   }
                                                   """);

        using var collected = CompilerBindingCollector.Collect(program);
        using var plan = CompilerStoragePlanner.Plan(collected);
        var bindings = plan.Bindings.ToArray();

        var outerBinding = bindings.Single(static binding => binding.Name == "outer");
        Assert.That(outerBinding.IsCaptured, Is.True);
        Assert.That(outerBinding.StorageKind, Is.EqualTo(CompilerPlannedStorageKind.ContextSlot));
    }
}

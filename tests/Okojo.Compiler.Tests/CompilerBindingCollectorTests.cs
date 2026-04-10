using Okojo.Compiler.Experimental;
using Okojo.Parsing;

namespace Okojo.Compiler.Tests;

public class CompilerBindingCollectorTests
{
    [Test]
    public void Collect_RootBindings_MatchesDirectLexicalSurface()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   export let alpha = 1;
                                                   class Beta {}
                                                   function gamma() {}
                                                   import delta, { epsilon as zeta } from "pkg";
                                                   """);

        using var result = CompilerBindingCollector.Collect(program);
        var rootBindings = result.Bindings.ToArray()
            .Where(binding => binding.ScopeId == result.RootScopeId)
            .ToArray();

        Assert.That(rootBindings.Select(static binding => (binding.Name, binding.Kind)).ToArray(), Is.EquivalentTo(new[]
        {
            ("alpha", CompilerCollectedBindingKind.Lexical),
            ("Beta", CompilerCollectedBindingKind.ClassDeclaration),
            ("delta", CompilerCollectedBindingKind.Import),
            ("gamma", CompilerCollectedBindingKind.FunctionDeclaration),
            ("zeta", CompilerCollectedBindingKind.Import)
        }));

        Assert.That(rootBindings.Single(static binding => binding.Name == "alpha").Position, Is.GreaterThan(0));
        Assert.That(rootBindings.Single(static binding => binding.Name == "Beta").Position, Is.GreaterThan(0));
    }

    [Test]
    public void Collect_BlockAndLoopBindings_AssignsNestedScopes()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   {
                                                       let left = 1;
                                                       const right = 2;
                                                   }
                                                   for (let i = 0; i < 1; i++) {
                                                       let bodyLocal = i;
                                                   }
                                                   """);

        using var result = CompilerBindingCollector.Collect(program);
        var scopes = result.Scopes.ToArray();
        var bindings = result.Bindings.ToArray();

        var outerBlockScopeId = bindings.Single(static binding => binding.Name == "left").ScopeId;
        var loopScopeId = bindings.Single(static binding => binding.Name == "i").ScopeId;
        var loopBodyScopeId = bindings.Single(static binding => binding.Name == "bodyLocal").ScopeId;

        Assert.That(scopes.Single(scope => scope.ScopeId == outerBlockScopeId).ParentScopeId, Is.EqualTo(0));
        Assert.That(scopes.Single(scope => scope.ScopeId == loopScopeId).ParentScopeId, Is.EqualTo(0));
        Assert.That(scopes.Single(scope => scope.ScopeId == loopBodyScopeId).ParentScopeId, Is.EqualTo(loopScopeId));

        Assert.That(bindings.Where(binding => binding.ScopeId == outerBlockScopeId)
            .Select(static binding => (binding.Name, binding.Kind, binding.IsConst))
            .OrderBy(static binding => binding.Name, StringComparer.Ordinal)
            .ToArray(), Is.EqualTo(new[]
        {
            ("left", CompilerCollectedBindingKind.Lexical, false),
            ("right", CompilerCollectedBindingKind.Lexical, true)
        }));

        Assert.That(bindings.Where(binding => binding.ScopeId == loopScopeId)
            .Select(static binding => (binding.Name, binding.Kind, binding.IsConst))
            .ToArray(), Is.EqualTo(new[]
        {
            ("i", CompilerCollectedBindingKind.LoopHeadAlias, false)
        }));

        Assert.That(bindings.Where(binding => binding.ScopeId == loopBodyScopeId)
            .Select(static binding => (binding.Name, binding.Kind, binding.IsConst))
            .ToArray(), Is.EqualTo(new[]
        {
            ("bodyLocal", CompilerCollectedBindingKind.Lexical, false)
        }));
    }

    [Test]
    public void Collect_FunctionAndCatchBindings_RecordsParametersAndAliases()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   function outer(first, { second }, ...rest) {
                                                       try {
                                                           throw 1;
                                                       } catch ({ message }) {
                                                           let inner = () => second + message + rest.length;
                                                           return inner();
                                                       }
                                                   }
                                                   """);

        using var result = CompilerBindingCollector.Collect(program);
        var bindings = result.Bindings.ToArray();

        var functionScopeId = bindings
            .Where(static binding => binding.Kind == CompilerCollectedBindingKind.Parameter)
            .GroupBy(static binding => binding.ScopeId)
            .Single(group =>
            {
                var names = group.Select(static binding => binding.Name)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray();
                return names.Contains("first", StringComparer.Ordinal) &&
                       names.Contains("second", StringComparer.Ordinal) &&
                       names.Contains("rest", StringComparer.Ordinal);
            })
            .Key;
        var catchScopeId = bindings
            .Where(static binding =>
                binding.Name == "message" && binding.Kind == CompilerCollectedBindingKind.CatchAlias)
            .Select(static binding => binding.ScopeId)
            .Distinct()
            .Single();

        Assert.That(bindings.Where(binding => binding.ScopeId == functionScopeId)
            .Select(static binding => (binding.Name, binding.Kind))
            .ToArray(), Does.Contain(("first", CompilerCollectedBindingKind.Parameter)));
        Assert.That(bindings.Where(binding => binding.ScopeId == functionScopeId)
            .Select(static binding => (binding.Name, binding.Kind))
            .ToArray(), Does.Contain(("second", CompilerCollectedBindingKind.Parameter)));
        Assert.That(bindings.Where(binding => binding.ScopeId == functionScopeId)
            .Select(static binding => (binding.Name, binding.Kind))
            .ToArray(), Does.Contain(("rest", CompilerCollectedBindingKind.Parameter)));

        Assert.That(bindings.Where(binding => binding.ScopeId == catchScopeId)
            .Select(static binding => (binding.Name, binding.Kind))
            .ToArray(), Is.EqualTo(new[]
        {
            ("message", CompilerCollectedBindingKind.CatchAlias),
            ("message", CompilerCollectedBindingKind.CatchAlias)
        }));
    }

    [Test]
    public void Collect_TracksIdentifierReferencesAcrossNestedFunctions()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   let outer = 1;
                                                   function f() {
                                                       return outer;
                                                   }
                                                   """);

        using var result = CompilerBindingCollector.Collect(program);
        var references = result.References.ToArray();

        Assert.That(references.Select(static reference => reference.Name).ToArray(), Does.Contain("outer"));
    }

    [Test]
    public void Collect_RootLexicalBindings_RecordSourcePositions()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   let blocked = 1;
                                                   const fixedValue = 2;
                                                   """);

        using var result = CompilerBindingCollector.Collect(program);
        var rootBindings = result.Bindings.ToArray()
            .Where(binding =>
                binding.ScopeId == result.RootScopeId && binding.Kind == CompilerCollectedBindingKind.Lexical)
            .OrderBy(static binding => binding.Position)
            .ToArray();

        Assert.That(rootBindings.Select(static binding => (binding.Name, binding.Position)).ToArray(), Is.EqualTo(new[]
        {
            ("blocked", rootBindings[0].Position),
            ("fixedValue", rootBindings[1].Position)
        }));
        Assert.That(rootBindings[0].Position, Is.LessThan(rootBindings[1].Position));
    }
}

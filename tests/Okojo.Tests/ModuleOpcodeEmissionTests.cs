using System.Text;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleOpcodeEmissionTests
{
    [Test]
    public void Compile_ModuleImportRead_UsesLdaModuleVariable()
    {
        var source = """
                     import { counter } from "./dep.js";
                     export function read() { return counter; }
                     """;

        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = source,
            ["/mods/dep.js"] = "export let counter = 0;"
        });
        var linker = new ModuleLinker(() => loader);
        var parsed = JavaScriptParser.ParseModule(source);
        var plan = linker.BuildPlan("/mods/main.js", parsed);
        var readDeclaration = parsed.Statements.OfType<JsExportDeclarationStatement>()
            .Select(static statement => statement.Declaration)
            .OfType<JsFunctionDeclaration>()
            .First(fn => string.Equals(fn.Name, "read", StringComparison.Ordinal));

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        using var compiler = JsCompiler.CreateForModuleExecution(
            realm,
            BuildCompileModuleBindings(plan.ResolvedImportBindings, plan.ExecutionPlan));
        var readFn = compiler.CompileHoistedFunctionTemplate(readDeclaration, parsed.SourceText, "/mods/main.js");
        var disasm = Disassembler.Dump(readFn.Script, new()
        {
            UnitKind = "function",
            UnitName = "read"
        });

        Assert.That(disasm, Does.Contain("LdaModuleVariable"));
        Assert.That(disasm, Does.Not.Contain("LdaContextSlot"));
        Assert.That(disasm, Does.Not.Contain("GetCurrentModuleImports"));
    }

    [Test]
    public void Compile_ModuleHoistedFunctionWithoutNestedCaptures_DoesNotCreateFunctionContext()
    {
        var source = """
                     export const value = 1;
                     export function read() { return value + 1; }
                     """;

        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = source
        });
        var linker = new ModuleLinker(() => loader);
        var parsed = JavaScriptParser.ParseModule(source);
        var plan = linker.BuildPlan("/mods/main.js", parsed);
        var readDeclaration = parsed.Statements.OfType<JsExportDeclarationStatement>()
            .Select(static statement => statement.Declaration)
            .OfType<JsFunctionDeclaration>()
            .First(fn => string.Equals(fn.Name, "read", StringComparison.Ordinal));

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        using var compiler = JsCompiler.CreateForModuleExecution(
            realm,
            BuildCompileModuleBindings(plan.ResolvedImportBindings, plan.ExecutionPlan));
        var readFn = compiler.CompileHoistedFunctionTemplate(readDeclaration, parsed.SourceText, "/mods/main.js");
        var disasm = Disassembler.Dump(readFn.Script, new()
        {
            UnitKind = "function",
            UnitName = "read"
        });

        Assert.That(disasm, Does.Not.Contain("CreateFunctionContextWithCells"));
    }

    [Test]
    public void Compile_ModuleHoistedFunction_LaterParameters_DoNotResolve_AsGlobals()
    {
        var source = """
                     export function read(a, b, c) { return `${b}|${c}`; }
                     """;

        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = source
        });
        var linker = new ModuleLinker(() => loader);
        var parsed = JavaScriptParser.ParseModule(source);
        var plan = linker.BuildPlan("/mods/main.js", parsed);
        var readDeclaration = parsed.Statements.OfType<JsExportDeclarationStatement>()
            .Select(static statement => statement.Declaration)
            .OfType<JsFunctionDeclaration>()
            .First(fn => string.Equals(fn.Name, "read", StringComparison.Ordinal));

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        using var compiler = JsCompiler.CreateForModuleExecution(
            realm,
            BuildCompileModuleBindings(plan.ResolvedImportBindings, plan.ExecutionPlan));
        var readFn = compiler.CompileHoistedFunctionTemplate(
            readDeclaration,
            parsed.SourceText,
            "/mods/main.js",
            parsed.IdentifierTable);
        var disasm = Disassembler.Dump(readFn.Script, new()
        {
            UnitKind = "function",
            UnitName = "read"
        });

        Assert.That(disasm, Does.Not.Contain("LdaGlobal"));
    }

    [Test]
    public void Compile_ModuleHoistedFunction_BlockCapturePattern_DoesNotResolve_Parameters_AsGlobals()
    {
        var source = """
                     export function help(commands, base$0, parentCommands) {
                       if (commands.length) {
                         const prefix = base$0 ? `${base$0} ` : '';
                         commands.forEach(command => {
                           const commandString = `${prefix}${parentCommands}${command[0]}`;
                           return commandString;
                         });
                       }
                     }
                     """;

        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = source
        });
        var linker = new ModuleLinker(() => loader);
        var parsed = JavaScriptParser.ParseModule(source);
        var plan = linker.BuildPlan("/mods/main.js", parsed);
        var helpDeclaration = parsed.Statements.OfType<JsExportDeclarationStatement>()
            .Select(static statement => statement.Declaration)
            .OfType<JsFunctionDeclaration>()
            .First(fn => string.Equals(fn.Name, "help", StringComparison.Ordinal));

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        using var compiler = JsCompiler.CreateForModuleExecution(
            realm,
            BuildCompileModuleBindings(plan.ResolvedImportBindings, plan.ExecutionPlan));
        var helpFn = compiler.CompileHoistedFunctionTemplate(
            helpDeclaration,
            parsed.SourceText,
            "/mods/main.js",
            parsed.IdentifierTable);
        var disasm = Disassembler.Dump(helpFn.Script, new()
        {
            UnitKind = "function",
            UnitName = "help"
        });

        Assert.That(disasm, Does.Not.Contain("LdaGlobal name:1"));
        Assert.That(disasm, Does.Not.Contain("LdaGlobal name:3"));
    }

    [Test]
    public void Compile_NestedFunction_With_Many_Captured_Locals_Uses_Wide_Function_Context_Cells()
    {
        var declarations = string.Join(Environment.NewLine,
            Enumerable.Range(0, 300).Select(static i => $"const v{i} = {i};"));
        var captures = string.Join(", ",
            Enumerable.Range(0, 300).Select(static i => $"v{i}"));
        var source = $$"""
                       {{declarations}}
                       function read() {
                         return function inner() {
                           return [{{captures}}];
                         };
                       }
                       """;
        var parsed = JavaScriptParser.ParseScript(source);

        using var engine = JsRuntime.CreateBuilder().Build();
        var realm = engine.MainRealm;
        var script = JsCompiler.Compile(realm, parsed);
        var scriptDisasm = Disassembler.Dump(script, new()
        {
            UnitKind = "script",
            UnitName = "main"
        });
        var readFn = script.ObjectConstants.OfType<JsBytecodeFunction>()
            .First(fn => string.Equals(fn.Name, "read", StringComparison.Ordinal));
        var readDisasm = Disassembler.Dump(readFn.Script, new()
        {
            UnitKind = "function",
            UnitName = "read"
        });
        var innerFn = readFn.Script.ObjectConstants.OfType<JsBytecodeFunction>()
            .First(fn => string.Equals(fn.Name, "inner", StringComparison.Ordinal));
        var disasm = Disassembler.Dump(innerFn.Script, new()
        {
            UnitKind = "function",
            UnitName = "inner"
        });

        Assert.That(scriptDisasm, Does.Contain("CreateFunctionContextWithCellsWide"));
        Assert.That(scriptDisasm, Does.Contain("slots:300"));
        Assert.That(disasm, Does.Contain("LdaContextSlot"));
        Assert.That(disasm, Does.Contain("slot:299"));
    }

    [Test]
    public void Compile_GiantWrapper_ObjectLiteral_Uses_Ushort_Object_Register_For_InitializeNamedProperty()
    {
        var wrapperSource = new StringBuilder();
        wrapperSource.AppendLine("(function (exports, require, module, __filename, __dirname) {");
        for (var i = 0; i < 280; i++)
            wrapperSource.AppendLine($"  const t{i} = {i};");
        wrapperSource.AppendLine("  module.exports = {");
        for (var i = 0; i < 12; i++)
            wrapperSource.AppendLine($"    p{i}: () => t{i},");
        wrapperSource.AppendLine("    resolveEventTimeStamp: () => 123");
        wrapperSource.AppendLine("  };");
        wrapperSource.AppendLine("})");

        var parsed = JavaScriptParser.ParseScript(wrapperSource.ToString());
        var wrapperExpression = (JsFunctionExpression)((JsExpressionStatement)parsed.Statements[0]).Expression;

        using var engine = JsRuntime.CreateBuilder().Build();
        var realm = engine.MainRealm;
        using var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            wrapperExpression,
            string.Empty,
            wrapperSource.ToString(),
            "giant-wrapper.js",
            parsed.IdentifierTable);
        var disasm = Disassembler.Dump(wrapper.Script, new()
        {
            UnitKind = "function",
            UnitName = "<wrapper>"
        });

        Assert.That(disasm, Does.Contain("InitializeNamedProperty obj:r"));
    }

    [Test]
    public void Compile_GiantWrapper_ObjectLiteral_Uses_Wide_Register_Transport_For_Object_Temp()
    {
        var wrapperSource = new StringBuilder();
        wrapperSource.AppendLine("(function (exports, require, module, __filename, __dirname) {");
        for (var i = 0; i < 520; i++)
            wrapperSource.AppendLine($"  const t{i} = {i};");
        wrapperSource.AppendLine("  const details = {");
        wrapperSource.AppendLine("    color: 'primary',");
        wrapperSource.AppendLine("    properties: null,");
        wrapperSource.AppendLine("    tooltipText: '',");
        wrapperSource.AppendLine("    track: 'Components'");
        wrapperSource.AppendLine("  };");
        wrapperSource.AppendLine("  module.exports = {");
        wrapperSource.AppendLine("    reusableComponentOptions: { start: -0, end: -0, detail: { devtools: details } }");
        wrapperSource.AppendLine("  };");
        wrapperSource.AppendLine("})");

        var parsed = JavaScriptParser.ParseScript(wrapperSource.ToString());
        var wrapperExpression = (JsFunctionExpression)((JsExpressionStatement)parsed.Statements[0]).Expression;

        using var engine = JsRuntime.CreateBuilder().Build();
        var realm = engine.MainRealm;
        using var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            wrapperExpression,
            string.Empty,
            wrapperSource.ToString(),
            "giant-wrapper-wide-temp.js",
            parsed.IdentifierTable);
        var disasm = Disassembler.Dump(wrapper.Script, new()
        {
            UnitKind = "function",
            UnitName = "<wrapper>"
        });

        Assert.That(disasm, Does.Contain("StarWide").Or.Contain("LdarWide").Or.Contain("MovWide"));
    }

    [Test]
    public void Compile_GiantWrapper_NamedPropertyLoad_Uses_Ushort_Object_Register_For_Wide_Form()
    {
        var wrapperSource = new StringBuilder();
        wrapperSource.AppendLine("(function (exports, require, module, __filename, __dirname) {");
        for (var i = 0; i < 520; i++)
            wrapperSource.AppendLine($"  const t{i} = {i};");
        wrapperSource.AppendLine("  const React = require('react');");
        wrapperSource.AppendLine("  return React.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;");
        wrapperSource.AppendLine("})");

        var parsed = JavaScriptParser.ParseScript(wrapperSource.ToString());
        var wrapperExpression = (JsFunctionExpression)((JsExpressionStatement)parsed.Statements[0]).Expression;

        using var engine = JsRuntime.CreateBuilder().Build();
        var realm = engine.MainRealm;
        using var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            wrapperExpression,
            string.Empty,
            wrapperSource.ToString(),
            "giant-wrapper-wide-named-load.js",
            parsed.IdentifierTable);
        var disasm = Disassembler.Dump(wrapper.Script, new()
        {
            UnitKind = "function",
            UnitName = "<wrapper>"
        });

        Assert.That(disasm, Does.Contain("LdaNamedPropertyWide obj:r"));
        Assert.That(disasm, Does.Match(@"LdaNamedPropertyWide obj:r\d{3,}"));
    }

    [Test]
    public void Compile_GiantWrapper_Chained_Require_Declarators_Preserve_Require_Parameter_Register()
    {
        var wrapperSource = new StringBuilder();
        wrapperSource.AppendLine("(function (exports, require, module, __filename, __dirname) {");
        for (var i = 0; i < 520; i++)
            wrapperSource.AppendLine($"  const t{i} = {i};");
        wrapperSource.AppendLine("  var React = require(\"react\"),");
        wrapperSource.AppendLine("      Scheduler = require(\"scheduler\");");
        wrapperSource.AppendLine("  return [React, Scheduler];");
        wrapperSource.AppendLine("})");

        var parsed = JavaScriptParser.ParseScript(wrapperSource.ToString());
        var wrapperExpression = (JsFunctionExpression)((JsExpressionStatement)parsed.Statements[0]).Expression;

        using var engine = JsRuntime.CreateBuilder().Build();
        var realm = engine.MainRealm;
        using var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            wrapperExpression,
            string.Empty,
            wrapperSource.ToString(),
            "giant-wrapper-require-preserve.js",
            parsed.IdentifierTable);
        var disasm = Disassembler.Dump(wrapper.Script, new()
        {
            UnitKind = "function",
            UnitName = "<wrapper>"
        });

        Assert.That(disasm, Does.Contain("CallUndefinedReceiver func:r1"));
        Assert.That(disasm, Does.Contain("CallUndefinedReceiver func:r1, args:r"));
        Assert.That(disasm, Does.Not.Contain("CallUndefinedReceiver func:r13"));
    }

    private static Dictionary<string, ModuleVariableBinding> BuildCompileModuleBindings(
        IReadOnlyList<JsResolvedImportBinding> importBindings,
        ModuleExecutionPlan executionPlan)
    {
        var map = new Dictionary<string, ModuleVariableBinding>(
            importBindings.Count + executionPlan.ExportLocalByName.Count,
            StringComparer.Ordinal);

        var importCount = 0;
        for (var i = 0; i < importBindings.Count; i++)
        {
            var binding = importBindings[i];
            if (map.ContainsKey(binding.LocalName))
                continue;

            var cellIndex = unchecked((sbyte)-(importCount + 1));
            map.Add(binding.LocalName, new(cellIndex, 0, true));
            importCount++;
        }

        var exportCount = 0;
        foreach (var pair in executionPlan.ExportLocalByName)
        {
            var localName = pair.Value;
            if (map.ContainsKey(localName))
                continue;

            var cellIndex = unchecked((sbyte)(exportCount + 1));
            map.Add(localName, new(cellIndex, 0, false));
            exportCount++;
        }

        return map;
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        private readonly Dictionary<string, string> modules = modules;

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (specifier.StartsWith("./", StringComparison.Ordinal))
            {
                var basePath = referrer is null
                    ? "/"
                    : referrer.Replace('\\', '/');
                var slash = basePath.LastIndexOf('/');
                var dir = slash >= 0 ? basePath[..(slash + 1)] : "/";
                return Normalize(dir + specifier[2..]);
            }

            return Normalize(specifier);
        }

        public string LoadSource(string resolvedId)
        {
            if (modules.TryGetValue(Normalize(resolvedId), out var source))
                return source;
            throw new InvalidOperationException("Module not found: " + resolvedId);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}

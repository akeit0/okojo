using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected void EmitParameterPrologue(FlatAst ast, in FlatFunctionInfo function)
    {
        if (function.HasSimpleParameterList)
            return;

        var parameters = ast.GetParameters(function);
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var restValueRegister = -1;
            if (function.RestParameterIndex >= 0)
            {
                if ((uint)function.RestParameterIndex > byte.MaxValue)
                    throw new NotSupportedException(
                        "Flat rest parameter index exceeds byte operand capacity."
                    );
                builder.Emit(JsOpCode.CreateRestParameter, (byte)function.RestParameterIndex);
                restValueRegister = builder.AllocateTemporaryRegister();
                EmitStar(restValueRegister);
            }

            var argumentSnapshot = builder.AllocateTemporaryRegisterBlock(parameters.Length);
            for (var i = 0; i < parameters.Length; i++)
            {
                EmitLdar(i);
                EmitStar(argumentSnapshot + i);
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                ref readonly var parameter = ref parameters[i];
                if (parameter.PatternNode >= 0)
                    EmitInitializeParameterPatternHoles(ast, parameter.PatternNode);
                else
                    EmitInitializeParameterHole(ast.GetString(parameter.NameStringIndex));
            }

            emittingParameterInitializers = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                ref readonly var parameter = ref parameters[i];
                if (
                    parameter.Kind
                    is JsFormalParameterBindingKind.Rest
                        or JsFormalParameterBindingKind.RestPattern
                )
                    EmitLdar(restValueRegister);
                else
                {
                    EmitLdar(argumentSnapshot + i);
                    if (parameter.InitializerNode >= 0)
                    {
                        var useDefault = builder.CreateLabel();
                        var initialized = builder.CreateLabel();
                        EmitJumpIfUndefined(useDefault);
                        EmitJump(initialized);
                        builder.BindLabel(useDefault);
                        if (parameter.PatternNode >= 0)
                            EmitExpression(ast, parameter.InitializerNode);
                        else
                            EmitExpressionWithInferredName(
                                ast,
                                parameter.InitializerNode,
                                ast.GetString(parameter.NameStringIndex)
                            );
                        builder.BindLabel(initialized);
                    }
                }

                if (parameter.PatternNode >= 0)
                    EmitStoreBindingTarget(ast, parameter.PatternNode);
                else
                    EmitStoreParameter(ast.GetString(parameter.NameStringIndex));
            }
        }
        finally
        {
            emittingParameterInitializers = false;
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitInitializeParameterPatternHoles(FlatAst ast, int nodeIndex)
    {
        ref readonly var node = ref ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.Identifier:
                EmitInitializeParameterHole(ast.GetString(node.Arg0));
                return;
            case AstKind.AssignmentExpression:
            case AstKind.SpreadElement:
                EmitInitializeParameterPatternHoles(ast, node.Arg0);
                return;
            case AstKind.ArrayBindingPattern:
                var elements = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < elements.Length; i++)
                    if (elements[i] >= 0)
                        EmitInitializeParameterPatternHoles(ast, elements[i]);
                return;
            case AstKind.ObjectBindingPattern:
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                    EmitInitializeParameterPatternHoles(ast, properties[i].ValueNode);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support parameter pattern '{node.Kind}'."
                );
        }
    }

    private void EmitInitializeParameterHole(string name)
    {
        builder.EmitLda(JsOpCode.LdaTheHole);
        EmitStoreParameter(name);
    }

    private void EmitStoreParameter(string name)
    {
        if (!TryResolveBinding(name, out var binding))
            throw new InvalidOperationException(
                $"No planned parameter binding found for '{name}'."
            );
        EmitInitializeParameterStore(binding);
    }

    private void EmitInitializeParameterStore(BindingStorage binding)
    {
        if (binding.Planned.StorageKind == CompilerPlannedStorageKind.LexicalRegister)
            EmitStar(binding.Register);
        else
            EmitStore(binding);
    }
}

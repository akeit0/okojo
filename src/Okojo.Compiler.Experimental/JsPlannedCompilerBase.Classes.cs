using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    private void EmitClassDeclaration(FlatAst ast, in AstNode node)
    {
        var info = ast.GetClass(node.Arg0);
        EmitClassExpression(ast, node.Arg0);
        var name = ast.GetString(info.NameStringIndex);
        if (!TryResolveBinding(name, out var binding))
            throw new InvalidOperationException($"No planned class binding found for '{name}'.");
        EmitStore(binding, isInitialization: true);
    }

    private void EmitClassExpression(FlatAst ast, int classIndex)
    {
        var info = ast.GetClass(classIndex);
        if (info.HasExtends)
            throw new NotSupportedException("Flat class heritage is not implemented yet.");

        var classScope = FindChildScope(
            activeScopes.Peek().ScopeId,
            CompilerCollectedScopeKind.Class,
            info.Position
        );
        EnterScope(classScope.ScopeId);
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var name = ast.GetString(info.NameStringIndex);
            ref readonly var constructor = ref ast[info.ConstructorNode];
            EmitFunctionExpression(
                ast,
                constructor.Arg0,
                constructor.Arg1,
                name.Length == 0 ? null : name
            );
            var constructorRegister = builder.AllocateTemporaryRegister();
            EmitStar(constructorRegister);

            if (name.Length != 0)
            {
                if (!TryResolveBinding(name, out var classAlias))
                    throw new InvalidOperationException(
                        $"No planned class lexical binding found for '{name}'."
                    );
                EmitLdar(constructorRegister);
                EmitStore(classAlias, isInitialization: true);
            }

            builder.EmitCallRuntime(
                (int)RuntimeId.ClassGetPrototypeAndSetConstructor,
                constructorRegister,
                1
            );
            var prototypeRegister = builder.AllocateTemporaryRegister();
            EmitStar(prototypeRegister);

            var elements = ast.GetClassElements(info);
            for (var i = 0; i < elements.Length; i++)
            {
                ref readonly var element = ref elements[i];
                if (element.Kind == JsClassElementKind.Constructor)
                    continue;
                EmitClassElement(ast, element, constructorRegister, prototypeRegister);
            }

            EmitLdar(constructorRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
            LeaveScope();
        }
    }

    private void EmitClassElement(
        FlatAst ast,
        in FlatClassElement element,
        int constructorRegister,
        int prototypeRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var targetRegister = element.IsStatic ? constructorRegister : prototypeRegister;
            if (element.Kind is JsClassElementKind.Getter or JsClassElementKind.Setter)
            {
                var arguments = builder.AllocateTemporaryRegisterBlock(4);
                EmitLdar(targetRegister);
                EmitStar(arguments);
                EmitClassElementKey(ast, element, arguments + 1);
                if (element.Kind == JsClassElementKind.Getter)
                    EmitClassElementFunction(ast, element);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(arguments + 2);
                if (element.Kind == JsClassElementKind.Setter)
                    EmitClassElementFunction(ast, element);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(arguments + 3);
                builder.EmitCallRuntime((int)RuntimeId.DefineClassAccessor, arguments, 4);
                return;
            }

            if (element.Kind != JsClassElementKind.Method)
                throw new NotSupportedException(
                    $"Flat class element '{element.Kind}' is not implemented yet."
                );
            var methodArguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitLdar(targetRegister);
            EmitStar(methodArguments);
            EmitClassElementKey(ast, element, methodArguments + 1);
            EmitClassElementFunction(ast, element);
            EmitStar(methodArguments + 2);
            builder.EmitCallRuntime((int)RuntimeId.DefineClassMethod, methodArguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitClassElementKey(FlatAst ast, in FlatClassElement element, int keyRegister)
    {
        if (element.IsComputed)
        {
            EmitExpression(ast, element.Key);
            EmitStar(keyRegister);
            builder.EmitCallRuntime((int)RuntimeId.NormalizePropertyKey, keyRegister, 1);
        }
        else
            EmitStringLiteral(ast.GetString(element.Key));
        EmitStar(keyRegister);
    }

    private void EmitClassElementFunction(FlatAst ast, in FlatClassElement element)
    {
        ref readonly var function = ref ast[element.ValueNode];
        EmitFunctionExpression(
            ast,
            function.Arg0,
            function.Arg1,
            element.IsComputed ? null : ast.GetString(element.Key)
        );
    }
}

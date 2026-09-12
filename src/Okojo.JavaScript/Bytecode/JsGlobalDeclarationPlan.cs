namespace Okojo.JavaScript.Bytecode;

internal enum JsGlobalDeclarationKind : byte
{
    Lexical,
    Var,
    Function,
}

internal readonly record struct JsGlobalDeclaration(string Name, JsGlobalDeclarationKind Kind);

/// <summary>Symbolic declaration obligations, checked at link/execution, not emission.</summary>
internal sealed class JsGlobalDeclarationPlan(JsGlobalDeclaration[] declarations)
{
    internal int[] LinkAtoms(JsRealm realm)
    {
        var atoms = new int[declarations.Length];
        for (var i = 0; i < atoms.Length; i++)
            atoms[i] = realm.Atoms.InternNoCheck(declarations[i].Name);
        return atoms;
    }

    internal void Validate(JsRealm realm, int[] atoms)
    {
        for (var i = 0; i < declarations.Length; i++)
        {
            var declaration = declarations[i];
            var atom = atoms[i];
            if (declaration.Kind == JsGlobalDeclarationKind.Lexical)
            {
                if (
                    realm.HasGlobalLexicalBindingAtom(atom)
                    || realm.GlobalObject.HasRestrictedGlobalPropertyAtom(atom)
                )
                    throw Error(
                        JsErrorKind.SyntaxError,
                        declaration.Name,
                        "SCRIPT_GLOBAL_LEXICAL_CONFLICT"
                    );
                continue;
            }
            if (realm.HasGlobalLexicalBindingAtom(atom))
                throw Error(
                    JsErrorKind.SyntaxError,
                    declaration.Name,
                    "SCRIPT_GLOBAL_VAR_LEXICAL_CONFLICT"
                );
            var isFunction = declaration.Kind == JsGlobalDeclarationKind.Function;
            var canDeclare = isFunction
                ? realm.GlobalObject.CanDeclareGlobalFunctionAtom(atom)
                : realm.GlobalObject.CanDeclareGlobalVarAtom(atom);
            if (!canDeclare)
                throw Error(
                    JsErrorKind.TypeError,
                    declaration.Name,
                    isFunction
                        ? "SCRIPT_GLOBAL_FUNCTION_NOT_DEFINABLE"
                        : "SCRIPT_GLOBAL_VAR_NOT_DEFINABLE"
                );
        }
    }

    private static JsRuntimeException Error(JsErrorKind kind, string name, string code) =>
        new(kind, $"Identifier '{name}' has already been declared", code);
}

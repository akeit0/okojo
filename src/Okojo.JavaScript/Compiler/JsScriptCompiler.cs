using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Compiler;

internal sealed partial class JsScriptCompiler : JsCompilerBase
{
    private readonly JsRealm? targetRealm;

    public JsScriptCompiler(JsRealm realm)
        : this(realm.CompilationPool)
    {
        targetRealm = realm;
    }

    internal JsScriptCompiler(CompileCollectionPool pool)
        : base(pool) { }

    private JsRealm TargetRealm =>
        targetRealm
        ?? throw new InvalidOperationException("Link the compilation unit to an explicit realm.");
}

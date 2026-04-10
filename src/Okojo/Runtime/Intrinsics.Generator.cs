namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsPlainObject GeneratorObjectPrototype { get; set; } = null!;
    internal JsHostFunction GeneratorNextFunction { get; set; } = null!;

    private void InstallGeneratorPrototypeBuiltins()
    {
        GeneratorObjectPrototype = new(Realm, false)
        {
            Prototype = IteratorPrototype
        };

        var nextFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsGeneratorObject generator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Generator.prototype.next called on incompatible receiver");
            var input = args.Length != 0 ? args[0] : JsValue.Undefined;
            return realm.ResumeGeneratorObject(generator, GeneratorResumeMode.Next, input);
        }, "next", 1);
        GeneratorNextFunction = nextFn;

        var returnFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsGeneratorObject generator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Generator.prototype.return called on incompatible receiver");
            var input = args.Length != 0 ? args[0] : JsValue.Undefined;
            return realm.ResumeGeneratorObject(generator, GeneratorResumeMode.Return, input);
        }, "return", 1);

        var throwFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsGeneratorObject generator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Generator.prototype.throw called on incompatible receiver");
            var input = args.Length != 0 ? args[0] : JsValue.Undefined;
            return realm.ResumeGeneratorObject(generator, GeneratorResumeMode.Throw, input);
        }, "throw", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(GeneratorFunctionPrototype),
                configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Generator"),
                configurable: true),
            PropertyDefinition.Mutable(IdNext, JsValue.FromObject(nextFn)),
            PropertyDefinition.Mutable(IdReturn, JsValue.FromObject(returnFn)),
            PropertyDefinition.Mutable(IdThrow, JsValue.FromObject(throwFn))
        ];
        GeneratorObjectPrototype.DefineNewPropertiesNoCollision(Realm, defs);

        Span<PropertyDefinition> generatorFunctionPrototypeDefs =
        [
            PropertyDefinition.Const(IdPrototype, GeneratorObjectPrototype, configurable: true)
        ];
        GeneratorFunctionPrototype.DefineNewPropertiesNoCollision(Realm, generatorFunctionPrototypeDefs);
        GeneratorObjectPrototypeForFunctions = GeneratorObjectPrototype;
    }
}

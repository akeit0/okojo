using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    internal void InstallIntrinsics()
    {
        InstallObjectPrototypeBuiltins();
        InstallArrayPrototypeBuiltins();
        InstallFunctionConstructorBuiltins();
        InstallObjectConstructorBuiltins();
        InstallArrayConstructorBuiltins();
        InstallErrorConstructorBuiltins();
        InstallBigIntConstructorBuiltins();
        InstallArrayBufferConstructorBuiltins();
        InstallSharedArrayBufferConstructorBuiltins();
        InstallIteratorConstructorBuiltins();
        InstallTypedArrayConstructorBuiltins();
        InstallDataViewBuiltins();
        InstallMapConstructorBuiltins();
        InstallSetConstructorBuiltins();
        InstallWeakCollectionBuiltins();
        InstallSymbolConstructorBuiltins();
        InstallPromiseConstructorBuiltins();
        InstallProxyConstructorBuiltins();
        InstallReflectBuiltins();
        InstallRegExpConstructorBuiltins();
        InstallDateConstructorBuiltins();
        InstallIntlBuiltins();
        InstallMathBuiltins();
        InstallJsonBuiltins();
        InstallBoxedPrototypeBuiltins();
        InstallGeneratorPrototypeBuiltins();
        Realm.InstallAsyncGeneratorPrototypeBuiltins();
        Realm.Global["Object"] = ObjectConstructor;
        Realm.Global["Array"] = ArrayConstructor;
        Realm.Global["Function"] = FunctionConstructor;
        Realm.Global["Error"] = ErrorConstructor;
        Realm.Global["TypeError"] = TypeErrorConstructor;
        Realm.Global["ReferenceError"] = ReferenceErrorConstructor;
        Realm.Global["RangeError"] = RangeErrorConstructor;
        Realm.Global["SyntaxError"] = SyntaxErrorConstructor;
        Realm.Global["EvalError"] = EvalErrorConstructor;
        Realm.Global["URIError"] = UriErrorConstructor;
        Realm.Global["AggregateError"] = AggregateErrorConstructor;
        Realm.Global["Number"] = NumberConstructor;
        Realm.Global["Boolean"] = BooleanConstructor;
        Realm.Global["String"] = StringConstructor;
        Realm.Global["BigInt"] = BigIntConstructor;
        Realm.Global["ArrayBuffer"] = ArrayBufferConstructor;
        Realm.Global["SharedArrayBuffer"] = SharedArrayBufferConstructor;
        Realm.Global["Atomics"] = AtomicsObject;
        Realm.Global["Iterator"] = IteratorConstructor;
        Realm.Global["Int8Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Int8);
        Realm.Global["Uint8Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Uint8);
        Realm.Global["Uint8ClampedArray"] = GetTypedArrayConstructor(TypedArrayElementKind.Uint8Clamped);
        Realm.Global["Int16Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Int16);
        Realm.Global["Uint16Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Uint16);
        Realm.Global["Int32Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Int32);
        Realm.Global["Uint32Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Uint32);
        Realm.Global["Float16Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Float16);
        Realm.Global["Float32Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Float32);
        Realm.Global["Float64Array"] = GetTypedArrayConstructor(TypedArrayElementKind.Float64);
        Realm.Global["BigInt64Array"] = GetTypedArrayConstructor(TypedArrayElementKind.BigInt64);
        Realm.Global["BigUint64Array"] = GetTypedArrayConstructor(TypedArrayElementKind.BigUint64);
        Realm.Global["DataView"] = DataViewConstructor;
        Realm.Global["Map"] = MapConstructor;
        Realm.Global["Set"] = SetConstructor;
        Realm.Global["WeakMap"] = WeakMapConstructor;
        Realm.Global["WeakSet"] = WeakSetConstructor;
        Realm.Global["WeakRef"] = WeakRefConstructor;
        Realm.Global["FinalizationRegistry"] = FinalizationRegistryConstructor;
        Realm.Global["RegExp"] = RegExpConstructor;
        Realm.Global["Date"] = DateConstructor;
        Realm.Global["eval"] = CreateGlobalEvalFunction();
        Realm.Global["isFinite"] = CreateGlobalIsFiniteFunction();
        Realm.Global["isNaN"] = CreateGlobalIsNaNFunction();
        Realm.Global["encodeURI"] = CreateGlobalEncodeUriFunction(false);
        Realm.Global["encodeURIComponent"] = CreateGlobalEncodeUriFunction(true);
        Realm.Global["decodeURI"] = CreateGlobalDecodeUriFunction(true);
        Realm.Global["decodeURIComponent"] = CreateGlobalDecodeUriFunction(false);
        var parseIntFn = CreateGlobalParseIntFunction();
        var parseFloatFn = CreateGlobalParseFloatFunction();
        Realm.Global["parseInt"] = parseIntFn;
        Realm.Global["parseFloat"] = parseFloatFn;
        Span<PropertyDefinition> numberParseDefs =
        [
            PropertyDefinition.Mutable(IdParseInt, JsValue.FromObject(parseIntFn)),
            PropertyDefinition.Mutable(IdParseFloat, JsValue.FromObject(parseFloatFn))
        ];
        NumberConstructor.DefineNewPropertiesNoCollision(Realm, numberParseDefs);
        const int atomNaN = IdNaN;
        const int atomInfinity = IdInfinity;
        const int atomUndefined = IdUndefined;
        Realm.GlobalObject.DefineOwnGlobalDataPropertyAtom(atomNaN, JsValue.NaN, false, false,
            false);
        Realm.GlobalObject.DefineOwnGlobalDataPropertyAtom(atomInfinity, new(double.PositiveInfinity),
            false, false, false);
        Realm.GlobalObject.DefineOwnGlobalDataPropertyAtom(atomUndefined, JsValue.Undefined, false,
            false, false);
        Realm.Global["Symbol"] = SymbolConstructor;
        Realm.Global["Promise"] = PromiseConstructor;
        Realm.Global["Proxy"] = ProxyConstructor;
        Realm.Global["Reflect"] = CreateReflectObject();
        Realm.Global["Intl"] = CreateIntlObject();
        Realm.Global["Math"] = CreateMathObject();
        Realm.Global["JSON"] = CreateJsonObject();
        if (Realm.Engine.IsClrAccessEnabled)
        {
            Realm.Global["clr"] = JsValue.FromObject(Realm.GetClrNamespace());
            Realm.Global["$null"] = JsValue.FromObject(Realm.CreateClrTypedNullHelperFunction());
            Realm.Global["$place"] = JsValue.FromObject(Realm.CreateClrPlaceHolderHelperFunction());
            Realm.Global["$cast"] = JsValue.FromObject(Realm.CreateClrCastHelperFunction());
            Realm.Global["$using"] = JsValue.FromObject(Realm.CreateClrUsingHelperFunction());
        }

        var apiModules = Realm.Engine.Options.RealmApiModules;
        for (var i = 0; i < apiModules.Count; i++)
            apiModules[i].Install(Realm);
    }


    private JsHostFunction CreateObjectConstructor()
    {
        return JsHostFunction.CreateEmptyShapedFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var callee = (JsHostFunction)info.Function;
            var args = info.Arguments;
            if (info.IsConstruct &&
                info.NewTarget.TryGetObject(out var newTargetObj) &&
                !ReferenceEquals(newTargetObj, callee))
            {
                var prototype = info.Realm.Intrinsics.GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                    callee.Realm.ObjectPrototype);
                return new JsPlainObject(realm, false)
                {
                    Prototype = prototype
                };
            }

            if (args.Length == 0 || args[0].IsUndefined || args[0].IsNull)
                return new JsPlainObject(realm);
            if (args[0].TryGetObject(out _))
                return args[0];
            return realm.BoxPrimitive(args[0]);
        }, "Object", 1);
    }

    private JsHostFunction CreateBooleanConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var value = args.Length > 0 && JsRealm.ToBoolean(args[0]);
            if (info.IsConstruct)
            {
                var callee = (JsHostFunction)info.Function;
                var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                    callee.Realm.BooleanPrototype);
                return new JsBooleanObject(realm, value, prototype);
            }

            return value ? JsValue.True : JsValue.False;
        }, "Boolean", 1, true);
    }

    private JsHostFunction CreateNumberConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var value = args.Length == 0 ? 0d : realm.ToNumberConstructorValue(args[0]);
            if (info.IsConstruct)
            {
                var callee = (JsHostFunction)info.Function;
                var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                    callee.Realm.NumberPrototype);
                return new JsNumberObject(realm, value, prototype);
            }

            return new(value);
        }, "Number", 1, true);
    }

    private JsHostFunction CreateStringConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            JsString value;
            if (args.Length == 0)
                value = JsString.Empty;
            else if (!info.IsConstruct && args[0].IsSymbol)
                value = args[0].AsSymbol().ToString();
            else
                value = realm.ToJsStringSlowPath(args[0]);

            if (!info.IsConstruct)
                return JsValue.FromString(value);

            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                callee.Realm.StringPrototype);
            return new JsStringObject(realm, value, prototype);
        }, "String", 1, true);
    }

    private JsHostFunction CreateFunctionConstructor()
    {
        return CreateDynamicFunctionConstructor("Function", JsBytecodeFunctionKind.Normal,
            FunctionPrototype, "function");
    }

    private JsHostFunction CreateGeneratorFunctionConstructor()
    {
        return CreateDynamicFunctionConstructor("GeneratorFunction", JsBytecodeFunctionKind.Generator,
            GeneratorFunctionPrototype, "function*");
    }

    private JsHostFunction CreateAsyncFunctionConstructor()
    {
        return CreateDynamicFunctionConstructor("AsyncFunction", JsBytecodeFunctionKind.Async,
            AsyncFunctionPrototype, "async function");
    }

    private JsHostFunction CreateAsyncGeneratorFunctionConstructor()
    {
        return CreateDynamicFunctionConstructor("AsyncGeneratorFunction", JsBytecodeFunctionKind.AsyncGenerator,
            AsyncGeneratorFunctionPrototype, "async function*");
    }

    private JsHostFunction CreateDynamicFunctionConstructor(
        string name,
        JsBytecodeFunctionKind kind,
        JsObject intrinsicPrototype,
        string prefix)
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var callee = (JsHostFunction)info.Function;
            var data = (DynamicFunctionConstructorData)callee.UserData!;
            return CreateDynamicFunction(callee, info.NewTarget, info.Arguments, data);
        }, name, 1, true)
        {
            UserData = new DynamicFunctionConstructorData(kind, intrinsicPrototype, prefix)
        };
    }

    private void InstallFunctionConstructorBuiltins()
    {
        FunctionConstructor.InitializePrototypeProperty(FunctionPrototype);

        GeneratorFunctionConstructor.InitializePrototypeProperty(GeneratorFunctionPrototype);
        AsyncFunctionConstructor.InitializePrototypeProperty(AsyncFunctionPrototype);
        AsyncGeneratorFunctionConstructor.InitializePrototypeProperty(AsyncGeneratorFunctionPrototype);
        Span<PropertyDefinition> generatorPrototypeDefs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(GeneratorFunctionConstructor),
                configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("GeneratorFunction"),
                configurable: true)
        ];
        GeneratorFunctionPrototype.DefineNewPropertiesNoCollision(Realm, generatorPrototypeDefs);

        Span<PropertyDefinition> asyncPrototypeDefs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(AsyncFunctionConstructor),
                configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("AsyncFunction"),
                configurable: true)
        ];
        AsyncFunctionPrototype.DefineNewPropertiesNoCollision(Realm, asyncPrototypeDefs);

        Span<PropertyDefinition> asyncGeneratorPrototypeDefs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(AsyncGeneratorFunctionConstructor),
                configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("AsyncGeneratorFunction"),
                configurable: true)
        ];
        AsyncGeneratorFunctionPrototype.DefineNewPropertiesNoCollision(Realm, asyncGeneratorPrototypeDefs);
    }

    private JsValue CreateDynamicFunction(
        JsHostFunction callee,
        in JsValue newTarget,
        ReadOnlySpan<JsValue> args,
        DynamicFunctionConstructorData data)
    {
        var functionRealm = callee.Realm;
        var parameters = Array.Empty<string>();
        if (args.Length > 1)
        {
            parameters = new string[args.Length - 1];
            for (var i = 0; i < parameters.Length; i++)
                parameters[i] = functionRealm.ToJsStringSlowPath(args[i]);
        }

        var body = args.Length == 0 ? string.Empty : functionRealm.ToJsStringSlowPath(args[^1]);

        var sourceText = $"{data.Prefix} anonymous({string.Join(",", parameters)}\n) {{\n{body}\n}}";
        var wrappedSource = $"({sourceText});";
        JsProgram program;
        var script = default(JsScript)!;
        try
        {
            if (data.Kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator &&
                DynamicFunctionParameterTextContainsYield(parameters))
                throw new JsParseException("Yield expression not allowed in formal parameter", 0, wrappedSource);

            program = JavaScriptParser.ParseScript(wrappedSource);
            if (TryGetDynamicFunctionExpression(program, out var functionExpression) &&
                functionExpression.Body.StrictDeclared &&
                functionExpression.HasDuplicateParameters)
                throw new JsParseException("Duplicate parameter name not allowed in this context",
                    functionExpression.Position,
                    wrappedSource);
            if (TryGetDynamicFunctionExpression(program, out functionExpression) &&
                data.Kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator &&
                DynamicFunctionParametersContainYield(functionExpression))
                throw new JsParseException("YieldExpression not permitted in this context", functionExpression.Position,
                    wrappedSource);

            script = JsCompiler.Compile(functionRealm, program);
        }
        catch (JsParseException ex)
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "FUNCTION_PARSE_ERROR");
        }
        catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.InternalError &&
                                            ex.Message.Contains("Private name '#", StringComparison.Ordinal))
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "FUNCTION_PARSE_ERROR");
        }
        catch (NotSupportedException ex) when (ex.Message.Contains("Private name '#", StringComparison.Ordinal))
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "FUNCTION_PARSE_ERROR");
        }

        var root = new JsBytecodeFunction(functionRealm, script, "__function_ctor__");
        JsValue result;
        try
        {
            result = functionRealm.InvokeBytecodeFunction(root, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty,
                JsValue.Undefined);
        }
        catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.InternalError &&
                                            ex.Message.Contains("Private name '#", StringComparison.Ordinal))
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "FUNCTION_PARSE_ERROR");
        }

        if (result.TryGetObject(out var resultObj) && resultObj is JsBytecodeFunction fn)
        {
            fn.Script = fn.Script with { FunctionSourceText = sourceText };
            fn.Prototype = GetPrototypeFromConstructorOrIntrinsic(newTarget, callee, data.IntrinsicPrototype);
            return result;
        }

        throw new JsRuntimeException(JsErrorKind.InternalError,
            "Dynamic function constructor did not produce a bytecode function");
    }

    private static bool TryGetDynamicFunctionExpression(JsProgram program, out JsFunctionExpression functionExpression)
    {
        if (program.Statements.Count == 1 &&
            program.Statements[0] is JsExpressionStatement { Expression: JsFunctionExpression expr })
        {
            functionExpression = expr;
            return true;
        }

        functionExpression = null!;
        return false;
    }

    private static bool DynamicFunctionParameterTextContainsYield(IReadOnlyList<string> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
            if (ContainsIdentifierToken(parameters[i], "yield"))
                return true;

        return false;
    }

    private static bool DynamicFunctionParametersContainYield(JsFunctionExpression functionExpression)
    {
        var initializers = functionExpression.ParameterInitializers;
        for (var i = 0; i < initializers.Count; i++)
            if (initializers[i] is not null && ExpressionContainsYield(initializers[i]!))
                return true;

        return false;
    }

    private static bool ExpressionContainsYield(JsExpression expression)
    {
        return expression switch
        {
            JsYieldExpression => true,
            JsAssignmentExpression a => ExpressionContainsYield(a.Left) || ExpressionContainsYield(a.Right),
            JsBinaryExpression b => ExpressionContainsYield(b.Left) || ExpressionContainsYield(b.Right),
            JsCallExpression c => ExpressionContainsYield(c.Callee) || AnyYieldExpression(c.Arguments),
            JsConditionalExpression c => ExpressionContainsYield(c.Test) || ExpressionContainsYield(c.Consequent) ||
                                         ExpressionContainsYield(c.Alternate),
            JsMemberExpression m => ExpressionContainsYield(m.Object) ||
                                    (m.IsComputed && ExpressionContainsYield(m.Property)),
            JsArrayExpression a => AnyNullableYieldExpression(a.Elements),
            JsObjectExpression o => ObjectExpressionContainsYield(o),
            JsSequenceExpression s => AnyYieldExpression(s.Expressions),
            JsSpreadExpression s => ExpressionContainsYield(s.Argument),
            JsTaggedTemplateExpression t => ExpressionContainsYield(t.Tag) ||
                                            AnyYieldExpression(t.Template.Expressions),
            JsTemplateExpression t => AnyYieldExpression(t.Expressions),
            JsUnaryExpression u => ExpressionContainsYield(u.Argument),
            JsUpdateExpression u => ExpressionContainsYield(u.Argument),
            JsAwaitExpression a => ExpressionContainsYield(a.Argument),
            JsNewExpression n => ExpressionContainsYield(n.Callee) || AnyYieldExpression(n.Arguments),
            JsParameterInitializerExpression p => ExpressionContainsYield(p.Expression),
            JsFunctionExpression => false,
            _ => false
        };
    }

    private static bool AnyYieldExpression(IReadOnlyList<JsExpression> expressions)
    {
        for (var i = 0; i < expressions.Count; i++)
            if (ExpressionContainsYield(expressions[i]))
                return true;

        return false;
    }

    private static bool AnyNullableYieldExpression(IReadOnlyList<JsExpression?> expressions)
    {
        for (var i = 0; i < expressions.Count; i++)
            if (expressions[i] is not null && ExpressionContainsYield(expressions[i]!))
                return true;

        return false;
    }

    private static bool ObjectExpressionContainsYield(JsObjectExpression expression)
    {
        for (var i = 0; i < expression.Properties.Count; i++)
        {
            var property = expression.Properties[i];
            if (property.ComputedKey is not null && ExpressionContainsYield(property.ComputedKey))
                return true;
            if (ExpressionContainsYield(property.Value))
                return true;
        }

        return false;
    }

    private static bool ContainsIdentifierToken(string text, string token)
    {
        for (var i = 0; i <= text.Length - token.Length; i++)
        {
            if (!text.AsSpan(i, token.Length).SequenceEqual(token))
                continue;
            if (i > 0 && IsIdentifierPart(text[i - 1]))
                continue;
            var end = i + token.Length;
            if (end < text.Length && IsIdentifierPart(text[end]))
                continue;
            return true;
        }

        return false;
    }

    private static bool IsIdentifierPart(char ch)
    {
        return ch == '_' || ch == '$' || char.IsLetterOrDigit(ch);
    }


    private JsHostFunction CreateSymbolConstructor()
    {
        return new(Realm, (in info) =>
        {
            if (!info.NewTarget.IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol is not a constructor");

            var realm = info.Realm;
            var args = info.Arguments;
            string? description = null;
            if (args.Length > 0 && !args[0].IsUndefined) description = realm.ToJsStringSlowPath(args[0]);

            var atom = realm.Atoms.InternSymbolString(description);
            var symbol = realm.Atoms.TryGetSymbolByAtom(atom, out var existing)
                ? existing
                : new(atom, description);
            return JsValue.FromSymbol(symbol);
        }, "Symbol", 0, true);
    }

    private void InstallSymbolConstructorBuiltins()
    {
        var symbolForFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var key = args.Length == 0 ? string.Empty : realm.ToJsStringSlowPath(args[0]);
            return JsValue.FromSymbol(realm.Agent.GetOrCreateRegisteredSymbol(key));
        }, "for", 1);

        var symbolKeyForFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].IsSymbol)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.keyFor requires a symbol");

            return realm.Agent.TryGetRegisteredSymbolKey(args[0].AsSymbol(), out var key)
                ? JsValue.FromString(key)
                : JsValue.Undefined;
        }, "keyFor", 1);

        var iterator = JsValue.FromSymbol(Realm.SymbolIteratorSymbol);
        var asyncIterator = JsValue.FromSymbol(Realm.SymbolAsyncIteratorSymbol);
        var hasInstance = JsValue.FromSymbol(Realm.SymbolHasInstanceSymbol);
        var toStringTag = JsValue.FromSymbol(Realm.SymbolToStringTagSymbol);
        var toPrimitive = JsValue.FromSymbol(Realm.SymbolToPrimitiveSymbol);
        var species = JsValue.FromSymbol(Realm.SymbolSpeciesSymbol);
        var isConcatSpreadable = JsValue.FromSymbol(Realm.SymbolIsConcatSpreadableSymbol);
        var match = JsValue.FromSymbol(Realm.SymbolMatchSymbol);
        var unscopables = JsValue.FromSymbol(Realm.SymbolUnscopablesSymbol);
        var dispose = JsValue.FromSymbol(Realm.SymbolDisposeSymbol);
        var asyncDispose = JsValue.FromSymbol(Realm.SymbolAsyncDisposeSymbol);
        var replace = JsValue.FromSymbol(Realm.SymbolReplaceSymbol);
        var matchAll = JsValue.FromSymbol(Realm.SymbolMatchAllSymbol);
        var split = JsValue.FromSymbol(Realm.SymbolSplitSymbol);
        var search = JsValue.FromSymbol(Realm.SymbolSearchSymbol);
        const int atomAsyncIterator = IdAsyncIterator;
        const int atomIsConcatSpreadable = IdIsConcatSpreadable;
        const int atomToPrimitive = IdToPrimitive;
        const int atomSpecies = IdSpecies;
        const int atomUnscopables = IdUnscopables;
        const int atomDispose = IdDispose;
        const int atomAsyncDispose = IdAsyncDispose;
        const int atomMatch = IdMatch;
        const int atomReplace = IdReplace;
        const int atomMatchAll = IdMatchAll;
        const int atomSplit = IdSplit;
        const int atomSearch = IdSearch;
        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdFor, JsValue.FromObject(symbolForFn)),
            PropertyDefinition.Mutable(IdKeyFor, JsValue.FromObject(symbolKeyForFn)),
            PropertyDefinition.Const(IdIterator, iterator),
            PropertyDefinition.Const(atomAsyncIterator, asyncIterator),
            PropertyDefinition.Const(IdHasInstance, hasInstance),
            PropertyDefinition.Const(IdToStringTag, toStringTag),
            PropertyDefinition.Const(atomToPrimitive, toPrimitive),
            PropertyDefinition.Const(atomSpecies, species),
            PropertyDefinition.Const(atomIsConcatSpreadable, isConcatSpreadable),
            PropertyDefinition.Const(atomMatch, match),
            PropertyDefinition.Const(atomUnscopables, unscopables),
            PropertyDefinition.Const(atomDispose, dispose),
            PropertyDefinition.Const(atomAsyncDispose, asyncDispose),
            PropertyDefinition.Const(atomReplace, replace),
            PropertyDefinition.Const(atomMatchAll, matchAll),
            PropertyDefinition.Const(atomSplit, split),
            PropertyDefinition.Const(atomSearch, search)
        ];
        SymbolConstructor.InitializePrototypeProperty(SymbolPrototype);
        SymbolConstructor.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private void InstallSymbolPrototypeBuiltins()
    {
        var toStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (thisValue.IsSymbol)
                return thisValue.AsSymbol().ToString();
            if (thisValue.TryGetObject(out var obj) && obj is JsSymbolObject boxed)
                return boxed.Value.ToString();
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Symbol.prototype.toString requires that 'this' be a Symbol",
                "SYMBOL_TOSTRING_BAD_RECEIVER");
        }, "toString", 0);

        var valueOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (thisValue.IsSymbol)
                return thisValue;
            if (thisValue.TryGetObject(out var obj) && obj is JsSymbolObject boxed)
                return JsValue.FromSymbol(boxed.Value);
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Symbol.prototype.valueOf requires that 'this' be a Symbol",
                "SYMBOL_VALUEOF_BAD_RECEIVER");
        }, "valueOf", 0);

        var descriptionGetFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (thisValue.IsSymbol)
                return thisValue.AsSymbol().Description is string symbolDescription
                    ? JsValue.FromString(symbolDescription)
                    : JsValue.Undefined;
            if (thisValue.TryGetObject(out var obj) && obj is JsSymbolObject boxed)
                return boxed.Value.Description is string boxedDescription
                    ? JsValue.FromString(boxedDescription)
                    : JsValue.Undefined;
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Symbol.prototype.description requires that 'this' be a Symbol",
                "SYMBOL_DESCRIPTION_BAD_RECEIVER");
        }, "get description", 0);

        var toPrimitiveFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (thisValue.IsSymbol)
                return thisValue;
            if (thisValue.TryGetObject(out var obj) && obj is JsSymbolObject boxed)
                return JsValue.FromSymbol(boxed.Value);
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Symbol.prototype[Symbol.toPrimitive] requires that 'this' be a Symbol",
                "SYMBOL_TOPRIMITIVE_BAD_RECEIVER");
        }, "[Symbol.toPrimitive]", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, SymbolConstructor),
            PropertyDefinition.Mutable(IdToString, toStringFn),
            PropertyDefinition.Mutable(IdValueOf, valueOfFn),
            PropertyDefinition.GetterData(IdDescription, descriptionGetFn, configurable: true),
            PropertyDefinition.Const(IdSymbolToPrimitive, JsValue.FromObject(toPrimitiveFn), configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Symbol"), configurable: true)
        ];
        SymbolPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private readonly record struct DynamicFunctionConstructorData(
        JsBytecodeFunctionKind Kind,
        JsObject IntrinsicPrototype,
        string Prefix);
}

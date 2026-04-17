using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.Runtime.Interop;

namespace Okojo.Reflection.Internal;

internal sealed class ClrInteropProvider : IClrAccessProvider
{
    private static readonly ConditionalWeakTable<JsRealm, RealmClrState> SStates = new();
    private static readonly ConcurrentDictionary<Type, Func<JsRealm, object, JsValue>> STaskWrappers = new();
    private static readonly ConcurrentDictionary<Type, Func<JsRealm, object, object>> STaskConverters = new();

    private static readonly MethodInfo SWrapGenericTaskMethod =
        typeof(ClrInteropProvider).GetMethod(nameof(WrapGenericTaskObject),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SWrapGenericValueTaskMethod =
        typeof(ClrInteropProvider).GetMethod(nameof(WrapGenericValueTaskObject),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SConvertPromiseToGenericTaskMethod =
        typeof(ClrInteropProvider).GetMethod(nameof(ConvertPromiseToGenericTaskObject),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SConvertPromiseToGenericValueTaskMethod =
        typeof(ClrInteropProvider).GetMethod(nameof(ConvertPromiseToGenericValueTaskObject),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly ClrInteropProvider SProvider = new();

    public HostTypeDescriptor CreateHostTypeDescriptor(JsAgent agent, Type clrType, int typeId)
    {
        var binding = HostBindingResolver.TryGetHostBinding(clrType);
        binding = AugmentAsyncEnumerableBinding(clrType, binding);
        return HostTypeDescriptor.Create(clrType, typeId, binding);
    }

    public JsHostFunction GetClrTypeFunction(JsRealm realm, Type type, HostBinding? binding = null)
    {
        var state = SStates.GetOrCreateValue(realm);
        if (state.TypeFunctions.TryGetValue(type, out var existing))
            return existing;

        var descriptor = binding is not null
            ? realm.Agent.GetOrCreateHostTypeDescriptor(type, binding)
            : realm.Agent.GetOrCreateHostTypeDescriptor(type);
        var staticLayout = descriptor.GetOrCreateStaticRealmLayout(realm);
        var function = JsHostFunction.CreateShapedFunction(realm, InvokeClrTypeFunction, type.Name, 0,
            staticLayout.Layout,
            IsClrTypeConstructable(type));
        function.UserData = new OkojoClrTypeFunctionData(type, staticLayout);
        if (staticLayout.SlotTemplate.Length != 0)
            Array.Copy(staticLayout.SlotTemplate, function.Slots, staticLayout.SlotTemplate.Length);
        state.TypeFunctions.Add(type, function);
        return function;
    }

    private static HostBinding? AugmentAsyncEnumerableBinding(Type clrType, HostBinding? binding)
    {
        if (binding?.AsyncEnumerator is not null)
            return binding;

        var asyncEnumerableInterface = clrType.GetInterfaces()
            .FirstOrDefault(static x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
        if (asyncEnumerableInterface is null)
            return binding;

        var elementType = asyncEnumerableInterface.GetGenericArguments()[0];
        var factory = typeof(ClrInteropProvider).GetMethod(nameof(CreateAsyncEnumerableBinding),
                          BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType);
        var asyncBinding = (HostAsyncEnumeratorBinding)factory.Invoke(null, null)!;
        if (binding is null)
            return new HostBinding(clrType, [], []) { AsyncEnumerator = asyncBinding };

        return new HostBinding(binding.ClrType, binding.InstanceMembers, binding.StaticMembers)
        {
            Indexer = binding.Indexer,
            Enumerator = binding.Enumerator,
            AsyncEnumerator = asyncBinding
        };
    }

    private static HostAsyncEnumeratorBinding CreateAsyncEnumerableBinding<T>()
    {
        return new(
            static target => ((IAsyncEnumerable<T>)target).GetAsyncEnumerator(),
            static state => ((IAsyncEnumerator<T>)state).MoveNextAsync(),
            static state => ((IAsyncEnumerator<T>)state).Current,
            static state => ((IAsyncEnumerator<T>)state).DisposeAsync());
    }

    public bool TryConvertTaskObjectToJsValue(JsRealm realm, object value, out JsValue jsValue)
    {
        if (value is Task task)
        {
            var type = value.GetType();
            if (TryGetGenericTaskResultType(type, out _))
            {
                var wrapper = STaskWrappers.GetOrAdd(type, static taskType =>
                {
                    _ = TryGetGenericTaskResultType(taskType, out var resultType);
                    var method = SWrapGenericTaskMethod.MakeGenericMethod(resultType);
                    return (Func<JsRealm, object, JsValue>)method.CreateDelegate(
                        typeof(Func<JsRealm, object, JsValue>));
                });
                jsValue = wrapper(realm, value);
                return true;
            }
        }

        var valueType = value.GetType();
        if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var wrapper = STaskWrappers.GetOrAdd(valueType, static taskType =>
            {
                var resultType = taskType.GetGenericArguments()[0];
                var method = SWrapGenericValueTaskMethod.MakeGenericMethod(resultType);
                return (Func<JsRealm, object, JsValue>)method.CreateDelegate(typeof(Func<JsRealm, object, JsValue>));
            });
            jsValue = wrapper(realm, value);
            return true;
        }

        jsValue = JsValue.Undefined;
        return false;
    }

    public bool TryConvertJsValueToTaskObject(JsRealm realm, JsValue value, Type targetType, out object? result,
        out int score)
    {
        score = 0;
        if (!targetType.IsGenericType)
        {
            result = null;
            return false;
        }

        var genericType = targetType.GetGenericTypeDefinition();
        if (genericType == typeof(Task<>))
        {
            var converter = STaskConverters.GetOrAdd(targetType, static closedType =>
            {
                var resultType = closedType.GetGenericArguments()[0];
                var method = SConvertPromiseToGenericTaskMethod.MakeGenericMethod(resultType);
                return (Func<JsRealm, object, object>)method.CreateDelegate(typeof(Func<JsRealm, object, object>));
            });
            result = converter(realm, value);
            return true;
        }

        if (genericType == typeof(ValueTask<>))
        {
            var converter = STaskConverters.GetOrAdd(targetType, static closedType =>
            {
                var resultType = closedType.GetGenericArguments()[0];
                var method = SConvertPromiseToGenericValueTaskMethod.MakeGenericMethod(resultType);
                return (Func<JsRealm, object, object>)method.CreateDelegate(typeof(Func<JsRealm, object, object>));
            });
            result = converter(realm, value);
            return true;
        }

        result = null;
        return false;
    }

    public JsValue BindGenericMethod(JsRealm realm, string memberName, object? target, MethodInfo[] methods,
        ReadOnlySpan<JsValue> typeArguments, int genericParameterCount)
    {
        if (typeArguments.Length != genericParameterCount)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"CLR generic method '{memberName}' requires {genericParameterCount} type arguments.",
                "CLR_GENERIC_METHOD_ARITY");

        var genericTypes = new Type[typeArguments.Length];
        for (var i = 0; i < typeArguments.Length; i++)
            if (!JsRealm.TryExtractClrType(typeArguments[i], out genericTypes[i]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Invalid generic type parameter on CLR method '{memberName}'.",
                    "CLR_GENERIC_ARGUMENT");

        MethodInfo[] closedMethods;
        try
        {
            closedMethods = methods.Select(x => x.MakeGenericMethod(genericTypes)).ToArray();
        }
        catch (ArgumentException ex)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Invalid generic type parameter on CLR method '{memberName}': {ex.Message}",
                "CLR_GENERIC_ARGUMENT");
        }

        var closedMember = HostNamedMemberDescriptor.CreateMethod(memberName, closedMethods);
        var function = new JsHostFunction(realm, HostTypeDescriptor.InvokeBoundGenericHostMethod, memberName,
            closedMember.FunctionLength)
        {
            UserData = new BoundGenericHostMethodData(closedMember, target)
        };
        return JsValue.FromObject(function);
    }

    public Array CreateParamsArray(Type elementType, int length)
    {
        return Array.CreateInstance(elementType, length);
    }

    public JsObject GetClrNamespace(JsRealm realm, string? namespacePath = null)
    {
        if (string.IsNullOrEmpty(namespacePath))
            return SStates.GetOrCreateValue(realm).RootNamespaceObject ??= new JsClrNamespaceObject(realm, null);

        var value = ResolveClrPath(realm, namespacePath);
        if (value.TryGetObject(out var obj) && obj is JsObject okojoObject)
            return okojoObject;

        throw new InvalidOperationException($"CLR path '{namespacePath}' does not resolve to a namespace.");
    }

    public JsValue ResolveClrPath(JsRealm realm, string path)
    {
        var state = SStates.GetOrCreateValue(realm);
        if (state.PathCache.TryGetValue(path, out var cached))
            return cached;

        var type = FindClrType(realm, path);
        var result = type is not null
            ? JsValue.FromObject(GetClrTypeFunction(realm, type))
            : JsValue.FromObject(new JsClrNamespaceObject(realm, path));
        state.PathCache.Add(path, result);
        return result;
    }

    public bool TryResolveClrPathExactly(JsRealm realm, string path, out JsValue value)
    {
        var type = FindClrType(realm, path);
        if (type is not null)
        {
            value = JsValue.FromObject(GetClrTypeFunction(realm, type));
            return true;
        }

        if (HasClrNamespace(realm, path))
        {
            value = ResolveClrPath(realm, path);
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    public JsHostFunction CreateClrTypedNullHelperFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            if (info.Arguments.Length == 0 || !JsRealm.TryExtractClrType(info.Arguments[0], out var targetType))
                throw new JsRuntimeException(JsErrorKind.TypeError, "$null requires a CLR type argument.",
                    "CLR_TYPED_NULL");

            return JsValue.FromObject(new JsClrTypedNullObject(info.Realm, targetType));
        }, "$null", 1);
    }

    public JsHostFunction CreateClrPlaceHolderHelperFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            if (info.Arguments.Length == 0 || !JsRealm.TryExtractClrType(info.Arguments[0], out var targetType))
                throw new JsRuntimeException(JsErrorKind.TypeError, "$place requires a CLR type argument.",
                    "CLR_PLACE");

            var placeholder = new JsClrPlaceHolderObject(info.Realm, targetType);
            if (info.Arguments.Length > 1)
                placeholder.InitializeFromJsValue(info.Realm, info.Arguments[1]);
            return JsValue.FromObject(placeholder);
        }, "$place", 2);
    }

    public JsHostFunction CreateClrCastHelperFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            if (info.Arguments.Length == 0 || !JsRealm.TryExtractClrType(info.Arguments[0], out var targetType))
                throw new JsRuntimeException(JsErrorKind.TypeError, "$cast requires a CLR type argument.", "CLR_CAST");

            var value = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            var converted = HostValueConverter.ConvertFromJsValue(info.Realm, value, targetType);
            return HostValueConverter.ConvertToJsValue(info.Realm, converted);
        }, "$cast", 2);
    }

    public JsHostFunction CreateClrUsingHelperFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            var resolver = new JsClrUsingResolverObject(info.Realm);
            resolver.AddImports(info.Arguments);
            return JsValue.FromObject(resolver);
        }, "$using", 0);
    }

    private static JsValue InvokeClrTypeFunction(scoped in CallInfo info)
    {
        var type = ((OkojoClrTypeFunctionData)((JsHostFunction)info.Function).UserData!).ClrType;
        if (type.IsGenericTypeDefinition && !info.IsConstruct)
            return BindGenericClrType(info.Realm, type, info.Arguments);

        if (!IsClrTypeConstructable(type))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"CLR type '{type.FullName ?? type.Name}' is not constructable.",
                "CLR_NOT_CONSTRUCTABLE");

        var instance = CreateClrInstance(info.Realm, type, info.Arguments);
        return HostValueConverter.ConvertToJsValue(info.Realm, instance);
    }

    private static bool IsClrTypeConstructable(Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            return false;
        if (type.IsValueType)
            return true;
        return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length != 0;
    }

    private static object? CreateClrInstance(JsRealm realm, Type type, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0 && type.IsValueType)
            return Activator.CreateInstance(type);

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        for (var i = 0; i < constructors.Length; i++)
            if (TryBuildConstructorArguments(realm, constructors[i], args, out var converted))
                return constructors[i].Invoke(converted);

        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"Could not resolve a constructor for CLR type '{type.FullName ?? type.Name}'.",
            "CLR_CONSTRUCTOR");
    }

    private static bool TryBuildConstructorArguments(JsRealm realm, ConstructorInfo constructor,
        ReadOnlySpan<JsValue> args, out object?[] converted)
    {
        var parameters = constructor.GetParameters();
        if (args.Length > parameters.Length)
        {
            converted = Array.Empty<object?>();
            return false;
        }

        converted = new object?[parameters.Length];
        var i = 0;
        try
        {
            for (; i < args.Length; i++)
                converted[i] = HostValueConverter.ConvertFromJsValue(realm, args[i], parameters[i].ParameterType);

            for (; i < parameters.Length; i++)
            {
                if (!parameters[i].HasDefaultValue)
                    return false;
                converted[i] = parameters[i].DefaultValue;
            }

            return true;
        }
        catch
        {
            converted = Array.Empty<object?>();
            return false;
        }
    }

    private static Type? FindClrType(JsRealm realm, string path)
    {
        var assemblies = realm.Engine.Options.ClrAssemblies;
        for (var i = 0; i < assemblies.Count; i++)
        {
            var assembly = assemblies[i];
            var direct = assembly.GetType(path, false, false);
            if (direct is not null)
                return direct;

            var nested = FindNestedClrType(assembly, path);
            if (nested is not null)
                return nested;

            var exactGeneric = FindGenericClrTypeDefinition(assembly, path, true);
            if (exactGeneric is not null)
                return exactGeneric;

            var generic = FindGenericClrTypeDefinition(assembly, path);
            if (generic is not null)
                return generic;
        }

        return null;
    }

    private static Type? FindNestedClrType(Assembly assembly, string path)
    {
        var comparedPath = path.Replace('+', '.');
        foreach (var type in assembly.GetTypes())
        {
            var fullName = type.FullName;
            if (fullName is not null && fullName.Replace('+', '.').Equals(comparedPath, StringComparison.Ordinal))
                return type;
        }

        return null;
    }

    private static Type? FindGenericClrTypeDefinition(Assembly assembly, string path, bool requireAritySuffix = false)
    {
        int? requiredArity = null;
        if (TryParseClrAritySuffix(path, out var unsuffixedPath, out var arity))
        {
            path = unsuffixedPath;
            requiredArity = arity;
        }
        else if (requireAritySuffix)
        {
            return null;
        }

        Type? singleMatch = null;
        var comparedPath = path.Replace('+', '.');
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsGenericTypeDefinition)
                continue;

            if (requiredArity.HasValue && type.GetGenericArguments().Length != requiredArity.Value)
                continue;

            var fullName = type.FullName;
            if (fullName is null)
                continue;

            var baseName = GetClrGenericLookupName(fullName);
            if (!baseName.Equals(comparedPath, StringComparison.Ordinal))
                continue;

            if (singleMatch is not null)
                return null;
            singleMatch = type;
        }

        return singleMatch;
    }

    private static bool TryParseClrAritySuffix(string name, out string unsuffixedName, out int arity)
    {
        var suffixIndex = name.LastIndexOf('$');
        if (suffixIndex > 0 &&
            suffixIndex + 1 < name.Length &&
            int.TryParse(name.AsSpan(suffixIndex + 1), out arity) &&
            arity >= 0)
        {
            unsuffixedName = name[..suffixIndex];
            return true;
        }

        unsuffixedName = name;
        arity = 0;
        return false;
    }

    private static string GetClrGenericLookupName(string fullName)
    {
        Span<char> buffer = stackalloc char[fullName.Length];
        var count = 0;
        for (var i = 0; i < fullName.Length; i++)
        {
            var ch = fullName[i];
            if (ch == '`')
            {
                i++;
                while (i < fullName.Length && char.IsDigit(fullName[i]))
                    i++;
                i--;
                continue;
            }

            buffer[count++] = ch == '+' ? '.' : ch;
        }

        return new(buffer[..count]);
    }

    private static JsValue BindGenericClrType(JsRealm realm, Type genericTypeDefinition,
        ReadOnlySpan<JsValue> typeArguments)
    {
        var genericTypes = new Type[typeArguments.Length];
        for (var i = 0; i < typeArguments.Length; i++)
            if (!JsRealm.TryExtractClrType(typeArguments[i], out genericTypes[i]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Invalid generic type parameter on '{genericTypeDefinition.FullName ?? genericTypeDefinition.Name}'.",
                    "CLR_GENERIC_ARGUMENT");

        if (genericTypeDefinition.GetGenericArguments().Length != genericTypes.Length)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"CLR generic type '{genericTypeDefinition.FullName ?? genericTypeDefinition.Name}' requires {genericTypeDefinition.GetGenericArguments().Length} type arguments.",
                "CLR_GENERIC_ARITY");

        try
        {
            var closedType = genericTypeDefinition.MakeGenericType(genericTypes);
            return JsValue.FromObject(SProvider.GetClrTypeFunction(realm, closedType));
        }
        catch (ArgumentException ex)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Invalid generic type parameter on '{genericTypeDefinition.FullName ?? genericTypeDefinition.Name}': {ex.Message}",
                "CLR_GENERIC_ARGUMENT");
        }
    }

    private static bool HasClrNamespace(JsRealm realm, string path)
    {
        var prefix = path.Replace('+', '.') + ".";
        var assemblies = realm.Engine.Options.ClrAssemblies;
        for (var i = 0; i < assemblies.Count; i++)
            foreach (var type in assemblies[i].GetTypes())
            {
                var fullName = type.FullName;
                if (fullName is null)
                    continue;

                var comparedName = fullName.Replace('+', '.');
                var tickIndex = comparedName.IndexOf('`');
                if (tickIndex >= 0)
                    comparedName = comparedName[..tickIndex];

                if (comparedName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

        return false;
    }

    private static bool TryGetGenericTaskResultType(Type type, out Type resultType)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Task<>))
            {
                resultType = current.GetGenericArguments()[0];
                return true;
            }

        resultType = null!;
        return false;
    }

    private static JsValue WrapGenericTaskObject<T>(JsRealm realm, object value)
    {
        return realm.WrapTask((Task<T>)value);
    }

    private static JsValue WrapGenericValueTaskObject<T>(JsRealm realm, object value)
    {
        return realm.WrapTask((ValueTask<T>)value);
    }

    private static object ConvertPromiseToGenericTaskObject<T>(JsRealm realm, object value)
    {
        return realm.ToTask<T>((JsValue)value);
    }

    private static object ConvertPromiseToGenericValueTaskObject<T>(JsRealm realm, object value)
    {
        return realm.ToValueTask<T>((JsValue)value);
    }

    private sealed class RealmClrState
    {
        public readonly Dictionary<string, JsValue> PathCache = new(StringComparer.Ordinal);
        public readonly Dictionary<Type, JsHostFunction> TypeFunctions = new();
        public JsObject? RootNamespaceObject;
    }

    private sealed class OkojoClrTypeFunctionData(Type clrType, HostRealmLayoutInfo layoutInfo) : IClrTypeFunctionData
    {
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        public Type ClrType { get; } = clrType;

        public HostRealmLayoutInfo LayoutInfo { get; } = layoutInfo;
        public string DisplayTag => $"CLR Type {ClrDisplay.FormatTypeName(ClrType)}";
    }
}

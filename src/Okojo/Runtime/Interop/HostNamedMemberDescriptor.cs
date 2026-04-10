using System.Reflection;

namespace Okojo.Runtime.Interop;

internal sealed class HostNamedMemberDescriptor
{
    private HostNamedMemberDescriptor(
        string name,
        Type receiverType,
        HostNamedMemberKind kind,
        bool isStatic,
        bool enumerable,
        bool configurable,
        FieldInfo? field,
        PropertyInfo? property,
        MethodInfo[]? methods,
        int? genericParameterCount,
        JsHostFunctionBody? bindableGetterBody,
        JsHostFunctionBody? bindableSetterBody,
        JsHostFunctionBody? bindableMethodBody,
        int bindableFunctionLength)
    {
        Name = name;
        ReceiverType = receiverType;
        Kind = kind;
        IsStatic = isStatic;
        Enumerable = enumerable;
        Configurable = configurable;
        Field = field;
        Property = property;
        Methods = methods;
        GenericParameterCount = genericParameterCount;
        BindableGetterBody = bindableGetterBody;
        BindableSetterBody = bindableSetterBody;
        BindableMethodBody = bindableMethodBody;
        BindableFunctionLength = bindableFunctionLength;
    }

    internal string Name { get; }
    internal Type ReceiverType { get; }
    internal HostNamedMemberKind Kind { get; }
    internal bool IsStatic { get; }
    internal bool Enumerable { get; }
    internal bool Configurable { get; }
    internal FieldInfo? Field { get; }
    internal PropertyInfo? Property { get; }
    internal MethodInfo[]? Methods { get; }
    internal int? GenericParameterCount { get; }
    internal JsHostFunctionBody? BindableGetterBody { get; }
    internal JsHostFunctionBody? BindableSetterBody { get; }
    internal JsHostFunctionBody? BindableMethodBody { get; }
    internal int BindableFunctionLength { get; }

    internal bool CanRead =>
        Field is not null ||
        (Property is not null && Property.CanRead) ||
        BindableGetterBody is not null;

    internal bool CanWrite =>
        (Field is not null && !Field.IsInitOnly) ||
        (Property is not null && Property.SetMethod is not null) ||
        BindableSetterBody is not null;

    internal int FunctionLength
    {
        get
        {
            if (BindableMethodBody is not null)
                return BindableFunctionLength;

            if (Methods is null || Methods.Length == 0)
                return 0;

            var best = int.MaxValue;
            for (var i = 0; i < Methods.Length; i++)
            {
                var required = CountRequiredParameters(Methods[i]);
                if (required < best)
                    best = required;
            }

            return best == int.MaxValue ? 0 : best;
        }
    }

    internal JsShapePropertyFlags SlotFlags
    {
        get
        {
            var flags = Configurable ? JsShapePropertyFlags.Configurable : JsShapePropertyFlags.None;
            if (Enumerable)
                flags |= JsShapePropertyFlags.Enumerable;
            if (Kind == HostNamedMemberKind.Method)
                return flags;
            if (CanRead)
                flags |= JsShapePropertyFlags.HasGetter;
            if (CanWrite)
                flags |= JsShapePropertyFlags.HasSetter;
            return flags;
        }
    }

    internal static HostNamedMemberDescriptor CreateField(FieldInfo field)
    {
        return new(field.Name, field.DeclaringType ?? field.FieldType, HostNamedMemberKind.Field, field.IsStatic,
            false,
            true, field, null, null, null, null, null, null, 0);
    }

    internal static HostNamedMemberDescriptor CreateProperty(PropertyInfo property)
    {
        return new(property.Name, property.DeclaringType ?? property.PropertyType, HostNamedMemberKind.Property,
            property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true,
            false, true, null, property, null, null, null, null, null, 0);
    }

    internal static HostNamedMemberDescriptor CreateMethod(string name, MethodInfo[] methods,
        int? genericParameterCount = null)
    {
        return new(name, methods[0].DeclaringType ?? methods[0].ReturnType, HostNamedMemberKind.Method,
            methods[0].IsStatic,
            false,
            true, null, null, methods, genericParameterCount, null, null, null, 0);
    }

    internal static HostNamedMemberDescriptor CreateGenerated(HostMemberBinding binding, Type receiverType)
    {
        return new(binding.Name, receiverType, (HostNamedMemberKind)binding.Kind, binding.IsStatic,
            false, true, null, null, null, null,
            binding.GetterBody, binding.SetterBody, binding.MethodBody, binding.FunctionLength);
    }

    internal object? ReadValue(object? target)
    {
        if (Field is not null)
            return Field.GetValue(target);
        if (Property is not null && Property.GetMethod is not null)
            return Property.GetValue(target);
        throw new InvalidOperationException($"Member '{Name}' is not readable.");
    }

    internal void WriteValue(JsRealm realm, object? target, JsValue value)
    {
        if (Field is not null)
        {
            Field.SetValue(target, HostValueConverter.ConvertFromJsValue(realm, value, Field.FieldType));
            return;
        }

        if (Property is not null && Property.SetMethod is not null)
        {
            Property.SetValue(target, HostValueConverter.ConvertFromJsValue(realm, value, Property.PropertyType));
            return;
        }

        throw new InvalidOperationException($"Member '{Name}' is not writable.");
    }

    internal JsValue InvokeMethod(JsRealm realm, object? target, ReadOnlySpan<JsValue> arguments)
    {
        var methods = Methods;
        if (methods is null || methods.Length == 0)
            throw new InvalidOperationException($"Member '{Name}' is not callable.");

        if (GenericParameterCount.HasValue)
            return BindGenericMethod(realm, target, methods, arguments, GenericParameterCount.Value);

        MethodInfo? bestMethod = null;
        var bestScore = int.MaxValue;
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if (!TryScoreMethodArguments(realm, method, arguments, out var score))
                continue;

            if (bestMethod is null || score < bestScore)
            {
                bestMethod = method;
                bestScore = score;
                if (score == 0)
                    break;
            }
        }

        if (bestMethod is null)
            throw new InvalidOperationException($"Could not resolve host method overload '{Name}'.");

        if (!TryBuildMethodArguments(realm, bestMethod, arguments, out var bestArgs, out _, out var bestByRefBindings))
            throw new InvalidOperationException($"Could not bind resolved host method overload '{Name}'.");

        var result = bestMethod.Invoke(target, bestArgs);
        if (bestByRefBindings is not null)
            for (var i = 0; i < bestByRefBindings.Count; i++)
            {
                var binding = bestByRefBindings[i];
                binding.Placeholder.SetBoxedValue(bestArgs[binding.ArgumentIndex]);
            }

        if (bestMethod.ReturnType == typeof(void))
            return JsValue.Undefined;

        return HostValueConverter.ConvertToJsValue(realm, result);
    }

    private JsValue BindGenericMethod(JsRealm realm, object? target, MethodInfo[] methods,
        ReadOnlySpan<JsValue> typeArguments,
        int genericParameterCount)
    {
        if (realm.Engine.ClrAccessProvider is { } provider)
            return provider.BindGenericMethod(realm, Name, target, methods, typeArguments, genericParameterCount);

        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"CLR generic method '{Name}' requires Okojo.Reflection CLR access.",
            "CLR_GENERIC_ARGUMENT");
    }

    private static int CountRequiredParameters(MethodInfo method)
    {
        var required = 0;
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (IsParamsParameter(parameters[i]))
                break;
            if (!parameters[i].HasDefaultValue)
                required++;
        }

        return required;
    }

    private static bool TryBuildMethodArguments(JsRealm realm, MethodInfo method, ReadOnlySpan<JsValue> arguments,
        out object?[] converted, out int score, out List<HostByRefBinding>? byRefBindings)
    {
        var parameters = method.GetParameters();
        score = 0;
        byRefBindings = null;

        if (parameters.Length == 0)
        {
            converted = arguments.Length == 0 ? Array.Empty<object?>() : Array.Empty<object?>();
            return arguments.Length == 0;
        }

        var paramsIndex = parameters.Length - 1;
        var hasParamsArray = IsParamsParameter(parameters[paramsIndex]);
        var fixedCount = hasParamsArray ? paramsIndex : parameters.Length;
        if (arguments.Length < CountRequiredParameters(method))
        {
            converted = Array.Empty<object?>();
            return false;
        }

        if (!hasParamsArray && arguments.Length > parameters.Length)
        {
            converted = Array.Empty<object?>();
            return false;
        }

        converted = new object?[parameters.Length];
        for (var i = 0; i < fixedCount; i++)
        {
            if (i < arguments.Length)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    if (!TryBindByRefArgument(realm, parameters[i], arguments[i], i, out converted[i],
                            out var byRefScore,
                            out var binding))
                    {
                        converted = Array.Empty<object?>();
                        byRefBindings = null;
                        return false;
                    }

                    byRefBindings ??= new();
                    byRefBindings.Add(binding);
                    score += byRefScore + 1;
                    continue;
                }

                if (!HostValueConverter.TryConvertFromJsValue(realm, arguments[i], parameterType,
                        out converted[i], out var argScore))
                {
                    converted = Array.Empty<object?>();
                    byRefBindings = null;
                    return false;
                }

                score += argScore;
                continue;
            }

            if (!parameters[i].HasDefaultValue)
            {
                converted = Array.Empty<object?>();
                byRefBindings = null;
                return false;
            }

            converted[i] = parameters[i].DefaultValue;
        }

        if (!hasParamsArray)
            return true;

        var paramsParameter = parameters[paramsIndex];
        var elementType = paramsParameter.ParameterType.GetElementType() ?? typeof(object);
        var varArgCount = Math.Max(0, arguments.Length - fixedCount);
        var paramsArray = CreateParamsArray(realm, elementType, varArgCount);
        for (var i = 0; i < varArgCount; i++)
        {
            if (!HostValueConverter.TryConvertFromJsValue(realm, arguments[fixedCount + i], elementType,
                    out var value, out var argScore))
            {
                converted = Array.Empty<object?>();
                byRefBindings = null;
                return false;
            }

            paramsArray.SetValue(value, i);
            score += argScore + 2;
        }

        converted[paramsIndex] = paramsArray;
        return true;
    }

    private static bool TryScoreMethodArguments(JsRealm realm, MethodInfo method, ReadOnlySpan<JsValue> arguments,
        out int score)
    {
        var parameters = method.GetParameters();
        score = 0;

        if (parameters.Length == 0)
            return arguments.Length == 0;

        var paramsIndex = parameters.Length - 1;
        var hasParamsArray = IsParamsParameter(parameters[paramsIndex]);
        var fixedCount = hasParamsArray ? paramsIndex : parameters.Length;
        if (arguments.Length < CountRequiredParameters(method))
            return false;

        if (!hasParamsArray && arguments.Length > parameters.Length)
            return false;

        for (var i = 0; i < fixedCount; i++)
        {
            if (i < arguments.Length)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    if (!TryGetByRefArgumentScore(realm, parameters[i], arguments[i], out var byRefScore))
                        return false;

                    score += byRefScore + 1;
                    continue;
                }

                if (!HostValueConverter.TryGetConversionScore(realm, arguments[i], parameterType, out var argScore))
                    return false;

                score += argScore;
                continue;
            }

            if (!parameters[i].HasDefaultValue)
                return false;
        }

        if (!hasParamsArray)
            return true;

        var paramsParameter = parameters[paramsIndex];
        var elementType = paramsParameter.ParameterType.GetElementType() ?? typeof(object);
        var varArgCount = Math.Max(0, arguments.Length - fixedCount);
        for (var i = 0; i < varArgCount; i++)
        {
            if (!HostValueConverter.TryGetConversionScore(realm, arguments[fixedCount + i], elementType,
                    out var argScore))
                return false;

            score += argScore + 2;
        }

        return true;
    }

    private static Array CreateParamsArray(JsRealm realm, Type elementType, int length)
    {
        if (elementType == typeof(object))
            return new object?[length];
        if (elementType == typeof(JsValue))
            return new JsValue[length];
        if (elementType == typeof(string))
            return new string?[length];
        if (elementType == typeof(bool))
            return new bool[length];
        if (elementType == typeof(byte))
            return new byte[length];
        if (elementType == typeof(sbyte))
            return new sbyte[length];
        if (elementType == typeof(short))
            return new short[length];
        if (elementType == typeof(ushort))
            return new ushort[length];
        if (elementType == typeof(int))
            return new int[length];
        if (elementType == typeof(uint))
            return new uint[length];
        if (elementType == typeof(long))
            return new long[length];
        if (elementType == typeof(ulong))
            return new ulong[length];
        if (elementType == typeof(float))
            return new float[length];
        if (elementType == typeof(double))
            return new double[length];
        if (elementType == typeof(decimal))
            return new decimal[length];
        if (realm.Engine.ClrAccessProvider is { } provider)
            return provider.CreateParamsArray(elementType, length);

        throw new InvalidOperationException(
            $"Cannot materialize params array for element type '{elementType}' without Okojo.Reflection CLR access.");
    }

    private static bool TryBindByRefArgument(JsRealm realm, ParameterInfo parameter, JsValue argument,
        int argumentIndex,
        out object? value, out int score, out HostByRefBinding binding)
    {
        score = 0;
        binding = default;
        if (!argument.TryGetObject(out var obj) || obj is not IClrByRefPlaceholder placeholder)
        {
            value = null;
            return false;
        }

        var elementType = parameter.ParameterType.GetElementType()!;
        if (!placeholder.TryPrepareByRefValue(realm, elementType, parameter.IsOut, out value, out score))
        {
            value = null;
            return false;
        }

        binding = new(argumentIndex, placeholder);
        return true;
    }

    private static bool TryGetByRefArgumentScore(JsRealm realm, ParameterInfo parameter, JsValue argument,
        out int score)
    {
        score = 0;
        if (!argument.TryGetObject(out var obj) || obj is not IClrByRefPlaceholder placeholder)
            return false;

        var elementType = parameter.ParameterType.GetElementType()!;
        if (!elementType.IsAssignableFrom(placeholder.TargetType) && elementType != placeholder.TargetType)
            return false;

        if (!placeholder.HasValue && !parameter.IsOut)
            return false;

        score = elementType == placeholder.TargetType ? 0 : 2;
        return true;
    }


    private static bool IsParamsParameter(ParameterInfo parameter)
    {
        return parameter.GetCustomAttribute<ParamArrayAttribute>() is not null &&
               parameter.ParameterType.IsArray;
    }
}

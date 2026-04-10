using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private Dictionary<Type, JsHostFunction>? boundHostTypeCache;
    private HostWrapperCache? hostWrapperCache;

    public JsHostObject WrapHostObject(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is JsHostObject hostObject && ReferenceEquals(hostObject.Realm, this))
            return hostObject;

        if (value is not IHostBindable && !Engine.IsClrAccessEnabled)
            throw new InvalidOperationException(
                "CLR access is disabled. Configure JsRuntime with options => options.AllowClrAccess().");

        var type = value.GetType();
        if (!type.IsValueType)
        {
            var cache = hostWrapperCache ??= new();
            return cache.GetOrAdd(value, this, static (target, realm) => realm.CreateHostObject(target));
        }

        return CreateHostObject(value);
    }

    public JsValue WrapHostValue(object? value)
    {
        return HostValueConverter.ConvertToJsValue(this, value);
    }

    private void EnsureClrAccessEnabled()
    {
        if (!Engine.IsClrAccessEnabled)
            throw new InvalidOperationException(
                "CLR access is disabled. Configure JsRuntime with options => options.AllowClrAccess().");
    }

    public JsHostFunction WrapHostType(Type type)
    {
        EnsureClrAccessEnabled();
        ArgumentNullException.ThrowIfNull(type);
        return Engine.ClrAccessProvider?.GetClrTypeFunction(this, type)
               ?? throw new InvalidOperationException(
                   "CLR access is disabled. Configure JsRuntime with options => options.AllowClrAccess().");
    }

    public JsHostFunction WrapHostType(Type type, HostBinding binding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(binding);
        var boundType = binding.ClrType;
        if (Engine.ClrAccessProvider is { } provider)
            return provider.GetClrTypeFunction(this, boundType, binding);

        var cache = boundHostTypeCache ??= new();
        if (cache.TryGetValue(boundType, out var existing))
            return existing;

        var descriptor = Agent.GetOrCreateHostTypeDescriptor(boundType, binding);
        var staticLayout = descriptor.GetOrCreateStaticRealmLayout(this);
        var function = JsHostFunction.CreateShapedFunction(this, InvokeBoundHostTypeFunction, boundType.Name, 0,
            staticLayout.Layout,
            IsBoundHostTypeConstructable(boundType));
        function.UserData = new BoundHostTypeFunctionData(boundType, staticLayout);
        if (staticLayout.SlotTemplate.Length != 0)
            Array.Copy(staticLayout.SlotTemplate, function.Slots, staticLayout.SlotTemplate.Length);
        cache.Add(boundType, function);
        return function;
    }

    private JsHostObject CreateHostObject(object value)
    {
        HostTypeDescriptor descriptor;
        if (value is IHostBindable bindable)
        {
            var binding = bindable.GetHostBinding();
            descriptor = Agent.GetOrCreateHostTypeDescriptor(binding.ClrType, binding);
        }
        else
        {
            descriptor = Agent.GetOrCreateDynamicHostTypeDescriptor(value.GetType());
        }

        return new(this, value, descriptor);
    }

    private static JsValue InvokeBoundHostTypeFunction(scoped in CallInfo info)
    {
        var typeData = (BoundHostTypeFunctionData)((JsHostFunction)info.Function).UserData!;
        if (!info.IsConstruct)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"CLR type '{typeData.ClrType.FullName ?? typeData.ClrType.Name}' is not constructable without 'new'.",
                "CLR_NOT_CONSTRUCTABLE");

        var instance = CreateBoundHostInstance(info.Realm, typeData.ClrType, info.Arguments);
        return HostValueConverter.ConvertToJsValue(info.Realm, instance);
    }

    private static bool IsBoundHostTypeConstructable(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            return false;
        if (type.IsValueType)
            return true;
        return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length != 0;
    }

    private static object? CreateBoundHostInstance(
        JsRealm realm,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type type,
        ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0 && type.IsValueType)
            return Activator.CreateInstance(type);

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        for (var i = 0; i < constructors.Length; i++)
            if (TryBuildBoundConstructorArguments(realm, constructors[i], args, out var converted))
                return constructors[i].Invoke(converted);

        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"Could not resolve a constructor for CLR type '{type.FullName ?? type.Name}'.",
            "CLR_CONSTRUCTOR");
    }

    private static bool TryBuildBoundConstructorArguments(JsRealm realm, ConstructorInfo constructor,
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

    private sealed class BoundHostTypeFunctionData(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type clrType,
        HostRealmLayoutInfo layoutInfo) : IClrTypeFunctionData
    {
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        public Type ClrType { get; } = clrType;

        public HostRealmLayoutInfo LayoutInfo { get; } = layoutInfo;
        public string DisplayTag => $"CLR Type {ClrDisplay.FormatTypeName(ClrType)}";
    }
}

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Okojo.Runtime.Interop;

internal sealed class HostTypeDescriptor
{
    private readonly object realmLayoutLock = new();

    private readonly Dictionary<JsRealm, HostRealmLayoutInfo>?[] realmLayouts =
        new Dictionary<JsRealm, HostRealmLayoutInfo>?[2];

    private HostTypeDescriptor(
        Type clrType,
        int typeId,
        HostNamedMemberDescriptor[] namedMembers,
        HostNamedMemberDescriptor[] staticNamedMembers,
        HostIndexerDescriptor? indexer,
        HostEnumeratorDescriptor? enumerator,
        HostAsyncEnumeratorDescriptor? asyncEnumerator)
    {
        ClrType = clrType;
        TypeId = typeId;
        NamedMembers = namedMembers;
        StaticNamedMembers = staticNamedMembers;
        Indexer = indexer;
        Enumerator = enumerator;
        AsyncEnumerator = asyncEnumerator;
    }

    internal Type ClrType { get; }
    internal int TypeId { get; }
    internal HostNamedMemberDescriptor[] NamedMembers { get; }
    internal HostNamedMemberDescriptor[] StaticNamedMembers { get; }
    internal HostIndexerDescriptor? Indexer { get; }
    internal HostEnumeratorDescriptor? Enumerator { get; }
    internal HostAsyncEnumeratorDescriptor? AsyncEnumerator { get; }
    internal bool SupportsSyncIteration => Enumerator is not null;
    internal bool SupportsAsyncIteration => AsyncEnumerator is not null;

    internal static HostTypeDescriptor Create(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors |
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.NonPublicFields |
            DynamicallyAccessedMemberTypes.PublicNestedTypes |
            DynamicallyAccessedMemberTypes.NonPublicNestedTypes |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties |
            DynamicallyAccessedMemberTypes.PublicEvents |
            DynamicallyAccessedMemberTypes.NonPublicEvents)]
        Type clrType, int typeId, HostBinding? binding = null)
    {
        var members = CreateNamedMembers(clrType, false, binding);
        var staticMembers = CreateNamedMembers(clrType, true, binding);
        var indexer = binding?.Indexer is { } manualIndexer
            ? new(manualIndexer.Getter, manualIndexer.Setter, manualIndexer.CollectOwnIndices)
            : CreateIndexer(clrType);
        var enumerator = binding?.Enumerator is { } manualEnumerator
            ? new(manualEnumerator.CreateEnumerator)
            : FindEnumerator(clrType);
        var asyncEnumerator = binding?.AsyncEnumerator is { } manualAsyncEnumerator
            ? new(manualAsyncEnumerator.CreateEnumerator)
            : FindAsyncEnumerator(clrType);
        return new(clrType, typeId, members, staticMembers, indexer, enumerator, asyncEnumerator);
    }

    internal HostRealmLayoutInfo GetOrCreateRealmLayout(JsRealm realm)
    {
        return GetOrCreateRealmLayout(realm, false);
    }

    internal HostRealmLayoutInfo GetOrCreateStaticRealmLayout(JsRealm realm)
    {
        return GetOrCreateRealmLayout(realm, true);
    }

    private HostRealmLayoutInfo GetOrCreateRealmLayout(JsRealm realm, bool isStatic)
    {
        var index = isStatic ? 1 : 0;
        lock (realmLayoutLock)
        {
            var layouts = realmLayouts[index] ??= new();
            if (layouts.TryGetValue(realm, out var info))
                return info;

            info = CreateRealmLayout(realm, isStatic ? StaticNamedMembers : NamedMembers);
            layouts.Add(realm, info);
            return info;
        }
    }

    private static HostRealmLayoutInfo CreateRealmLayout(JsRealm realm, HostNamedMemberDescriptor[] members)
    {
        var slotInfoByAtom = new Dictionary<int, SlotInfo>(members.Length);
        var lazyMethodsByAtom = new Dictionary<int, (HostNamedMemberDescriptor Member, int Slot)>();
        var slotCursor = 0;
        foreach (var member in members)
        {
            var atom = realm.Atoms.InternNoCheck(member.Name);
            var flags = member.SlotFlags;
            slotInfoByAtom.Add(atom, new(slotCursor, flags));
            slotCursor += (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
        }

        var layout = new StaticNamedPropertyLayout(realm, slotInfoByAtom, slotCursor);
        var slotTemplate = slotCursor == 0 ? Array.Empty<JsValue>() : new JsValue[slotCursor];
        foreach (var member in members)
        {
            var atom = realm.Atoms.InternNoCheck(member.Name);
            var slotInfo = slotInfoByAtom[atom];
            if (member.Kind == HostNamedMemberKind.Method)
            {
                slotTemplate[slotInfo.Slot] = JsValue.TheHole;
                lazyMethodsByAtom.Add(atom, (member, slotInfo.Slot));
                continue;
            }

            if (member.CanRead)
            {
                var getter = new JsHostFunction(realm, member.BindableGetterBody ?? InvokeHostGetter,
                    $"get {member.Name}", 0)
                {
                    UserData = member
                };
                slotTemplate[slotInfo.Slot] = JsValue.FromObject(getter);
            }

            if (member.CanWrite)
            {
                var setter = new JsHostFunction(realm, member.BindableSetterBody ?? InvokeHostSetter,
                    $"set {member.Name}", 1)
                {
                    UserData = member
                };
                var setterSlot = (slotInfo.Flags & JsShapePropertyFlags.BothAccessor) ==
                                 JsShapePropertyFlags.BothAccessor
                    ? slotInfo.AccessorSetterSlot
                    : slotInfo.Slot;
                slotTemplate[setterSlot] = JsValue.FromObject(setter);
            }
        }

        return new(layout, slotTemplate,
            lazyMethodsByAtom.Count == 0 ? null : lazyMethodsByAtom);
    }

    private static HostNamedMemberDescriptor[] CreateNamedMembers(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors |
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.NonPublicFields |
            DynamicallyAccessedMemberTypes.PublicNestedTypes |
            DynamicallyAccessedMemberTypes.NonPublicNestedTypes |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties |
            DynamicallyAccessedMemberTypes.PublicEvents |
            DynamicallyAccessedMemberTypes.NonPublicEvents)]
        Type clrType, bool isStatic,
        HostBinding? binding)
    {
        var members = new Dictionary<string, HostNamedMemberDescriptor>(StringComparer.Ordinal);
        var boundMembers = isStatic ? binding?.StaticMembers : binding?.InstanceMembers;
        if (boundMembers is not null)
            for (var i = 0; i < boundMembers.Length; i++)
            {
                var boundMember = boundMembers[i];
                if (boundMember.IsStatic != isStatic || members.ContainsKey(boundMember.Name))
                    continue;
                members.Add(boundMember.Name, HostNamedMemberDescriptor.CreateGenerated(boundMember, clrType));
            }

        var memberFlags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        var namedDataMembers = clrType.GetMembers(memberFlags)
            .Where(static x => x is FieldInfo or PropertyInfo)
            .OrderBy(static x => x.MetadataToken);

        foreach (var member in namedDataMembers)
            switch (member)
            {
                case FieldInfo field when !members.ContainsKey(field.Name):
                    members.Add(field.Name, HostNamedMemberDescriptor.CreateField(field));
                    break;
                case PropertyInfo property when property.GetIndexParameters().Length == 0 &&
                                                IsStaticProperty(property) == isStatic &&
                                                (property.CanRead || property.SetMethod is not null) &&
                                                !members.ContainsKey(property.Name):
                    members.Add(property.Name, HostNamedMemberDescriptor.CreateProperty(property));
                    break;
            }

        var methods = clrType.GetMethods(memberFlags)
            .Where(static x => x.DeclaringType != typeof(object) && ShouldExposeMethod(x))
            .GroupBy(static x => x.Name, StringComparer.Ordinal)
            .OrderBy(static x => x.Key, StringComparer.Ordinal);

        foreach (var group in methods)
        {
            if (members.ContainsKey(group.Key))
                continue;

            var overloads = group.OrderBy(static x => x.MetadataToken).ToArray();
            if (overloads.Length != 0)
                members.Add(group.Key, HostNamedMemberDescriptor.CreateMethod(group.Key, overloads));

            var genericGroups = overloads
                .Where(static x => x.IsGenericMethodDefinition)
                .GroupBy(static x => x.GetGenericArguments().Length)
                .OrderBy(static x => x.Key);

            foreach (var genericGroup in genericGroups)
            {
                var suffixedName = $"{group.Key}${genericGroup.Key}";
                if (members.ContainsKey(suffixedName))
                    continue;

                members.Add(suffixedName,
                    HostNamedMemberDescriptor.CreateMethod(suffixedName,
                        genericGroup.OrderBy(static x => x.MetadataToken).ToArray(),
                        genericGroup.Key));
            }
        }

        return members.Values.ToArray();
    }

    private static bool IsStaticProperty(PropertyInfo property)
    {
        return property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true;
    }

    private static bool ShouldExposeMethod(MethodInfo method)
    {
        if (!method.IsSpecialName)
            return true;

        return method.Name.StartsWith("get_", StringComparison.Ordinal) ||
               method.Name.StartsWith("set_", StringComparison.Ordinal) ||
               method.Name.StartsWith("op_", StringComparison.Ordinal);
    }

    private static HostIndexerDescriptor? CreateIndexer(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type clrType)
    {
        if (clrType.IsArray)
        {
            var elementType = clrType.GetElementType() ?? typeof(object);
            return new(
                (realm, target, index) =>
                {
                    var array = (Array)target;
                    if (index >= (uint)array.Length)
                        return (false, JsValue.Undefined);
                    return (true, HostValueConverter.ConvertToJsValue(realm, array.GetValue((int)index)));
                },
                (realm, target, index, value) =>
                {
                    var array = (Array)target;
                    if (index >= (uint)array.Length)
                        return false;
                    array.SetValue(HostValueConverter.ConvertFromJsValue(realm, value, elementType), (int)index);
                    return true;
                },
                (target, indices) =>
                {
                    var array = (Array)target;
                    for (var i = 0; i < array.Length; i++)
                        indices.Add((uint)i);
                });
        }

        var itemProperty = clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static x => x.MetadataToken)
            .FirstOrDefault(static x =>
            {
                var parameters = x.GetIndexParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(int) && x.GetMethod is not null;
            });

        if (itemProperty is not null)
        {
            var countAccessor = clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                    .FirstOrDefault(static x => x.Name is "Count" or "Length" &&
                                                                x.PropertyType == typeof(int) &&
                                                                x.GetIndexParameters().Length == 0 &&
                                                                x.GetMethod is not null)
                                ?? throw new InvalidOperationException(
                                    $"Host type '{clrType}' exposes an int indexer but no readable Count/Length.");
            var indexParameterType = itemProperty.GetIndexParameters()[0].ParameterType;
            return new(
                (realm, target, index) =>
                {
                    var count = (int)(countAccessor.GetValue(target) ?? 0);
                    if (index >= (uint)count)
                        return (false, JsValue.Undefined);
                    var result = itemProperty.GetValue(target, new object?[] { (int)index });
                    return (true, HostValueConverter.ConvertToJsValue(realm, result));
                },
                itemProperty.SetMethod is null
                    ? null
                    : (realm, target, index, value) =>
                    {
                        var count = (int)(countAccessor.GetValue(target) ?? 0);
                        if (index >= (uint)count)
                            return false;
                        itemProperty.SetValue(target,
                            HostValueConverter.ConvertFromJsValue(realm, value, itemProperty.PropertyType),
                            new[] { Convert.ChangeType(index, indexParameterType, CultureInfo.InvariantCulture)! });
                        return true;
                    },
                (target, indices) =>
                {
                    var count = (int)(countAccessor.GetValue(target) ?? 0);
                    for (var i = 0; i < count; i++)
                        indices.Add((uint)i);
                });
        }

        if (typeof(IList).IsAssignableFrom(clrType))
            return new(
                (realm, target, index) =>
                {
                    var list = (IList)target;
                    if (index >= (uint)list.Count)
                        return (false, JsValue.Undefined);
                    return (true, HostValueConverter.ConvertToJsValue(realm, list[(int)index]));
                },
                (realm, target, index, value) =>
                {
                    var list = (IList)target;
                    if (index >= (uint)list.Count)
                        return false;
                    list[(int)index] = HostValueConverter.ConvertFromJsValue(realm, value, typeof(object));
                    return true;
                },
                (target, indices) =>
                {
                    var list = (IList)target;
                    for (var i = 0; i < list.Count; i++)
                        indices.Add((uint)i);
                });

        return null;
    }

    private static HostEnumeratorDescriptor? FindEnumerator(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type clrType)
    {
        foreach (var method in clrType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(static x => x.Name == "GetEnumerator" &&
                                        !x.IsStatic &&
                                        x.GetParameters().Length == 0)
                     .OrderBy(static x => x.MetadataToken))
            if (TryCreateEnumeratorDescriptor(method, out var descriptor))
                return descriptor;

        return null;
    }

    private static HostAsyncEnumeratorDescriptor? FindAsyncEnumerator(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type clrType)
    {
        MethodInfo? cancellationTokenCandidate = null;
        foreach (var method in clrType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(static x => x.Name == "GetAsyncEnumerator" && !x.IsStatic)
                     .OrderBy(static x => x.MetadataToken))
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                if (TryCreateAsyncEnumeratorDescriptor(method, false, out var descriptor))
                    return descriptor;
                continue;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
                cancellationTokenCandidate ??= method;
        }

        return cancellationTokenCandidate is not null &&
               TryCreateAsyncEnumeratorDescriptor(cancellationTokenCandidate, true, out var asyncDescriptor)
            ? asyncDescriptor
            : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Reflection-based host iteration intentionally inspects public members on the returned enumerator type.")]
    private static bool TryCreateEnumeratorDescriptor(MethodInfo method, [NotNullWhen(true)] out HostEnumeratorDescriptor? descriptor)
    {
        descriptor = null;
        if (!TryCreateEnumeratorAdapterFactory(method.ReturnType, out var createAdapter))
            return false;

        descriptor = new(target =>
        {
            var enumerator = method.Invoke(target, null)
                             ?? throw new InvalidOperationException("GetEnumerator returned null.");
            return createAdapter(enumerator);
        });
        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Reflection-based host async iteration intentionally inspects public members on the returned enumerator type.")]
    private static bool TryCreateAsyncEnumeratorDescriptor(
        MethodInfo method,
        bool passCancellationToken,
        [NotNullWhen(true)] out HostAsyncEnumeratorDescriptor? descriptor)
    {
        descriptor = null;
        if (!TryCreateAsyncEnumeratorAdapterFactory(method.ReturnType, out var createAdapter))
            return false;

        descriptor = new(target =>
        {
            object?[]? args = passCancellationToken ? [CancellationToken.None] : null;
            var enumerator = method.Invoke(target, args)
                             ?? throw new InvalidOperationException("GetAsyncEnumerator returned null.");
            return createAdapter(enumerator);
        });
        return true;
    }

    private static bool TryCreateEnumeratorAdapterFactory(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.PublicProperties)]
        Type enumeratorType,
        [NotNullWhen(true)] out Func<object, HostEnumeratorAdapter>? createAdapter)
    {
        if (typeof(IEnumerator).IsAssignableFrom(enumeratorType))
        {
            createAdapter = static enumerator => HostEnumeratorAdapter.FromInterface((IEnumerator)enumerator);
            return true;
        }

        var moveNext = FindMethod(enumeratorType, "MoveNext", typeof(bool));
        var current = FindCurrentProperty(enumeratorType);
        if (moveNext is null || current is null)
        {
            createAdapter = null;
            return false;
        }

        var dispose = CreateDisposeAction(enumeratorType);
        createAdapter = enumerator => new HostEnumeratorAdapter(
            enumerator,
            state => (bool)moveNext.Invoke(state, null)!,
            state => current.GetValue(state),
            dispose);
        return true;
    }

    private static bool TryCreateAsyncEnumeratorAdapterFactory(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.PublicProperties)]
        Type enumeratorType,
        [NotNullWhen(true)] out Func<object, HostAsyncEnumeratorAdapter>? createAdapter)
    {
        var moveNextAsync = CreateMoveNextAsync(enumeratorType);
        var current = FindCurrentProperty(enumeratorType);
        if (moveNextAsync is null || current is null)
        {
            createAdapter = null;
            return false;
        }

        var disposeAsync = CreateDisposeAsync(enumeratorType);
        createAdapter = enumerator => new HostAsyncEnumeratorAdapter(
            enumerator,
            moveNextAsync,
            state => current.GetValue(state),
            disposeAsync);
        return true;
    }

    private static PropertyInfo? FindCurrentProperty(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type enumeratorType)
    {
        return enumeratorType.GetProperty("Current", BindingFlags.Instance | BindingFlags.Public, null, null,
            Type.EmptyTypes, null);
    }

    private static MethodInfo? FindMethod(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type type,
        string name,
        Type returnType)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.Name == name &&
                        !x.IsStatic &&
                        x.GetParameters().Length == 0 &&
                        x.ReturnType == returnType)
            .OrderBy(x => x.MetadataToken)
            .FirstOrDefault();
    }

    private static Action<object>? CreateDisposeAction(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type enumeratorType)
    {
        if (typeof(IDisposable).IsAssignableFrom(enumeratorType))
            return static state => ((IDisposable)state).Dispose();

        var disposeMethod = FindMethod(enumeratorType, "Dispose", typeof(void));
        return disposeMethod is null
            ? null
            : state => { _ = disposeMethod.Invoke(state, null); };
    }

    private static Func<object, ValueTask<bool>>? CreateMoveNextAsync(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type enumeratorType)
    {
        var moveNextAsync = enumeratorType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static x => x.Name == "MoveNextAsync" &&
                               !x.IsStatic &&
                               x.GetParameters().Length == 0)
            .OrderBy(static x => x.MetadataToken)
            .FirstOrDefault();
        if (moveNextAsync is null)
            return null;

        if (moveNextAsync.ReturnType == typeof(ValueTask<bool>))
            return state => (ValueTask<bool>)moveNextAsync.Invoke(state, null)!;
        if (moveNextAsync.ReturnType == typeof(Task<bool>))
            return state => new ValueTask<bool>((Task<bool>)moveNextAsync.Invoke(state, null)!);
        if (moveNextAsync.ReturnType == typeof(bool))
            return state => new ValueTask<bool>((bool)moveNextAsync.Invoke(state, null)!);

        return null;
    }

    private static Func<object, ValueTask>? CreateDisposeAsync(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type enumeratorType)
    {
        if (typeof(IAsyncDisposable).IsAssignableFrom(enumeratorType))
            return static state => ((IAsyncDisposable)state).DisposeAsync();
        if (typeof(IDisposable).IsAssignableFrom(enumeratorType))
            return static state =>
            {
                ((IDisposable)state).Dispose();
                return ValueTask.CompletedTask;
            };

        var disposeAsyncMethod = enumeratorType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static x => x.Name == "DisposeAsync" &&
                               !x.IsStatic &&
                               x.GetParameters().Length == 0)
            .OrderBy(static x => x.MetadataToken)
            .FirstOrDefault();
        if (disposeAsyncMethod is not null)
        {
            if (disposeAsyncMethod.ReturnType == typeof(ValueTask))
                return state => (ValueTask)disposeAsyncMethod.Invoke(state, null)!;
            if (disposeAsyncMethod.ReturnType == typeof(Task))
                return state => new ValueTask((Task)disposeAsyncMethod.Invoke(state, null)!);
            if (disposeAsyncMethod.ReturnType == typeof(void))
                return state =>
                {
                    _ = disposeAsyncMethod.Invoke(state, null);
                    return ValueTask.CompletedTask;
                };
        }

        var disposeMethod = FindMethod(enumeratorType, "Dispose", typeof(void));
        return disposeMethod is null
            ? null
            : state =>
            {
                _ = disposeMethod.Invoke(state, null);
                return ValueTask.CompletedTask;
            };
    }

    private static JsHostObject RequireHostObject(JsRealm realm, JsValue value, Type receiverType, string memberName)
    {
        if (value.TryGetObject(out var obj) && obj is JsHostObject host && receiverType.IsInstanceOfType(host.Data))
            return host;
        throw new InvalidOperationException(
            $"Host member '{memberName}' requires a compatible host receiver.");
    }

    internal static JsValue InvokeHostGetter(scoped in CallInfo info)
    {
        var member = (HostNamedMemberDescriptor)((JsHostFunction)info.Function).UserData!;
        if (member.IsStatic)
            return HostValueConverter.ConvertToJsValue(info.Realm, member.ReadValue(null));

        var host = RequireHostObject(info.Realm, info.ThisValue, member.ReceiverType, member.Name);
        return HostValueConverter.ConvertToJsValue(info.Realm, member.ReadValue(host.Data));
    }

    internal static JsValue InvokeHostSetter(scoped in CallInfo info)
    {
        var member = (HostNamedMemberDescriptor)((JsHostFunction)info.Function).UserData!;
        var args = info.Arguments;
        if (member.IsStatic)
        {
            member.WriteValue(info.Realm, null, args.Length != 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }

        var host = RequireHostObject(info.Realm, info.ThisValue, member.ReceiverType, member.Name);
        member.WriteValue(info.Realm, host.Data, args.Length != 0 ? args[0] : JsValue.Undefined);
        return JsValue.Undefined;
    }

    internal static JsValue InvokeHostMethod(scoped in CallInfo info)
    {
        var member = (HostNamedMemberDescriptor)((JsHostFunction)info.Function).UserData!;
        if (member.IsStatic)
            return member.InvokeMethod(info.Realm, null, info.Arguments);

        var host = RequireHostObject(info.Realm, info.ThisValue, member.ReceiverType, member.Name);
        return member.InvokeMethod(info.Realm, host.Data, info.Arguments);
    }

    internal static JsValue InvokeBoundGenericHostMethod(scoped in CallInfo info)
    {
        var data = (BoundGenericHostMethodData)((JsHostFunction)info.Function).UserData!;
        return data.Member.InvokeMethod(info.Realm, data.Target, info.Arguments);
    }
}

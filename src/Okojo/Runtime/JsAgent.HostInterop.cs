using System.Diagnostics.CodeAnalysis;

namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    private readonly object hostTypeDescriptorLock = new();
    private Dictionary<Type, HostTypeDescriptor>? hostTypeDescriptors;
    private int nextHostTypeId;

    internal HostTypeDescriptor GetOrCreateDynamicHostTypeDescriptor(Type clrType)
    {
        lock (hostTypeDescriptorLock)
        {
            var descriptors = hostTypeDescriptors ??= new();
            if (descriptors.TryGetValue(clrType, out var descriptor))
                return descriptor;

            descriptor =
                Engine.ClrAccessProvider?.CreateHostTypeDescriptor(this, clrType,
                    Interlocked.Increment(ref nextHostTypeId))
                ?? throw new InvalidOperationException(
                    "CLR access is disabled. Configure JsRuntime with options => options.AllowClrAccess().");
            descriptors.Add(clrType, descriptor);
            return descriptor;
        }
    }

    internal HostTypeDescriptor GetOrCreateHostTypeDescriptor(
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
        Type clrType)
    {
        return GetOrCreateHostTypeDescriptor(clrType, null);
    }

    internal HostTypeDescriptor GetOrCreateHostTypeDescriptor(
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
        Type clrType, HostBinding? bindingOverride)
    {
        lock (hostTypeDescriptorLock)
        {
            var descriptors = hostTypeDescriptors ??= new();
            if (descriptors.TryGetValue(clrType, out var descriptor))
                return descriptor;

            descriptor = bindingOverride is not null
                ? HostTypeDescriptor.Create(clrType, Interlocked.Increment(ref nextHostTypeId), bindingOverride)
                : Engine.ClrAccessProvider?.CreateHostTypeDescriptor(this, clrType,
                      Interlocked.Increment(ref nextHostTypeId))
                  ?? throw new InvalidOperationException(
                      "CLR access is disabled. Configure JsRuntime with options => options.AllowClrAccess().");
            descriptors.Add(clrType, descriptor);
            return descriptor;
        }
    }
}

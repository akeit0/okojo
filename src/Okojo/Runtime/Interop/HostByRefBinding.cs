namespace Okojo.Runtime.Interop;

internal readonly record struct HostByRefBinding(int ArgumentIndex, IClrByRefPlaceholder Placeholder);
